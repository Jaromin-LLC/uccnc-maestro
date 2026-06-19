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
        public const string ParkG28 = "parkG28";
        public const string ParkG30 = "parkG30";
        public const string ParkCustom = "parkCustom";
    }

    public class ToolChangePos
    {
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
        public double zSafe { get; set; }
    }

    /// <summary>
    /// User-defined park location in absolute machine (G53) coordinates. Unlike G28/G30
    /// (which UCCNC ties to the homed machine origin), this is freely adjustable and the
    /// "Park (custom position)" auto-op moves here via a G53 rapid.
    /// </summary>
    public class ParkPos
    {
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
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
        public ParkPos parkPos { get; set; }
        public ProbeSettings probe { get; set; }
        public bool testMode { get; set; }

        // Retract to Safe Z before tool-change / probe moves.
        public bool useSafeZForTc { get; set; }

        public MaestroSettings()
        {
            mediaRoot = @"C:\UCCNC\Maestro\Media";
            toolChangePos = new ToolChangePos();
            parkPos = new ParkPos();
            probe = new ProbeSettings();
            useSafeZForTc = true;
        }
    }

    public class ToolInfo
    {
        public int id { get; set; }

        // Operator-facing label identifying where the tool lives in physical storage
        // (e.g. "Drawer 3", "T7", "Rack A2"). Freeform, editable, and not required to be
        // unique. The unique key is id; the controller tool number is driven by id.
        public string num { get; set; }
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
            num = "";
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
            string detail = SizeDescription();
            if (!string.IsNullOrEmpty(num))
            {
                return string.IsNullOrEmpty(detail) ? num.Trim() : (num + " — " + detail).Trim();
            }
            if (!string.IsNullOrEmpty(detail)) return detail;
            return ("Tool " + id).Trim();
        }

        /// <summary>
        /// Concatenated "diameter type (desc)" without the tool-number prefix, for use
        /// where the tool number is shown separately.
        /// </summary>
        public string SizeDescription()
        {
            string label = "";
            if (!string.IsNullOrEmpty(diameter)) label += diameter;
            if (!string.IsNullOrEmpty(type)) label += (label.Length > 0 ? " " : "") + type;
            if (!string.IsNullOrEmpty(desc)) label += (label.Length > 0 ? " " : "") + "(" + desc + ")";
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

    /// <summary>
    /// One instance of an auto-op in a step's pre/post sequence. The same id may
    /// appear multiple times (e.g. two custom MDI commands with different mdi text).
    /// </summary>
    public class WorkflowOp
    {
        public string id { get; set; }
        public string mdi { get; set; }

        public WorkflowOp()
        {
            id = "";
            mdi = "";
        }

        public WorkflowOp(string opId)
        {
            id = opId ?? "";
            mdi = "";
        }

        public WorkflowOp Clone()
        {
            return new WorkflowOp { id = id ?? "", mdi = mdi ?? "" };
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
        public List<WorkflowOp> preOps { get; set; }
        public List<WorkflowOp> postOps { get; set; }

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
            preOps = new List<WorkflowOp>();
            postOps = new List<WorkflowOp>();

            // Seed sensible defaults for a brand-new op step. This runs only at
            // construction; on JSON load the deserializer overwrites preOps/postOps
            // with the saved arrays (including an intentionally-empty []), so a user
            // who clears all ops keeps them cleared.
            SeedDefaultOps();
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

        /// <summary>
        /// Seeds the default auto-ops for an op step. Intended for brand-new steps only
        /// (called from the constructor) - never call this during load/commit/run, or an
        /// intentionally-empty op list would be silently repopulated.
        /// </summary>
        public void SeedDefaultOps()
        {
            if (IsOp)
            {
                preOps = new List<WorkflowOp>
                {
                    new WorkflowOp(AutoOpIds.MoveToolChange),
                    new WorkflowOp(AutoOpIds.ToolPrompt),
                    new WorkflowOp(AutoOpIds.AutoZero)
                };
                postOps = new List<WorkflowOp>
                {
                    new WorkflowOp(AutoOpIds.SpindleOff),
                    new WorkflowOp(AutoOpIds.MoveToolChange)
                };
            }
            EnsureOpsNotNull();
        }

        /// <summary>Guarantees the op lists are non-null without changing their contents.</summary>
        public void EnsureOpsNotNull()
        {
            if (preOps == null) preOps = new List<WorkflowOp>();
            if (postOps == null) postOps = new List<WorkflowOp>();
        }

        public void NormalizeType()
        {
            if (type == "flip") type = "gate";
            EnsureOpsNotNull();
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
        public ParkPos parkPos { get; set; }
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
            parkPos = new ParkPos();
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
            json = MigrateLegacyOpsJson(json);
            var loaded = Serializer.Deserialize<ProjectsDocument>(json) ?? new ProjectsDocument();
            NormalizeDocument(loaded);
            return loaded;
        }

        /// <summary>
        /// Converts legacy string-array preOps/postOps and step-level customMdi into
        /// the WorkflowOp object format before typed deserialization.
        /// </summary>
        private static string MigrateLegacyOpsJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            object rootObj;
            try { rootObj = Serializer.DeserializeObject(json); }
            catch { return json; }

            var root = rootObj as Dictionary<string, object>;
            if (root == null) return json;

            object projectsObj;
            if (!root.TryGetValue("projects", out projectsObj)) return json;

            var projects = projectsObj as object[];
            if (projects == null) return json;

            bool changed = false;
            foreach (var projectObj in projects)
            {
                var project = projectObj as Dictionary<string, object>;
                if (project == null) continue;

                object stepsObj;
                if (!project.TryGetValue("steps", out stepsObj)) continue;

                var steps = stepsObj as object[];
                if (steps == null) continue;

                foreach (var stepObj in steps)
                {
                    var step = stepObj as Dictionary<string, object>;
                    if (step == null) continue;

                    string legacyMdi = "";
                    object customMdiObj;
                    if (step.TryGetValue("customMdi", out customMdiObj) && customMdiObj != null)
                        legacyMdi = customMdiObj.ToString() ?? "";

                    if (MigrateOpListField(step, "preOps", legacyMdi)) changed = true;
                    if (MigrateOpListField(step, "postOps", "")) changed = true;

                    if (step.ContainsKey("customMdi"))
                    {
                        step.Remove("customMdi");
                        changed = true;
                    }
                }
            }

            return changed ? Serializer.Serialize(rootObj) : json;
        }

        private static bool MigrateOpListField(Dictionary<string, object> step, string fieldName, string legacyMdi)
        {
            object listObj;
            if (!step.TryGetValue(fieldName, out listObj)) return false;

            var rawList = listObj as object[];
            if (rawList == null) return false;

            bool needsMigration = false;
            foreach (var item in rawList)
            {
                if (item is string)
                {
                    needsMigration = true;
                    break;
                }
            }

            if (!needsMigration && string.IsNullOrEmpty(legacyMdi)) return false;

            var migrated = new List<object>();
            bool mdiApplied = false;
            foreach (var item in rawList)
            {
                var opDict = item as Dictionary<string, object>;
                if (opDict != null)
                {
                    migrated.Add(opDict);
                    continue;
                }

                var opId = item as string;
                if (opId == null) continue;

                var entry = new Dictionary<string, object> { { "id", opId }, { "mdi", "" } };
                if (!mdiApplied && opId == AutoOpIds.CustomMdi && !string.IsNullOrEmpty(legacyMdi))
                {
                    entry["mdi"] = legacyMdi;
                    mdiApplied = true;
                }
                migrated.Add(entry);
            }

            step[fieldName] = migrated.ToArray();
            return true;
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
            if (doc.settings.parkPos == null) doc.settings.parkPos = new ParkPos();
            if (doc.projects == null) doc.projects = new List<WorkflowProject>();

            foreach (var project in doc.projects)
            {
                if (project.steps == null) project.steps = new List<WorkflowStep>();
                if (project.toolChangePos == null) project.toolChangePos = new ToolChangePos();
                if (project.parkPos == null) project.parkPos = new ParkPos();
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
                if (tool.num == null) tool.num = "";
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
            merged.parkPos = project.parkPos ?? new ParkPos();
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

            // Run times are intentionally preserved across a reset so they persist
            // between production runs as a reference estimate. GetLastRunSeconds
            // null/range-guards and SetLastRunSeconds grows the list as needed, so a
            // stale-length list left here is harmless.
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
