using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NexZeus
{
    public class TweakDefinition
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string RegistryHive { get; set; }
        public required string RegistryPath { get; set; }
        public required string ValueName { get; set; }
        public object? OnValue { get; set; }
        public object? OffValue { get; set; }
        public RegistryValueKind ValueKind { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class TweakBackupEntry
    {
        public required string Id { get; set; }
        public object? OriginalValue { get; set; }
        public bool ValueExisted { get; set; }
    }

    public class TweakEngine
    {
        private readonly string _backupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "tweak_backup.json");

        public List<TweakDefinition> GetAvailableTweaks()
        {
            var tweaks = new List<TweakDefinition>
            {
                new TweakDefinition
                {
                    Id = "disable_nagle",
                    Name = "Disable Nagle's Algorithm",
                    Description = "Reduces network latency by sending packets immediately instead of buffering them.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                    ValueName = "TcpAckFrequency",
                    OnValue = 1,
                    OffValue = null,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "disable_animations",
                    Name = "Disable Windows Animations",
                    Description = "Reduces UI overhead by turning off window/taskbar animations, freeing up minor CPU/GPU resources.",
                    RegistryHive = "CurrentUser",
                    RegistryPath = @"Control Panel\Desktop\WindowMetrics",
                    ValueName = "MinAnimate",
                    OnValue = "0",
                    OffValue = "1",
                    ValueKind = RegistryValueKind.String
                },
                new TweakDefinition
                {
                    Id = "disable_transparency",
                    Name = "Disable Transparency Effects",
                    Description = "Turns off Windows transparency/blur effects to reduce GPU compositing load.",
                    RegistryHive = "CurrentUser",
                    RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    ValueName = "EnableTransparency",
                    OnValue = 0,
                    OffValue = 1,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "disable_network_throttling",
                    Name = "Disable Network Throttling Index",
                    Description = "Removes Windows' multimedia network throttling limit, which can cap bandwidth for background network activity during gameplay.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    ValueName = "NetworkThrottlingIndex",
                    OnValue = 0xFFFFFFFF,
                    OffValue = 10,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "disable_tcp_timestamps",
                    Name = "Disable TCP Timestamps",
                    Description = "Reduces per-packet overhead slightly by removing timestamp data from TCP packets.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                    ValueName = "Tcp1323Opts",
                    OnValue = 0,
                    OffValue = 3,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "gpu_priority_boost",
                    Name = "Boost GPU Priority for Games",
                    Description = "Tells Windows to give game processes higher GPU scheduling priority over background apps.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    ValueName = "GPU Priority",
                    OnValue = 8,
                    OffValue = 2,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "cpu_priority_boost",
                    Name = "Boost CPU Priority for Games",
                    Description = "Increases CPU scheduling priority given to foreground game processes.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    ValueName = "Priority",
                    OnValue = 6,
                    OffValue = 2,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "system_responsiveness",
                    Name = "Lower SystemResponsiveness Reservation",
                    Description = "Reduces the % of CPU Windows reserves for background/multimedia tasks (MMCSS), giving games more headroom. 0 = max game priority.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    ValueName = "SystemResponsiveness",
                    OnValue = 0,
                    OffValue = 20,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "tcp_ack_delay",
                    Name = "Disable TCP Delayed ACK",
                    Description = "Forces Windows to send TCP ACKs immediately instead of batching them for ~200ms, lowering effective ping/jitter in online games.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                    ValueName = "TcpDelAckTicks",
                    OnValue = 0,
                    OffValue = null,
                    ValueKind = RegistryValueKind.DWord
                },
                new TweakDefinition
                {
                    Id = "games_task_priority",
                    Name = "Max Out 'Games' MMCSS Task Priority",
                    Description = "Sets the Games multimedia task class to the highest scheduling category (High) so foreground games win CPU contention against background services.",
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    ValueName = "Scheduling Category",
                    OnValue = "High",
                    OffValue = "Medium",
                    ValueKind = RegistryValueKind.String
                },
                new TweakDefinition
                {
                    Id = "disable_fullscreen_optimizations",
                    Name = "Disable Fullscreen Optimizations (System-wide)",
                    Description = "Forces true exclusive fullscreen behavior system-wide, which can reduce input latency in some games.",
                    RegistryHive = "CurrentUser",
                    RegistryPath = @"System\GameConfigStore",
                    ValueName = "GameDVR_FSEBehaviorMode",
                    OnValue = 2,
                    OffValue = 0,
                    ValueKind = RegistryValueKind.DWord
                }
            };

            var backups = LoadBackups();
            foreach (var tweak in tweaks)
            {
                tweak.IsEnabled = backups.ContainsKey(tweak.Id);
            }

            return tweaks;
        }

        public bool ApplyTweak(TweakDefinition tweak)
        {
            try
            {
                var baseKey = tweak.RegistryHive == "LocalMachine" ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = baseKey.OpenSubKey(tweak.RegistryPath, writable: true);
                if (key == null) return false;

                BackupOriginalValue(key, tweak);

                if (tweak.OnValue == null)
                    key.DeleteValue(tweak.ValueName, false);
                else
                    key.SetValue(tweak.ValueName, tweak.OnValue, tweak.ValueKind);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RevertTweak(TweakDefinition tweak)
        {
            try
            {
                var backups = LoadBackups();
                if (!backups.TryGetValue(tweak.Id, out var entry)) return false;

                var baseKey = tweak.RegistryHive == "LocalMachine" ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = baseKey.OpenSubKey(tweak.RegistryPath, writable: true);
                if (key == null) return false;

                if (!entry.ValueExisted)
                    key.DeleteValue(tweak.ValueName, false);
                else
                    key.SetValue(tweak.ValueName, entry.OriginalValue ?? string.Empty, tweak.ValueKind);

                backups.Remove(tweak.Id);
                SaveBackups(backups);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BackupOriginalValue(RegistryKey key, TweakDefinition tweak)
        {
            var backups = LoadBackups();
            if (backups.ContainsKey(tweak.Id)) return;

            var existing = key.GetValue(tweak.ValueName);
            backups[tweak.Id] = new TweakBackupEntry
            {
                Id = tweak.Id,
                OriginalValue = existing,
                ValueExisted = existing != null
            };
            SaveBackups(backups);
        }

        private Dictionary<string, TweakBackupEntry> LoadBackups()
        {
            try
            {
                if (!File.Exists(_backupPath)) return [];
                string json = File.ReadAllText(_backupPath);
                return JsonSerializer.Deserialize<Dictionary<string, TweakBackupEntry>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void SaveBackups(Dictionary<string, TweakBackupEntry> backups)
        {
            try
            {
                string? folder = Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(_backupPath, JsonSerializer.Serialize(backups));
            }
            catch { }
        }
    }
}