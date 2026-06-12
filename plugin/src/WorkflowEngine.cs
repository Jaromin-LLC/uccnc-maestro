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

        private readonly UCCNCplugin _host;
        private readonly Form _owner;

        private Thread _runThread;
        private volatile bool _abortRequested;
        private volatile bool _operatorConfirmed;
        private ManualResetEvent _cycleFinishedEvent = new ManualResetEvent(false);
        private ManualResetEvent _confirmEvent = new ManualResetEvent(false);

        private ProjectsDocument _document;
        private ToolLibraryDocument _toolLibrary;
        private ProjectRunState _state;
        private WorkflowProject _activeProject;
        private int _activeStepIndex = -1;
        private bool _running;

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
            if (_running) return;
            if (project == null || project.steps == null || project.steps.Count == 0) return;
            int end = project.steps.Count - 1;
            StartThread(() => RunSteps(project, startIndex, end, false));
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
            }
        }

        private bool ExecuteStep(WorkflowProject project, int stepIndex, MachineOps ops)
        {
            _activeStepIndex = stepIndex;
            WorkflowStep step = project.steps[stepIndex];
            step.EnsureDefaultOps();
            var stepTimer = Stopwatch.StartNew();

            RaiseStepStatus(stepIndex, StepRunStatus.Running);
            SetStatus("Running step " + (stepIndex + 1) + ": " + step.label);

            if (step.IsGate)
            {
                if (!WaitForOperatorConfirm(step, stepIndex, true)) return false;
                RecordStepRuntime(project.id, stepIndex, stepTimer);
                return true;
            }

            foreach (string opId in step.preOps)
            {
                if (_abortRequested) return false;

                if (opId == AutoOpIds.ToolPrompt)
                {
                    if (!WaitForOperatorConfirm(step, stepIndex, false)) return false;
                    var tool = GetToolForStep(step);
                    if (tool != null)
                        ops.SetCurrentTool(tool.num, SetStatus);
                    continue;
                }

                if (!ops.ExecuteAutoOp(opId, step, SetStatus, () => _abortRequested))
                    return false;
            }

            string fullPath = (step.file ?? "").Trim();

            _cycleFinishedEvent.Reset();
            if (!ops.LoadAndRunFile(fullPath, SetStatus, () => _abortRequested, WaitForCycleFinish,
                    () => _cycleFinishedEvent.Reset(), () => _cycleFinishedEvent.WaitOne(0)))
                return false;

            if (_abortRequested) return false;

            foreach (string opId in step.postOps)
            {
                if (_abortRequested) return false;
                if (!ops.ExecuteAutoOp(opId, step, SetStatus, () => _abortRequested))
                    return false;
            }

            SetStatus("Step complete: " + step.label);
            RecordStepRuntime(project.id, stepIndex, stepTimer);
            return true;
        }

        private void RecordStepRuntime(string projectId, int stepIndex, Stopwatch timer)
        {
            int seconds = (int)Math.Max(1, Math.Round(timer.Elapsed.TotalSeconds));
            RunStateStore.SetLastRunSeconds(_state, projectId, stepIndex, seconds);
            SaveState();
        }

        private void WaitForCycleFinish()
        {
            while (!_cycleFinishedEvent.WaitOne(200))
            {
                if (_abortRequested) return;
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
