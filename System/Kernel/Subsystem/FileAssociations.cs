using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Executable.Lua;
using Windose.Programs;
using Windose.System.Elf;
using Windose.System.Kernel;
using Windose.System.Kernel.Attributes;
using Windose.System.Lua;
using Windose.System.System_Calls;

namespace Windose.System.Kernel.Subsystem
{
    public static class FileAssociations
    {
        private static readonly Dictionary<string, Type> associations = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> elfAssociations = new(StringComparer.OrdinalIgnoreCase);

        static FileAssociations()
        {
            RegisterDefaults();
        }

        private static void RegisterDefaults()
        {
            Type ie = typeof(InternetExplorer);
            Register(".html", ie);
            Register(".htm", ie);

            Type mp = typeof(MediaPlayer);
            Register(".avi", mp);
            Register(".mp4", mp);
            Register(".webm", mp);
            Register(".mkv", mp);
            Register(".mov", mp);
            Register(".wmv", mp);
            Register(".flv", mp);
            Register(".gif", mp);
            Register(".wav", mp);

            Type breeze = typeof(BreezeEditor);
            Register(".breeze", breeze);
            Register(".txt", breeze);
            Register(".log", breeze);
            Register(".ini", breeze);
            Register(".cfg", breeze);
            Register(".json", breeze);
            Register(".xml", breeze);
            Register(".cs", breeze);
            Register(".c", breeze);
            Register(".h", breeze);
            Register(".lua", breeze);
            Register(".md", breeze);

            Type iv = typeof(ImageViewer);
            Register(".png", iv);
            Register(".webp", iv);
            Register(".jpg", iv);
            Register(".jpeg", iv);
            Register(".bmp", iv);
            Register(".qoi", iv);
            Register(".tga", iv);
            Register(".ico", iv);
            Register(".ppm", iv);
        }

        public static void Register(string extension, Type applicationType)
        {
            if (string.IsNullOrWhiteSpace(extension) || applicationType == null) return;
            extension = Normalize(extension);
            associations[extension] = applicationType;
        }

        public static void RegisterElf(string extension, string elfPath)
        {
            if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(elfPath)) return;
            extension = Normalize(extension);
            elfAssociations[extension] = elfPath;
        }

        public static bool TryGetApplication(string extension, out Type applicationType)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                applicationType = null;
                return false;
            }

            return associations.TryGetValue(Normalize(extension), out applicationType);
        }

        public static bool TryGetElfApplication(string extension, out string elfPath)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                elfPath = null;
                return false;
            }

            return elfAssociations.TryGetValue(Normalize(extension), out elfPath);
        }

        public static string Normalize(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "";
            extension = extension.Trim().TrimStart('*');
            if (string.IsNullOrEmpty(extension)) return "";
            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            return extension.ToLowerInvariant();
        }

        [RequiresUnreferencedCode("File associations are discovered through reflection.")]
        public static void DiscoverPrograms(Assembly assembly)
        {
            if (assembly == null) return;
            try
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type == null || !type.IsClass || type.IsAbstract)
                    {
                        continue;
                    }

                    if (!typeof(Window).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    IEnumerable<FileAssociationAttribute> attrs = type.GetCustomAttributes<FileAssociationAttribute>();
                    foreach (var attr in attrs)
                    {
                        if (attr != null && !string.IsNullOrEmpty(attr.Extension))
                        {
                            Register(attr.Extension, type);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("FileAssociations", $"Reflection discovery error: {ex.Message}", ConsoleMessageType.Warning);
            }
        }

        public static bool Launch(string path, int maxHops = 4)
        {
            if (maxHops <= 0 || string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            string ext = Path.GetExtension(path);

            // 0. Shortcut resolution (.lnk)
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (ShortcutFile.TryRead(path, out ShortcutInfo info) && !string.IsNullOrEmpty(info.Target))
                {
                    if (string.Equals(info.Target, "program::BreezeEditor", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(info.Target, "BreezeEditor", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists("/mnt/Programs/aeroeditor.elf"))
                        {
                            LaunchTracker.StartElf("/mnt/Programs/aeroeditor.elf");
                            return true;
                        }
                        LaunchTracker.Start(() => new BreezeEditor(100, 80, 900, 620, ""));
                        return true;
                    }
                    if (string.Equals(info.Target, "program::MediaPlayer", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(info.Target, "MediaPlayer", StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchTracker.Start(() => new MediaPlayer("", 120, 100, 720, 520));
                        return true;
                    }
                    if (string.Equals(info.Target, "program::CommandPrompt", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(info.Target, "CommandPrompt", StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchTracker.Start(() => new CommandPrompt(100, 100, 720, 480));
                        return true;
                    }

                    string resolvedTarget = info.Target;
                    if (!File.Exists(resolvedTarget) && File.Exists("/mnt/Programs/" + Path.GetFileName(resolvedTarget)))
                    {
                        resolvedTarget = "/mnt/Programs/" + Path.GetFileName(resolvedTarget);
                    }

                    if (File.Exists(resolvedTarget))
                    {
                        string targetExt = Path.GetExtension(resolvedTarget);
                        if (string.Equals(targetExt, ".elf", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string progName = Path.GetFileName(resolvedTarget);
                                List<string> argsList = new List<string> { progName };
                                if (!string.IsNullOrWhiteSpace(info.Arguments))
                                {
                                    argsList.AddRange(info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                                }
                                LaunchTracker.StartElf(resolvedTarget, argsList.ToArray(), p =>
                                {
                                    if (p != null && !string.IsNullOrEmpty(info.WorkingDir)) p.Env["PWD"] = info.WorkingDir;
                                });
                                return true;
                            }
                            catch (Exception ex)
                            {
                                SystemLogger.WriteLine("Shortcut", $"Error launching {resolvedTarget}: {ex.Message}", ConsoleMessageType.Error);
                                return false;
                            }
                        }
                        return Launch(resolvedTarget, maxHops - 1);
                    }
                    else if (Directory.Exists(info.Target))
                    {
                        LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, info.Name, info.Target));
                        return true;
                    }
                    else if (info.Target.EndsWith("terminal.elf", StringComparison.OrdinalIgnoreCase) ||
                             info.Target.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
                             info.Target.Contains("Command", StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchTracker.Start(() => new CommandPrompt(100, 100, 720, 480));
                        return true;
                    }
                    else
                    {
                        SystemLogger.WriteLine("Shortcut", $"Shortcut target does not exist: {info.Target}", ConsoleMessageType.Warning);
                        return false;
                    }
                }
                return false;
            }

            // 1. Direct ELF execution
            if (string.Equals(ext, ".elf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    LaunchTracker.StartElf(path, new string[] { path });
                    return true;
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("ELF", $"Error launching {path}: {ex.Message}", ConsoleMessageType.Error);
                    return false;
                }
            }

            // 2. Direct Lua execution
            if (string.Equals(ext, ".lua", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var lua = LuaAPI.NewState();
                    lua.L_OpenLibs();
                    new LuaWindoseLib(path, new[] { "fs.read", "fs.write", "ui.window", "reg.read", "reg.write" }).Install(lua);
                    lua.L_DoFile(path);
                    return true;
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("Lua", $"Failed to run Lua script {path}: {ex.Message}", ConsoleMessageType.Error);
                }
            }

            if (TryGetElfApplication(ext, out string associatedElf) && File.Exists(associatedElf))
            {
                try
                {
                    LaunchTracker.StartElf(associatedElf, new string[] { associatedElf, path });
                    return true;
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("ELF", $"Error launching {associatedElf} for {path}: {ex.Message}", ConsoleMessageType.Error);
                }
            }

            if (!TryGetApplication(ext, out Type program) || program == null)
            {
                return false;
            }

            if (program == typeof(MediaPlayer))
            {
                LaunchTracker.Start(() => new MediaPlayer(path, 120, 100, 720, 520));
                return true;
            }

            if (program == typeof(BreezeEditor))
            {
                if (File.Exists("/mnt/Programs/aeroeditor.elf"))
                {
                    Windose.System.Elf.ElfRunner.RunFile("/mnt/Programs/aeroeditor.elf", new string[] { path });
                    return true;
                }
                LaunchTracker.Start(() => new BreezeEditor(100, 80, 900, 620, path));
                return true;
            }

            if (program == typeof(ImageViewer))
            {
                LaunchTracker.Start(() => new ImageViewer(path, 120, 120, 640, 480));
                return true;
            }

            if (program == typeof(InternetExplorer))
            {
                LaunchTracker.Start(() => new InternetExplorer(path, 80, 60));
                return true;
            }

            try
            {
                var ctorWithPath = program.GetConstructor(new[] { typeof(string) });
                if (ctorWithPath != null)
                {
                    LaunchTracker.Start(() => (Window)ctorWithPath.Invoke(new object[] { path }));
                    return true;
                }

                LaunchTracker.Start(() => (Window)Activator.CreateInstance(program)!);
                return true;
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("FileAssociations", $"Failed to launch {program.Name}: {ex.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        public static string GetIconPath(string pathOrExtension, int maxHops = 4)
        {
            if (string.IsNullOrEmpty(pathOrExtension) || maxHops <= 0) return "/mnt/System/Icons/file_lines.png";
            string ext = Normalize(pathOrExtension.Contains('.') ? Path.GetExtension(pathOrExtension) : pathOrExtension);

            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(pathOrExtension) && ShortcutFile.TryRead(pathOrExtension, out ShortcutInfo info))
                {
                    if (!string.IsNullOrEmpty(info.Icon) && File.Exists(info.Icon))
                        return info.Icon;
                    if (!string.IsNullOrEmpty(info.Target))
                    {
                        if (info.Target.Contains("Breeze", StringComparison.OrdinalIgnoreCase))
                            return "imageres.dll,-102";
                        if (info.Target.Contains("Media", StringComparison.OrdinalIgnoreCase))
                            return "wmploc.dll,-102";
                        if (info.Target.Contains("terminal", StringComparison.OrdinalIgnoreCase) || info.Target.Contains("Command", StringComparison.OrdinalIgnoreCase))
                            return "imageres.dll,-75";
                        return GetIconPath(info.Target, maxHops - 1);
                    }
                }
                return "ddores.dll,-2001";
            }

            string regIcon = Registry.GetString($"{StaticRegistry.FileAssociation}/{ext}", "");
            if (!string.IsNullOrEmpty(regIcon))
                return regIcon;

            switch (ext)
            {
                case ".folder":
                case ".directory":
                    return "/mnt/System/Icons/directory_closed.png";

                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                case ".webp":
                case ".tga":
                case ".ico":
                case ".ppm":
                case ".qoi":
                    return "imageres.dll,-99";

                case ".avi":
                case ".mp4":
                case ".webm":
                case ".mkv":
                case ".mov":
                case ".wmv":
                case ".flv":
                case ".wav":
                    return "wmploc.dll,-102";

                case ".txt":
                case ".log":
                case ".ini":
                case ".cfg":
                case ".json":
                case ".xml":
                case ".cs":
                case ".c":
                case ".h":
                    return "imageres.dll,-102";

                case ".breeze":
                    return "imageres.dll,-102";

                case ".html":
                case ".htm":
                    return "shell32.dll,-220";

                case ".elf":
                    return "imageres.dll,-75";

                case ".lua":
                    return "imageres.dll,-75";

                default:
                    return "imageres.dll,-2";
            }
        }

        public static Image GetIcon(string pathOrExtension)
        {
            string iconPath = GetIconPath(pathOrExtension);
            try
            {
                return Windose.System.Resources.IconResource.Load(iconPath, 32);
            }
            catch { }
            return Windose.System.Resources.IconResource.GetFallbackIcon(32);
        }

        public static string GetFileTypeDescription(string pathOrExtension)
        {
            if (string.IsNullOrEmpty(pathOrExtension)) return "File";
            string ext = pathOrExtension.Contains('.') ? Path.GetExtension(pathOrExtension) : pathOrExtension;
            ext = Normalize(ext);

            switch (ext)
            {
                case ".folder":
                case ".directory":
                    return "File Folder";

                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                case ".webp":
                case ".tga":
                case ".ico":
                case ".ppm":
                case ".qoi":
                    return "Image File";

                case ".avi":
                case ".mp4":
                case ".webm":
                case ".mkv":
                case ".mov":
                case ".wmv":
                case ".flv":
                    return "Video Clip";

                case ".wav":
                    return "WAV Audio File";

                case ".mp3":
                    return "MP3 Audio (Cosmos plays WAV only)";

                case ".ogg":
                    return "OGG Audio (Cosmos plays WAV only)";

                case ".wma":
                    return "WMA Audio (Cosmos plays WAV only)";

                case ".txt":
                case ".log":
                    return "Text Document";

                case ".breeze":
                    return "Breeze Script";

                case ".html":
                case ".htm":
                    return "HTML Document";

                case ".elf":
                    return "ELF Executable";

                case ".lnk":
                    return "Shortcut";

                case ".lua":
                    return "Lua Script";

                case ".ttf":
                case ".psf":
                case ".psfu":
                    return "Font File";

                case ".db":
                    return "Database File";

                case ".sys":
                    return "System File";

                case ".conf":
                case ".ini":
                case ".cfg":
                    return "Configuration File";

                case ".cs":
                    return "C# Source File";

                case ".c":
                case ".h":
                    return "C Source File";

                default:
                    if (!string.IsNullOrEmpty(ext))
                    {
                        return ext.TrimStart('.').ToUpperInvariant() + " File";
                    }
                    return "File";
            }
        }
    }
}
