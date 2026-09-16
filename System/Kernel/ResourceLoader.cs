using Cosmos.Kernel.Core.Runtime;
using System.Reflection;
using Windose.System.System_Calls;


namespace Windose.System.Kernel
{
    public static class ResourceLoader
    {
        public static Assembly assembly { get; private set; } = typeof(ResourceLoader).Assembly;
        /// <summary>
        /// Loads a resource from the embedded resources and returns its byte array representation.
        /// AssemblyName.ResourceFolder.ResourceFileName.ResourceExtension.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
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
                stream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }


        /// <summary>
        /// Loads a resource from storage (file system) and returns its byte array representation.
        /// Use absolute path to the resource file. If the resource cannot be loaded, it returns null and logs an error message.
        /// Will throw if Vfs is not initialized and the file doesn't exists. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static async Task<byte[]?> LoadStorageAsync(string path)
        {
            try
            {
                return await File.ReadAllBytesAsync(path);
            }
            catch (OperationCanceledException e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to load resource from storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return null;
            }
        }

        public static bool WriteStorage(string path, string resource)
        {
            try
            {
                byte[]? data = FromStream(resource);
                if (data == null)
                {
                    return false;
                }
                if (File.Exists(path))
                {
                    return false;
                }

                string? directory = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(path, data);

                return true;
            }
            catch (OperationCanceledException e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        public static bool WriteStorage(string path, byte[] data)
        {
            try
            {
                File.WriteAllBytes(path, data);
                return true;
            }
            catch (OperationCanceledException e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write resource to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }


        public static bool WriteStorage(string path, Stream stream)
        {
            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
                return true;
            }
            catch (OperationCanceledException e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write resource to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        //TODO generate path based on resource name and folder structure, and write to storage if not exists
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
    }
}
