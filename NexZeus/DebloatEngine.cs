using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NexZeus
{
    public enum DebloatActionType { Registry, Service, ScheduledTask }

    public class DebloatDefinition
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Category { get; set; } // Telemetry, Cortana, Xbox, BackgroundTasks
        public DebloatActionType ActionType { get; set; }

        // Registry
        public string? RegistryHive { get; set; }
        public string? RegistryPath { get; set; }
        public string? ValueName { get; set; }
        public object? OnValue { get; set; }
        public RegistryValueKind ValueKind { get; set; }

        // Service / ScheduledTask
        public string? TargetName { get; set; }

        public bool IsEnabled { get; set; } // "enabled" = debloat is currently applied
    }

    public class DebloatBackupEntry
    {
        public required string Id { get; set; }
        public DebloatActionType ActionType { get; set; }
        public object? OriginalRegistryValue { get; set; }
        public bool RegistryValueExisted { get; set; }
        public string? OriginalServiceStartType { get; set; } // e.g. "auto", "demand"
    }

    /// <summary>
    /// Applies well-known, reversible privacy/debloat tweaks: disables Windows diagnostic
    /// telemetry, Cortana, Xbox Live background services, and scheduled background tasks
    /// that otherwise contribute to idle CPU/disk usage. Every change is backed up before
    /// being applied so it can be fully reverted via RevertDebloat/RevertAll.
    /// </summary>
    public class DebloatEngine
    {
        private readonly string _backupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "debloat_backup.json");

        public List<DebloatDefinition> GetAvailableDebloats()
        {
            var items = new List<DebloatDefinition>
            {
                // ---- Telemetry ----
                new DebloatDefinition
                {
                    Id = "telemetry_level", Name = "Set Telemetry to Minimum",
                    Description = "Sets Windows diagnostic data collection to the lowest allowed level via policy.",
                    Category = "Telemetry", ActionType = DebloatActionType.Registry,
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    ValueName = "AllowTelemetry", OnValue = 0, ValueKind = RegistryValueKind.DWord
                },
                new DebloatDefinition
                {
                    Id = "diagtrack_service", Name = "Disable Connected User Experiences and Telemetry",
                    Description = "Stops and disables the DiagTrack service, the main channel Windows uses to upload telemetry.",
                    Category = "Telemetry", ActionType = DebloatActionType.Service, TargetName = "DiagTrack"
                },
                new DebloatDefinition
                {
                    Id = "dmwappush_service", Name = "Disable WAP Push Message Routing",
                    Description = "Disables the dmwappushservice, used for some diagnostic/push data routing.",
                    Category = "Telemetry", ActionType = DebloatActionType.Service, TargetName = "dmwappushservice"
                },
                new DebloatDefinition
                {
                    Id = "task_compat_appraiser", Name = "Disable Compatibility Appraiser Task",
                    Description = "Stops the background task that scans installed apps for telemetry/compatibility reporting.",
                    Category = "Telemetry", ActionType = DebloatActionType.ScheduledTask,
                    TargetName = @"Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"
                },
                new DebloatDefinition
                {
                    Id = "task_ceip_consolidator", Name = "Disable Customer Experience Improvement Task",
                    Description = "Stops the CEIP data consolidator scheduled task.",
                    Category = "Telemetry", ActionType = DebloatActionType.ScheduledTask,
                    TargetName = @"Microsoft\Windows\Customer Experience Improvement Program\Consolidator"
                },

                // ---- Cortana ----
                new DebloatDefinition
                {
                    Id = "cortana_policy", Name = "Disable Cortana (Policy)",
                    Description = "Disables Cortana system-wide via the Windows Search policy key.",
                    Category = "Cortana", ActionType = DebloatActionType.Registry,
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                    ValueName = "AllowCortana", OnValue = 0, ValueKind = RegistryValueKind.DWord
                },
                new DebloatDefinition
                {
                    Id = "cortana_consent", Name = "Revoke Cortana Search Consent",
                    Description = "Clears Cortana's cloud-search consent and web result integration for the current user.",
                    Category = "Cortana", ActionType = DebloatActionType.Registry,
                    RegistryHive = "CurrentUser",
                    RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search",
                    ValueName = "CortanaConsent", OnValue = 0, ValueKind = RegistryValueKind.DWord
                },

                // ---- Xbox Live ----
                new DebloatDefinition
                {
                    Id = "xbl_auth_service", Name = "Disable Xbox Live Auth Manager",
                    Description = "Disables the XblAuthManager service used for Xbox Live sign-in.",
                    Category = "Xbox", ActionType = DebloatActionType.Service, TargetName = "XblAuthManager"
                },
                new DebloatDefinition
                {
                    Id = "xbl_gamesave_service", Name = "Disable Xbox Live Game Save",
                    Description = "Disables the XblGameSave service used for Xbox cloud save sync.",
                    Category = "Xbox", ActionType = DebloatActionType.Service, TargetName = "XblGameSave"
                },
                new DebloatDefinition
                {
                    Id = "xbox_netapi_service", Name = "Disable Xbox Live Networking Service",
                    Description = "Disables XboxNetApiSvc, used for Xbox Live networking/matchmaking support.",
                    Category = "Xbox", ActionType = DebloatActionType.Service, TargetName = "XboxNetApiSvc"
                },
                new DebloatDefinition
                {
                    Id = "xbox_gip_service", Name = "Disable Xbox Accessory Management",
                    Description = "Disables XboxGipSvc, which manages Xbox controllers/accessories in the background.",
                    Category = "Xbox", ActionType = DebloatActionType.Service, TargetName = "XboxGipSvc"
                },
                new DebloatDefinition
                {
                    Id = "gamedvr_policy", Name = "Disable Game DVR / Xbox Game Bar Capture",
                    Description = "Turns off background Game DVR recording, which otherwise polls foreground games continuously.",
                    Category = "Xbox", ActionType = DebloatActionType.Registry,
                    RegistryHive = "CurrentUser",
                    RegistryPath = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled", OnValue = 0, ValueKind = RegistryValueKind.DWord
                },

                // ---- Background Tasks ----
                new DebloatDefinition
                {
                    Id = "apps_run_background", Name = "Block UWP Apps Running in Background",
                    Description = "Forces Store/UWP apps to stop running in the background system-wide, cutting idle CPU/battery use.",
                    Category = "BackgroundTasks", ActionType = DebloatActionType.Registry,
                    RegistryHive = "LocalMachine",
                    RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                    ValueName = "LetAppsRunInBackground", OnValue = 2, ValueKind = RegistryValueKind.DWord
                },
                new DebloatDefinition
                {
                    Id = "sysmain_service", Name = "Disable SysMain (Superfetch)",
                    Description = "Disables SysMain, which precaches apps into RAM in the background and can cause idle disk/CPU activity, especially on SSDs.",
                    Category = "BackgroundTasks", ActionType = DebloatActionType.Service, TargetName = "SysMain"
                }
            };

            var backups = LoadBackups();
            foreach (var item in items)
                item.IsEnabled = backups.ContainsKey(item.Id);

            return items;
        }

        public bool ApplyDebloat(DebloatDefinition def)
        {
            try
            {
                return def.ActionType switch
                {
                    DebloatActionType.Registry => ApplyRegistry(def),
                    DebloatActionType.Service => ApplyService(def),
                    DebloatActionType.ScheduledTask => ApplyScheduledTask(def),
                    _ => false
                };
            }
            catch { return false; }
        }

        public bool RevertDebloat(DebloatDefinition def)
        {
            try
            {
                var backups = LoadBackups();
                if (!backups.TryGetValue(def.Id, out var entry)) return false;

                bool ok = def.ActionType switch
                {
                    DebloatActionType.Registry => RevertRegistry(def, entry),
                    DebloatActionType.Service => RevertService(def, entry),
                    DebloatActionType.ScheduledTask => RevertScheduledTask(def),
                    _ => false
                };

                if (ok)
                {
                    backups.Remove(def.Id);
                    SaveBackups(backups);
                }
                return ok;
            }
            catch { return false; }
        }

        public (int applied, int failed) ApplyCategory(string category)
        {
            int applied = 0, failed = 0;
            foreach (var def in GetAvailableDebloats())
            {
                if (!def.Category.Equals(category, StringComparison.OrdinalIgnoreCase) || def.IsEnabled) continue;
                if (ApplyDebloat(def)) applied++; else failed++;
            }
            return (applied, failed);
        }

        public int RevertAll()
        {
            int count = 0;
            foreach (var def in GetAvailableDebloats())
            {
                if (def.IsEnabled && RevertDebloat(def)) count++;
            }
            return count;
        }

        // ---------------- Registry ----------------

        private bool ApplyRegistry(DebloatDefinition def)
        {
            var baseKey = def.RegistryHive == "LocalMachine" ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = baseKey.CreateSubKey(def.RegistryPath!, writable: true);
            if (key == null) return false;

            var existing = key.GetValue(def.ValueName);
            SaveBackupEntry(new DebloatBackupEntry
            {
                Id = def.Id,
                ActionType = DebloatActionType.Registry,
                OriginalRegistryValue = existing,
                RegistryValueExisted = existing != null
            });

            key.SetValue(def.ValueName!, def.OnValue!, def.ValueKind);
            return true;
        }

        private bool RevertRegistry(DebloatDefinition def, DebloatBackupEntry entry)
        {
            var baseKey = def.RegistryHive == "LocalMachine" ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = baseKey.OpenSubKey(def.RegistryPath!, writable: true);
            if (key == null) return true; // nothing to revert

            if (!entry.RegistryValueExisted)
                key.DeleteValue(def.ValueName!, false);
            else
                key.SetValue(def.ValueName!, entry.OriginalRegistryValue!, def.ValueKind);

            return true;
        }

        // ---------------- Services ----------------

        private bool ApplyService(DebloatDefinition def)
        {
            string? originalType = QueryServiceStartType(def.TargetName!);

            SaveBackupEntry(new DebloatBackupEntry
            {
                Id = def.Id,
                ActionType = DebloatActionType.Service,
                OriginalServiceStartType = originalType ?? "demand"
            });

            RunProcess("sc.exe", $"config \"{def.TargetName}\" start= disabled");
            RunProcess("sc.exe", $"stop \"{def.TargetName}\"");
            return true;
        }

        private bool RevertService(DebloatDefinition def, DebloatBackupEntry entry)
        {
            string startType = entry.OriginalServiceStartType ?? "demand";
            RunProcess("sc.exe", $"config \"{def.TargetName}\" start= {startType}");
            if (startType is "auto" or "demand")
                RunProcess("sc.exe", $"start \"{def.TargetName}\"");
            return true;
        }

        private static string? QueryServiceStartType(string serviceName)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", $"qc \"{serviceName}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return null;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("AUTO_START")) return "auto";
                if (output.Contains("DEMAND_START")) return "demand";
                if (output.Contains("DISABLED")) return "disabled";
                if (output.Contains("BOOT_START")) return "boot";
                if (output.Contains("SYSTEM_START")) return "system";
                return "demand";
            }
            catch { return null; }
        }

        // ---------------- Scheduled Tasks ----------------

        private bool ApplyScheduledTask(DebloatDefinition def)
        {
            SaveBackupEntry(new DebloatBackupEntry { Id = def.Id, ActionType = DebloatActionType.ScheduledTask });
            var result = RunProcess("schtasks.exe", $"/Change /TN \"{def.TargetName}\" /Disable");
            return result;
        }

        private bool RevertScheduledTask(DebloatDefinition def)
        {
            return RunProcess("schtasks.exe", $"/Change /TN \"{def.TargetName}\" /Enable");
        }

        // ---------------- Helpers ----------------

        private static bool RunProcess(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process == null) return false;
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch { return false; }
        }

        private void SaveBackupEntry(DebloatBackupEntry entry)
        {
            var backups = LoadBackups();
            backups[entry.Id] = entry;
            SaveBackups(backups);
        }

        private Dictionary<string, DebloatBackupEntry> LoadBackups()
        {
            try
            {
                if (!File.Exists(_backupPath)) return [];
                string json = File.ReadAllText(_backupPath);
                return JsonSerializer.Deserialize<Dictionary<string, DebloatBackupEntry>>(json) ?? [];
            }
            catch { return []; }
        }

        private void SaveBackups(Dictionary<string, DebloatBackupEntry> backups)
        {
            try
            {
                string? folder = Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_backupPath, JsonSerializer.Serialize(backups));
            }
            catch { }
        }
    }
}