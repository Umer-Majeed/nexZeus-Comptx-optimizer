using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NexZeus
{
    public class AppSettingsData
    {
        public int PingThresholdMs { get; set; } = 100;
        public int CpuThresholdPercent { get; set; } = 85;
        public string BloxStrikePlaceId { get; set; } = "";
        public bool StartWithWindows { get; set; } = false;
        public bool AutoOptimizeOnGameStart { get; set; } = false;
        public bool AutoSuspendBackgroundApps { get; set; } = false;
        public List<string> AutoApplyTweakIds { get; set; } = [];
        public List<string> ExcludedProcessNames { get; set; } = [];
    }

    public static class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "settings.json");

        private static readonly JsonSerializerOptions CachedOptions = new() { WriteIndented = true };

        private static AppSettingsData _data = new();

        static AppSettings()
        {
            Load();
        }

        public static int PingThresholdMs
        {
            get => _data.PingThresholdMs;
            set { _data.PingThresholdMs = value; Save(); }
        }

        public static int CpuThresholdPercent
        {
            get => _data.CpuThresholdPercent;
            set { _data.CpuThresholdPercent = value; Save(); }
        }

        public static string BloxStrikePlaceId
        {
            get => _data.BloxStrikePlaceId;
            set { _data.BloxStrikePlaceId = value; Save(); }
        }

        public static bool StartWithWindows
        {
            get => _data.StartWithWindows;
            set { _data.StartWithWindows = value; Save(); ApplyStartupSetting(value); }
        }

        public static bool AutoOptimizeOnGameStart
        {
            get => _data.AutoOptimizeOnGameStart;
            set { _data.AutoOptimizeOnGameStart = value; Save(); }
        }

        public static bool AutoSuspendBackgroundApps
        {
            get => _data.AutoSuspendBackgroundApps;
            set { _data.AutoSuspendBackgroundApps = value; Save(); }
        }

        public static List<string> AutoApplyTweakIds
        {
            get => _data.AutoApplyTweakIds;
            set { _data.AutoApplyTweakIds = value; Save(); }
        }

        public static List<string> ExcludedProcessNames
        {
            get => _data.ExcludedProcessNames;
            set { _data.ExcludedProcessNames = value; Save(); }
        }

        private static void ApplyStartupSetting(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

                if (key != null)
                {
                    if (enable)
                    {
                        string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("NexZeus", exePath);
                        }
                    }
                    else
                    {
                        key.DeleteValue("NexZeus", false);
                    }
                }
            }
            catch { }
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _data = JsonSerializer.Deserialize<AppSettingsData>(json, CachedOptions) ?? new AppSettingsData();
                }
                else
                {
                    _data = new AppSettingsData();
                }
            }
            catch
            {
                _data = new AppSettingsData();
            }
        }

        private static void Save()
        {
            try
            {
                string? folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                string json = JsonSerializer.Serialize(_data, CachedOptions);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}