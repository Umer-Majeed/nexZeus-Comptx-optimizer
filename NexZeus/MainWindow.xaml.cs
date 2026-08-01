using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private DispatcherTimer _timer;
        private bool _wasRobloxRunning = false;

        // Stutter detection tracking
        private float _lastCpu = 0;
        private int _stutterCount = 0;

        // Session Recorder fields
        private SessionRecorder _recorder = new();
        private long _lastPing = 0;

        // Network Diagnostics tracking fields
        private List<long> _recentPings = new();
        private int _pingAttempts = 0;
        private int _pingFailures = 0;

        // Windows Optimizer instance
        private WindowsOptimizer _optimizer = new();

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
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;

            // Ping update event subscriber
            _timer.Tick += async (s, e) => await UpdatePingAsync();

            _timer.Start();

            // GPU name display
            GpuText.Text = $"GPU: {GetGpuName()}";
        }

        private string GetGpuName()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return obj["Name"]?.ToString() ?? "Unknown";
                    }
                }
            }
            catch
            {
                return "N/A";
            }
            return "Unknown";
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Real-time CPU Usage (%)
            float cpu = _cpuCounter.NextValue();
            CpuText.Text = $"CPU: {cpu:F1}%";

            // Stutter Event Detection
            if (_lastCpu > 0 && Math.Abs(cpu - _lastCpu) > 30)
            {
                _stutterCount++;
                StutterText.Text = $"Stutter Events: {_stutterCount}";
            }
            _lastCpu = cpu;

            // App RAM Consumption (GBs mein)
            double appRamGB = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0 / 1024.0;
            RamText.Text = $"App RAM: {appRamGB:F2} GB";

            // Real-time System RAM Usage (Used / Total GB)
            SysRamText.Text = $"System RAM: {GetSystemRamUsage()}";

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
            // Check multiple potential Roblox process names
            var robloxProcesses = Process.GetProcessesByName("RobloxPlayerBeta");
            if (robloxProcesses.Length == 0)
                robloxProcesses = Process.GetProcessesByName("RobloxPlayerLauncher");
            if (robloxProcesses.Length == 0)
                robloxProcesses = Process.GetProcessesByName("Windows10Universal"); // MS Store Version

            bool isRunning = robloxProcesses.Length > 0;

            if (isRunning && !_wasRobloxRunning)
            {
                // Roblox session started
                RobloxStatusText.Text = "Roblox: Session Started";
                RobloxStatusText.Foreground = Brushes.LimeGreen;

                // Auto-start recording
                _recorder.Start();
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                // Roblox session stopped
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = Brushes.Gray;
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = "Roblox: Running";
                RobloxStatusText.Foreground = Brushes.LimeGreen;
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
                    PingText.Text = $"Ping: {_lastPing} ms";

                    _recentPings.Add(_lastPing);
                    if (_recentPings.Count > 10) _recentPings.RemoveAt(0);

                    if (_recentPings.Count > 1)
                    {
                        double avg = _recentPings.Average();
                        double jitter = _recentPings.Select(p => Math.Abs(p - avg)).Average();
                        JitterText.Text = $"Jitter: {jitter:F1} ms";
                    }
                }
                else
                {
                    _pingFailures++;
                    PingText.Text = "Ping: Timeout";
                }
            }
            catch
            {
                _pingFailures++;
                PingText.Text = "Ping: Error";
            }

            double lossPercent = _pingAttempts > 0 ? (_pingFailures * 100.0 / _pingAttempts) : 0;
            PacketLossText.Text = $"Packet Loss: {lossPercent:F1}%";
        }

        private string GetSystemRamUsage()
        {
            try
            {
                double totalGB = 0, freeGB = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        totalGB = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024.0 / 1024.0;
                        freeGB = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024.0 / 1024.0;
                    }
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
            StatusText.Text = "Diagnostics running...";
        }

        private void StopSession_Click(object sender, RoutedEventArgs e)
        {
            var issues = _recorder.AnalyzeSession();
            _recorder.Stop();
            string path = _recorder.SaveReport();

            StatusText.Text = path != null ? "Report saved!" : "No data recorded.";
            ReportText.Text = string.Join("\n", issues);
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
            var results = _optimizer.CheckSettings();
            OptimizationText.Text = string.Join("\n", results);
        }

        private void ApplyFixes_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will enable Windows Game Mode and switch to the High Performance power plan. Continue?",
                "Confirm Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            bool gm = _optimizer.EnableGameMode();
            bool pp = _optimizer.SetHighPerformancePlan();

            OptimizationText.Text = $"Game Mode: {(gm ? "Enabled" : "Failed")}\nPower Plan: {(pp ? "Set to High Performance" : "Failed")}";
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }
    }
}