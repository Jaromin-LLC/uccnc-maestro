using System.Collections.Generic;

namespace Plugins.Companion
{
    /// <summary>
    /// Serializable live status pushed to the phone via GET /api/status and SSE.
    /// Plain data objects (no Forms / Plugininterface dependency) so the standalone
    /// test host can build and emit them too.
    /// </summary>
    public class StatusSnapshot
    {
        public string machineId { get; set; }
        public string machineName { get; set; }
        public bool connected { get; set; }
        public long ts { get; set; }

        public MachineStatus machine { get; set; }
        public MaestroStatus maestro { get; set; }
        public ControllerStatus controller { get; set; }

        public double[] jogStepSizes { get; set; }
        public double jogFeed { get; set; }
        public string cameraUrl { get; set; }

        public StatusSnapshot()
        {
            machine = new MachineStatus();
            maestro = new MaestroStatus();
            controller = new ControllerStatus();
            jogStepSizes = new double[] { 0.01, 0.1, 1, 10 };
            jogFeed = 1500;
            cameraUrl = "";
        }
    }

    public class AxisFlags
    {
        public bool x { get; set; }
        public bool y { get; set; }
        public bool z { get; set; }
        public bool a { get; set; }
    }

    public class AxisPos
    {
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
        public double a { get; set; }
    }

    public class MachineStatus
    {
        public AxisFlags homed { get; set; }
        public bool cycleRunning { get; set; }
        public bool feedHold { get; set; }
        public bool moving { get; set; }
        public bool alarm { get; set; }
        public bool estopped { get; set; }
        public string units { get; set; }
        public AxisPos pos { get; set; }
        public AxisPos machinePos { get; set; }
        public double feedRate { get; set; }
        public double spindleRpm { get; set; }
        public bool spindleOn { get; set; }
        public int feedOverride { get; set; }
        public int spindleOverride { get; set; }
        public int rapidOverride { get; set; }
        public int gcodeLine { get; set; }

        public MachineStatus()
        {
            homed = new AxisFlags();
            units = "mm";
            pos = new AxisPos();
            machinePos = new AxisPos();
            feedOverride = 100;
            spindleOverride = 100;
            rapidOverride = 100;
        }
    }

    public class MaestroStepStatus
    {
        public int index { get; set; }
        public string label { get; set; }
        public string type { get; set; }
        public string toolLabel { get; set; }
        public string status { get; set; }
        public int lastRunSeconds { get; set; }
    }

    public class MaestroStatus
    {
        public bool running { get; set; }
        public string activeProjectId { get; set; }
        public int activeStepIndex { get; set; }
        public string statusText { get; set; }
        public bool promptWaiting { get; set; }
        public string promptText { get; set; }
        public bool promptIsGateOnly { get; set; }
        public string promptPhotoUrl { get; set; }
        public List<MaestroStepStatus> steps { get; set; }
        public int fileCurrentLine { get; set; }
        public int fileTotalLines { get; set; }
        public int estimateSeconds { get; set; }
        public int elapsedSeconds { get; set; }
        public int remainingSeconds { get; set; }

        public MaestroStatus()
        {
            activeProjectId = "";
            activeStepIndex = -1;
            statusText = "Ready";
            promptText = "";
            promptPhotoUrl = "";
            steps = new List<MaestroStepStatus>();
        }
    }

    public class ControllerStatus
    {
        public string heldBy { get; set; }
        public bool youHoldControl { get; set; }

        public ControllerStatus()
        {
            heldBy = "";
        }
    }
}
