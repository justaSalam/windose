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
            return ResourceManager.GetResourceAsSpan(path).ToArray();
        }


        /// <summary>
        /// Loads a resource from storage (file system) and returns its byte array representation.
        /// Use absolute path to the resource file. If the resource cannot be loaded, it returns null and logs an error message.
        /// Will throw if Vfs is not initialized and the file doesn't exists. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static byte[]? FromStorage(string path)
        {
            try
            {
                return File.ReadAllBytes(path);

            }
            catch(Exception e) 
            {
                ConsoleMessage.WriteLine("Resource Loader", $"Failed to load resource from storage: {path}. Exception: {e.Message}", ConsoleMessageType.Error);
                return null;
            }
        }
    }
}
