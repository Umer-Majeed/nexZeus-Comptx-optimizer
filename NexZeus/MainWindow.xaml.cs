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
        private OverlayWindow? _overlayWindow;
        private readonly FpsMonitor _fpsMonitor = new();
        private System.Windows.Controls.MenuItem? _overlayLockMenuItem;

        private float _lastCpu;
        private int _stutterCount;

        private readonly SessionRecorder _recorder = new();
        private long _lastPing;

        private readonly List<long> _recentPings = [];
        private int _pingAttempts;
        private int _pingFailures;
        private bool _isPingInProgress;

        private readonly TweakEngine _tweakEngine = new();
        private readonly DebloatEngine _debloatEngine = new();
        private bool _isAutoApplying;

        private readonly ProcessManager _processManager = new();
        private readonly PredictiveEcoService _predictiveEco = new();
        private readonly GameProfileService _gameProfileService = new();
        private bool _isDisposed;

        private List<CleanupTarget> _tempTargets = [];

        private readonly DnsOptimizer _dnsOptimizer = new();
        private readonly CloudProfileService _cloudProfileService = new();

        private List<MsiDeviceInfo> _msiDevices = [];
        private readonly Dictionary<string, int> _pendingCpuSelection = [];

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

            _gameProfileService.ProfileApplied += (game, plan) =>
                Dispatcher.Invoke(() => GameProfileStatusText.Text = $"{game}: switched to '{plan}'");
            _gameProfileService.ProfileReverted += game =>
                Dispatcher.Invoke(() => GameProfileStatusText.Text = $"{game} exited: power plan reverted");

            Loaded += async (s, e) =>
            {
                LoadTweaks();
                LoadDebloats();
                LoadMsiDevices();
                LoadStartupApps();
                LoadGameProfiles();
                AutoOptimizeCheckBox.IsChecked = AppSettings.AutoOptimizeOnGameStart;
                PredictiveEcoCheckBox.IsChecked = AppSettings.EnablePredictiveEcoMode;
                await RefreshProcessesInternal();

                // Fire-and-forget: neither of these should block the UI from
                // becoming responsive, and both fail silently/log on error.
                _ = UpdateChecker.CheckAndPromptAsync(this);
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

        private void LoadGameProfiles()
        {
            try
            {
                GameProfilesList.ItemsSource = null;
                GameProfilesList.ItemsSource = AppSettings.GameProfiles;

                var plans = PowerPlanManager.GetAvailablePlans();
                NewProfilePlanCombo.ItemsSource = plans;
                NewProfilePlanCombo.DisplayMemberPath = "Name";
                if (plans.Count > 0) NewProfilePlanCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                GameProfileStatusText.Text = "Failed to load profiles: " + ex.Message;
            }
        }

        private void AddGameProfile_Click(object sender, RoutedEventArgs e)
        {
            string processName = NewProfileProcessNameBox.Text.Trim();
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName = processName[..^4];

            if (string.IsNullOrWhiteSpace(processName) || NewProfilePlanCombo.SelectedItem is not PowerPlan plan)
            {
                GameProfileStatusText.Text = "Enter a process name and pick a power plan first.";
                return;
            }

            var profiles = AppSettings.GameProfiles;
            if (profiles.Any(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
            {
                GameProfileStatusText.Text = $"A profile for '{processName}' already exists.";
                return;
            }

            profiles.Add(new GameProfileData
            {
                Id = Guid.NewGuid().ToString("N"),
                ProcessName = processName,
                PowerPlanGuid = plan.Guid,
                PowerPlanName = plan.Name,
                Enabled = true
            });
            AppSettings.GameProfiles = profiles;

            NewProfileProcessNameBox.Text = string.Empty;
            GameProfileStatusText.Text = $"Added profile: {processName} → {plan.Name}";
            LoadGameProfiles();
        }

        private void RemoveGameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: string id })
            {
                var profiles = AppSettings.GameProfiles;
                profiles.RemoveAll(p => p.Id == id);
                AppSettings.GameProfiles = profiles;
                LoadGameProfiles();
            }
        }

        private void GameProfileEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox { Tag: string id } checkBox)
            {
                var profiles = AppSettings.GameProfiles;
                var profile = profiles.FirstOrDefault(p => p.Id == id);
                if (profile != null)
                {
                    profile.Enabled = checkBox.IsChecked == true;
                    AppSettings.GameProfiles = profiles;
                }
            }
        }

        private void LoadDebloats()
        {
            try
            {
                DebloatList.ItemsSource = _debloatEngine.GetAvailableDebloats();
            }
            catch (Exception ex)
            {
                DebloatStatusText.Text = "Failed to load debloat list: " + ex.Message;
            }
        }

        private void DebloatToggled(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox { Tag: string id } checkBox)
            {
                if (DebloatList.ItemsSource is List<DebloatDefinition> items)
                {
                    var item = items.Find(d => d.Id == id);
                    if (item != null)
                    {
                        bool success = checkBox.IsChecked == true
                            ? _debloatEngine.ApplyDebloat(item)
                            : _debloatEngine.RevertDebloat(item);

                        DebloatStatusText.Text = success
                            ? $"{(checkBox.IsChecked == true ? "Applied" : "Reverted")}: {item.Name}"
                            : $"Failed: {item.Name} (try running as Administrator)";
                    }
                }
            }
        }

        private void RevertAllDebloat_Click(object sender, RoutedEventArgs e)
        {
            int count = _debloatEngine.RevertAll();
            DebloatStatusText.Text = $"Reverted {count} privacy/debloat tweak(s).";
            LoadDebloats();
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

        #region Cloud Profiles
        private async void FindCloudProfiles_Click(object sender, RoutedEventArgs e)
        {
            await LoadCloudProfilesAsync();
        }

        private async Task LoadCloudProfilesAsync()
        {
            try
            {
                FindCloudProfilesButton.IsEnabled = false;
                CloudProfileStatusText.Text = "Searching for matching community profiles...";

                string cpu = CloudProfileService.GetCpuName();
                string gpu = GetGpuName();

                var matches = await _cloudProfileService.GetMatchingProfilesAsync(cpu, gpu);

                if (matches.Count > 0)
                {
                    CloudProfilesList.ItemsSource = matches;
                    CloudProfileStatusText.Text = $"Found {matches.Count} profile(s) for your exact hardware.";
                    return;
                }

                // No exact hardware match — fall back to the community's top-rated profiles.
                var top = await _cloudProfileService.GetTopProfilesAsync();
                CloudProfilesList.ItemsSource = top;
                CloudProfileStatusText.Text = top.Count > 0
                    ? $"No exact match for your hardware. Showing {top.Count} top-rated community profile(s) instead."
                    : "No cloud profiles available yet — be the first to share yours!";
            }
            catch (Exception ex)
            {
                CloudProfileStatusText.Text = "Couldn't reach the cloud profile service. Check your internet connection.";
                Logger.LogException(ex, "LoadCloudProfilesAsync");
            }
            finally
            {
                FindCloudProfilesButton.IsEnabled = true;
            }
        }

        private async void ApplyCloudProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: CloudProfile profile } button) return;

            bool confirm = ThemedMessageBox.Show(this,
                $"Apply this community profile?\n\n" +
                $"DNS: {profile.DnsPrimary ?? "unchanged"} / {profile.DnsSecondary ?? "unchanged"}\n" +
                $"Tweaks: {profile.Tweaks.Count} setting(s)\n\n" +
                "This will change your active DNS server and registry tweaks.",
                "Apply Cloud Profile",
                ThemedMessageBoxIcon.Question);

            if (!confirm) return;

            button.IsEnabled = false;
            int appliedCount = 0;
            int failedCount = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(profile.DnsPrimary))
                {
                    string? adapter = DnsOptimizer.GetActiveAdapterName();
                    string secondary = string.IsNullOrWhiteSpace(profile.DnsSecondary) ? profile.DnsPrimary : profile.DnsSecondary;

                    if (_dnsOptimizer.ApplyDns(adapter, profile.DnsPrimary, secondary))
                        appliedCount++;
                    else
                        failedCount++;
                }

                if (profile.Tweaks.Count > 0)
                {
                    var availableTweaks = _tweakEngine.GetAvailableTweaks();
                    foreach (var tweakId in profile.Tweaks)
                    {
                        var match = availableTweaks.Find(t => t.Id == tweakId);
                        if (match == null) continue;

                        if (_tweakEngine.ApplyTweak(match))
                        {
                            appliedCount++;
                            if (!AppSettings.AutoApplyTweakIds.Contains(tweakId))
                                AppSettings.AutoApplyTweakIds.Add(tweakId);
                        }
                        else
                        {
                            failedCount++;
                        }
                    }
                }

                CloudProfileStatusText.Text = failedCount == 0
                    ? $"Applied {appliedCount} setting(s) from {profile.SubmittedBy ?? "a community"} profile."
                    : $"Applied {appliedCount} setting(s), {failedCount} failed (try running as Administrator).";

                LoadTweaks(); // refresh checkbox states to reflect newly-applied tweaks
            }
            catch (Exception ex)
            {
                CloudProfileStatusText.Text = "Failed to apply cloud profile: " + ex.Message;
                Logger.LogException(ex, "ApplyCloudProfile_Click");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void UpvoteCloudProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: CloudProfile profile } button) return;
            if (string.IsNullOrEmpty(profile.Id)) return;

            button.IsEnabled = false;
            try
            {
                double newRating = ((profile.Rating * profile.Votes) + 5.0) / (profile.Votes + 1);
                bool ok = await _cloudProfileService.RateAsync(profile.Id, Math.Round(newRating, 2), profile.Votes + 1);

                CloudProfileStatusText.Text = ok
                    ? "Thanks for rating this profile!"
                    : "Rating failed — check your connection and try again.";

                if (ok) await LoadCloudProfilesAsync();
            }
            catch (Exception ex)
            {
                CloudProfileStatusText.Text = "Failed to submit rating: " + ex.Message;
                Logger.LogException(ex, "UpvoteCloudProfile_Click");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void ShareCloudProfile_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = ThemedMessageBox.Show(this,
                "This uploads your CPU, GPU, RAM, current DNS servers, and enabled tweak IDs to a public community list so others can benefit from your setup. No personal files or identifying info are sent.\n\nContinue?",
                "Share Setup Publicly",
                ThemedMessageBoxIcon.Question);

            if (!confirm) return;

            ShareCloudProfileButton.IsEnabled = false;
            CloudProfileStatusText.Text = "Submitting your setup...";

            try
            {
                var currentDns = DnsOptimizer.GetCurrentDns();

                var profile = new CloudProfile
                {
                    Cpu = CloudProfileService.GetCpuName(),
                    Gpu = GetGpuName(),
                    RamGb = CloudProfileService.GetRamGb(),
                    DnsPrimary = currentDns.Count > 0 ? currentDns[0] : null,
                    DnsSecondary = currentDns.Count > 1 ? currentDns[1] : null,
                    Tweaks = new List<string>(AppSettings.AutoApplyTweakIds),
                    FpsAvg = _fpsMonitor.CurrentFps > 0 ? _fpsMonitor.CurrentFps : 0,
                    PingAvg = _recentPings.Count > 0 ? _recentPings.Average() : 0,
                    Rating = 5,
                    Votes = 1,
                    SubmittedBy = Environment.UserName
                };

                bool ok = await _cloudProfileService.SubmitAsync(profile);
                CloudProfileStatusText.Text = ok
                    ? "Your setup was shared with the community. Thank you!"
                    : "Couldn't share your setup — check your internet connection and try again.";
            }
            catch (Exception ex)
            {
                CloudProfileStatusText.Text = "Failed to share setup: " + ex.Message;
                Logger.LogException(ex, "ShareCloudProfile_Click");
            }
            finally
            {
                ShareCloudProfileButton.IsEnabled = true;
            }
        }
        #endregion

        #region MSI Interrupt Optimizer
        private void LoadMsiDevices()
        {
            try
            {
                _msiDevices = MsiInterruptOptimizer.GetMsiCapableDevices();
                MsiDevicesList.ItemsSource = _msiDevices;
                MsiStatusText.Text = _msiDevices.Count > 0
                    ? $"{_msiDevices.Count} MSI-capable device(s) found."
                    : "No MSI-capable devices found (or app needs admin rights).";
            }
            catch (Exception ex)
            {
                MsiStatusText.Text = "Scan failed: " + ex.Message;
            }
        }

        private void RefreshMsiDevices_Click(object sender, RoutedEventArgs e)
        {
            LoadMsiDevices();
        }

        private void MsiToggled(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox { Tag: string instanceId } checkBox) return;

            var device = _msiDevices.Find(d => d.InstanceId == instanceId);
            if (device == null) return;

            bool enable = checkBox.IsChecked == true;
            bool success = MsiInterruptOptimizer.SetMsiEnabled(device, enable);

            if (success)
            {
                device.MsiEnabled = enable;
                MsiStatusText.Text = $"{(enable ? "Enabled" : "Disabled")} MSI mode for {device.FriendlyName}. Re-enable the device (or reboot) to fully apply.";
            }
            else
            {
                checkBox.IsChecked = !enable; // revert visual state, write failed
                MsiStatusText.Text = $"Failed to update MSI for {device.FriendlyName} — run NexZeus as Administrator.";
            }
        }

        private void CpuAffinityCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox { Tag: string instanceId } combo) return;

            List<string> items = ["Auto (No Pin)"];
            int cores = MsiInterruptOptimizer.GetLogicalCoreCount();
            for (int i = 0; i < cores; i++) items.Add($"Core {i}");
            combo.ItemsSource = items;

            var device = _msiDevices.Find(d => d.InstanceId == instanceId);
            combo.SelectedIndex = device is { AssignedCpu: >= 0 } ? device.AssignedCpu + 1 : 0;
        }

        private void CpuAffinityCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox { Tag: string instanceId } combo) return;
            if (combo.SelectedIndex < 0) return;

            _pendingCpuSelection[instanceId] = combo.SelectedIndex - 1; // index 0 = "Auto" => -1
        }

        private void PinAffinity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: string instanceId }) return;

            var device = _msiDevices.Find(d => d.InstanceId == instanceId);
            if (device == null) return;

            int cpuIndex = _pendingCpuSelection.TryGetValue(instanceId, out var selected) ? selected : device.AssignedCpu;
            bool success = MsiInterruptOptimizer.SetInterruptAffinity(device, cpuIndex);

            if (success)
            {
                device.AssignedCpu = cpuIndex;
                MsiStatusText.Text = cpuIndex < 0
                    ? $"Cleared CPU pin for {device.FriendlyName}."
                    : $"Pinned {device.FriendlyName} interrupts to Core {cpuIndex}. Reboot recommended.";
            }
            else
            {
                MsiStatusText.Text = $"Failed to pin {device.FriendlyName} — run NexZeus as Administrator.";
            }
        }
        #endregion

        #region Network & TCP Optimizer (One-Click)
        private static readonly string[] NetworkTweakIds =
        [
            "disable_network_throttling",
            "system_responsiveness",
            "tcp_ack_delay",
            "games_task_priority"
        ];

        private void OptimizeNetwork_Click(object sender, RoutedEventArgs e)
        {
            var allTweaks = _tweakEngine.GetAvailableTweaks();
            int applied = 0, failed = 0;

            foreach (var id in NetworkTweakIds)
            {
                var tweak = allTweaks.Find(t => t.Id == id);
                if (tweak == null) continue;

                if (_tweakEngine.ApplyTweak(tweak)) applied++;
                else failed++;

                if (!AppSettings.AutoApplyTweakIds.Contains(id))
                    AppSettings.AutoApplyTweakIds.Add(id);
            }

            NetworkOptStatusText.Text = failed == 0
                ? $"✔ Applied {applied}/{NetworkTweakIds.Length} network tweaks. Restart may be needed for full effect."
                : $"Applied {applied}, failed {failed} — run NexZeus as Administrator and retry.";

            LoadTweaks(); // refresh checkboxes in the Active Game Tweaks list to reflect new state
        }

        private void RevertNetwork_Click(object sender, RoutedEventArgs e)
        {
            var allTweaks = _tweakEngine.GetAvailableTweaks();
            int reverted = 0;

            foreach (var id in NetworkTweakIds)
            {
                var tweak = allTweaks.Find(t => t.Id == id);
                if (tweak == null) continue;

                if (_tweakEngine.RevertTweak(tweak)) reverted++;
                AppSettings.AutoApplyTweakIds.Remove(id);
            }

            NetworkOptStatusText.Text = $"Reverted {reverted}/{NetworkTweakIds.Length} network tweaks to Windows defaults.";
            LoadTweaks();
        }
        #endregion

        private void AutoOptimize_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoOptimizeCheckBox.IsChecked.HasValue)
            {
                AppSettings.AutoOptimizeOnGameStart = AutoOptimizeCheckBox.IsChecked.Value;
            }
        }

        private void PredictiveEco_Changed(object sender, RoutedEventArgs e)
        {
            if (PredictiveEcoCheckBox.IsChecked.HasValue)
            {
                AppSettings.EnablePredictiveEcoMode = PredictiveEcoCheckBox.IsChecked.Value;
                if (!AppSettings.EnablePredictiveEcoMode) _predictiveEco.ForceRestore();
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

            string? adapter = DnsOptimizer.GetActiveAdapterName();
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
            string? adapter = DnsOptimizer.GetActiveAdapterName();
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
            var contextMenu = new System.Windows.Controls.ContextMenu
            {
                Style = (Style)FindResource("TrayContextMenu")
            };

            var menuItemStyle = (Style)FindResource("TrayMenuItem");

            var openItem = new System.Windows.Controls.MenuItem { Header = "Open", Style = menuItemStyle };
            openItem.Click += (s, e) => ShowFromTray();

            _overlayLockMenuItem = new System.Windows.Controls.MenuItem { Header = "Unlock Overlay (Drag)", Style = menuItemStyle };
            _overlayLockMenuItem.Click += (s, e) =>
            {
                if (_overlayWindow == null) return;
                bool newLockState = !_overlayWindow.IsLocked;
                _overlayWindow.SetLocked(newLockState);
                _overlayLockMenuItem.Header = newLockState ? "Lock Overlay Position" : "Unlock Overlay (Drag)";
            };

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit", Style = menuItemStyle };
            exitItem.Click += (s, e) => ExitApp();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(_overlayLockMenuItem);
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
            _predictiveEco.ForceRestore();
            _gameProfileService.ForceRevert();

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
            _predictiveEco.Tick();
            _gameProfileService.Tick();

            if (_recorder.IsRecording)
            {
                _recorder.AddSample(cpu, appRamGB, _lastPing, _stutterCount);
            }

            CheckThresholds(cpu, _lastPing);
        }

        private void CheckThresholds(float cpu, long ping)
        {
            List<string> warnings = [];

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

                if (_overlayWindow == null || !_overlayWindow.IsLoaded)
                    _overlayWindow = new OverlayWindow();
                _overlayWindow.Show();

                // Tracks the actual Roblox client process presenting frames — correct FPS regardless of which place is loaded.
                _fpsMonitor.Start(processes[0].ProcessName + ".exe");

                if (isBloxStrike && AppSettings.AutoOptimizeOnGameStart)
                {
                    ExecuteAutoOptimizations();
                }
            }
            else if (!isRunning && _wasRobloxRunning)
            {
                RobloxStatusText.Text = "Roblox: Not Running";
                RobloxStatusText.Foreground = System.Windows.Media.Brushes.Gray;

                _overlayWindow?.Close();
                _overlayWindow = null;
                _fpsMonitor.Stop();

                if (AppSettings.AutoOptimizeOnGameStart) RevertAutoTweaks();
            }
            else if (isRunning)
            {
                RobloxStatusText.Text = isBloxStrike ? "BloxStrike: Active" : "Roblox: Running";
                RobloxStatusText.Foreground = isBloxStrike ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.LimeGreen;
            }

            // Push live stats into the overlay every tick, while it's open — correct per-game because
            // FpsMonitor is bound to the real Roblox process and the label follows isBloxStrike above.
            if (_overlayWindow != null && isRunning)
            {
                string label = isBloxStrike ? "BloxStrike" : "Roblox";
                _overlayWindow.UpdateStats(
                    label,
                    _fpsMonitor.CurrentFps,
                    _fpsMonitor.LastFrameTimeMs,
                    _lastPing,
                    _fpsMonitor.IsStuttering,
                    _fpsMonitor.GetFrameTimeHistory());
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
            string target = RobloxLogReader.GetCurrentServerIp() ?? "8.8.8.8";
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 1000);

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