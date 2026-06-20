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
        private readonly Button _runFromButton;
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

        private Panel _progressOverlay;
        private Panel _progressCard;
        private Label _progressProjectLabel;
        private Label _progressStepLabel;
        private Label _progressFileLabel;
        private Label _progressCaption;
        private Label _progressCountdown;
        private ProgressBar _progressBar;
        private Button _progressCancelButton;
        private Button _progressCloseButton;
        private int _runningEstimateSeconds;
        private bool _progressClosed;
        private int _fileCurrentLine;
        private int _fileTotalLines;

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
            _runFromButton = MakeButton("RUN FROM\u2026", Color.FromArgb(150, 90, 30), 13F, 52);
            _resetButton = MakeButton("RESET", Color.FromArgb(100, 100, 100), 15F, 60);
            _abortButton = MakeButton("ABORT", Color.FromArgb(180, 40, 40), 15F, 60);
            _runAllButton.Width = _runFromButton.Width = _resetButton.Width = _abortButton.Width = 172;
            _runAllButton.Margin = _resetButton.Margin = _abortButton.Margin = new Padding(0, 0, 0, 12);
            _runFromButton.Margin = new Padding(0, 0, 0, 12);
            _runAllButton.Click += RunAllButton_Click;
            _runFromButton.Click += RunFromButton_Click;
            _resetButton.Click += ResetButton_Click;
            _abortButton.Click += AbortButton_Click;

            buttonPanel.Controls.Add(_runAllButton);
            buttonPanel.Controls.Add(_runFromButton);
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
            _stepGrid.Columns.Add("Operation", "Operation");
            _stepGrid.Columns.Add("ToolNum", "Tool");
            _stepGrid.Columns.Add("Tool", "Tool Description");
            _stepGrid.Columns.Add("Runtime", "Runtime");
            _stepGrid.Columns.Add(new DataGridViewButtonColumn { Name = "Video", HeaderText = "Video", UseColumnTextForButtonValue = false, FlatStyle = FlatStyle.Standard, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 80 });
            _stepGrid.Columns.Add(new DataGridViewDisableButtonColumn { Name = "Run", HeaderText = "Action", Text = "RUN", UseColumnTextForButtonValue = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 150 });
            _stepGrid.Columns["Status"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            _stepGrid.Columns["Operation"].FillWeight = 220;
            _stepGrid.Columns["ToolNum"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            _stepGrid.Columns["Tool"].FillWeight = 110;
            _stepGrid.Columns["Runtime"].FillWeight = 40;
            foreach (DataGridViewColumn col in _stepGrid.Columns)
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            _stepGrid.CellContentClick += StepGrid_CellContentClick;
            _stepGrid.CellClick += StepGrid_CellClick;
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

            _progressOverlay = BuildProgressOverlay();

            Controls.Add(_stepGrid);
            Controls.Add(_statusLabel);
            Controls.Add(buttonPanel);
            Controls.Add(topPanel);
            Controls.Add(_overlay);
            Controls.Add(_progressOverlay);

            _engine.StepStatusChanged += Engine_StepStatusChanged;
            _engine.StepWorkStarted += Engine_StepWorkStarted;
            _engine.FileProgressChanged += Engine_FileProgressChanged;
            _engine.RunningChanged += Engine_RunningChanged;
            _engine.PromptRequired += Engine_PromptRequired;
            _engine.StatusChanged += msg => { if (_statusLabel != null) _statusLabel.Text = msg; };
            _engine.RunFinished += Engine_RunFinished;
            _engine.ProjectCompleted += Engine_ProjectCompleted;

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

        // Large, glanceable progress view shown while an op step is cutting. It is a
        // non-blocking overlay (not ShowDialog) so the engine keeps driving the run and
        // the operator-prompt overlay can still take over for tool changes / gates.
        private Panel BuildProgressOverlay()
        {
            var overlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 35),
                Visible = false
            };

            var card = new Panel
            {
                Size = new Size(860, 580),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _progressCard = card;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));   // project banner
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // body
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));  // buttons

            _progressProjectLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 122, 204),
                AutoEllipsis = true
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(28, 12, 28, 12)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));    // step label
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));    // file label
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));     // top spacer (centers block)
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));    // caption
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));   // big clock (above bar)
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));    // progress bar
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));    // reopen hint
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));     // bottom spacer

            _progressStepLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _progressFileLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12F),
                ForeColor = Color.FromArgb(110, 110, 110),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _progressCaption = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(110, 110, 110),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _progressCountdown = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 60F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 90, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1000,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 2, 0, 2)
            };
            var hint = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = Color.FromArgb(140, 140, 140),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Closing leaves the run going \u2014 tap the running step to reopen this view."
            };

            body.Controls.Add(_progressStepLabel, 0, 0);
            body.Controls.Add(_progressFileLabel, 0, 1);
            body.Controls.Add(_progressCaption, 0, 3);
            body.Controls.Add(_progressCountdown, 0, 4);
            body.Controls.Add(_progressBar, 0, 5);
            body.Controls.Add(hint, 0, 6);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(20, 8, 20, 16)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _progressCancelButton = MakeButton("CANCEL OPERATION", Color.FromArgb(180, 40, 40), 16F, 64);
            _progressCancelButton.Dock = DockStyle.Fill;
            _progressCancelButton.Margin = new Padding(0, 0, 10, 0);
            _progressCancelButton.Click += ProgressCancelButton_Click;

            _progressCloseButton = MakeButton("CLOSE", Color.FromArgb(100, 100, 100), 16F, 64);
            _progressCloseButton.Dock = DockStyle.Fill;
            _progressCloseButton.Margin = new Padding(10, 0, 0, 0);
            _progressCloseButton.Click += ProgressCloseButton_Click;

            buttons.Controls.Add(_progressCancelButton, 0, 0);
            buttons.Controls.Add(_progressCloseButton, 1, 0);

            layout.Controls.Add(_progressProjectLabel, 0, 0);
            layout.Controls.Add(body, 0, 1);
            layout.Controls.Add(buttons, 0, 2);

            card.Controls.Add(layout);
            overlay.Controls.Add(card);

            var cardRef = card;
            overlay.Resize += (s, e) => LayoutOverlayCard(overlay, cardRef);

            return overlay;
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
                string toolNum = tool != null ? (tool.num ?? "") : (step.IsGate ? "-" : "");
                string toolText = tool != null ? tool.SizeDescription() : (step.IsGate ? "-" : "");
                int lastRun = RunStateStore.GetLastRunSeconds(_engine.State, _currentProject.id, i);

                int row = _stepGrid.Rows.Add(StatusText(GetRowVisualState(i)), step.label, toolNum, toolText, FormatRuntime(lastRun));

                // Only rows with a video get a clickable button; others become a blank
                // text cell so no empty button is drawn.
                if (!string.IsNullOrEmpty(step.video))
                    _stepGrid.Rows[row].Cells["Video"].Value = "\u25B6 VIDEO";
                else
                    _stepGrid.Rows[row].Cells["Video"] = new DataGridViewTextBoxCell { Value = "" };
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
            if (_progressOverlay != null && _progressOverlay.Visible)
                UpdateProgressDisplay();
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

                var step = _currentProject.steps[r];
                bool toolChange = step.IsOp && step.preOps != null &&
                    step.preOps.Exists(o => o != null && o.id == AutoOpIds.ToolPrompt);
                if (step.IsGate)
                    cell.Label = "BEGIN";
                else if (toolChange)
                    cell.Label = "CHANGE TOOL";
                else
                    cell.Label = "RUN";

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

        // While a run is in progress, tapping the currently running step reopens the
        // progress overlay if the operator had closed it.
        private void StepGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !_engine.IsRunning) return;
            if (e.ColumnIndex == _stepGrid.Columns["Video"].Index ||
                e.ColumnIndex == _stepGrid.Columns["Run"].Index) return;
            if (e.RowIndex != ActiveRowIndex()) return;

            _progressClosed = false;
            RefreshProgressVisibility();
        }

        private void StepGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _currentProject == null) return;

            if (e.ColumnIndex == _stepGrid.Columns["Video"].Index)
            {
                if (e.RowIndex < _currentProject.steps.Count)
                    PlayStepVideo(_currentProject.steps[e.RowIndex]);
                return;
            }

            if (e.ColumnIndex != _stepGrid.Columns["Run"].Index) return;
            if (_engine.IsRunning) return;
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

        // Operator override: start a full run at an arbitrary step. This is the recovery
        // path for when the saved completion state no longer matches the machine (e.g. a
        // step ran on the controller but Maestro recorded it as stopped). The step picker
        // is a modal dialog so the grid stays untouched; the per-step RUN buttons still
        // only enable the next expected step.
        private void RunFromButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null || _engine.IsRunning) return;
            if (_currentProject.steps == null || _currentProject.steps.Count == 0) return;

            int idx;
            if (!PromptForStartStep(out idx)) return;
            if (idx < 0 || idx >= _currentProject.steps.Count) return;

            _engine.RunAll(_currentProject, idx, true);
        }

        // Modal step picker for the RUN FROM override. Returns true (with the chosen
        // zero-based step index) only when the operator confirms.
        private bool PromptForStartStep(out int selectedIndex)
        {
            selectedIndex = -1;

            using (var dialog = new Form())
            {
                dialog.Text = "Run From Step (Override)";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(560, 320);
                dialog.Font = new Font("Segoe UI", 12F);

                var info = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 132,
                    Padding = new Padding(16, 16, 16, 8),
                    Text =
                        "Choose the step to start a full run from.\r\n\r\n" +
                        "OVERRIDE: this ignores the normal step order. Earlier steps are left " +
                        "as-is and will NOT run.\r\n\r\n" +
                        "Make sure the machine is set up for the chosen step (correct tool " +
                        "installed and zeroed) before continuing."
                };

                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Font = new Font("Segoe UI", 14F),
                    Location = new Point(16, 150),
                    Width = dialog.ClientSize.Width - 32,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };

                int firstNotDone = RunStateStore.FirstNotDone(_engine.State, _currentProject.id, _currentProject.steps.Count);
                for (int i = 0; i < _currentProject.steps.Count; i++)
                {
                    var step = _currentProject.steps[i];
                    bool done = RunStateStore.IsDone(_engine.State, _currentProject.id, i);
                    string suffix = done ? "  [done]" : (i == firstNotDone ? "  [next]" : "");
                    combo.Items.Add((i + 1) + ".  " + step.label + suffix);
                }
                combo.SelectedIndex = firstNotDone >= 0 ? firstNotDone : 0;

                var runButton = MakeButton("RUN FROM HERE", Color.FromArgb(150, 90, 30), 14F, 56);
                runButton.Size = new Size(220, 56);
                runButton.Location = new Point(dialog.ClientSize.Width - 236, dialog.ClientSize.Height - 72);
                runButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                runButton.DialogResult = DialogResult.OK;

                var cancelButton = MakeButton("CANCEL", Color.FromArgb(120, 120, 120), 14F, 56);
                cancelButton.Size = new Size(140, 56);
                cancelButton.Location = new Point(16, dialog.ClientSize.Height - 72);
                cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                cancelButton.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(combo);
                dialog.Controls.Add(info);
                dialog.Controls.Add(runButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = runButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(_host) != DialogResult.OK) return false;

                selectedIndex = combo.SelectedIndex;
                return selectedIndex >= 0;
            }
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

        private void Engine_ProjectCompleted(WorkflowProject project)
        {
            if (project == null) return;
            MessageBox.Show(_host,
                "Project complete: " + project.name + "\r\n\r\nPress OK to reset for another run.",
                "Project Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _engine.ResetProject(project);
            RefreshStepGrid();
        }

        private void AbortButton_Click(object sender, EventArgs e)
        {
            _engine.RequestAbort();
        }

        // Engine status/run events are raised on the background run thread. All UI work
        // (especially showing/laying out the progress overlay and starting the runtime
        // timer) must happen on the UI thread, or the controls' handles get created on
        // the worker thread and the plugin deadlocks.
        private void Engine_RunFinished()
        {
            if (InvokeRequired) { BeginInvoke((Action)Engine_RunFinished); return; }
            StopLiveRuntime();
            HideOverlay();
            RefreshProgressVisibility();
            UpdateRunButtonStates();
        }

        private void Engine_StepStatusChanged(int index, StepRunStatus status)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Engine_StepStatusChanged(index, status))); return; }
            _stepStatuses[index] = status;
            // The live runtime clock and progress view are driven by StepWorkStarted (after
            // any tool change), not the Running status, so the estimate only covers machine
            // work. Here we just react to a step ending.
            if (status != StepRunStatus.Running)
            {
                StopLiveRuntime();
                if (status == StepRunStatus.Done)
                    UpdateRuntimeCell(index);
                RefreshProgressVisibility();
            }
            UpdateRunButtonStates();
        }

        // Machine work for a step has begun (tool change, if any, is done). Start the
        // remaining-time countdown from now and show the progress view.
        private void Engine_StepWorkStarted(int index)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Engine_StepWorkStarted(index))); return; }
            _progressClosed = false;
            _fileCurrentLine = 0;
            _fileTotalLines = 0;
            _runningEstimateSeconds = _currentProject != null
                ? RunStateStore.GetLastRunSeconds(_engine.State, _currentProject.id, index)
                : 0;
            StartLiveRuntime(index);
            RefreshProgressVisibility();
        }

        private void Engine_FileProgressChanged(int current, int total)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Engine_FileProgressChanged(current, total))); return; }
            _fileCurrentLine = current;
            _fileTotalLines = total;
            if (_progressOverlay != null && _progressOverlay.Visible)
                UpdateProgressDisplay();
        }

        private void Engine_RunningChanged(bool running)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Engine_RunningChanged(running))); return; }
            _runAllButton.Enabled = !running;
            _runFromButton.Enabled = !running;
            _resetButton.Enabled = !running;
            _abortButton.Enabled = running;
            _projectCombo.Enabled = !running;
            UpdateRunButtonStates();
            RefreshProgressVisibility();
        }

        // Shows the progress overlay only while an op step is actively running and no
        // operator prompt (tool change / gate) is on screen, and only if the operator
        // hasn't dismissed it for the current step.
        private void RefreshProgressVisibility()
        {
            if (_progressOverlay == null) return;

            bool shouldShow =
                _engine.IsRunning &&
                _liveRuntimeStepIndex >= 0 &&
                !_progressClosed &&
                !_overlay.Visible &&
                IsOpStep(_liveRuntimeStepIndex);

            if (shouldShow)
            {
                UpdateProgressDisplay();
                _progressOverlay.Visible = true;
                _progressOverlay.BringToFront();
                LayoutOverlayCard(_progressOverlay, _progressCard);
            }
            else
            {
                _progressOverlay.Visible = false;
            }
        }

        private bool IsOpStep(int index)
        {
            if (_currentProject == null || _currentProject.steps == null) return false;
            if (index < 0 || index >= _currentProject.steps.Count) return false;
            return _currentProject.steps[index].IsOp;
        }

        private void UpdateProgressDisplay()
        {
            if (_currentProject == null || _liveRuntimeStepIndex < 0 ||
                _liveRuntimeStepIndex >= _currentProject.steps.Count) return;

            var step = _currentProject.steps[_liveRuntimeStepIndex];
            _progressProjectLabel.Text = _currentProject.name ?? "";
            _progressStepLabel.Text = "Step " + (_liveRuntimeStepIndex + 1) + " of " +
                _currentProject.steps.Count + ":  " + step.label;
            string file = (step.file ?? "").Trim();
            _progressFileLabel.Text = string.IsNullOrEmpty(file) ? "" : Path.GetFileName(file);

            int elapsed = Math.Max(0, (int)(DateTime.Now - _stepRunStarted).TotalSeconds);

            // Clock = estimated time remaining (countdown) when a prior run time exists;
            // otherwise a plain elapsed count-up.
            if (_runningEstimateSeconds > 0)
            {
                int remaining = Math.Max(0, _runningEstimateSeconds - elapsed);
                _progressCaption.Text = "ESTIMATED TIME REMAINING";
                _progressCountdown.Text = FormatClock(remaining);
                _progressCountdown.ForeColor = remaining > 0
                    ? Color.FromArgb(0, 90, 30)
                    : Color.FromArgb(150, 90, 30);
            }
            else
            {
                _progressCaption.Text = "ELAPSED  (no estimate yet)";
                _progressCountdown.Text = FormatClock(elapsed);
                _progressCountdown.ForeColor = Color.FromArgb(0, 90, 30);
            }

            // Bar = actual g-code file progress (% of lines executed). When the line count
            // is unknown (e.g. demo mode), fall back to a time-based fill against the
            // estimate so the bar still moves.
            _progressBar.Visible = true;
            double fraction;
            if (_fileTotalLines > 0)
                fraction = (double)_fileCurrentLine / _fileTotalLines;
            else if (_runningEstimateSeconds > 0)
                fraction = (double)elapsed / _runningEstimateSeconds;
            else
                fraction = 0;

            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            _progressBar.Value = (int)Math.Round(1000.0 * fraction);
        }

        private static string FormatClock(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            if (hours > 0)
                return hours + ":" + minutes.ToString("00") + ":" + seconds.ToString("00");
            return minutes + ":" + seconds.ToString("00");
        }

        private void ProgressCloseButton_Click(object sender, EventArgs e)
        {
            _progressClosed = true;
            _progressOverlay.Visible = false;
        }

        private void ProgressCancelButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(_host,
                    "Cancel the running operation?",
                    "Cancel Operation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _engine.RequestAbort();
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
                string toolIdent = !string.IsNullOrEmpty(tool.num) ? tool.num : tool.SizeDescription();
                _overlayBanner.Text = "CHANGE TOOL  ->  " + toolIdent;
                _overlayBanner.BackColor = Color.FromArgb(214, 120, 0);
                _confirmButton.Text = "TOOL INSTALLED";
                if (!hasImage) SetGraphicGlyph("\U0001F527");
                _overlayInstructions.Text = BuildToolText(step, stepIndex, tool);
            }

            ShowOverlay();
            RefreshProgressVisibility();
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
                   (string.IsNullOrEmpty(tool.num) ? "" : "Storage: " + tool.num + "\n") +
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
            PlayStepVideo(_promptStep);
        }

        // Opens the step's video in the OS default player. Shared by the prompt overlay
        // button and the on-demand "VIDEO" button in the step grid.
        private void PlayStepVideo(WorkflowStep step)
        {
            if (step == null || string.IsNullOrEmpty(step.video)) return;
            string path = ResolveMediaPath(step.video);
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
            RefreshProgressVisibility();
        }

        private void CancelPromptButton_Click(object sender, EventArgs e)
        {
            HideOverlay();
            _engine.CancelPrompt();
            RefreshProgressVisibility();
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
