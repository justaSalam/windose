using Cosmos.Kernel.Core.Runtime;
using Windose.System.System_Calls;


namespace Windose.System.Kernel
{
    internal static class ResourceLoader
    {

        /// <summary>
        /// Loads a resource from the embedded resources and returns its byte array representation.
        /// AssemblyName.ResourceFolder.ResourceFileName.ResourceExtension.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static byte[]? FromStream(string path)
        {
            try
            {
                return ResourceManager.GetResourceAsSpan(path).ToArray();
            }
            catch (Exception e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to load resource: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return null;
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

        public static bool WriteStorageAsync(string path, string resource)
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

                File.WriteAllBytes(path, data);
                return true;
            }
            catch (OperationCanceledException e)
            {
                SystemLogger.WriteLine("Resource Loader", $"Failed to write to storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        public static bool WriteStorageAsync(string path, byte[] data)
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
    }
}
