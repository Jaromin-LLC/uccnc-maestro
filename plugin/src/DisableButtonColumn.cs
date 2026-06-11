using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Plugins
{
    public enum RunButtonMode
    {
        ReadyToRun = 0,
        Disabled = 1,
        Done = 2
    }

    public class DataGridViewDisableButtonColumn : DataGridViewButtonColumn
    {
        public DataGridViewDisableButtonColumn()
        {
            CellTemplate = new DataGridViewDisableButtonCell();
        }
    }

    public class DataGridViewDisableButtonCell : DataGridViewButtonCell
    {
        private bool _enabled = true;
        private RunButtonMode _mode = RunButtonMode.Disabled;

        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public RunButtonMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        public override object Clone()
        {
            var cell = (DataGridViewDisableButtonCell)base.Clone();
            cell.Enabled = Enabled;
            cell.Mode = Mode;
            return cell;
        }

        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds,
            int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue,
            string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            if (_mode == RunButtonMode.ReadyToRun && _enabled)
            {
                PaintCellBackground(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle, paintParts);
                Rectangle buttonArea = GetContentBounds(cellBounds, advancedBorderStyle);
                using (var brush = new SolidBrush(Color.FromArgb(0, 150, 0)))
                    graphics.FillRectangle(brush, buttonArea);
                ControlPaint.DrawBorder(graphics, buttonArea, Color.FromArgb(0, 110, 0), ButtonBorderStyle.Solid);

                string text = formattedValue == null ? "RUN" : formattedValue.ToString();
                using (var font = new Font(DataGridView.Font, FontStyle.Bold))
                {
                    TextRenderer.DrawText(graphics, text, font, buttonArea, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            if (_mode == RunButtonMode.Done)
            {
                PaintCellBackground(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle, paintParts);
                Rectangle content = GetContentBounds(cellBounds, advancedBorderStyle);
                using (var font = new Font(DataGridView.Font, FontStyle.Bold))
                {
                    TextRenderer.DrawText(graphics, "\u2713", font, content, Color.FromArgb(120, 160, 120),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            PaintCellBackground(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle, paintParts);

            Rectangle buttonAreaDisabled = GetContentBounds(cellBounds, advancedBorderStyle);
            if (Application.RenderWithVisualStyles)
                ButtonRenderer.DrawButton(graphics, buttonAreaDisabled, PushButtonState.Disabled);
            else
                ControlPaint.DrawButton(graphics, buttonAreaDisabled, ButtonState.Inactive);

            string disabledText = formattedValue == null ? string.Empty : formattedValue.ToString();
            TextRenderer.DrawText(graphics, disabledText, DataGridView.Font, buttonAreaDisabled, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void PaintCellBackground(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds,
            DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            if ((paintParts & DataGridViewPaintParts.Background) == DataGridViewPaintParts.Background)
            {
                using (var brush = new SolidBrush(cellStyle.BackColor))
                    graphics.FillRectangle(brush, cellBounds);
            }

            if ((paintParts & DataGridViewPaintParts.Border) == DataGridViewPaintParts.Border)
                PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
        }

        private Rectangle GetContentBounds(Rectangle cellBounds, DataGridViewAdvancedBorderStyle advancedBorderStyle)
        {
            Rectangle buttonArea = cellBounds;
            Rectangle adjustment = BorderWidths(advancedBorderStyle);
            buttonArea.X += adjustment.X + 4;
            buttonArea.Y += adjustment.Y + 4;
            buttonArea.Width -= adjustment.Width + 8;
            buttonArea.Height -= adjustment.Height + 8;
            return buttonArea;
        }
    }
}
