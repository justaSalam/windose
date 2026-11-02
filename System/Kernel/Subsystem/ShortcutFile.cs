using System;
using System.Collections.Generic;
using System.IO;

namespace Windose.System.Kernel.Subsystem
{
    public struct ShortcutInfo
    {
        public string Target;
        public string Arguments;
        public string WorkingDir;
        public string Name;
        public string? Icon;
    }

    public static class ShortcutFile
    {
        public static bool TryRead(string path, out ShortcutInfo info)
        {
            info = default;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            try
            {
                string target = "";
                string args = "";
                string workingDir = "/";
                string name = Path.GetFileNameWithoutExtension(path);
                string? icon = null;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("Target", StringComparison.OrdinalIgnoreCase)) target = val;
                    else if (key.Equals("Arguments", StringComparison.OrdinalIgnoreCase) || key.Equals("Args", StringComparison.OrdinalIgnoreCase)) args = val;
                    else if (key.Equals("WorkingDir", StringComparison.OrdinalIgnoreCase) || key.Equals("WorkingDirectory", StringComparison.OrdinalIgnoreCase)) workingDir = val;
                    else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase) || key.Equals("Title", StringComparison.OrdinalIgnoreCase)) name = val;
                    else if (key.Equals("Icon", StringComparison.OrdinalIgnoreCase)) icon = val;
                }

                if (string.IsNullOrEmpty(target)) return false;

                info = new ShortcutInfo
                {
                    Target = target,
                    Arguments = args,
                    WorkingDir = string.IsNullOrEmpty(workingDir) ? "/" : workingDir,
                    Name = name,
                    Icon = icon
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool Create(string path, string target, string name, string? icon = null, string? args = null, string? workingDir = null)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(target)) return false;

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                List<string> lines = new List<string>
                {
                    "[Shortcut]",
                    $"Target={target}",
                    $"Name={name}",
                    $"Arguments={args ?? ""}",
                    $"WorkingDir={workingDir ?? "/"}"
                };
                if (!string.IsNullOrEmpty(icon))
                {
                    lines.Add($"Icon={icon}");
                }

                File.WriteAllLines(path, lines);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
