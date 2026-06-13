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
        public string mediaRoot { get; set; }
        public ToolChangePos toolChangePos { get; set; }
        public ProbeSettings probe { get; set; }
        public bool testMode { get; set; }

        // Retract to Safe Z before tool-change / probe moves.
        public bool useSafeZForTc { get; set; }

        public MaestroSettings()
        {
            mediaRoot = @"C:\UCCNC\Maestro\Media";
            toolChangePos = new ToolChangePos();
            probe = new ProbeSettings();
            useSafeZForTc = true;
        }
    }

    public class ToolInfo
    {
        public int id { get; set; }
        public int num { get; set; }
        public string type { get; set; }
        public string diameter { get; set; }
        public string desc { get; set; }
        public string image { get; set; }

        // Edge-probe support for tools without a usable center (fly / surfacing cutters).
        // Offsets shift the probe point off the fixed plate XY so a cutting edge lands over
        // the puck; edgeProbePrompt pauses to let the operator rotate a blade into place.
        public double probeXOffset { get; set; }
        public double probeYOffset { get; set; }
        public bool edgeProbePrompt { get; set; }

        public ToolInfo()
        {
            id = 0;
            num = 1;
            type = "";
            diameter = "";
            desc = "";
            image = "";
            probeXOffset = 0;
            probeYOffset = 0;
            edgeProbePrompt = false;
        }

        public string DisplayLabel()
        {
            string label = "T" + num;
            if (!string.IsNullOrEmpty(diameter)) label += " — " + diameter;
            if (!string.IsNullOrEmpty(type)) label += " " + type;
            if (!string.IsNullOrEmpty(desc)) label += " (" + desc + ")";
            return label.Trim();
        }

        public override string ToString()
        {
            return DisplayLabel();
        }
    }

    public class ToolLibraryDocument
    {
        public List<ToolInfo> tools { get; set; }

        public ToolLibraryDocument()
        {
            tools = new List<ToolInfo>();
        }
    }

    public class WorkflowStep
    {
        public string type { get; set; }
        public string label { get; set; }
        public string file { get; set; }
        public int toolId { get; set; }
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
            toolId = 0;
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

        /// <summary>When true, probe / tool-change values on this project replace global defaults.</summary>
        public bool overrideMachineSettings { get; set; }
        public ToolChangePos toolChangePos { get; set; }
        public ProbeSettings probe { get; set; }
        public bool useSafeZForTc { get; set; }

        public WorkflowProject()
        {
            id = "NEW_PROJECT";
            name = "New Project";
            description = "";
            image = "";
            steps = new List<WorkflowStep>();
            toolChangePos = new ToolChangePos();
            probe = new ProbeSettings();
            useSafeZForTc = true;
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
        public static string ToolsFile = Path.Combine(MaestroRoot, "tools.json");
        public static string StateFile = Path.Combine(MaestroRoot, "state.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(MaestroRoot);
            Directory.CreateDirectory(Path.Combine(MaestroRoot, "Media"));
            Directory.CreateDirectory(Path.Combine(MaestroRoot, "Media", "Tools"));
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
                if (project.toolChangePos == null) project.toolChangePos = new ToolChangePos();
                if (project.probe == null) project.probe = new ProbeSettings();
                foreach (var step in project.steps)
                {
                    if (step.toolId < 0) step.toolId = 0;
                    if (string.IsNullOrEmpty(step.instructions) && !string.IsNullOrEmpty(step.notes))
                        step.instructions = step.notes;
                    step.NormalizeType();
                }
            }
        }

        public static ToolLibraryDocument LoadTools(string path)
        {
            if (!File.Exists(path))
            {
                var doc = new ToolLibraryDocument();
                NormalizeToolLibrary(doc);
                return doc;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = Serializer.Deserialize<ToolLibraryDocument>(json) ?? new ToolLibraryDocument();
            NormalizeToolLibrary(loaded);
            return loaded;
        }

        public static void SaveTools(string path, ToolLibraryDocument doc)
        {
            NormalizeToolLibrary(doc);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            string json = Serializer.Serialize(doc);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static ToolLibraryDocument CloneToolLibrary(ToolLibraryDocument source)
        {
            string json = Serializer.Serialize(source);
            var clone = Serializer.Deserialize<ToolLibraryDocument>(json);
            NormalizeToolLibrary(clone);
            return clone;
        }

        public static void NormalizeToolLibrary(ToolLibraryDocument doc)
        {
            if (doc == null) return;
            if (doc.tools == null) doc.tools = new List<ToolInfo>();

            int maxId = 0;
            foreach (var tool in doc.tools)
            {
                if (tool != null && tool.id > maxId) maxId = tool.id;
            }

            foreach (var tool in doc.tools)
            {
                if (tool == null) continue;
                if (tool.id <= 0)
                {
                    maxId++;
                    tool.id = maxId;
                }
                if (tool.type == null) tool.type = "";
                if (tool.diameter == null) tool.diameter = "";
                if (tool.desc == null) tool.desc = "";
                if (tool.image == null) tool.image = "";
            }
        }

        public static int NextToolId(ToolLibraryDocument lib)
        {
            int max = 0;
            if (lib == null || lib.tools == null) return 1;
            foreach (var tool in lib.tools)
            {
                if (tool != null && tool.id > max) max = tool.id;
            }
            return max + 1;
        }

        public static int NextToolNum(ToolLibraryDocument lib)
        {
            int max = 0;
            if (lib == null || lib.tools == null) return 1;
            foreach (var tool in lib.tools)
            {
                if (tool != null && tool.num > max) max = tool.num;
            }
            return max + 1;
        }

        public static ToolInfo FindTool(ToolLibraryDocument lib, int toolId)
        {
            if (lib == null || lib.tools == null || toolId <= 0) return null;
            foreach (var tool in lib.tools)
            {
                if (tool != null && tool.id == toolId)
                    return tool;
            }
            return null;
        }

        public static bool IsToolReferenced(ProjectsDocument projects, int toolId)
        {
            if (projects == null || projects.projects == null || toolId <= 0) return false;
            foreach (var project in projects.projects)
            {
                if (project.steps == null) continue;
                foreach (var step in project.steps)
                {
                    if (step.toolId == toolId)
                        return true;
                }
            }
            return false;
        }

        public static ProjectsDocument CloneDocument(ProjectsDocument source)
        {
            string json = Serializer.Serialize(source);
            var clone = Serializer.Deserialize<ProjectsDocument>(json);
            NormalizeDocument(clone);
            return clone;
        }
        public static MaestroSettings ResolveForProject(MaestroSettings global, WorkflowProject project)
        {
            if (global == null) global = new MaestroSettings();
            if (project == null || !project.overrideMachineSettings)
                return global;

            string json = Serializer.Serialize(global);
            var merged = Serializer.Deserialize<MaestroSettings>(json) ?? new MaestroSettings();
            merged.probe = project.probe ?? new ProbeSettings();
            merged.toolChangePos = project.toolChangePos ?? new ToolChangePos();
            merged.useSafeZForTc = project.useSafeZForTc;
            return merged;
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
