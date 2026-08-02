using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace NexZeus
{
    public class ProcessInfo
    {
        public int Pid { get; set; }
        public required string Name { get; set; }
        public double CpuPercent { get; set; }
        public double RamMB { get; set; }
        public bool IsSuspended { get; set; }
    }

    public class ProcessManager
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);

        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "explorer", "dwm", "RobloxPlayerBeta", "NexZeus",
            "audiodg", "smss", "fontdrvhost", "registry"
        };

        public static List<ProcessInfo> GetBackgroundProcesses()
        {
            var result = new List<ProcessInfo>();
            var processes = Process.GetProcesses()
                .Where(p => !ProtectedProcesses.Contains(p.ProcessName))
                .OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; } catch { return 0; }
                })
                .Take(15);

            foreach (var p in processes)
            {
                try
                {
                    result.Add(new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        RamMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                        CpuPercent = 0
                    });
                }
                catch { }
            }
            return result;
        }

        public static bool SuspendProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (ProtectedProcesses.Contains(process.ProcessName)) return false;
                int status = NtSuspendProcess(process.Handle);
                return status >= 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool ResumeProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                int status = NtResumeProcess(process.Handle);
                return status >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}