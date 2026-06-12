using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Plugins
{
    public class OperatorView : UserControl
    {
        private readonly MaestroForm _host;
        private readonly WorkflowEngine _engine;

        private readonly ComboBox _projectCombo;
        private readonly Label _projectDescription;
        private readonly Label _demoBanner;
        private readonly DataGridView _stepGrid;
        private readonly Button _runAllButton;
        private readonly Button _resetButton;
        private readonly Button _abortButton;
        private readonly PictureBox _projectPhoto;
        private readonly Label _statusLabel;

        private readonly Panel _overlay;
        private readonly Panel _card;
        private readonly Label _overlayBanner;
        private readonly Label _overlayGraphic;
        private readonly PictureBox _overlayPhoto;
        private readonly Label _overlayInstructions;
        private readonly Button _playVideoButton;
        private readonly Button _confirmButton;
        private readonly Button _cancelPromptButton;

        private WorkflowProject _currentProject;
        private WorkflowStep _promptStep;
        private readonly Dictionary<int, StepRunStatus> _stepStatuses = new Dictionary<int, StepRunStatus>();
        private readonly Timer _runtimeTimer;
        private DateTime _stepRunStarted = DateTime.MinValue;
        private int _liveRuntimeStepIndex = -1;

        private enum RowVisualState
        {
            Done,
            Running,
            Ready,
            Pending,
            Stopped
        }

        public OperatorView(MaestroForm host, WorkflowEngine engine)
        {
            _host = host;
            _engine = engine;

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(10, 8, 10, 8) };
            _projectCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 360,
                Location = new Point(10, 10),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold)
            };
            _projectCombo.SelectedIndexChanged += ProjectCombo_SelectedIndexChanged;

            _projectDescription = new Label
            {
                Location = new Point(10, 56),
                Size = new Size(880, 38),
                Font = new Font("Segoe UI", 12F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoEllipsis = true
            };

            _demoBanner = new Label
            {
                Text = "DEMO MODE",
                Size = new Size(200, 44),
                Location = new Point(420, 10),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(214, 120, 0),
                Visible = false
            };

            topPanel.Controls.Add(_projectCombo);
            topPanel.Controls.Add(_projectDescription);
            topPanel.Controls.Add(_demoBanner);

            topPanel.Resize += (s, e) =>
            {
                int desired = topPanel.ClientSize.Width - _demoBanner.Width - 14;
                int minLeft = _projectCombo.Right + 20;
                _demoBanner.Left = Math.Max(minLeft, desired);
                _demoBanner.Top = 10;
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 196,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(10, 10, 10, 10),
                BackColor = Color.FromArgb(245, 245, 245)
            };

            _runAllButton = MakeButton("RUN ALL", Color.FromArgb(0, 122, 204), 15F, 60);
            _resetButton = MakeButton("RESET", Color.FromArgb(100, 100, 100), 15F, 60);
            _abortButton = MakeButton("ABORT", Color.FromArgb(180, 40, 40), 15F, 60);
            _runAllButton.Width = _resetButton.Width = _abortButton.Width = 172;
            _runAllButton.Margin = _resetButton.Margin = _abortButton.Margin = new Padding(0, 0, 0, 12);
            _runAllButton.Click += RunAllButton_Click;
            _resetButton.Click += ResetButton_Click;
            _abortButton.Click += AbortButton_Click;

            buttonPanel.Controls.Add(_runAllButton);
            buttonPanel.Controls.Add(_resetButton);
            buttonPanel.Controls.Add(_abortButton);

            _projectPhoto = new PictureBox
            {
                Width = 172,
                Height = 150,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Visible = false,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttonPanel.Controls.Add(_projectPhoto);

            _stepGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                Font = new Font("Segoe UI", 12F),
                ColumnHeadersHeight = 40,
                AllowUserToResizeRows = false
            };
            _stepGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            _stepGrid.RowTemplate.Height = 46;
            _stepGrid.Columns.Add("Status", "Status");
            _stepGrid.Columns.Add("Step", "#");
            _stepGrid.Columns.Add("Operation", "Operation");
            _stepGrid.Columns.Add("Tool", "Tool");
            _stepGrid.Columns.Add("Diameter", "Dia.");
            _stepGrid.Columns.Add("Runtime", "Runtime");
            _stepGrid.Columns.Add(new DataGridViewDisableButtonColumn { Name = "Run", HeaderText = "Action", Text = "RUN", UseColumnTextForButtonValue = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 120 });
            _stepGrid.Columns["Step"].FillWeight = 30;
            _stepGrid.Columns["Diameter"].FillWeight = 45;
            _stepGrid.Columns["Runtime"].FillWeight = 40;
            _stepGrid.CellContentClick += StepGrid_CellContentClick;
            _stepGrid.CellFormatting += StepGrid_CellFormatting;

            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Font = new Font("Segoe UI", 12F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Color.FromArgb(235, 235, 235),
                AutoEllipsis = true
            };

            _overlay = BuildOverlay(out _card, out _overlayBanner, out _overlayGraphic, out _overlayPhoto,
                out _overlayInstructions, out _playVideoButton, out _cancelPromptButton, out _confirmButton);

            Controls.Add(_stepGrid);
            Controls.Add(_statusLabel);
            Controls.Add(buttonPanel);
            Controls.Add(topPanel);
            Controls.Add(_overlay);

            _engine.StepStatusChanged += Engine_StepStatusChanged;
            _engine.RunningChanged += Engine_RunningChanged;
            _engine.PromptRequired += Engine_PromptRequired;
            _engine.StatusChanged += msg => { if (_statusLabel != null) _statusLabel.Text = msg; };
            _engine.RunFinished += () => { StopLiveRuntime(); HideOverlay(); UpdateRunButtonStates(); };

            _runtimeTimer = new Timer { Interval = 1000 };
            _runtimeTimer.Tick += RuntimeTimer_Tick;

            HideOverlay();
        }

        private Panel BuildOverlay(out Panel card, out Label banner, out Label graphic, out PictureBox photo,
            out Label instructions, out Button playVideo, out Button cancel, out Button confirm)
        {
            var overlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(40, 40, 45),
                Visible = false
            };

            card = new Panel
            {
                Size = new Size(860, 580),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));   // banner
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // body
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));  // buttons

            banner = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(214, 120, 0)
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(20, 16, 20, 16)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            graphic = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Emoji", 110F),
                Text = "\U0001F527"
            };
            photo = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false
            };
            var graphicCell = new Panel { Dock = DockStyle.Fill };
            graphicCell.Controls.Add(photo);
            graphicCell.Controls.Add(graphic);

            instructions = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 16F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0)
            };

            body.Controls.Add(graphicCell, 0, 0);
            body.Controls.Add(instructions, 1, 0);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(20, 8, 20, 16)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            cancel = MakeButton("CANCEL", Color.FromArgb(120, 120, 120), 14F, 64);
            cancel.Dock = DockStyle.Fill;
            cancel.Margin = new Padding(0, 0, 10, 0);
            cancel.Click += CancelPromptButton_Click;

            playVideo = MakeButton("PLAY VIDEO", Color.FromArgb(70, 70, 70), 14F, 64);
            playVideo.Dock = DockStyle.Fill;
            playVideo.Margin = new Padding(0, 0, 10, 0);
            playVideo.Click += PlayVideoButton_Click;

            confirm = MakeButton("CONFIRM", Color.FromArgb(0, 150, 0), 20F, 64);
            confirm.Dock = DockStyle.Fill;
            confirm.Margin = new Padding(0);
            confirm.Click += ConfirmButton_Click;

            buttons.Controls.Add(cancel, 0, 0);
            buttons.Controls.Add(playVideo, 1, 0);
            buttons.Controls.Add(confirm, 2, 0);

            layout.Controls.Add(banner, 0, 0);
            layout.Controls.Add(body, 0, 1);
            layout.Controls.Add(buttons, 0, 2);

            card.Controls.Add(layout);
            overlay.Controls.Add(card);

            var cardRef = card;
            overlay.Resize += (s, e) => LayoutOverlayCard(overlay, cardRef);

            return overlay;
        }

        private static void LayoutOverlayCard(Panel overlay, Panel card)
        {
            const int margin = 12;
            int w = Math.Min(860, overlay.ClientSize.Width - margin * 2);
            int h = Math.Min(580, overlay.ClientSize.Height - margin * 2);
            if (w < 200) w = Math.Max(0, overlay.ClientSize.Width);
            if (h < 200) h = Math.Max(0, overlay.ClientSize.Height);
            card.Size = new Size(w, h);
            card.Left = Math.Max(0, (overlay.ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(0, (overlay.ClientSize.Height - card.Height) / 2);
        }

        private static Button MakeButton(string text, Color backColor, float fontSize, int height)
        {
            return new Button
            {
                Text = text,
                AutoSize = false,
                Height = height,
                MinimumSize = new Size(0, height),
                Padding = new Padding(10, 4, 10, 4),
                Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 10, 0)
            };
        }

        private void SyncDemoToggle()
        {
            _demoBanner.Visible = _engine.TestMode;
        }

        public void ReloadProjects()
        {
            SyncDemoToggle();
            _projectCombo.Items.Clear();
            if (_engine.Document == null || _engine.Document.projects == null) return;

            foreach (var project in _engine.Document.projects)
                _projectCombo.Items.Add(project);

            if (_projectCombo.Items.Count == 0) return;

            int selectIndex = 0;
            if (!string.IsNullOrEmpty(_engine.State.lastProjectId))
            {
                for (int i = 0; i < _engine.Document.projects.Count; i++)
                {
                    if (_engine.Document.projects[i].id == _engine.State.lastProjectId)
                    {
                        selectIndex = i;
                        break;
                    }
                }
            }

            _projectCombo.SelectedIndex = selectIndex;
        }

        private void ProjectCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            _currentProject = _projectCombo.SelectedItem as WorkflowProject;
            if (_currentProject == null) return;

            _projectDescription.Text = string.IsNullOrEmpty(_currentProject.description)
                ? _currentProject.name
                : _currentProject.description;

            LoadProjectPhoto();
            RefreshStepGrid();
        }

        private void LoadProjectPhoto()
        {
            if (_projectPhoto.Image != null)
            {
                var old = _projectPhoto.Image;
                _projectPhoto.Image = null;
                old.Dispose();
            }

            if (_currentProject == null || string.IsNullOrEmpty(_currentProject.image))
            {
                _projectPhoto.Visible = false;
                return;
            }

            string path = ResolveMediaPath(_currentProject.image);
            var img = ImageUtil.LoadOriented(path);
            if (img == null)
            {
                _projectPhoto.Visible = false;
                return;
            }

            _projectPhoto.Image = img;
            _projectPhoto.Visible = true;
        }

        private void RefreshStepGrid()
        {
            _stepGrid.Rows.Clear();
            _stepStatuses.Clear();
            if (_currentProject == null) return;

            for (int i = 0; i < _currentProject.steps.Count; i++)
            {
                var step = _currentProject.steps[i];
                var status = RunStateStore.IsDone(_engine.State, _currentProject.id, i)
                    ? StepRunStatus.Done
                    : StepRunStatus.Pending;
                _stepStatuses[i] = status;

                var tool = step.IsGate ? null : _engine.GetToolForStep(step);
                string toolText = tool != null ? tool.type : (step.IsGate ? "-" : "");
                string dia = tool != null ? tool.diameter : (step.IsGate ? "-" : "");
                int lastRun = RunStateStore.GetLastRunSeconds(_engine.State, _currentProject.id, i);

                _stepGrid.Rows.Add(StatusText(GetRowVisualState(i)), (i + 1).ToString(), step.label, toolText, dia, FormatRuntime(lastRun));
            }

            UpdateRunButtonStates();
        }

        private static string FormatRuntime(int totalSeconds)
        {
            if (totalSeconds <= 0) return "-";
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes + ":" + seconds.ToString("00");
        }

        private void UpdateRuntimeCell(int stepIndex)
        {
            if (_currentProject == null || stepIndex < 0 || stepIndex >= _stepGrid.Rows.Count) return;
            int seconds = RunStateStore.GetLastRunSeconds(_engine.State, _currentProject.id, stepIndex);
            _stepGrid.Rows[stepIndex].Cells["Runtime"].Value = FormatRuntime(seconds);
        }

        private void StartLiveRuntime(int stepIndex)
        {
            _liveRuntimeStepIndex = stepIndex;
            _stepRunStarted = DateTime.Now;
            if (stepIndex >= 0 && stepIndex < _stepGrid.Rows.Count)
                _stepGrid.Rows[stepIndex].Cells["Runtime"].Value = "0:01";
            _runtimeTimer.Start();
        }

        private void StopLiveRuntime()
        {
            _runtimeTimer.Stop();
            _liveRuntimeStepIndex = -1;
        }

        private void RuntimeTimer_Tick(object sender, EventArgs e)
        {
            if (_liveRuntimeStepIndex < 0 || _currentProject == null) return;
            if (_liveRuntimeStepIndex >= _stepGrid.Rows.Count) return;
            int elapsed = Math.Max(1, (int)(DateTime.Now - _stepRunStarted).TotalSeconds);
            _stepGrid.Rows[_liveRuntimeStepIndex].Cells["Runtime"].Value = FormatRuntime(elapsed);
        }

        private int ActiveRowIndex()
        {
            if (_currentProject == null) return -1;

            if (_engine.IsRunning &&
                _engine.ActiveProject == _currentProject &&
                _engine.ActiveStepIndex >= 0)
                return _engine.ActiveStepIndex;

            foreach (var kv in _stepStatuses)
            {
                if (kv.Value == StepRunStatus.Running)
                    return kv.Key;
            }

            return RunStateStore.FirstNotDone(_engine.State, _currentProject.id, _currentProject.steps.Count);
        }

        private RowVisualState GetRowVisualState(int rowIndex)
        {
            StepRunStatus status;
            if (!_stepStatuses.TryGetValue(rowIndex, out status))
                return RowVisualState.Pending;

            if (status == StepRunStatus.Done) return RowVisualState.Done;
            if (status == StepRunStatus.Running) return RowVisualState.Running;
            if (status == StepRunStatus.Stopped) return RowVisualState.Stopped;

            if (!_engine.IsRunning && rowIndex == ActiveRowIndex())
                return RowVisualState.Ready;

            return RowVisualState.Pending;
        }

        private void ScrollToActiveRow()
        {
            int active = ActiveRowIndex();
            if (active < 0 || active >= _stepGrid.Rows.Count) return;
            try
            {
                _stepGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, active);
            }
            catch { }
        }

        private void UpdateRunButtonStates()
        {
            if (_currentProject == null) return;
            int runIndex = _stepGrid.Columns["Run"].Index;
            int statusIndex = _stepGrid.Columns["Status"].Index;
            int firstNotDone = RunStateStore.FirstNotDone(_engine.State, _currentProject.id, _currentProject.steps.Count);
            bool running = _engine.IsRunning;

            for (int r = 0; r < _stepGrid.Rows.Count; r++)
            {
                var cell = _stepGrid.Rows[r].Cells[runIndex] as DataGridViewDisableButtonCell;
                if (cell == null) continue;

                RowVisualState visual = GetRowVisualState(r);
                _stepGrid.Rows[r].Cells[statusIndex].Value = StatusText(visual);

                if (visual == RowVisualState.Done)
                {
                    cell.Mode = RunButtonMode.Done;
                    cell.Enabled = false;
                }
                else if (!running && r == firstNotDone)
                {
                    cell.Mode = RunButtonMode.ReadyToRun;
                    cell.Enabled = true;
                }
                else
                {
                    cell.Mode = RunButtonMode.Disabled;
                    cell.Enabled = false;
                }
            }

            _stepGrid.Invalidate();
            ScrollToActiveRow();
        }

        private static string StatusText(RowVisualState state)
        {
            switch (state)
            {
                case RowVisualState.Running: return "\u25B6 RUNNING";
                case RowVisualState.Done: return "\u2713 DONE";
                case RowVisualState.Ready: return "\u25CF READY";
                case RowVisualState.Stopped: return "\u2717 STOPPED";
                default: return "\u25CB PENDING";
            }
        }

        private static string StatusText(StepRunStatus status)
        {
            switch (status)
            {
                case StepRunStatus.Running: return "\u25B6 RUNNING";
                case StepRunStatus.Done: return "\u2713 DONE";
                case StepRunStatus.Stopped: return "\u2717 STOPPED";
                default: return "\u25CB PENDING";
            }
        }

        private void StepGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;

            RowVisualState visual = GetRowVisualState(e.RowIndex);
            Color back;
            Color fore;
            FontStyle fontStyle = FontStyle.Regular;

            switch (visual)
            {
                case RowVisualState.Done:
                    back = Color.FromArgb(242, 242, 242);
                    fore = Color.FromArgb(154, 154, 154);
                    break;
                case RowVisualState.Running:
                    back = Color.FromArgb(255, 233, 194);
                    fore = Color.FromArgb(90, 58, 0);
                    fontStyle = FontStyle.Bold;
                    break;
                case RowVisualState.Ready:
                    back = Color.FromArgb(220, 235, 255);
                    fore = Color.FromArgb(10, 61, 110);
                    fontStyle = FontStyle.Bold;
                    break;
                case RowVisualState.Stopped:
                    back = Color.White;
                    fore = Color.Red;
                    fontStyle = FontStyle.Bold;
                    break;
                default:
                    back = Color.White;
                    fore = Color.FromArgb(68, 68, 68);
                    break;
            }

            e.CellStyle.BackColor = back;
            e.CellStyle.ForeColor = fore;
            e.CellStyle.SelectionBackColor = back;
            e.CellStyle.SelectionForeColor = fore;
            e.CellStyle.Font = new Font(_stepGrid.Font, fontStyle);
        }

        private void StepGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _stepGrid.Columns["Run"].Index) return;
            if (_engine.IsRunning || _currentProject == null) return;
            var cell = _stepGrid.Rows[e.RowIndex].Cells[e.ColumnIndex] as DataGridViewDisableButtonCell;
            if (cell == null || !cell.Enabled) return;
            _engine.RunStep(_currentProject, e.RowIndex, false);
        }

        private void RunAllButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null || _engine.IsRunning) return;
            int start = RunStateStore.FirstNotDone(_engine.State, _currentProject.id, _currentProject.steps.Count);
            if (start < 0) start = 0;
            _engine.RunAll(_currentProject, start);
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null || _engine.IsRunning) return;
            if (MessageBox.Show(_host, "Reset all step completion flags for " + _currentProject.name + "?",
                    "Reset Project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _engine.ResetProject(_currentProject);
            RefreshStepGrid();
        }

        private void AbortButton_Click(object sender, EventArgs e)
        {
            _engine.RequestAbort();
        }

        private void Engine_StepStatusChanged(int index, StepRunStatus status)
        {
            _stepStatuses[index] = status;
            if (status == StepRunStatus.Running)
                StartLiveRuntime(index);
            else
            {
                StopLiveRuntime();
                if (status == StepRunStatus.Done)
                    UpdateRuntimeCell(index);
            }
            UpdateRunButtonStates();
        }

        private void Engine_RunningChanged(bool running)
        {
            _runAllButton.Enabled = !running;
            _resetButton.Enabled = !running;
            _abortButton.Enabled = running;
            _projectCombo.Enabled = !running;
            UpdateRunButtonStates();
        }

        private void Engine_PromptRequired(WorkflowStep step, int stepIndex, bool isGateOnly)
        {
            _promptStep = step;
            bool hasVideo = !string.IsNullOrEmpty(step.video);
            _playVideoButton.Visible = hasVideo;

            if (isGateOnly)
            {
                bool hasPhoto = LoadOverlayImage(step.photo);
                _overlayBanner.Text = "OPERATOR ACTION  -  Step " + (stepIndex + 1);
                _overlayBanner.BackColor = Color.FromArgb(0, 120, 180);
                _confirmButton.Text = "DONE - CONTINUE";
                if (!hasPhoto) SetGraphicGlyph("\U0001F504");
                _overlayInstructions.Text = step.label + "\n\n" + step.DisplayInstructions;
            }
            else
            {
                var tool = _engine.GetToolForStep(step) ?? new ToolInfo();
                bool hasImage = LoadOverlayImage(tool.image) || LoadOverlayImage(step.photo);
                _overlayBanner.Text = "CHANGE TOOL  ->  T" + tool.num;
                _overlayBanner.BackColor = Color.FromArgb(214, 120, 0);
                _confirmButton.Text = "TOOL INSTALLED";
                if (!hasImage) SetGraphicGlyph("\U0001F527");
                _overlayInstructions.Text = BuildToolText(step, stepIndex, tool);
            }

            ShowOverlay();
        }

        private void SetGraphicGlyph(string glyph)
        {
            _overlayPhoto.Visible = false;
            _overlayGraphic.Visible = true;
            _overlayGraphic.Text = glyph;
        }

        private bool LoadOverlayImage(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            string path = ResolveMediaPath(relativePath);
            if (_overlayPhoto.Image != null) { var old = _overlayPhoto.Image; _overlayPhoto.Image = null; old.Dispose(); }
            var img = ImageUtil.LoadOriented(path);
            if (img == null) return false;
            _overlayPhoto.Image = img;
            _overlayPhoto.Visible = true;
            _overlayGraphic.Visible = false;
            return true;
        }

        private static string BuildToolText(WorkflowStep step, int stepIndex, ToolInfo tool)
        {
            if (tool == null) tool = new ToolInfo();
            return step.label + "\n\n" +
                   "Install Tool #" + tool.num + "\n" +
                   tool.diameter + "  " + tool.type + "\n" +
                   (string.IsNullOrEmpty(tool.desc) ? "" : tool.desc + "\n") + "\n" +
                   (string.IsNullOrEmpty(step.DisplayInstructions) ? "" : step.DisplayInstructions + "\n\n") +
                   "Tighten the collet, then press CONFIRM.";
        }

        private string ResolveMediaPath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath)) return relativePath;
            string mediaRoot = _engine.Document.settings.mediaRoot;
            if (string.IsNullOrEmpty(mediaRoot))
                mediaRoot = MaestroPaths.MaestroRoot + "\\Media";
            return Path.Combine(mediaRoot, relativePath);
        }

        private void PlayVideoButton_Click(object sender, EventArgs e)
        {
            if (_promptStep == null || string.IsNullOrEmpty(_promptStep.video)) return;
            string path = ResolveMediaPath(_promptStep.video);
            if (!File.Exists(path))
            {
                MessageBox.Show(_host, "Video not found:\n" + path, "Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Process.Start(path); }
            catch (Exception ex)
            {
                MessageBox.Show(_host, "Could not open video:\n" + ex.Message, "Video", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfirmButton_Click(object sender, EventArgs e)
        {
            HideOverlay();
            _engine.ConfirmPrompt();
        }

        private void CancelPromptButton_Click(object sender, EventArgs e)
        {
            HideOverlay();
            _engine.CancelPrompt();
        }

        private void ShowOverlay()
        {
            _overlay.Visible = true;
            _overlay.BringToFront();
            LayoutOverlayCard(_overlay, _card);
        }

        private void HideOverlay()
        {
            _overlay.Visible = false;
            _promptStep = null;
        }
    }
}
