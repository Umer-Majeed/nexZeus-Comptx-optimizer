using Hardcodet.Wpf.TaskbarNotification;
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
using System.Windows.Input;
using System.Windows.Threading;

namespace NexZeus
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _ramCounter;
        private readonly DispatcherTimer _timer;
        private bool _wasRobloxRunning;
        private int _tickCount;

        private float _lastCpu;
        private int _stutterCount;

        private readonly SessionRecorder _recorder = new();
        private long _lastPing;

        private readonly List<long> _recentPings = [];
        private int _pingAttempts;
        private int _pingFailures;
        private bool _isPingInProgress;

        private readonly TweakEngine _tweakEngine = new();
        private bool _isAutoApplying;

        private readonly ProcessManager _processManager = new();
        private bool _isDisposed;

        private List<CleanupTarget> _tempTargets = [];

        private readonly DnsOptimizer _dnsOptimizer = new();

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

                _cpuCounter.NextValue();
                _ramCounter.NextValue();
            }
            catch (Exception ex)
            {
                OptimizationText.Text = "Warning: Performance counters unavailable.";
                Debug.WriteLine(ex.Message);
            }

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += Timer_Tick;
            _timer.Tick += async (s, e) => await SafeUpdatePingAsync();
            _timer.Start();

            GpuText.Text = $"GPU: {GetGpuName()}";
            SetupTrayIcon();

            Loaded += async (s, e) =>
            {
                LoadTweaks();
                LoadStartupApps();
                AutoOptimizeCheckBox.IsChecked = AppSettings.AutoOptimizeOnGameStart;
                await RefreshProcessesInternal();
            };
        }

        #region Custom Window Control Handlers
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion

        #region Background Processes
        private async void RefreshProcesses_Click(object? sender, RoutedEventArgs? e)
        {
            await RefreshProcessesInternal();
        }

        private async Task RefreshProcessesInternal()
        {
            try
            {
                var groups = await _processManager.GetGroupedProcessesAsync();
                ProcessGroupList.ItemsSource = groups;
            }
            catch (Exception ex)
            {
                OptimizationText.Text = "Failed to refresh process list.";
                Debug.WriteLine(ex.Message);
            }
        }

        private async void RefreshGroups_Click(object sender, RoutedEventArgs e)
        {
            await RefreshProcessesInternal();
            OptimizationText.Text = "Background processes list refreshed.";
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

                OptimizationText.Text = $"Successfully applied actions to {modifiedCount} process group(s).";
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

        private void ProcessGroupHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.DataContext is ProcessGroupInfo group)
            {
                group.IsExpanded = !group.IsExpanded;
            }
        }
        #endregion

        #region Tweaks Engine
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

        private void ExecuteAutoOptimizations()
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
                        if (success) { tweak.IsEnabled = true; appliedCount++; }
                    }
                }

                TweaksList.ItemsSource = null;
                TweaksList.ItemsSource = tweaks;
                Task.Run(RefreshProcessesInternal);

                OptimizationText.Text = $"Auto-applied {appliedCount} tweaks for BloxStrike!";
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

        private void RevertAutoTweaks()
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
                        if (success) { tweak.IsEnabled = false; revertedCount++; }
                    }
                }

                TweaksList.ItemsSource = null;
                TweaksList.ItemsSource = tweaks;
                Task.Run(RefreshProcessesInternal);

                OptimizationText.Text = $"Reverted {revertedCount} auto-tweaks as game exited.";
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
        #endregion

        #region Startup Apps
        private void LoadStartupApps()
        {
            try
            {
                StartupAppsList.ItemsSource = StartupManager.GetStartupApps();
            }
            catch (Exception ex)
            {
                StartupResultText.Text = "Failed to load startup apps: " + ex.Message;
            }
        }

        private void StartupAppToggled(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox { Tag: StartupAppInfo app } checkBox)
            {
                bool success = checkBox.IsChecked == true
                    ? StartupManager.EnableStartupApp(app)
                    : StartupManager.DisableStartupApp(app);

                StartupResultText.Text = success
                    ? $"{app.Name} {(checkBox.IsChecked == true ? "enabled" : "disabled")}."
                    : $"Failed to update {app.Name}.";
            }
        }
        #endregion

        #region Temp Cleaner
        private void ScanTemp_Click(object sender, RoutedEventArgs e)
        {
            _tempTargets = TempCleaner.ScanTargets();
            TempTargetsList.ItemsSource = _tempTargets;

            long totalSize = 0;
            foreach (var t in _tempTargets) totalSize += t.SizeBytes;

            TempCleanResultText.Text = _tempTargets.Count > 0
                ? $"Found {TempCleaner.FormatSize(totalSize)} of junk files across {_tempTargets.Count} location(s)."
                : "No junk files found.";
        }

        private void CleanTemp_Click(object sender, RoutedEventArgs e)
        {
            if (_tempTargets.Count == 0)
            {
                TempCleanResultText.Text = "Scan first before cleaning.";
                return;
            }

            bool confirm = ThemedMessageBox.Show(this,
                "This will permanently delete temporary/cache files. Files currently in use will be safely skipped. Continue?",
                "Confirm Cleanup", ThemedMessageBoxIcon.Warning);

            if (!confirm) return;

            int totalDeleted = 0, totalFailed = 0;
            long totalFreed = 0;

            foreach (var target in _tempTargets)
            {
                var (deleted, freed, failed) = TempCleaner.CleanFolder(target.Path);
                totalDeleted += deleted;
                totalFreed += freed;
                totalFailed += failed;
            }

            TempCleanResultText.Text = $"Cleaned {totalDeleted} files, freed {TempCleaner.FormatSize(totalFreed)}." +
                (totalFailed > 0 ? $" ({totalFailed} files skipped — in use)" : "");

            ScanTemp_Click(sender, e);
        }
        #endregion

        #region RAM Optimizer
        private void TrimRam_Click(object sender, RoutedEventArgs e)
        {
            RamTrimResultText.Text = "Trimming memory...";

            var (trimmedCount, freedMB) = RamOptimizer.TrimStandbyMemory();

            RamTrimResultText.Text = trimmedCount > 0
                ? $"Trimmed {trimmedCount} process(es), reclaimed ~{freedMB} MB to standby."
                : "No processes were trimmed.";
        }
        #endregion

        #region DNS Optimizer
        private void BenchmarkDns_Click(object sender, RoutedEventArgs e)
        {
            DnsResultText.Text = "Testing DNS servers...";

            var results = _dnsOptimizer.BenchmarkServers();
            DnsResultsList.ItemsSource = results;

            var fastest = results.FirstOrDefault(r => r.LatencyMs != -1);
            DnsResultText.Text = fastest != null
                ? $"Fastest: {fastest.Name} ({fastest.LatencyMs} ms). Click 'Apply' next to it to switch."
                : "Could not reach any DNS servers. Check your internet connection.";
        }

        private void ApplyDns_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not DnsServerResult server)
                return;

            string? adapter = _dnsOptimizer.GetActiveAdapterName();
            if (adapter == null)
            {
                DnsResultText.Text = "Could not find an active network adapter.";
                return;
            }

            bool success = _dnsOptimizer.ApplyDns(adapter, server.PrimaryIp, server.SecondaryIp);
            DnsResultText.Text = success
                ? $"Switched to {server.Name} ({server.PrimaryIp}). Restart your browser/game for it to take effect."
                : "Failed to apply DNS. Try running NexZeus as Administrator.";
        }

        private void RevertDns_Click(object sender, RoutedEventArgs e)
        {
            string? adapter = _dnsOptimizer.GetActiveAdapterName();
            if (adapter == null)
            {
                DnsResultText.Text = "Could not find an active network adapter.";
                return;
            }

            bool success = _dnsOptimizer.RevertDns(adapter);
            DnsResultText.Text = success
                ? "Reverted to your original DNS settings."
                : "Failed to revert DNS. Try running NexZeus as Administrator.";
        }
        #endregion

        #region Tray Icon
        private void SetupTrayIcon()
        {
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
            openItem.Click += (s, e) => ShowFromTray();

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) => ExitApp();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(exitItem);

            MyTaskbarIcon.ContextMenu = contextMenu;
            MyTaskbarIcon.TrayMouseDoubleClick += (s, e) => ShowFromTray();
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
            if (AppSettings.AutoOptimizeOnGameStart) RevertAutoTweaks();

            MyTaskbarIcon?.Dispose();
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
        #endregion

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) Hide();
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
            float cpu = _cpuCounter?.NextValue() ?? 0f;
            CpuText.Text = $"{cpu:F1}%";

            if (_lastCpu > 0 && Math.Abs(cpu - _lastCpu) > 30)
            {
                _stutterCount++;
                StutterText.Text = _stutterCount.ToString();
            }
            _lastCpu = cpu;

            double appRamGB = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0 / 1024.0;
            RamText.Text = $"{appRamGB:F2} GB";

            _tickCount++;
            if (_tickCount % 5 == 0)
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

        private void CheckRobloxStatus()
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length == 0) processes = Process.GetProcessesByName("RobloxPlayerLauncher");
            if (processes.Length == 0) processes = Process.GetProcessesByName("Windows10Universal");

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
                }
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.Gray;

                if (AppSettings.AutoOptimizeOnGameStart) RevertAutoTweaks();
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Active" : "Roblox: Running";
                RobloxStatusText.Foreground = isBloxStrike ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.LimeGreen;
            }

            _wasRobloxRunning = isRunning;
        }

        private async Task SafeUpdatePingAsync()
        {
            if (_isPingInProgress) return;
            _isPingInProgress = true;

            try { await UpdatePingAsync(); }
            finally { _isPingInProgress = false; }
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

        private void StartButton_Click(object sender, RoutedEventArgs e) => OptimizationText.Text = "Diagnostics running...";

        private void StopSession_Click(object sender, RoutedEventArgs e)
        {
            _recorder.AnalyzeSession();
            _recorder.Stop();
            string? path = _recorder.SaveReport();
            OptimizationText.Text = path != null ? "Report saved successfully!" : "No data recorded.";
        }

        private void ViewHistory_Click(object sender, RoutedEventArgs e)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NexZeus", "Sessions");
            if (!Directory.Exists(folder))
            {
                OptimizationText.Text = "No sessions recorded yet.";
                return;
            }

            var files = Directory.GetFiles(folder, "*.csv").OrderByDescending(f => f).Take(5).Select(Path.GetFileName);
            OptimizationText.Text = files.Any() ? "Recent sessions:\n" + string.Join("\n", files) : "No sessions recorded yet.";
        }

        private void CheckOptimization_Click(object sender, RoutedEventArgs e)
        {
            var results = WindowsOptimizer.CheckSettings();
            OptimizationText.Text = string.Join("\n", results);
        }

        private void ApplyFixes_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = ThemedMessageBox.Show(this,
                "This will enable Windows Game Mode and switch to the High Performance power plan. Continue?",
                "Confirm Changes", ThemedMessageBoxIcon.Question);

            if (!confirm) return;

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