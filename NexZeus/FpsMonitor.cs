using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace NexZeus
{
    public class FpsMonitor : IDisposable
    {
        private Process? _process;
        private readonly List<double> _frameTimesMs = [];
        private readonly Lock _lock = new();
        private int _msColumnIndex = -1;

        public int CurrentFps { get; private set; }
        public double LastFrameTimeMs { get; private set; }
        public bool IsStuttering { get; private set; } // last frame > 2x rolling avg

        /// <summary>Snapshot of the last ~60 frame times (ms), oldest first — feed straight into the overlay graph.</summary>
        public double[] GetFrameTimeHistory()
        {
            lock (_lock) return [.. _frameTimesMs];
        }

        public void Start(string processNameWithExe)
        {
            Stop();

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "PresentMon.exe");
            if (!File.Exists(exePath))
            {
                Debug.WriteLine($"PresentMon.exe not found at: {exePath}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--process_name {processNameWithExe} --output_stdout --no_console_stats --stop_existing_session --terminate_on_proc_exit",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnLine;
            _process.ErrorDataReceived += (s, e) => Debug.WriteLine($"[PresentMon STDERR]: {e.Data}");

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void OnLine(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            var parts = e.Data.Split(',');

            if (_msColumnIndex == -1)
            {
                _msColumnIndex = Array.FindIndex(parts, h => h.Trim().Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
                return; // header line
            }

            if (_msColumnIndex == -1 || _msColumnIndex >= parts.Length) return;
            if (!double.TryParse(parts[_msColumnIndex], out double ms) || ms <= 0) return;

            lock (_lock)
            {
                double prevAvg = _frameTimesMs.Count > 0 ? _frameTimesMs.Average() : ms;

                _frameTimesMs.Add(ms);
                if (_frameTimesMs.Count > 60) _frameTimesMs.RemoveAt(0);

                LastFrameTimeMs = ms;
                IsStuttering = ms > prevAvg * 2.0 && prevAvg > 0;

                double avgMs = _frameTimesMs.Average();
                CurrentFps = avgMs > 0 ? (int)Math.Round(1000.0 / avgMs) : 0;
            }
        }

        public void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.OutputDataReceived -= OnLine;
                    _process.Kill(true);
                }
            }
            catch { }
            finally
            {
                _process?.Dispose();
                _process = null;
                lock (_lock) { _frameTimesMs.Clear(); }
                CurrentFps = 0;
                LastFrameTimeMs = 0;
                IsStuttering = false;
                _msColumnIndex = -1;
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}