using Cosmos.Kernel.Core.Runtime;
using System.Reflection;
using Windose.System.System_Calls;
using Windose.System.Elf;

namespace Windose.System.Kernel
{
    public static class ResourceLoader
    {
        public static Assembly assembly { get; private set; } = typeof(ResourceLoader).Assembly;

        /// <summary>
        /// Loads a resource from the embedded resources and returns its byte array representation.
        /// </summary>
        public static byte[]? FromStream(string path)
        {
            using (Stream? stream = assembly.GetManifestResourceStream(path))
            {
                if (stream == null)
                {
                    SystemLogger.WriteLine("Resource Loader", $"Resource not found: {path}", ConsoleMessageType.Error);
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                stream.ReadExactly(buffer);
                return buffer;
            }
        }

        public static bool WriteStorage(string path, string resource, bool overwrite = false)
        {
            try
            {
                byte[]? data = FromStream(resource);
                if (data == null)
                {
                    return false;
                }

                if (path.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
                {
                    ElfRunner.Preload(path, data);
                }

                if (File.Exists(path) && !overwrite)
                {
                    return false;
                }

                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(path, data);
                return true;
            }
            catch (Exception e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        public static bool WriteStorage(string path, byte[] data)
        {
            try
            {
                if (data != null && path.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
                {
                    ElfRunner.Preload(path, data);
                }
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, data);
                return true;
            }
            catch (Exception e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write resource to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        const string prefix = "Windose.Resources.";
        const string storageRoot = "/mnt/System";
        public static void LoadAssemblyResources()
        {
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(prefix))
                    continue;

                string relative = resource.Substring(prefix.Length);
                int extensionIndex = relative.LastIndexOf('.');
                if (extensionIndex == -1)
                    continue;

                string name = relative.Substring(0, extensionIndex);
                string extension = relative.Substring(extensionIndex);
                string path = storageRoot + "/" + name.Replace('.', '/') + extension;

                if (!File.Exists(path))
                {
                    WriteStorage(path, resource);
                }
            }
        }

        public static void SysRequired()
        {
            WriteStorage("/mnt/System/Icons/console_prompt.png", "Windose.Resources.Icons.console_prompt.png", true);

            const string elfPrefix = "Windose.System.ELF.programs.bin.";
            const string incPrefix = "Windose.System.ELF.programs.include.";

            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(elfPrefix, StringComparison.OrdinalIgnoreCase) &&
                    resource.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
                {
                    string name = resource.Substring(elfPrefix.Length);
                    WriteStorage("/mnt/Programs/" + name, resource, true);
                }
                else if (resource.StartsWith(incPrefix, StringComparison.OrdinalIgnoreCase) &&
                         resource.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                {
                    string rel = resource.Substring(incPrefix.Length);
                    if (rel.StartsWith("windose.", StringComparison.OrdinalIgnoreCase))
                    {
                        rel = "windose/" + rel.Substring("windose.".Length);
                    }
                    WriteStorage("/mnt/System/include/" + rel, resource, true);
                }
            }
        }
    }
}
