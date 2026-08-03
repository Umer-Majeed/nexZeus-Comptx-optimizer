using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NexZeus
{
    public class ProcessInfo : INotifyPropertyChanged
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double RamMB { get; set; }

        private bool _isSuspended;
        public bool IsSuspended
        {
            get => _isSuspended;
            set { _isSuspended = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSuspended))); }
        }

        public bool IsExcluded { get; set; }
        public string Category { get; set; } = "Background Processes";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ProcessManager
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        private static readonly HashSet<string> WindowsSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "dwm", "audiodg", "smss", "fontdrvhost", "registry",
            "Memory Compression", "Secure System", "MsMpEng", "SecurityHealthService",
            "sihost", "ctfmon", "TaskHostW", "ShellExperienceHost", "SearchUI",
            "RuntimeBroker", "ApplicationFrameHost", "TextInputHost", "WmiPrvSE", "conhost"
        };

        private readonly HashSet<int> _suspendedPids = [];

        public Task<List<ProcessInfo>> GetBackgroundProcessesAsync()
        {
            return Task.Run(() =>
            {
                var excluded = AppSettings.ExcludedProcessNames;
                var result = new List<ProcessInfo>();
                int currentSessionId = Process.GetCurrentProcess().SessionId;

                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.HasExited) continue;
                        if (p.Id <= 4 || p.Id == Environment.ProcessId) continue;
                        if (p.SessionId != currentSessionId) continue;

                        string name = p.ProcessName;
                        if (WindowsSystemProcesses.Contains(name)) continue;

                        string category = !string.IsNullOrEmpty(p.MainWindowTitle) ? "Apps" : "Background Processes";
                        double ramMb = p.WorkingSet64 / 1024.0 / 1024.0;

                        result.Add(new ProcessInfo
                        {
                            Pid = p.Id,
                            Name = name,
                            RamMB = Math.Round(ramMb, 1),
                            IsSuspended = _suspendedPids.Contains(p.Id),
                            IsExcluded = excluded.Contains(name),
                            Category = category
                        });
                    }
                    catch { }
                }

                return result.OrderByDescending(p => p.RamMB).ToList();
            });
        }

        public Task<List<ProcessGroupInfo>> GetGroupedProcessesAsync()
        {
            return Task.Run(async () =>
            {
                var flat = await GetBackgroundProcessesAsync();
                var groups = flat
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ProcessGroupInfo
                    {
                        Name = g.Key,
                        Category = g.First().Category,
                        Instances = new ObservableCollection<ProcessInfo>(g.OrderBy(p => p.Pid)),
                        IsExpanded = false
                    })
                    .OrderByDescending(g => g.TotalRamMB)
                    .ToList();
                return groups;
            });
        }

        public async Task<int> SuspendGroupAsync(ProcessGroupInfo group)
        {
            int success = 0;
            foreach (var p in group.Instances)
            {
                if (await SuspendProcessAsync(p.Pid)) { p.IsSuspended = true; success++; }
            }
            return success;
        }

        public async Task<int> ResumeGroupAsync(ProcessGroupInfo group)
        {
            int success = 0;
            foreach (var p in group.Instances)
            {
                if (await ResumeProcessAsync(p.Pid)) { p.IsSuspended = false; success++; }
            }
            return success;
        }

        public Task<bool> SuspendProcessAsync(int pid)
        {
            return Task.Run(() =>
            {
                IntPtr handle = IntPtr.Zero;
                try
                {
                    handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                    if (handle == IntPtr.Zero) return false;

                    int status = NtSuspendProcess(handle);
                    if (status == 0)
                    {
                        _suspendedPids.Add(pid);
                        return true;
                    }
                    return false;
                }
                catch { return false; }
                finally
                {
                    if (handle != IntPtr.Zero) CloseHandle(handle);
                }
            });
        }

        public Task<bool> ResumeProcessAsync(int pid)
        {
            return Task.Run(() =>
            {
                IntPtr handle = IntPtr.Zero;
                try
                {
                    handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                    if (handle == IntPtr.Zero) return false;

                    int status = NtResumeProcess(handle);
                    if (status == 0)
                    {
                        _suspendedPids.Remove(pid);
                        return true;
                    }
                    return false;
                }
                catch { return false; }
                finally
                {
                    if (handle != IntPtr.Zero) CloseHandle(handle);
                }
            });
        }
    }
}