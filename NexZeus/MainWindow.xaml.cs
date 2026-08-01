using System;
using System.Diagnostics;
using System.Management;
using System.Windows;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private DispatcherTimer _timer;

        public MainWindow()
        {
            InitializeComponent();

            // CPU aur RAM counters initialize kar rahe hain
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            _cpuCounter.NextValue();
            _ramCounter.NextValue();

            // Timer set up (har 1 second mein update hoga)
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
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
    }
}