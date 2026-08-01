using System;
using System.Collections.Generic;
using System.Text;

namespace NexZeus
{
    public static class AppSettings
    {
        public static int PingThresholdMs { get; set; } = 100;
        public static int CpuThresholdPercent { get; set; } = 85;
    }
}