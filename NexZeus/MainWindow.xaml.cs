using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramCounter;
        private readonly DispatcherTimer _timer;
        private bool _wasRobloxRunning = false;

        // Stutter detection tracking
        private float _lastCpu = 0;
        private int _stutterCount = 0;

        // Session Recorder fields
        private readonly SessionRecorder _recorder = new();
        private long _lastPing = 0;

        // Network Diagnostics tracking fields
        private readonly List<long> _recentPings = [];
        private int _pingAttempts = 0;
        private int _pingFailures = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        public MainWindow()
        {
            InitializeComponent();

            // CPU aur RAM counters initialize kar rahe hain
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            // Initial call zero value avoidance ke liye
            _cpuCounter.NextValue();
            _ramCounter.NextValue();

            // Timer set up (har 1 second mein update hoga)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            // Ping update event subscriber
            _timer.Tick += async (s, e) => await UpdatePingAsync();

            _timer.Start();

            // GPU name display
            GpuText.Text = $"GPU: {GetGpuName()}";
        }

        private static string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    return obj["Name"]?.ToString() ?? "Unknown";
                }
            }
            catch
            {
                return "N/A";
            }
            return "Unknown";
        }

        private void Timer_Tick(object? sender, EventArgs? e)
        {
            // Real-time CPU Usage (%)
            float cpu = _cpuCounter.NextValue();
            CpuText.Text = $"{cpu:F1}%";

            // Stutter Event Detection
            if (_lastCpu > 0 && Math.Abs(cpu - _lastCpu) > 30)
            {
                _stutterCount++;
                StutterText.Text = _stutterCount.ToString();
            }
            _lastCpu = cpu;

            // App RAM Consumption (GBs mein)
            double appRamGB = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0 / 1024.0;
            RamText.Text = $"{appRamGB:F2} GB";

            // Real-time System RAM Usage (Used / Total GB)
            SysRamText.Text = GetSystemRamUsage();

            // Roblox status check
            CheckRobloxStatus();

            // Active session record sample
            if (_recorder.IsRecording)
            {
                _recorder.AddSample(cpu, appRamGB, _lastPing, _stutterCount);
            }

            // Threshold Warning Check
            CheckThresholds(cpu, _lastPing);
        }

        private void CheckThresholds(float cpu, long ping)
        {
            var warnings = new List<string>();

            if (cpu > AppSettings.CpuThresholdPercent)
                warnings.Add($"⚠ CPU above threshold ({cpu:F0}% > {AppSettings.CpuThresholdPercent}%)");

            if (ping > AppSettings.PingThresholdMs && ping > 0)
                warnings.Add($"⚠ Ping above threshold ({ping}ms > {AppSettings.PingThresholdMs}ms)");

            WarningText.Text = string.Join("  |  ", warnings);
        }

        private void CheckRobloxStatus()
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length == 0)
                processes = Process.GetProcessesByName("RobloxPlayerLauncher");
            if (processes.Length == 0)
                processes = Process.GetProcessesByName("Windows10Universal");

            bool isRunning = processes.Length > 0;
            bool isBloxStrike = false;

            if (isRunning)
            {
                var sb = new StringBuilder(256);
                IntPtr hWnd = GetForegroundWindow();
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();
                isBloxStrike = title.Contains("BloxStrike", StringComparison.OrdinalIgnoreCase) ||
                               title.Contains("Roblox", StringComparison.OrdinalIgnoreCase);
            }

            if (isRunning && !_wasRobloxRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Session Started" : "Roblox: Session Started";
                RobloxStatusText.Foreground = Brushes.LimeGreen;
                _recorder.Start();
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = Brushes.Gray;
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Active" : "Roblox: Running";
                RobloxStatusText.Foreground = isBloxStrike ? Brushes.Lime : Brushes.LimeGreen;
            }

            _wasRobloxRunning = isRunning;
        }

        private async Task UpdatePingAsync()
        {
            _pingAttempts++;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1000);

                if (reply.Status == IPStatus.Success)
                {
                    _lastPing = reply.RoundtripTime;
                    PingText.Text = $"{_lastPing} ms";

                    _recentPings.Add(_lastPing);
                    if (_recentPings.Count > 10) _recentPings.RemoveAt(0);

                    if (_recentPings.Count > 1)
                    {
                        double avg = _recentPings.Average();
                        double jitter = _recentPings.Select(p => Math.Abs(p - avg)).Average();
                        JitterText.Text = $"{jitter:F1} ms";
                    }
                }
                else
                {
                    _pingFailures++;
                    PingText.Text = "Timeout";
                }
            }
            catch
            {
                _pingFailures++;
                PingText.Text = "Error";
            }

            double lossPercent = _pingAttempts > 0 ? (_pingFailures * 100.0 / _pingAttempts) : 0;
            PacketLossText.Text = $"{lossPercent:F1}%";
        }

        private static string GetSystemRamUsage()
        {
            try
            {
                double totalGB = 0, freeGB = 0;
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    totalGB = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024.0 / 1024.0;
                    freeGB = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024.0 / 1024.0;
                }
                double usedGB = totalGB - freeGB;
                return $"{usedGB:F1} / {totalGB:F1} GB";
            }
            catch
            {
                return "N/A";
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            ReportText.Text = "Diagnostics running...";
        }

        private void StopSession_Click(object sender, RoutedEventArgs e)
        {
            _recorder.AnalyzeSession();
            _recorder.Stop();
            string? path = _recorder.SaveReport();

            ReportText.Text = path != null ? "Report saved successfully!" : "No data recorded.";
        }

        private void ViewHistory_Click(object sender, RoutedEventArgs e)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NexZeus", "Sessions");

            if (!Directory.Exists(folder))
            {
                ReportText.Text = "No sessions recorded yet.";
                return;
            }

            var files = Directory.GetFiles(folder, "*.csv")
                                  .OrderByDescending(f => f)
                                  .Take(5)
                                  .Select(Path.GetFileName);

            ReportText.Text = files.Any()
                ? "Recent sessions:\n" + string.Join("\n", files)
                : "No sessions recorded yet.";
        }

        private void CheckOptimization_Click(object sender, RoutedEventArgs e)
        {
            var results = WindowsOptimizer.CheckSettings();
            OptimizationText.Text = string.Join("\n", results);
        }

        private void ApplyFixes_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will enable Windows Game Mode and switch to the High Performance power plan. Continue?",
                "Confirm Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            bool gm = WindowsOptimizer.EnableGameMode();
            bool pp = WindowsOptimizer.SetHighPerformancePlan();

            OptimizationText.Text = $"Game Mode: {(gm ? "Enabled" : "Failed")}\nPower Plan: {(pp ? "Set to High Performance" : "Failed")}";
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow { Owner = this };
            settings.ShowDialog();
        }
    }
}