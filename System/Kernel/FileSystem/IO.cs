using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.Kernel.FileSystem
{
    public static class IO
    {
        public static bool TryLoadFile(string path, out byte[] data)
        {
            try
            {
                data = File.ReadAllBytes(path);
                return true;
            }
            catch
            {
                data = Array.Empty<byte>();
                return false;
            }
        }
        public static bool TryLoadFile(string path, out string data)
        {
            try
            {
                data = File.ReadAllText(path);
                return true;
            }
            catch
            {
                data = string.Empty;
                return false;
            }
        }
        public static bool TryLoadFile(string path, out string[] lines)
        {
            try
            {
                lines = File.ReadAllLines(path);
                return true;
            }
            catch
            {
                lines = Array.Empty<string>();
                return false;
            }
        }

        public static bool TrySaveFile(string path, byte[] data)
        {
            try
            {
                File.WriteAllBytes(path, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySaveFile(string path, string data)
        {
            try
            {
                File.WriteAllText(path, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySaveFile(string path, string[] lines)
        {
            try
            {
                File.WriteAllLines(path, lines);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCreateDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetFiles(string path, out string[] files)
        {
            try
            {
                files = Directory.GetFiles(path);
                return true;
            }
            catch
            {
                files = Array.Empty<string>();
                return false;
            }
        }
        public static bool TryGetDirectories(string path, out string[] directories)
        {
            try
            {
                directories = Directory.GetDirectories(path);
                return true;
            }
            catch
            {
                directories = Array.Empty<string>();
                return false;
            }
        }

        public static bool TryGetFileInfo(string path, out FileInfo fileInfo)
        {
            try
            {
                fileInfo = new FileInfo(path);
                return true;
            }
            catch
            {
                fileInfo = null;
                return false;
            }
        }

        public static bool TryGetDirectoryInfo(string path, out DirectoryInfo directoryInfo)
        {
            try
            {
                directoryInfo = new DirectoryInfo(path);
                return true;
            }
            catch
            {
                directoryInfo = null;
                return false;
            }
        }


        }
}
