using System;
using System.Collections.Generic;
using System.Drawing;
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
        private TextBox _stepLabelBox;
        private ComboBox _stepTypeCombo;
        private TextBox _stepFileBox;
        private TextBox _stepInstructionsBox;
        private ComboBox _stepToolCombo;
        private PictureBox _stepToolPreview;
        private Label _libToolNumLabel;
        private TextBox _libToolTypeBox;
        private TextBox _libToolDiaBox;
        private TextBox _libToolDescBox;
        private TextBox _libToolImageBox;
        private PictureBox _libToolImagePreview;
        private TextBox _photoBox;
        private TextBox _videoBox;
        private PictureBox _photoPreview;
        private CheckedListBox _preOpsList;
        private CheckedListBox _postOpsList;
        private TextBox _mediaRootBox;
        private CheckBox _testModeBox;
        private CheckBox _useMachineTcBox;
        private readonly ToolTip _toolTips;
        private GroupBox _globalMachineGroup;
        private Panel _globalMachinePanel;
        private MachineSettingsFieldSet _globalMachineFields;
        private GroupBox _projectOverrideGroup;
        private CheckBox _overrideMachineBox;
        private Panel _projectOverridePanel;
        private MachineSettingsFieldSet _projectOverrideFields;
        private SplitContainer _toolsSplit;
        private Label _saveStatusLabel;

        private WorkflowProject _selectedProject;
        private int _selectedStepIndex = -1;
        private ToolInfo _selectedLibraryTool;
        private bool _loadingEditor;

        private static readonly string[] AutoOpLabels =
        {
            AutoOpIds.MoveToolChange + " | Move to tool change",
            AutoOpIds.ToolPrompt + " | Tool install prompt",
            AutoOpIds.AutoZero + " | Auto zero (probe)",
            AutoOpIds.SpindleOff + " | Spindle off",
            AutoOpIds.GotoWorkZero + " | Go to work zero",
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

            var rightSplit = new SplitContainer
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
            rightSplit.Panel1.Controls.Add(stepPanel);

            var editorPanel = BuildEditorPanel();
            rightSplit.Panel2.Controls.Add(editorPanel);
            split.Panel2.Controls.Add(rightSplit);

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
            var saveBtn = MakeActionButton("Save All", Color.FromArgb(0, 122, 204));
            saveBtn.Location = new Point(8, 8);
            saveBtn.Click += SaveBtn_Click;
            _saveStatusLabel = new Label { Location = new Point(120, 12), AutoSize = true, Text = "" };
            bottomPanel.Controls.Add(saveBtn);
            bottomPanel.Controls.Add(_saveStatusLabel);

            Controls.Add(adminTabs);

            Load += (s, e) =>
            {
                SafeSetSplitter(split, 190, 100, 200);
                SafeSetSplitter(rightSplit, 200, 80, 80);
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

        private Panel BuildEditorPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            int y = 8;

            scroll.Controls.Add(MkLabel("Project ID", 8, y));
            _projectIdBox = MkText(120, 8, 220); y += 28;
            scroll.Controls.Add(_projectIdBox);
            scroll.Controls.Add(MkLabel("Project Name", 8, y));
            _projectNameBox = MkText(120, y, 220); y += 28;
            scroll.Controls.Add(_projectNameBox);
            scroll.Controls.Add(MkLabel("Description", 8, y));
            _projectDescBox = MkText(120, y, 420, 40, true); y += 48;
            scroll.Controls.Add(_projectDescBox);

            scroll.Controls.Add(MkLabel("Step Label", 8, y));
            _stepLabelBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_stepLabelBox);
            scroll.Controls.Add(MkLabel("Step Type", 8, y));
            _stepTypeCombo = new ComboBox { Location = new Point(120, y), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _stepTypeCombo.Items.AddRange(new object[] { "op", "gate" });
            scroll.Controls.Add(_stepTypeCombo); y += 28;
            scroll.Controls.Add(MkLabel("G-code File", 8, y));
            _stepFileBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_stepFileBox);
            _toolTips.SetToolTip(_stepFileBox, "Full path to the G-code file run for this step (e.g. C:\\UCCNC\\Maestro\\GCode\\ProjectA\\op1.nc).");
            var browseGcodeBtn = new Button { Text = "Browse...", Location = new Point(450, y - 28), Width = 80 };
            browseGcodeBtn.Click += BrowseGcodeBtn_Click;
            scroll.Controls.Add(browseGcodeBtn);

            scroll.Controls.Add(MkLabel("Instructions", 8, y));
            _stepInstructionsBox = MkText(120, y, 420, 70, true); y += 84;
            scroll.Controls.Add(_stepInstructionsBox);

            scroll.Controls.Add(MkLabel("Tool", 8, y));
            _stepToolCombo = new ComboBox
            {
                Location = new Point(120, y),
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _stepToolCombo.SelectedIndexChanged += StepToolCombo_SelectedIndexChanged;
            scroll.Controls.Add(_stepToolCombo);
            var newToolBtn = new Button { Text = "New Tool...", Location = new Point(450, y - 2), Width = 90 };
            newToolBtn.Click += NewToolFromStepBtn_Click;
            scroll.Controls.Add(newToolBtn);
            _stepToolPreview = new PictureBox
            {
                Location = new Point(550, y - 2),
                Size = new Size(100, 80),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(248, 248, 248)
            };
            scroll.Controls.Add(_stepToolPreview);
            y += 88;

            _photoBox = new TextBox();

            scroll.Controls.Add(MkLabel("Step Photo", 8, y));
            y += 20;
            _photoPreview = MakeImagePicker(new Point(8, y), "Click to add\nstep photo",
                () => PickImageInto(_photoBox, _photoPreview));
            scroll.Controls.Add(_photoPreview);
            y += _photoPreview.Height + 10;

            scroll.Controls.Add(MkLabel("Video", 8, y));
            _videoBox = MkText(120, y, 320);
            scroll.Controls.Add(_videoBox);
            var browseVideoBtn = new Button { Text = "Pick Video...", Location = new Point(450, y - 2), Width = 90 };
            browseVideoBtn.Click += BrowseVideoBtn_Click;
            scroll.Controls.Add(browseVideoBtn);
            y += 34;

            scroll.Controls.Add(MkLabel("Pre Ops", 8, y));
            _preOpsList = new CheckedListBox { Location = new Point(120, y), Size = new Size(220, 100) };
            scroll.Controls.Add(_preOpsList);
            scroll.Controls.Add(MkLabel("Post Ops", 350, y));
            _postOpsList = new CheckedListBox { Location = new Point(430, y), Size = new Size(220, 100) };
            scroll.Controls.Add(_postOpsList); y += 116;

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
            y += _projectOverrideGroup.Height + 12;

            foreach (var label in AutoOpLabels)
            {
                _preOpsList.Items.Add(label);
                _postOpsList.Items.Add(label);
            }

            _stepTypeCombo.SelectedIndexChanged += (s, e) => EditorChanged(s, e);
            _projectOverrideFields.HookChanged(ProjectMachineSettingsChanged);
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
            editor.Controls.Add(MkLabel("Tool #", 8, y));
            _libToolNumLabel = new Label
            {
                Location = new Point(120, y + 2),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _toolTips.SetToolTip(_libToolNumLabel, "Auto-assigned tool number (next available). Not editable.");
            editor.Controls.Add(_libToolNumLabel); y += 30;
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

            _useMachineTcBox = new CheckBox
            {
                Text = "Use UCCNC screenset probing / tool-change fields",
                Location = new Point(12, y),
                AutoSize = true,
                Checked = false
            };
            _toolTips.SetToolTip(_useMachineTcBox,
                "When enabled, Maestro reads probe and tool-change values from your UCCNC screenset (Probing page) instead of the settings below. " +
                "Use this only if your screenset exposes those fields.");
            scroll.Controls.Add(_useMachineTcBox); y += 30;

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

            _useMachineTcBox.CheckedChanged += (s, e) => { SettingsChanged(s, e); UpdateMachineSettingsVisibility(); };
            _globalMachineFields.HookChanged(SettingsChanged);
            _mediaRootBox.TextChanged += SettingsChanged;
            _testModeBox.CheckedChanged += SettingsChanged;

            return scroll;
        }

        private void HookEditorChanges()
        {
            _projectIdBox.TextChanged += EditorChanged;
            _projectNameBox.TextChanged += EditorChanged;
            _projectDescBox.TextChanged += EditorChanged;
            _stepLabelBox.TextChanged += EditorChanged;
            _stepFileBox.TextChanged += EditorChanged;
            _stepInstructionsBox.TextChanged += EditorChanged;
            _photoBox.TextChanged += EditorChanged;
            _videoBox.TextChanged += EditorChanged;
            _preOpsList.ItemCheck += (s, e) =>
            {
                if (_loadingEditor) return;
                BeginInvoke(new MethodInvoker(() => { if (!_loadingEditor) ApplyEditorToStep(); }));
            };
            _postOpsList.ItemCheck += (s, e) =>
            {
                if (_loadingEditor) return;
                BeginInvoke(new MethodInvoker(() => { if (!_loadingEditor) ApplyEditorToStep(); }));
            };
        }

        private void OverrideMachineBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            if (_overrideMachineBox.Checked && _selectedProject != null)
                SeedProjectOverrideFromGlobal();
            SaveProjectMachineSettings();
            UpdateMachineSettingsVisibility();
        }

        private void ProjectMachineSettingsChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            SaveProjectMachineSettings();
        }

        private void SeedProjectOverrideFromGlobal()
        {
            if (_workingDoc == null || _workingDoc.settings == null || _selectedProject == null) return;
            var s = _workingDoc.settings;
            if (s.probe == null) s.probe = new ProbeSettings();
            if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();
            if (_selectedProject.probe == null) _selectedProject.probe = new ProbeSettings();
            if (_selectedProject.toolChangePos == null) _selectedProject.toolChangePos = new ToolChangePos();

            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(s.probe);
            _selectedProject.probe = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ProbeSettings>(json);
            json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(s.toolChangePos);
            _selectedProject.toolChangePos = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ToolChangePos>(json);
            _selectedProject.useSafeZForTc = s.useSafeZForTc;

            _loadingEditor = true;
            try
            {
                _projectOverrideFields.LoadFrom(_selectedProject.probe, _selectedProject.toolChangePos, _selectedProject.useSafeZForTc);
            }
            finally { _loadingEditor = false; }
        }

        private void UpdateMachineSettingsVisibility()
        {
            bool useScreenset = _useMachineTcBox.Checked;
            bool hasProject = _selectedProject != null;

            // Global settings stay visible and keep their values; they are greyed out
            // when the UCCNC screenset fields are used instead.
            _globalMachineGroup.Visible = true;
            _globalMachineGroup.Enabled = !useScreenset;

            // Project overrides stay visible whenever a project is selected, and are
            // greyed out (values retained) when the screenset fields are used.
            _projectOverrideGroup.Visible = hasProject;
            _projectOverrideGroup.Enabled = !useScreenset;
            _overrideMachineBox.Enabled = !useScreenset && hasProject;

            bool showOverride = hasProject && _overrideMachineBox.Checked;
            _projectOverridePanel.Visible = showOverride;
            _projectOverrideFields.SetEnabled(showOverride && !useScreenset);
        }

        private void SaveProjectMachineSettings()
        {
            if (_selectedProject == null) return;
            _selectedProject.overrideMachineSettings = _overrideMachineBox.Checked;
            if (_selectedProject.probe == null) _selectedProject.probe = new ProbeSettings();
            if (_selectedProject.toolChangePos == null) _selectedProject.toolChangePos = new ToolChangePos();
            bool useSafeZ = _selectedProject.useSafeZForTc;
            if (_overrideMachineBox.Checked)
                _projectOverrideFields.SaveTo(_selectedProject.probe, _selectedProject.toolChangePos, ref useSafeZ);
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

            _loadingEditor = true;
            try
            {
                _overrideMachineBox.Checked = _selectedProject.overrideMachineSettings;
                _projectOverrideFields.LoadFrom(_selectedProject.probe, _selectedProject.toolChangePos, _selectedProject.useSafeZForTc);
            }
            finally { _loadingEditor = false; }

            UpdateMachineSettingsVisibility();
        }

        public void LoadDocument(ProjectsDocument doc)
        {
            _workingDoc = JsonStore.CloneDocument(doc);
            _workingTools = JsonStore.CloneToolLibrary(_engine.ToolLibrary);
            RefreshProjectList();
            RefreshToolList();
            LoadSettingsFields();
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
                _selectedStepIndex = -1;
                ClearStepFields();
            }
        }

        private void LoadSelectedProject()
        {
            CommitEditorToModel();
            _selectedProject = _projectList.SelectedItem as WorkflowProject;
            ApplyProjectFields();
            RefreshStepList();
        }

        private void RefreshStepList()
        {
            _stepList.SelectedIndexChanged -= StepList_SelectedIndexChanged;
            try
            {
                int keepIndex = _selectedStepIndex;
                _stepList.Items.Clear();
                if (_selectedProject == null || _selectedProject.steps == null)
                {
                    _selectedStepIndex = -1;
                    ClearStepFields();
                    return;
                }

                for (int i = 0; i < _selectedProject.steps.Count; i++)
                {
                    var step = _selectedProject.steps[i];
                    _stepList.Items.Add((i + 1) + ". [" + step.type + "] " + step.label);
                }

                if (_stepList.Items.Count == 0)
                {
                    _selectedStepIndex = -1;
                    ClearStepFields();
                    return;
                }

                if (keepIndex < 0 || keepIndex >= _stepList.Items.Count)
                    keepIndex = 0;

                _selectedStepIndex = keepIndex;
                _stepList.SelectedIndex = keepIndex;
                ApplyStepFields();
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

            int newIndex = _stepList.SelectedIndex;
            if (newIndex == _selectedStepIndex) return;

            CommitEditorToModel();

            _selectedStepIndex = newIndex;
            if (newIndex >= 0)
                ApplyStepFields();
            else
                ClearStepFields();
        }

        private void CommitEditorToModel()
        {
            if (_selectedStepIndex < 0 || _selectedProject == null) return;
            ApplyEditorToStep();
        }

        private void ApplyProjectFields()
        {
            if (_selectedProject == null) return;
            _loadingEditor = true;
            try
            {
                _projectIdBox.Text = _selectedProject.id ?? "";
                _projectNameBox.Text = _selectedProject.name ?? "";
                _projectDescBox.Text = _selectedProject.description ?? "";
            }
            finally
            {
                _loadingEditor = false;
            }
            LoadProjectMachineFields();
        }

        private void ApplyStepFields()
        {
            if (_selectedProject == null || _selectedProject.steps == null ||
                _selectedStepIndex < 0 || _selectedStepIndex >= _selectedProject.steps.Count)
            {
                ClearStepFields();
                return;
            }

            var step = _selectedProject.steps[_selectedStepIndex];
            step.NormalizeType();
            if (step.preOps == null) step.preOps = new List<string>();
            if (step.postOps == null) step.postOps = new List<string>();

            _loadingEditor = true;
            try
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
            finally
            {
                _loadingEditor = false;
            }
        }

        private void ClearStepFields()
        {
            _loadingEditor = true;
            try
            {
                _stepLabelBox.Text = "";
                _stepTypeCombo.SelectedIndex = 0;
                _stepFileBox.Text = "";
                _stepInstructionsBox.Text = "";
                RefreshStepToolCombo(0);
                _photoBox.Text = "";
                _videoBox.Text = "";
                _photoPreview.Image = null;
                _stepToolPreview.Image = null;
                ClearCheckedOps(_preOpsList);
                ClearCheckedOps(_postOpsList);
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        private void LoadSettingsFields()
        {
            if (_workingDoc == null || _workingDoc.settings == null) return;
            _loadingEditor = true;
            try
            {
                var s = _workingDoc.settings;
                if (s.probe == null) s.probe = new ProbeSettings();
                if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();

                _mediaRootBox.Text = s.mediaRoot ?? "";
                _testModeBox.Checked = s.testMode;
                _useMachineTcBox.Checked = s.useMachineTcFields;
                _globalMachineFields.LoadFrom(s.probe, s.toolChangePos, s.useSafeZForTc);
            }
            finally
            {
                _loadingEditor = false;
            }
            UpdateMachineSettingsVisibility();
        }

        private void EditorChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            ApplyEditorToStep();
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            if (_workingDoc == null) return;
            if (_workingDoc.settings == null) _workingDoc.settings = new MaestroSettings();
            var s = _workingDoc.settings;
            if (s.probe == null) s.probe = new ProbeSettings();
            if (s.toolChangePos == null) s.toolChangePos = new ToolChangePos();

            s.mediaRoot = _mediaRootBox.Text.Trim();
            s.testMode = _testModeBox.Checked;
            s.useMachineTcFields = _useMachineTcBox.Checked;

            bool useSafeZ = s.useSafeZForTc;
            _globalMachineFields.SaveTo(s.probe, s.toolChangePos, ref useSafeZ);
            s.useSafeZForTc = useSafeZ;
        }

        private void ApplyEditorToStep()
        {
            if (_selectedProject != null)
            {
                _selectedProject.id = _projectIdBox.Text.Trim();
                _selectedProject.name = _projectNameBox.Text.Trim();
                _selectedProject.description = _projectDescBox.Text.Trim();
            }

            if (_selectedProject == null || _selectedStepIndex < 0 || _selectedStepIndex >= _selectedProject.steps.Count)
                return;

            var step = _selectedProject.steps[_selectedStepIndex];
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

            if (_stepList.SelectedIndex >= 0 && _stepList.SelectedIndex < _stepList.Items.Count)
            {
                string listText = (_selectedStepIndex + 1) + ". [" + step.type + "] " + step.label;
                if (!string.Equals(_stepList.Items[_stepList.SelectedIndex], listText))
                    _stepList.Items[_stepList.SelectedIndex] = listText;
            }
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
        }

        private void AddStepBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null) return;
            CommitEditorToModel();
            _selectedProject.steps.Add(new WorkflowStep());
            _selectedStepIndex = _selectedProject.steps.Count - 1;
            RefreshStepList();
        }

        private void DelStepBtn_Click(object sender, EventArgs e)
        {
            if (_selectedProject == null || _selectedStepIndex < 0) return;
            CommitEditorToModel();
            _selectedProject.steps.RemoveAt(_selectedStepIndex);
            if (_selectedStepIndex >= _selectedProject.steps.Count)
                _selectedStepIndex = _selectedProject.steps.Count - 1;
            RefreshStepList();
        }

        private void MoveStep(int delta)
        {
            if (_selectedProject == null || _selectedStepIndex < 0) return;
            CommitEditorToModel();
            int newIndex = _selectedStepIndex + delta;
            if (newIndex < 0 || newIndex >= _selectedProject.steps.Count) return;
            var item = _selectedProject.steps[_selectedStepIndex];
            _selectedProject.steps.RemoveAt(_selectedStepIndex);
            _selectedProject.steps.Insert(newIndex, item);
            _selectedStepIndex = newIndex;
            RefreshStepList();
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
            _loadingEditor = true;
            try
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
                UpdateStepToolPreview(selectedToolId);
            }
            finally { _loadingEditor = false; }
        }

        private int GetSelectedStepToolId()
        {
            var entry = _stepToolCombo.SelectedItem as ToolComboEntry;
            return entry != null ? entry.ToolId : 0;
        }

        private void UpdateStepToolPreview(int toolId)
        {
            if (_stepToolPreview.Image != null) { var old = _stepToolPreview.Image; _stepToolPreview.Image = null; old.Dispose(); }
            var tool = JsonStore.FindTool(_workingTools, toolId);
            if (tool == null || string.IsNullOrEmpty(tool.image)) return;
            LoadImagePreview(_stepToolPreview, tool.image);
        }

        private void StepToolCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            UpdateStepToolPreview(GetSelectedStepToolId());
            ApplyEditorToStep();
        }

        private void NewToolFromStepBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new NewToolDialog(_host, GetMediaRoot()))
            {
                if (dlg.ShowDialog(_host) != DialogResult.OK || dlg.Result == null) return;
                if (_workingTools == null) _workingTools = new ToolLibraryDocument();
                if (_workingTools.tools == null) _workingTools.tools = new List<ToolInfo>();
                dlg.Result.num = JsonStore.NextToolNum(_workingTools);
                AssignToolId(dlg.Result);
                _workingTools.tools.Add(dlg.Result);
                RefreshToolList();
                RefreshStepToolCombo(dlg.Result.id);
                ApplyEditorToStep();
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

            _loadingEditor = true;
            try
            {
                _libToolNumLabel.Text = "T" + _selectedLibraryTool.num;
                _libToolTypeBox.Text = _selectedLibraryTool.type ?? "";
                _libToolDiaBox.Text = _selectedLibraryTool.diameter ?? "";
                _libToolDescBox.Text = _selectedLibraryTool.desc ?? "";
                _libToolImageBox.Text = _selectedLibraryTool.image ?? "";
                LoadImagePreview(_libToolImagePreview, _selectedLibraryTool.image);
            }
            finally { _loadingEditor = false; }
        }

        private void ClearLibraryToolFields()
        {
            _loadingEditor = true;
            try
            {
                _libToolNumLabel.Text = "";
                _libToolTypeBox.Text = "";
                _libToolDiaBox.Text = "";
                _libToolDescBox.Text = "";
                _libToolImageBox.Text = "";
                if (_libToolImagePreview.Image != null) { var old = _libToolImagePreview.Image; _libToolImagePreview.Image = null; old.Dispose(); }
            }
            finally { _loadingEditor = false; }
        }

        private void CommitLibraryToolToModel()
        {
            if (_selectedLibraryTool == null) return;
            _selectedLibraryTool.type = _libToolTypeBox.Text.Trim();
            _selectedLibraryTool.diameter = _libToolDiaBox.Text.Trim();
            _selectedLibraryTool.desc = _libToolDescBox.Text.Trim();
            _selectedLibraryTool.image = _libToolImageBox.Text.Trim();
        }

        private void HookLibraryToolChanges()
        {
            _libToolTypeBox.TextChanged += LibraryToolChanged;
            _libToolDiaBox.TextChanged += LibraryToolChanged;
            _libToolDescBox.TextChanged += LibraryToolChanged;
        }

        private void LibraryToolChanged(object sender, EventArgs e)
        {
            if (_loadingEditor || _selectedLibraryTool == null) return;
            CommitLibraryToolToModel();
            int idx = _toolList.SelectedIndex;
            if (idx >= 0 && idx < _toolList.Items.Count)
            {
                _loadingEditor = true;
                try
                {
                    _toolList.Items[idx] = _selectedLibraryTool;
                    _toolList.SelectedIndex = idx;
                }
                finally { _loadingEditor = false; }
            }
            RefreshStepToolComboPreserving();
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
                num = JsonStore.NextToolNum(_workingTools),
                type = "New tool",
                diameter = "",
                desc = ""
            };
            AssignToolId(tool);
            _workingTools.tools.Add(tool);
            RefreshToolList();
            _toolList.SelectedItem = tool;
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
            copy.num = JsonStore.NextToolNum(_workingTools);
            AssignToolId(copy);
            _workingTools.tools.Add(copy);
            RefreshToolList();
            _toolList.SelectedItem = copy;
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
            string path = Path.Combine(mediaRoot, relativePath);
            if (!File.Exists(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var tmp = Image.FromStream(fs))
                    target.Image = new Bitmap(tmp);
            }
            catch { }
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
            if (DocumentSaved != null) DocumentSaved();
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
