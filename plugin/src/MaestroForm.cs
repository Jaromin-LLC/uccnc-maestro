using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Plugins
{
    public class MaestroForm : Form
    {
        private readonly UCCNCplugin _plugin;
        private readonly TabControl _tabs;
        private readonly TabPage _operatorPage;
        private readonly TabPage _adminPage;
        private readonly OperatorView _operatorView;
        private readonly AdminView _adminView;
        private readonly Label _liveStatusLabel;
        private readonly Label _liveMachineLabel;
        private bool _mustClose;
        private IntPtr _uccncHandle = IntPtr.Zero;

        public WorkflowEngine Engine { get; private set; }

        public MaestroForm(UCCNCplugin plugin)
        {
            _plugin = plugin;
            Text = "(uc)CNC Maestro  -  build " + BuildInfo.Id;
            Icon = LoadEmbeddedIcon();
            Size = new Size(1180, 760);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 640);
            Font = new Font("Segoe UI", 9F);

            Engine = new WorkflowEngine(plugin, this);
            Engine.LoadData();

            _liveStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(30, 30, 30),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoEllipsis = true,
                Text = "Ready"
            };

            _liveMachineLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(90, 90, 90),
                Font = new Font("Segoe UI", 9F),
                AutoEllipsis = true,
                Text = "Machine: Idle"
            };

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _operatorView = new OperatorView(this, Engine);
            _adminView = new AdminView(this, Engine);

            _operatorPage = new TabPage("Operator") { Padding = new Padding(8) };
            var opPage = _operatorPage;
            opPage.Controls.Add(_operatorView);
            _operatorView.Dock = DockStyle.Fill;

            _adminPage = new TabPage("Admin") { Padding = new Padding(8) };
            _adminPage.Controls.Add(_adminView);
            _adminView.Dock = DockStyle.Fill;

            _tabs.TabPages.Add(opPage);
            _tabs.TabPages.Add(_adminPage);

            // Lock the Admin tab while an operation is running.
            _tabs.Selecting += Tabs_Selecting;
            Engine.RunningChanged += running =>
            {
                if (IsDisposed) return;
                _adminPage.Text = running ? "Admin (locked)" : "Admin";
                if (running && _tabs.SelectedTab == _adminPage)
                    _tabs.SelectedTab = _operatorPage;
            };

            Controls.Add(_tabs);
            Controls.Add(BuildStatusBar());

            Engine.StatusChanged += msg => { if (!IsDisposed) _liveStatusLabel.Text = msg; };
            _adminView.DocumentSaved += () =>
            {
                Engine.ReloadDocument();
                _operatorView.ReloadProjects();
            };

            Load += MaestroForm_Load;
            FormClosing += MaestroForm_FormClosing;
        }

        // Bottom status bar: a transparent (form-colored) strip set off from the main
        // screen by a recessed top edge, showing the live status and machine state.
        private Panel BuildStatusBar()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SystemColors.Control,
                Padding = new Padding(12, 8, 12, 6)
            };
            bar.Paint += StatusBar_Paint;

            var textArea = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SystemColors.Control
            };
            textArea.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            textArea.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));
            textArea.Controls.Add(_liveStatusLabel, 0, 0);
            textArea.Controls.Add(_liveMachineLabel, 0, 1);

            bar.Controls.Add(textArea);
            return bar;
        }

        // Classic recessed status-bar edge: a dark line above a light line along the top.
        private static void StatusBar_Paint(object sender, PaintEventArgs e)
        {
            var bar = (Control)sender;
            using (var shadow = new Pen(SystemColors.ControlDark))
                e.Graphics.DrawLine(shadow, 0, 0, bar.Width, 0);
            using (var highlight = new Pen(SystemColors.ControlLightLight))
                e.Graphics.DrawLine(highlight, 0, 1, bar.Width, 1);
        }

        // The window icon is embedded in the DLL as a manifest resource (see make.ps1)
        // so it ships inside the binary - no file path or installer change is needed.
        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("UccncMaestro.icon.ico"))
                {
                    if (stream == null) return null;
                    return new Icon(stream);
                }
            }
            catch { return null; }
        }

        private void Tabs_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (e.TabPage == _adminPage && Engine.IsRunning)
            {
                e.Cancel = true;
                _liveStatusLabel.Text = "Admin is locked while an operation is running.";
            }
        }

        private void MaestroForm_Load(object sender, EventArgs e)
        {
            CheckForIllegalCrossThreadCalls = false;
            _operatorView.ReloadProjects();
            _adminView.LoadDocument(Engine.Document);
        }

        /// <summary>
        /// Shows Maestro as an owned window of the UCCNC main window so UCCNC
        /// cannot cover it when cycle start raises the controller window.
        /// </summary>
        public void ShowOwnedByUccnc()
        {
            _uccncHandle = UccncWindow.GetMainHandle();

            if (!Visible)
            {
                if (_uccncHandle != IntPtr.Zero)
                    Show(new WindowWrapper(_uccncHandle));
                else
                    Show();
            }
            else if (_uccncHandle != IntPtr.Zero && IsHandleCreated)
            {
                UccncWindow.SetOwner(Handle, _uccncHandle);
            }

            BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_uccncHandle == IntPtr.Zero)
                _uccncHandle = UccncWindow.GetMainHandle();

            if (_uccncHandle != IntPtr.Zero)
                UccncWindow.SetOwner(Handle, _uccncHandle);
        }

        public void UpdateLiveStatus()
        {
            if (_plugin.UC == null) return;

            string cycle = _plugin.UC.GetLED(54) ? "CYCLE RUNNING" : "Idle";
            string line = "";
            try { line = "Line " + _plugin.UC.Getcurrentgcodelinenumber(); } catch { }

            string engine = Engine.IsRunning ? " | Maestro RUNNING" : "";
            _liveMachineLabel.Text = "Machine: " + cycle + " | " + line + engine;
        }

        public void CloseFormSafe()
        {
            if (_mustClose) return;
            Thread t = new Thread(CloseFormThread);
            t.IsBackground = true;
            t.Start();
        }

        private void CloseFormThread()
        {
            _plugin.loopstop = true;
            while (_plugin.loopworking) Thread.Sleep(10);
            _mustClose = true;
            try { Invoke(new MethodInvoker(Close)); } catch { }
        }

        private void MaestroForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_mustClose)
            {
                e.Cancel = true;
                CloseFormSafe();
            }
        }
    }
}
