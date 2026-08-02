using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;

namespace NexZeus
{
    public class WindowsOptimizer
    {
        public static List<string> CheckSettings()
        {
            return
            [
                CheckGameMode(),
                CheckPowerPlan()
            ];
        }

        private static string CheckGameMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
                if (key == null) return "⚠ Game Mode is OFF — enabling it can help prioritize game resources.";

                var value = key.GetValue("AutoGameModeEnabled");
                bool enabled = value != null && (int)value == 1;
                return enabled ? "✔ Game Mode is ON" : "⚠ Game Mode is OFF — enabling it can help prioritize game resources.";
            }
            catch
            {
                return "? Could not check Game Mode status.";
            }
        }

        private static string CheckPowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/GETACTIVESCHEME")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return "? Could not check power plan.";

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("High performance") || output.Contains("Ultimate Performance"))
                    return "✔ Power plan is set for performance.";
                else
                    return "⚠ Power plan is Balanced/Power Saver — switching to High Performance can reduce input lag.";
            }
            catch
            {
                return "? Could not check power plan.";
            }
        }

        public static bool EnableGameMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", writable: true);
                key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetHighPerformancePlan()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/SETACTIVE 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}