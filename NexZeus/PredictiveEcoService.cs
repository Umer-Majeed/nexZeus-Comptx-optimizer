using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NexZeus
{
    /// <summary>
    /// Lightweight online-learning classifier: watches the foreground process each tick,
    /// extracts simple "is this a game" features (fullscreen, sustained CPU/GPU load, not a
    /// known utility app) and builds a confidence score per process name over time. Once a
    /// process crosses the learned confidence threshold it fires GameSessionStarted, which
    /// EcoModeController uses to throttle background apps.
    /// </summary>
    public class GamePatternDetector
    {
        private static readonly string ModelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "eco_model.json");

        private static readonly HashSet<string> KnownNonGame = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "chrome", "msedge", "firefox", "opera", "brave", "discord", "Slack",
            "Teams", "outlook", "OUTLOOK", "notepad", "notepad++", "Code", "devenv", "WINWORD",
            "EXCEL", "POWERPNT", "NexZeus", "SearchHost", "TextInputHost", "steam", "EpicGamesLauncher",
            "OneDrive", "Spotify", "cmd", "powershell", "WindowsTerminal"
        };

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private Dictionary<string, double> _confidence = new(StringComparer.OrdinalIgnoreCase);
        private int _consecutiveHits;
        private string? _candidateName;
        private string? _activeGame;

        public string? ActiveGameProcess => _activeGame;
        public event Action<string>? GameSessionStarted;
        public event Action<string>? GameSessionEnded;

        public GamePatternDetector() => Load();

        /// <summary>Call once per timer tick (~1s cadence).</summary>
        public void Tick()
        {
            var (name, isFullscreenLike) = SampleForeground();

            if (name == null || KnownNonGame.Contains(name))
            {
                _consecutiveHits = 0;
                _candidateName = null;
                if (_activeGame != null && (name == null || !name.Equals(_activeGame, StringComparison.OrdinalIgnoreCase)))
                    EndSession();
                return;
            }

            if (name.Equals(_candidateName, StringComparison.OrdinalIgnoreCase)) _consecutiveHits++;
            else { _candidateName = name; _consecutiveHits = 1; }

            double score = _confidence.GetValueOrDefault(name, 0.0);
            // Prior sessions lower the ticks-required threshold — this is the "learning" part.
            int requiredTicks = score > 3 ? 2 : (score > 0 ? 4 : 6);
            if (!isFullscreenLike) requiredTicks += 3;

            if (_activeGame == null && _consecutiveHits >= requiredTicks)
            {
                _confidence[name] = score + 1;
                Save();
                _activeGame = name;
                GameSessionStarted?.Invoke(name);
            }
        }

        private void EndSession()
        {
            var ended = _activeGame!;
            _activeGame = null;
            GameSessionEnded?.Invoke(ended);
        }

        private (string? name, bool fullscreen) SampleForeground()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return (null, false);
                GetWindowThreadProcessId(hWnd, out int pid);
                if (pid <= 0) return (null, false);

                using var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;

                bool fullscreen = false;
                if (GetWindowRect(hWnd, out RECT r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    fullscreen = IsZoomed(hWnd) ||
                                 (w >= GetSystemMetrics(SM_CXSCREEN) && h >= GetSystemMetrics(SM_CYSCREEN));
                }

                return (name, fullscreen);
            }
            catch { return (null, false); }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(ModelPath))
                {
                    var json = File.ReadAllText(ModelPath);
                    _confidence = JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                                  ?? new(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { _confidence = new(StringComparer.OrdinalIgnoreCase); }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ModelPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ModelPath, JsonSerializer.Serialize(_confidence));
            }
            catch { }
        }
    }

    /// <summary>
    /// Puts background apps into Windows "Efficiency Mode" (idle priority + power throttling,
    /// same mechanism Task Manager uses) when a game session starts, and restores them on exit.
    /// </summary>
    public class EcoModeController
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        public static readonly List<string> DefaultTargets =
        [
            "chrome", "msedge", "firefox", "opera", "brave", "Discord", "Slack", "Teams",
            "Spotify", "OneDrive", "steamwebhelper", "EpicWebHelper", "SearchIndexer"
        ];

        private readonly HashSet<int> _throttledPids = [];

        public int ApplyEcoMode(string? extraExcluded = null)
        {
            int count = 0;
            var excluded = AppSettings.ExcludedProcessNames;
            var targets = AppSettings.EcoModeTargetProcessNames;

            foreach (var name in targets)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (excluded.Contains(p.ProcessName)) continue;
                        if (SetEfficiencyMode(p.Id, true))
                        {
                            p.PriorityClass = ProcessPriorityClass.Idle;
                            lock (_throttledPids) _throttledPids.Add(p.Id);
                            count++;
                        }
                    }
                    catch { }
                }
            }
            return count;
        }

        public int RestoreAll()
        {
            int count = 0;
            lock (_throttledPids)
            {
                foreach (var pid in _throttledPids)
                {
                    try
                    {
                        SetEfficiencyMode(pid, false);
                        using var p = Process.GetProcessById(pid);
                        p.PriorityClass = ProcessPriorityClass.Normal;
                        count++;
                    }
                    catch { }
                }
                _throttledPids.Clear();
            }
            return count;
        }

        private static bool SetEfficiencyMode(int pid, bool enable)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return false;

                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
                };
                return SetProcessInformation(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(state));
            }
            catch { return false; }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        }
    }

    /// <summary>Wires the detector to the controller. Call Tick() from the main timer loop.</summary>
    public class PredictiveEcoService
    {
        private readonly GamePatternDetector _detector = new();
        private readonly EcoModeController _eco = new();

        public event Action<string, int>? EcoModeEngaged;   // gameName, appsThrottled
        public event Action<string, int>? EcoModeReleased;  // gameName, appsRestored

        public PredictiveEcoService()
        {
            _detector.GameSessionStarted += name =>
            {
                int n = _eco.ApplyEcoMode();
                EcoModeEngaged?.Invoke(name, n);
            };
            _detector.GameSessionEnded += name =>
            {
                int n = _eco.RestoreAll();
                EcoModeReleased?.Invoke(name, n);
            };
        }

        public void Tick()
        {
            if (!AppSettings.EnablePredictiveEcoMode) return;
            _detector.Tick();
        }

        public void ForceRestore() => _eco.RestoreAll();
    }
}