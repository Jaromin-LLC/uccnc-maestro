using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Plugins.Companion
{
    /// <summary>
    /// A discovered Maestro machine on the LAN (heard via the UDP beacon).
    /// </summary>
    public class DiscoveredPeer
    {
        public string machineId { get; set; }
        public string machineName { get; set; }
        public string host { get; set; }   // sender IP - what a phone connects to
        public int port { get; set; }      // companion HTTP port
        public string version { get; set; }
        public DateTime lastSeenUtc { get; set; }
    }

    /// <summary>
    /// Lightweight LAN auto-discovery. Each plugin periodically broadcasts a small UDP
    /// beacon describing itself and listens for other machines' beacons, building a peer
    /// list. The browser PWA can't do UDP, so it asks its connected server for the list
    /// via <c>/api/peers</c>. Beacon and HTTP share the same port number (UDP vs TCP).
    /// </summary>
    public class MaestroBeacon : IDisposable
    {
        // Bumped if the wire format changes; receivers ignore mismatched magic.
        private const string Magic = "UCCNCMAESTRO1";

        private readonly int _port;
        private readonly string _machineId;
        private readonly string _machineName;
        private readonly string _version;
        private readonly Action<string> _log;

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _lock = new object();
        private readonly Dictionary<string, DiscoveredPeer> _peers = new Dictionary<string, DiscoveredPeer>();
        private static readonly TimeSpan PeerTtl = TimeSpan.FromSeconds(20);

        private UdpClient _udp;
        private Thread _rxThread;
        private Timer _txTimer;
        private volatile bool _running;

        public MaestroBeacon(int port, string machineId, string machineName, string version, Action<string> log)
        {
            _port = port;
            _machineId = machineId ?? "";
            _machineName = machineName ?? "";
            _version = version ?? "";
            _log = log ?? (s => { });
        }

        public void Start()
        {
            if (_running) return;
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
                _udp.EnableBroadcast = true;
            }
            catch (Exception ex)
            {
                _log("Discovery beacon disabled (bind failed: " + ex.Message + ").");
                try { if (_udp != null) _udp.Close(); } catch { }
                _udp = null;
                return;
            }

            _running = true;
            _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MaestroBeaconRx" };
            _rxThread.Start();
            _txTimer = new Timer(_ => SendBeacon(), null, 0, 5000);
            _log("Discovery beacon active on UDP " + _port + ".");
        }

        private void SendBeacon()
        {
            if (!_running || _udp == null) return;
            try
            {
                var info = new Dictionary<string, object>
                {
                    { "id", _machineId },
                    { "name", _machineName },
                    { "port", _port },
                    { "version", _version }
                };
                string payload = Magic + _json.Serialize(info);
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _port));
            }
            catch { /* transient network errors are fine; next tick retries */ }
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try { data = _udp.Receive(ref remote); }
                catch { if (!_running) break; continue; }

                try
                {
                    string text = Encoding.UTF8.GetString(data);
                    if (!text.StartsWith(Magic)) continue;
                    var info = _json.Deserialize<Dictionary<string, object>>(text.Substring(Magic.Length));
                    if (info == null) continue;

                    string id = Str(info, "id");
                    if (string.IsNullOrEmpty(id) || id == _machineId) continue; // skip self

                    var peer = new DiscoveredPeer
                    {
                        machineId = id,
                        machineName = Str(info, "name"),
                        host = remote.Address.ToString(),
                        port = Int(info, "port", _port),
                        version = Str(info, "version"),
                        lastSeenUtc = DateTime.UtcNow
                    };
                    lock (_lock) { _peers[id] = peer; }
                }
                catch { /* ignore malformed beacons */ }
            }
        }

        /// <summary>Currently known peers (self excluded), pruned by TTL.</summary>
        public List<DiscoveredPeer> Peers()
        {
            var now = DateTime.UtcNow;
            var list = new List<DiscoveredPeer>();
            lock (_lock)
            {
                var stale = new List<string>();
                foreach (var kv in _peers)
                {
                    if (now - kv.Value.lastSeenUtc > PeerTtl) stale.Add(kv.Key);
                    else list.Add(kv.Value);
                }
                foreach (var k in stale) _peers.Remove(k);
            }
            return list;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null) ? v.ToString() : "";
        }

        private static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            object v; int n;
            if (d != null && d.TryGetValue(key, out v) && v != null && int.TryParse(v.ToString(), out n)) return n;
            return fallback;
        }

        public void Dispose()
        {
            _running = false;
            try { if (_txTimer != null) _txTimer.Dispose(); } catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
            _udp = null;
        }
    }
}
