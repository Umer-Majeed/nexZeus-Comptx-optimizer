using System;
using System.IO;
using System.Text.Json;

namespace NexZeus
{
    public class AppSettingsData
    {
        public int PingThresholdMs { get; set; } = 100;
        public int CpuThresholdPercent { get; set; } = 85;
    }

    public static class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NexZeus", "settings.json");

        private static AppSettingsData _data;

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

        private static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _data = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
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
                string folder = Path.GetDirectoryName(FilePath);
                Directory.CreateDirectory(folder);
                string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}