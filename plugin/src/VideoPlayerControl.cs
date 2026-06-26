using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ElementHost = System.Windows.Forms.Integration.ElementHost;
using WpfMediaElement = System.Windows.Controls.MediaElement;
using WpfMediaState = System.Windows.Controls.MediaState;
using WpfStretch = System.Windows.Media.Stretch;
using WpfGrid = System.Windows.Controls.Grid;
using WpfRotateTransform = System.Windows.Media.RotateTransform;
using WpfBrushes = System.Windows.Media.Brushes;

namespace Plugins
{
    // A self-contained WinForms control that plays a video file in-app using a WPF
    // MediaElement hosted via ElementHost. All WPF types are confined to this file (with
    // aliases) so the rest of the WinForms UI is unaffected. Playback uses the OS media
    // stack, so standard MP4/H.264 (typical Android recordings) plays with no extra
    // binaries shipped. If the OS cannot decode the file, PlaybackFailed is raised so the
    // caller can offer the external-player fallback.
    public sealed class VideoPlayerControl : UserControl
    {
        private readonly ElementHost _host;
        private readonly WpfMediaElement _media;
        private readonly WpfGrid _grid;
        private readonly Timer _tick;

        private readonly Panel _bar;
        private readonly Button _playPause;
        private readonly Button _restart;
        private readonly Button _rotate;
        private readonly TrackBar _seek;
        private readonly Label _time;

        private bool _isPlaying;
        private bool _hasDuration;
        private double _durationSeconds;
        private bool _syncing;
        private int _rotation;

        // Raised when the file cannot be played in-app (missing codec, corrupt file, etc.).
        public event Action<string> PlaybackFailed;

        public VideoPlayerControl()
        {
            BackColor = Color.FromArgb(20, 20, 20);

            _media = new WpfMediaElement
            {
                LoadedBehavior = WpfMediaState.Manual,
                UnloadedBehavior = WpfMediaState.Manual,
                ScrubbingEnabled = true,
                Stretch = WpfStretch.Uniform,
                Volume = 0.8
            };
            _media.MediaOpened += Media_MediaOpened;
            _media.MediaEnded += Media_MediaEnded;
            _media.MediaFailed += Media_MediaFailed;

            // The MediaElement lives in a WPF Grid so a LayoutTransform rotation (to correct
            // phone-recorded orientation) is measured/letterboxed correctly within the host.
            _grid = new WpfGrid { Background = WpfBrushes.Black };
            _grid.Children.Add(_media);

            _host = new ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Child = _grid
            };

            _bar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = Color.FromArgb(38, 38, 40),
                Padding = new Padding(8, 8, 8, 8)
            };

            _playPause = MakeBarButton("Pause");
            _playPause.Dock = DockStyle.Left;
            _playPause.Width = 96;
            _playPause.Click += (s, e) => TogglePlayPause();

            _restart = MakeBarButton("Restart");
            _restart.Dock = DockStyle.Left;
            _restart.Width = 96;
            _restart.Click += (s, e) => Restart();

            _rotate = MakeBarButton("Rotate");
            _rotate.Dock = DockStyle.Left;
            _rotate.Width = 96;
            _rotate.Click += (s, e) => RotateClockwise();

            _time = new Label
            {
                Dock = DockStyle.Right,
                Width = 130,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "0:00 / 0:00"
            };

            _seek = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                SmallChange = 1,
                LargeChange = 10
            };
            _seek.ValueChanged += Seek_ValueChanged;

            // Add order matters for docking: Fill first, then docked edges. Among Dock.Left
            // controls the last one added sits leftmost, so add them right-to-left to get
            // Play | Restart | Rotate.
            _bar.Controls.Add(_seek);
            _bar.Controls.Add(_time);
            _bar.Controls.Add(_rotate);
            _bar.Controls.Add(_restart);
            _bar.Controls.Add(_playPause);

            Controls.Add(_host);
            Controls.Add(_bar);

            _tick = new Timer { Interval = 250 };
            _tick.Tick += Tick_Tick;
        }

        private static Button MakeBarButton(string text)
        {
            return new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 70, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Height = 42
            };
        }

        // Loads and starts playback of the given absolute file path.
        public void Play(string absolutePath)
        {
            try
            {
                _hasDuration = false;
                _durationSeconds = 0;

                // WPF MediaElement ignores the MP4 rotation metadata phones embed, so read
                // it ourselves and correct the display orientation.
                int detected;
                _rotation = TryGetMp4Rotation(absolutePath, out detected) ? detected : 0;
                ApplyRotation();

                _media.Source = new Uri(absolutePath, UriKind.Absolute);
                _media.Play();
                _isPlaying = true;
                _playPause.Text = "Pause";
                _tick.Start();
            }
            catch (Exception ex)
            {
                RaiseFailed(ex.Message);
            }
        }

        // Manual orientation override for clips whose metadata is missing or wrong.
        public void RotateClockwise()
        {
            _rotation = (_rotation + 90) % 360;
            ApplyRotation();
        }

        private void ApplyRotation()
        {
            _media.LayoutTransform = (_rotation % 360 == 0) ? null : new WpfRotateTransform(_rotation);
        }

        public void TogglePlayPause()
        {
            if (_isPlaying) { _media.Pause(); _isPlaying = false; _playPause.Text = "Play"; }
            else { _media.Play(); _isPlaying = true; _playPause.Text = "Pause"; }
        }

        public void Restart()
        {
            _media.Position = TimeSpan.Zero;
            _media.Play();
            _isPlaying = true;
            _playPause.Text = "Pause";
        }

        // Stops playback and releases the file handle. Safe to call repeatedly.
        public void StopAndRelease()
        {
            _tick.Stop();
            _isPlaying = false;
            try { _media.Stop(); _media.Close(); _media.Source = null; }
            catch { }
        }

        private void Media_MediaOpened(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                _durationSeconds = _media.NaturalDuration.TimeSpan.TotalSeconds;
                _hasDuration = _durationSeconds > 0;
            }
            UpdateTimeLabel(0);
        }

        private void Media_MediaEnded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Pause at the final frame rather than rewinding, and let the operator replay.
            _media.Pause();
            _isPlaying = false;
            _playPause.Text = "Play";
        }

        private void Media_MediaFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
        {
            string msg = e != null && e.ErrorException != null ? e.ErrorException.Message : "Unknown playback error.";
            RaiseFailed(msg);
        }

        private void RaiseFailed(string message)
        {
            _tick.Stop();
            _isPlaying = false;
            var handler = PlaybackFailed;
            if (handler != null) handler(message);
        }

        private void Tick_Tick(object sender, EventArgs e)
        {
            if (!_hasDuration) return;
            double pos = _media.Position.TotalSeconds;
            int val = (int)Math.Round((pos / _durationSeconds) * _seek.Maximum);
            if (val < _seek.Minimum) val = _seek.Minimum;
            if (val > _seek.Maximum) val = _seek.Maximum;

            _syncing = true;
            _seek.Value = val;
            _syncing = false;

            UpdateTimeLabel(pos);
        }

        private void Seek_ValueChanged(object sender, EventArgs e)
        {
            if (_syncing || !_hasDuration) return;
            double frac = (double)_seek.Value / _seek.Maximum;
            double target = frac * _durationSeconds;
            _media.Position = TimeSpan.FromSeconds(target);
            UpdateTimeLabel(target);
        }

        private void UpdateTimeLabel(double posSeconds)
        {
            _time.Text = FormatTime(posSeconds) + " / " + FormatTime(_durationSeconds);
        }

        private static string FormatTime(double totalSeconds)
        {
            if (totalSeconds < 0 || double.IsNaN(totalSeconds)) totalSeconds = 0;
            int t = (int)Math.Round(totalSeconds);
            int m = t / 60;
            int s = t % 60;
            return m + ":" + s.ToString("00");
        }

        // --- MP4 rotation metadata --------------------------------------------------------
        // Reads the rotation angle (0/90/180/270) from the first track's tkhd transform
        // matrix in an MP4/MOV container. Returns false (and 0) for anything it can't parse.
        private static bool TryGetMp4Rotation(string path, out int degrees)
        {
            degrees = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long fileLen = fs.Length;
                    long pos = 0;
                    var hdr = new byte[16];
                    while (pos + 8 <= fileLen)
                    {
                        fs.Position = pos;
                        if (fs.Read(hdr, 0, 8) != 8) break;
                        long size = ReadU32(hdr, 0);
                        string type = Ascii(hdr, 4, 4);
                        long headerSize = 8;
                        if (size == 1)
                        {
                            if (fs.Read(hdr, 8, 8) != 8) break;
                            size = (long)ReadU64(hdr, 8);
                            headerSize = 16;
                        }
                        else if (size == 0)
                        {
                            size = fileLen - pos;
                        }
                        if (size < headerSize) break;

                        if (type == "moov")
                        {
                            long contentLen = size - headerSize;
                            if (contentLen <= 0 || contentLen > 64L * 1024 * 1024) return false;
                            var moov = new byte[contentLen];
                            fs.Position = pos + headerSize;
                            if (!ReadFully(fs, moov, (int)contentLen)) return false;
                            return TryRotationFromContainer(moov, 0, moov.Length, true, out degrees);
                        }
                        pos += size;
                    }
                }
            }
            catch { }
            return false;
        }

        // Walks child boxes in [start,end). When parsingMoov, recurse into 'trak' boxes;
        // otherwise (inside a trak) parse the first 'tkhd' found.
        private static bool TryRotationFromContainer(byte[] buf, int start, int end, bool parsingMoov, out int degrees)
        {
            degrees = 0;
            int p = start;
            while (p + 8 <= end)
            {
                long size = ReadU32(buf, p);
                string type = Ascii(buf, p + 4, 4);
                int hs = 8;
                if (size == 1) { size = (long)ReadU64(buf, p + 8); hs = 16; }
                else if (size == 0) size = end - p;
                if (size < hs || p + size > end) break;

                if (parsingMoov && type == "trak")
                {
                    int d;
                    if (TryRotationFromContainer(buf, p + hs, (int)(p + size), false, out d) && d != 0)
                    {
                        degrees = d;
                        return true;
                    }
                }
                else if (!parsingMoov && type == "tkhd")
                {
                    return TryParseTkhd(buf, p + hs, out degrees);
                }
                p += (int)size;
            }
            return false;
        }

        private static bool TryParseTkhd(byte[] buf, int q, out int degrees)
        {
            degrees = 0;
            byte version = buf[q];
            q += 4; // version (1) + flags (3)
            q += (version == 1) ? (8 + 8 + 4 + 4 + 8) : (4 + 4 + 4 + 4 + 4);
            q += 8 + 2 + 2 + 2 + 2; // reserved(8) + layer + alternate_group + volume + reserved
            if (q + 8 > buf.Length) return false;
            // Unity matrix: a (16.16) and b (16.16) give the rotation.
            double a = ReadS32(buf, q) / 65536.0;
            double b = ReadS32(buf, q + 4) / 65536.0;
            double ang = Math.Atan2(b, a) * 180.0 / Math.PI;
            int d = ((int)Math.Round(ang / 90.0) * 90) % 360;
            if (d < 0) d += 360;
            degrees = d;
            return true;
        }

        private static long ReadU32(byte[] b, int o)
        {
            return ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];
        }

        private static ulong ReadU64(byte[] b, int o)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[o + i];
            return v;
        }

        private static int ReadS32(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }

        private static string Ascii(byte[] b, int o, int len)
        {
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = (char)b[o + i];
            return new string(chars);
        }

        private static bool ReadFully(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAndRelease();
                if (_tick != null) _tick.Dispose();
                if (_host != null) _host.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
