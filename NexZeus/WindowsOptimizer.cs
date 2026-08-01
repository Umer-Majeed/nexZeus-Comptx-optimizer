using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NexZeus
{
    public class WindowsOptimizer
    {
        public List<string> CheckSettings()
        {
            var results = new List<string>();

            results.Add(CheckGameMode());
            results.Add(CheckPowerPlan());

            return results;
        }

        private string CheckGameMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
                var value = key?.GetValue("AutoGameModeEnabled");
                bool enabled = value != null && (int)value == 1;
                return enabled ? "✔ Game Mode is ON" : "⚠ Game Mode is OFF — enabling it can help prioritize game resources.";
            }
            catch
            {
                return "? Could not check Game Mode status.";
            }
        }

        private string CheckPowerPlan()
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
    }
}