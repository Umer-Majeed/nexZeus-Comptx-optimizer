using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly PerformanceCounter _cpuCounter = new("Processor", "% Processor Time", "_Total");
        private readonly PerformanceCounter _ramCounter = new("Memory", "% Committed Bytes In Use");
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
        private bool _isPingInProgress;

        // Tweak Engine instance
        private readonly TweakEngine _tweakEngine = new();
        private bool _isAutoApplying;

        // Process Manager instance
        private readonly ProcessManager _processManager = new();

        // Auto-suspended List
        private readonly List<int> _autoSuspendedPids = [];

        public MainWindow()
        {
            InitializeComponent();

            _cpuCounter.NextValue();
            _ramCounter.NextValue();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Tick += async (s, e) => await SafeUpdatePingAsync();
            _timer.Start();

            GpuText.Text = $"GPU: {GetGpuName()}";
            SetupTrayIcon();

            Loaded += async (s, e) =>
            {
                LoadTweaks();
                AutoOptimizeCheckBox.IsChecked = AppSettings.AutoOptimizeOnGameStart;
                await RefreshProcessesInternal();
            };
        }

        private async void RefreshProcesses_Click(object? sender, RoutedEventArgs? e)
        {
            await RefreshProcessesInternal();
        }

        private async Task RefreshProcessesInternal()
        {
            var groups = await _processManager.GetGroupedProcessesAsync();
            ProcessGroupList.ItemsSource = groups;
        }

        private async void RefreshGroups_Click(object sender, RoutedEventArgs e)
        {
            await RefreshProcessesInternal();
            ProcessActionResultText.Text = "Background processes list refreshed.";
        }

        private void SelectGroupAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ProcessGroupInfo group && btn.CommandParameter is string action)
            {
                group.SelectedAction = action;
            }
        }

        private async void ApplyGroupActions_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessGroupList.ItemsSource is IEnumerable<ProcessGroupInfo> groups)
            {
                int modifiedCount = 0;
                foreach (var group in groups)
                {
                    if (group.SelectedAction == "Suspend")
                    {
                        await _processManager.SuspendGroupAsync(group);
                        modifiedCount++;
                    }
                    else if (group.SelectedAction == "Resume")
                    {
                        await _processManager.ResumeGroupAsync(group);
                        modifiedCount++;
                    }
                }

                ProcessActionResultText.Text = $"Applied actions to {modifiedCount} process group(s).";
                await RefreshProcessesInternal();
            }
        }

        private void ExcludeToggled(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox { Tag: string processName } checkBox)
            {
                bool isExcluded = checkBox.IsChecked ?? false;
                if (isExcluded)
                {
                    if (!AppSettings.ExcludedProcessNames.Contains(processName))
                        AppSettings.ExcludedProcessNames.Add(processName);
                }
                else
                {
                    AppSettings.ExcludedProcessNames.Remove(processName);
                }
            }
        }

        private void LoadTweaks()
        {
            try
            {
                var tweaks = _tweakEngine.GetAvailableTweaks();

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

            if (sender is System.Windows.Controls.CheckBox { Tag: string tweakId } checkBox)
            {
                if (TweaksList.ItemsSource is List<TweakDefinition> tweaks)
                {
                    var tweak = tweaks.Find(t => t.Id == tweakId);

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
                            }
                        }
                        else
                        {
                            success = _tweakEngine.RevertTweak(tweak);
                            OptimizationText.Text = success ? $"Reverted: {tweak.Name}" : $"Failed to revert: {tweak.Name}";

                            AppSettings.AutoApplyTweakIds.Remove(tweakId);
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
            CleanupResources();
            System.Windows.Application.Current.Shutdown();
        }

        private void CleanupResources()
        {
            _timer.Stop();

            if (AppSettings.AutoOptimizeOnGameStart)
            {
                RevertAutoTweaks();
            }

            if (AppSettings.AutoSuspendBackgroundApps)
            {
                foreach (var pid in _autoSuspendedPids)
                {
                    _ = _processManager.ResumeProcessAsync(pid);
                }
                _autoSuspendedPids.Clear();
            }

            _trayIcon?.Dispose();
            Dispose();
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            GC.SuppressFinalize(this);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
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

            WarningText.Text = warnings.Count > 0 ? string.Join("  |  ", warnings) : "System Status: Normal";
        }

        private async void CheckRobloxStatus()
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

                if (isBloxStrike && AppSettings.AutoOptimizeOnGameStart)
                {
                    ExecuteAutoOptimizations();

                    if (AppSettings.AutoSuspendBackgroundApps)
                    {
                        var procs = await _processManager.GetBackgroundProcessesAsync();
                        foreach (var p in procs)
                        {
                            if (await _processManager.SuspendProcessAsync(p.Pid))
                            {
                                _autoSuspendedPids.Add(p.Pid);
                            }
                        }
                    }
                }
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.Gray;

                if (AppSettings.AutoOptimizeOnGameStart)
                {
                    RevertAutoTweaks();
                }

                if (AppSettings.AutoSuspendBackgroundApps)
                {
                    foreach (var pid in _autoSuspendedPids)
                    {
                        await _processManager.ResumeProcessAsync(pid);
                    }
                    _autoSuspendedPids.Clear();
                }
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Active" : "Roblox: Running";
                RobloxStatusText.Foreground = isBloxStrike ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.LimeGreen;
            }

            _wasRobloxRunning = isRunning;
        }

        private async void ExecuteAutoOptimizations()
        {
            try
            {
                if (TweaksList.ItemsSource is not List<TweakDefinition> tweaks) return;

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

                _autoSuspendedPids.Clear();

                if (AppSettings.AutoSuspendBackgroundApps)
                {
                    var topGroups = (await _processManager.GetGroupedProcessesAsync()).Take(3);
                    foreach (var group in topGroups)
                    {
                        foreach (var p in group.Instances)
                        {
                            if (await _processManager.SuspendProcessAsync(p.Pid))
                            {
                                _autoSuspendedPids.Add(p.Pid);
                            }
                        }
                    }
                }

                TweaksList.ItemsSource = null;
                TweaksList.ItemsSource = tweaks;
                await RefreshProcessesInternal();

                OptimizationText.Text = $"Auto-applied {appliedCount} tweaks & suspended {_autoSuspendedPids.Count} background apps for BloxStrike!";
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

        private async void RevertAutoTweaks()
        {
            try
            {
                if (TweaksList.ItemsSource is not List<TweakDefinition> tweaks) return;

                _isAutoApplying = true;
                int revertedCount = 0;

                foreach (var id in AppSettings.AutoApplyTweakIds)
                {
                    var tweak = tweaks.Find(t => t.Id == id);
                    if (tweak != null)
                    {
                        bool success = _tweakEngine.RevertTweak(tweak);
                        if (success)
                        {
                            tweak.IsEnabled = false;
                            revertedCount++;
                        }
                    }
                }

                foreach (var pid in _autoSuspendedPids)
                {
                    await _processManager.ResumeProcessAsync(pid);
                }
                _autoSuspendedPids.Clear();

                TweaksList.ItemsSource = null;
                TweaksList.ItemsSource = tweaks;
                await RefreshProcessesInternal();

                OptimizationText.Text = $"Reverted {revertedCount} auto-tweaks & resumed background apps as game exited.";
            }
            catch (Exception ex)
            {
                OptimizationText.Text = "Auto-revert error: " + ex.Message;
            }
            finally
            {
                _isAutoApplying = false;
            }
        }

        private async Task SafeUpdatePingAsync()
        {
            if (_isPingInProgress) return;
            _isPingInProgress = true;

            try
            {
                await UpdatePingAsync();
            }
            finally
            {
                _isPingInProgress = false;
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