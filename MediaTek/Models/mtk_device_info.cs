// ============================================================================
// LoveAlways - MediaTek Device Info Models
// MediaTek Device Information Models
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;

namespace LoveAlways.MediaTek.Models
{
    /// <summary>
    /// MTK Chip Info
    /// </summary>
    public class MtkChipInfo
    {
        /// <summary>Hardware Code (HW Code)</summary>
        public ushort HwCode { get; set; }

        /// <summary>Hardware Version</summary>
        public ushort HwVer { get; set; }

        /// <summary>Hardware Subcode</summary>
        public ushort HwSubCode { get; set; }

        /// <summary>Software Version</summary>
        public ushort SwVer { get; set; }

        /// <summary>Chip Name</summary>
        public string ChipName { get; set; }

        /// <summary>Chip Description</summary>
        public string Description { get; set; }

        /// <summary>Watchdog Address</summary>
        public uint WatchdogAddr { get; set; }

        /// <summary>UART Address</summary>
        public uint UartAddr { get; set; }

        /// <summary>BROM Payload Address</summary>
        public uint BromPayloadAddr { get; set; }

        /// <summary>DA Payload Address</summary>
        public uint DaPayloadAddr { get; set; }

        /// <summary>CQ_DMA Base Address</summary>
        public uint? CqDmaBase { get; set; }

        /// <summary>DA Mode</summary>
        public int DaMode { get; set; } = 6;  // Default XML mode

        /// <summary>Supports XFlash</summary>
        public bool SupportsXFlash { get; set; }

        /// <summary>Requires Signature</summary>
        public bool RequiresSignature { get; set; }

        /// <summary>Supports 64-bit</summary>
        public bool Is64Bit { get; set; }

        /// <summary>BROM is patched</summary>
        public bool BromPatched { get; set; }

        /// <summary>Requires V6 Loader</summary>
        public bool RequiresLoader { get; set; }

        /// <summary>Loader Filename</summary>
        public string LoaderName { get; set; }

        /// <summary>Codename</summary>
        public string Codename { get; set; }

        /// <summary>Exploit Type</summary>
        public string ExploitType { get; set; }

        /// <summary>Has available exploit</summary>
        public bool HasExploit { get; set; }

        /// <summary>
        /// Get Chip Name (based on HW Code)
        /// </summary>
        public string GetChipName()
        {
            if (!string.IsNullOrEmpty(ChipName))
                return ChipName;

            return HwCode switch
            {
                0x0279 => "MT6797",
                0x0321 => "MT6735",
                0x0326 => "MT6755",
                0x0335 => "MT6737",
                0x0507 => "MT6779",
                0x0551 => "MT6768",
                0x0562 => "MT6761",
                0x0570 => "MT6580",
                0x0571 => "MT6572",
                0x0572 => "MT6572",
                0x0588 => "MT6785",
                0x0600 => "MT6853",
                0x0601 => "MT6757",
                0x0688 => "MT6771",
                0x0690 => "MT6763",
                0x0699 => "MT6739",
                0x0707 => "MT6762",
                0x0717 => "MT6765",
                0x0725 => "MT6765",
                0x0766 => "MT6877",
                0x0959 => "MT6877",  // Preloader mode HW Code
                0x0788 => "MT6873",
                0x0813 => "MT6833",
                0x0816 => "MT6893",
                0x0886 => "MT6885",
                0x0950 => "MT6833",
                0x0989 => "MT6891",
                0x0996 => "MT6895",
                0x1172 => "MT6895",  // Dimensity 8200 (Previously mislabeled as MT6983)
                0x1186 => "MT6983",  // Dimensity 9000 (To be confirmed)
                0x1208 => "MT6895",
                0x1209 => "MT6985",
                0x2502 => "MT2502",
                0x2503 => "MT2503",
                0x2601 => "MT2601",
                0x6261 => "MT6261",
                0x6570 => "MT6570",
                0x6575 => "MT6575",
                0x6577 => "MT6577",
                0x6580 => "MT6580",
                0x6582 => "MT6582",
                0x6589 => "MT6589",
                0x6592 => "MT6592",
                0x6595 => "MT6595",
                0x6752 => "MT6752",
                0x6753 => "MT6753",
                0x6755 => "MT6755",
                0x6757 => "MT6757",
                0x6761 => "MT6761",
                0x6763 => "MT6763",
                0x6765 => "MT6765",
                0x6768 => "MT6768",
                0x6771 => "MT6771",
                0x6779 => "MT6779",
                0x6785 => "MT6785",
                0x6795 => "MT6795",
                0x6797 => "MT6797",
                0x8127 => "MT8127",
                0x8135 => "MT8135",
                0x8163 => "MT8163",
                0x8167 => "MT8167",
                0x8168 => "MT8168",
                0x8173 => "MT8173",
                0x8176 => "MT8176",
                0x8695 => "MT8695",
                _ => $"MT{HwCode:X4}"
            };
        }

        /// <summary>
        /// Clone
        /// </summary>
        public MtkChipInfo Clone()
        {
            return (MtkChipInfo)MemberwiseClone();
        }
    }

    /// <summary>
    /// MTK Device Info
    /// </summary>
    public class MtkDeviceInfo
    {
        /// <summary>Device Path/Port Name</summary>
        public string DevicePath { get; set; }

        /// <summary>COM Port</summary>
        public string ComPort { get; set; }

        /// <summary>USB VID</summary>
        public int Vid { get; set; }

        /// <summary>USB PID</summary>
        public int Pid { get; set; }

        /// <summary>Device Description</summary>
        public string Description { get; set; }

        /// <summary>Whether in download mode</summary>
        public bool IsDownloadMode { get; set; }

        /// <summary>Chip Info</summary>
        public MtkChipInfo ChipInfo { get; set; }

        /// <summary>ME ID</summary>
        public byte[] MeId { get; set; }

        /// <summary>SOC ID</summary>
        public byte[] SocId { get; set; }

        /// <summary>
        /// ME ID Hex String
        /// </summary>
        public string MeIdHex => MeId != null ? BitConverter.ToString(MeId).Replace("-", "") : "";

        /// <summary>
        /// SOC ID Hex String
        /// </summary>
        public string SocIdHex => SocId != null ? BitConverter.ToString(SocId).Replace("-", "") : "";

        /// <summary>
        /// DA Mode (5 = XFlash, 6 = XML)
        /// </summary>
        public int DaMode { get; set; }
    }

    /// <summary>
    /// DA Entry Info
    /// </summary>
    public class DaEntry
    {
        /// <summary>DA Name</summary>
        public string Name { get; set; }

        /// <summary>Load Address</summary>
        public uint LoadAddr { get; set; }

        /// <summary>Signature Length</summary>
        public int SignatureLen { get; set; }

        /// <summary>DA Data</summary>
        public byte[] Data { get; set; }

        /// <summary>Whether 64-bit</summary>
        public bool Is64Bit { get; set; }

        /// <summary>DA Version</summary>
        public int Version { get; set; }

        /// <summary>DA Type (Legacy/XFlash/XML)</summary>
        public int DaType { get; set; }
    }

    /// <summary>
    /// MTK Partition Info
    /// </summary>
    public class MtkPartitionInfo
    {
        /// <summary>Partition Name</summary>
        public string Name { get; set; }

        /// <summary>Start Sector</summary>
        public ulong StartSector { get; set; }

        /// <summary>Sector Count</summary>
        public ulong SectorCount { get; set; }

        /// <summary>Partition Size (Bytes)</summary>
        public ulong Size { get; set; }

        /// <summary>Partition Type</summary>
        public string Type { get; set; }

        /// <summary>Partition Attributes</summary>
        public ulong Attributes { get; set; }

        /// <summary>Whether Read-Only</summary>
        public bool IsReadOnly => (Attributes & 0x1) != 0;

        /// <summary>Whether System Partition</summary>
        public bool IsSystem => (Attributes & 0x2) != 0;

        /// <summary>
        /// Formatted Size Display
        /// </summary>
        public string SizeDisplay
        {
            get
            {
                if (Size >= 1024UL * 1024 * 1024)
                    return $"{Size / (1024.0 * 1024 * 1024):F2} GB";
                if (Size >= 1024 * 1024)
                    return $"{Size / (1024.0 * 1024):F2} MB";
                if (Size >= 1024)
                    return $"{Size / 1024.0:F2} KB";
                return $"{Size} B";
            }
        }
    }

    /// <summary>
    /// MTK Target Config
    /// </summary>
    public class MtkTargetConfig
    {
        /// <summary>Raw Config Value</summary>
        public uint RawValue { get; set; }

        /// <summary>SBC Enabled</summary>
        public bool SbcEnabled { get; set; }

        /// <summary>SLA Enabled</summary>
        public bool SlaEnabled { get; set; }

        /// <summary>DAA Enabled</summary>
        public bool DaaEnabled { get; set; }

        /// <summary>SW JTAG Enabled</summary>
        public bool SwJtagEnabled { get; set; }

        /// <summary>EPP Enabled</summary>
        public bool EppEnabled { get; set; }

        /// <summary>Root Cert Required</summary>
        public bool CertRequired { get; set; }

        /// <summary>Memory Read Auth Required</summary>
        public bool MemReadAuth { get; set; }

        /// <summary>Memory Write Auth Required</summary>
        public bool MemWriteAuth { get; set; }

        /// <summary>CMD C8 Blocked</summary>
        public bool CmdC8Blocked { get; set; }
    }

    /// <summary>
    /// MTK Flash Info
    /// </summary>
    public class MtkFlashInfo
    {
        /// <summary>Flash Type (eMMC/UFS/NAND)</summary>
        public string FlashType { get; set; }

        /// <summary>Flash Manufacturer ID</summary>
        public ushort ManufacturerId { get; set; }

        /// <summary>Flash Capacity (Bytes)</summary>
        public ulong Capacity { get; set; }

        /// <summary>Block Size</summary>
        public uint BlockSize { get; set; }

        /// <summary>Page Size</summary>
        public uint PageSize { get; set; }

        /// <summary>Flash Model</summary>
        public string Model { get; set; }

        /// <summary>
        /// Formatted Capacity Display
        /// </summary>
        public string CapacityDisplay
        {
            get
            {
                if (Capacity >= 1024UL * 1024 * 1024 * 1024)
                    return $"{Capacity / (1024.0 * 1024 * 1024 * 1024):F2} TB";
                if (Capacity >= 1024UL * 1024 * 1024)
                    return $"{Capacity / (1024.0 * 1024 * 1024):F2} GB";
                if (Capacity >= 1024 * 1024)
                    return $"{Capacity / (1024.0 * 1024):F2} MB";
                return $"{Capacity} B";
            }
        }
    }

    /// <summary>
    /// MTK Security Info
    /// </summary>
    public class MtkSecurityInfo
    {
        /// <summary>Secure Boot Enabled</summary>
        public bool SecureBootEnabled { get; set; }

        /// <summary>Whether Unfused Device</summary>
        public bool IsUnfused { get; set; }

        /// <summary>SLA Enabled</summary>
        public bool SlaEnabled { get; set; }

        /// <summary>DAA Enabled</summary>
        public bool DaaEnabled { get; set; }

        /// <summary>ME ID</summary>
        public string MeId { get; set; }

        /// <summary>SOC ID</summary>
        public string SocId { get; set; }

        /// <summary>Anti-rollback Version</summary>
        public uint AntiRollbackVersion { get; set; }

        /// <summary>Locked Status</summary>
        public bool IsLocked { get; set; }

        /// <summary>SBC Enabled</summary>
        public bool SbcEnabled { get; set; }
    }

    /// <summary>
    /// MTK Bootloader Status
    /// </summary>
    public class MtkBootloaderStatus
    {
        /// <summary>Unlocked</summary>
        public bool IsUnlocked { get; set; }

        /// <summary>Unfused Device</summary>
        public bool IsUnfused { get; set; }

        /// <summary>Secure Boot Enabled</summary>
        public bool SecureBootEnabled { get; set; }

        /// <summary>Security Version</summary>
        public uint SecurityVersion { get; set; }

        /// <summary>Device Model</summary>
        public string DeviceModel { get; set; }

        /// <summary>Status Message</summary>
        public string StatusMessage
        {
            get
            {
                if (IsUnfused)
                    return "Unfused (Development Device)";
                if (IsUnlocked)
                    return "Unlocked";
                return "Locked";
            }
        }
    }

    /// <summary>
    /// Exploit Information
    /// </summary>
    public class MtkExploitInfo
    {
        /// <summary>Connected</summary>
        public bool IsConnected { get; set; }

        /// <summary>Chip Name</summary>
        public string ChipName { get; set; }

        /// <summary>Hardware Code</summary>
        public ushort HwCode { get; set; }

        /// <summary>Exploit Type (Carbonara, AllinoneSignature, None)</summary>
        public string ExploitType { get; set; }

        /// <summary>Supports ALLINONE-SIGNATURE exploit</summary>
        public bool IsAllinoneSignatureSupported { get; set; }

        /// <summary>Supports Carbonara exploit</summary>
        public bool IsCarbonaraSupported { get; set; }

        /// <summary>List of chips supporting ALLINONE-SIGNATURE</summary>
        public MtkChipExploitInfo[] AllinoneSignatureChips { get; set; }
    }

    /// <summary>
    /// Chip Exploit Info
    /// </summary>
    public class MtkChipExploitInfo
    {
        /// <summary>Chip Name</summary>
        public string ChipName { get; set; }

        /// <summary>Hardware Code</summary>
        public ushort HwCode { get; set; }

        /// <summary>Description</summary>
        public string Description { get; set; }
    }
}
