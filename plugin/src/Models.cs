using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Plugins
{
    public static class AutoOpIds
    {
        public const string MoveToolChange = "moveToolChange";
        public const string ToolPrompt = "toolPrompt";
        public const string AutoZero = "autoZero";
        public const string SpindleOff = "spindleOff";
        public const string GotoWorkZero = "gotoWorkZero";
        public const string CustomMdi = "customMdi";
    }

    public class ToolChangePos
    {
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
        public double zSafe { get; set; }
    }

    public class ProbeSettings
    {
        public double xPlate { get; set; }
        public double yPlate { get; set; }
        public double dist { get; set; }
        public double feedFast { get; set; }
        public double feedSlow { get; set; }
        public double plateThickness { get; set; }
        public double retractDist { get; set; }
        public double plateRapidZ { get; set; }
        public double plateZero { get; set; }
        public bool plateRapid { get; set; }
    }

    public class MaestroSettings
    {
        public string gcodeRoot { get; set; }
        public string mediaRoot { get; set; }
        public ToolChangePos toolChangePos { get; set; }
        public ProbeSettings probe { get; set; }
        public bool testMode { get; set; }

        // When true, probe / tool-change values are read from the UCCNC screenset's
        // probing-page fields (e.g. a screenset that exposes them). When false
        // (default) Maestro is fully self-contained and uses the values below, so
        // no particular screenset is required.
        public bool useMachineTcFields { get; set; }

        // Retract to Safe Z before tool-change / probe moves. Used when
        // useMachineTcFields is false.
        public bool useSafeZForTc { get; set; }

        public MaestroSettings()
        {
            gcodeRoot = @"C:\UCCNC\Maestro\GCode";
            mediaRoot = @"C:\UCCNC\Maestro\Media";
            toolChangePos = new ToolChangePos();
            probe = new ProbeSettings();
            useMachineTcFields = false;
            useSafeZForTc = true;
        }
    }

    public class ToolInfo
    {
        public int num { get; set; }
        public string type { get; set; }
        public string diameter { get; set; }
        public string desc { get; set; }
        public int rpm { get; set; }
        public string image { get; set; }

        public ToolInfo()
        {
            num = 1;
            type = "";
            diameter = "";
            desc = "";
            rpm = 18000;
            image = "";
        }
    }

    public class WorkflowStep
    {
        public string type { get; set; }
        public string label { get; set; }
        public string file { get; set; }
        public ToolInfo tool { get; set; }
        public int minutes { get; set; }
        public string instructions { get; set; }
        public string notes { get; set; }
        public string photo { get; set; }
        public string video { get; set; }
        public List<string> preOps { get; set; }
        public List<string> postOps { get; set; }
        public string customMdi { get; set; }

        public WorkflowStep()
        {
            type = "op";
            label = "New Step";
            file = "";
            tool = new ToolInfo();
            instructions = "";
            notes = "";
            photo = "";
            video = "";
            preOps = new List<string>();
            postOps = new List<string>();
            customMdi = "";
        }

        public bool IsGate
        {
            get { return type == "gate" || type == "flip"; }
        }

        public bool IsOp
        {
            get { return type == "op"; }
        }

        public string DisplayInstructions
        {
            get
            {
                if (!string.IsNullOrEmpty(instructions)) return instructions;
                return notes ?? "";
            }
        }

        public void EnsureDefaultOps()
        {
            if (IsOp && (preOps == null || preOps.Count == 0))
            {
                preOps = new List<string>
                {
                    AutoOpIds.MoveToolChange,
                    AutoOpIds.ToolPrompt,
                    AutoOpIds.AutoZero
                };
            }
            if (IsOp && (postOps == null || postOps.Count == 0))
            {
                postOps = new List<string>
                {
                    AutoOpIds.SpindleOff,
                    AutoOpIds.MoveToolChange
                };
            }
            if (preOps == null) preOps = new List<string>();
            if (postOps == null) postOps = new List<string>();
        }

        public void NormalizeType()
        {
            if (type == "flip") type = "gate";
            EnsureDefaultOps();
        }
    }

    public class WorkflowProject
    {
        public string id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string image { get; set; }
        public List<WorkflowStep> steps { get; set; }

        public WorkflowProject()
        {
            id = "NEW_PROJECT";
            name = "New Project";
            description = "";
            image = "";
            steps = new List<WorkflowStep>();
        }

        public override string ToString()
        {
            return name ?? id ?? "Project";
        }
    }

    public class ProjectsDocument
    {
        public MaestroSettings settings { get; set; }
        public List<WorkflowProject> projects { get; set; }

        public ProjectsDocument()
        {
            settings = new MaestroSettings();
            projects = new List<WorkflowProject>();
        }
    }

    public class ProjectRunState
    {
        public string lastProjectId { get; set; }
        public Dictionary<string, List<bool>> done { get; set; }
        public Dictionary<string, List<int>> lastRunSeconds { get; set; }

        public ProjectRunState()
        {
            lastProjectId = "";
            done = new Dictionary<string, List<bool>>();
            lastRunSeconds = new Dictionary<string, List<int>>();
        }
    }

    public enum StepRunStatus
    {
        Pending = 0,
        Running = 1,
        Done = 2,
        Stopped = 3
    }

    public static class MaestroPaths
    {
        public static string MaestroRoot = @"C:\UCCNC\Maestro";
        public static string ProjectsFile = Path.Combine(MaestroRoot, "projects.json");
        public static string StateFile = Path.Combine(MaestroRoot, "state.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(MaestroRoot);
            Directory.CreateDirectory(Path.Combine(MaestroRoot, "GCode"));
            Directory.CreateDirectory(Path.Combine(MaestroRoot, "Media"));
        }
    }

    public static class JsonStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 64
        };

        public static ProjectsDocument LoadProjects(string path)
        {
            if (!File.Exists(path))
            {
                var doc = new ProjectsDocument();
                NormalizeDocument(doc);
                return doc;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = Serializer.Deserialize<ProjectsDocument>(json) ?? new ProjectsDocument();
            NormalizeDocument(loaded);
            return loaded;
        }

        public static void SaveProjects(string path, ProjectsDocument doc)
        {
            NormalizeDocument(doc);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            string json = Serializer.Serialize(doc);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static ProjectRunState LoadState(string path)
        {
            if (!File.Exists(path)) return new ProjectRunState();
            string json = File.ReadAllText(path, Encoding.UTF8);
            return Serializer.Deserialize<ProjectRunState>(json) ?? new ProjectRunState();
        }

        public static void SaveState(string path, ProjectRunState state)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            string json = Serializer.Serialize(state);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static void NormalizeDocument(ProjectsDocument doc)
        {
            if (doc.settings == null) doc.settings = new MaestroSettings();
            if (doc.projects == null) doc.projects = new List<WorkflowProject>();

            foreach (var project in doc.projects)
            {
                if (project.steps == null) project.steps = new List<WorkflowStep>();
                foreach (var step in project.steps)
                {
                    if (step.tool == null) step.tool = new ToolInfo();
                    if (string.IsNullOrEmpty(step.instructions) && !string.IsNullOrEmpty(step.notes))
                        step.instructions = step.notes;
                    step.NormalizeType();
                }
            }
        }

        public static ProjectsDocument CloneDocument(ProjectsDocument source)
        {
            string json = Serializer.Serialize(source);
            var clone = Serializer.Deserialize<ProjectsDocument>(json);
            NormalizeDocument(clone);
            return clone;
        }
    }

    public static class RunStateStore
    {
        public static bool IsDone(ProjectRunState state, string projectId, int stepIndex)
        {
            if (state.done == null || !state.done.ContainsKey(projectId)) return false;
            var flags = state.done[projectId];
            return stepIndex >= 0 && stepIndex < flags.Count && flags[stepIndex];
        }

        public static void SetDone(ProjectRunState state, string projectId, int stepIndex, bool value)
        {
            if (state.done == null) state.done = new Dictionary<string, List<bool>>();
            if (!state.done.ContainsKey(projectId))
                state.done[projectId] = new List<bool>();

            var flags = state.done[projectId];
            while (flags.Count <= stepIndex) flags.Add(false);
            flags[stepIndex] = value;
        }

        public static void ClearProject(ProjectRunState state, string projectId, int stepCount)
        {
            if (state.done == null) state.done = new Dictionary<string, List<bool>>();
            state.done[projectId] = new List<bool>();
            for (int i = 0; i < stepCount; i++) state.done[projectId].Add(false);

            if (state.lastRunSeconds == null) state.lastRunSeconds = new Dictionary<string, List<int>>();
            state.lastRunSeconds[projectId] = new List<int>();
            for (int i = 0; i < stepCount; i++) state.lastRunSeconds[projectId].Add(0);
        }

        public static int GetLastRunSeconds(ProjectRunState state, string projectId, int stepIndex)
        {
            if (state.lastRunSeconds == null || !state.lastRunSeconds.ContainsKey(projectId)) return 0;
            var times = state.lastRunSeconds[projectId];
            if (stepIndex < 0 || stepIndex >= times.Count) return 0;
            return times[stepIndex];
        }

        public static void SetLastRunSeconds(ProjectRunState state, string projectId, int stepIndex, int seconds)
        {
            if (state.lastRunSeconds == null) state.lastRunSeconds = new Dictionary<string, List<int>>();
            if (!state.lastRunSeconds.ContainsKey(projectId))
                state.lastRunSeconds[projectId] = new List<int>();

            var times = state.lastRunSeconds[projectId];
            while (times.Count <= stepIndex) times.Add(0);
            times[stepIndex] = Math.Max(0, seconds);
        }

        public static int FirstNotDone(ProjectRunState state, string projectId, int stepCount)
        {
            for (int i = 0; i < stepCount; i++)
            {
                if (!IsDone(state, projectId, i)) return i;
            }
            return -1;
        }
    }
}
