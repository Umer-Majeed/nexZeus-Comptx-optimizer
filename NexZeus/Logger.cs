using System;
using System.IO;
using System.Text;

namespace NexZeus
{
    /// <summary>
    /// Simple rolling file logger. Writes to
    /// %UserProfile%\Documents\NexZeus\Logs\nexzeus-YYYY-MM-DD.log
    /// so users (and support requests) can actually see what went wrong,
    /// instead of everything disappearing into Debug.WriteLine.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "Logs");

        private static readonly object _lock = new();

        private static string CurrentLogFile =>
            Path.Combine(LogFolder, $"nexzeus-{DateTime.Now:yyyy-MM-dd}.log");

        public static string LogFolderPath => LogFolder;

        public static void LogInfo(string message) => Write("INFO", message);

        public static void LogWarning(string message) => Write("WARN", message);

        public static void LogError(string message) => Write("ERROR", message);

        public static void LogException(Exception ex, string? context = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(context))
                sb.AppendLine($"Context: {context}");

            sb.AppendLine(ex.ToString());

            Write("ERROR", sb.ToString());
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogFolder);
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(CurrentLogFile, line);
                }
            }
            catch
            {
                // Logging must never crash the app it's trying to protect.
            }

            // Still write to Debug output for anyone running from Visual Studio.
            System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        }

        /// <summary>Deletes log files older than the given number of days (default 14) to avoid unbounded growth.</summary>
        public static void PruneOldLogs(int daysToKeep = 14)
        {
            try
            {
                if (!Directory.Exists(LogFolder)) return;

                foreach (var file in Directory.GetFiles(LogFolder, "nexzeus-*.log"))
                {
                    if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-daysToKeep))
                        File.Delete(file);
                }
            }
            catch
            {
                // best-effort cleanup only
            }
        }
    }
}