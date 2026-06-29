using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Plugins.Companion
{
    /// <summary>
    /// Embedded local-network HTTP server: serves the companion PWA, exposes a REST API
    /// for status/control, and pushes live status over Server-Sent Events. No external
    /// dependencies (uses the in-box HttpListener), so it ships inside the plugin DLL and
    /// also runs in the standalone test host.
    /// </summary>
    public class MaestroServer
    {
        private readonly IMaestroController _controller;
        private readonly CompanionSettings _settings;
        private readonly IWebAssetProvider _assets;
        private readonly Action<string> _log;
        private readonly string _tokenStorePath;

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private string _boundUrl = "";
        private MaestroBeacon _beacon;

        // Pairing tokens -> client label.
        private readonly object _tokenLock = new object();
        private Dictionary<string, ClientToken> _tokens = new Dictionary<string, ClientToken>();

        // Active-controller lock.
        private readonly object _ctlLock = new object();
        private string _controlToken;
        private string _controlLabel = "";
        private DateTime _controlActivity = DateTime.MinValue;
        private static readonly TimeSpan ControlIdleTimeout = TimeSpan.FromSeconds(20);

        // Continuous-jog watchdog.
        private readonly object _jogLock = new object();
        private bool _jogging;
        private DateTime _lastJogKeepAlive = DateTime.MinValue;
        private Timer _jogWatchdog;
        private static readonly TimeSpan JogWatchdogTimeout = TimeSpan.FromMilliseconds(600);

        // SSE connections.
        private readonly object _sseLock = new object();
        private readonly List<AutoResetEvent> _sseSignals = new List<AutoResetEvent>();

        public MaestroServer(IMaestroController controller, CompanionSettings settings,
            IWebAssetProvider assets, Action<string> log, string tokenStorePath)
        {
            _controller = controller;
            _settings = settings;
            _assets = assets;
            _log = log ?? (s => { });
            _tokenStorePath = tokenStorePath;
            LoadTokens();
        }

        public string BoundUrl { get { return _boundUrl; } }
        public bool IsRunning { get { return _running; } }

        // Build identifier surfaced via /api/info so a device can confirm which build it
        // is actually talking to (set by the plugin from the stamped BuildInfo; the test
        // host leaves the default).
        public string BuildId = "dev";

        public void Start()
        {
            if (_running) return;

            _controller.SnapshotChanged += OnSnapshotChanged;
            _jogWatchdog = new Timer(JogWatchdogTick, null, 250, 250);

            _listener = new HttpListener();
            int port = _settings.port;

            // Try LAN-wide binding first; if the URL ACL isn't reserved (AccessDenied),
            // fall back to localhost so the server still comes up for local testing.
            bool bound = false;
            if (_settings.openOnLan)
            {
                try
                {
                    _listener.Prefixes.Add("http://+:" + port + "/");
                    _listener.Start();
                    _boundUrl = "http://<this-pc>:" + port + "/";
                    bound = true;
                }
                catch (HttpListenerException ex)
                {
                    _log("LAN bind failed (" + ex.Message + "); falling back to localhost. " +
                         "To allow phones to connect, reserve the URL ACL: " +
                         "netsh http add urlacl url=http://+:" + port + "/ user=Everyone");
                    try { _listener.Close(); } catch { }
                    _listener = new HttpListener();
                }
            }

            if (!bound)
            {
                _listener.Prefixes.Add("http://localhost:" + port + "/");
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
                _boundUrl = "http://localhost:" + port + "/";
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MaestroServer" };
            _acceptThread.Start();
            _log("Companion server listening on " + _boundUrl);

            // LAN auto-discovery: advertise this machine + collect peers (only when on LAN).
            if (_settings.openOnLan)
            {
                try
                {
                    _beacon = new MaestroBeacon(_settings.port, _controller.MachineId, _controller.MachineName, "1.1.0", _log);
                    _beacon.Start();
                }
                catch { _beacon = null; }
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _controller.SnapshotChanged -= OnSnapshotChanged; } catch { }
            try { if (_beacon != null) _beacon.Dispose(); } catch { }
            try { if (_jogWatchdog != null) _jogWatchdog.Dispose(); } catch { }
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }
            WakeAllSse();
            _log("Companion server stopped.");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { HandleRequest(ctx); }
                    catch (Exception ex)
                    {
                        try { WriteError(ctx, "server_error", ex.Message, 500); } catch { }
                    }
                });
            }
        }

        // ----- Routing -------------------------------------------------------

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";

            AddCommonHeaders(ctx);
            if (req.HttpMethod == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

            if (!path.StartsWith("/api"))
            {
                ServeStatic(ctx, req.Url.AbsolutePath);
                return;
            }

            // Unauthenticated endpoints.
            if (path == "/api/health") { WriteJson(ctx, new { ok = true }, 200); return; }
            if (path == "/api/info") { HandleInfo(ctx); return; }
            if (path == "/api/peers") { HandlePeers(ctx); return; }
            if (path == "/api/pair") { HandlePair(ctx); return; }

            // Everything else requires a valid token.
            ClientToken client = Authenticate(req);
            if (client == null) { WriteError(ctx, "unauthorized", "Pair this machine first.", 401); return; }

            switch (path)
            {
                case "/api/status": HandleStatus(ctx, client); return;
                case "/api/events": HandleEvents(ctx, client); return;
                case "/api/projects": WriteJson(ctx, new { projects = _controller.GetProjects().projects }, 200); return;
                case "/api/tools": WriteJson(ctx, new { tools = _controller.GetTools().tools }, 200); return;
                case "/api/media": HandleMedia(ctx); return;

                case "/api/jog": HandleJog(ctx, client); return;
                case "/api/jog/stop": RunControl(ctx, client, false, () => { StopJog(); return _controller.JogStop(); }); return;
                case "/api/jog/keepalive": HandleJogKeepAlive(ctx, client); return;
                case "/api/spindle": HandleSpindle(ctx, client); return;

                case "/api/zero": HandleAxisCommand(ctx, client, axis => _controller.Zero(axis)); return;
                case "/api/home": HandleAxisCommand(ctx, client, axis => _controller.Home(axis)); return;
                case "/api/goto-zero": RunControl(ctx, client, false, () => _controller.GotoZero()); return;
                case "/api/park": HandlePark(ctx, client); return;
                case "/api/autozero": RunControl(ctx, client, false, () => _controller.AutoZero()); return;

                // Safety commands bypass the active-controller lock.
                case "/api/feedhold": RunControl(ctx, client, true, () => _controller.FeedHold()); return;
                case "/api/resume": RunControl(ctx, client, false, () => _controller.Resume()); return;
                case "/api/stop": RunControl(ctx, client, true, () => { StopJog(); return _controller.Stop(); }); return;
                case "/api/estop": RunControl(ctx, client, true, () => { StopJog(); return _controller.EStop(); }); return;

                case "/api/maestro/select": HandleSelect(ctx, client); return;
                case "/api/maestro/run-all": HandleRunAll(ctx, client); return;
                case "/api/maestro/run-step": HandleRunStep(ctx, client); return;
                case "/api/maestro/reset": RunControl(ctx, client, false, () => _controller.ResetProject()); return;
                case "/api/maestro/abort": RunControl(ctx, client, true, () => _controller.Abort()); return;
                case "/api/maestro/confirm": RunControl(ctx, client, false, () => _controller.ConfirmPrompt()); return;
                case "/api/maestro/cancel": RunControl(ctx, client, false, () => _controller.CancelPrompt()); return;

                default: WriteError(ctx, "not_found", "Unknown endpoint: " + path, 404); return;
            }
        }

        // ----- Endpoint handlers --------------------------------------------

        private void HandleInfo(HttpListenerContext ctx)
        {
            WriteJson(ctx, new
            {
                machineId = _controller.MachineId,
                machineName = _controller.MachineName,
                version = "1.1.0",
                build = BuildId,
                requiresPin = _settings.requirePin
            }, 200);
        }

        private void HandlePeers(HttpListenerContext ctx)
        {
            var peers = _beacon != null ? _beacon.Peers() : new List<DiscoveredPeer>();
            var list = new List<object>();
            foreach (var p in peers)
            {
                list.Add(new
                {
                    machineId = p.machineId,
                    machineName = p.machineName,
                    host = p.host,
                    port = p.port,
                    version = p.version,
                    url = "http://" + p.host + ":" + p.port + "/"
                });
            }
            WriteJson(ctx, new
            {
                self = new { machineId = _controller.MachineId, machineName = _controller.MachineName },
                discoveryEnabled = _beacon != null,
                peers = list
            }, 200);
        }

        private void HandlePair(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST") { WriteError(ctx, "bad_request", "POST required", 400); return; }
            var body = ReadJson(ctx);
            string pin = GetString(body, "pin");
            string clientLabel = GetString(body, "client");
            if (string.IsNullOrEmpty(clientLabel)) clientLabel = "Phone";

            if (_settings.requirePin && pin != _settings.pin)
            {
                WriteError(ctx, "bad_pin", "Incorrect PIN.", 401);
                return;
            }

            string token = NewToken();
            lock (_tokenLock)
            {
                _tokens[token] = new ClientToken { token = token, label = clientLabel, createdTs = NowMs() };
                SaveTokens();
            }
            _log("Paired client '" + clientLabel + "'.");
            WriteJson(ctx, new
            {
                token = token,
                machineId = _controller.MachineId,
                machineName = _controller.MachineName
            }, 200);
        }

        private void HandleStatus(HttpListenerContext ctx, ClientToken client)
        {
            WriteJson(ctx, BuildSnapshot(client), 200);
        }

        private void HandleEvents(HttpListenerContext ctx, ClientToken client)
        {
            var res = ctx.Response;
            res.StatusCode = 200;
            res.ContentType = "text/event-stream";
            res.Headers["Cache-Control"] = "no-cache";
            res.Headers["Connection"] = "keep-alive";
            res.SendChunked = true;

            var signal = new AutoResetEvent(false);
            lock (_sseLock) { _sseSignals.Add(signal); }

            try
            {
                var stream = res.OutputStream;
                WriteSse(stream, "status", _json.Serialize(BuildSnapshot(client)));

                while (_running)
                {
                    bool changed = signal.WaitOne(500);
                    if (!_running) break;
                    string payload = _json.Serialize(BuildSnapshot(client));
                    if (changed) WriteSse(stream, "status", payload);
                    else WriteSse(stream, "status", payload); // heartbeat also carries fresh status
                    stream.Flush();
                }
            }
            catch { /* client disconnected */ }
            finally
            {
                lock (_sseLock) { _sseSignals.Remove(signal); }
                // If the controlling client's stream dropped, stop any continuous jog.
                if (IsControlHolder(client)) StopJog();
                try { res.Close(); } catch { }
            }
        }

        private void HandleMedia(HttpListenerContext ctx)
        {
            string path = ctx.Request.QueryString["path"];
            if (string.IsNullOrEmpty(path)) { WriteError(ctx, "bad_request", "path required", 400); return; }

            // Constrain to the configured media root to prevent arbitrary file reads.
            string mediaRoot = Path.GetFullPath(Path.Combine(MaestroPaths.MaestroRoot, "Media"));
            string full;
            try
            {
                full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(mediaRoot, path));
            }
            catch { WriteError(ctx, "bad_request", "bad path", 400); return; }

            if (!full.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                WriteError(ctx, "not_found", "media not found", 404);
                return;
            }

            byte[] data = File.ReadAllBytes(full);
            WriteBytes(ctx, data, ContentTypes.ForPath(full), 200);
        }

        private void HandleJog(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            string axis = GetString(body, "axis");
            int dir = GetInt(body, "dir", 1) >= 0 ? 1 : -1;
            string mode = GetString(body, "mode");
            if (string.IsNullOrEmpty(mode)) mode = "step";
            double step = GetDouble(body, "step", 1.0);
            double feed = GetDouble(body, "feed", _settings != null ? 1500 : 1500);

            RunControl(ctx, client, false, () =>
            {
                if (mode == "cont")
                {
                    lock (_jogLock) { _jogging = true; _lastJogKeepAlive = DateTime.UtcNow; }
                }
                return _controller.Jog(axis, dir, mode, step, feed);
            });
        }

        private void HandleJogKeepAlive(HttpListenerContext ctx, ClientToken client)
        {
            if (!AcquireControl(client, false)) { WriteLocked(ctx); return; }
            lock (_jogLock) { _lastJogKeepAlive = DateTime.UtcNow; }
            WriteJson(ctx, new { ok = true }, 200);
        }

        private void HandleSpindle(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            bool on = GetBool(body, "on", false);
            double rpm = GetDouble(body, "rpm", 0);
            RunControl(ctx, client, false, () => _controller.Spindle(on, rpm));
        }

        private void HandleAxisCommand(HttpListenerContext ctx, ClientToken client, Func<string, CommandResult> action)
        {
            var body = ReadJson(ctx);
            string axis = GetString(body, "axis");
            if (string.IsNullOrEmpty(axis)) axis = "all";
            RunControl(ctx, client, false, () => action(axis));
        }

        private void HandlePark(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            string type = GetString(body, "type");
            if (string.IsNullOrEmpty(type)) type = "custom";
            RunControl(ctx, client, false, () => _controller.Park(type));
        }

        private void HandleSelect(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            string projectId = GetString(body, "projectId");
            RunControl(ctx, client, false, () => _controller.SelectProject(projectId));
        }

        private void HandleRunAll(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            int fromIndex = GetInt(body, "fromIndex", -1);
            RunControl(ctx, client, false, () => _controller.RunAll(fromIndex));
        }

        private void HandleRunStep(HttpListenerContext ctx, ClientToken client)
        {
            var body = ReadJson(ctx);
            int index = GetInt(body, "index", -1);
            RunControl(ctx, client, false, () => _controller.RunStep(index));
        }

        // ----- Control lock + execution -------------------------------------

        /// <summary>
        /// Executes a control command, enforcing the active-controller lock unless the
        /// command is a safety command (E-STOP / feed hold / stop), which any paired
        /// client may issue.
        /// </summary>
        private void RunControl(HttpListenerContext ctx, ClientToken client, bool bypassLock, Func<CommandResult> action)
        {
            if (!bypassLock && !AcquireControl(client, true))
            {
                WriteLocked(ctx);
                return;
            }
            if (bypassLock) TouchControlActivity();

            CommandResult result;
            try { result = action(); }
            catch (Exception ex) { result = CommandResult.Fail("server_error", ex.Message, 500); }

            if (result == null) result = CommandResult.Ok();
            if (result.ok)
            {
                OnSnapshotChanged();
                WriteJson(ctx, new { ok = true, message = result.message ?? "" }, 200);
            }
            else
            {
                WriteError(ctx, result.error ?? "error", result.message ?? "", result.httpStatus > 0 ? result.httpStatus : 400);
            }
        }

        private bool AcquireControl(ClientToken client, bool claim)
        {
            lock (_ctlLock)
            {
                bool stale = (DateTime.UtcNow - _controlActivity) > ControlIdleTimeout;
                if (_controlToken == null || _controlToken == client.token || stale)
                {
                    if (claim)
                    {
                        _controlToken = client.token;
                        _controlLabel = client.label;
                        _controlActivity = DateTime.UtcNow;
                    }
                    return true;
                }
                return false;
            }
        }

        private void TouchControlActivity()
        {
            lock (_ctlLock) { _controlActivity = DateTime.UtcNow; }
        }

        private bool IsControlHolder(ClientToken client)
        {
            lock (_ctlLock) { return _controlToken != null && _controlToken == client.token; }
        }

        private void WriteLocked(HttpListenerContext ctx)
        {
            string holder;
            lock (_ctlLock) { holder = _controlLabel; }
            WriteError(ctx, "locked", "Another controller (" + holder + ") holds control.", 423);
        }

        // ----- Jog watchdog --------------------------------------------------

        private void JogWatchdogTick(object state)
        {
            bool stop = false;
            lock (_jogLock)
            {
                if (_jogging && (DateTime.UtcNow - _lastJogKeepAlive) > JogWatchdogTimeout)
                {
                    _jogging = false;
                    stop = true;
                }
            }
            if (stop)
            {
                try { _controller.JogStop(); } catch { }
                _log("Jog watchdog: no keepalive, motion stopped.");
            }
        }

        private void StopJog()
        {
            lock (_jogLock) { _jogging = false; }
        }

        // ----- Snapshot ------------------------------------------------------

        private StatusSnapshot BuildSnapshot(ClientToken client)
        {
            var snap = _controller.GetSnapshot();
            snap.machineId = _controller.MachineId;
            snap.machineName = _controller.MachineName;
            snap.cameraUrl = _controller.CameraUrl ?? "";
            snap.ts = NowMs();

            // Server owns the controller lock, so fill it relative to the requesting client.
            string holder;
            bool youHold;
            lock (_ctlLock)
            {
                bool stale = (DateTime.UtcNow - _controlActivity) > ControlIdleTimeout;
                holder = (_controlToken == null || stale) ? "" : _controlLabel;
                youHold = _controlToken != null && _controlToken == client.token && !stale;
            }
            if (snap.controller == null) snap.controller = new ControllerStatus();
            snap.controller.heldBy = holder;
            snap.controller.youHoldControl = youHold;
            return snap;
        }

        private void OnSnapshotChanged()
        {
            WakeAllSse();
        }

        private void WakeAllSse()
        {
            lock (_sseLock)
            {
                foreach (var s in _sseSignals)
                {
                    try { s.Set(); } catch { }
                }
            }
        }

        private static void WriteSse(Stream stream, string eventName, string data)
        {
            var sb = new StringBuilder();
            sb.Append("event: ").Append(eventName).Append('\n');
            sb.Append("data: ").Append(data).Append("\n\n");
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
        }

        // ----- Static assets -------------------------------------------------

        private void ServeStatic(HttpListenerContext ctx, string absolutePath)
        {
            if (ctx.Request.HttpMethod != "GET") { WriteError(ctx, "bad_request", "GET required", 400); return; }

            string rel = (absolutePath ?? "/").TrimStart('/');
            if (string.IsNullOrEmpty(rel)) rel = "index.html";

            byte[] data; string contentType;
            if (_assets != null && _assets.TryGet(rel, out data, out contentType))
            {
                WriteBytes(ctx, data, contentType, 200);
                return;
            }

            // SPA fallback: unknown non-file paths serve index.html.
            if (!rel.Contains(".") && _assets != null && _assets.TryGet("index.html", out data, out contentType))
            {
                WriteBytes(ctx, data, contentType, 200);
                return;
            }

            WriteError(ctx, "not_found", "Not found: " + rel, 404);
        }

        // ----- Auth / tokens -------------------------------------------------

        private ClientToken Authenticate(HttpListenerRequest req)
        {
            string auth = req.Headers["Authorization"];
            string token = null;
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth.Substring(7).Trim();
            if (string.IsNullOrEmpty(token)) token = req.QueryString["token"]; // SSE convenience
            if (string.IsNullOrEmpty(token)) return null;

            lock (_tokenLock)
            {
                ClientToken ct;
                return _tokens.TryGetValue(token, out ct) ? ct : null;
            }
        }

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private void LoadTokens()
        {
            if (string.IsNullOrEmpty(_tokenStorePath) || !File.Exists(_tokenStorePath)) return;
            try
            {
                string json = File.ReadAllText(_tokenStorePath, Encoding.UTF8);
                var doc = _json.Deserialize<TokenStoreDoc>(json);
                if (doc != null && doc.tokens != null)
                {
                    lock (_tokenLock)
                    {
                        foreach (var t in doc.tokens)
                            if (t != null && !string.IsNullOrEmpty(t.token)) _tokens[t.token] = t;
                    }
                }
            }
            catch { }
        }

        private void SaveTokens()
        {
            if (string.IsNullOrEmpty(_tokenStorePath)) return;
            try
            {
                var doc = new TokenStoreDoc();
                foreach (var kv in _tokens) doc.tokens.Add(kv.Value);
                string dir = Path.GetDirectoryName(_tokenStorePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_tokenStorePath, _json.Serialize(doc), new UTF8Encoding(false));
            }
            catch { }
        }

        // ----- HTTP write helpers -------------------------------------------

        private void AddCommonHeaders(HttpListenerContext ctx)
        {
            var h = ctx.Response.Headers;
            // Permissive CORS so a dev client (e.g. Vite on another port) can connect on the LAN.
            h["Access-Control-Allow-Origin"] = "*";
            h["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
            h["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        }

        private void WriteJson(HttpListenerContext ctx, object obj, int status)
        {
            WriteBytes(ctx, Encoding.UTF8.GetBytes(_json.Serialize(obj)), "application/json; charset=utf-8", status);
        }

        private void WriteError(HttpListenerContext ctx, string error, string message, int status)
        {
            WriteJson(ctx, new { error = error, message = message }, status);
        }

        private void WriteBytes(HttpListenerContext ctx, byte[] data, string contentType, int status)
        {
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        private Dictionary<string, object> ReadJson(HttpListenerContext ctx)
        {
            try
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(body)) return new Dictionary<string, object>();
                    return _json.Deserialize<Dictionary<string, object>>(body) ?? new Dictionary<string, object>();
                }
            }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                int i;
                if (int.TryParse(v.ToString(), out i)) return i;
            }
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                bool b;
                if (bool.TryParse(v.ToString(), out b)) return b;
                if (v.ToString() == "1") return true;
                if (v.ToString() == "0") return false;
            }
            return fallback;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                double r;
                if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
            }
            return fallback;
        }

        private static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public class ClientToken
        {
            public string token { get; set; }
            public string label { get; set; }
            public long createdTs { get; set; }
        }

        private class TokenStoreDoc
        {
            public List<ClientToken> tokens { get; set; }
            public TokenStoreDoc() { tokens = new List<ClientToken>(); }
        }
    }
}
