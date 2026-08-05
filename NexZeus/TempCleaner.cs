using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NexZeus
{
    public class CleanupTarget
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
    }

    public static class TempCleaner
    {
        private static List<string> GetTargetFolders()
        {
            return new List<string>
            {
                Path.GetTempPath(),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Temp"),
                Environment.ExpandEnvironmentVariables(@"%WINDIR%\Prefetch"),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\INetCache")
            };
        }

        public static List<CleanupTarget> ScanTargets()
        {
            var results = new List<CleanupTarget>();

            foreach (var folder in GetTargetFolders())
            {
                if (!Directory.Exists(folder)) continue;

                long totalSize = 0;
                int count = 0;

                try
                {
                    var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            totalSize += info.Length;
                            count++;
                        }
                        catch { /* file locked/inaccessible, skip */ }
                    }
                }
                catch { /* folder inaccessible, skip */ }

                if (count > 0)
                {
                    results.Add(new CleanupTarget
                    {
                        Name = new DirectoryInfo(folder).Name,
                        Path = folder,
                        SizeBytes = totalSize,
                        FileCount = count
                    });
                }
            }

            return results;
        }

        public static (int deletedFiles, long freedBytes, int failedFiles) CleanFolder(string folderPath)
        {
            int deleted = 0;
            int failed = 0;
            long freedBytes = 0;

            if (!Directory.Exists(folderPath)) return (0, 0, 0);

            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        long size = info.Length;
                        File.Delete(file);
                        deleted++;
                        freedBytes += size;
                    }
                    catch
                    {
                        failed++; // file in use, permission denied, etc — safely skipped
                    }
                }
            }
            catch { }

            return (deleted, freedBytes, failed);
        }

        public static string FormatSize(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
        }
    }
}