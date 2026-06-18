using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Plugins
{
    public class AdminView : UserControl
    {
        public event Action DocumentSaved;

        private readonly MaestroForm _host;
        private readonly WorkflowEngine _engine;
        private ProjectsDocument _workingDoc;
        private ToolLibraryDocument _workingTools;

        private readonly ListBox _projectList;
        private readonly ListBox _stepList;
        private ListBox _toolList;
        private TextBox _projectIdBox;
        private TextBox _projectNameBox;
        private TextBox _projectDescBox;
        private TextBox _projectImageBox;
        private PictureBox _projectImagePreview;
        private TextBox _stepLabelBox;
        private ComboBox _stepTypeCombo;
        private TextBox _stepFileBox;
        private TextBox _stepInstructionsBox;
        private ComboBox _stepToolCombo;
        private TextBox _libToolNumBox;
        private TextBox _libToolTypeBox;
        private TextBox _libToolDiaBox;
        private TextBox _libToolDescBox;
        private TextBox _libToolImageBox;
        private PictureBox _libToolImagePreview;
        private TextBox _libToolProbeXBox;
        private TextBox _libToolProbeYBox;
        private CheckBox _libToolEdgeProbeCheck;
        private TextBox _photoBox;
        private TextBox _videoBox;
        private PictureBox _photoPreview;
        private CheckedListBox _preOpsList;
        private CheckedListBox _postOpsList;
        private TextBox _mediaRootBox;
        private CheckBox _testModeBox;
        private readonly ToolTip _toolTips;
        private GroupBox _globalMachineGroup;
        private Panel _globalMachinePanel;
        private MachineSettingsFieldSet _globalMachineFields;
        private GroupBox _projectOverrideGroup;
        private CheckBox _overrideMachineBox;
        private Panel _projectOverridePanel;
        private MachineSettingsFieldSet _projectOverrideFields;
        private SplitContainer _toolsSplit;
        private SplitContainer _stepsSplit;
        private TabControl _editorTabs;
        private Button _saveBtn;
        private Label _saveStatusLabel;
        private bool _dirty;

        private WorkflowProject _selectedProject;
        // The step the editor fields are currently bound to. Editor commits write
        // to this object, so they are immune to list reordering/index changes.
        // Selection itself is tracked solely by _stepList.SelectedIndex - there is
        // no parallel selected-index field to drift out of sync.
        private WorkflowStep _editingStep;
        private ToolInfo _selectedLibraryTool;
        private int _loadDepth;
        // True while the editor is being populated from the model, so user-driven
        // change events (TextChanged, ItemCheck, etc.) are ignored. Backed by a depth
        // counter so it is re-entrant: a nested load (e.g. RefreshStepToolCombo called
        // inside ApplyStepFields) can't clear a parent's guard early.
        private bool _loadingEditor { get { return _loadDepth > 0; } }

        private IDisposable BeginLoad()
        {
            _loadDepth++;
            return new LoadScope(this);
        }

        private sealed class LoadScope : IDisposable
        {
            private AdminView _view;
            public LoadScope(AdminView view) { _view = view; }
            public void Dispose()
            {
                if (_view == null) return;
                if (_view._loadDepth > 0) _view._loadDepth--;
                _view = null;
            }
        }

        private static readonly string[] AutoOpLabels =
        {
            AutoOpIds.MoveToolChange + " | Move to tool change",
            AutoOpIds.ToolPrompt + " | Tool install prompt",
            AutoOpIds.AutoZero + " | Auto zero (probe)",
            AutoOpIds.SpindleOff + " | Spindle off",
            AutoOpIds.GotoWorkZero + " | Go to work zero",
            AutoOpIds.ParkG28 + " | Park (G28)",
            AutoOpIds.ParkG30 + " | Park (G30)",
            AutoOpIds.ParkCustom + " | Park (custom position)",
            AutoOpIds.CustomMdi + " | Custom MDI"
        };

        public AdminView(MaestroForm host, WorkflowEngine engine)
        {
            _host = host;
            _engine = engine;
            _toolTips = new ToolTip { AutoPopDelay = 16000, InitialDelay = 400, ShowAlways = true };

            var adminTabs = new TabControl { Dock = DockStyle.Fill };

            // ---------- Projects sub-tab ----------
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 2)
            };
            var addProjectBtn = MakeIconButton("\uE710", "Add project");
            var dupProjectBtn = MakeIconButton("\uE8C8", "Duplicate project");
            var delProjectBtn = MakeIconButton("\uE74D", "Remove project");
            addProjectBtn.Click += AddProjectBtn_Click;
            dupProjectBtn.Click += DupProjectBtn_Click;
            delProjectBtn.Click += DelProjectBtn_Click;
            _toolTips.SetToolTip(addProjectBtn, "Add project");
            _toolTips.SetToolTip(dupProjectBtn, "Duplicate selected project");
            _toolTips.SetToolTip(delProjectBtn, "Remove selected project");
            leftButtons.Controls.Add(addProjectBtn);
            leftButtons.Controls.Add(dupProjectBtn);
            leftButtons.Controls.Add(delProjectBtn);

            _projectList = new ListBox { Dock = DockStyle.Fill };
            _projectList.SelectedIndexChanged += ProjectList_SelectedIndexChanged;
            leftPanel.Controls.Add(_projectList);
            leftPanel.Controls.Add(new Label { Text = "Projects", Dock = DockStyle.Top, Height = 20 });
            leftPanel.Controls.Add(leftButtons);
            split.Panel1.Controls.Add(leftPanel);

            _stepsSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel1
            };

            var stepPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            var addStepBtn = new Button { Text = "Add Step", Width = 80 };
            var delStepBtn = new Button { Text = "Delete Step", Width = 90 };
            var upStepBtn = new Button { Text = "Up", Width = 50 };
            var downStepBtn = new Button { Text = "Down", Width = 60 };
            addStepBtn.Click += AddStepBtn_Click;
            delStepBtn.Click += DelStepBtn_Click;
            upStepBtn.Click += (s, e) => MoveStep(-1);
            downStepBtn.Click += (s, e) => MoveStep(1);
            stepButtons.Controls.Add(addStepBtn);
            stepButtons.Controls.Add(delStepBtn);
            stepButtons.Controls.Add(upStepBtn);
            stepButtons.Controls.Add(downStepBtn);

            _stepList = new ListBox { Dock = DockStyle.Fill };
            _stepList.SelectedIndexChanged += StepList_SelectedIndexChanged;
            stepPanel.Controls.Add(_stepList);
            stepPanel.Controls.Add(new Label { Text = "Steps", Dock = DockStyle.Top, Height = 20 });
            stepPanel.Controls.Add(stepButtons);
            _stepsSplit.Panel1.Controls.Add(stepPanel);
            _stepsSplit.Panel2.Controls.Add(BuildStepEditorPanel());

            _editorTabs = new TabControl { Dock = DockStyle.Fill };
            var projectTab = new TabPage("Project") { Padding = new Padding(4) };
            projectTab.Controls.Add(BuildProjectSettingsPanel());
            var stepsTab = new TabPage("Steps") { Padding = new Padding(4) };
            stepsTab.Controls.Add(_stepsSplit);
            _editorTabs.TabPages.Add(projectTab);
            _editorTabs.TabPages.Add(stepsTab);
            _editorTabs.SelectedIndexChanged += (s, e) =>
            {
                if (_editorTabs.SelectedTab == stepsTab)
                    SafeSetSplitter(_stepsSplit, 170, 130, 220);
            };

            split.Panel2.Controls.Add(_editorTabs);

            var projectsPage = new TabPage("Projects") { Padding = new Padding(4) };
            projectsPage.Controls.Add(split);

            // ---------- Tools sub-tab ----------
            var toolsPage = new TabPage("Tools") { Padding = new Padding(4) };
            toolsPage.Controls.Add(BuildToolsPanel());

            // ---------- Settings sub-tab ----------
            var settingsPage = new TabPage("Settings") { Padding = new Padding(4) };
            settingsPage.Controls.Add(BuildSettingsPanel());

            adminTabs.TabPages.Add(projectsPage);
            adminTabs.TabPages.Add(toolsPage);
            adminTabs.TabPages.Add(settingsPage);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            _saveBtn = MakeActionButton("Save All", Color.FromArgb(0, 122, 204));
            _saveBtn.Location = new Point(8, 8);
            _saveBtn.Click += SaveBtn_Click;
            _saveStatusLabel = new Label { Location = new Point(120, 12), AutoSize = true, Text = "" };
            bottomPanel.Controls.Add(_saveBtn);
            bottomPanel.Controls.Add(_saveStatusLabel);
            UpdateSaveButtonState();

            Controls.Add(adminTabs);

            Load += (s, e) =>
            {
                SafeSetSplitter(split, 190, 100, 200);
                if (_stepsSplit != null)
                    SafeSetSplitter(_stepsSplit, 170, 130, 220);
                if (_toolsSplit != null)
                    SafeSetSplitter(_toolsSplit, 220, 120, 200);
            };
            Controls.Add(bottomPanel);
        }

        private static Button MakeIconButton(string glyph, string tip)
        {
            var btn = new Button
            {
                Text = glyph,
                Width = 36,
                Height = 30,
                Margin = new Padding(0, 0, 4, 0),
                Font = new Font("Segoe MDL2 Assets", 11f),
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = true
            };
            return btn;
        }

        private Panel BuildProjectSettingsPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            int y = 8;

            var projectGroup = new GroupBox
            {
                Text = "Project Settings",
                Location = new Point(8, y),
                Width = 760,
                Padding = new Padding(8)
            };
            int py = 24;

            projectGroup.Controls.Add(MkLabel("Project ID", 12, py));
            _projectIdBox = MkText(124, py, 220);
            projectGroup.Controls.Add(_projectIdBox); py += 28;
            projectGroup.Controls.Add(MkLabel("Project Name", 12, py));
            _projectNameBox = MkText(124, py, 220);
            projectGroup.Controls.Add(_projectNameBox); py += 28;
            projectGroup.Controls.Add(MkLabel("Description", 12, py));
            _projectDescBox = MkText(124, py, 420, 40, true);
            projectGroup.Controls.Add(_projectDescBox); py += 48;

            _projectImageBox = new TextBox { Visible = false };
            projectGroup.Controls.Add(_projectImageBox);
            projectGroup.Controls.Add(MkLabel("Project Photo", 560, 24));
            _projectImagePreview = MakeImagePicker(new Point(560, 44), "Click to add\nproject photo",
                () => PickImageInto(_projectImageBox, _projectImagePreview));
            _toolTips.SetToolTip(_projectImagePreview, "Optional photo shown on the Operator panel below the run buttons.");
            projectGroup.Controls.Add(_projectImagePreview);
            projectGroup.Height = Math.Max(py, 44 + _projectImagePreview.Height + 12);

            scroll.Controls.Add(projectGroup);
            y = projectGroup.Bottom + 12;

            _projectOverrideGroup = new GroupBox
            {
                Text = "Project-specific machine overrides",
                Location = new Point(8, y),
                Size = new Size(660, 460),
                Padding = new Padding(8)
            };
            _overrideMachineBox = new CheckBox
            {
                Text = "Override global machine settings for this project",
                Location = new Point(12, 20),
                AutoSize = true
            };
            _toolTips.SetToolTip(_overrideMachineBox,
                "Rare: use different probe or tool-change coordinates for this project only. When unchecked, the global defaults from the Settings tab apply.");
            _overrideMachineBox.CheckedChanged += OverrideMachineBox_CheckedChanged;
            _projectOverrideGroup.Controls.Add(_overrideMachineBox);

            _projectOverridePanel = new Panel
            {
                Location = new Point(8, 48),
                Size = new Size(640, 400),
                AutoScroll = true,
                Visible = false
            };
            int oy = 8;
            _projectOverrideFields = MachineSettingsFieldSet.Create(_projectOverridePanel, ref oy, _toolTips);
            _projectOverrideGroup.Controls.Add(_projectOverridePanel);
            scroll.Controls.Add(_projectOverrideGroup);

            _projectOverrideFields.HookChanged(ProjectMachineSettingsChanged);
            _projectIdBox.TextChanged += EditorChanged;
            _projectNameBox.TextChanged += EditorChanged;
            _projectDescBox.TextChanged += EditorChanged;

            return scroll;
        }

        private Panel BuildStepEditorPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            int y = 8;

            var stepGroup = new GroupBox
            {
                Text = "Step",
                Location = new Point(8, y),
                Width = 760,
                Padding = new Padding(8)
            };
            int sy = 24;

            stepGroup.Controls.Add(MkLabel("Step Label", 12, sy));
            _stepLabelBox = MkText(124, sy, 320);
            stepGroup.Controls.Add(_stepLabelBox); sy += 28;
            stepGroup.Controls.Add(MkLabel("Step Type", 12, sy));
            _stepTypeCombo = new ComboBox { Location = new Point(124, sy), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _stepTypeCombo.Items.AddRange(new object[] { "op", "gate" });
            stepGroup.Controls.Add(_stepTypeCombo); sy += 28;
            stepGroup.Controls.Add(MkLabel("G-code File", 12, sy));
            _stepFileBox = MkText(124, sy, 320);
            stepGroup.Controls.Add(_stepFileBox);
            _toolTips.SetToolTip(_stepFileBox, "Full path to the G-code file run for this step. The file can live anywhere (e.g. your CAM output folder).");
            var browseGcodeBtn = new Button { Text = "Browse...", Location = new Point(454, sy - 2), Width = 80 };
            browseGcodeBtn.Click += BrowseGcodeBtn_Click;
            stepGroup.Controls.Add(browseGcodeBtn); sy += 28;

            stepGroup.Controls.Add(MkLabel("Instructions", 12, sy));
            _stepInstructionsBox = MkText(124, sy, 420, 70, true);
            stepGroup.Controls.Add(_stepInstructionsBox); sy += 84;

            stepGroup.Controls.Add(MkLabel("Tool", 12, sy));
            _stepToolCombo = new ComboBox
            {
                Location = new Point(124, sy),
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _stepToolCombo.SelectedIndexChanged += StepToolCombo_SelectedIndexChanged;
            stepGroup.Controls.Add(_stepToolCombo);
            var newToolBtn = new Button { Text = "New Tool...", Location = new Point(454, sy - 2), Width = 90 };
            newToolBtn.Click += NewToolFromStepBtn_Click;
            stepGroup.Controls.Add(newToolBtn);
            sy += 36;

            _photoBox = new TextBox();
            stepGroup.Controls.Add(MkLabel("Step Photo", 560, 24));
            _photoPreview = MakeImagePicker(new Point(560, 44), "Click to add\nstep photo",
                () => PickImageInto(_photoBox, _photoPreview));
            stepGroup.Controls.Add(_photoPreview);

            stepGroup.Controls.Add(MkLabel("Video", 12, sy));
            _videoBox = MkText(124, sy, 320);
            stepGroup.Controls.Add(_videoBox);
            var browseVideoBtn = new Button { Text = "Pick Video...", Location = new Point(454, sy - 2), Width = 90 };
            browseVideoBtn.Click += BrowseVideoBtn_Click;
            stepGroup.Controls.Add(browseVideoBtn);
            var playVideoBtn = new Button { Text = "Play", Location = new Point(550, sy - 2), Width = 60 };
            playVideoBtn.Click += PlayVideoBtn_Click;
            _toolTips.SetToolTip(playVideoBtn, "Open the selected video in your default player to verify it.");
            stepGroup.Controls.Add(playVideoBtn);
            sy += 34;

            stepGroup.Controls.Add(MkLabel("Pre Ops", 12, sy));
            _preOpsList = new CheckedListBox { Location = new Point(124, sy), Size = new Size(220, 140) };
            stepGroup.Controls.Add(_preOpsList);
            stepGroup.Controls.Add(MkLabel("Post Ops", 354, sy));
            _postOpsList = new CheckedListBox { Location = new Point(434, sy), Size = new Size(220, 140) };
            stepGroup.Controls.Add(_postOpsList);
            stepGroup.Height = Math.Max(sy + _postOpsList.Height + 12, 44 + _photoPreview.Height + 12);

            foreach (var label in AutoOpLabels)
            {
                _preOpsList.Items.Add(label);
                _postOpsList.Items.Add(label);
            }

            scroll.Controls.Add(stepGroup);

            _stepTypeCombo.SelectedIndexChanged += (s, e) => EditorChanged(s, e);
            HookEditorChanges();

            return scroll;
        }

        private Panel BuildToolsPanel()
        {
            var wrap = new Panel { Dock = DockStyle.Fill };
            _toolsSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };
            var split = _toolsSplit;

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 2)
            };
            var addToolBtn = MakeIconButton("\uE710", "Add tool");
            var dupToolBtn = MakeIconButton("\uE8C8", "Duplicate tool");
            var delToolBtn = MakeIconButton("\uE74D", "Remove tool");
            addToolBtn.Click += AddToolBtn_Click;
            dupToolBtn.Click += DupToolBtn_Click;
            delToolBtn.Click += DelToolBtn_Click;
            _toolTips.SetToolTip(addToolBtn, "Add tool");
            _toolTips.SetToolTip(dupToolBtn, "Duplicate selected tool");
            _toolTips.SetToolTip(delToolBtn, "Remove selected tool");
            leftButtons.Controls.Add(addToolBtn);
            leftButtons.Controls.Add(dupToolBtn);
            leftButtons.Controls.Add(delToolBtn);

            _toolList = new ListBox { Dock = DockStyle.Fill };
            _toolList.SelectedIndexChanged += ToolList_SelectedIndexChanged;
            leftPanel.Controls.Add(_toolList);
            leftPanel.Controls.Add(new Label { Text = "Tool Library", Dock = DockStyle.Top, Height = 20 });
            leftPanel.Controls.Add(leftButtons);
            split.Panel1.Controls.Add(leftPanel);

            var editor = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            int y = 8;
            var libToolNumLabel = MkLabel("Storage label", 8, y);
            _toolTips.SetToolTip(libToolNumLabel, "Freeform label identifying where this tool lives in physical storage (e.g. \"Drawer 3\", \"T7\", \"Rack A2\"). Shown to the operator; need not be unique.");
            editor.Controls.Add(libToolNumLabel);
            _libToolNumBox = MkText(120, y, 320);
            _toolTips.SetToolTip(_libToolNumBox, "Freeform label identifying where this tool lives in physical storage (e.g. \"Drawer 3\", \"T7\", \"Rack A2\"). Shown to the operator; need not be unique.");
            editor.Controls.Add(_libToolNumBox); y += 30;
            editor.Controls.Add(MkLabel("Type", 8, y));
            _libToolTypeBox = MkText(120, y, 320);
            editor.Controls.Add(_libToolTypeBox); y += 30;
            editor.Controls.Add(MkLabel("Diameter", 8, y));
            _libToolDiaBox = MkText(120, y, 320);
            editor.Controls.Add(_libToolDiaBox); y += 30;
            editor.Controls.Add(MkLabel("Description", 8, y));
            _libToolDescBox = MkText(120, y, 320);
            editor.Controls.Add(_libToolDescBox); y += 34;

            _libToolImageBox = new TextBox { Visible = false };
            editor.Controls.Add(_libToolImageBox);
            editor.Controls.Add(MkLabel("Tool Image", 8, y));
            _libToolImagePreview = MakeImagePicker(new Point(120, y), "Click to add\ntool image",
                PickLibraryToolImage);
            editor.Controls.Add(_libToolImagePreview);
            y += _libToolImagePreview.Height + 8;

            var probeXLabel = MkLabel("Probe X offset", 8, y);
            _toolTips.SetToolTip(probeXLabel, "Shifts the probe point off the fixed plate X so a cutting edge lands over the puck (edge probe). 0 = probe at center.");
            editor.Controls.Add(probeXLabel);
            _libToolProbeXBox = MkText(120, y, 120);
            editor.Controls.Add(_libToolProbeXBox); y += 30;

            var probeYLabel = MkLabel("Probe Y offset", 8, y);
            _toolTips.SetToolTip(probeYLabel, "Shifts the probe point off the fixed plate Y so a cutting edge lands over the puck (edge probe). 0 = probe at center.");
            editor.Controls.Add(probeYLabel);
            _libToolProbeYBox = MkText(120, y, 120);
            editor.Controls.Add(_libToolProbeYBox); y += 30;

            _libToolEdgeProbeCheck = new CheckBox
            {
                Text = "Prompt to rotate spindle before probing (edge probe)",
                Location = new Point(8, y),
                AutoSize = true
            };
            _toolTips.SetToolTip(_libToolEdgeProbeCheck, "For tools with no usable center (fly / surfacing cutters): pauses before probing so the operator can rotate a cutting edge over the plate.");
            editor.Controls.Add(_libToolEdgeProbeCheck); y += 32;

            HookLibraryToolChanges();
            split.Panel2.Controls.Add(editor);

            wrap.Controls.Add(split);
            return wrap;
        }

        private Panel BuildSettingsPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            int y = 8;

            var pathsGroup = new GroupBox
            {
                Text = "Folders & mode",
                Location = new Point(8, y),
                Size = new Size(660, 110),
                Padding = new Padding(8)
            };
            int py = 24;
            pathsGroup.Controls.Add(MkLabel("Media Root", 12, py));
            _mediaRootBox = MkText(120, py, 420);
            pathsGroup.Controls.Add(_mediaRootBox);
            _toolTips.SetToolTip(_mediaRootBox, "Base folder for photos, videos, and tool images copied from the Admin editor.");
            py += 30;
            _testModeBox = new CheckBox { Text = "Test mode (skip probing checks)", Location = new Point(120, py), AutoSize = true };
            _toolTips.SetToolTip(_testModeBox, "Skips physical probing and machine moves for demonstration. Never enable on a cutting machine.");
            pathsGroup.Controls.Add(_testModeBox);
            scroll.Controls.Add(pathsGroup);
            y += pathsGroup.Height + 12;

            _globalMachineGroup = new GroupBox
            {
                Text = "Global machine settings (defaults for all projects)",
                Location = new Point(8, y),
                Size = new Size(660, 430),
                Padding = new Padding(8)
            };
            _globalMachinePanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int gy = 8;
            _globalMachineFields = MachineSettingsFieldSet.Create(_globalMachinePanel, ref gy, _toolTips);
            _globalMachineGroup.Controls.Add(_globalMachinePanel);
            scroll.Controls.Add(_globalMachineGroup);
            y += _globalMachineGroup.Height + 12;

            _globalMachineFields.HookChanged(SettingsChanged);
            _mediaRootBox.TextChanged += SettingsChanged;
            _testModeBox.CheckedChanged += SettingsChanged;

            return scroll;
        }

        private void HookEditorChanges()
        {
            _stepLabelBox.TextChanged += EditorChanged;
            _stepFileBox.TextChanged += EditorChanged;
            _stepInstructionsBox.TextChanged += EditorChanged;
            _photoBox.TextChanged += EditorChanged;
            _videoBox.TextChanged += EditorChanged;
            _preOpsList.ItemCheck += (s, e) => OnOpsItemCheck(_preOpsList, e, true);
            _postOpsList.ItemCheck += (s, e) => OnOpsItemCheck(_postOpsList, e, false);
        }

        // Applies a pre/post-op checkbox change immediately to the currently selected
        // step. ItemCheck fires BEFORE the control's checked state is updated, so the
        // changed item's value is read from e.NewValue rather than GetItemChecked - this
        // avoids the old deferred BeginInvoke, whose write could land on a different step
        // if the selection changed before it ran.
        private void OnOpsItemCheck(CheckedListBox list, ItemCheckEventArgs e, bool isPreOps)
        {
            if (_loadingEditor) return;
            if (_editingStep == null) return;

            var ops = BuildOpsList(list, e.Index, e.NewValue == CheckState.Checked);
            if (isPreOps) _editingStep.preOps = ops;
            else _editingStep.postOps = ops;
            MarkDirty();
        }

        private static List<string> BuildOpsList(CheckedListBox list, int changedIndex, bool changedValue)
        {
            var result = new List<string>();
            for (int i = 0; i < list.Items.Count; i++)
            {
                bool isChecked = (i == changedIndex) ? changedValue : list.GetItemChecked(i);
                if (isChecked) result.Add(AutoOpLabels[i].Split('|')[0].Trim());
            }
            return result;
        }

        private void OverrideMachineBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            if (_overrideMachineBox.Checked && _selectedProject != null)
                SeedProjectOverrideFromGlobal();
            SaveProjectMachineSettings();
            UpdateMachineSettingsVisibility();
            MarkDirty();
        }

        private void ProjectMachineSettingsChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            SaveProjectMachineSettings();
            MarkDirty();
        }

        private void SeedProjectOverrideFromGlobal()
        {
            if (_workingDoc == null || _workingDoc.settings == null || _selectedProject == null) return;
            var s = _workingDoc.settings;
            if (s.probe == null) s.probe = new ProbeSettings();
            if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();
            if (s.parkPos == null) s.parkPos = new ParkPos();
            if (_selectedProject.probe == null) _selectedProject.probe = new ProbeSettings();
            if (_selectedProject.toolChangePos == null) _selectedProject.toolChangePos = new ToolChangePos();
            if (_selectedProject.parkPos == null) _selectedProject.parkPos = new ParkPos();

            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(s.probe);
            _selectedProject.probe = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ProbeSettings>(json);
            json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(s.toolChangePos);
            _selectedProject.toolChangePos = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ToolChangePos>(json);
            json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(s.parkPos);
            _selectedProject.parkPos = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ParkPos>(json);
            _selectedProject.useSafeZForTc = s.useSafeZForTc;

            using (BeginLoad())
            {
                _projectOverrideFields.LoadFrom(_selectedProject.probe, _selectedProject.toolChangePos, _selectedProject.parkPos, _selectedProject.useSafeZForTc);
            }
        }

        private void UpdateMachineSettingsVisibility()
        {
            bool hasProject = _selectedProject != null;

            _globalMachineGroup.Visible = true;
            _globalMachineGroup.Enabled = true;

            // Project overrides stay visible whenever a project is selected.
            _projectOverrideGroup.Visible = hasProject;
            _projectOverrideGroup.Enabled = true;
            _overrideMachineBox.Enabled = hasProject;

            bool showOverride = hasProject && _overrideMachineBox.Checked;
            _projectOverridePanel.Visible = showOverride;
            _projectOverrideFields.SetEnabled(showOverride);
        }

        private void SaveProjectMachineSettings()
        {
            if (_selectedProject == null) return;
            _selectedProject.overrideMachineSettings = _overrideMachineBox.Checked;
            if (_selectedProject.probe == null) _selectedProject.probe = new ProbeSettings();
            if (_selectedProject.toolChangePos == null) _selectedProject.toolChangePos = new ToolChangePos();
            if (_selectedProject.parkPos == null) _selectedProject.parkPos = new ParkPos();
            bool useSafeZ = _selectedProject.useSafeZForTc;
            if (_overrideMachineBox.Checked)
                _projectOverrideFields.SaveTo(_selectedProject.probe, _selectedProject.toolChangePos, _selectedProject.parkPos, ref useSafeZ);
            _selectedProject.useSafeZForTc = useSafeZ;
        }

        private void LoadProjectMachineFields()
        {
            if (_selectedProject == null)
            {
                _overrideMachineBox.Checked = false;
                UpdateMachineSettingsVisibility();
                return;
            }

            if (_selectedProject.probe == null) _selectedProject.probe = new ProbeSettings();
            if (_selectedProject.toolChangePos == null) _selectedProject.toolChangePos = new ToolChangePos();
            if (_selectedProject.parkPos == null) _selectedProject.parkPos = new ParkPos();

            using (BeginLoad())
            {
                _overrideMachineBox.Checked = _selectedProject.overrideMachineSettings;
                _projectOverrideFields.LoadFrom(_selectedProject.probe, _selectedProject.toolChangePos, _selectedProject.parkPos, _selectedProject.useSafeZForTc);
            }

            UpdateMachineSettingsVisibility();
        }

        public void LoadDocument(ProjectsDocument doc)
        {
            _workingDoc = JsonStore.CloneDocument(doc);
            _workingTools = JsonStore.CloneToolLibrary(_engine.ToolLibrary);
            RefreshProjectList();
            RefreshToolList();
            LoadSettingsFields();
            SetSaved();
        }

        private void RefreshProjectList()
        {
            int keepIndex = _projectList.SelectedIndex;
            _projectList.SelectedIndexChanged -= ProjectList_SelectedIndexChanged;
            try
            {
                _projectList.Items.Clear();
                if (_workingDoc == null || _workingDoc.projects == null) return;
                foreach (var p in _workingDoc.projects)
                    _projectList.Items.Add(p);

                if (_projectList.Items.Count == 0) return;

                if (keepIndex < 0 || keepIndex >= _projectList.Items.Count)
                    keepIndex = 0;
                _projectList.SelectedIndex = keepIndex;
            }
            finally
            {
                _projectList.SelectedIndexChanged += ProjectList_SelectedIndexChanged;
            }

            if (_projectList.SelectedIndex >= 0)
                LoadSelectedProject();
            else
            {
                _selectedProject = null;
                _editingStep = null;
                ClearProjectFields();
                ClearStepFields();
            }
        }

        private void LoadSelectedProject()
        {
            CommitEditorToModel();
            _selectedProject = _projectList.SelectedItem as WorkflowProject;
            ApplyProjectFields();
            RefreshStepList(0);
        }

        // Rebuilds the step list from the model and selects desiredIndex (clamped),
        // then loads that step into the editor. The SelectedIndexChanged handler is
        // detached so this programmatic rebuild doesn't trigger a redundant commit.
        private void RefreshStepList(int desiredIndex)
        {
            _stepList.SelectedIndexChanged -= StepList_SelectedIndexChanged;
            try
            {
                _stepList.Items.Clear();
                if (_selectedProject == null || _selectedProject.steps == null ||
                    _selectedProject.steps.Count == 0)
                {
                    _editingStep = null;
                    ClearStepFields();
                    return;
                }

                for (int i = 0; i < _selectedProject.steps.Count; i++)
                {
                    var step = _selectedProject.steps[i];
                    _stepList.Items.Add((i + 1) + ". [" + step.type + "] " + step.label);
                }

                if (desiredIndex < 0 || desiredIndex >= _stepList.Items.Count)
                    desiredIndex = 0;

                _stepList.SelectedIndex = desiredIndex;
                LoadStep(desiredIndex);
            }
            finally
            {
                _stepList.SelectedIndexChanged += StepList_SelectedIndexChanged;
            }
        }

        private void ProjectList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            LoadSelectedProject();
        }

        private void StepList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;

            // Commit the step the editor was bound to (_editingStep) BEFORE loading
            // the newly selected one. Because the commit targets the object, not an
            // index, it lands on the right step even though _stepList.SelectedIndex
            // has already advanced to the new row.
            CommitEditorToModel();
            LoadStep(_stepList.SelectedIndex);
        }

        private void CommitEditorToModel()
        {
            if (_selectedProject == null) return;
            ApplyEditorToStep();
        }

        private void ApplyProjectFields()
        {
            if (_selectedProject == null) return;
            using (BeginLoad())
            {
                _projectIdBox.Text = _selectedProject.id ?? "";
                _projectNameBox.Text = _selectedProject.name ?? "";
                _projectDescBox.Text = _selectedProject.description ?? "";
                _projectImageBox.Text = _selectedProject.image ?? "";
                LoadImagePreview(_projectImagePreview, _selectedProject.image);
            }
            LoadProjectMachineFields();
        }

        private void ClearProjectFields()
        {
            using (BeginLoad())
            {
                _projectIdBox.Text = "";
                _projectNameBox.Text = "";
                _projectDescBox.Text = "";
                _projectImageBox.Text = "";
                if (_projectImagePreview != null)
                {
                    if (_projectImagePreview.Image != null)
                    {
                        var old = _projectImagePreview.Image;
                        _projectImagePreview.Image = null;
                        old.Dispose();
                    }
                }
                _overrideMachineBox.Checked = false;
                UpdateMachineSettingsVisibility();
            }
        }

        // Binds the editor to the step at the given list index and populates the
        // fields. A negative/out-of-range index clears the editor and unbinds.
        private void LoadStep(int index)
        {
            if (_selectedProject == null || _selectedProject.steps == null ||
                index < 0 || index >= _selectedProject.steps.Count)
            {
                _editingStep = null;
                ClearStepFields();
                return;
            }

            _editingStep = _selectedProject.steps[index];
            ApplyStepFields(_editingStep);
        }

        private void ApplyStepFields(WorkflowStep step)
        {
            if (step == null)
            {
                ClearStepFields();
                return;
            }

            step.NormalizeType();
            if (step.preOps == null) step.preOps = new List<string>();
            if (step.postOps == null) step.postOps = new List<string>();

            using (BeginLoad())
            {
                _stepLabelBox.Text = step.label ?? "";
                _stepTypeCombo.SelectedItem = step.IsGate ? "gate" : "op";
                if (_stepTypeCombo.SelectedIndex < 0)
                    _stepTypeCombo.SelectedIndex = 0;
                _stepFileBox.Text = step.file ?? "";
                _stepInstructionsBox.Text = step.DisplayInstructions;
                RefreshStepToolCombo(step.toolId);
                _photoBox.Text = step.photo ?? "";
                _videoBox.Text = step.video ?? "";
                SetCheckedOps(_preOpsList, step.preOps);
                SetCheckedOps(_postOpsList, step.postOps);
                LoadPhotoPreview(step.photo);
            }
        }

        private void ClearStepFields()
        {
            using (BeginLoad())
            {
                _stepLabelBox.Text = "";
                _stepTypeCombo.SelectedIndex = 0;
                _stepFileBox.Text = "";
                _stepInstructionsBox.Text = "";
                RefreshStepToolCombo(0);
                _photoBox.Text = "";
                _videoBox.Text = "";
                _photoPreview.Image = null;
                ClearCheckedOps(_preOpsList);
                ClearCheckedOps(_postOpsList);
            }
        }

        private void LoadSettingsFields()
        {
            if (_workingDoc == null || _workingDoc.settings == null) return;
            using (BeginLoad())
            {
                var s = _workingDoc.settings;
                if (s.probe == null) s.probe = new ProbeSettings();
                if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();
                if (s.parkPos == null) s.parkPos = new ParkPos();

                _mediaRootBox.Text = s.mediaRoot ?? "";
                _testModeBox.Checked = s.testMode;
                _globalMachineFields.LoadFrom(s.probe, s.toolChangePos, s.parkPos, s.useSafeZForTc);
            }
            UpdateMachineSettingsVisibility();
        }

        private void EditorChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            ApplyEditorToStep();
            MarkDirty();
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            if (_workingDoc == null) return;
            MarkDirty();
            if (_workingDoc.settings == null) _workingDoc.settings = new MaestroSettings();
            var s = _workingDoc.settings;
            if (s.probe == null) s.probe = new ProbeSettings();
            if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();
            if (s.parkPos == null) s.parkPos = new ParkPos();

            s.mediaRoot = _mediaRootBox.Text.Trim();
            s.testMode = _testModeBox.Checked;

            bool useSafeZ = s.useSafeZForTc;
            _globalMachineFields.SaveTo(s.probe, s.toolChangePos, s.parkPos, ref useSafeZ);
            s.useSafeZForTc = useSafeZ;
        }

        private void ApplyEditorToStep()
        {
            if (_selectedProject != null)
            {
                _selectedProject.id = _projectIdBox.Text.Trim();
                _selectedProject.name = _projectNameBox.Text.Trim();
                _selectedProject.description = _projectDescBox.Text.Trim();
                _selectedProject.image = _projectImageBox.Text.Trim();
            }

            var step = _editingStep;
            if (step == null) return;

            step.label = _stepLabelBox.Text.Trim();
            step.type = _stepTypeCombo.SelectedItem != null ? _stepTypeCombo.SelectedItem.ToString() : "op";
            step.file = _stepFileBox.Text.Trim();
            step.instructions = _stepInstructionsBox.Text.Trim();
            step.notes = step.instructions;
            step.toolId = GetSelectedStepToolId();
            step.photo = _photoBox.Text.Trim();
            step.video = _videoBox.Text.Trim();
            step.preOps = GetCheckedOps(_preOpsList);
            step.postOps = GetCheckedOps(_postOpsList);
            step.NormalizeType();

            UpdateStepListLabel(step);
        }

        // Refreshes the list row that represents the given step. The row is found by
        // the step's actual position in the model (IndexOf), so the label always
        // tracks the correct step regardless of where the visual selection is.
        private void UpdateStepListLabel(WorkflowStep step)
        {
            if (step == null || _selectedProject == null || _selectedProject.steps == null)
                return;

            int index = _selectedProject.steps.IndexOf(step);
            if (index < 0 || index >= _stepList.Items.Count) return;

            string listText = (index + 1) + ". [" + step.type + "] " + step.label;
            if (string.Equals(_stepList.Items[index], listText)) return;

            // Reassigning a ListBox item does a native remove+re-insert, which drops
            // and restores the selection and re-fires SelectedIndexChanged. That
            // handler would reload the editor and reset the textbox caret to 0,
            // making fields type backwards. Detach while updating, and restore the
            // visual selection in case the reassign moved it.
            _stepList.SelectedIndexChanged -= StepList_SelectedIndexChanged;
            try
            {
                int keepSelection = _stepList.SelectedIndex;
                _stepList.Items[index] = listText;
                if (_stepList.SelectedIndex != keepSelection &&
                    keepSelection >= 0 && keepSelection < _stepList.Items.Count)
                    _stepList.SelectedIndex = keepSelection;
            }
            finally { _stepList.SelectedIndexChanged += StepList_SelectedIndexChanged; }
        }

        private static List<string> GetCheckedOps(CheckedListBox list)
        {
            var result = new List<string>();
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.GetItemChecked(i))
                    result.Add(AutoOpLabels[i].Split('|')[0].Trim());
            }
            return result;
        }

        private static void SetCheckedOps(CheckedListBox list, List<string> ids)
        {
            if (ids == null) ids = new List<string>();
            for (int i = 0; i < list.Items.Count; i++)
            {
                string id = AutoOpLabels[i].Split('|')[0].Trim();
                list.SetItemChecked(i, ids.Contains(id));
            }
        }

        private static void ClearCheckedOps(CheckedListBox list)
        {
            for (int i = 0; i < list.Items.Count; i++)
                list.SetItemChecked(i, false);
        }

        private void AddProjectBtn_Click(object sender, EventArgs e)
        {
            CommitEditorToModel();
            var p = new WorkflowProject
            {
                id = "NEW_PROJECT_" + DateTime.Now.ToString("HHmmss"),
                name = "New Project",
                steps = new List<WorkflowStep> { new WorkflowStep() }
            };
            _workingDoc.projects.Add(p);
            RefreshProjectList();
            _projectList.SelectedItem = p;
            MarkDirty();
        }

        private void DupProjectBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null) return;
            CommitEditorToModel();
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(_selectedProject);
            var clone = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<WorkflowProject>(json);
            clone.id = clone.id + "_COPY";
            clone.name = clone.name + " (Copy)";
            _workingDoc.projects.Add(clone);
            RefreshProjectList();
            _projectList.SelectedItem = clone;
            MarkDirty();
        }

        private void DelProjectBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null) return;
            if (MessageBox.Show(_host, "Remove project \"" + _selectedProject.name + "\"?\n\nThis cannot be undone until you save.",
                    "Remove Project", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _workingDoc.projects.Remove(_selectedProject);
            _selectedProject = null;
            RefreshProjectList();
            MarkDirty();
        }

        private void AddStepBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null) return;
            CommitEditorToModel();
            _selectedProject.steps.Add(new WorkflowStep());
            RefreshStepList(_selectedProject.steps.Count - 1);
            MarkDirty();
        }

        private void DelStepBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null) return;
            int index = _stepList.SelectedIndex;
            if (index < 0) return;
            CommitEditorToModel();
            _selectedProject.steps.RemoveAt(index);
            int next = index;
            if (next >= _selectedProject.steps.Count)
                next = _selectedProject.steps.Count - 1;
            RefreshStepList(next);
            MarkDirty();
        }

        private void MoveStep(int delta)
        {
            if (_selectedProject == null) return;
            int index = _stepList.SelectedIndex;
            if (index < 0) return;
            CommitEditorToModel();
            int newIndex = index + delta;
            if (newIndex < 0 || newIndex >= _selectedProject.steps.Count) return;
            var item = _selectedProject.steps[index];
            _selectedProject.steps.RemoveAt(index);
            _selectedProject.steps.Insert(newIndex, item);
            RefreshStepList(newIndex);
            MarkDirty();
        }

        private class ToolComboEntry
        {
            public int ToolId;
            public string Label;
            public override string ToString() { return Label; }
        }

        private string GetMediaRoot()
        {
            if (_workingDoc != null && _workingDoc.settings != null &&
                !string.IsNullOrEmpty(_workingDoc.settings.mediaRoot))
                return _workingDoc.settings.mediaRoot.Trim();
            return MaestroPaths.MaestroRoot + "\\Media";
        }

        private void RefreshStepToolCombo(int selectedToolId)
        {
            using (BeginLoad())
            {
                _stepToolCombo.Items.Clear();
                _stepToolCombo.Items.Add(new ToolComboEntry { ToolId = 0, Label = "(no tool)" });
                if (_workingTools != null && _workingTools.tools != null)
                {
                    foreach (var tool in _workingTools.tools)
                    {
                        if (tool == null) continue;
                        _stepToolCombo.Items.Add(new ToolComboEntry { ToolId = tool.id, Label = tool.DisplayLabel() });
                    }
                }

                int idx = 0;
                for (int i = 0; i < _stepToolCombo.Items.Count; i++)
                {
                    var entry = _stepToolCombo.Items[i] as ToolComboEntry;
                    if (entry != null && entry.ToolId == selectedToolId)
                    {
                        idx = i;
                        break;
                    }
                }
                _stepToolCombo.SelectedIndex = _stepToolCombo.Items.Count > 0 ? idx : -1;
            }
        }

        private int GetSelectedStepToolId()
        {
            var entry = _stepToolCombo.SelectedItem as ToolComboEntry;
            return entry != null ? entry.ToolId : 0;
        }

        private void StepToolCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            ApplyEditorToStep();
            MarkDirty();
        }

        private void NewToolFromStepBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new NewToolDialog(_host, GetMediaRoot()))
            {
                if (dlg.ShowDialog(_host) != DialogResult.OK || dlg.Result == null) return;
                if (_workingTools == null) _workingTools = new ToolLibraryDocument();
                if (_workingTools.tools == null) _workingTools.tools = new List<ToolInfo>();
                AssignToolId(dlg.Result);
                _workingTools.tools.Add(dlg.Result);
                RefreshToolList();
                RefreshStepToolCombo(dlg.Result.id);
                ApplyEditorToStep();
                MarkDirty();
            }
        }

        private void AssignToolId(ToolInfo tool)
        {
            if (tool == null) return;
            if (tool.id <= 0)
                tool.id = JsonStore.NextToolId(_workingTools);
        }

        private void RefreshToolList()
        {
            int keep = _toolList.SelectedIndex;
            _toolList.SelectedIndexChanged -= ToolList_SelectedIndexChanged;
            try
            {
                _toolList.Items.Clear();
                if (_workingTools == null || _workingTools.tools == null) return;
                foreach (var tool in _workingTools.tools)
                {
                    if (tool != null) _toolList.Items.Add(tool);
                }
                if (_toolList.Items.Count == 0) return;
                if (keep < 0 || keep >= _toolList.Items.Count) keep = 0;
                _toolList.SelectedIndex = keep;
            }
            finally
            {
                _toolList.SelectedIndexChanged += ToolList_SelectedIndexChanged;
            }

            if (_toolList.SelectedIndex >= 0)
                ApplyLibraryToolFields();
            else
                ClearLibraryToolFields();

            RefreshStepToolComboPreserving();
        }

        private void RefreshStepToolComboPreserving()
        {
            if (_stepToolCombo == null) return;
            RefreshStepToolCombo(GetSelectedStepToolId());
        }

        private void ToolList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            CommitLibraryToolToModel();
            _selectedLibraryTool = _toolList.SelectedItem as ToolInfo;
            ApplyLibraryToolFields();
        }

        private void ApplyLibraryToolFields()
        {
            _selectedLibraryTool = _toolList.SelectedItem as ToolInfo;
            if (_selectedLibraryTool == null)
            {
                ClearLibraryToolFields();
                return;
            }

            using (BeginLoad())
            {
                _libToolNumBox.Text = _selectedLibraryTool.num ?? "";
                _libToolTypeBox.Text = _selectedLibraryTool.type ?? "";
                _libToolDiaBox.Text = _selectedLibraryTool.diameter ?? "";
                _libToolDescBox.Text = _selectedLibraryTool.desc ?? "";
                _libToolImageBox.Text = _selectedLibraryTool.image ?? "";
                _libToolProbeXBox.Text = _selectedLibraryTool.probeXOffset.ToString(CultureInfo.InvariantCulture);
                _libToolProbeYBox.Text = _selectedLibraryTool.probeYOffset.ToString(CultureInfo.InvariantCulture);
                _libToolEdgeProbeCheck.Checked = _selectedLibraryTool.edgeProbePrompt;
                LoadImagePreview(_libToolImagePreview, _selectedLibraryTool.image);
            }
        }

        private void ClearLibraryToolFields()
        {
            using (BeginLoad())
            {
                _libToolNumBox.Text = "";
                _libToolTypeBox.Text = "";
                _libToolDiaBox.Text = "";
                _libToolDescBox.Text = "";
                _libToolImageBox.Text = "";
                _libToolProbeXBox.Text = "";
                _libToolProbeYBox.Text = "";
                _libToolEdgeProbeCheck.Checked = false;
                if (_libToolImagePreview.Image != null) { var old = _libToolImagePreview.Image; _libToolImagePreview.Image = null; old.Dispose(); }
            }
        }

        private void CommitLibraryToolToModel()
        {
            if (_selectedLibraryTool == null) return;
            _selectedLibraryTool.num = _libToolNumBox.Text.Trim();
            _selectedLibraryTool.type = _libToolTypeBox.Text.Trim();
            _selectedLibraryTool.diameter = _libToolDiaBox.Text.Trim();
            _selectedLibraryTool.desc = _libToolDescBox.Text.Trim();
            _selectedLibraryTool.image = _libToolImageBox.Text.Trim();
            _selectedLibraryTool.probeXOffset = ParseToolOffset(_libToolProbeXBox.Text);
            _selectedLibraryTool.probeYOffset = ParseToolOffset(_libToolProbeYBox.Text);
            _selectedLibraryTool.edgeProbePrompt = _libToolEdgeProbeCheck.Checked;
        }

        private static double ParseToolOffset(string text)
        {
            double value;
            if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
            return 0;
        }

        private void HookLibraryToolChanges()
        {
            _libToolNumBox.TextChanged += LibraryToolChanged;
            _libToolTypeBox.TextChanged += LibraryToolChanged;
            _libToolDiaBox.TextChanged += LibraryToolChanged;
            _libToolDescBox.TextChanged += LibraryToolChanged;
            _libToolProbeXBox.TextChanged += LibraryToolChanged;
            _libToolProbeYBox.TextChanged += LibraryToolChanged;
            _libToolEdgeProbeCheck.CheckedChanged += LibraryToolChanged;
        }

        private void LibraryToolChanged(object sender, EventArgs e)
        {
            if (_loadingEditor || _selectedLibraryTool == null) return;
            CommitLibraryToolToModel();
            int idx = _toolList.SelectedIndex;
            if (idx >= 0 && idx < _toolList.Items.Count)
            {
                using (BeginLoad())
                {
                    _toolList.Items[idx] = _selectedLibraryTool;
                    _toolList.SelectedIndex = idx;
                }
            }
            RefreshStepToolComboPreserving();
            MarkDirty();
        }

        private void PickLibraryToolImage()
        {
            if (_selectedLibraryTool == null)
            {
                MessageBox.Show(_host, "Select a tool first.", "Maestro",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CopyToolImageFile(_libToolImageBox);
            _selectedLibraryTool.image = _libToolImageBox.Text.Trim();
            LoadImagePreview(_libToolImagePreview, _libToolImageBox.Text);
            MarkDirty();
        }

        private void CopyToolImageFile(TextBox targetBox)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*";
                if (dlg.ShowDialog(_host) != DialogResult.OK) return;

                string mediaRoot = GetMediaRoot();
                string toolsDir = Path.Combine(mediaRoot, "Tools");
                Directory.CreateDirectory(toolsDir);
                string destName = Path.GetFileName(dlg.FileName);
                string destPath = Path.Combine(toolsDir, destName);
                File.Copy(dlg.FileName, destPath, true);
                targetBox.Text = "Tools\\" + destName;
            }
        }

        private void AddToolBtn_Click(object sender, EventArgs e)
        {
            CommitLibraryToolToModel();
            if (_workingTools == null) _workingTools = new ToolLibraryDocument();
            if (_workingTools.tools == null) _workingTools.tools = new List<ToolInfo>();
            var tool = new ToolInfo
            {
                num = "",
                type = "New tool",
                diameter = "",
                desc = ""
            };
            AssignToolId(tool);
            _workingTools.tools.Add(tool);
            RefreshToolList();
            _toolList.SelectedItem = tool;
            MarkDirty();
        }

        private void DupToolBtn_Click(object sender, EventArgs e)
        {
            CommitLibraryToolToModel();
            var source = _toolList.SelectedItem as ToolInfo;
            if (source == null) return;

            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(source);
            var copy = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ToolInfo>(json);
            copy.id = 0;
            if (_workingTools.tools == null) _workingTools.tools = new List<ToolInfo>();
            AssignToolId(copy);
            _workingTools.tools.Add(copy);
            RefreshToolList();
            _toolList.SelectedItem = copy;
            MarkDirty();
        }

        private void DelToolBtn_Click(object sender, EventArgs e)
        {
            var tool = _toolList.SelectedItem as ToolInfo;
            if (tool == null) return;

            if (JsonStore.IsToolReferenced(_workingDoc, tool.id))
            {
                MessageBox.Show(_host,
                    "This tool is used by one or more project steps. Remove it from those steps first.",
                    "Remove Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(_host, "Remove tool \"" + tool.DisplayLabel() + "\" from the library?",
                    "Remove Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _workingTools.tools.Remove(tool);
            _selectedLibraryTool = null;
            RefreshToolList();
            MarkDirty();
        }

        private void BrowseGcodeBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                string current = _stepFileBox.Text.Trim();
                try
                {
                    string dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
                catch { }
                dlg.Filter = "G-code files|*.nc;*.tap;*.ngc;*.cnc|All files|*.*";
                if (dlg.ShowDialog(_host) != DialogResult.OK) return;
                _stepFileBox.Text = dlg.FileName;
                ApplyEditorToStep();
            }
        }

        private void BrowseVideoBtn_Click(object sender, EventArgs e)
        {
            CopyMediaFile(_videoBox, new[] { "mp4", "avi", "wmv", "mov", "mkv" });
        }

        private void PlayVideoBtn_Click(object sender, EventArgs e)
        {
            string rel = _videoBox.Text.Trim();
            if (string.IsNullOrEmpty(rel))
            {
                MessageBox.Show(_host, "No video set for this step.", "Video",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = Path.IsPathRooted(rel) ? rel : Path.Combine(GetMediaRoot(), rel);
            if (!File.Exists(path))
            {
                MessageBox.Show(_host, "Video not found:\n" + path, "Video",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try { System.Diagnostics.Process.Start(path); }
            catch (Exception ex)
            {
                MessageBox.Show(_host, "Could not open video:\n" + ex.Message, "Video",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PickImageInto(TextBox backing, PictureBox preview)
        {
            if (_selectedProject == null)
            {
                MessageBox.Show(_host, "Select a project first.", "Maestro",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CopyMediaFile(backing, new[] { "jpg", "jpeg", "png", "bmp", "gif" });
            LoadImagePreview(preview, backing.Text);
            MarkDirty();
        }

        private PictureBox MakeImagePicker(Point location, string emptyText, Action onPick)
        {
            var box = new PictureBox
            {
                Location = location,
                Size = new Size(180, 140),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(248, 248, 248),
                Cursor = Cursors.Hand
            };
            box.Paint += (s, e) =>
            {
                if (box.Image == null)
                    TextRenderer.DrawText(e.Graphics, emptyText, box.Font, box.ClientRectangle,
                        Color.Gray,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            };
            box.Click += (s, e) => onPick();

            var replaceBtn = new Button
            {
                Text = "\uE70F",
                Font = new Font("Segoe MDL2 Assets", 9f),
                Size = new Size(26, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            replaceBtn.FlatAppearance.BorderColor = Color.Silver;
            replaceBtn.Location = new Point(box.Width - replaceBtn.Width - 2, 2);
            replaceBtn.Click += (s, e) => onPick();
            _toolTips.SetToolTip(replaceBtn, "Replace image");
            box.Controls.Add(replaceBtn);
            return box;
        }

        private void CopyMediaFile(TextBox targetBox, string[] extensions)
        {
            if (_selectedProject == null) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Media files|*." + string.Join(";*.", extensions) + "|All files|*.*";
                if (dlg.ShowDialog(_host) != DialogResult.OK) return;

                string mediaRoot = _mediaRootBox.Text.Trim();
                if (string.IsNullOrEmpty(mediaRoot)) mediaRoot = MaestroPaths.MaestroRoot + "\\Media";
                string projectDir = Path.Combine(mediaRoot, _selectedProject.id);
                Directory.CreateDirectory(projectDir);

                string destName = Path.GetFileName(dlg.FileName);
                string destPath = Path.Combine(projectDir, destName);
                File.Copy(dlg.FileName, destPath, true);
                targetBox.Text = _selectedProject.id + "\\" + destName;
                ApplyEditorToStep();
            }
        }

        private void LoadPhotoPreview(string relativePath)
        {
            LoadImagePreview(_photoPreview, relativePath);
        }

        private void LoadImagePreview(PictureBox target, string relativePath)
        {
            if (target.Image != null) { var old = target.Image; target.Image = null; old.Dispose(); }
            if (string.IsNullOrEmpty(relativePath)) return;
            string mediaRoot = _mediaRootBox.Text.Trim();
            if (string.IsNullOrEmpty(mediaRoot)) mediaRoot = MaestroPaths.MaestroRoot + "\\Media";
            string path = Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(mediaRoot, relativePath);
            target.Image = ImageUtil.LoadOriented(path);
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            ApplyEditorToStep();
            CommitLibraryToolToModel();
            SaveProjectMachineSettings();
            SettingsChanged(null, null);
            JsonStore.SaveProjects(MaestroPaths.ProjectsFile, _workingDoc);
            JsonStore.SaveTools(MaestroPaths.ToolsFile, _workingTools);
            _engine.ReloadDocument();
            _saveStatusLabel.Text = "Saved " + DateTime.Now.ToString("HH:mm:ss");
            SetSaved();
            if (DocumentSaved != null) DocumentSaved();
        }

        private void MarkDirty()
        {
            if (_loadingEditor) return;
            _dirty = true;
            UpdateSaveButtonState();
        }

        private void SetSaved()
        {
            _dirty = false;
            UpdateSaveButtonState();
        }

        private void UpdateSaveButtonState()
        {
            if (_saveBtn == null) return;
            _saveBtn.Enabled = _dirty;
            _saveBtn.BackColor = _dirty ? Color.FromArgb(0, 122, 204) : Color.FromArgb(200, 200, 200);
            _saveBtn.ForeColor = _dirty ? Color.White : Color.FromArgb(120, 120, 120);
        }

        private static void SafeSetSplitter(SplitContainer split, int desired, int panel1Min, int panel2Min)
        {
            int span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (span < panel1Min + panel2Min + split.SplitterWidth + 10) return;
            try
            {
                split.Panel1MinSize = panel1Min;
                split.Panel2MinSize = panel2Min;
                int max = span - panel2Min - split.SplitterWidth;
                int value = Math.Max(panel1Min, Math.Min(desired, max));
                split.SplitterDistance = value;
            }
            catch { }
        }

        private static Label MkLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        }

        private static TextBox MkText(int x, int y, int width, int height = 22, bool multiline = false)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Width = width,
                Height = height,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };
        }

        private static Button MakeActionButton(string text, Color backColor)
        {
            return new Button
            {
                Text = text,
                Width = 100,
                Height = 28,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
        }
    }
}
