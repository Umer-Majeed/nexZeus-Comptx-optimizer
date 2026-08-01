using System;
using System.Diagnostics;
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

            // PerformanceCounter pehli call par 0.0 value deta hai, isliye pehle hi initial call kar rahe hain
            _cpuCounter.NextValue();
            _ramCounter.NextValue();

            // Timer set up (har 1 second mein update hoga)
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Real-time CPU Usage (%)
            float cpu = _cpuCounter.NextValue();
            CpuText.Text = $"CPU: {cpu:F1}%";

            // Real-time System RAM Usage (%)
            float ram = _ramCounter.NextValue();
            RamText.Text = $"RAM: {ram:F1}%";
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Diagnostics running...";
        }
    }
}