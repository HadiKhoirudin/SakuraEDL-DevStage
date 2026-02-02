
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.Linq;

namespace LoveAlways.Fastboot.Models
{
    /// <summary>
    /// Fastboot Device Information
    /// </summary>
    public class FastbootDeviceInfo
    {
        /// <summary>
        /// Device serial number
        /// </summary>
        public string Serial { get; set; }

        /// <summary>
        /// Device status (fastboot/fastbootd)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Product name
        /// </summary>
        public string Product { get; set; }

        /// <summary>
        /// Whether secure boot is enabled
        /// </summary>
        public bool SecureBoot { get; set; }

        /// <summary>
        /// Current slot (A/B partition)
        /// </summary>
        public string CurrentSlot { get; set; }

        /// <summary>
        /// Whether in fastbootd userspace mode
        /// </summary>
        public bool IsFastbootd { get; set; }

        /// <summary>
        /// Maximum download size
        /// </summary>
        public long MaxDownloadSize { get; set; } = -1;

        /// <summary>
        /// Snapshot update status
        /// </summary>
        public string SnapshotUpdateStatus { get; set; }

        /// <summary>
        /// Unlock status
        /// </summary>
        public bool? Unlocked { get; set; }

        /// <summary>
        /// Bootloader version
        /// </summary>
        public string BootloaderVersion { get; set; }

        /// <summary>
        /// Baseband version
        /// </summary>
        public string BasebandVersion { get; set; }

        /// <summary>
        /// Hardware version
        /// </summary>
        public string HardwareVersion { get; set; }

        /// <summary>
        /// Variant
        /// </summary>
        public string Variant { get; set; }

        /// <summary>
        /// Partition size dictionary
        /// </summary>
        public Dictionary<string, long> PartitionSizes { get; private set; } = new Dictionary<string, long>();

        /// <summary>
        /// Partition logical status dictionary
        /// </summary>
        public Dictionary<string, bool?> PartitionIsLogical { get; private set; } = new Dictionary<string, bool?>();

        /// <summary>
        /// All raw variables
        /// </summary>
        public Dictionary<string, string> RawVariables { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether A/B partition is supported
        /// </summary>
        public bool HasABPartition => !string.IsNullOrEmpty(CurrentSlot);

        /// <summary>
        /// Get the value of a specified variable
        /// </summary>
        /// <param name="key">Variable name (case-insensitive)</param>
        /// <returns>Variable value, or null if it doesn't exist</returns>
        public string GetVariable(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            string lowKey = key.ToLowerInvariant();
            if (RawVariables.TryGetValue(lowKey, out string value))
                return value;

            return null;
        }

        /// <summary>
        /// Parse device information from getvar:all output
        /// </summary>
        public static FastbootDeviceInfo ParseFromGetvarAll(string rawData)
        {
            var info = new FastbootDeviceInfo();

            if (string.IsNullOrEmpty(rawData))
                return info;

            foreach (string line in rawData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // fastboot output format: (bootloader) key: value or key: value
                string processedLine = line.Trim();

                // Remove (bootloader) prefix
                if (processedLine.StartsWith("(bootloader)"))
                {
                    processedLine = processedLine.Substring(12).Trim();
                }

                // Parse key: value format
                int colonIndex = processedLine.IndexOf(':');
                if (colonIndex <= 0) continue;

                string key = processedLine.Substring(0, colonIndex).Trim().ToLowerInvariant();
                string value = processedLine.Substring(colonIndex + 1).Trim();

                // Store raw variables
                info.RawVariables[key] = value;

                // Parse partition size: partition-size:boot_a: 0x4000000
                if (key.StartsWith("partition-size:"))
                {
                    string partName = key.Substring("partition-size:".Length);
                    if (TryParseHexOrDecimal(value, out long size))
                    {
                        info.PartitionSizes[partName] = size;
                    }
                    continue;
                }

                // Parse logical partition: is-logical:system_a: yes
                if (key.StartsWith("is-logical:"))
                {
                    string partName = key.Substring("is-logical:".Length);
                    info.PartitionIsLogical[partName] = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                // Parse other common variables
                switch (key)
                {
                    case "product":
                        info.Product = value;
                        break;
                    case "secure":
                        info.SecureBoot = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "current-slot":
                        info.CurrentSlot = value;
                        break;
                    case "is-userspace":
                        info.IsFastbootd = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "max-download-size":
                        if (TryParseHexOrDecimal(value, out long maxSize))
                            info.MaxDownloadSize = maxSize;
                        break;
                    case "snapshot-update-status":
                        info.SnapshotUpdateStatus = value;
                        break;
                    case "unlocked":
                        info.Unlocked = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "version-bootloader":
                        info.BootloaderVersion = value;
                        break;
                    case "version-baseband":
                        info.BasebandVersion = value;
                        break;
                    case "hw-revision":
                        info.HardwareVersion = value;
                        break;
                    case "variant":
                        info.Variant = value;
                        break;
                }
            }

            return info;
        }

        /// <summary>
        /// Try to parse hex or decimal number
        /// </summary>
        private static bool TryParseHexOrDecimal(string value, out long result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;

            value = value.Trim();

            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    result = Convert.ToInt64(value.Substring(2), 16);
                    return true;
                }
                else
                {
                    return long.TryParse(value, out result);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get partition list
        /// </summary>
        public List<FastbootPartitionInfo> GetPartitions()
        {
            var partitions = new List<FastbootPartitionInfo>();

            foreach (var kv in PartitionSizes.OrderBy(x => x.Key))
            {
                bool? isLogical = null;
                PartitionIsLogical.TryGetValue(kv.Key, out isLogical);

                partitions.Add(new FastbootPartitionInfo
                {
                    Name = kv.Key,
                    Size = kv.Value,
                    IsLogical = isLogical
                });
            }

            return partitions;
        }
    }

    /// <summary>
    /// Fastboot Partition Information
    /// </summary>
    public class FastbootPartitionInfo
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public bool? IsLogical { get; set; }

        /// <summary>
        /// Formatted size display
        /// </summary>
        public string SizeFormatted
        {
            get
            {
                if (Size < 0) return "Unknown";
                if (Size >= 1024L * 1024 * 1024)
                    return $"{Size / (1024.0 * 1024 * 1024):F2} GB";
                if (Size >= 1024 * 1024)
                    return $"{Size / (1024.0 * 1024):F2} MB";
                if (Size >= 1024)
                    return $"{Size / 1024.0:F2} KB";
                return $"{Size} B";
            }
        }

        /// <summary>
        /// Display text for whether it's a logical partition
        /// </summary>
        public string IsLogicalText
        {
            get
            {
                if (IsLogical == null) return "-";
                return IsLogical.Value ? "Yes" : "No";
            }
        }
    }

    /// <summary>
    /// Fastboot Device List Item
    /// </summary>
    public class FastbootDeviceListItem
    {
        public string Serial { get; set; }
        public string Status { get; set; }

        public override string ToString()
        {
            return $"{Serial} ({Status})";
        }
    }
}
