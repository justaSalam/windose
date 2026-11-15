using System;
using System.Collections.Generic;
using System.IO;

namespace Windose.System.Kernel.Subsystem
{
    /// <summary>
    /// In-memory cache of known installed programs in /mnt/Programs and /mnt/Apps.
    /// Replaces slow synchronous File.Exists disk cluster checks on the UI thread with instantaneous O(1) memory lookups.
    /// </summary>
    public static class ProgramCache
    {
        private static readonly HashSet<string> knownPrograms = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object cacheLock = new();
        private static bool initialized = false;

        public static void Initialize()
        {
            Refresh();
        }

        public static void Refresh()
        {
            lock (cacheLock)
            {
                knownPrograms.Clear();
                ScanDirectory("/mnt/Programs");
                ScanDirectory("/mnt/Apps");
                ScanDirectory("/mnt/System");
                initialized = true;
            }
        }

        private static void ScanDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir);
                    foreach (string file in files)
                    {
                        string norm = file.Replace('\\', '/');
                        knownPrograms.Add(norm);
                        string fn = Path.GetFileName(norm);
                        if (!string.IsNullOrEmpty(fn))
                        {
                            knownPrograms.Add(fn);
                        }
                    }
                }
            }
            catch { }
        }

        public static bool HasProgram(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return false;

            lock (cacheLock)
            {
                if (!initialized)
                {
                    Refresh();
                }

                string clean = pathOrName.Replace('\\', '/');
                if (knownPrograms.Contains(clean)) return true;

                string fn = Path.GetFileName(clean);
                if (knownPrograms.Contains(fn)) return true;
            }

            // Fallback to disk check if file was created after scan
            try
            {
                if (File.Exists(pathOrName))
                {
                    Register(pathOrName);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static void Register(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (cacheLock)
            {
                string norm = path.Replace('\\', '/');
                knownPrograms.Add(norm);
                string fn = Path.GetFileName(norm);
                if (!string.IsNullOrEmpty(fn))
                {
                    knownPrograms.Add(fn);
                }
            }
        }
    }
}
