using System;
using System.Collections.Generic;
using System.Threading;

namespace Plugins.Companion
{
    /// <summary>
    /// Self-contained, in-memory machine + workflow simulation. No UCCNC dependency, so it
    /// powers the standalone test host and lets the whole API + PWA be exercised on
    /// localhost. Jog/zero/home/park move simulated DROs; running a project plays through
    /// its steps with simulated g-code progress and operator prompts.
    /// </summary>
    public class SimulatedMaestroController : IMaestroController
    {
        public event Action SnapshotChanged;

        private readonly CompanionSettings _settings;
        private readonly object _lock = new object();

        private ProjectsDocument _projects;
        private ToolLibraryDocument _tools;

        // Machine state.
        private readonly bool[] _homed = { true, true, true, true };
        private readonly double[] _work = { 0, 0, 0, 0 };       // X Y Z A work coords
        private readonly double[] _machine = { 0, 0, 0, 0 };     // machine coords
        private bool _feedHold;
        private bool _estopped;
        private double _spindleRpm;
        private bool _spindleOn;
        private int _gcodeLine;

        // Jog state.
        private string _jogAxis;
        private int _jogDir;
        private double _jogFeed = 1500;
        private bool _jogContinuous;

        // Workflow state.
        private WorkflowProject _activeProject;
        private string _statusText = "Ready";
        private volatile bool _running;
        private int _activeStepIndex = -1;
        private string[] _stepStatus = new string[0];   // pending/running/done/stopped
        private int _fileCurrentLine;
        private int _fileTotalLines;
        private int _estimateSeconds;
        private int _elapsedSeconds;
        private bool _promptWaiting;
        private string _promptText = "";
        private bool _promptIsGateOnly;
        private Thread _runThread;
        private volatile bool _abort;
        private ManualResetEvent _confirm = new ManualResetEvent(false);
        private volatile bool _confirmed;

        private const int SimStepLines = 500;
        private const int SimStepSeconds = 15;

        public SimulatedMaestroController(CompanionSettings settings, ProjectsDocument projects, ToolLibraryDocument tools)
        {
            _settings = settings;
            _projects = projects ?? new ProjectsDocument();
            _tools = tools ?? new ToolLibraryDocument();
            if (_projects.projects.Count > 0)
                SelectProjectInternal(_projects.projects[0].id);

            var ticker = new Thread(TickLoop) { IsBackground = true, Name = "SimTicker" };
            ticker.Start();
        }

        public string MachineId { get { return _settings.machineId; } }
        public string MachineName { get { return _settings.machineName; } }
        public string CameraUrl { get { return _settings.cameraUrl ?? ""; } }

        public ProjectsDocument GetProjects() { return _projects; }
        public ToolLibraryDocument GetTools() { return _tools; }

        // ----- Ticker: drives continuous jog motion + fires periodic updates -----

        private void TickLoop()
        {
            const int dtMs = 80;
            while (true)
            {
                bool moved = false;
                lock (_lock)
                {
                    if (_jogContinuous && !string.IsNullOrEmpty(_jogAxis))
                    {
                        double delta = _jogDir * (_jogFeed / 60.0) * (dtMs / 1000.0);
                        ApplyAxisDelta(_jogAxis, delta);
                        moved = true;
                    }
                }
                if (moved && SnapshotChanged != null) SnapshotChanged();
                Thread.Sleep(dtMs);
            }
        }

        private int AxisIndex(string axis)
        {
            switch ((axis ?? "").ToUpperInvariant())
            {
                case "X": return 0;
                case "Y": return 1;
                case "Z": return 2;
                case "A": return 3;
                default: return -1;
            }
        }

        private void ApplyAxisDelta(string axis, double delta)
        {
            int i = AxisIndex(axis);
            if (i < 0) return;
            _work[i] += delta;
            _machine[i] += delta;
        }

        // ----- Jog -----

        public CommandResult Jog(string axis, int dir, string mode, double step, double feed)
        {
            if (AxisIndex(axis) < 0) return CommandResult.BadRequest("Unknown axis: " + axis);
            if (_running) return CommandResult.Conflict("Cannot jog while a job is running.");

            lock (_lock)
            {
                _jogFeed = feed > 0 ? feed : _jogFeed;
                if (mode == "cont")
                {
                    _jogAxis = axis.ToUpperInvariant();
                    _jogDir = dir >= 0 ? 1 : -1;
                    _jogContinuous = true;
                }
                else
                {
                    double d = (dir >= 0 ? 1 : -1) * Math.Abs(step);
                    ApplyAxisDelta(axis, d);
                    _jogContinuous = false;
                }
            }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult JogStop()
        {
            lock (_lock) { _jogContinuous = false; _jogAxis = null; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Spindle(bool on, double rpm)
        {
            if (_running) return CommandResult.Conflict("Cannot control spindle while a job is running.");
            lock (_lock)
            {
                _spindleOn = on && rpm > 0;
                _spindleRpm = _spindleOn ? rpm : 0;
                _statusText = _spindleOn ? ("Spindle ON at " + (int)rpm + " RPM.") : "Spindle OFF.";
            }
            Changed();
            return CommandResult.Ok();
        }

        // ----- Machine commands -----

        public CommandResult Zero(string axis)
        {
            lock (_lock)
            {
                if (axis == "all")
                {
                    for (int i = 0; i < 4; i++) _work[i] = 0;
                    _statusText = "Zeroed all axes.";
                }
                else
                {
                    int i = AxisIndex(axis);
                    if (i < 0) return CommandResult.BadRequest("Unknown axis: " + axis);
                    _work[i] = 0;
                    _statusText = "Zeroed " + axis.ToUpperInvariant() + ".";
                }
            }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Home(string axis)
        {
            lock (_lock)
            {
                if (axis == "all")
                {
                    for (int i = 0; i < 4; i++) { _homed[i] = true; _machine[i] = 0; }
                    _statusText = "Homed all axes.";
                }
                else
                {
                    int i = AxisIndex(axis);
                    if (i < 0) return CommandResult.BadRequest("Unknown axis: " + axis);
                    _homed[i] = true; _machine[i] = 0;
                    _statusText = "Homed " + axis.ToUpperInvariant() + ".";
                }
                _estopped = false;
            }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult GotoZero()
        {
            lock (_lock) { _work[0] = 0; _work[1] = 0; _statusText = "Moved to work zero."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Park(string type)
        {
            lock (_lock) { _statusText = "Parked (" + type + ")."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult AutoZero()
        {
            lock (_lock) { _work[2] = 0; _statusText = "Auto-zero complete (Z=0)."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult FeedHold()
        {
            lock (_lock) { _feedHold = true; _statusText = "Feed hold."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Resume()
        {
            lock (_lock) { _feedHold = false; _statusText = "Resumed."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Stop()
        {
            _abort = true;
            _confirm.Set();
            lock (_lock) { _feedHold = false; _jogContinuous = false; _spindleOn = false; _spindleRpm = 0; _statusText = "Stopped."; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult EStop()
        {
            _abort = true;
            _confirm.Set();
            lock (_lock) { _estopped = true; _jogContinuous = false; _feedHold = false; _spindleOn = false; _spindleRpm = 0; _statusText = "E-STOP."; }
            Changed();
            return CommandResult.Ok();
        }

        // ----- Maestro workflow -----

        public CommandResult SelectProject(string projectId)
        {
            if (_running) return CommandResult.Conflict("Cannot change project while running.");
            if (!SelectProjectInternal(projectId)) return CommandResult.BadRequest("Unknown project: " + projectId);
            Changed();
            return CommandResult.Ok();
        }

        private bool SelectProjectInternal(string projectId)
        {
            WorkflowProject found = null;
            foreach (var p in _projects.projects)
                if (p.id == projectId) { found = p; break; }
            if (found == null) return false;

            lock (_lock)
            {
                _activeProject = found;
                _stepStatus = new string[found.steps.Count];
                for (int i = 0; i < _stepStatus.Length; i++) _stepStatus[i] = "pending";
                _activeStepIndex = -1;
                _fileCurrentLine = 0;
                _fileTotalLines = 0;
                _statusText = "Selected " + found.name;
            }
            return true;
        }

        public CommandResult RunAll(int fromIndex)
        {
            if (_running) return CommandResult.Conflict("Already running.");
            if (_activeProject == null) return CommandResult.BadRequest("No project selected.");
            int start = fromIndex >= 0 ? fromIndex : FirstNotDone();
            if (start < 0) start = 0;
            StartRun(start, _activeProject.steps.Count - 1);
            return CommandResult.Ok();
        }

        public CommandResult RunStep(int index)
        {
            if (_running) return CommandResult.Conflict("Already running.");
            if (_activeProject == null) return CommandResult.BadRequest("No project selected.");
            if (index < 0 || index >= _activeProject.steps.Count) return CommandResult.BadRequest("Step out of range.");
            StartRun(index, index);
            return CommandResult.Ok();
        }

        public CommandResult ResetProject()
        {
            if (_running) return CommandResult.Conflict("Cannot reset while running.");
            lock (_lock)
            {
                for (int i = 0; i < _stepStatus.Length; i++) _stepStatus[i] = "pending";
                _activeStepIndex = -1;
                _statusText = "Project reset.";
            }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Abort()
        {
            _abort = true;
            _confirm.Set();
            return CommandResult.Ok();
        }

        public CommandResult ConfirmPrompt()
        {
            _confirmed = true;
            _confirm.Set();
            return CommandResult.Ok();
        }

        public CommandResult CancelPrompt()
        {
            _confirmed = false;
            _confirm.Set();
            return CommandResult.Ok();
        }

        private int FirstNotDone()
        {
            for (int i = 0; i < _stepStatus.Length; i++)
                if (_stepStatus[i] != "done") return i;
            return -1;
        }

        private void StartRun(int startIndex, int endIndex)
        {
            _abort = false;
            _running = true;
            _runThread = new Thread(() => RunSteps(startIndex, endIndex)) { IsBackground = true };
            _runThread.Start();
            Changed();
        }

        private void RunSteps(int startIndex, int endIndex)
        {
            try
            {
                var project = _activeProject;
                for (int i = startIndex; i <= endIndex && !_abort; i++)
                {
                    WorkflowStep step = project.steps[i];
                    lock (_lock)
                    {
                        _activeStepIndex = i;
                        _stepStatus[i] = "running";
                        _statusText = "Running step " + (i + 1) + ": " + step.label;
                        _fileTotalLines = step.IsOp ? SimStepLines : 0;
                        _fileCurrentLine = 0;
                        _estimateSeconds = step.IsOp ? SimStepSeconds : 0;
                        _elapsedSeconds = 0;
                    }
                    Changed();

                    bool needsPrompt = step.IsGate || HasToolPrompt(step);
                    if (needsPrompt)
                    {
                        string text = step.IsGate
                            ? (string.IsNullOrEmpty(step.DisplayInstructions) ? "Operator action required." : step.DisplayInstructions)
                            : "Install tool and confirm to continue.";
                        if (!WaitForConfirm(text, step.IsGate)) { MarkStopped(i); return; }
                    }

                    if (step.IsOp)
                    {
                        if (!SimulateCut()) { MarkStopped(i); return; }
                    }

                    lock (_lock) { _stepStatus[i] = "done"; }
                    Changed();
                }

                lock (_lock)
                {
                    _statusText = _abort ? "Aborted." : "Run finished.";
                    _activeStepIndex = -1;
                }
            }
            catch (Exception ex)
            {
                lock (_lock) { _statusText = "Error: " + ex.Message; }
            }
            finally
            {
                _running = false;
                lock (_lock) { _promptWaiting = false; }
                Changed();
            }
        }

        private static bool HasToolPrompt(WorkflowStep step)
        {
            if (step == null || step.preOps == null) return false;
            foreach (var op in step.preOps)
                if (op != null && op.id == AutoOpIds.ToolPrompt) return true;
            return false;
        }

        private bool WaitForConfirm(string text, bool gateOnly)
        {
            _confirmed = false;
            _confirm.Reset();
            lock (_lock)
            {
                _promptWaiting = true;
                _promptText = text;
                _promptIsGateOnly = gateOnly;
                _statusText = "Waiting for operator...";
            }
            Changed();

            while (!_confirm.WaitOne(150))
                if (_abort) break;

            lock (_lock) { _promptWaiting = false; _promptText = ""; }
            Changed();
            return !_abort && _confirmed;
        }

        private bool SimulateCut()
        {
            lock (_lock) { _spindleRpm = 18000; _spindleOn = true; }
            int totalMs = SimStepSeconds * 1000;
            int elapsedMs = 0;
            const int dt = 100;
            while (elapsedMs < totalMs)
            {
                if (_abort) { lock (_lock) { _spindleRpm = 0; _spindleOn = false; } return false; }
                if (!_feedHold)
                {
                    elapsedMs += dt;
                    lock (_lock)
                    {
                        double frac = (double)elapsedMs / totalMs;
                        _fileCurrentLine = (int)(frac * _fileTotalLines);
                        _elapsedSeconds = elapsedMs / 1000;
                        _gcodeLine = _fileCurrentLine;
                    }
                    Changed();
                }
                Thread.Sleep(dt);
            }
            lock (_lock) { _spindleRpm = 0; _spindleOn = false; _fileCurrentLine = _fileTotalLines; }
            Changed();
            return true;
        }

        private void MarkStopped(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _stepStatus.Length) _stepStatus[index] = "stopped";
                _spindleRpm = 0;
                _statusText = _abort ? "Aborted." : "Stopped.";
                _activeStepIndex = -1;
            }
            _running = false;
            Changed();
        }

        // ----- Snapshot -----

        public StatusSnapshot GetSnapshot()
        {
            var snap = new StatusSnapshot();
            lock (_lock)
            {
                snap.connected = true;
                snap.machine.homed = new AxisFlags { x = _homed[0], y = _homed[1], z = _homed[2], a = _homed[3] };
                snap.machine.cycleRunning = _running && _activeStepIndex >= 0;
                snap.machine.feedHold = _feedHold;
                snap.machine.moving = _jogContinuous || (_running && _activeStepIndex >= 0 && !_feedHold);
                snap.machine.estopped = _estopped;
                snap.machine.units = "mm";
                snap.machine.pos = new AxisPos { x = _work[0], y = _work[1], z = _work[2], a = _work[3] };
                snap.machine.machinePos = new AxisPos { x = _machine[0], y = _machine[1], z = _machine[2], a = _machine[3] };
                snap.machine.spindleRpm = _spindleRpm;
                snap.machine.spindleOn = _spindleOn;
                snap.machine.gcodeLine = _gcodeLine;

                snap.maestro.running = _running;
                snap.maestro.activeProjectId = _activeProject != null ? _activeProject.id : "";
                snap.maestro.activeStepIndex = _activeStepIndex;
                snap.maestro.statusText = _statusText;
                snap.maestro.promptWaiting = _promptWaiting;
                snap.maestro.promptText = _promptText;
                snap.maestro.promptIsGateOnly = _promptIsGateOnly;
                snap.maestro.fileCurrentLine = _fileCurrentLine;
                snap.maestro.fileTotalLines = _fileTotalLines;
                snap.maestro.estimateSeconds = _estimateSeconds;
                snap.maestro.elapsedSeconds = _elapsedSeconds;
                snap.maestro.remainingSeconds = Math.Max(0, _estimateSeconds - _elapsedSeconds);

                if (_activeProject != null)
                {
                    for (int i = 0; i < _activeProject.steps.Count; i++)
                    {
                        var step = _activeProject.steps[i];
                        snap.maestro.steps.Add(new MaestroStepStatus
                        {
                            index = i,
                            label = step.label,
                            type = step.IsGate ? "gate" : "op",
                            toolLabel = ToolLabel(step),
                            status = i < _stepStatus.Length ? _stepStatus[i] : "pending",
                            lastRunSeconds = step.IsOp ? SimStepSeconds : 0
                        });
                    }
                }
            }
            return snap;
        }

        private string ToolLabel(WorkflowStep step)
        {
            if (step == null || step.toolId <= 0) return "";
            var tool = JsonStore.FindTool(_tools, step.toolId);
            return tool != null ? tool.DisplayLabel() : ("Tool " + step.toolId);
        }

        private void Changed()
        {
            if (SnapshotChanged != null) SnapshotChanged();
        }
    }
}
