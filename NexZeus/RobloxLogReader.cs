using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NexZeus
{
    public static partial class RobloxLogReader
    {
        [GeneratedRegex(@"placeId[:=]\s*(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex PlaceIdRegex();

        public static string? GetCurrentPlaceId()
        {
            try
            {
                string logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "logs");

                if (!Directory.Exists(logsFolder)) return null;

                FileInfo? latestLog = new DirectoryInfo(logsFolder)
                    .GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (latestLog == null) return null;

                using var fs = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string content = reader.ReadToEnd();

                var matches = PlaceIdRegex().Matches(content);
                if (matches.Count > 0)
                {
                    return matches[^1].Groups[1].Value;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}