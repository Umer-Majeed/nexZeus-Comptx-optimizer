using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;

namespace NexZeus
{
    public class DnsServerResult
    {
        public required string Name { get; set; }
        public required string PrimaryIp { get; set; }
        public required string SecondaryIp { get; set; }
        public long LatencyMs { get; set; } // -1 = unreachable
    }

    public class DnsOptimizer
    {
        private readonly List<DnsServerResult> _candidates =
        [
            new() { Name = "Cloudflare", PrimaryIp = "1.1.1.1", SecondaryIp = "1.0.0.1" },
            new() { Name = "Google", PrimaryIp = "8.8.8.8", SecondaryIp = "8.8.4.4" },
            new() { Name = "Quad9", PrimaryIp = "9.9.9.9", SecondaryIp = "149.112.112.112" },
            new() { Name = "OpenDNS", PrimaryIp = "208.67.222.222", SecondaryIp = "208.67.220.220" }
        ];

        private readonly string _backupPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NexZeus", "dns_backup.json");

        public static string? GetActiveAdapterName()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    string desc = ni.Description.ToLower();
                    if (desc.Contains("virtual") || desc.Contains("vmware") ||
                        desc.Contains("bluetooth") || desc.Contains("loopback"))
                        continue;

                    return ni.Name;
                }
            }
            return null;
        }

        /// <summary>Returns the DNS servers configured on the first enabled IP-active adapter.</summary>
        public static List<string> GetCurrentDns()
        {
            List<string> result = [];
            try
            {
                const string query = "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = true";
                using var searcher = new ManagementObjectSearcher(query);

                foreach (ManagementObject mo in searcher.Get().Cast<ManagementObject>())
                {
                    var dnsServers = mo["DNSServerSearchOrder"] as string[];
                    if (dnsServers is { Length: > 0 })
                    {
                        result.AddRange(dnsServers);
                        break;
                    }
                }
            }
            catch { }
            return result;
        }

        public List<DnsServerResult> BenchmarkServers()
        {
            List<DnsServerResult> results = [];

            foreach (var candidate in _candidates)
            {
                long latency = PingServer(candidate.PrimaryIp);
                results.Add(new DnsServerResult
                {
                    Name = candidate.Name,
                    PrimaryIp = candidate.PrimaryIp,
                    SecondaryIp = candidate.SecondaryIp,
                    LatencyMs = latency
                });
            }

            return results
                .OrderBy(r => r.LatencyMs == -1 ? long.MaxValue : r.LatencyMs)
                .ToList();
        }

        private static long PingServer(string ip)
        {
            try
            {
                using var ping = new Ping();
                long total = 0;
                int successCount = 0;

                for (int i = 0; i < 2; i++)
                {
                    PingReply reply = ping.Send(ip, 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        total += reply.RoundtripTime;
                        successCount++;
                    }
                }

                return successCount == 0 ? -1 : total / successCount;
            }
            catch
            {
                return -1;
            }
        }

        public bool ApplyDns(string? adapterName, string primaryIp, string secondaryIp)
        {
            if (string.IsNullOrEmpty(adapterName)) return false;

            try
            {
                BackupCurrentDns();

                string args = $"interface ip set dns name=\"{adapterName}\" static {primaryIp} primary";
                RunNetsh(args);

                if (!string.IsNullOrEmpty(secondaryIp))
                {
                    string args2 = $"interface ip add dns name=\"{adapterName}\" {secondaryIp} index=2";
                    RunNetsh(args2);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RevertDns(string? adapterName)
        {
            if (string.IsNullOrEmpty(adapterName)) return false;

            try
            {
                var backup = LoadBackup();

                if (backup == null || backup.Count == 0)
                {
                    string args = $"interface ip set dns name=\"{adapterName}\" source=dhcp";
                    RunNetsh(args);
                    return true;
                }

                string argsPrimary = $"interface ip set dns name=\"{adapterName}\" static {backup[0]} primary";
                RunNetsh(argsPrimary);

                for (int i = 1; i < backup.Count; i++)
                {
                    string argsSecondary = $"interface ip add dns name=\"{adapterName}\" {backup[i]} index={i + 1}";
                    RunNetsh(argsSecondary);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RunNetsh(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }

        private void BackupCurrentDns()
        {
            var current = GetCurrentDns();
            if (current.Count == 0) return;

            try
            {
                string? dir = System.IO.Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                if (!System.IO.File.Exists(_backupPath))
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(current);
                    System.IO.File.WriteAllText(_backupPath, json);
                }
            }
            catch { }
        }

        private List<string>? LoadBackup()
        {
            try
            {
                if (!System.IO.File.Exists(_backupPath)) return null;
                string json = System.IO.File.ReadAllText(_backupPath);
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}