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

        private readonly ListBox _projectList;
        private readonly ListBox _stepList;
        private TextBox _projectIdBox;
        private TextBox _projectNameBox;
        private TextBox _projectDescBox;
        private TextBox _stepLabelBox;
        private ComboBox _stepTypeCombo;
        private TextBox _stepFileBox;
        private TextBox _stepInstructionsBox;
        private NumericUpDown _stepMinutesBox;
        private TextBox _toolNumBox;
        private TextBox _toolTypeBox;
        private TextBox _toolDiaBox;
        private TextBox _toolDescBox;
        private NumericUpDown _toolRpmBox;
        private TextBox _toolImageBox;
        private PictureBox _toolImagePreview;
        private TextBox _photoBox;
        private TextBox _videoBox;
        private PictureBox _photoPreview;
        private CheckedListBox _preOpsList;
        private CheckedListBox _postOpsList;
        private TextBox _gcodeRootBox;
        private TextBox _mediaRootBox;
        private CheckBox _testModeBox;
        private CheckBox _useMachineTcBox;
        private TextBox _plateXBox;
        private TextBox _plateYBox;
        private TextBox _probeDistBox;
        private TextBox _retractBox;
        private TextBox _feedFastBox;
        private TextBox _feedSlowBox;
        private TextBox _plateRapidZBox;
        private TextBox _plateZeroBox;
        private TextBox _tcXBox;
        private TextBox _tcYBox;
        private TextBox _tcZBox;
        private TextBox _safeZBox;
        private CheckBox _useSafeZBox;
        private CheckBox _plateRapidBox;
        private Label _saveStatusLabel;

        private WorkflowProject _selectedProject;
        private int _selectedStepIndex = -1;
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

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            var addProjectBtn = new Button { Text = "Add", Width = 60 };
            var dupProjectBtn = new Button { Text = "Duplicate", Width = 80 };
            var delProjectBtn = new Button { Text = "Delete", Width = 60 };
            addProjectBtn.Click += AddProjectBtn_Click;
            dupProjectBtn.Click += DupProjectBtn_Click;
            delProjectBtn.Click += DelProjectBtn_Click;
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

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            var saveBtn = MakeActionButton("Save All", Color.FromArgb(0, 122, 204));
            saveBtn.Location = new Point(8, 8);
            saveBtn.Click += SaveBtn_Click;
            _saveStatusLabel = new Label { Location = new Point(120, 12), AutoSize = true, Text = "" };
            bottomPanel.Controls.Add(saveBtn);
            bottomPanel.Controls.Add(_saveStatusLabel);

            Controls.Add(split);

            Load += (s, e) =>
            {
                SafeSetSplitter(split, 190, 100, 200);
                SafeSetSplitter(rightSplit, 200, 80, 80);
            };
            Controls.Add(bottomPanel);
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
            var browseGcodeBtn = new Button { Text = "Browse...", Location = new Point(450, y - 28), Width = 80 };
            browseGcodeBtn.Click += BrowseGcodeBtn_Click;
            scroll.Controls.Add(browseGcodeBtn);

            scroll.Controls.Add(MkLabel("Instructions", 8, y));
            _stepInstructionsBox = MkText(120, y, 420, 70, true); y += 78;
            scroll.Controls.Add(_stepInstructionsBox);
            scroll.Controls.Add(MkLabel("Minutes", 8, y));
            _stepMinutesBox = new NumericUpDown { Location = new Point(120, y), Width = 80, Maximum = 999, Minimum = 0 };
            scroll.Controls.Add(_stepMinutesBox); y += 28;

            scroll.Controls.Add(MkLabel("Tool #", 8, y));
            _toolNumBox = MkText(120, y, 60);
            scroll.Controls.Add(MkLabel("Type", 190, y));
            _toolTypeBox = MkText(230, y, 140);
            scroll.Controls.Add(MkLabel("Dia.", 380, y));
            _toolDiaBox = MkText(420, y, 80);
            scroll.Controls.Add(_toolNumBox);
            scroll.Controls.Add(_toolTypeBox);
            scroll.Controls.Add(_toolDiaBox); y += 28;
            scroll.Controls.Add(MkLabel("Tool Desc", 8, y));
            _toolDescBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_toolDescBox);
            scroll.Controls.Add(MkLabel("RPM", 8, y));
            _toolRpmBox = new NumericUpDown { Location = new Point(120, y), Width = 100, Maximum = 60000, Minimum = 0, Value = 18000 };
            scroll.Controls.Add(_toolRpmBox); y += 28;

            scroll.Controls.Add(MkLabel("Tool Image", 8, y));
            _toolImageBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_toolImageBox);
            var browseToolImageBtn = new Button { Text = "Pick Tool Image...", Location = new Point(450, y - 28), Width = 120 };
            browseToolImageBtn.Click += BrowseToolImageBtn_Click;
            scroll.Controls.Add(browseToolImageBtn);
            _toolImagePreview = new PictureBox
            {
                Location = new Point(560, 160),
                Size = new Size(180, 140),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            scroll.Controls.Add(_toolImagePreview);

            scroll.Controls.Add(MkLabel("Photo", 8, y));
            _photoBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_photoBox);
            var browsePhotoBtn = new Button { Text = "Pick Photo...", Location = new Point(450, y - 28), Width = 90 };
            browsePhotoBtn.Click += BrowsePhotoBtn_Click;
            scroll.Controls.Add(browsePhotoBtn);
            scroll.Controls.Add(MkLabel("Video", 8, y));
            _videoBox = MkText(120, y, 320); y += 28;
            scroll.Controls.Add(_videoBox);
            var browseVideoBtn = new Button { Text = "Pick Video...", Location = new Point(450, y - 28), Width = 90 };
            browseVideoBtn.Click += BrowseVideoBtn_Click;
            scroll.Controls.Add(browseVideoBtn);

            _photoPreview = new PictureBox
            {
                Location = new Point(560, 8),
                Size = new Size(180, 140),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            scroll.Controls.Add(_photoPreview);

            scroll.Controls.Add(MkLabel("Pre Ops", 8, y));
            _preOpsList = new CheckedListBox { Location = new Point(120, y), Size = new Size(220, 100) };
            scroll.Controls.Add(_preOpsList);
            scroll.Controls.Add(MkLabel("Post Ops", 350, y));
            _postOpsList = new CheckedListBox { Location = new Point(430, y), Size = new Size(220, 100) };
            scroll.Controls.Add(_postOpsList); y += 108;

            scroll.Controls.Add(MkLabel("G-code Root", 8, y));
            _gcodeRootBox = MkText(120, y, 420); y += 28;
            scroll.Controls.Add(_gcodeRootBox);
            scroll.Controls.Add(MkLabel("Media Root", 8, y));
            _mediaRootBox = MkText(120, y, 420); y += 28;
            scroll.Controls.Add(_mediaRootBox);
            _testModeBox = new CheckBox { Text = "Test mode (skip probing checks)", Location = new Point(120, y), AutoSize = true };
            scroll.Controls.Add(_testModeBox); y += 24;
            _useMachineTcBox = new CheckBox { Text = "Use screenset probing/tool-change fields (only if your UCCNC screenset provides them)", Location = new Point(120, y), AutoSize = true, Checked = false };
            scroll.Controls.Add(_useMachineTcBox); y += 26;

            scroll.Controls.Add(MkLabel("Probing / Tool Change (machine coordinates; used when the box above is OFF)", 8, y)); y += 24;

            scroll.Controls.Add(MkLabel("Plate X", 8, y));
            _plateXBox = MkText(120, y, 90);
            scroll.Controls.Add(_plateXBox);
            scroll.Controls.Add(MkLabel("Plate Y", 300, y));
            _plateYBox = MkText(380, y, 90);
            scroll.Controls.Add(_plateYBox); y += 28;

            scroll.Controls.Add(MkLabel("Probe dist", 8, y));
            _probeDistBox = MkText(120, y, 90);
            scroll.Controls.Add(_probeDistBox);
            scroll.Controls.Add(MkLabel("Retract dist", 300, y));
            _retractBox = MkText(380, y, 90);
            scroll.Controls.Add(_retractBox); y += 28;

            scroll.Controls.Add(MkLabel("Fast feed", 8, y));
            _feedFastBox = MkText(120, y, 90);
            scroll.Controls.Add(_feedFastBox);
            scroll.Controls.Add(MkLabel("Slow feed", 300, y));
            _feedSlowBox = MkText(380, y, 90);
            scroll.Controls.Add(_feedSlowBox); y += 28;

            scroll.Controls.Add(MkLabel("Plate rapid Z", 8, y));
            _plateRapidZBox = MkText(120, y, 90);
            scroll.Controls.Add(_plateRapidZBox);
            scroll.Controls.Add(MkLabel("Plate Z zero", 300, y));
            _plateZeroBox = MkText(380, y, 90);
            scroll.Controls.Add(_plateZeroBox); y += 28;

            scroll.Controls.Add(MkLabel("Tool change X", 8, y));
            _tcXBox = MkText(120, y, 90);
            scroll.Controls.Add(_tcXBox);
            scroll.Controls.Add(MkLabel("Tool change Y", 300, y));
            _tcYBox = MkText(380, y, 90);
            scroll.Controls.Add(_tcYBox); y += 28;

            scroll.Controls.Add(MkLabel("Tool change Z", 8, y));
            _tcZBox = MkText(120, y, 90);
            scroll.Controls.Add(_tcZBox);
            scroll.Controls.Add(MkLabel("Safe Z", 300, y));
            _safeZBox = MkText(380, y, 90);
            scroll.Controls.Add(_safeZBox); y += 28;

            _useSafeZBox = new CheckBox { Text = "Retract to Safe Z before tool-change / probe moves", Location = new Point(120, y), AutoSize = true, Checked = true };
            scroll.Controls.Add(_useSafeZBox); y += 24;
            _plateRapidBox = new CheckBox { Text = "Rapid to Plate rapid Z before probing", Location = new Point(120, y), AutoSize = true };
            scroll.Controls.Add(_plateRapidBox); y += 24;

            foreach (var label in AutoOpLabels)
            {
                _preOpsList.Items.Add(label);
                _postOpsList.Items.Add(label);
            }

            _stepTypeCombo.SelectedIndexChanged += (s, e) => EditorChanged(s, e);
            HookEditorChanges();

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
            _stepMinutesBox.ValueChanged += EditorChanged;
            _toolNumBox.TextChanged += EditorChanged;
            _toolTypeBox.TextChanged += EditorChanged;
            _toolDiaBox.TextChanged += EditorChanged;
            _toolDescBox.TextChanged += EditorChanged;
            _toolRpmBox.ValueChanged += EditorChanged;
            _toolImageBox.TextChanged += EditorChanged;
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
            _gcodeRootBox.TextChanged += SettingsChanged;
            _mediaRootBox.TextChanged += SettingsChanged;
            _testModeBox.CheckedChanged += SettingsChanged;
            _useMachineTcBox.CheckedChanged += SettingsChanged;
            _plateXBox.TextChanged += SettingsChanged;
            _plateYBox.TextChanged += SettingsChanged;
            _probeDistBox.TextChanged += SettingsChanged;
            _retractBox.TextChanged += SettingsChanged;
            _feedFastBox.TextChanged += SettingsChanged;
            _feedSlowBox.TextChanged += SettingsChanged;
            _plateRapidZBox.TextChanged += SettingsChanged;
            _plateZeroBox.TextChanged += SettingsChanged;
            _tcXBox.TextChanged += SettingsChanged;
            _tcYBox.TextChanged += SettingsChanged;
            _tcZBox.TextChanged += SettingsChanged;
            _safeZBox.TextChanged += SettingsChanged;
            _useSafeZBox.CheckedChanged += SettingsChanged;
            _plateRapidBox.CheckedChanged += SettingsChanged;
        }

        public void LoadDocument(ProjectsDocument doc)
        {
            _workingDoc = JsonStore.CloneDocument(doc);
            RefreshProjectList();
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
            if (step.tool == null) step.tool = new ToolInfo();
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
                _stepMinutesBox.Value = Math.Max(0, step.minutes);
                _toolNumBox.Text = step.tool.num.ToString();
                _toolTypeBox.Text = step.tool.type ?? "";
                _toolDiaBox.Text = step.tool.diameter ?? "";
                _toolDescBox.Text = step.tool.desc ?? "";
                _toolRpmBox.Value = Math.Max(0, step.tool.rpm);
                _toolImageBox.Text = step.tool.image ?? "";
                _photoBox.Text = step.photo ?? "";
                _videoBox.Text = step.video ?? "";
                SetCheckedOps(_preOpsList, step.preOps);
                SetCheckedOps(_postOpsList, step.postOps);
                LoadPhotoPreview(step.photo);
                LoadImagePreview(_toolImagePreview, step.tool.image);
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
                _stepMinutesBox.Value = 0;
                _toolNumBox.Text = "1";
                _toolTypeBox.Text = "";
                _toolDiaBox.Text = "";
                _toolDescBox.Text = "";
                _toolRpmBox.Value = 18000;
                _toolImageBox.Text = "";
                _photoBox.Text = "";
                _videoBox.Text = "";
                _photoPreview.Image = null;
                _toolImagePreview.Image = null;
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
                var p = s.probe;
                var tc = s.toolChangePos;

                _gcodeRootBox.Text = s.gcodeRoot ?? "";
                _mediaRootBox.Text = s.mediaRoot ?? "";
                _testModeBox.Checked = s.testMode;
                _useMachineTcBox.Checked = s.useMachineTcFields;

                _plateXBox.Text = FmtD(p.xPlate);
                _plateYBox.Text = FmtD(p.yPlate);
                _probeDistBox.Text = FmtD(p.dist);
                _retractBox.Text = FmtD(p.retractDist);
                _feedFastBox.Text = FmtD(p.feedFast);
                _feedSlowBox.Text = FmtD(p.feedSlow);
                _plateRapidZBox.Text = FmtD(p.plateRapidZ);
                _plateZeroBox.Text = FmtD(p.plateZero);
                _tcXBox.Text = FmtD(tc.x);
                _tcYBox.Text = FmtD(tc.y);
                _tcZBox.Text = FmtD(tc.z);
                _safeZBox.Text = FmtD(tc.zSafe);
                _useSafeZBox.Checked = s.useSafeZForTc;
                _plateRapidBox.Checked = p.plateRapid;
            }
            finally
            {
                _loadingEditor = false;
            }
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

            s.gcodeRoot = _gcodeRootBox.Text.Trim();
            s.mediaRoot = _mediaRootBox.Text.Trim();
            s.testMode = _testModeBox.Checked;
            s.useMachineTcFields = _useMachineTcBox.Checked;

            s.probe.xPlate = ParseD(_plateXBox);
            s.probe.yPlate = ParseD(_plateYBox);
            s.probe.dist = ParseD(_probeDistBox);
            s.probe.retractDist = ParseD(_retractBox);
            s.probe.feedFast = ParseD(_feedFastBox);
            s.probe.feedSlow = ParseD(_feedSlowBox);
            s.probe.plateRapidZ = ParseD(_plateRapidZBox);
            s.probe.plateZero = ParseD(_plateZeroBox);
            s.probe.plateRapid = _plateRapidBox.Checked;
            s.toolChangePos.x = ParseD(_tcXBox);
            s.toolChangePos.y = ParseD(_tcYBox);
            s.toolChangePos.z = ParseD(_tcZBox);
            s.toolChangePos.zSafe = ParseD(_safeZBox);
            s.useSafeZForTc = _useSafeZBox.Checked;
        }

        private static double ParseD(TextBox box)
        {
            double v;
            if (box == null) return 0;
            double.TryParse((box.Text ?? "").Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static string FmtD(double value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            step.minutes = (int)_stepMinutesBox.Value;
            if (step.tool == null) step.tool = new ToolInfo();
            int toolNum;
            step.tool.num = int.TryParse(_toolNumBox.Text, out toolNum) ? toolNum : 1;
            step.tool.type = _toolTypeBox.Text.Trim();
            step.tool.diameter = _toolDiaBox.Text.Trim();
            step.tool.desc = _toolDescBox.Text.Trim();
            step.tool.rpm = (int)_toolRpmBox.Value;
            step.tool.image = _toolImageBox.Text.Trim();
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
            if (MessageBox.Show(_host, "Delete project " + _selectedProject.name + "?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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

        private void BrowseGcodeBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                string root = _gcodeRootBox.Text.Trim();
                if (Directory.Exists(root)) dlg.InitialDirectory = root;
                dlg.Filter = "G-code files|*.nc;*.tap;*.ngc;*.cnc|All files|*.*";
                if (dlg.ShowDialog(_host) != DialogResult.OK) return;
                if (!string.IsNullOrEmpty(root) && dlg.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    _stepFileBox.Text = dlg.FileName.Substring(root.Length).TrimStart('\\', '/');
                else
                    _stepFileBox.Text = dlg.FileName;
                ApplyEditorToStep();
            }
        }

        private void BrowsePhotoBtn_Click(object sender, EventArgs e)
        {
            CopyMediaFile(_photoBox, new[] { "jpg", "jpeg", "png", "bmp", "gif" });
            LoadPhotoPreview(_photoBox.Text);
        }

        private void BrowseVideoBtn_Click(object sender, EventArgs e)
        {
            CopyMediaFile(_videoBox, new[] { "mp4", "avi", "wmv", "mov", "mkv" });
        }

        private void BrowseToolImageBtn_Click(object sender, EventArgs e)
        {
            CopyMediaFile(_toolImageBox, new[] { "jpg", "jpeg", "png", "bmp", "gif" });
            LoadImagePreview(_toolImagePreview, _toolImageBox.Text);
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
            SettingsChanged(null, null);
            JsonStore.SaveProjects(MaestroPaths.ProjectsFile, _workingDoc);
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
