using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
        private int _currentFileEndLine = -1;
        private int _maxLineSeen = -1;

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
            bool stoppedEarly = false;

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
                                stoppedEarly = true;
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
                        stoppedEarly = true;
                        break;
                    }

                    RunStateStore.SetDone(_state, project.id, i, true);
                    SaveState();
                    RaiseStepStatus(i, StepRunStatus.Done);
                    if (i == project.steps.Count - 1)
                        lastStepCompleted = true;
                }

                // A stopped step leaves its own reason in the status line; don't bury it.
                if (!_abortRequested && !stoppedEarly)
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
            _currentFileEndLine = FindProgramEndLine(fullPath);
            _maxLineSeen = -1;
            RaiseFileProgress(0, _currentFileTotalLines);

            if (!ops.LoadAndRunFile(fullPath, SetStatus, () => _abortRequested, WaitForCycleFinish,
                    () => { _cycleStartedEvent.Reset(); _cycleFinishedEvent.Reset(); },
                    () => _cycleStartedEvent.WaitOne(0),
                    () => _cycleFinishedEvent.WaitOne(0)))
                return false;

            if (_abortRequested) return false;

            if (!ConfirmProgramReachedEnd(fullPath)) return false;

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

        // Also records the furthest line reached: UCCNC may rewind to line 0 at M30, so
        // the line read at completion can't be trusted to show how far the file got.
        private void ReportLiveFileProgress()
        {
            int current;
            try { current = _host.UC.Getcurrentgcodelinenumber(); }
            catch { return; }
            if (current < 0) return;
            if (current > _maxLineSeen) _maxLineSeen = current;
            if (FileProgressChanged != null) FileProgressChanged(current, _currentFileTotalLines);
        }

        // UCCNC LED numbers: 54 = cycle running, 229 = M0 stop active. The M1 LED (230)
        // is not used: its docs don't say whether it means "stopped at M1" or "optional
        // stop enabled", and the latter would block completion forever.
        private const int LedCycleRunning = 54;
        private const int LedM0Stop = 229;

        // A single dark read of the cycle LED is not the end of the program: an AXBB-E
        // link stall or a stale screen refresh can drop it for a poll while the file is
        // still running. Without the finish event, the cycle must look idle (LED off
        // and no motion) continuously for this long before the file counts as done.
        private const int CycleIdleConfirmMs = 2000;
        private const int CyclePollMs = 200;

        private void WaitForCycleFinish()
        {
            var idle = new Stopwatch();
            bool announcedStop = false;

            while (true)
            {
                if (_abortRequested) return;
                ReportLiveFileProgress();

                if (_host.UC.GetLED(LedM0Stop))
                {
                    // Paused inside this file. Any finish raised by the pause must not
                    // complete the step once Cycle Start resumes it.
                    _cycleFinishedEvent.Reset();
                    idle.Reset();
                    if (!announcedStop)
                    {
                        announcedStop = true;
                        SetStatus("Program stop (M0) at line " + CurrentLineText() +
                            " - press Cycle Start to continue this file.");
                    }
                }
                else
                {
                    announcedStop = false;

                    if (_cycleFinishedEvent.WaitOne(0))
                    {
                        SetStatus("Cycle finished (finish event, line " + CurrentLineText() + ").");
                        return;
                    }

                    if (_host.UC.GetLED(LedCycleRunning) || _host.UC.IsMoving())
                        idle.Reset();
                    else if (!idle.IsRunning)
                        idle.Start();

                    if (idle.IsRunning && idle.ElapsedMilliseconds >= CycleIdleConfirmMs)
                    {
                        SetStatus("Cycle finished (cycle LED idle " + (CycleIdleConfirmMs / 1000.0).ToString("0.#") +
                            "s, no finish event, line " + CurrentLineText() + ").");
                        return;
                    }
                }

                _cycleFinishedEvent.WaitOne(CyclePollMs);
            }
        }

        private string CurrentLineText()
        {
            try { return _host.UC.Getcurrentgcodelinenumber().ToString(); }
            catch { return "?"; }
        }

        // The last few blocks (retract, M5, M30) can execute between progress polls, and
        // UCCNC's line numbering may be off by one from the file's, so "reached the end"
        // allows this much slack. A file abandoned mid-run is far outside it.
        private const int EndOfFileToleranceLines = 5;

        private static readonly Regex ProgramEndWord =
            new Regex(@"(?<![A-Z])M0*(2|30)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ParenComment = new Regex(@"\([^)]*\)", RegexOptions.CultureInvariant);

        // Whatever signal ended the cycle (finish event or confirmed idle), the step only
        // counts as done if the program got to its end. Otherwise hold the workflow and let
        // the operator decide; the default answer stops rather than advancing.
        private bool ConfirmProgramReachedEnd(string fullPath)
        {
            // Demo mode simulates a missing file; nothing ran, so there is nothing to check.
            if (!File.Exists(fullPath)) return true;

            ReportLiveFileProgress();
            int reached = _maxLineSeen;
            int end = _currentFileEndLine;

            if (end >= 0 && reached >= end - EndOfFileToleranceLines)
                return true;

            string reachedText = reached >= 0 ? reached.ToString() : "unknown";
            string endText = end >= 0 ? end.ToString() : "unknown";
            string fileName = Path.GetFileName(fullPath);

            SetStatus("HOLD - " + fileName + " reported finished at line " + reachedText +
                ", but the program ends at line " + endText + ". Waiting for operator.");

            DialogResult dr = DialogResult.No;
            InvokeUi(() =>
            {
                dr = MessageBox.Show(_owner,
                    "UCCNC reported that this file finished, but it did not reach the end of the program.\n\n" +
                    "File: " + fileName + "\n" +
                    "Furthest line reached: " + reachedText + "\n" +
                    "Program ends at line: " + endText + "\n\n" +
                    "If the machine is still running this file, let it finish or stop it before answering.\n\n" +
                    "Did this file really run to completion?\n\n" +
                    "Yes = continue with the next operation\n" +
                    "No = stop the workflow here (recommended)",
                    "Confirm Program Finished", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
            });

            if (dr == DialogResult.Yes)
            {
                SetStatus("Operator confirmed " + fileName + " finished (reached line " + reachedText +
                    " of program end " + endText + ").");
                return true;
            }

            SetStatus("Stopped - " + fileName + " did not reach the end of the program (line " + reachedText +
                " of " + endText + ").");
            return false;
        }

        // Index (0-based, matching UCCNC's line numbers) of the block that ends the program:
        // the first M2/M30, or the last line with code if the file has neither. Blocks after
        // M30 (e.g. subprograms) never run straight through, so they don't count. Returns -1
        // when the file can't be read, which makes completion require operator confirmation.
        private static int FindProgramEndLine(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return -1;
                int index = 0;
                int lastCode = -1;
                using (var reader = new StreamReader(path))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string code = StripComments(line).Trim();
                        if (code.Length > 0 && code != "%")
                        {
                            lastCode = index;
                            if (ProgramEndWord.IsMatch(code)) return index;
                        }
                        index++;
                    }
                }
                return lastCode;
            }
            catch { return -1; }
        }

        private static string StripComments(string line)
        {
            int semi = line.IndexOf(';');
            if (semi >= 0) line = line.Substring(0, semi);
            return ParenComment.Replace(line, "");
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
