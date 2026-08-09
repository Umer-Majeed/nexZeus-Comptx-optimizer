using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NexZeus
{
    public class PowerPlan
    {
        public required string Guid { get; set; }
        public required string Name { get; set; }
    }

    /// <summary>Thin wrapper around powercfg.exe for listing/reading/switching Windows power plans.</summary>
    public static class PowerPlanManager
    {
        // Hidden-by-default "Ultimate Performance" scheme Microsoft ships on Windows 10/11 Pro+.
        private const string UltimatePerformanceSeedGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public static List<PowerPlan> GetAvailablePlans()
        {
            EnsureUltimatePerformanceVisible();

            var plans = new List<PowerPlan>();
            string output = RunPowercfg("/list");

            // Lines look like: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *"
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("Power Scheme GUID", StringComparison.OrdinalIgnoreCase)) continue;

                int guidStart = line.IndexOf(':') + 1;
                int parenStart = line.IndexOf('(');
                int parenEnd = line.IndexOf(')');
                if (guidStart <= 0 || parenStart < 0 || parenEnd < 0) continue;

                string guid = line[guidStart..parenStart].Trim();
                string name = line[(parenStart + 1)..parenEnd].Trim();
                if (System.Guid.TryParse(guid, out _))
                    plans.Add(new PowerPlan { Guid = guid, Name = name });
            }

            return plans;
        }

        public static string? GetActivePlanGuid()
        {
            string output = RunPowercfg("/getactivescheme");
            int colonIdx = output.IndexOf(':');
            int parenIdx = output.IndexOf('(');
            if (colonIdx < 0 || parenIdx < 0) return null;

            string guid = output[(colonIdx + 1)..parenIdx].Trim();
            return System.Guid.TryParse(guid, out _) ? guid : null;
        }

        public static bool SetActivePlan(string guid)
        {
            return RunPowercfgExit($"/setactive {guid}") == 0;
        }

        /// <summary>Ultimate Performance is hidden until duplicated once; safe to call repeatedly.</summary>
        private static void EnsureUltimatePerformanceVisible()
        {
            string existing = RunPowercfg("/list");
            if (existing.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase)) return;

            RunPowercfg($"-duplicatescheme {UltimatePerformanceSeedGuid}");
        }

        private static string RunPowercfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return string.Empty;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch { return string.Empty; }
        }

        private static int RunPowercfgExit(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return -1;
                process.WaitForExit();
                return process.ExitCode;
            }
            catch { return -1; }
        }
    }

    public class GameProfileData
    {
        public required string Id { get; set; }
        public required string ProcessName { get; set; } // no .exe
        public required string PowerPlanGuid { get; set; }
        public required string PowerPlanName { get; set; }
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Watches configured game processes; when any of them launches, swaps to that profile's
    /// power plan (e.g. Ultimate Performance), and reverts to whatever plan was active before
    /// once every tracked game has exited.
    /// </summary>
    public class GameProfileService
    {
        private HashSet<string> _previouslyRunning = new(StringComparer.OrdinalIgnoreCase);
        private string? _originalPlanGuid;
        private string? _activeProfileName;

        public event Action<string, string>? ProfileApplied;  // gameName, planName
        public event Action<string>? ProfileReverted;          // gameName

        public void Tick()
        {
            var profiles = AppSettings.GameProfiles.Where(p => p.Enabled).ToList();
            if (profiles.Count == 0)
            {
                if (_originalPlanGuid != null) RevertNow();
                return;
            }

            var runningNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                if (Process.GetProcessesByName(profile.ProcessName).Length > 0)
                    runningNow.Add(profile.ProcessName);
            }

            bool wasEmpty = _previouslyRunning.Count == 0;
            bool isEmpty = runningNow.Count == 0;

            if (wasEmpty && !isEmpty)
            {
                // A tracked game just launched — capture current plan, then switch.
                string firstGame = runningNow.First();
                var profile = profiles.First(p => p.ProcessName.Equals(firstGame, StringComparison.OrdinalIgnoreCase));

                _originalPlanGuid = PowerPlanManager.GetActivePlanGuid();
                if (PowerPlanManager.SetActivePlan(profile.PowerPlanGuid))
                {
                    _activeProfileName = profile.ProcessName;
                    ProfileApplied?.Invoke(profile.ProcessName, profile.PowerPlanName);
                }
            }
            else if (!wasEmpty && isEmpty)
            {
                RevertNow();
            }

            _previouslyRunning = runningNow;
        }

        private void RevertNow()
        {
            if (_originalPlanGuid != null && PowerPlanManager.SetActivePlan(_originalPlanGuid))
            {
                ProfileReverted?.Invoke(_activeProfileName ?? "game");
            }
            _originalPlanGuid = null;
            _activeProfileName = null;
        }

        /// <summary>Force a revert (e.g. on app shutdown) if a swap is currently active.</summary>
        public void ForceRevert()
        {
            if (_originalPlanGuid != null) RevertNow();
        }
    }
}