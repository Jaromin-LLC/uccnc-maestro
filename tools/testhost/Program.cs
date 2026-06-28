using System;
using System.IO;
using Plugins;
using Plugins.Companion;

namespace Plugins.TestHost
{
    /// <summary>
    /// Standalone console host that runs the companion server against a simulated machine,
    /// so the whole API + PWA can be exercised on localhost without UCCNC. Serves the PWA
    /// straight from the app/ folder (edit + refresh, no recompile).
    ///
    /// Usage: UccncMaestro.TestHost.exe [--root <repoRoot>] [--port <n>] [--web <dir>]
    /// </summary>
    internal static class Program
    {
        private static void Main(string[] args)
        {
            string root = Directory.GetCurrentDirectory();
            int port = 8723;
            string webDir = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--root") root = args[i + 1];
                else if (args[i] == "--port") int.TryParse(args[i + 1], out port);
                else if (args[i] == "--web") webDir = args[i + 1];
            }

            if (string.IsNullOrEmpty(webDir)) webDir = Path.Combine(root, "app");
            string projectsFile = Path.Combine(root, "plugin", "config", "projects.json");
            string toolsFile = Path.Combine(root, "plugin", "config", "tools.json");

            var projects = JsonStore.LoadProjects(projectsFile);
            var tools = JsonStore.LoadTools(toolsFile);

            string settingsFile = Path.Combine(Path.GetTempPath(), "maestro-testhost-companion.json");
            string tokenFile = Path.Combine(Path.GetTempPath(), "maestro-testhost-tokens.json");
            var settings = CompanionSettingsStore.Load(settingsFile, "Sim CNC");
            settings.port = port;
            settings.enabled = true;
            settings.EnsureDefaults("Sim CNC");
            CompanionSettingsStore.Save(settingsFile, settings);

            if (!Directory.Exists(webDir))
            {
                Console.WriteLine("[WARN] Web dir not found: " + webDir + " (PWA will 404). Pass --web <appDir>.");
            }

            var controller = new SimulatedMaestroController(settings, projects, tools);
            var assets = new FileSystemWebAssets(webDir);
            var server = new MaestroServer(controller, settings, assets, m => Console.WriteLine("[server] " + m), tokenFile);
            server.Start();

            Console.WriteLine();
            Console.WriteLine("=== CNC Maestro companion test host ===");
            Console.WriteLine("Open in a browser:  http://localhost:" + port + "/");
            Console.WriteLine("Pairing PIN:        " + (settings.requirePin ? settings.pin : "(none)"));
            Console.WriteLine("Machine name:       " + settings.machineName);
            Console.WriteLine("Serving PWA from:   " + webDir);
            Console.WriteLine("Projects loaded:    " + projects.projects.Count);
            Console.WriteLine();
            Console.WriteLine("To reach it from a phone on the same WiFi, run elevated (or reserve the URL ACL),");
            Console.WriteLine("then use this PC's LAN IP instead of localhost.");
            Console.WriteLine();
            Console.WriteLine("Press Enter to stop (or Ctrl+C when run non-interactively).");

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; server.Stop(); Environment.Exit(0); };

            string line = Console.ReadLine();
            if (line == null)
            {
                // No console input (e.g. launched in the background) - keep serving until killed.
                new System.Threading.ManualResetEvent(false).WaitOne();
            }

            server.Stop();
        }
    }
}
