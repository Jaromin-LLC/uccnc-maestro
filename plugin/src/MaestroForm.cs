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

        public WorkflowEngine Engine { get; private set; }

        public MaestroForm(UCCNCplugin plugin)
        {
            _plugin = plugin;
            Text = "Jaromin CNC Maestro";
            Size = new Size(1180, 760);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 640);
            Font = new Font("Segoe UI", 9F);

            Engine = new WorkflowEngine(plugin, this);
            Engine.LoadData();

            _liveStatusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Padding = new Padding(8, 0, 0, 0),
                Text = "Ready"
            };

            _liveMachineLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(60, 60, 63),
                ForeColor = Color.LightGray,
                Padding = new Padding(8, 0, 0, 0),
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
            Controls.Add(_liveStatusLabel);
            Controls.Add(_liveMachineLabel);

            Engine.StatusChanged += msg => { if (!IsDisposed) _liveStatusLabel.Text = msg; };
            _adminView.DocumentSaved += () =>
            {
                Engine.ReloadDocument();
                _operatorView.ReloadProjects();
            };

            Load += MaestroForm_Load;
            FormClosing += MaestroForm_FormClosing;
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
