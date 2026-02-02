// ============================================================================
// LoveAlways - UFS/eMMC Provisioning Service
// Qualcomm Storage Provisioning Service - Parse and generate provision.xml
// ============================================================================
// WARNING: Provisioning operations are irreversible and will permanently change device storage layout!
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace LoveAlways.Qualcomm.Services
{
    #region Data Models

    /// <summary>
    /// UFS Global Config
    /// </summary>
    public class UfsGlobalConfig
    {
        /// <summary>
        /// Boot Enable (bBootEnable)
        /// </summary>
        public bool BootEnable { get; set; } = true;

        /// <summary>
        /// Write Protect Type
        /// 0 = None, 1 = Power On Write Protect, 2 = Permanent Write Protect
        /// </summary>
        public int WriteProtect { get; set; } = 0;

        /// <summary>
        /// Boot LUN (bDescrAccessEn)
        /// </summary>
        public int BootLun { get; set; } = 1;

        /// <summary>
        /// Extended Config (qWriteBoosterBufferPreserveUserSpaceEn)
        /// </summary>
        public bool WriteBoosterPreserveUserSpace { get; set; } = true;

        /// <summary>
        /// Write Booster Buffer Size (dNumSharedWriteBoosterBufferAllocUnits)
        /// </summary>
        public long WriteBoosterBufferSize { get; set; } = 0;
    }

    /// <summary>
    /// UFS LUN Config
    /// </summary>
    public class UfsLunConfig
    {
        /// <summary>
        /// LUN Number
        /// </summary>
        public int LunNumber { get; set; }

        /// <summary>
        /// Is Bootable (bBootLunID)
        /// </summary>
        public bool Bootable { get; set; } = false;

        /// <summary>
        /// Size (in sectors)
        /// </summary>
        public long SizeInSectors { get; set; }

        /// <summary>
        /// Size (in KB)
        /// </summary>
        public long SizeInKB { get; set; }

        /// <summary>
        /// Sector Size (Default 4096)
        /// </summary>
        public int SectorSize { get; set; } = 4096;

        /// <summary>
        /// Memory Type (0=Normal, 1=System Code, 2=Non-Persistent, 3=Enhanced1)
        /// </summary>
        public int MemoryType { get; set; } = 0;

        /// <summary>
        /// Write Protect Group Count
        /// </summary>
        public int WriteProtectGroupNum { get; set; } = 0;

        /// <summary>
        /// Data Reliability (bDataReliability)
        /// </summary>
        public int DataReliability { get; set; } = 0;

        /// <summary>
        /// Logical Block Size
        /// </summary>
        public int LogicalBlockSize { get; set; } = 4096;

        /// <summary>
        /// Provisioning Type
        /// </summary>
        public int ProvisioningType { get; set; } = 0;
    }

    /// <summary>
    /// eMMC Config
    /// </summary>
    public class EmmcConfig
    {
        /// <summary>
        /// Boot Partition 1 Size (in 128KB units)
        /// </summary>
        public int BootPartition1Size { get; set; } = 0;

        /// <summary>
        /// Boot Partition 2 Size (in 128KB units)
        /// </summary>
        public int BootPartition2Size { get; set; } = 0;

        /// <summary>
        /// RPMB Partition Size (in 128KB units)
        /// </summary>
        public int RpmbSize { get; set; } = 0;

        /// <summary>
        /// GP Partition Sizes (in sectors)
        /// </summary>
        public long[] GpPartitionSizes { get; set; } = new long[4];

        /// <summary>
        /// Enhanced User Area Size (in sectors)
        /// </summary>
        public long EnhancedUserAreaSize { get; set; } = 0;

        /// <summary>
        /// Enhanced User Area Start Address (in sectors)
        /// </summary>
        public long EnhancedUserAreaStart { get; set; } = 0;
    }

    /// <summary>
    /// Full Provision Config
    /// </summary>
    public class ProvisionConfig
    {
        /// <summary>
        /// Storage Type (UFS or eMMC)
        /// </summary>
        public string StorageType { get; set; } = "UFS";

        /// <summary>
        /// UFS Global Config
        /// </summary>
        public UfsGlobalConfig UfsGlobal { get; set; } = new UfsGlobalConfig();

        /// <summary>
        /// UFS LUN Config List
        /// </summary>
        public List<UfsLunConfig> UfsLuns { get; set; } = new List<UfsLunConfig>();

        /// <summary>
        /// eMMC Config
        /// </summary>
        public EmmcConfig Emmc { get; set; } = new EmmcConfig();
    }

    #endregion

    /// <summary>
    /// UFS/eMMC Provisioning Service
    /// Parse and generate provision.xml config files
    /// </summary>
    public class ProvisionService
    {
        private readonly Action<string> _log;

        public ProvisionService(Action<string> log = null)
        {
            _log = log ?? (_ => { });
        }

        #region Parse provision.xml

        /// <summary>
        /// Parse provision.xml file
        /// </summary>
        public ProvisionConfig ParseProvisionXml(string xmlPath)
        {
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Provision XML file not found", xmlPath);

            string xmlContent = File.ReadAllText(xmlPath);
            return ParseProvisionXmlContent(xmlContent);
        }

        /// <summary>
        /// Parse provision.xml content
        /// </summary>
        public ProvisionConfig ParseProvisionXmlContent(string xmlContent)
        {
            var config = new ProvisionConfig();

            try
            {
                var doc = XDocument.Parse(xmlContent);
                var root = doc.Root;

                if (root == null || root.Name.LocalName != "data")
                {
                    _log("[Provision] XML format error: Missing data root element");
                    return config;
                }

                // Parse UFS config
                var ufsConfig = root.Element("ufs");
                if (ufsConfig != null)
                {
                    config.StorageType = "UFS";
                    ParseUfsConfig(ufsConfig, config);
                }

                // Parse eMMC config
                var emmcConfig = root.Element("emmc");
                if (emmcConfig != null)
                {
                    config.StorageType = "eMMC";
                    ParseEmmcConfig(emmcConfig, config);
                }

                _log(string.Format("[Provision] Parse complete: {0}, {1} LUNs",
                    config.StorageType, config.UfsLuns.Count));
            }
            catch (Exception ex)
            {
                _log(string.Format("[Provision] Parse failed: {0}", ex.Message));
            }

            return config;
        }

        private void ParseUfsConfig(XElement ufsElement, ProvisionConfig config)
        {
            // Parse global config
            var global = ufsElement.Element("global");
            if (global != null)
            {
                config.UfsGlobal.BootEnable = GetBoolAttribute(global, "bBootEnable", true);
                config.UfsGlobal.WriteProtect = GetIntAttribute(global, "bSecureWriteProtectEn", 0);
                config.UfsGlobal.BootLun = GetIntAttribute(global, "bDescrAccessEn", 1);
                config.UfsGlobal.WriteBoosterPreserveUserSpace = GetBoolAttribute(global, "qWriteBoosterBufferPreserveUserSpaceEn", true);
                config.UfsGlobal.WriteBoosterBufferSize = GetLongAttribute(global, "dNumSharedWriteBoosterBufferAllocUnits", 0);
            }

            // Parse LUN config
            foreach (var lunElement in ufsElement.Elements("lun"))
            {
                var lun = new UfsLunConfig
                {
                    LunNumber = GetIntAttribute(lunElement, "physical_partition_number", 0),
                    Bootable = GetBoolAttribute(lunElement, "bBootLunID", false),
                    SizeInSectors = GetLongAttribute(lunElement, "num_partition_sectors", 0),
                    SizeInKB = GetLongAttribute(lunElement, "size_in_KB", 0),
                    SectorSize = GetIntAttribute(lunElement, "SECTOR_SIZE_IN_BYTES", 4096),
                    MemoryType = GetIntAttribute(lunElement, "bMemoryType", 0),
                    WriteProtectGroupNum = GetIntAttribute(lunElement, "bProvisioningType", 0),
                    DataReliability = GetIntAttribute(lunElement, "bDataReliability", 0),
                    LogicalBlockSize = GetIntAttribute(lunElement, "bLogicalBlockSize", 4096)
                };

                config.UfsLuns.Add(lun);
            }
        }

        private void ParseEmmcConfig(XElement emmcElement, ProvisionConfig config)
        {
            config.Emmc.BootPartition1Size = GetIntAttribute(emmcElement, "BOOT_SIZE_MULTI1", 0);
            config.Emmc.BootPartition2Size = GetIntAttribute(emmcElement, "BOOT_SIZE_MULTI2", 0);
            config.Emmc.RpmbSize = GetIntAttribute(emmcElement, "RPMB_SIZE_MULT", 0);
            config.Emmc.EnhancedUserAreaSize = GetLongAttribute(emmcElement, "ENH_SIZE_MULT", 0);
            config.Emmc.EnhancedUserAreaStart = GetLongAttribute(emmcElement, "ENH_START_ADDR", 0);

            // Parse GP partitions
            for (int i = 0; i < 4; i++)
            {
                config.Emmc.GpPartitionSizes[i] = GetLongAttribute(emmcElement, $"GP_SIZE_MULT{i + 1}", 0);
            }
        }

        #endregion

        #region Generate provision.xml

        /// <summary>
        /// Generate UFS provision.xml content
        /// </summary>
        public string GenerateUfsProvisionXml(ProvisionConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" ?>");
            sb.AppendLine("<data>");
            sb.AppendLine("  <!--NOTE: This is an ** Alarm Auto Generated file ** and target specific-->");
            sb.AppendLine("  <!--NOTE: Modify at your own risk-->");
            sb.AppendLine();
            sb.AppendLine("  <ufs>");

            // Global config
            sb.AppendFormat("    <global bBootEnable=\"{0}\" bDescrAccessEn=\"{1}\" " +
                "bInitPowerMode=\"1\" bInitActiveICCLevel=\"0\" " +
                "bSecureRemovalType=\"0\" bConfigDescrLock=\"0\" " +
                "bSecureWriteProtectEn=\"{2}\" " +
                "qWriteBoosterBufferPreserveUserSpaceEn=\"{3}\" " +
                "dNumSharedWriteBoosterBufferAllocUnits=\"{4}\" />\n",
                config.UfsGlobal.BootEnable ? 1 : 0,
                config.UfsGlobal.BootLun,
                config.UfsGlobal.WriteProtect,
                config.UfsGlobal.WriteBoosterPreserveUserSpace ? 1 : 0,
                config.UfsGlobal.WriteBoosterBufferSize);
            sb.AppendLine();

            // LUN config
            foreach (var lun in config.UfsLuns)
            {
                sb.AppendFormat("    <lun physical_partition_number=\"{0}\" " +
                    "bBootLunID=\"{1}\" " +
                    "num_partition_sectors=\"{2}\" " +
                    "size_in_KB=\"{3}\" " +
                    "SECTOR_SIZE_IN_BYTES=\"{4}\" " +
                    "bMemoryType=\"{5}\" " +
                    "bProvisioningType=\"{6}\" " +
                    "bDataReliability=\"{7}\" " +
                    "bLogicalBlockSize=\"{8}\" />\n",
                    lun.LunNumber,
                    lun.Bootable ? 1 : 0,
                    lun.SizeInSectors,
                    lun.SizeInKB,
                    lun.SectorSize,
                    lun.MemoryType,
                    lun.ProvisioningType,
                    lun.DataReliability,
                    lun.LogicalBlockSize);
            }

            sb.AppendLine("  </ufs>");
            sb.AppendLine("</data>");

            return sb.ToString();
        }

        /// <summary>
        /// Generate eMMC provision.xml content
        /// </summary>
        public string GenerateEmmcProvisionXml(ProvisionConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" ?>");
            sb.AppendLine("<data>");
            sb.AppendLine("  <!--NOTE: This is an ** Alarm Auto Generated file ** and target specific-->");
            sb.AppendLine();
            sb.AppendFormat("  <emmc BOOT_SIZE_MULTI1=\"{0}\" BOOT_SIZE_MULTI2=\"{1}\" " +
                "RPMB_SIZE_MULT=\"{2}\" " +
                "ENH_SIZE_MULT=\"{3}\" ENH_START_ADDR=\"{4}\" " +
                "GP_SIZE_MULT1=\"{5}\" GP_SIZE_MULT2=\"{6}\" " +
                "GP_SIZE_MULT3=\"{7}\" GP_SIZE_MULT4=\"{8}\" />\n",
                config.Emmc.BootPartition1Size,
                config.Emmc.BootPartition2Size,
                config.Emmc.RpmbSize,
                config.Emmc.EnhancedUserAreaSize,
                config.Emmc.EnhancedUserAreaStart,
                config.Emmc.GpPartitionSizes[0],
                config.Emmc.GpPartitionSizes[1],
                config.Emmc.GpPartitionSizes[2],
                config.Emmc.GpPartitionSizes[3]);
            sb.AppendLine("</data>");

            return sb.ToString();
        }

        /// <summary>
        /// Save provision.xml to file
        /// </summary>
        public void SaveProvisionXml(ProvisionConfig config, string outputPath)
        {
            string content;
            if (config.StorageType == "UFS")
                content = GenerateUfsProvisionXml(config);
            else
                content = GenerateEmmcProvisionXml(config);

            File.WriteAllText(outputPath, content, Encoding.UTF8);
            _log(string.Format("[Provision] Saved: {0}", outputPath));
        }

        #endregion

        #region Default Configs

        /// <summary>
        /// Create default UFS config (Typical 8 LUN layout)
        /// </summary>
        public static ProvisionConfig CreateDefaultUfsConfig(long totalSizeGB = 256)
        {
            var config = new ProvisionConfig
            {
                StorageType = "UFS",
                UfsGlobal = new UfsGlobalConfig
                {
                    BootEnable = true,
                    BootLun = 1,
                    WriteProtect = 0,
                    WriteBoosterPreserveUserSpace = true,
                    WriteBoosterBufferSize = 0x200000 // 4GB Write Booster
                }
            };

            // Typical LUN layout
            // LUN 0: Main boot (xbl, xbl_config)
            config.UfsLuns.Add(new UfsLunConfig { LunNumber = 0, Bootable = true, SizeInKB = 8192, MemoryType = 3 });
            // LUN 1: Backup boot
            config.UfsLuns.Add(new UfsLunConfig { LunNumber = 1, Bootable = true, SizeInKB = 8192, MemoryType = 3 });
            // LUN 2: System related
            config.UfsLuns.Add(new UfsLunConfig { LunNumber = 2, Bootable = false, SizeInKB = 4096, MemoryType = 0 });
            // LUN 3: Persistent data
            config.UfsLuns.Add(new UfsLunConfig { LunNumber = 3, Bootable = false, SizeInKB = 512, MemoryType = 0 });
            // LUN 4: Main system partitions (super, userdata, etc.)
            config.UfsLuns.Add(new UfsLunConfig { LunNumber = 4, Bootable = false, SizeInKB = totalSizeGB * 1024 * 1024 - 30000, MemoryType = 0 });
            // LUN 5-7: Reserved
            for (int i = 5; i <= 7; i++)
                config.UfsLuns.Add(new UfsLunConfig { LunNumber = i, Bootable = false, SizeInKB = 0, MemoryType = 0 });

            return config;
        }

        /// <summary>
        /// Create default eMMC config
        /// </summary>
        public static ProvisionConfig CreateDefaultEmmcConfig()
        {
            return new ProvisionConfig
            {
                StorageType = "eMMC",
                Emmc = new EmmcConfig
                {
                    BootPartition1Size = 32, // 4MB
                    BootPartition2Size = 32, // 4MB
                    RpmbSize = 8,            // 1MB
                    GpPartitionSizes = new long[4]
                }
            };
        }

        #endregion

        #region Helper Methods

        private static int GetIntAttribute(XElement element, string name, int defaultValue)
        {
            var attr = element.Attribute(name);
            if (attr == null) return defaultValue;

            string value = attr.Value;

            // Support hex
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexResult))
                    return hexResult;
            }

            if (int.TryParse(value, out int result))
                return result;

            return defaultValue;
        }

        private static long GetLongAttribute(XElement element, string name, long defaultValue)
        {
            var attr = element.Attribute(name);
            if (attr == null) return defaultValue;

            string value = attr.Value;

            // 支持 16 进制
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out long hexResult))
                    return hexResult;
            }

            if (long.TryParse(value, out long result))
                return result;

            return defaultValue;
        }

        private static bool GetBoolAttribute(XElement element, string name, bool defaultValue)
        {
            var attr = element.Attribute(name);
            if (attr == null) return defaultValue;

            string value = attr.Value.ToLowerInvariant();

            if (value == "1" || value == "true" || value == "yes")
                return true;
            if (value == "0" || value == "false" || value == "no")
                return false;

            return defaultValue;
        }

        #endregion

        #region Analysis Report

        /// <summary>
        /// Generate config analysis report
        /// </summary>
        public string GenerateAnalysisReport(ProvisionConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("       Provision Config Analysis Report");
            sb.AppendLine("========================================");
            sb.AppendLine();

            sb.AppendLine(string.Format("Storage Type: {0}", config.StorageType));
            sb.AppendLine();

            if (config.StorageType == "UFS")
            {
                sb.AppendLine("[Global Config]");
                sb.AppendLine(string.Format("  Boot Enable: {0}", config.UfsGlobal.BootEnable ? "Yes" : "No"));
                sb.AppendLine(string.Format("  Boot LUN: {0}", config.UfsGlobal.BootLun));
                sb.AppendLine(string.Format("  Write Protect: {0}", GetWriteProtectDescription(config.UfsGlobal.WriteProtect)));
                sb.AppendLine(string.Format("  Write Booster: {0}",
                    config.UfsGlobal.WriteBoosterBufferSize > 0
                        ? string.Format("{0:F2} GB", config.UfsGlobal.WriteBoosterBufferSize * 4096.0 / 1024 / 1024 / 1024)
                        : "Not configured"));
                sb.AppendLine();

                sb.AppendLine("[LUN Config]");
                sb.AppendLine(string.Format("  Total {0} LUNs", config.UfsLuns.Count));
                sb.AppendLine();

                long totalSize = 0;
                foreach (var lun in config.UfsLuns.OrderBy(l => l.LunNumber))
                {
                    string sizeStr;
                    if (lun.SizeInKB >= 1024 * 1024)
                        sizeStr = string.Format("{0:F2} GB", lun.SizeInKB / 1024.0 / 1024.0);
                    else if (lun.SizeInKB >= 1024)
                        sizeStr = string.Format("{0:F2} MB", lun.SizeInKB / 1024.0);
                    else
                        sizeStr = string.Format("{0} KB", lun.SizeInKB);

                    sb.AppendLine(string.Format("  LUN {0}: {1,-12} {2} {3}",
                        lun.LunNumber,
                        sizeStr,
                        lun.Bootable ? "[BOOT]" : "      ",
                        GetMemoryTypeDescription(lun.MemoryType)));

                    totalSize += lun.SizeInKB;
                }

                sb.AppendLine();
                sb.AppendLine(string.Format("  Total Capacity: {0:F2} GB", totalSize / 1024.0 / 1024.0));
            }
            else
            {
                sb.AppendLine("[eMMC Config]");
                sb.AppendLine(string.Format("  Boot 分区 1: {0} MB", config.Emmc.BootPartition1Size * 128 / 1024));
                sb.AppendLine(string.Format("  Boot 分区 2: {0} MB", config.Emmc.BootPartition2Size * 128 / 1024));
                sb.AppendLine(string.Format("  RPMB: {0} MB", config.Emmc.RpmbSize * 128 / 1024));
            }

            sb.AppendLine();
            sb.AppendLine("========================================");

            return sb.ToString();
        }

        private static string GetWriteProtectDescription(int wp)
        {
            switch (wp)
            {
                case 0: return "No protection";
                case 1: return "Power on write protect";
                case 2: return "Permanent write protect";
                default: return string.Format("Unknown ({0})", wp);
            }
        }

        private static string GetMemoryTypeDescription(int memType)
        {
            switch (memType)
            {
                case 0: return "Normal";
                case 1: return "System Code";
                case 2: return "Non-Persistent";
                case 3: return "Enhanced (SLC)";
                default: return string.Format("Unknown ({0})", memType);
            }
        }

        #endregion
    }
}
