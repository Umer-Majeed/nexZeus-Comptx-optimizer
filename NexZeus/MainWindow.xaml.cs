using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramCounter;
        private readonly DispatcherTimer _timer;
        private bool _wasRobloxRunning;
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        // Stutter detection tracking
        private float _lastCpu;
        private int _stutterCount;

        // Session Recorder fields
        private readonly SessionRecorder _recorder = new();
        private long _lastPing;

        // Network Diagnostics tracking fields
        private readonly List<long> _recentPings = [];
        private int _pingAttempts;
        private int _pingFailures;

        // Tweak Engine instance
        private readonly TweakEngine _tweakEngine = new();
        private bool _isAutoApplying;

        public MainWindow()
        {
            InitializeComponent();

            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            _cpuCounter.NextValue();
            _ramCounter.NextValue();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Tick += async (s, e) => await UpdatePingAsync();
            _timer.Start();

            GpuText.Text = $"GPU: {GetGpuName()}";
            SetupTrayIcon();

            // Load interactive tweaks checklist and sync settings
            Loaded += (s, e) =>
            {
                LoadTweaks();
                AutoOptimizeCheckBox.IsChecked = AppSettings.AutoOptimizeOnGameStart;
            };
        }

        private void LoadTweaks()
        {
            try
            {
                var tweaks = _tweakEngine.GetAvailableTweaks();

                // Pre-check tweaks if their IDs are stored in saved AppSettings
                foreach (var t in tweaks)
                {
                    if (AppSettings.AutoApplyTweakIds.Contains(t.Id))
                    {
                        t.IsEnabled = true;
                    }
                }

                TweaksList.ItemsSource = tweaks;
            }
            catch (Exception ex)
            {
                OptimizationText.Text = "Failed to load tweaks: " + ex.Message;
            }
        }

        private void TweakToggled(object sender, RoutedEventArgs e)
        {
            if (_isAutoApplying) return;

            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is string tweakId)
            {
                var tweaks = TweaksList.ItemsSource as List<TweakDefinition>;
                var tweak = tweaks?.Find(t => t.Id == tweakId);

                if (tweak != null)
                {
                    bool success;
                    if (checkBox.IsChecked == true)
                    {
                        success = _tweakEngine.ApplyTweak(tweak);
                        OptimizationText.Text = success ? $"Applied: {tweak.Name}" : $"Failed to apply: {tweak.Name}";

                        if (success && !AppSettings.AutoApplyTweakIds.Contains(tweakId))
                        {
                            AppSettings.AutoApplyTweakIds.Add(tweakId);
                            // Trigger save via property assignment
                            AppSettings.AutoApplyTweakIds = AppSettings.AutoApplyTweakIds;
                        }
                    }
                    else
                    {
                        success = _tweakEngine.RevertTweak(tweak);
                        OptimizationText.Text = success ? $"Reverted: {tweak.Name}" : $"Failed to revert: {tweak.Name}";

                        if (AppSettings.AutoApplyTweakIds.Contains(tweakId))
                        {
                            AppSettings.AutoApplyTweakIds.Remove(tweakId);
                            AppSettings.AutoApplyTweakIds = AppSettings.AutoApplyTweakIds;
                        }
                    }
                }
            }
        }

        private void AutoOptimize_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoOptimizeCheckBox.IsChecked.HasValue)
            {
                AppSettings.AutoOptimizeOnGameStart = AutoOptimizeCheckBox.IsChecked.Value;
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "NexZeus"
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (s, e) => ShowFromTray());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApp());
            _trayIcon.ContextMenuStrip = contextMenu;

            _trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApp()
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            base.OnClosing(e);
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
            float cpu = _cpuCounter.NextValue();
            CpuText.Text = $"{cpu:F1}%";

            if (_lastCpu > 0 && Math.Abs(cpu - _lastCpu) > 30)
            {
                _stutterCount++;
                StutterText.Text = _stutterCount.ToString();
            }
            _lastCpu = cpu;

            double appRamGB = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0 / 1024.0;
            RamText.Text = $"{appRamGB:F2} GB";

            SysRamText.Text = GetSystemRamUsage();
            CheckRobloxStatus();

            if (_recorder.IsRecording)
            {
                _recorder.AddSample(cpu, appRamGB, _lastPing, _stutterCount);
            }

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

            if (isRunning && !string.IsNullOrWhiteSpace(AppSettings.BloxStrikePlaceId))
            {
                string? currentPlaceId = RobloxLogReader.GetCurrentPlaceId();
                isBloxStrike = currentPlaceId == AppSettings.BloxStrikePlaceId;
            }

            if (isRunning && !_wasRobloxRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Session Started" : "Roblox: Session Started";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                _recorder.Start();

                // Trigger Auto-Optimization if BloxStrike is detected and feature is enabled
                if (isBloxStrike && AppSettings.AutoOptimizeOnGameStart)
                {
                    ExecuteAutoOptimizations();
                }
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Active" : "Roblox: Running";
                RobloxStatusText.Foreground = isBloxStrike ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.LimeGreen;
            }

            _wasRobloxRunning = isRunning;
        }

        private void ExecuteAutoOptimizations()
        {
            try
            {
                var tweaks = TweaksList.ItemsSource as List<TweakDefinition>;
                if (tweaks == null) return;

                _isAutoApplying = true;
                int appliedCount = 0;

                foreach (var tweak in tweaks)
                {
                    if (AppSettings.AutoApplyTweakIds.Contains(tweak.Id))
                    {
                        bool success = _tweakEngine.ApplyTweak(tweak);
                        if (success)
                        {
                            tweak.IsEnabled = true;
                            appliedCount++;
                        }
                    }
                }

                // Refresh UI bindings safely
                TweaksList.ItemsSource = null;
                TweaksList.ItemsSource = tweaks;

                OptimizationText.Text = $"Auto-applied {appliedCount} profile optimizations for BloxStrike!";
            }
            catch (Exception ex)
            {
                OptimizationText.Text = "Auto-optimization error: " + ex.Message;
            }
            finally
            {
                _isAutoApplying = false;
            }
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
            var confirm = System.Windows.MessageBox.Show(
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