using System;
using System.IO;
using System.Text.Json;

namespace NexZeus
{
    public class AppSettingsData
    {
        public int PingThresholdMs { get; set; } = 100;
        public int CpuThresholdPercent { get; set; } = 85;
        public string BloxStrikePlaceId { get; set; } = "";
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