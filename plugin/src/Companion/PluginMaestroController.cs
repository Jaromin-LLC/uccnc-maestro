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

        // Buttons (Callbutton). 128 = Cycle Start (confirmed in MachineOps).
        public const int BtnCycleStart = 128;
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

        // Continuous jog emulation (incremental moves until stopped).
        private volatile bool _jogActive;
        private Thread _jogThread;

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
            _jogActive = true;
            // Emulate continuous jog with a stream of small incremental moves while the
            // client holds the button (the server watchdog calls JogStop when keepalives
            // stop). NOTE: a production build may switch to UCCNC's jog Callbutton codes;
            // centralize that change here.
            _jogThread = new Thread(() =>
            {
                double inc = Math.Max(0.05, feed / 60.0 * 0.12); // ~0.12 s of travel per pulse
                while (_jogActive)
                {
                    JogIncrement(axis, dir * inc, feed);
                    Changed();
                    Thread.Sleep(100);
                }
            }) { IsBackground = true };
            _jogThread.Start();
        }

        private void StopContinuousJog()
        {
            _jogActive = false;
            var t = _jogThread;
            if (t != null) { try { t.Join(300); } catch { } }
            _jogThread = null;
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
            // Homing is not exposed via a confirmed Callbutton id here; surface a clear
            // message rather than press an unknown button. (Verify the Home button number
            // on the target UCCNC build, then implement via Callbutton.)
            return CommandResult.Fail("not_supported",
                "Remote homing is not enabled for this machine yet. Home from UCCNC, then use the app.", 501);
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
            // UCCNC feed-hold button id is not confirmed here; fall back to Stop so a
            // safety request always halts motion. (Verify feed-hold button to enable a
            // true pause/resume.)
            try { _host.UC.Stop(); } catch { }
            SetStatus("Feed hold (stop) requested.");
            return CommandResult.Ok();
        }

        public CommandResult Resume()
        {
            try { _host.UC.Callbutton(UccncIo.BtnCycleStart); } catch { }
            SetStatus("Resume (cycle start) requested.");
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
            try { _host.UC.Stop(); } catch { }
            SetStatus("E-STOP requested.");
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
            double wx = 0, wy = 0, wz = 0, wa = 0;
            int feedOv = 100, line = 0;
            if (uc != null)
            {
                try { homedX = uc.GetLED(UccncIo.LedHomedX); } catch { }
                try { homedY = uc.GetLED(UccncIo.LedHomedY); } catch { }
                try { homedZ = uc.GetLED(UccncIo.LedHomedZ); } catch { }
                try { cycle = uc.GetLED(UccncIo.LedCycle); } catch { }
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
            snap.machine.moving = false;
            snap.machine.units = "mm";
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
