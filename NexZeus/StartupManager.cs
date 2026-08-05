using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace NexZeus
{
    public class StartupAppInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // "Registry" or "Startup Folder"

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DisabledBackupPath = @"Software\NexZeus\DisabledStartup";

        public static List<StartupAppInfo> GetStartupApps()
        {
            var result = new List<StartupAppInfo>();

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (runKey != null)
                {
                    foreach (var name in runKey.GetValueNames())
                    {
                        result.Add(new StartupAppInfo
                        {
                            Name = name,
                            Command = runKey.GetValue(name)?.ToString() ?? "",
                            Source = "Registry",
                            IsEnabled = true
                        });
                    }
                }

                using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledBackupPath);
                if (disabledKey != null)
                {
                    foreach (var name in disabledKey.GetValueNames())
                    {
                        result.Add(new StartupAppInfo
                        {
                            Name = name,
                            Command = disabledKey.GetValue(name)?.ToString() ?? "",
                            Source = "Registry",
                            IsEnabled = false
                        });
                    }
                }
            }
            catch { }

            return result;
        }

        public static bool DisableStartupApp(StartupAppInfo app)
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                using var backupKey = Registry.CurrentUser.CreateSubKey(DisabledBackupPath);

                if (runKey == null || backupKey == null) return false;

                backupKey.SetValue(app.Name, app.Command);
                runKey.DeleteValue(app.Name, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool EnableStartupApp(StartupAppInfo app)
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                using var backupKey = Registry.CurrentUser.OpenSubKey(DisabledBackupPath, writable: true);

                if (runKey == null) return false;

                runKey.SetValue(app.Name, app.Command);
                backupKey?.DeleteValue(app.Name, false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}