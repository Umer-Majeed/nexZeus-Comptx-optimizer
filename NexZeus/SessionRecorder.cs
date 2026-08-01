using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NexZeus
{
    public class SessionSample
    {
        public DateTime Timestamp { get; set; }
        public float Cpu { get; set; }
        public double AppRamGB { get; set; }
        public long PingMs { get; set; }
    }

    public class SessionRecorder
    {
        private List<SessionSample> _samples = new();
        public bool IsRecording { get; private set; }
        public DateTime? StartTime { get; private set; }

        public void Start()
        {
            _samples.Clear();
            StartTime = DateTime.Now;
            IsRecording = true;
        }

        public void Stop()
        {
            IsRecording = false;
        }

        public void AddSample(float cpu, double ramGB, long pingMs)
        {
            if (!IsRecording) return;
            _samples.Add(new SessionSample
            {
                Timestamp = DateTime.Now,
                Cpu = cpu,
                AppRamGB = ramGB,
                PingMs = pingMs
            });
        }

        public string SaveReport()
        {
            if (_samples.Count == 0) return null;

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NexZeus", "Sessions");
            Directory.CreateDirectory(folder);

            string fileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string fullPath = Path.Combine(folder, fileName);

            using var writer = new StreamWriter(fullPath);
            writer.WriteLine("Timestamp,CPU(%),AppRAM(GB),Ping(ms)");
            foreach (var s in _samples)
                writer.WriteLine($"{s.Timestamp:HH:mm:ss},{s.Cpu:F1},{s.AppRamGB:F2},{s.PingMs}");

            writer.WriteLine();
            writer.WriteLine($"Avg CPU,{_samples.Average(s => s.Cpu):F1}");
            writer.WriteLine($"Max CPU,{_samples.Max(s => s.Cpu):F1}");
            writer.WriteLine($"Avg Ping,{_samples.Average(s => s.PingMs):F1}");
            writer.WriteLine($"Max Ping,{_samples.Max(s => s.PingMs)}");

            return fullPath;
        }

        public List<string> AnalyzeSession()
        {
            var issues = new List<string>();
            if (_samples.Count == 0) return issues;

            float avgCpu = _samples.Average(s => s.Cpu);
            float maxCpu = _samples.Max(s => s.Cpu);
            double avgPing = _samples.Average(s => s.PingMs);
            long maxPing = _samples.Max(s => s.PingMs);

            if (avgCpu > 85)
                issues.Add("⚠ High average CPU usage (" + avgCpu.ToString("F0") + "%) — background apps may be competing for CPU time.");

            if (maxCpu > 95)
                issues.Add("⚠ CPU hit near 100% at least once — this can cause frame drops/stutter.");

            if (avgPing > 100)
                issues.Add("⚠ Average ping is high (" + avgPing.ToString("F0") + "ms) — check your network connection or router.");

            if (maxPing > 300)
                issues.Add("⚠ Ping spiked above 300ms — possible network instability or interference.");

            if (issues.Count == 0)
                issues.Add("✔ No major issues detected — system performed well during this session.");

            return issues;
        }
    }
}