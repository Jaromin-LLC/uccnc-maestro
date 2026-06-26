using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Plugins
{
    public class WorkflowEngine
    {
        public event Action<string> StatusChanged;
        public event Action<int, StepRunStatus> StepStatusChanged;
        public event Action<bool> RunningChanged;
        public event Action<WorkflowStep, int, bool> PromptRequired;
        public event Action RunFinished;
        public event Action<WorkflowProject> ProjectCompleted;

        // Raised when a step's measured machine work begins (after any tool change is
        // confirmed), so the UI can start the remaining-time countdown from that point.
        public event Action<int> StepWorkStarted;

        // Raised with (currentLine, totalLines) while a g-code file is running so the UI
        // can show real file progress. totalLines is 0 when unknown (e.g. demo mode).
        public event Action<int, int> FileProgressChanged;

        private readonly UCCNCplugin _host;
        private readonly Form _owner;

        private Thread _runThread;
        private volatile bool _abortRequested;
        private volatile bool _operatorConfirmed;
        private ManualResetEvent _cycleStartedEvent = new ManualResetEvent(false);
        private ManualResetEvent _cycleFinishedEvent = new ManualResetEvent(false);
        private ManualResetEvent _confirmEvent = new ManualResetEvent(false);

        private ProjectsDocument _document;
        private ToolLibraryDocument _toolLibrary;
        private ProjectRunState _state;
        private WorkflowProject _activeProject;
        private int _activeStepIndex = -1;
        private bool _running;
        private int _currentFileTotalLines;

        public WorkflowEngine(UCCNCplugin host, Form owner)
        {
            _host = host;
            _owner = owner;
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public WorkflowProject ActiveProject
        {
            get { return _activeProject; }
        }

        public int ActiveStepIndex
        {
            get { return _activeStepIndex; }
        }

        public ProjectsDocument Document
        {
            get { return _document; }
        }

        public ToolLibraryDocument ToolLibrary
        {
            get { return _toolLibrary; }
        }

        public ToolInfo GetToolForStep(WorkflowStep step)
        {
            if (step == null || step.toolId <= 0) return null;
            return JsonStore.FindTool(_toolLibrary, step.toolId);
        }

        public ProjectRunState State
        {
            get { return _state; }
        }

        public void LoadData()
        {
            MaestroPaths.EnsureDirectories();
            _document = JsonStore.LoadProjects(MaestroPaths.ProjectsFile);
            _toolLibrary = JsonStore.LoadTools(MaestroPaths.ToolsFile);
            _state = JsonStore.LoadState(MaestroPaths.StateFile);
        }

        public void ReloadDocument()
        {
            _document = JsonStore.LoadProjects(MaestroPaths.ProjectsFile);
            _toolLibrary = JsonStore.LoadTools(MaestroPaths.ToolsFile);
        }

        public bool TestMode
        {
            get { return _document != null && _document.settings != null && _document.settings.testMode; }
        }

        public void SetTestMode(bool on)
        {
            if (_document == null || _document.settings == null) return;
            _document.settings.testMode = on;
            try { JsonStore.SaveProjects(MaestroPaths.ProjectsFile, _document); } catch { }
        }

        public void SaveState()
        {
            JsonStore.SaveState(MaestroPaths.StateFile, _state);
        }

        public void NotifyCycleStarted()
        {
            _cycleStartedEvent.Set();
        }

        public void NotifyCycleFinished()
        {
            _cycleFinishedEvent.Set();
        }

        public void RequestAbort()
        {
            _abortRequested = true;
            _confirmEvent.Set();
            try { _host.UC.Stop(); } catch { }
            SetStatus("Abort requested...");
        }

        public void ConfirmPrompt()
        {
            _operatorConfirmed = true;
            _confirmEvent.Set();
        }

        public void CancelPrompt()
        {
            _operatorConfirmed = false;
            _confirmEvent.Set();
        }

        public void RunStep(WorkflowProject project, int stepIndex, bool allowOutOfOrder)
        {
            if (_running) return;
            StartThread(() => RunSteps(project, stepIndex, stepIndex, allowOutOfOrder));
        }

        public void RunAll(WorkflowProject project, int startIndex)
        {
            RunAll(project, startIndex, false);
        }

        // allowOutOfOrder lets the operator override the normal sequence and start a
        // full run at any step (recovery when the saved state is out of sync with the
        // machine). Normal RUN ALL passes false so the out-of-order guard stays active.
        public void RunAll(WorkflowProject project, int startIndex, bool allowOutOfOrder)
        {
            if (_running) return;
            if (project == null || project.steps == null || project.steps.Count == 0) return;
            int end = project.steps.Count - 1;
            if (startIndex < 0) startIndex = 0;
            if (startIndex > end) return;
            StartThread(() => RunSteps(project, startIndex, end, allowOutOfOrder));
        }

        public void ResetProject(WorkflowProject project)
        {
            if (project == null) return;
            RunStateStore.ClearProject(_state, project.id, project.steps.Count);
            SaveState();
            for (int i = 0; i < project.steps.Count; i++)
                RaiseStepStatus(i, StepRunStatus.Pending);
            SetStatus("Project reset: " + project.name);
        }

        private void StartThread(ThreadStart action)
        {
            _abortRequested = false;
            _runThread = new Thread(action);
            _runThread.IsBackground = true;
            _runThread.Start();
        }

        private void RunSteps(WorkflowProject project, int startIndex, int endIndex, bool allowOutOfOrder)
        {
            SetRunning(true);
            _activeProject = project;
            _state.lastProjectId = project.id;
            SaveState();

            bool lastStepCompleted = false;

            try
            {
                var ops = new MachineOps(_host.UC, _document.settings, project, _owner, InvokeUi);

                if (!ops.Preflight(SetStatus))
                {
                    SetRunning(false);
                    return;
                }

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (_abortRequested) break;

                    if (!allowOutOfOrder)
                    {
                        int first = RunStateStore.FirstNotDone(_state, project.id, project.steps.Count);
                        if (first >= 0 && i > first)
                        {
                            DialogResult dr = DialogResult.No;
                            InvokeUi(() =>
                            {
                                dr = MessageBox.Show(_owner,
                                    "Step " + (i + 1) + " is out of order.\nThe next expected step is " + (first + 1) + ".\n\nRun anyway?",
                                    "Out of Order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            });
                            if (dr != DialogResult.Yes)
                            {
                                SetStatus("Cancelled - out of order step.");
                                break;
                            }
                        }
                    }

                    if (RunStateStore.IsDone(_state, project.id, i))
                    {
                        DialogResult dr = DialogResult.No;
                        InvokeUi(() =>
                        {
                            dr = MessageBox.Show(_owner,
                                "Step " + (i + 1) + " is already marked complete.\n\nRun it again?",
                                "Already Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        });
                        if (dr != DialogResult.Yes) continue;
                    }

                    if (!ExecuteStep(project, i, ops))
                    {
                        RaiseStepStatus(i, StepRunStatus.Stopped);
                        break;
                    }

                    RunStateStore.SetDone(_state, project.id, i, true);
                    SaveState();
                    RaiseStepStatus(i, StepRunStatus.Done);
                    if (i == project.steps.Count - 1)
                        lastStepCompleted = true;
                }

                if (!_abortRequested)
                    SetStatus("Run finished.");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _activeStepIndex = -1;
                SetRunning(false);
                if (RunFinished != null) RunFinished();
                if (lastStepCompleted && !_abortRequested && ProjectCompleted != null)
                    InvokeUi(() => ProjectCompleted(project));
            }
        }

        private bool ExecuteStep(WorkflowProject project, int stepIndex, MachineOps ops)
        {
            _activeStepIndex = stepIndex;
            WorkflowStep step = project.steps[stepIndex];
            step.EnsureOpsNotNull();

            RaiseStepStatus(stepIndex, StepRunStatus.Running);
            SetStatus("Running step " + (stepIndex + 1) + ": " + step.label);

            if (step.IsGate)
            {
                // Gate pre-ops run before the prompt so the machine can clear the work
                // area (e.g. Park) ahead of the operator action; post-ops run after the
                // operator confirms. There is no g-code file or work-time to record.
                foreach (WorkflowOp op in step.preOps)
                {
                    if (_abortRequested) return false;
                    if (op == null || string.IsNullOrEmpty(op.id)) continue;
                    if (!ops.ExecuteAutoOp(op, step, SetStatus, () => _abortRequested))
                        return false;
                }

                if (!WaitForOperatorConfirm(step, stepIndex, true)) return false;

                foreach (WorkflowOp op in step.postOps)
                {
                    if (_abortRequested) return false;
                    if (op == null || string.IsNullOrEmpty(op.id)) continue;
                    if (!ops.ExecuteAutoOp(op, step, SetStatus, () => _abortRequested))
                        return false;
                }

                return true;
            }

            ops.SetActiveProbeTool(GetToolForStep(step));

            // The remaining-time estimate measures only the unattended machine work: it
            // starts when the tool change is confirmed (or immediately, if the step has no
            // tool prompt) and ends after the last post-op. Operator time spent at the
            // tool-change prompt is excluded so the recorded estimate is repeatable.
            bool hasToolPrompt = step.preOps.Exists(o => o != null && o.id == AutoOpIds.ToolPrompt);
            var workTimer = new Stopwatch();
            bool workStarted = false;
            Action beginWork = delegate
            {
                if (workStarted) return;
                workStarted = true;
                workTimer.Start();
                if (StepWorkStarted != null) StepWorkStarted(stepIndex);
            };

            if (!hasToolPrompt) beginWork();

            foreach (WorkflowOp op in step.preOps)
            {
                if (_abortRequested) return false;
                if (op == null || string.IsNullOrEmpty(op.id)) continue;

                if (op.id == AutoOpIds.ToolPrompt)
                {
                    if (!WaitForOperatorConfirm(step, stepIndex, false)) return false;
                    var tool = GetToolForStep(step);
                    if (tool != null)
                        ops.SetCurrentTool(tool.id, SetStatus);
                    beginWork();
                    continue;
                }

                if (!ops.ExecuteAutoOp(op, step, SetStatus, () => _abortRequested))
                    return false;
            }

            string fullPath = (step.file ?? "").Trim();

            _currentFileTotalLines = CountFileLines(fullPath);
            RaiseFileProgress(0, _currentFileTotalLines);

            if (!ops.LoadAndRunFile(fullPath, SetStatus, () => _abortRequested, WaitForCycleFinish,
                    () => { _cycleStartedEvent.Reset(); _cycleFinishedEvent.Reset(); },
                    () => _cycleStartedEvent.WaitOne(0),
                    () => _cycleFinishedEvent.WaitOne(0)))
                return false;

            if (_abortRequested) return false;

            RaiseFileProgress(_currentFileTotalLines, _currentFileTotalLines);

            foreach (WorkflowOp op in step.postOps)
            {
                if (_abortRequested) return false;
                if (op == null || string.IsNullOrEmpty(op.id)) continue;
                if (!ops.ExecuteAutoOp(op, step, SetStatus, () => _abortRequested))
                    return false;
            }

            SetStatus("Step complete: " + step.label);
            if (workStarted) RecordStepRuntime(project.id, stepIndex, workTimer);
            return true;
        }

        private void RecordStepRuntime(string projectId, int stepIndex, Stopwatch timer)
        {
            int seconds = (int)Math.Max(1, Math.Round(timer.Elapsed.TotalSeconds));
            RunStateStore.SetLastRunSeconds(_state, projectId, stepIndex, seconds);
            SaveState();
        }

        // Counts physical lines in the g-code file so the UI can show file progress as a
        // percentage. Returns 0 (unknown) when the file is missing or unreadable, e.g. in
        // demo mode, so the UI falls back to a time-based bar.
        private int CountFileLines(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                int count = 0;
                using (var reader = new StreamReader(path))
                    while (reader.ReadLine() != null) count++;
                return count;
            }
            catch { return 0; }
        }

        private void RaiseFileProgress(int current, int total)
        {
            if (FileProgressChanged != null) FileProgressChanged(current, total);
        }

        private void ReportLiveFileProgress()
        {
            if (FileProgressChanged == null) return;
            int current;
            try { current = _host.UC.Getcurrentgcodelinenumber(); }
            catch { return; }
            if (current < 0) return;
            FileProgressChanged(current, _currentFileTotalLines);
        }

        private void WaitForCycleFinish()
        {
            while (!_cycleFinishedEvent.WaitOne(200))
            {
                if (_abortRequested) return;
                ReportLiveFileProgress();
                if (!_host.UC.GetLED(54)) break;
            }
        }

        private bool WaitForOperatorConfirm(WorkflowStep step, int stepIndex, bool isGateOnly)
        {
            _operatorConfirmed = false;
            _confirmEvent.Reset();

            if (PromptRequired != null)
                InvokeUi(() => PromptRequired(step, stepIndex, isGateOnly));

            while (!_confirmEvent.WaitOne(200))
            {
                if (_abortRequested)
                {
                    return false;
                }
            }

            if (!_operatorConfirmed)
            {
                SetStatus("Cancelled by operator.");
                return false;
            }

            return true;
        }

        private void SetStatus(string msg)
        {
            try { _host.UC.AddStatusmessage(msg); } catch { }
            if (StatusChanged != null) StatusChanged(msg);
        }

        private void SetRunning(bool running)
        {
            _running = running;
            if (RunningChanged != null) RunningChanged(running);
        }

        private void RaiseStepStatus(int index, StepRunStatus status)
        {
            if (StepStatusChanged != null) StepStatusChanged(index, status);
        }

        private void InvokeUi(Action action)
        {
            if (_owner == null || _owner.IsDisposed)
            {
                action();
                return;
            }

            if (_owner.InvokeRequired)
                _owner.Invoke(action);
            else
                action();
        }
    }
}
