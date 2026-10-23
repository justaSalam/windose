using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.Kernel.Attributes;
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
            Register(".mp3", mp);
            Register(".ogg", mp);
            Register(".wma", mp);

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
                case ".mp3":
                case ".ogg":
                case ".wma":
                    return "Audio File";

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
