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

        public MachineOps(Plugininterface.Entry uc, MaestroSettings settings, Form owner, Action<Action> uiInvoke)
        {
            _uc = uc;
            _settings = settings ?? new MaestroSettings();
            _owner = owner;
            _uiInvoke = uiInvoke;
        }

        public bool TestMode
        {
            get { return _settings != null && _settings.testMode; }
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

        // When true, source probe / tool-change values from the UCCNC screenset's
        // probing-page fields. When false, Maestro uses its own stored settings and
        // no particular screenset is required.
        private bool UseFields
        {
            get { return _settings != null && _settings.useMachineTcFields; }
        }

        private ProbeSettings Probe
        {
            get { return _settings.probe ?? new ProbeSettings(); }
        }

        private ToolChangePos Tc
        {
            get { return _settings.toolChangePos ?? new ToolChangePos(); }
        }

        // Prefer a non-zero screenset field value (only when enabled), otherwise the
        // configured plugin setting.
        private double Cfg(int fieldId, double settingValue)
        {
            if (UseFields)
            {
                double val = _uc.Getfielddouble(true, fieldId);
                if (val != 0) return val;
            }
            return settingValue;
        }

        private bool TcSafeZEnabled()
        {
            if (UseFields) return _uc.Getcheckboxstate(true, 20330);
            return _settings.useSafeZForTc;
        }

        private double GetSafeZ()
        {
            return Cfg(20300, Tc.zSafe);
        }

        private double GetTcX()
        {
            return Cfg(20314, Tc.x);
        }

        private double GetTcY()
        {
            return Cfg(20315, Tc.y);
        }

        private double GetTcZ()
        {
            return Cfg(20316, Tc.z);
        }

        public bool Preflight(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE ACTIVE - configuration check and probing are skipped.");
            }

            if (!TestMode)
            {
                double cfgXPlate = Cfg(20302, Probe.xPlate);
                double cfgYPlate = Cfg(20303, Probe.yPlate);
                double cfgProbeDist = Cfg(20305, Probe.dist);
                double cfgProbeFeed = Cfg(20309, Probe.feedFast);

                if ((cfgXPlate == 0 && cfgYPlate == 0) || cfgProbeDist == 0 || cfgProbeFeed == 0)
                {
                    string where = UseFields
                        ? "on the UCCNC probing settings page"
                        : "in the Admin tab under Probing / Tool Change";
                    ShowMessage(
                        "Tool setter probing is not configured.\n\n" +
                        "Set the Fixed Plate X/Y location, probe distance and probe feedrates " + where + " (one-time machine setup), then try again.",
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

        public bool ProbeFixedPlate(Action<string> status)
        {
            if (TestMode)
            {
                if (status != null) status("TEST MODE - probing skipped.");
                return true;
            }

            bool plateRapid = UseFields ? _uc.Getcheckboxstate(true, 20684) : Probe.plateRapid;
            bool tcSafeZ = TcSafeZEnabled();
            double safeZ = GetSafeZ();

            double plateZero = Cfg(20327, Probe.plateZero);
            double xPlate = Cfg(20302, Probe.xPlate);
            double yPlate = Cfg(20303, Probe.yPlate);
            double probeDist = Cfg(20305, Probe.dist);
            double retractDist = Cfg(20306, Probe.retractDist);
            double firstProbeFeed = Cfg(20309, Probe.feedFast);
            double secProbeFeed = Cfg(20310, Probe.feedSlow);
            double plateRapidZ = Cfg(20312, Probe.plateRapidZ);

            string feedOr = _uc.Getfield(true, 232);
            double currentFeedOr = Convert.ToDouble(feedOr.Replace("%", string.Empty), CultureInfo.InvariantCulture);
            double feedFactor = 100 / currentFeedOr;
            firstProbeFeed = firstProbeFeed * feedFactor;
            secProbeFeed = secProbeFeed * feedFactor;

            if (tcSafeZ)
            {
                _uc.Codesync("G53 G0 Z" + Dbl2Str(safeZ));
                WaitForIdle();
            }

            _uc.Codesync("G53 G0 X" + Dbl2Str(xPlate) + " Y" + Dbl2Str(yPlate));
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

            _uc.Setfield(true, plateZero, 228);
            _uc.Validatefield(true, 228);
            _uc.Wait(250);
            if (status != null) status("Tool zeroed on fixed plate.");
            return true;
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
            _uc.Setcurrenttool(toolNum);
            if (UseFields)
            {
                _uc.Setfieldtext(true, toolNum.ToString(), 20326);
                _uc.Validatefield(true, 20326);
            }
            if (status != null) status("Tool set to T" + toolNum);
            return true;
        }

        public bool LoadAndRunFile(string fullPath, Action<string> status, Func<bool> isAborted, Action waitForCycleFinish,
            Action resetCycleFinished, Func<bool> cycleFinished)
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

            if (resetCycleFinished != null) resetCycleFinished();

            if (status != null) status("Cycle start...");
            _uc.Callbutton(128);

            int waited = 0;
            while (!_uc.GetLED(54))
            {
                if (isAborted != null && isAborted()) return false;
                if (cycleFinished != null && cycleFinished())
                {
                    if (status != null) status("Run finished.");
                    return true;
                }
                _uc.Wait(50);
                waited += 50;
                if (waited >= 5000)
                {
                    ShowMessage(
                        "Cycle Start did not trigger after loading:\n" + System.IO.Path.GetFileName(fullPath) +
                        "\n\nMake sure the machine is reset (the Reset button is green / not flashing) and try again.",
                        "Run Error", MessageBoxIcon.Warning);
                    if (status != null) status("Aborted - cycle start did not trigger.");
                    return false;
                }
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
