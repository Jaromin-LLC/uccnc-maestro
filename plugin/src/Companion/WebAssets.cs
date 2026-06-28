using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Plugins.Companion
{
    /// <summary>
    /// Supplies the static PWA files (index.html, js, css, icons...) to the server.
    /// </summary>
    public interface IWebAssetProvider
    {
        bool TryGet(string relativePath, out byte[] data, out string contentType);
    }

    public static class ContentTypes
    {
        public static string ForPath(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".mjs": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".webmanifest": return "application/manifest+json; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".mp4": return "video/mp4";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }

    /// <summary>
    /// Serves the PWA from a folder on disk - used by the test host so files can be
    /// edited and reloaded without recompiling.
    /// </summary>
    public class FileSystemWebAssets : IWebAssetProvider
    {
        private readonly string _root;

        public FileSystemWebAssets(string root)
        {
            _root = Path.GetFullPath(root);
        }

        public bool TryGet(string relativePath, out byte[] data, out string contentType)
        {
            data = null;
            contentType = null;

            string rel = (relativePath ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(rel)) rel = "index.html";

            string full = Path.GetFullPath(Path.Combine(_root, rel));
            // Prevent path traversal outside the web root.
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(full)) return false;

            try
            {
                data = File.ReadAllBytes(full);
                contentType = ContentTypes.ForPath(full);
                return true;
            }
            catch
            {
                data = null;
                contentType = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Serves the PWA from manifest resources embedded in the DLL. Resource names are
    /// flattened (e.g. "UccncMaestro.app.index.html"); we map a request path to a name by
    /// replacing '/' with '.' and prefixing.
    /// </summary>
    public class EmbeddedWebAssets : IWebAssetProvider
    {
        private readonly Assembly _asm;
        private readonly string _prefix;
        private readonly HashSet<string> _names;

        public EmbeddedWebAssets(Assembly asm, string prefix)
        {
            _asm = asm;
            _prefix = prefix;
            _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _asm.GetManifestResourceNames()) _names.Add(n);
        }

        public bool TryGet(string relativePath, out byte[] data, out string contentType)
        {
            data = null;
            contentType = null;

            string rel = (relativePath ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(rel)) rel = "index.html";

            // "icons/icon-192.png" -> "<prefix>.icons.icon-192.png"
            string resource = _prefix + "." + rel.Replace('/', '.');
            if (!_names.Contains(resource)) return false;

            try
            {
                using (var stream = _asm.GetManifestResourceStream(resource))
                {
                    if (stream == null) return false;
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        data = ms.ToArray();
                    }
                }
                contentType = ContentTypes.ForPath(rel);
                return true;
            }
            catch
            {
                data = null;
                contentType = null;
                return false;
            }
        }
    }
}
