using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Plugins.Companion
{
    /// <summary>
    /// "Mobile" tab in the Maestro window: shows how to connect a phone (URLs + PIN),
    /// lets the operator enable/disable the server, change the port / machine name /
    /// camera URL, and rotate the pairing PIN. Apply restarts the server via a callback.
    /// </summary>
    public class CompanionView : Panel
    {
        private readonly CompanionSettings _settings;
        private readonly Func<MaestroServer> _getServer;
        private readonly Action _onApply;

        private CheckBox _enabled;
        private CheckBox _openOnLan;
        private CheckBox _requirePin;
        private TextBox _port;
        private TextBox _machineName;
        private TextBox _cameraUrl;
        private Label _pinLabel;
        private Label _urlList;
        private Label _statusLabel;

        public CompanionView(CompanionSettings settings, Func<MaestroServer> getServer, Action onApply)
        {
            _settings = settings;
            _getServer = getServer;
            _onApply = onApply;

            Dock = DockStyle.Fill;
            Padding = new Padding(16);
            AutoScroll = true;
            BuildUi();
            RefreshDisplay();
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "Phone / Tablet Companion",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 16)
            };

            var hint = new Label
            {
                Text = "Connect a phone on the same WiFi. Open the URL below in the phone browser, " +
                       "then add the phone to the home screen. Enter the PIN once to pair.",
                AutoSize = false,
                Size = new Size(560, 40),
                Location = new Point(18, 48)
            };

            _statusLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 92),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 120, 0)
            };

            var urlCaption = MakeCaption("Connect at:", 18, 120);
            _urlList = new Label
            {
                AutoSize = false,
                Size = new Size(540, 60),
                Location = new Point(20, 142),
                Font = new Font("Consolas", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 60, 140)
            };

            var pinCaption = MakeCaption("Pairing PIN:", 18, 208);
            _pinLabel = new Label
            {
                AutoSize = true,
                Location = new Point(120, 204),
                Font = new Font("Consolas", 18F, FontStyle.Bold)
            };
            var regen = new Button { Text = "New PIN", Location = new Point(240, 202), Size = new Size(90, 30) };
            regen.Click += (s, e) => { _settings.pin = CompanionSettings.GeneratePin(); RefreshDisplay(); SaveOnly(); };

            int y = 256;
            _enabled = MakeCheck("Enable companion server", 18, ref y);
            _enabled.Checked = _settings.enabled;
            _openOnLan = MakeCheck("Allow phones on the local network (otherwise localhost only)", 18, ref y);
            _openOnLan.Checked = _settings.openOnLan;
            _requirePin = MakeCheck("Require PIN to pair", 18, ref y);
            _requirePin.Checked = _settings.requirePin;

            var portCaption = MakeCaption("Port:", 18, y + 6);
            _port = new TextBox { Location = new Point(120, y + 2), Size = new Size(80, 24), Text = _settings.port.ToString() };
            y += 36;

            var nameCaption = MakeCaption("Machine name:", 18, y + 6);
            _machineName = new TextBox { Location = new Point(120, y + 2), Size = new Size(260, 24), Text = _settings.machineName };
            y += 36;

            var camCaption = MakeCaption("Camera URL:", 18, y + 6);
            _cameraUrl = new TextBox { Location = new Point(120, y + 2), Size = new Size(420, 24), Text = _settings.cameraUrl ?? "" };
            y += 44;

            var apply = new Button
            {
                Text = "Apply && Restart Server",
                Location = new Point(20, y),
                Size = new Size(200, 34),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            apply.Click += Apply_Click;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_statusLabel);
            Controls.Add(urlCaption);
            Controls.Add(_urlList);
            Controls.Add(pinCaption);
            Controls.Add(_pinLabel);
            Controls.Add(regen);
            Controls.Add(portCaption);
            Controls.Add(_port);
            Controls.Add(nameCaption);
            Controls.Add(_machineName);
            Controls.Add(camCaption);
            Controls.Add(_cameraUrl);
            Controls.Add(apply);
        }

        private Label MakeCaption(string text, int x, int yPos)
        {
            return new Label { Text = text, AutoSize = true, Location = new Point(x, yPos), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        }

        private CheckBox MakeCheck(string text, int x, ref int yPos)
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Location = new Point(x, yPos) };
            yPos += 28;
            return cb;
        }

        private void Apply_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Enter a valid port (1-65535).", "Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.enabled = _enabled.Checked;
            _settings.openOnLan = _openOnLan.Checked;
            _settings.requirePin = _requirePin.Checked;
            _settings.port = port;
            _settings.machineName = _machineName.Text.Trim();
            _settings.cameraUrl = _cameraUrl.Text.Trim();
            _settings.EnsureDefaults(_settings.machineName);

            if (_onApply != null) _onApply();
            RefreshDisplay();
        }

        private void SaveOnly()
        {
            if (_onApply != null) _onApply();
        }

        public void RefreshDisplay()
        {
            _pinLabel.Text = _settings.requirePin ? _settings.pin : "(no PIN)";

            var server = _getServer != null ? _getServer() : null;
            bool running = server != null && server.IsRunning;
            _statusLabel.Text = running ? "Server running" : "Server stopped";
            _statusLabel.ForeColor = running ? Color.FromArgb(0, 120, 0) : Color.FromArgb(160, 0, 0);

            var lines = new List<string>();
            if (running && _settings.openOnLan)
            {
                foreach (var ip in GetLanIps())
                    lines.Add("http://" + ip + ":" + _settings.port + "/");
            }
            lines.Add("http://localhost:" + _settings.port + "/   (this PC)");
            _urlList.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        private static IEnumerable<string> GetLanIps()
        {
            var result = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string s = ua.Address.ToString();
                            if (!s.StartsWith("169.254")) result.Add(s);
                        }
                    }
                }
            }
            catch { }
            if (result.Count == 0)
            {
                try
                {
                    foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                        if (a.AddressFamily == AddressFamily.InterNetwork) result.Add(a.ToString());
                }
                catch { }
            }
            return result;
        }
    }
}
