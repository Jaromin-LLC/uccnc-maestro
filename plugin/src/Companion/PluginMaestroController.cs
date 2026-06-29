using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Plugins.Companion
{
    /// <summary>
    /// UCCNC field / LED / button identifiers used by the companion bridge.
    ///
    /// Z work DRO (228) and feed-override (232) are confirmed in MachineOps; the rest are
    /// the conventional UCCNC defaults and are centralized here so they can be verified /
    /// adjusted against the target UCCNC build in one place (see docs/companion/SECURITY.md
    /// "preconditions"). Behaviour that depends on an unverified id fails safe (read => 0,
    /// command => no-op with a status note).
    /// </summary>
    internal static class UccncIo
    {
        // Work-coordinate DRO field numbers (main screen).
        public const int DroWorkX = 226;
        public const int DroWorkY = 227;
        public const int DroWorkZ = 228; // confirmed in MachineOps
        public const int DroWorkA = 229;

        public const int FeedOverrideField = 232; // confirmed in MachineOps

        // LEDs.
        public const int LedCycle = 54;     // confirmed in MaestroForm
        public const int LedHomedX = 56;    // confirmed in MachineOps
        public const int LedHomedY = 57;    // confirmed in MachineOps
        public const int LedHomedZ = 58;    // confirmed in MachineOps
        public const int LedReset = 25;     // active reset signal (machine disabled)
        public const int LedEstop = 36;     // hardware E-stop input triggered
        public const int LedFeedhold = 217; // feed-hold button active

        // Buttons (Callbutton). 128 = Cycle Start (confirmed in MachineOps).
        public const int BtnCycleStart = 128;
        public const int BtnCycleStop = 130;
        public const int BtnResetOn = 512;  // puts the machine into reset (remote E-stop)
        public const int BtnResetOff = 513;
        public const int BtnFeedHoldOn = 523;
        public const int BtnFeedHoldOff = 524;

        // Homing buttons: all = 113; per-axis X/Y/Z/A = 107/108/109/110.
        public const int BtnHomeAll = 113;
        public static int HomeButtonForAxis(string axis)
        {
            switch ((axis ?? "all").ToUpperInvariant())
            {
                case "ALL": case "": return BtnHomeAll;
                case "X": return 107;
                case "Y": return 108;
                case "Z": return 109;
                case "A": return 110;
                default: return -1;
            }
        }

        // Native jog buttons (Callbutton). Standard UCCNC codes: a "start" code begins
        // jogging in a direction and the matching "off" code stops it (continuous jog must
        // be turned off explicitly). 161 forces continuous mode first.
        public const int JogModeCont = 161;

        // Jog feedrate field (units/min) - lets the app set continuous jog speed directly.
        public const int JogFeedrateField = 913;

        // Start codes: X+ 147, X- 148, Y+ 149, Y- 150, Z+ 151, Z- 152, A+ 153, A- 154.
        // Off codes:   X+ 229, X- 230, Y+ 231, Y- 232, Z+ 233, Z- 234, A+ 235, A- 236.
        public static bool TryGetJogButtons(string axis, int dir, out int startBtn, out int stopBtn)
        {
            startBtn = 0; stopBtn = 0;
            int baseStart, baseStop;
            switch ((axis ?? "").ToUpperInvariant())
            {
                case "X": baseStart = 147; baseStop = 229; break;
                case "Y": baseStart = 149; baseStop = 231; break;
                case "Z": baseStart = 151; baseStop = 233; break;
                case "A": baseStart = 153; baseStop = 235; break;
                default: return false;
            }
            // +dir uses the base code; -dir uses the next (odd) code.
            int offset = dir >= 0 ? 0 : 1;
            startBtn = baseStart + offset;
            stopBtn = baseStop + offset;
            return true;
        }
    }

    /// <summary>
    /// Real controller: bridges the companion server to the live machine
    /// (Plugininterface.Entry) and the Maestro WorkflowEngine. Long-running motion ops run
    /// on background threads so HTTP handlers return promptly; live state is pushed via the
    /// engine's events and direct DRO reads.
    /// </summary>
    public class PluginMaestroController : IMaestroController
    {
        public event Action SnapshotChanged;

        private readonly UCCNCplugin _host;
        private readonly WorkflowEngine _engine;
        private readonly Form _owner;
        private readonly CompanionSettings _settings;
        private readonly Action<Action> _uiInvoke;

        private readonly object _lock = new object();
        private string _selectedProjectId = "";
        private string[] _stepStatus = new string[0];
        private string _statusText = "Ready";

        private bool _promptWaiting;
        private string _promptText = "";
        private bool _promptIsGateOnly;
        private string _promptPhotoUrl = "";

        private int _fileCurrentLine, _fileTotalLines;
        private int _activeStepIndex = -1;
        private DateTime _workStart = DateTime.MinValue;
        private int _estimateSeconds;

        // Native continuous jog: remember the active "off" button so JogStop / watchdog can
        // turn it back off (UCCNC continuous jog does not self-stop).
        private volatile bool _jogActive;
        private int _jogStopButton;

        // Manual spindle (jog screen) - reflected in the snapshot until UCCNC reports RPM.
        private bool _spindleOn;
        private double _spindleRpm;

        public PluginMaestroController(UCCNCplugin host, WorkflowEngine engine, Form owner,
            CompanionSettings settings, Action<Action> uiInvoke)
        {
            _host = host;
            _engine = engine;
            _owner = owner;
            _settings = settings;
            _uiInvoke = uiInvoke;

            if (_engine.State != null && !string.IsNullOrEmpty(_engine.State.lastProjectId))
                _selectedProjectId = _engine.State.lastProjectId;
            else if (_engine.Document != null && _engine.Document.projects.Count > 0)
                _selectedProjectId = _engine.Document.projects[0].id;

            RebuildStepStatus();
            HookEngine();
        }

        public string MachineId { get { return _settings.machineId; } }
        public string MachineName { get { return _settings.machineName; } }
        public string CameraUrl { get { return _settings.cameraUrl ?? ""; } }

        public ProjectsDocument GetProjects() { return _engine.Document; }
        public ToolLibraryDocument GetTools() { return _engine.ToolLibrary; }

        // ----- Engine events -> cached state + SSE push -----

        private void HookEngine()
        {
            _engine.StatusChanged += msg => { lock (_lock) { _statusText = msg; } Changed(); };
            _engine.RunningChanged += running => { if (!running) RebuildStepStatus(); Changed(); };
            _engine.StepStatusChanged += (index, status) =>
            {
                lock (_lock)
                {
                    EnsureStepStatusSize();
                    if (index >= 0 && index < _stepStatus.Length)
                        _stepStatus[index] = StatusToString(status);
                    _activeStepIndex = status == StepRunStatus.Running ? index : _activeStepIndex;
                }
                Changed();
            };
            _engine.PromptRequired += (step, index, gateOnly) =>
            {
                lock (_lock)
                {
                    _promptWaiting = true;
                    _promptIsGateOnly = gateOnly;
                    _promptText = gateOnly
                        ? (string.IsNullOrEmpty(step.DisplayInstructions) ? "Operator action required." : step.DisplayInstructions)
                        : (string.IsNullOrEmpty(step.DisplayInstructions) ? "Install tool and confirm." : step.DisplayInstructions);
                    _promptPhotoUrl = string.IsNullOrEmpty(step.photo) ? "" : ("/api/media?path=" + Uri.EscapeDataString(step.photo));
                }
                Changed();
            };
            _engine.FileProgressChanged += (cur, total) => { lock (_lock) { _fileCurrentLine = cur; _fileTotalLines = total; } Changed(); };
            _engine.StepWorkStarted += index =>
            {
                lock (_lock)
                {
                    _workStart = DateTime.UtcNow;
                    _estimateSeconds = RunStateStore.GetLastRunSeconds(_engine.State, _selectedProjectId, index);
                    _promptWaiting = false;
                }
                Changed();
            };
            _engine.RunFinished += () => { lock (_lock) { _promptWaiting = false; _activeStepIndex = -1; } Changed(); };
        }

        // ----- Jog -----

        public CommandResult Jog(string axis, int dir, string mode, double step, double feed)
        {
            string ax = (axis ?? "").ToUpperInvariant();
            if (ax != "X" && ax != "Y" && ax != "Z" && ax != "A") return CommandResult.BadRequest("Unknown axis: " + axis);
            if (_engine.IsRunning) return CommandResult.Conflict("Cannot jog while a job is running.");
            if (_host.UC != null && _host.UC.GetLED(UccncIo.LedCycle)) return CommandResult.Conflict("Cannot jog while a cycle is active.");

            double dist = Math.Abs(step) <= 0 ? 1.0 : Math.Abs(step);
            int d = dir >= 0 ? 1 : -1;
            double f = feed > 0 ? feed : 1500;

            if (mode == "cont")
            {
                StartContinuousJog(ax, d, f);
                return CommandResult.Ok();
            }

            JogIncrement(ax, d * dist, f);
            Changed();
            return CommandResult.Ok();
        }

        private void JogIncrement(string axis, double signedDist, double feed)
        {
            // Incremental (relative) move; restore absolute mode afterwards so later G53
            // machine moves behave (see MachineOps note about G90/G53).
            try
            {
                _host.UC.Codesync("G91 G1 " + axis + signedDist.ToString("F4", CultureInfo.InvariantCulture) +
                                   " F" + feed.ToString("F1", CultureInfo.InvariantCulture));
                _host.UC.Codesync("G90");
            }
            catch { }
        }

        private void StartContinuousJog(string axis, int dir, double feed)
        {
            StopContinuousJog();

            int startBtn, stopBtn;
            if (!UccncIo.TryGetJogButtons(axis, dir, out startBtn, out stopBtn))
            {
                // Unknown axis: fall back to a single bounded step so we never stream moves.
                JogIncrement(axis, dir * 1.0, feed);
                Changed();
                return;
            }

            // True continuous jog: force continuous mode, then press the axis jog button.
            // Motion runs until the matching "off" button (or Stop) is sent - the HTTP
            // keepalive watchdog calls JogStop() when the client releases or disconnects.
            lock (_lock) { _jogStopButton = stopBtn; _jogActive = true; }
            try
            {
                // Set the jog feedrate (units/min) from the app's JOG FEED slider, then jog.
                if (feed > 0)
                {
                    try { _host.UC.Setfield(true, feed, UccncIo.JogFeedrateField); _host.UC.Validatefield(true, UccncIo.JogFeedrateField); } catch { }
                }
                _host.UC.Callbutton(UccncIo.JogModeCont);
                _host.UC.Callbutton(startBtn);
            }
            catch { /* fail safe: leave _jogActive so a later JogStop still fires the off code */ }
        }

        private void StopContinuousJog()
        {
            int stopBtn;
            lock (_lock)
            {
                if (!_jogActive) { _jogStopButton = 0; return; }
                _jogActive = false;
                stopBtn = _jogStopButton;
                _jogStopButton = 0;
            }
            if (stopBtn != 0) { try { _host.UC.Callbutton(stopBtn); } catch { } }
        }

        public CommandResult JogStop()
        {
            StopContinuousJog();
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Spindle(bool on, double rpm)
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Cannot control spindle while a job is running.");
            try
            {
                if (on && rpm > 0)
                {
                    _host.UC.Codesync("M3 S" + ((int)rpm).ToString(CultureInfo.InvariantCulture));
                    lock (_lock) { _spindleOn = true; _spindleRpm = rpm; }
                    SetStatus("Spindle ON at " + (int)rpm + " RPM.");
                }
                else
                {
                    _host.UC.Code("M5");
                    lock (_lock) { _spindleOn = false; _spindleRpm = 0; }
                    SetStatus("Spindle OFF.");
                }
            }
            catch (Exception ex) { return CommandResult.Fail("server_error", ex.Message, 500); }
            return CommandResult.Ok();
        }

        // ----- Machine commands -----

        public CommandResult Zero(string axis)
        {
            try
            {
                if (axis == "all")
                {
                    SetWorkZero(UccncIo.DroWorkX);
                    SetWorkZero(UccncIo.DroWorkY);
                    SetWorkZero(UccncIo.DroWorkZ);
                    SetWorkZero(UccncIo.DroWorkA);
                }
                else
                {
                    int field = FieldForAxis(axis);
                    if (field < 0) return CommandResult.BadRequest("Unknown axis: " + axis);
                    SetWorkZero(field);
                }
            }
            catch (Exception ex) { return CommandResult.Fail("server_error", ex.Message, 500); }
            Changed();
            return CommandResult.Ok();
        }

        private void SetWorkZero(int field)
        {
            _host.UC.Setfield(true, 0.0, field);
            _host.UC.Validatefield(true, field);
        }

        public CommandResult Home(string axis)
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Cannot home while a job is running.");
            if (_host.UC != null && _host.UC.GetLED(UccncIo.LedCycle)) return CommandResult.Conflict("Cannot home while a cycle is active.");

            int btn = UccncIo.HomeButtonForAxis(axis);
            if (btn < 0) return CommandResult.BadRequest("Unknown axis: " + axis);
            StopContinuousJog();
            try { _host.UC.Callbutton(btn); }
            catch (Exception ex) { return CommandResult.Fail("server_error", ex.Message, 500); }
            SetStatus(btn == UccncIo.BtnHomeAll ? "Homing all axes..." : ("Homing " + axis.ToUpperInvariant() + "..."));
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult GotoZero()
        {
            RunOnBackground(ops => ops.GotoWorkZero(SetStatusFromOps));
            return CommandResult.Ok();
        }

        public CommandResult Park(string type)
        {
            RunOnBackground(ops =>
            {
                switch ((type ?? "").ToLowerInvariant())
                {
                    case "g28": return ops.Park("G28", SetStatusFromOps);
                    case "g30": return ops.Park("G30", SetStatusFromOps);
                    default: return ops.ParkCustom(SetStatusFromOps);
                }
            });
            return CommandResult.Ok();
        }

        public CommandResult AutoZero()
        {
            if (!Homed()) return CommandResult.Unavailable("Reference all axes before auto-zero.");
            RunOnBackground(ops => ops.ProbeFixedPlate(SetStatusFromOps));
            return CommandResult.Ok();
        }

        public CommandResult FeedHold()
        {
            // True feed hold: pauses motion (resumable), unlike Stop which ends the move.
            try { _host.UC.Callbutton(UccncIo.BtnFeedHoldOn); } catch { }
            SetStatus("Feed hold.");
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Resume()
        {
            // Release feed hold; if a job was paused mid-run this resumes it.
            try { _host.UC.Callbutton(UccncIo.BtnFeedHoldOff); } catch { }
            SetStatus("Resumed.");
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Stop()
        {
            StopContinuousJog();
            if (_engine.IsRunning) _engine.RequestAbort();
            else { try { _host.UC.Stop(); } catch { } }
            SetStatus("Stop requested.");
            return CommandResult.Ok();
        }

        public CommandResult EStop()
        {
            StopContinuousJog();
            if (_engine.IsRunning) _engine.RequestAbort();
            // Put the machine into Reset (disables motion) - this is what an operator sees
            // as "E-stop / reset" on the UCCNC screen. Stop() as a belt-and-suspenders halt.
            try { _host.UC.Callbutton(UccncIo.BtnResetOn); } catch { }
            try { _host.UC.Stop(); } catch { }
            SetStatus("E-STOP - machine in reset.");
            Changed();
            return CommandResult.Ok();
        }

        // ----- Maestro workflow -----

        public CommandResult SelectProject(string projectId)
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Cannot change project while running.");
            if (FindProject(projectId) == null) return CommandResult.BadRequest("Unknown project: " + projectId);
            lock (_lock) { _selectedProjectId = projectId; }
            RebuildStepStatus();
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult RunAll(int fromIndex)
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Already running.");
            var project = FindProject(_selectedProjectId);
            if (project == null) return CommandResult.BadRequest("No project selected.");
            int start = fromIndex >= 0
                ? fromIndex
                : RunStateStore.FirstNotDone(_engine.State, project.id, project.steps.Count);
            _uiInvoke(() => _engine.RunAll(project, start < 0 ? 0 : start, fromIndex >= 0));
            return CommandResult.Ok();
        }

        public CommandResult RunStep(int index)
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Already running.");
            var project = FindProject(_selectedProjectId);
            if (project == null) return CommandResult.BadRequest("No project selected.");
            if (index < 0 || index >= project.steps.Count) return CommandResult.BadRequest("Step out of range.");
            _uiInvoke(() => _engine.RunStep(project, index, true));
            return CommandResult.Ok();
        }

        public CommandResult ResetProject()
        {
            if (_engine.IsRunning) return CommandResult.Conflict("Cannot reset while running.");
            var project = FindProject(_selectedProjectId);
            if (project == null) return CommandResult.BadRequest("No project selected.");
            _engine.ResetProject(project);
            RebuildStepStatus();
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult Abort()
        {
            if (_engine.IsRunning) _engine.RequestAbort();
            return CommandResult.Ok();
        }

        public CommandResult ConfirmPrompt()
        {
            _engine.ConfirmPrompt();
            lock (_lock) { _promptWaiting = false; }
            Changed();
            return CommandResult.Ok();
        }

        public CommandResult CancelPrompt()
        {
            _engine.CancelPrompt();
            lock (_lock) { _promptWaiting = false; }
            Changed();
            return CommandResult.Ok();
        }

        // ----- Snapshot -----

        public StatusSnapshot GetSnapshot()
        {
            var snap = new StatusSnapshot();
            var uc = _host.UC;

            bool homedX = false, homedY = false, homedZ = false, cycle = false;
            bool reset = false, estop = false, feedhold = false;
            double wx = 0, wy = 0, wz = 0, wa = 0;
            int feedOv = 100, line = 0;
            if (uc != null)
            {
                try { homedX = uc.GetLED(UccncIo.LedHomedX); } catch { }
                try { homedY = uc.GetLED(UccncIo.LedHomedY); } catch { }
                try { homedZ = uc.GetLED(UccncIo.LedHomedZ); } catch { }
                try { cycle = uc.GetLED(UccncIo.LedCycle); } catch { }
                try { reset = uc.GetLED(UccncIo.LedReset); } catch { }
                try { estop = uc.GetLED(UccncIo.LedEstop); } catch { }
                try { feedhold = uc.GetLED(UccncIo.LedFeedhold); } catch { }
                try { wx = uc.Getfielddouble(true, UccncIo.DroWorkX); } catch { }
                try { wy = uc.Getfielddouble(true, UccncIo.DroWorkY); } catch { }
                try { wz = uc.Getfielddouble(true, UccncIo.DroWorkZ); } catch { }
                try { wa = uc.Getfielddouble(true, UccncIo.DroWorkA); } catch { }
                try { feedOv = ParsePercent(uc.Getfield(true, UccncIo.FeedOverrideField)); } catch { }
                try { line = uc.Getcurrentgcodelinenumber(); } catch { }
            }

            snap.connected = uc != null;
            snap.machine.homed = new AxisFlags { x = homedX, y = homedY, z = homedZ, a = true };
            snap.machine.cycleRunning = cycle;
            snap.machine.estopped = reset || estop;
            snap.machine.feedHold = feedhold;
            snap.machine.moving = false;
            snap.machine.units = CompanionSettings.NormalizeUnits(_settings.units);
            snap.machine.pos = new AxisPos { x = wx, y = wy, z = wz, a = wa };
            snap.machine.machinePos = new AxisPos { x = wx, y = wy, z = wz, a = wa };
            snap.machine.feedOverride = feedOv;
            snap.machine.gcodeLine = line;

            lock (_lock)
            {
                snap.machine.spindleOn = _spindleOn;
                snap.machine.spindleRpm = _spindleRpm;

                var project = FindProject(_selectedProjectId);
                snap.maestro.running = _engine.IsRunning;
                snap.maestro.activeProjectId = _selectedProjectId;
                snap.maestro.activeStepIndex = _engine.IsRunning ? _activeStepIndex : -1;
                snap.maestro.statusText = _statusText;
                snap.maestro.promptWaiting = _promptWaiting;
                snap.maestro.promptText = _promptText;
                snap.maestro.promptIsGateOnly = _promptIsGateOnly;
                snap.maestro.promptPhotoUrl = _promptPhotoUrl;
                snap.maestro.fileCurrentLine = _fileCurrentLine;
                snap.maestro.fileTotalLines = _fileTotalLines;
                snap.maestro.estimateSeconds = _estimateSeconds;
                int elapsed = _workStart == DateTime.MinValue ? 0 : (int)(DateTime.UtcNow - _workStart).TotalSeconds;
                snap.maestro.elapsedSeconds = _engine.IsRunning ? elapsed : 0;
                snap.maestro.remainingSeconds = Math.Max(0, _estimateSeconds - elapsed);

                if (project != null)
                {
                    EnsureStepStatusSize(project);
                    for (int i = 0; i < project.steps.Count; i++)
                    {
                        var step = project.steps[i];
                        snap.maestro.steps.Add(new MaestroStepStatus
                        {
                            index = i,
                            label = step.label,
                            type = step.IsGate ? "gate" : "op",
                            toolLabel = ToolLabel(step),
                            status = i < _stepStatus.Length ? _stepStatus[i] : "pending",
                            lastRunSeconds = RunStateStore.GetLastRunSeconds(_engine.State, project.id, i)
                        });
                    }
                }
            }
            return snap;
        }

        // ----- Helpers -----

        private bool Homed()
        {
            var uc = _host.UC;
            if (uc == null) return false;
            try { return uc.GetLED(UccncIo.LedHomedX) && uc.GetLED(UccncIo.LedHomedY) && uc.GetLED(UccncIo.LedHomedZ); }
            catch { return false; }
        }

        private static int ParsePercent(string s)
        {
            if (string.IsNullOrEmpty(s)) return 100;
            double v;
            if (double.TryParse(s.Replace("%", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                return (int)Math.Round(v);
            return 100;
        }

        private static int FieldForAxis(string axis)
        {
            switch ((axis ?? "").ToUpperInvariant())
            {
                case "X": return UccncIo.DroWorkX;
                case "Y": return UccncIo.DroWorkY;
                case "Z": return UccncIo.DroWorkZ;
                case "A": return UccncIo.DroWorkA;
                default: return -1;
            }
        }

        private WorkflowProject FindProject(string id)
        {
            if (_engine.Document == null || _engine.Document.projects == null) return null;
            foreach (var p in _engine.Document.projects)
                if (p.id == id) return p;
            return null;
        }

        private string ToolLabel(WorkflowStep step)
        {
            if (step == null || step.toolId <= 0) return "";
            var tool = JsonStore.FindTool(_engine.ToolLibrary, step.toolId);
            return tool != null ? tool.DisplayLabel() : ("Tool " + step.toolId);
        }

        private void RebuildStepStatus()
        {
            var project = FindProject(_selectedProjectId);
            lock (_lock)
            {
                int n = project != null ? project.steps.Count : 0;
                _stepStatus = new string[n];
                for (int i = 0; i < n; i++)
                    _stepStatus[i] = RunStateStore.IsDone(_engine.State, _selectedProjectId, i) ? "done" : "pending";
            }
        }

        private void EnsureStepStatusSize()
        {
            EnsureStepStatusSize(FindProject(_selectedProjectId));
        }

        private void EnsureStepStatusSize(WorkflowProject project)
        {
            int n = project != null ? project.steps.Count : 0;
            if (_stepStatus.Length != n)
            {
                var next = new string[n];
                for (int i = 0; i < n; i++)
                    next[i] = i < _stepStatus.Length ? _stepStatus[i] : "pending";
                _stepStatus = next;
            }
        }

        private static string StatusToString(StepRunStatus status)
        {
            switch (status)
            {
                case StepRunStatus.Running: return "running";
                case StepRunStatus.Done: return "done";
                case StepRunStatus.Stopped: return "stopped";
                default: return "pending";
            }
        }

        private void RunOnBackground(Func<MachineOps, bool> action)
        {
            var project = FindProject(_selectedProjectId);
            var settings = _engine.Document != null ? _engine.Document.settings : new MaestroSettings();
            var ops = new MachineOps(_host.UC, settings, project, _owner, _uiInvoke);
            var t = new Thread(() => { try { action(ops); } catch { } }) { IsBackground = true };
            t.Start();
        }

        private void SetStatus(string msg)
        {
            lock (_lock) { _statusText = msg; }
            Changed();
        }

        private void SetStatusFromOps(string msg) { SetStatus(msg); }

        private void Changed()
        {
            if (SnapshotChanged != null) SnapshotChanged();
        }
    }
}
