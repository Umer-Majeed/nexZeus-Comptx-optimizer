using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private DispatcherTimer _timer;
        private bool _wasRobloxRunning = false;

        // Session Recorder fields
        private SessionRecorder _recorder = new();
        private long _lastPing = 0;

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
                _recorder.AddSample(cpu, appRamGB, _lastPing);
            }
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
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;

                // Auto-start recording
                _recorder.Start();
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                // Roblox session stopped
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = "Roblox: Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }

            _wasRobloxRunning = isRunning;
        }

        private async Task UpdatePingAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1000);
                PingText.Text = reply.Status == IPStatus.Success
                    ? $"Ping: {reply.RoundtripTime} ms"
                    : "Ping: Timeout";

                // Save last ping for recorder
                _lastPing = reply.Status == IPStatus.Success ? reply.RoundtripTime : 0;
            }
            catch
            {
                PingText.Text = "Ping: Error";
                _lastPing = 0;
            }
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
    }
}