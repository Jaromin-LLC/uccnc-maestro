using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Plugins.Companion
{
    /// <summary>
    /// Settings for the companion (mobile) server. Persisted separately from
    /// projects.json so the workflow schema is untouched and the standalone test host
    /// can use its own file. A stable machineId is generated on first load.
    /// </summary>
    public class CompanionSettings
    {
        public bool enabled { get; set; }
        public int port { get; set; }

        // Bind to all LAN interfaces (http://+:port) when true; otherwise localhost only.
        public bool openOnLan { get; set; }

        // When false, pairing succeeds without a PIN (trusted-LAN convenience). Off by default.
        public bool requirePin { get; set; }

        public string pin { get; set; }
        public string machineName { get; set; }
        public string machineId { get; set; }

        // Optional shop-camera stream URL (MJPEG/HLS) surfaced in the app. Empty = no camera.
        public string cameraUrl { get; set; }

        public CompanionSettings()
        {
            enabled = true;
            port = 8723;
            openOnLan = true;
            requirePin = true;
            pin = "";
            machineName = "";
            machineId = "";
            cameraUrl = "";
        }

        public void EnsureDefaults(string defaultName)
        {
            if (port <= 0 || port > 65535) port = 8723;
            if (string.IsNullOrEmpty(machineId))
                machineId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(machineName))
                machineName = string.IsNullOrEmpty(defaultName) ? ("CNC " + machineId.Substring(0, 4)) : defaultName;
            if (requirePin && string.IsNullOrEmpty(pin))
                pin = GeneratePin();
        }

        public static string GeneratePin()
        {
            var rnd = new Random(unchecked(Environment.TickCount * 31 + Guid.NewGuid().GetHashCode()));
            return rnd.Next(0, 10000).ToString("D4");
        }
    }

    public static class CompanionPaths
    {
        public static string SettingsFile = Path.Combine(MaestroPaths.MaestroRoot, "companion.json");
        public static string TokenStoreFile = Path.Combine(MaestroPaths.MaestroRoot, "companion-tokens.json");
    }

    public static class CompanionSettingsStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static CompanionSettings Load(string path, string defaultName)
        {
            CompanionSettings settings = null;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    settings = Serializer.Deserialize<CompanionSettings>(json);
                }
            }
            catch { settings = null; }

            if (settings == null) settings = new CompanionSettings();
            settings.EnsureDefaults(defaultName);
            return settings;
        }

        public static void Save(string path, CompanionSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string temp = path + ".tmp";
                string json = Serializer.Serialize(settings);
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch { }
        }
    }
}
