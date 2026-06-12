using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Plugins
{
    public class NewToolDialog : Form
    {
        private readonly TextBox _typeBox;
        private readonly TextBox _diaBox;
        private readonly TextBox _descBox;
        private readonly TextBox _imageBox;
        private readonly PictureBox _imagePreview;

        public ToolInfo Result { get; private set; }

        public NewToolDialog(Form owner, string mediaRoot, ToolInfo seed = null)
        {
            Text = seed == null ? "New Tool" : "Edit Tool";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 320);
            Font = new Font("Segoe UI", 9F);

            int y = 12;
            Controls.Add(MkLabel("Type", 12, y));
            _typeBox = MkText(100, y, 340);
            Controls.Add(_typeBox); y += 30;

            Controls.Add(MkLabel("Diameter", 12, y));
            _diaBox = MkText(100, y, 340);
            Controls.Add(_diaBox); y += 30;

            Controls.Add(MkLabel("Description", 12, y));
            _descBox = MkText(100, y, 340);
            Controls.Add(_descBox); y += 34;

            _imageBox = new TextBox { Visible = false };
            Controls.Add(_imageBox);
            Controls.Add(MkLabel("Tool Image", 12, y));
            _imagePreview = new PictureBox
            {
                Location = new Point(100, y),
                Size = new Size(160, 120),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(248, 248, 248),
                Cursor = Cursors.Hand
            };
            _imagePreview.Paint += (s, e) =>
            {
                if (_imagePreview.Image == null)
                    TextRenderer.DrawText(e.Graphics, "Click to add\nimage", _imagePreview.Font,
                        _imagePreview.ClientRectangle, Color.Gray,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            };
            _imagePreview.Click += (s, e) => PickImage(mediaRoot);
            Controls.Add(_imagePreview); y += 130;

            var okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(280, y), Width = 75 };
            var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(365, y), Width = 75 };
            okBtn.Click += OkBtn_Click;
            Controls.Add(okBtn);
            Controls.Add(cancelBtn);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            if (seed != null)
            {
                _typeBox.Text = seed.type ?? "";
                _diaBox.Text = seed.diameter ?? "";
                _descBox.Text = seed.desc ?? "";
                _imageBox.Text = seed.image ?? "";
                LoadImagePreview(mediaRoot);
            }
        }

        private void PickImage(string mediaRoot)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                if (string.IsNullOrEmpty(mediaRoot)) mediaRoot = MaestroPaths.MaestroRoot + "\\Media";
                string toolsDir = Path.Combine(mediaRoot, "Tools");
                Directory.CreateDirectory(toolsDir);
                string destName = Path.GetFileName(dlg.FileName);
                string destPath = Path.Combine(toolsDir, destName);
                File.Copy(dlg.FileName, destPath, true);
                _imageBox.Text = "Tools\\" + destName;
                LoadImagePreview(mediaRoot);
            }
        }

        private void LoadImagePreview(string mediaRoot)
        {
            if (_imagePreview.Image != null) { var old = _imagePreview.Image; _imagePreview.Image = null; old.Dispose(); }
            if (string.IsNullOrEmpty(_imageBox.Text)) return;
            if (string.IsNullOrEmpty(mediaRoot)) mediaRoot = MaestroPaths.MaestroRoot + "\\Media";
            string path = Path.Combine(mediaRoot, _imageBox.Text);
            if (!File.Exists(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var tmp = Image.FromStream(fs))
                    _imagePreview.Image = new Bitmap(tmp);
            }
            catch { }
        }

        private void OkBtn_Click(object sender, EventArgs e)
        {
            Result = new ToolInfo
            {
                type = _typeBox.Text.Trim(),
                diameter = _diaBox.Text.Trim(),
                desc = _descBox.Text.Trim(),
                image = _imageBox.Text.Trim()
            };
        }

        private static Label MkLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y + 3), AutoSize = true };
        }

        private static TextBox MkText(int x, int y, int width)
        {
            return new TextBox { Location = new Point(x, y), Width = width };
        }
    }
}
