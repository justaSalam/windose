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
                stream.ReadExactly(buffer);
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

        public static bool WriteStorage(string path, string resource, bool overwrite = false)
        {
            try
            {
                byte[]? data = FromStream(resource);
                if (data == null)
                {
                    return false;
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

        public static void SysRequired()
        {
            WriteStorage("/mnt/Programs/window.elf", "Windose.System.ELF.programs.bin.window.elf", true);
            WriteStorage("/mnt/Programs/hello.elf", "Windose.System.ELF.programs.bin.hello.elf", true);
            WriteStorage("/mnt/Programs/canvas.elf", "Windose.System.ELF.programs.bin.canvas.elf", true);
            WriteStorage("/mnt/Programs/explorer.elf", "Windose.System.ELF.programs.bin.explorer.elf", true);
            WriteStorage("/mnt/Programs/calc.elf", "Windose.System.ELF.programs.bin.calc.elf", true);
            WriteStorage("/mnt/Programs/search.elf", "Windose.System.ELF.programs.bin.search.elf", true);
            WriteStorage("/mnt/Programs/run.elf", "Windose.System.ELF.programs.bin.run.elf", true);
            WriteStorage("/mnt/Programs/pager.elf", "Windose.System.ELF.programs.bin.pager.elf", true);
            WriteStorage("/mnt/Programs/widgets.elf", "Windose.System.ELF.programs.bin.widgets.elf", true);
            WriteStorage("/mnt/Programs/libctest.elf", "Windose.System.ELF.programs.bin.libctest.elf", true);
            WriteStorage("/mnt/Programs/memhog.elf", "Windose.System.ELF.programs.bin.memhog.elf", true);
            WriteStorage("/mnt/Programs/spin.elf", "Windose.System.ELF.programs.bin.spin.elf", true);
            WriteStorage("/mnt/Programs/cosmoslogindialog.elf", "Windose.System.ELF.programs.bin.cosmoslogindialog.elf", true);
            WriteStorage("/mnt/Programs/newelfapp.elf", "Windose.System.ELF.programs.bin.newelfapp.elf", true);

            WriteStorage("/mnt/Programs/aeroeditor.elf", "Windose.System.ELF.programs.bin.aeroeditor.elf", true);
            WriteStorage("/mnt/Programs/dir.elf", "Windose.System.ELF.programs.bin.dir.elf", true);
            WriteStorage("/mnt/Programs/cat.elf", "Windose.System.ELF.programs.bin.cat.elf", true);
            WriteStorage("/mnt/Programs/echo.elf", "Windose.System.ELF.programs.bin.echo.elf", true);
            WriteStorage("/mnt/Programs/cls.elf", "Windose.System.ELF.programs.bin.cls.elf", true);
            WriteStorage("/mnt/Programs/mem.elf", "Windose.System.ELF.programs.bin.mem.elf", true);
            WriteStorage("/mnt/Programs/mkdir.elf", "Windose.System.ELF.programs.bin.mkdir.elf", true);
            WriteStorage("/mnt/Programs/rm.elf", "Windose.System.ELF.programs.bin.rm.elf", true);
            WriteStorage("/mnt/Programs/touch.elf", "Windose.System.ELF.programs.bin.touch.elf", true);
            WriteStorage("/mnt/Programs/terminal.elf", "Windose.System.ELF.programs.bin.terminal.elf", true);
            WriteStorage("/mnt/Programs/startmenu.elf", "Windose.System.ELF.programs.bin.startmenu.elf", true);
            WriteStorage("/mnt/Programs/taskmgr.elf", "Windose.System.ELF.programs.bin.taskmgr.elf", true);
            WriteStorage("/mnt/Programs/regedit.elf", "Windose.System.ELF.programs.bin.regedit.elf", true);
            WriteStorage("/mnt/Programs/winver.elf", "Windose.System.ELF.programs.bin.winver.elf", true);
            WriteStorage("/mnt/Programs/paint.elf", "Windose.System.ELF.programs.bin.paint.elf", true);
            WriteStorage("/mnt/Programs/tcc.elf", "Windose.System.ELF.programs.bin.tcc.elf", true);
            WriteStorage("/mnt/Programs/properties.elf", "Windose.System.ELF.programs.bin.properties.elf", true);
            WriteStorage("/mnt/Programs/sysdm.elf", "Windose.System.ELF.programs.bin.sysdm.elf", true);

            WriteStorage("/mnt/System/include/kernel.h", "Windose.System.ELF.programs.include.kernel.h", true);
            WriteStorage("/mnt/System/include/windose.h", "Windose.System.ELF.programs.include.windose.h", true);
            WriteStorage("/mnt/System/include/stdio.h", "Windose.System.ELF.programs.include.stdio.h", true);
            WriteStorage("/mnt/System/include/stdlib.h", "Windose.System.ELF.programs.include.stdlib.h", true);
            WriteStorage("/mnt/System/include/string.h", "Windose.System.ELF.programs.include.string.h", true);
            WriteStorage("/mnt/System/include/stdint.h", "Windose.System.ELF.programs.include.stdint.h", true);
            WriteStorage("/mnt/System/include/ctype.h", "Windose.System.ELF.programs.include.ctype.h", true);
            WriteStorage("/mnt/System/include/stdbool.h", "Windose.System.ELF.programs.include.stdbool.h", true);
            WriteStorage("/mnt/System/include/assert.h", "Windose.System.ELF.programs.include.assert.h", true);
            WriteStorage("/mnt/System/include/limits.h", "Windose.System.ELF.programs.include.limits.h", true);
            WriteStorage("/mnt/System/include/errno.h", "Windose.System.ELF.programs.include.errno.h", true);
            WriteStorage("/mnt/System/include/math.h", "Windose.System.ELF.programs.include.math.h", true);
            WriteStorage("/mnt/System/include/time.h", "Windose.System.ELF.programs.include.time.h", true);
        }
    }
}
