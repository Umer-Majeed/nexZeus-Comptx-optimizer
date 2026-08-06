using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexZeus
{
    public static class RamOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        private static readonly string[] ProtectedProcesses =
        {
            "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "dwm", "audiodg", "smss", "fontdrvhost", "registry",
            "Memory Compression", "Secure System", "MsMpEng", "SecurityHealthService",
            "RobloxPlayerBeta", "NexZeus", "devenv"
        };

        public static (int trimmedCount, long estimatedFreedMB) TrimStandbyMemory()
        {
            int trimmed = 0;
            long freedEstimate = 0;
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.HasExited) continue;
                    if (process.SessionId != currentSessionId) continue;
                    if (Array.Exists(ProtectedProcesses, p => p.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase))) continue;

                    long beforeMB = process.WorkingSet64 / 1024 / 1024;

                    bool success = EmptyWorkingSet(process.Handle) != 0;
                    if (success)
                    {
                        trimmed++;
                        freedEstimate += beforeMB;
                    }
                }
                catch { /* access denied or process exited, skip */ }
            }

            return (trimmed, freedEstimate);
        }
    }
}