using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Plugins
{
    /// <summary>
    /// Drag-and-drop editor for ordered pre/post operation sequences.
    /// </summary>
    public class OpsSequenceEditor : UserControl
    {
        public event Action Changed;

        private static readonly string[] PaletteOpIds = BuildSortedPaletteOpIds();
        private static readonly Dictionary<string, string> OpLabels = BuildOpLabels();

        private readonly ListBox _paletteList;
        private readonly ListBox _preOpsList;
        private readonly ListBox _postOpsList;
        private Button _removeBtn;
        private readonly ToolTip _toolTip;

        private WorkflowStep _step;
        private bool _loading;
        private Point _dragStart;
        private bool _dragPending;

        private enum SequenceKind { PreOps, PostOps }

        private sealed class OpDragPayload
        {
            public bool FromPalette;
            public SequenceKind SourceKind;
            public int SourceIndex;
            public string OpId;
        }

        private sealed class OpListEntry
        {
            public WorkflowOp Op;
            public int Index;
            public SequenceKind Kind;

            public string DisplayText
            {
                get
                {
                    string label = GetOpLabel(Op != null ? Op.id : "");
                    if (Op != null && Op.id == AutoOpIds.CustomMdi && string.IsNullOrWhiteSpace(Op.mdi))
                        label += " (not set)";
                    return (Index + 1) + ". " + label;
                }
            }

            public override string ToString() { return DisplayText; }
        }

        public OpsSequenceEditor()
        {
            Height = 180;
            MinimumSize = new Size(640, 160);

            _toolTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400, ShowAlways = true };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _paletteList = CreatePaletteListBox();
            _preOpsList = CreateSequenceListBox(SequenceKind.PreOps);
            _postOpsList = CreateSequenceListBox(SequenceKind.PostOps);

            root.Controls.Add(BuildListPanel("Available operations", _paletteList), 0, 0);
            root.Controls.Add(BuildListPanel("Pre Ops (run order)", _preOpsList), 1, 0);
            root.Controls.Add(BuildListPanel("Post Ops (run order)", _postOpsList), 2, 0);
            root.Controls.Add(BuildButtonColumn(), 3, 0);

            Controls.Add(root);

            PopulatePalette();
            WireDragDrop(_paletteList, true, SequenceKind.PreOps);
            WireDragDrop(_preOpsList, false, SequenceKind.PreOps);
            WireDragDrop(_postOpsList, false, SequenceKind.PostOps);

            _preOpsList.SelectedIndexChanged += SequenceSelectionChanged;
            _postOpsList.SelectedIndexChanged += SequenceSelectionChanged;
        }

        private static string[] BuildSortedPaletteOpIds()
        {
            var ids = new List<string>
            {
                AutoOpIds.MoveToolChange,
                AutoOpIds.ToolPrompt,
                AutoOpIds.AutoZero,
                AutoOpIds.SpindleOff,
                AutoOpIds.GotoWorkZero,
                AutoOpIds.ParkG28,
                AutoOpIds.ParkG30,
                AutoOpIds.ParkCustom,
                AutoOpIds.CustomMdi
            };
            ids.Sort((a, b) => string.Compare(
                FormatOpLabel(a), FormatOpLabel(b), StringComparison.OrdinalIgnoreCase));
            return ids.ToArray();
        }

        private static Dictionary<string, string> BuildOpLabels()
        {
            var map = new Dictionary<string, string>();
            foreach (string id in PaletteOpIds)
                map[id] = FormatOpLabel(id);
            return map;
        }

        public static string GetOpLabel(string opId)
        {
            string label;
            if (opId != null && OpLabels.TryGetValue(opId, out label))
                return label;
            return opId ?? "";
        }

        private static string FormatOpLabel(string opId)
        {
            switch (opId)
            {
                case AutoOpIds.MoveToolChange: return "Move to tool change";
                case AutoOpIds.ToolPrompt: return "Tool install prompt";
                case AutoOpIds.AutoZero: return "Auto zero (probe)";
                case AutoOpIds.SpindleOff: return "Spindle off";
                case AutoOpIds.GotoWorkZero: return "Go to work zero";
                case AutoOpIds.ParkG28: return "Park (G28)";
                case AutoOpIds.ParkG30: return "Park (G30)";
                case AutoOpIds.ParkCustom: return "Park (custom position)";
                case AutoOpIds.CustomMdi: return "Custom MDI";
                default: return opId ?? "";
            }
        }

        private void PopulatePalette()
        {
            _paletteList.Items.Clear();
            foreach (string id in PaletteOpIds)
                _paletteList.Items.Add(GetOpLabel(id));
        }

        private ListBox CreatePaletteListBox()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One,
                Sorted = false
            };
        }

        private ListBox CreateSequenceListBox(SequenceKind kind)
        {
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                AllowDrop = true,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 18
            };
            list.DrawItem += SequenceList_DrawItem;
            list.MouseMove += (s, e) => UpdateSequenceTooltip(list, e.Location);
            list.MouseLeave += (s, e) => _toolTip.RemoveAll();
            list.DoubleClick += (s, e) => OnSequenceDoubleClick(list);
            return list;
        }

        private Panel BuildListPanel(string title, ListBox list)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 0) };
            panel.Controls.Add(list);
            panel.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 18,
                AutoSize = false
            });
            return panel;
        }

        private Control BuildButtonColumn()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 22, 0, 0) };
            var removeBtn = new Button
            {
                Text = "\uE74D",
                Width = 36,
                Height = 30,
                Enabled = false,
                FlatStyle = FlatStyle.Standard,
                Font = new Font("Segoe MDL2 Assets", 11f),
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = true,
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            _toolTip.SetToolTip(removeBtn, "Remove selected operation");
            removeBtn.Click += (s, e) => RemoveSelected();
            panel.Controls.Add(removeBtn);
            _removeBtn = removeBtn;
            return panel;
        }

        private void SequenceList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            var list = (ListBox)sender;
            var entry = list.Items[e.Index] as OpListEntry;
            if (entry == null) return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? SystemColors.Highlight : e.BackColor;
            Color fore = selected ? SystemColors.HighlightText : SystemColors.ControlText;

            if (!selected && entry.Op != null && entry.Op.id == AutoOpIds.CustomMdi)
            {
                fore = string.IsNullOrWhiteSpace(entry.Op.mdi)
                    ? Color.FromArgb(170, 70, 70)
                    : Color.FromArgb(50, 50, 50);
            }

            using (var backBrush = new SolidBrush(back))
                e.Graphics.FillRectangle(backBrush, e.Bounds);

            TextRenderer.DrawText(e.Graphics, entry.DisplayText, e.Font, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            e.DrawFocusRectangle();
        }

        private void UpdateSequenceTooltip(ListBox list, Point location)
        {
            int index = list.IndexFromPoint(location);
            if (index < 0)
            {
                _toolTip.RemoveAll();
                return;
            }

            var entry = list.Items[index] as OpListEntry;
            if (entry == null || entry.Op == null || entry.Op.id != AutoOpIds.CustomMdi)
            {
                _toolTip.RemoveAll();
                return;
            }

            string tip = string.IsNullOrWhiteSpace(entry.Op.mdi)
                ? "No MDI command configured — nothing will run.\nDouble-click to set the command."
                : entry.Op.mdi.Trim();
            _toolTip.SetToolTip(list, tip);
        }

        private void OnSequenceDoubleClick(ListBox list)
        {
            if (_step == null || list.SelectedItem == null) return;
            var entry = list.SelectedItem as OpListEntry;
            if (entry == null || entry.Op == null || entry.Op.id != AutoOpIds.CustomMdi) return;

            if (EditCustomMdi(entry.Op))
            {
                RefreshSequenceList(_preOpsList, SequenceKind.PreOps);
                RefreshSequenceList(_postOpsList, SequenceKind.PostOps);
                list.SelectedIndex = entry.Index;
                RaiseChanged();
            }
        }

        private bool EditCustomMdi(WorkflowOp op)
        {
            Form owner = FindForm();
            using (var dlg = new Form())
            {
                dlg.Text = "Custom MDI command";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(440, 200);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;

                var label = new Label
                {
                    Text = "G-code / MDI command to run for this step:",
                    Location = new Point(12, 12),
                    AutoSize = true
                };
                var box = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Location = new Point(12, 36),
                    Size = new Size(416, 110),
                    Text = op.mdi ?? "",
                    Font = new Font(Font.FontFamily, 9.5f)
                };
                var okBtn = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new Point(272, 156),
                    Width = 75
                };
                var cancelBtn = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(353, 156),
                    Width = 75
                };

                dlg.Controls.Add(label);
                dlg.Controls.Add(box);
                dlg.Controls.Add(okBtn);
                dlg.Controls.Add(cancelBtn);
                dlg.AcceptButton = okBtn;
                dlg.CancelButton = cancelBtn;

                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                op.mdi = box.Text;
                return true;
            }
        }

        private void WireDragDrop(ListBox list, bool isPalette, SequenceKind kind)
        {
            list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragStart = e.Location;
                _dragPending = true;
            };

            list.MouseMove += (s, e) =>
            {
                if (!_dragPending || e.Button != MouseButtons.Left) return;
                if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width &&
                    Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height)
                    return;

                _dragPending = false;
                var payload = new OpDragPayload();
                if (isPalette)
                {
                    int idx = list.IndexFromPoint(_dragStart);
                    if (idx < 0 || idx >= PaletteOpIds.Length) return;
                    payload.FromPalette = true;
                    payload.OpId = PaletteOpIds[idx];
                }
                else
                {
                    int idx = list.IndexFromPoint(_dragStart);
                    if (idx < 0) return;
                    payload.FromPalette = false;
                    payload.SourceKind = kind;
                    payload.SourceIndex = idx;
                }

                list.DoDragDrop(payload, DragDropEffects.Copy | DragDropEffects.Move);
            };

            list.MouseUp += (s, e) => { _dragPending = false; };

            if (!isPalette)
            {
                list.DragEnter += Sequence_DragEnter;
                list.DragOver += (s, e) => Sequence_DragOver(list, kind, e);
                list.DragDrop += (s, e) => Sequence_DragDrop(list, kind, e);
            }
        }

        private void Sequence_DragEnter(object sender, DragEventArgs e)
        {
            if (GetPayload(e) != null)
                e.Effect = e.AllowedEffect;
        }

        private void Sequence_DragOver(ListBox target, SequenceKind targetKind, DragEventArgs e)
        {
            var payload = GetPayload(e);
            if (payload == null) return;

            if (payload.FromPalette)
                e.Effect = DragDropEffects.Copy;
            else if (payload.SourceKind == targetKind)
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.Move;
        }

        private void Sequence_DragDrop(ListBox target, SequenceKind targetKind, DragEventArgs e)
        {
            var payload = GetPayload(e);
            if (payload == null || _step == null) return;

            Point client = target.PointToClient(new Point(e.X, e.Y));
            int dropIndex = target.IndexFromPoint(client);
            if (dropIndex < 0) dropIndex = target.Items.Count;

            var targetList = GetModelList(targetKind);

            if (payload.FromPalette)
            {
                targetList.Insert(dropIndex, new WorkflowOp(payload.OpId));
            }
            else
            {
                var sourceList = GetModelList(payload.SourceKind);
                if (payload.SourceIndex < 0 || payload.SourceIndex >= sourceList.Count) return;

                WorkflowOp moving = sourceList[payload.SourceIndex];
                sourceList.RemoveAt(payload.SourceIndex);

                if (payload.SourceKind == targetKind && payload.SourceIndex < dropIndex)
                    dropIndex--;

                if (dropIndex < 0) dropIndex = 0;
                if (dropIndex > targetList.Count) dropIndex = targetList.Count;
                targetList.Insert(dropIndex, moving);
            }

            RefreshSequenceList(_preOpsList, SequenceKind.PreOps);
            RefreshSequenceList(_postOpsList, SequenceKind.PostOps);
            if (dropIndex >= 0 && dropIndex < target.Items.Count)
                target.SelectedIndex = dropIndex;

            UpdateRemoveButtonState();
            RaiseChanged();
        }

        private static OpDragPayload GetPayload(DragEventArgs e)
        {
            return e.Data.GetData(typeof(OpDragPayload)) as OpDragPayload;
        }

        private List<WorkflowOp> GetModelList(SequenceKind kind)
        {
            if (_step == null) return new List<WorkflowOp>();
            _step.EnsureOpsNotNull();
            return kind == SequenceKind.PreOps ? _step.preOps : _step.postOps;
        }

        public void Bind(WorkflowStep step)
        {
            _loading = true;
            try
            {
                _step = step;
                if (_step != null) _step.EnsureOpsNotNull();
                RefreshSequenceList(_preOpsList, SequenceKind.PreOps);
                RefreshSequenceList(_postOpsList, SequenceKind.PostOps);
                UpdateRemoveButtonState();
            }
            finally
            {
                _loading = false;
            }
        }

        public void Clear()
        {
            _loading = true;
            try
            {
                _step = null;
                _preOpsList.Items.Clear();
                _postOpsList.Items.Clear();
                UpdateRemoveButtonState();
            }
            finally
            {
                _loading = false;
            }
        }

        private void RefreshSequenceList(ListBox list, SequenceKind kind)
        {
            int keep = list.SelectedIndex;
            list.Items.Clear();
            if (_step == null) return;

            var ops = GetModelList(kind);
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op == null) continue;
                list.Items.Add(new OpListEntry { Op = op, Index = i, Kind = kind });
            }

            if (keep >= 0 && keep < list.Items.Count)
                list.SelectedIndex = keep;
        }

        private OpListEntry GetSelectedEntry()
        {
            if (_preOpsList.Focused && _preOpsList.SelectedItem is OpListEntry)
                return (OpListEntry)_preOpsList.SelectedItem;
            if (_postOpsList.Focused && _postOpsList.SelectedItem is OpListEntry)
                return (OpListEntry)_postOpsList.SelectedItem;
            if (_preOpsList.SelectedItem is OpListEntry)
                return (OpListEntry)_preOpsList.SelectedItem;
            if (_postOpsList.SelectedItem is OpListEntry)
                return (OpListEntry)_postOpsList.SelectedItem;
            return null;
        }

        private void RemoveSelected()
        {
            if (_step == null) return;
            var entry = GetSelectedEntry();
            if (entry == null) return;

            var list = GetModelList(entry.Kind);
            if (entry.Index < 0 || entry.Index >= list.Count) return;
            list.RemoveAt(entry.Index);

            RefreshSequenceList(_preOpsList, SequenceKind.PreOps);
            RefreshSequenceList(_postOpsList, SequenceKind.PostOps);
            UpdateRemoveButtonState();
            RaiseChanged();
        }

        private void SequenceSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (sender == _preOpsList && _preOpsList.SelectedIndex >= 0)
                _postOpsList.ClearSelected();
            else if (sender == _postOpsList && _postOpsList.SelectedIndex >= 0)
                _preOpsList.ClearSelected();
            UpdateRemoveButtonState();
        }

        private void UpdateRemoveButtonState()
        {
            bool canRemove = GetSelectedEntry() != null;
            _removeBtn.Enabled = canRemove;
            _removeBtn.ForeColor = canRemove ? Color.FromArgb(80, 80, 80) : Color.FromArgb(180, 180, 180);
        }

        private void RaiseChanged()
        {
            if (_loading) return;
            if (Changed != null) Changed();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Delete && _removeBtn.Enabled)
            {
                RemoveSelected();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
