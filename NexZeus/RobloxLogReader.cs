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

        [GeneratedRegex(@"UDMUX Address\s*=\s*((?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase)]
        private static partial Regex ServerIpRegex();

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

        /// <summary>Real BloxStrike/Roblox game-server IP for the currently active place, parsed from the live log.
        /// Returns null if not found — caller should fall back to a generic ping target only if this is null.</summary>
        public static string? GetCurrentServerIp()
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

                var matches = ServerIpRegex().Matches(content);
                if (matches.Count > 0)
                    return matches[^1].Groups[1].Value; // most recent = current server

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}