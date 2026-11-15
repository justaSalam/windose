using System;
using System.Collections.Generic;
using System.IO;
using Windose.System.System_Calls;

namespace Windose.System.Kernel.Subsystem
{
    public static class FileClipboard
    {
        public static List<string> Paths { get; } = new List<string>();
        public static bool IsCut { get; private set; }

        public static bool HasContent => Paths.Count > 0;

        public static void SetCopy(IEnumerable<string> paths)
        {
            Paths.Clear();
            if (paths != null)
                Paths.AddRange(paths);
            IsCut = false;
        }

        public static void SetCut(IEnumerable<string> paths)
        {
            Paths.Clear();
            if (paths != null)
                Paths.AddRange(paths);
            IsCut = true;
        }

        public static void Clear()
        {
            Paths.Clear();
            IsCut = false;
        }

        public static bool PasteTo(string destinationDirectory)
        {
            if (string.IsNullOrEmpty(destinationDirectory) || !Directory.Exists(destinationDirectory) || Paths.Count == 0)
                return false;

            bool anySuccess = false;
            string[] items = Paths.ToArray();

            foreach (string sourcePath in items)
            {
                if (string.IsNullOrWhiteSpace(sourcePath)) continue;

                try
                {
                    if (File.Exists(sourcePath))
                    {
                        string fileName = Path.GetFileName(sourcePath);
                        string destPath = Path.Combine(destinationDirectory, fileName);

                        if (IsCut)
                        {
                            if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(destPath)) File.Delete(destPath);
                                File.Move(sourcePath, destPath);
                                anySuccess = true;
                            }
                        }
                        else
                        {
                            if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                            {
                                string baseName = Path.GetFileNameWithoutExtension(fileName);
                                string ext = Path.GetExtension(fileName);
                                destPath = FileSystemManager.GetUniquePath(destinationDirectory, $"{baseName} - Copy", ext);
                            }
                            File.Copy(sourcePath, destPath, true);
                            anySuccess = true;
                        }
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        string dirName = Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
                        string destPath = Path.Combine(destinationDirectory, dirName);

                        if (IsCut)
                        {
                            if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                                Directory.Move(sourcePath, destPath);
                                anySuccess = true;
                            }
                        }
                        else
                        {
                            if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                            {
                                destPath = FileSystemManager.GetUniquePath(destinationDirectory, $"{dirName} - Copy");
                            }
                            CopyDirectoryRecursive(sourcePath, destPath);
                            anySuccess = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("Clipboard", $"Paste error for {sourcePath}: {ex.Message}", ConsoleMessageType.Error);
                }
            }

            if (IsCut && anySuccess)
            {
                Paths.Clear();
                IsCut = false;
            }

            return anySuccess;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string folderName = Path.GetFileName(subDir.TrimEnd('/', '\\'));
                string targetSubDir = Path.Combine(destDir, folderName);
                CopyDirectoryRecursive(subDir, targetSubDir);
            }
        }
    }
}
