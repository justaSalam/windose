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

        public static bool TryGetApplication(string extension, out Type applicationType)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                applicationType = null;
                return false;
            }

            return associations.TryGetValue(Normalize(extension), out applicationType);
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

        public static bool Launch(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            string ext = Path.GetExtension(path);

            // 0. Shortcut resolution (.lnk)
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (ShortcutFile.TryRead(path, out ShortcutInfo info) && !string.IsNullOrEmpty(info.Target))
                {
                    if (string.Equals(info.Target, "program::BreezeEditor", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(info.Target, "BreezeEditor", StringComparison.OrdinalIgnoreCase))
                    {
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
                                byte[] data = File.ReadAllBytes(resolvedTarget);
                                string progName = Path.GetFileName(resolvedTarget);
                                List<string> argsList = new List<string> { progName };
                                if (!string.IsNullOrWhiteSpace(info.Arguments))
                                {
                                    argsList.AddRange(info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                                }
                                ElfProgram p = ElfRunner.Spawn(data, progName, argsList.ToArray(), out string error);
                                if (p != null)
                                {
                                    if (!string.IsNullOrEmpty(info.WorkingDir)) p.Env["PWD"] = info.WorkingDir;
                                    return true;
                                }
                                SystemLogger.WriteLine("Shortcut", $"Failed to launch ELF target {resolvedTarget}: {error}", ConsoleMessageType.Error);
                                return false;
                            }
                            catch (Exception ex)
                            {
                                SystemLogger.WriteLine("Shortcut", $"Error launching {resolvedTarget}: {ex.Message}", ConsoleMessageType.Error);
                                return false;
                            }
                        }
                        return Launch(resolvedTarget);
                    }
                    else if (Directory.Exists(info.Target))
                    {
                        LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, info.Name, info.Target));
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
                    byte[] data = File.ReadAllBytes(path);
                    if (ElfRunner.Spawn(data, Path.GetFileName(path), new string[] { path }, out string error) == null)
                    {
                        SystemLogger.WriteLine("ELF", $"Failed to launch {path}: {error}", ConsoleMessageType.Error);
                        return false;
                    }
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

        public static string GetIconPath(string pathOrExtension)
        {
            if (string.IsNullOrEmpty(pathOrExtension)) return "/mnt/System/Icons/file_lines.png";
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
                            return "/mnt/System/Icons/write_wordpad.png";
                        if (info.Target.Contains("Media", StringComparison.OrdinalIgnoreCase))
                            return "/mnt/System/Icons/media_player.png";
                        if (info.Target.Contains("terminal", StringComparison.OrdinalIgnoreCase) || info.Target.Contains("Command", StringComparison.OrdinalIgnoreCase))
                            return "/mnt/System/Icons/console_prompt.png";
                        return GetIconPath(info.Target);
                    }
                }
                return "/mnt/System/Icons/appwiz_file.png";
            }

            string regIcon = Registry.GetString($"{StaticRegistry.FileAssociation}/{ext}", "");
            if (!string.IsNullOrEmpty(regIcon) && File.Exists(regIcon))
                return regIcon;

            switch (ext)
            {
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
                    return "/mnt/System/Icons/paint_file.png";

                case ".avi":
                case ".mp4":
                case ".webm":
                case ".mkv":
                case ".mov":
                case ".wmv":
                case ".flv":
                case ".wav":
                    return "/mnt/System/Icons/media_player_file.png";

                case ".txt":
                case ".log":
                case ".ini":
                case ".cfg":
                case ".json":
                case ".xml":
                case ".cs":
                case ".c":
                case ".h":
                    return "/mnt/System/Icons/notepad_file.png";

                case ".breeze":
                    return "/mnt/System/Icons/write_wordpad.png";

                case ".html":
                case ".htm":
                    return "/mnt/System/Icons/msie.png";

                case ".elf":
                    return "/mnt/System/Icons/application_hammer_grouppol.png";

                case ".lua":
                    return "/mnt/System/Icons/console_prompt.png";

                default:
                    return "/mnt/System/Icons/file_lines.png";
            }
        }

        public static Image GetIcon(string pathOrExtension)
        {
            string iconPath = GetIconPath(pathOrExtension);
            try
            {
                if (File.Exists(iconPath))
                    return new Png(iconPath);
            }
            catch { }
            return new Png("/mnt/System/Icons/file_lines.png");
        }

        public static string GetFileTypeDescription(string pathOrExtension)
        {
            if (string.IsNullOrEmpty(pathOrExtension)) return "File";
            string ext = pathOrExtension.Contains('.') ? Path.GetExtension(pathOrExtension) : pathOrExtension;
            ext = Normalize(ext);

            switch (ext)
            {
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
