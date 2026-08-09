using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NexZeus
{
    /// <summary>
    /// Represents one PCI/PCIe device (GPU, NIC, storage controller, etc.) found under
    /// HKLM\SYSTEM\CurrentControlSet\Enum that exposes an Interrupt Management subkey.
    /// </summary>
    public class MsiDeviceInfo
    {
        public required string InstanceId { get; set; }   // e.g. PCI\VEN_10DE&DEV_2782&...\4&1a2b3c4d&0&0008
        public required string FriendlyName { get; set; }
        public bool MsiSupported { get; set; }             // MSISupported == 1 already present
        public bool MsiEnabled { get; set; }                // current on/off state
        public int MessageNumberLimit { get; set; }         // 0 = device default
        public int AssignedCpu { get; set; } = -1;          // -1 = not pinned, else target core index
    }

    /// <summary>
    /// Reads/writes the "Interrupt Management\MessageSignaledInterruptProperties" and
    /// "Interrupt Management\Affinity Policy" registry subkeys that Windows uses per-device.
    /// Enabling MSI mode + pinning device interrupts to a core (ideally not core 0, and not
    /// a core sharing an L2/L3 slice with the game thread) is the same trick MSI Utility /
    /// Process Lasso's "MSI mode" toggle and hone.gg's advanced tab use to cut DPC latency.
    /// Requires the app to run elevated (admin) — HKLM\SYSTEM writes will fail silently otherwise.
    /// </summary>
    public class MsiInterruptOptimizer
    {
        private const string EnumRoot = @"SYSTEM\CurrentControlSet\Enum";

        /// <summary>Enumerates PCI devices (GPU/NIC/storage/USB controllers) that can use MSI mode.</summary>
        public static List<MsiDeviceInfo> GetMsiCapableDevices()
        {
            List<MsiDeviceInfo> results = [];
            using var enumKey = Registry.LocalMachine.OpenSubKey(EnumRoot);
            if (enumKey == null) return results;

            // Only PCI bus devices carry Interrupt Management subkeys.
            using var pciKey = enumKey.OpenSubKey("PCI");
            if (pciKey == null) return results;

            foreach (var venDevName in pciKey.GetSubKeyNames())
            {
                using var venDevKey = pciKey.OpenSubKey(venDevName);
                if (venDevKey == null) continue;

                foreach (var instanceName in venDevKey.GetSubKeyNames())
                {
                    string instanceId = $@"PCI\{venDevName}\{instanceName}";
                    using var instanceKey = venDevKey.OpenSubKey(instanceName);
                    using var msiKey = instanceKey?.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
                    if (instanceKey == null || msiKey == null) continue; // device doesn't expose MSI controls

                    string friendly = instanceKey.GetValue("FriendlyName") as string
                                       ?? instanceKey.GetValue("DeviceDesc") as string
                                       ?? venDevName;
                    // DeviceDesc is often "%SomeString%;Actual Name" — trim the semicolon prefix.
                    int semi = friendly.LastIndexOf(';');
                    if (semi >= 0) friendly = friendly[(semi + 1)..];

                    int msiSupported = (int)(msiKey.GetValue("MSISupported") ?? 0);
                    int limit = (int)(msiKey.GetValue("MessageNumberLimit") ?? 0);

                    int assignedCpu = -1;
                    using var affKey = instanceKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy");
                    if (affKey?.GetValue("AssignmentSetOverride") is byte[] mask)
                        assignedCpu = MaskToFirstCpu(mask);

                    results.Add(new()
                    {
                        InstanceId = instanceId,
                        FriendlyName = friendly,
                        MsiSupported = msiSupported == 1,
                        MsiEnabled = msiSupported == 1,
                        MessageNumberLimit = limit,
                        AssignedCpu = assignedCpu
                    });
                }
            }

            return results.OrderBy(d => d.FriendlyName).ToList();
        }

        /// <summary>Turns MSI mode on/off for a device. Falls back gracefully if the key is missing (creates it).</summary>
        public static bool SetMsiEnabled(MsiDeviceInfo device, bool enable)
        {
            try
            {
                string path = $@"{EnumRoot}\{device.InstanceId}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
                using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
                if (key == null) return false;
                key.SetValue("MSISupported", enable ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Pins a device's interrupt affinity to a single logical CPU core (0-based index).
        /// Recommended: avoid core 0 (busy with OS housekeeping) and avoid the core your game's
        /// main thread is pinned to. Pass cpuIndex = -1 to clear the override (let Windows decide).
        /// </summary>
        public static bool SetInterruptAffinity(MsiDeviceInfo device, int cpuIndex)
        {
            try
            {
                string path = $@"{EnumRoot}\{device.InstanceId}\Device Parameters\Interrupt Management\Affinity Policy";
                using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
                if (key == null) return false;

                if (cpuIndex < 0)
                {
                    key.DeleteValue("AssignmentSetOverride", false);
                    key.DeleteValue("DevicePolicy", false);
                    return true;
                }

                key.SetValue("AssignmentSetOverride", CpuToMask(cpuIndex), RegistryValueKind.Binary);
                // 4 = IrqPolicySpecifiedProcessors -> honor our exact mask instead of "closest" heuristics.
                key.SetValue("DevicePolicy", 4, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }

        private static byte[] CpuToMask(int cpuIndex)
        {
            var mask = new byte[8]; // supports up to 64 logical cores, little-endian bitmask
            mask[cpuIndex / 8] = (byte)(1 << (cpuIndex % 8));
            return mask;
        }

        private static int MaskToFirstCpu(byte[] mask)
        {
            for (int i = 0; i < mask.Length * 8; i++)
                if ((mask[i / 8] & (1 << (i % 8))) != 0) return i;
            return -1;
        }

        /// <summary>Logical CPU core count, used to populate the affinity picker in the UI.</summary>
        public static int GetLogicalCoreCount() => Environment.ProcessorCount;
    }
}