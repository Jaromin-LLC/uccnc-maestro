using System;
using System.Globalization;
using System.Windows.Forms;

namespace Plugins
{
    public class MachineOps
    {
        private readonly Plugininterface.Entry _uc;
        private readonly MaestroSettings _settings;
        private readonly Form _owner;
        private readonly Action<Action> _uiInvoke;
        private ToolInfo _probeTool;

        public MachineOps(Plugininterface.Entry uc, MaestroSettings settings, WorkflowProject project, Form owner, Action<Action> uiInvoke)
        {
            _uc = uc;
            _settings = JsonStore.ResolveForProject(settings, project) ?? new MaestroSettings();
            _owner = owner;
            _uiInvoke = uiInvoke;
        }

        public bool TestMode
        {
            get { return _settings != null && _settings.testMode; }
        }

        /// <summary>
        /// Sets the tool whose per-tool probe settings (edge-probe offsets / rotate prompt)
        /// apply to the next ProbeFixedPlate call. Null restores center probing.
        /// </summary>
        public void SetActiveProbeTool(ToolInfo tool)
        {
            _probeTool = tool;
        }

        private void ShowMessage(string text, string caption, MessageBoxIcon icon)
        {
            Action show = () =>
            {
                if (_owner != null && !_owner.IsDisposed)
                    MessageBox.Show(_owner, text, caption, MessageBoxButtons.OK, icon);
                else
                    MessageBox.Show(text, caption, MessageBoxButtons.OK, icon);
            };

            if (_uiInvoke != null) _uiInvoke(show);
            else show();
        }

        private bool ConfirmMessage(string text, string caption, MessageBoxIcon icon)
        {
            DialogResult result = DialogResult.Cancel;
            Action show = () =>
            {
                if (_owner != null && !_owner.IsDisposed)
                    result = MessageBox.Show(_owner, text, caption, MessageBoxButtons.OKCancel, icon);
                else
                    result = MessageBox.Show(text, caption, MessageBoxButtons.OKCancel, icon);
            };

            // InvokeUi marshals synchronously (Control.Invoke), so result is populated
            // before this returns.
            if (_uiInvoke != null) _uiInvoke(show);
            else show();

            return result == DialogResult.OK;
        }

        private static string Dbl2Str(double val)
        {
            return val.ToString("F6", CultureInfo.InvariantCulture);
        }

        private void WaitForIdle()
        {
            while (_uc.IsMoving())
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        private ProbeSettings Probe
        {
            get { return _settings.probe ?? new ProbeSettings(); }
        }

        private ToolChangePos Tc
        {
            get { return _settings.toolChangePos ?? new ToolChangePos(); }
        }

        private ParkPos Pk
        {
            get { return _settings.parkPos ?? new ParkPos(); }
        }

        private bool TcSafeZEnabled()
        {
            return _settings.useSafeZForTc;
        }

        private double GetSafeZ()
        {
            return Tc.zSafe;
        }

        private double GetTcX()
        {
            return Tc.x;
        }

        private double GetTcY()
        {
            return Tc.y;
        }

        private double GetTcZ()
        {
            return Tc.z;
        }

        public bool Preflight(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE ACTIVE - configuration check and probing are skipped.");
            }

            if (!TestMode)
            {
                if ((Probe.xPlate == 0 && Probe.yPlate == 0) || Probe.dist == 0 || Probe.feedFast == 0)
                {
                    ShowMessage(
                        "Tool setter probing is not configured.\n\n" +
                        "Set the Fixed Plate X/Y location, probe distance and probe feedrates in the Admin tab under Probing / Tool Change (one-time machine setup), then try again.",
                        "Probing Not Configured", MessageBoxIcon.Error);
                    if (status != null) status("Aborted - configure tool setter probing first.");
                    return false;
                }
            }

            if (!_uc.GetLED(56) || !_uc.GetLED(57) || !_uc.GetLED(58))
            {
                ShowMessage(
                    "All axes must be referenced (homed) before starting.",
                    "Axes Not Homed", MessageBoxIcon.Error);
                if (status != null) status("Aborted - reference all axes first.");
                return false;
            }

            if (_uc.GetLED(54))
            {
                ShowMessage(
                    "Machine is cycling. Stop the current program before starting Maestro.",
                    "Cycle Active", MessageBoxIcon.Error);
                if (status != null) status("Aborted - cycle already active.");
                return false;
            }

            return true;
        }

        public bool MoveToolChange(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - skipping move to tool change position.");
                _uc.Wait(400);
                return true;
            }

            bool tcSafeZ = TcSafeZEnabled();
            double safeZ = GetSafeZ();
            double tcX = GetTcX();
            double tcY = GetTcY();
            double tcZ = GetTcZ();

            if (status != null) status("Moving to tool change position...");

            _uc.Codesync("G90");
            WaitForIdle();

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(safeZ));
                WaitForIdle();
            }
            else
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(tcZ));
                WaitForIdle();
            }

            _uc.Codesync("G53 G0 X" + Dbl2Str(tcX) + " Y" + Dbl2Str(tcY));
            WaitForIdle();

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(tcZ));
                WaitForIdle();
            }

            return true;
        }

        public bool SpindleOff(Action<string> status)
        {
            if (status != null) status("Stopping spindle...");
            _uc.Code("M5");
            WaitForIdle();
            return true;
        }

        public bool GotoWorkZero(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - skipping move to work zero.");
                _uc.Wait(300);
                return true;
            }

            if (status != null) status("Moving to work zero...");
            _uc.Codesync("G90");
            _uc.Codesync("G0 X0 Y0");
            WaitForIdle();
            return true;
        }

        public bool RunCustomMdi(string mdi, Action<string> status)
        {
            if (string.IsNullOrWhiteSpace(mdi)) return true;
            if (status != null) status("Running MDI: " + mdi);
            _uc.Codesync(mdi.Trim());
            WaitForIdle();
            return true;
        }

        /// <summary>
        /// Parks the spindle using a UCCNC predefined position move (G28 = primary
        /// reference, G30 = secondary park). Lifts to Safe Z first (when enabled) so the
        /// tool clears the part before traveling. The G28/G30 target positions are
        /// configured in UCCNC itself.
        /// </summary>
        public bool Park(string parkCode, Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - park (" + parkCode + ") skipped.");
                return true;
            }

            _uc.Codesync("G90");
            WaitForIdle();

            if (TcSafeZEnabled())
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(GetSafeZ()));
                WaitForIdle();
            }

            if (status != null) status("Parking (" + parkCode + ")...");
            _uc.Codesync(parkCode);
            WaitForIdle();

            if (status != null) status("Parked (" + parkCode + ").");
            return true;
        }

        /// <summary>
        /// Moves to the user-configured park position using absolute machine (G53)
        /// rapids, so the destination is adjustable and behaves identically on any
        /// machine (unlike G28/G30, which UCCNC ties to the homed origin). Retracts to
        /// Safe Z first (when enabled) before traveling in XY.
        /// </summary>
        public bool ParkCustom(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - park (custom) skipped.");
                return true;
            }

            bool tcSafeZ = TcSafeZEnabled();
            double safeZ = GetSafeZ();
            double px = Pk.x;
            double py = Pk.y;
            double pz = Pk.z;

            if (status != null) status("Moving to park position...");

            _uc.Codesync("G90");
            WaitForIdle();

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(safeZ));
                WaitForIdle();
            }
            else
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(pz));
                WaitForIdle();
            }

            _uc.Codesync("G53 G0 X" + Dbl2Str(px) + " Y" + Dbl2Str(py));
            WaitForIdle();

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(pz));
                WaitForIdle();
            }

            if (status != null) status("Parked (custom position).");
            return true;
        }

        public bool ProbeFixedPlate(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - probing skipped.");
                return true;
            }

            bool plateRapid = Probe.plateRapid;
            bool tcSafeZ = TcSafeZEnabled();
            double safeZ = GetSafeZ();

            double plateZero = Probe.plateZero;
            double xPlate = Probe.xPlate;
            double yPlate = Probe.yPlate;
            double probeDist = Probe.dist;
            double retractDist = Probe.retractDist;
            double firstProbeFeed = Probe.feedFast;
            double secProbeFeed = Probe.feedSlow;
            double plateRapidZ = Probe.plateRapidZ;

            // Per-tool edge-probe: shift the probe point so a cutting edge lands over the
            // puck instead of the (debris-prone, possibly relieved) tool center.
            double probeXOffset = _probeTool != null ? _probeTool.probeXOffset : 0;
            double probeYOffset = _probeTool != null ? _probeTool.probeYOffset : 0;
            bool edgeProbePrompt = _probeTool != null && _probeTool.edgeProbePrompt;
            double xProbe = xPlate + probeXOffset;
            double yProbe = yPlate + probeYOffset;

            string feedOr = _uc.Getfield(true, 232);
            double currentFeedOr = Convert.ToDouble(feedOr.Replace("%", string.Empty), CultureInfo.InvariantCulture);
            double feedFactor = 100 / currentFeedOr;
            firstProbeFeed = firstProbeFeed * feedFactor;
            secProbeFeed = secProbeFeed * feedFactor;

            // G53 (move in machine coordinates) is only honored in G90 absolute mode.
            // If the controller is left in G91, G53 is ignored and the move runs in the
            // active work offset - sending the plate move to the wrong location. Force
            // absolute mode before any G53 move.
            _uc.Codesync("G90");
            WaitForIdle();

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(safeZ));
                WaitForIdle();
            }

            _uc.Codesync("G53 G0 X" + Dbl2Str(xProbe) + " Y" + Dbl2Str(yProbe));
            WaitForIdle();

            if (_uc.GetLED(37))
            {
                ShowMessage(
                    "Probe input is active. Check connections and try again.",
                    "Probe Active", MessageBoxIcon.Error);
                if (status != null) status("Aborted - probe input active.");
                return false;
            }

            if (plateRapid)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(plateRapidZ));
                WaitForIdle();
            }

            if (edgeProbePrompt)
            {
                if (status != null) status("Waiting - rotate spindle so a cutting edge is over the plate.");
                if (!ConfirmMessage(
                        "Rotate the spindle by hand so a cutting edge is directly over the touch plate, then click OK.\n\n" +
                        "Click Cancel to abort probing.",
                        "Position Cutting Edge", MessageBoxIcon.Information))
                {
                    if (status != null) status("Aborted - edge probe canceled.");
                    return false;
                }
            }

            _uc.Codesync("G90");
            WaitForIdle();

            if (status != null) status("Initial probe for Z zero...");
            double zNew = (_uc.Getfielddouble(true, 228) - probeDist);
            _uc.Codesync("G31 Z" + Dbl2Str(zNew) + " F" + Dbl2Str(firstProbeFeed));
            WaitForIdle();

            if (!ValidateProbePass("first", status)) return false;

            zNew = _uc.Getvar(5063);
            if (!ValidateProbeTouchZ(zNew, "first", status)) return false;
            _uc.Codesync("G0 Z" + Dbl2Str((zNew + retractDist)));
            WaitForIdle();

            zNew = (_uc.Getfielddouble(true, 228) - probeDist);
            if (status != null) status("Final probe for Z zero...");
            _uc.Codesync("G31 Z" + Dbl2Str(zNew) + " F" + Dbl2Str(secProbeFeed));
            WaitForIdle();

            if (!ValidateProbePass("second", status)) return false;

            zNew = _uc.Getvar(5063);
            if (!ValidateProbeTouchZ(zNew, "second", status)) return false;
            _uc.Codesync("G0 Z" + Dbl2Str(zNew));
            WaitForIdle();

            PropagateZeroToAllOffsets(plateZero, status);

            // Lift clear of the plate after zeroing so the program's first move
            // doesn't drag the tool across the plate/part. Uses the same Safe Z
            // and checkbox that gate the pre-probe approach retract.
            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(safeZ));
                WaitForIdle();
            }

            if (status != null) status("Tool zeroed on fixed plate.");
            return true;
        }

        /// <summary>
        /// Setting the Z DRO only re-zeros the active work offset. Programs that cycle
        /// through multiple fixture offsets (G54-G59) for N-up parts need the same Z
        /// plane in every offset, otherwise parts after the first run with the previous
        /// tool's length.
        ///
        /// Called with the tool parked at the plate touch point. Rather than reading the
        /// offset-origin variables (#52xx) or relying on G10 L2/L20 P-addressing - neither
        /// behaves reliably across UCCNC builds here - we use the one mechanism proven to
        /// work on this machine: making a fixture active and writing the Z DRO zeroes that
        /// fixture. We briefly cycle each fixture active, zero it to plateZero, then restore
        /// the original active fixture. No motion occurs; only each offset's Z is set.
        /// </summary>
        private void PropagateZeroToAllOffsets(double plateZero, Action<string> status)
        {
            int active = (int)_uc.Getvar(5220);
            if (active < 1 || active > 6) active = 1;

            for (int p = 1; p <= 6; p++)
            {
                _uc.Codesync("G" + (53 + p));
                _uc.Setfield(true, plateZero, 228);
                _uc.Validatefield(true, 228);
            }

            // Restore the coordinate system that was active before propagation.
            _uc.Codesync("G" + (53 + active));

            if (status != null) status("Z zero applied to all work offsets (G54-G59).");
        }

        /// <summary>
        /// UCCNC #5060 after G31: 0 = probe triggered, 1 = max distance reached without touch,
        /// 2 = probe was already active when G31 started.
        /// </summary>
        private bool ValidateProbePass(string passLabel, Action<string> status)
        {
            double probeFinished = _uc.Getvar(5060);
            if (probeFinished == 0 && _uc.GetLED(244))
                return true;

            string message;
            if (probeFinished == 2)
            {
                message =
                    "The " + passLabel + " probe move was aborted because the probe input was already active.\n\n" +
                    "Check the probe wiring/noise and that nothing is touching the probe, then try again.";
            }
            else if (probeFinished == 1)
            {
                message =
                    "The " + passLabel + " probe move reached the configured probe distance without triggering.\n\n" +
                    "Increase probe distance slightly, verify the tool setter location and probe input, then try again.";
            }
            else
            {
                message =
                    "The " + passLabel + " probe move did not report a successful touch (#5060=" +
                    probeFinished.ToString(CultureInfo.InvariantCulture) + ").\n\n" +
                    "Check probe distance, probe input, and wiring, then try again.";
            }

            ShowMessage(message, "Probe Error", MessageBoxIcon.Error);
            if (status != null) status("Aborted - " + passLabel + " probe failed.");
            try { _uc.Stop(); } catch { }
            return false;
        }

        /// <summary>
        /// When G31 fails, UCCNC clears #5063 to 0. Moving G0 Z0 after that causes a dangerous plunge.
        /// </summary>
        private bool ValidateProbeTouchZ(double zTouch, string passLabel, Action<string> status)
        {
            if (Math.Abs(zTouch) > 0.0001)
                return true;

            ShowMessage(
                "The " + passLabel + " probe returned an invalid Z touch coordinate (0).\n\n" +
                "The move was stopped before setting work zero. Check probe distance and input.",
                "Probe Error", MessageBoxIcon.Error);
            if (status != null) status("Aborted - invalid " + passLabel + " probe Z.");
            try { _uc.Stop(); } catch { }
            return false;
        }

        public bool SetCurrentTool(int toolNum, Action<string> status)
        {
            // Older UCCNC releases (e.g. 2019 builds) do not expose Setcurrenttool on the
            // plugin interface. A direct call would throw MissingMethodException when this
            // method is JIT-compiled, so resolve it via reflection and skip when absent.
            var setCurrentTool = typeof(Plugininterface.Entry).GetMethod("Setcurrenttool", new[] { typeof(int) });
            if (setCurrentTool != null)
            {
                setCurrentTool.Invoke(_uc, new object[] { toolNum });
                if (status != null) status("Tool set to T" + toolNum);
            }
            else
            {
                if (status != null) status("Tool T" + toolNum + " confirmed (this UCCNC version cannot set the current tool number).");
            }

            return true;
        }

        // Cycle Start can legitimately take several seconds to register after a file
        // load - more so on this machine, where AXBB-E link stalls can briefly freeze
        // the controller. A short fixed timeout produced false "did not trigger" errors
        // on runs that actually started and completed, so the window is generous and the
        // real signal comes from the latching start/finish events below.
        private const int CycleStartTimeoutMs = 20000;

        public bool LoadAndRunFile(string fullPath, Action<string> status, Func<bool> isAborted, Action waitForCycleFinish,
            Action resetCycleSignals, Func<bool> cycleStarted, Func<bool> cycleFinished)
        {
            if (!System.IO.File.Exists(fullPath))
            {
                if (TestMode)
                {
                    if (status != null) status("DEMO - file missing, simulating: " + System.IO.Path.GetFileName(fullPath));
                    for (int i = 0; i < 12; i++)
                    {
                        if (isAborted != null && isAborted()) return false;
                        _uc.Wait(150);
                    }
                    if (status != null) status("DEMO - simulated run complete.");
                    return true;
                }

                ShowMessage(
                    "G-code file not found:\n" + fullPath,
                    "Load Error", MessageBoxIcon.Error);
                if (status != null) status("Aborted - file not found.");
                return false;
            }

            if (status != null) status("Loading " + System.IO.Path.GetFileName(fullPath));
            _uc.Loadfile(fullPath);

            int loadWait = 0;
            while (_uc.IsLoading())
            {
                if (isAborted != null && isAborted()) return false;
                _uc.Wait(50);
                loadWait += 50;
                if (loadWait >= 30000) break;
            }
            _uc.Wait(200);

            if (isAborted != null && isAborted()) return false;

            _uc.Callbutton(127);
            _uc.Wait(150);

            // Clear any latched start/finish state from prior moves so the signals we
            // read below belong to this cycle only.
            if (resetCycleSignals != null) resetCycleSignals();

            if (status != null) status("Cycle start...");
            _uc.Callbutton(128);

            // Confirm the cycle actually began. The start/finish events latch (set by
            // UCCNC's Cyclethreadstart/finish callbacks), so neither a very short program
            // that completes before we sample, nor a controller that is slow to spool up,
            // is mistaken for a failed start.
            int waited = 0;
            bool started = false;
            while (true)
            {
                if (isAborted != null && isAborted()) return false;

                // Short cycle that already ran to completion between samples.
                if (cycleFinished != null && cycleFinished())
                {
                    if (status != null) status("Run finished.");
                    return true;
                }

                // Cycle confirmed running - hand off to the finish wait below.
                if ((cycleStarted != null && cycleStarted()) || _uc.GetLED(54))
                {
                    started = true;
                    break;
                }

                _uc.Wait(50);
                waited += 50;
                if (waited >= CycleStartTimeoutMs) break;
            }

            if (!started)
            {
                // The generous window elapsed with no start/finish signal and the cycle
                // LED never came on: the Cycle Start press did not take. Stop the
                // controller so a delayed start can't run unattended, then surface the error.
                try { _uc.Stop(); } catch { }
                ShowMessage(
                    "Cycle Start did not trigger after loading:\n" + System.IO.Path.GetFileName(fullPath) +
                    "\n\nMake sure the machine is reset (the Reset button is green / not flashing) and try again.",
                    "Run Error", MessageBoxIcon.Warning);
                if (status != null) status("Aborted - cycle start did not trigger.");
                return false;
            }

            if (status != null) status("Running " + System.IO.Path.GetFileName(fullPath));

            if (waitForCycleFinish != null)
            {
                waitForCycleFinish();
            }
            else
            {
                while (_uc.GetLED(54))
                {
                    if (isAborted != null && isAborted()) return false;
                    _uc.Wait(200);
                }
            }

            return true;
        }

        public bool ExecuteAutoOp(string opId, WorkflowStep step, Action<string> status, Func<bool> isAborted)
        {
            if (isAborted != null && isAborted()) return false;

            switch (opId)
            {
                case AutoOpIds.MoveToolChange:
                    return MoveToolChange(status);
                case AutoOpIds.SpindleOff:
                    return SpindleOff(status);
                case AutoOpIds.AutoZero:
                    return ProbeFixedPlate(status);
                case AutoOpIds.GotoWorkZero:
                    return GotoWorkZero(status);
                case AutoOpIds.ParkG28:
                    return Park("G28", status);
                case AutoOpIds.ParkG30:
                    return Park("G30", status);
                case AutoOpIds.ParkCustom:
                    return ParkCustom(status);
                case AutoOpIds.CustomMdi:
                    return RunCustomMdi(step != null ? step.customMdi : "", status);
                case AutoOpIds.ToolPrompt:
                    return true;
                default:
                    if (status != null) status("Unknown auto-op: " + opId);
                    return true;
            }
        }
    }
}
