// LoveAlways - Device Information Service
// Supports retrieving device info from Sahara, Firehose, Super partition, build.prop and various other sources
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.Models;

namespace LoveAlways.Qualcomm.Services
{
    #region Data Models

    /// <summary>
    /// Full Device Information
    /// </summary>
    public class DeviceFullInfo
    {
        // Basic Information (Obtained via Sahara)
        public string ChipSerial { get; set; }
        public string ChipName { get; set; }
        public string HwId { get; set; }
        public string PkHash { get; set; }
        public string Vendor { get; set; }

        // Firmware Information (Obtained via Firehose/build.prop)
        public string Brand { get; set; }
        public string Model { get; set; }
        public string Product { get; set; }       // Product Codename
        public string DevProduct { get; set; }    // Device Product Name
        public string MarketName { get; set; }
        public string MarketNameEn { get; set; }
        public string MarketRegion { get; set; }  // Market Region
        public string Region { get; set; }        // Region Code
        public string DeviceCodename { get; set; }
        public string AndroidVersion { get; set; }
        public string SdkVersion { get; set; }
        public string SecurityPatch { get; set; }
        public string BuildId { get; set; }
        public string Fingerprint { get; set; }
        public string OtaVersion { get; set; }
        public string OtaVersionFull { get; set; } // New: Full firmware package name
        public string DisplayId { get; set; }
        public string BuiltDate { get; set; }     // New: Human readable build date
        public string BuildTimestamp { get; set; } // New: Unix timestamp

        // Storage Information
        public string StorageType { get; set; }
        public int SectorSize { get; set; }
        public bool IsAbDevice { get; set; }
        public string CurrentSlot { get; set; }

        // OPLUS specific info
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

        // Lenovo specific info
        public string LenovoSeries { get; set; }

        // Hardware identification info (Obtained via devinfo partition)
        public string HardwareSn { get; set; }
        public string Imei1 { get; set; }
        public string Imei2 { get; set; }

        // Information Sources
        public Dictionary<string, string> Sources { get; set; }

        public DeviceFullInfo()
        {
            ChipSerial = "";
            ChipName = "";
            HwId = "";
            PkHash = "";
            Vendor = "";
            Brand = "";
            Model = "";
            MarketName = "";
            MarketNameEn = "";
            DeviceCodename = "";
            AndroidVersion = "";
            SdkVersion = "";
            SecurityPatch = "";
            BuildId = "";
            Fingerprint = "";
            OtaVersion = "";
            DisplayId = "";
            StorageType = "";
            CurrentSlot = "";
            OplusCpuInfo = "";
            OplusNvId = "";
            OplusProject = "";
            LenovoSeries = "";
            HardwareSn = "";
            Imei1 = "";
            Imei2 = "";
            Sources = new Dictionary<string, string>();
        }

        /// <summary>
        /// Get device name for display
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(MarketName)) return MarketName;
                if (!string.IsNullOrEmpty(MarketNameEn)) return MarketNameEn;
                if (!string.IsNullOrEmpty(Brand) && !string.IsNullOrEmpty(Model))
                    return $"{Brand} {Model}";
                return Model;
            }
        }

        /// <summary>
        /// Get formatted device info summary
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(DisplayName))
                sb.AppendLine($"Device: {DisplayName}");
            if (!string.IsNullOrEmpty(Model) && Model != DisplayName)
                sb.AppendLine($"Model: {Model}");
            if (!string.IsNullOrEmpty(ChipName) && ChipName != "Unknown")
                sb.AppendLine($"Chip: {ChipName}");
            if (!string.IsNullOrEmpty(AndroidVersion))
                sb.AppendLine($"Android: {AndroidVersion}");
            if (!string.IsNullOrEmpty(OtaVersion))
                sb.AppendLine($"Version: {OtaVersion}");
            if (!string.IsNullOrEmpty(StorageType))
                sb.AppendLine($"Storage: {StorageType.ToUpper()}");
            if (!string.IsNullOrEmpty(OplusProject))
                sb.AppendLine($"Project ID: {OplusProject}");
            if (!string.IsNullOrEmpty(OplusNvId))
                sb.AppendLine($"NV ID: {OplusNvId}");
            if (!string.IsNullOrEmpty(LenovoSeries))
                sb.AppendLine($"Lenovo Series: {LenovoSeries}");
            if (!string.IsNullOrEmpty(HardwareSn))
                sb.AppendLine($"Hardware SN: {HardwareSn}");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Build.prop parse results
    /// </summary>
    public class BuildPropInfo
    {
        public string Brand { get; set; }
        public string Model { get; set; }
        public string Product { get; set; }        // Product Codename ro.product.product
        public string DevProduct { get; set; }     // Device Product Name
        public string Device { get; set; }
        public string DeviceName { get; set; }     // ro.product.name
        public string Codename { get; set; }       // ro.product.device / ro.build.product
        public string MarketName { get; set; }
        public string MarketNameEn { get; set; }
        public string MarketRegion { get; set; }   // Market Region
        public string Region { get; set; }         // Region Code
        public string Manufacturer { get; set; }
        public string AndroidVersion { get; set; }
        public string SdkVersion { get; set; }
        public string SecurityPatch { get; set; }
        public string BuildId { get; set; }
        public string Fingerprint { get; set; }
        public string DisplayId { get; set; }
        public string OtaVersion { get; set; }
        public string OtaVersionFull { get; set; }
        public string Incremental { get; set; }
        public string BuildDate { get; set; }
        public string BuildUtc { get; set; }
        public string BootSlot { get; set; }

        // OPLUS specific
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

        // Lenovo specific
        public string LenovoSeries { get; set; }

        public Dictionary<string, string> AllProperties { get; set; }

        public BuildPropInfo()
        {
            Brand = "";
            Model = "";
            Device = "";
            DeviceName = "";
            Codename = "";
            MarketName = "";
            MarketNameEn = "";
            Manufacturer = "";
            AndroidVersion = "";
            SdkVersion = "";
            SecurityPatch = "";
            BuildId = "";
            Fingerprint = "";
            DisplayId = "";
            OtaVersion = "";
            OtaVersionFull = "";
            Incremental = "";
            BuildDate = "";
            BuildUtc = "";
            BootSlot = "";
            OplusCpuInfo = "";
            OplusNvId = "";
            OplusProject = "";
            LenovoSeries = "";
            AllProperties = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// LP Partition Info (Enhanced version - supports precise physical offset)
    /// </summary>
    public class LpPartitionInfo
    {
        public string Name { get; set; }
        public uint Attrs { get; set; }
        public long RelativeSector { get; set; } // 512B sector offset relative to super start
        public long AbsoluteSector { get; set; } // Absolute physical sector on disk (converted according to physicalSectorSize)
        public long SizeInSectors { get; set; }  // Partition size (number of sectors)
        public long Size { get; set; }           // Partition size (bytes)
        public string FileSystem { get; set; }

        public LpPartitionInfo()
        {
            Name = "";
            FileSystem = "unknown";
        }
    }

    /// <summary>
    /// Image mapping block info (Used for precise offset reading)
    /// </summary>
    public class ImageMapBlock
    {
        public long BlockIndex { get; set; }
        public long BlockCount { get; set; }
        public long FileOffset { get; set; } // Offset in the .img file
    }

    /// <summary>
    /// Image map table parser (For OPPO/Realme firmware .map files)
    /// </summary>
    public class ImageMapParser
    {
        public List<ImageMapBlock> ParseMapFile(string mapPath, int blockSize = 4096)
        {
            var blocks = new List<ImageMapBlock>();
            if (!File.Exists(mapPath)) return blocks;

            try
            {
                var lines = File.ReadAllLines(mapPath);
                long currentFileOffset = 0;

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    
                    // Format is usually: start_block block_count
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        long start;
                        long count;
                        if (long.TryParse(parts[0], out start) && long.TryParse(parts[1], out count))
                        {
                            blocks.Add(new ImageMapBlock
                            {
                                BlockIndex = start,
                                BlockCount = count,
                                FileOffset = currentFileOffset
                            });
                            currentFileOffset += count * blockSize;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageMapParser] Parse block map exception: {ex.Message}");
            }
            return blocks;
        }
    }

    #endregion

    /// <summary>
    /// Device Info Service - Supports retrieving device info from various sources
    /// </summary>
    public class DeviceInfoService
    {
        // LP Metadata 常量
        private const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467;  // "gDla"
        private const uint LP_METADATA_HEADER_MAGIC = 0x41680530;    // Standard: "0\x05hA"
        private const uint LP_METADATA_HEADER_MAGIC_ALP0 = 0x414c5030; // Lenovo: "0PLA"
        private const ushort EXT4_MAGIC = 0xEF53;
        private const uint EROFS_MAGIC = 0xE0F5E1E2;

        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;

        public DeviceInfoService(Action<string> log = null, Action<string> logDetail = null)
        {
            _log = log ?? delegate { };
            _logDetail = logDetail ?? delegate { };
        }

        #region Build.prop Parsing

        /// <summary>
        /// Parse device info from build.prop file path
        /// </summary>
        public BuildPropInfo ParseBuildPropFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _log($"File not found: {filePath}");
                    return null;
                }

                var content = File.ReadAllText(filePath, Encoding.UTF8);
                return ParseBuildProp(content);
            }
            catch (Exception ex)
            {
                _log($"Parse build.prop failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse device info from build.prop content (Enhanced version - supports property block detection)
        /// </summary>
        public BuildPropInfo ParseBuildProp(string content)
        {
            var info = new BuildPropInfo();
            if (string.IsNullOrEmpty(content)) return info;

            // Include namespaces required for regular expressions
            // Note: Regex is in System.Text.RegularExpressions
            
            // If content contains many non-printable characters, it might be raw partition data
            // We need to extract valid lines (Enhanced regex)
            string[] lines;
            if (content.Contains("\0"))
            {
                var list = new List<string>();
                // 1. Match standard ro. properties
                var matches = System.Text.RegularExpressions.Regex.Matches(content, @"(ro|display|persist)\.[a-zA-Z0-9._-]+=[^\r\n\x00\s]+");
                foreach (System.Text.RegularExpressions.Match m in matches) list.Add(m.Value);
                
                // 2. Match properties specific to OPLUS/Lenovo/Xiaomi/ZTE
                var oplusMatches = System.Text.RegularExpressions.Regex.Matches(content, @"(separate\.soft|region|date\.utc|ro\.build\.oplus_nv_id|display\.id\.show|ro\.lenovo\.series|ro\.lenovo\.cpuinfo|ro\.system_ext\.build\.version\.incremental|ro\.zui\.version|ro\.miui\.ui\.version\.name|ro\.miui\.ui\.version\.code|ro\.miui\.region|ro\.build\.MiFavor_version|ro\.build\.display\.id)=[^\r\n\x00\s]+");
                foreach (System.Text.RegularExpressions.Match m in oplusMatches) list.Add(m.Value);
                
                lines = list.ToArray();
            }
            else
            {
                lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;

                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();

                // Remove possible trailing gibberish (common in EROFS raw block extraction)
                if (value.Length > 0 && (value[value.Length - 1] < 32 || value[value.Length - 1] > 126))
                {
                    value = value.TrimEnd('\0', '\r', '\n', '\t', ' ');
                }

                if (string.IsNullOrEmpty(value)) continue;

                info.AllProperties[key] = value;

                // Core property mapping
                switch (key)
                {
                    case "ro.product.vendor.brand":
                    case "ro.product.brand":
                    case "ro.product.manufacturer":
                        if (string.IsNullOrEmpty(info.Brand) || value != "oplus")
                            info.Brand = value;
                        break;
                    
                    // OPLUS Market Name (Highest priority)
                    case "ro.vendor.oplus.market.name":
                        // This is the most accurate market name, e.g., "OnePlus 12"
                        info.MarketName = value;
                        break;

                    case "ro.vendor.oplus.market.enname":
                        // English market name, e.g., "OnePlus 12", as a fallback
                        if (string.IsNullOrEmpty(info.MarketName))
                            info.MarketName = value;
                        break;

                    case "ro.product.marketname":
                    case "ro.product.vendor.marketname":
                    case "ro.product.odm.marketname":
                        // Market names from other manufacturers
                        if (string.IsNullOrEmpty(info.MarketName))
                            info.MarketName = value;
                        break;

                    case "ro.product.model":
                    case "ro.product.vendor.model":
                    case "ro.product.odm.model":
                    case "ro.product.odm.cert":
                    case "ro.lenovo.series":
                        if (string.IsNullOrEmpty(info.Model) || value.Length > info.Model.Length || key == "ro.lenovo.series")
                        {
                            // If it contains Y700 or Legion, it has the highest priority
                            if (value.Contains("Y700") || value.Contains("Legion"))
                                info.MarketName = value;
                            else
                                info.Model = value;
                        }
                        break;

                    case "ro.miui.ui.version.name":
                        // MIUI/HyperOS version name: V14.0.x.x or OS1.0.x.x
                        // Note: This property might only have a short version like "V125", needs to work with ro.build.display.id
                        if (string.IsNullOrEmpty(info.OtaVersion))
                            info.OtaVersion = value;
                        // Detect HyperOS version
                        if (value.Contains("OS3.")) info.AndroidVersion = "16.0";
                        else if (value.Contains("OS2.")) info.AndroidVersion = "15.0";
                        else if (value.Contains("OS1.")) info.AndroidVersion = "14.0";
                        break;

                    case "ro.miui.ui.version.code":
                        // Version code, lower priority
                        if (string.IsNullOrEmpty(info.OtaVersion))
                            info.OtaVersion = value;
                        break;
                    
                    // Full incremental version number (Xiaomi: V14.0.8.0.TNJCNXM, General: eng.xxx.20240101)
                    case "ro.build.version.incremental":
                    case "ro.system.build.version.incremental":
                    case "ro.vendor.build.version.incremental":
                        // Always save Incremental
                        if (string.IsNullOrEmpty(info.Incremental))
                            info.Incremental = value;
                        
                        // Xiaomi device full version number, e.g., "V14.0.8.0.TNJCNXM" or "OS1.0.x.x"
                        if (!string.IsNullOrEmpty(value))
                        {
                            // Xiaomi MIUI/HyperOS versions
                            if (value.StartsWith("V") || value.StartsWith("OS"))
                            {
                                if (string.IsNullOrEmpty(info.OtaVersion) || info.OtaVersion.Length < value.Length)
                                    info.OtaVersion = value;
                            }
                            // Other devices: if it contains a full version number format
                            else if (value.Contains(".") && value.Length > 8)
                            {
                                if (string.IsNullOrEmpty(info.OtaVersion))
                                    info.OtaVersion = value;
                            }
                        }
                        break;

                    case "ro.build.MiFavor_version":
                        // ZTE NebulaOS/MiFavor version
                        if (string.IsNullOrEmpty(info.OtaVersion)) info.OtaVersion = value;
                        break;

                    // OPLUS display version (Highest priority) - e.g., "PJD110_14.0.0.801(CN01)"
                    case "ro.build.display.id.show":
                        // This is the most accurate display version, use directly
                        info.OtaVersion = value;
                        info.OtaVersionFull = value;
                        break;

                    // OPLUS full OTA package name - e.g., "PJD110domestic_11_14.0.0.801(CN01)_2024051322460079"
                    case "ro.build.display.full_id":
                        info.OtaVersionFull = value;
                        // If OtaVersion is not set, extract simplified version from full package name
                        if (string.IsNullOrEmpty(info.OtaVersion))
                        {
                            // Extract 14.0.0.801(CN01) part
                            var m = Regex.Match(value, @"(\d+\.\d+\.\d+\.\d+)\(([A-Z]{2}\d+)\)");
                            if (m.Success)
                                info.OtaVersion = string.Format("{0}({1})", m.Groups[1].Value, m.Groups[2].Value);
                        }
                        break;

                    case "ro.build.version.ota":
                        // OPLUS full OTA version: PJD110_11.A.70_0700_202405132246
                        // Only as a fallback for OtaVersionFull, do not overwrite OtaVersion
                        if (string.IsNullOrEmpty(info.OtaVersionFull))
                            info.OtaVersionFull = value;
                        break;

                    case "ro.build.display.id":
                    case "ro.system_ext.build.version.incremental":
                    case "ro.vendor.build.display.id":
                        // Always save DisplayId
                        if (string.IsNullOrEmpty(info.DisplayId) && key == "ro.build.display.id")
                            info.DisplayId = value;
                        
                        // If ro.build.display.id.show is already set (contains parentheses), skip OtaVersion setting
                        if (!string.IsNullOrEmpty(info.OtaVersion) && info.OtaVersion.Contains("("))
                            break;
                        
                        // Lenovo ZUIOS handling: TB321FU_CN_OPEN_USER_Q00011.0_V_ZUI_17.0.10.308_ST_251030
                        if (value.Contains("ZUI") || value.Contains("ZUXOS"))
                        {
                            info.OtaVersionFull = value;
                            // Extract simplified: 17.0.10.308
                            var m = Regex.Match(value, @"\d+\.\d+\.\d+\.\d+");
                            if (m.Success) info.OtaVersion = m.Value;
                        }
                        // If it's Nubia/RedMagic, this field usually contains RedMagicOSxx or NebulaOS
                        else if (value.Contains("RedMagic") || value.Contains("Nebula"))
                        {
                            info.OtaVersion = value;
                        }
                        // OPLUS devices: Full OTA version Usually in format PKG110_14.0.0.801(CN01)
                        else if (value.Contains("(") && value.Contains(")") && (value.Contains("CN") || value.Contains("GL") || value.Contains("EU") || value.Contains("IN")))
                        {
                            info.OtaVersionFull = value;
                            info.OtaVersion = value; // Use full format directly
                        }
                        // Xiaomi MIUI/HyperOS version format: V14.0.8.0.TNJCNXM or OS1.0.x.x
                        else if (value.StartsWith("V") || value.StartsWith("OS"))
                        {
                            if (string.IsNullOrEmpty(info.OtaVersion) || info.OtaVersion.Length < value.Length)
                                info.OtaVersion = value;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(info.OtaVersion))
                                info.OtaVersion = value;
                        }
                        break;
                    
                    // OPLUS device specific properties
                    case "ro.vendor.oplus.ota.version":
                    case "ro.oem.version":
                        // OPLUS full OTA version
                        if (!string.IsNullOrEmpty(value))
                        {
                            info.OtaVersionFull = value;
                            // If current OtaVersion is just a simple number, use this more complete version
                            if (string.IsNullOrEmpty(info.OtaVersion) || !info.OtaVersion.Contains("."))
                                info.OtaVersion = value;
                        }
                        break;
                    case "display.id.show":
                    case "region": // Map region to OtaVersion for UI display convenience
                        if (key == "display.id.show" || key == "region" || string.IsNullOrEmpty(info.OtaVersion))
                        {
                            // If it's display.id.show and contains (CNxx), this is the most accurate display version
                            if (key == "display.id.show" && value.Contains("(") && value.Contains(")"))
                            {
                                info.OtaVersion = value;
                            }
                            else if (string.IsNullOrEmpty(info.OtaVersion))
                            {
                                info.OtaVersion = value;
                            }
                        }
                        break;
                    
                    case "ro.build.oplus_nv_id":
                        info.OplusNvId = value;
                        break;
                    
                    // OPLUS Project ID (multiple sources, in order of priority)
                    case "ro.oplus.image.my_product.type":
                        // Most accurate Project ID source (e.g., 22825)
                        info.OplusProject = value;
                        break;
                    
                    case "ro.separate.soft":
                    case "ro.product.supported_versions":
                        // Fallback Project ID source
                        if (string.IsNullOrEmpty(info.OplusProject))
                            info.OplusProject = value;
                        break;
                    
                    // OPLUS ROM Version
                    case "ro.build.version.oplusrom":
                    case "ro.build.version.oplusrom.display":
                    case "ro.build.version.oplusrom.confidential":
                        if (!info.AllProperties.ContainsKey("oplus_rom_version"))
                            info.AllProperties["oplus_rom_version"] = value;
                        break;
                    
                    // OPLUS Region
                    case "ro.oplus.image.my_region.type":
                    case "ro.oplus.pipeline_key":
                        if (!info.AllProperties.ContainsKey("oplus_region"))
                            info.AllProperties["oplus_region"] = value;
                        break;
                    
                    case "ro.lenovo.cpuinfo":
                        info.OplusCpuInfo = value; // Borrow OplusCpuInfo to store CPU info
                        break;

                    case "ro.build.date":
                        info.BuildDate = value;
                        break;

                    case "ro.build.date.utc":
                        info.BuildUtc = value;
                        break;

                    case "ro.build.version.release":
                    case "ro.build.version.release_or_codename":
                    case "ro.vendor.build.version.release":
                    case "ro.vendor.build.version.release_or_codename":
                    case "ro.odm.build.version.release":
                    case "ro.product.build.version.release":
                    case "ro.system.build.version.release":
                        if (string.IsNullOrEmpty(info.AndroidVersion))
                            info.AndroidVersion = value;
                        break;
                    
                    case "ro.build.version.sdk":
                    case "ro.vendor.build.version.sdk":
                    case "ro.system.build.version.sdk":
                        if (string.IsNullOrEmpty(info.SdkVersion))
                            info.SdkVersion = value;
                        break;
                    
                    case "ro.build.version.security_patch":
                    case "ro.vendor.build.version.security_patch":
                    case "ro.system.build.version.security_patch":
                        if (string.IsNullOrEmpty(info.SecurityPatch))
                            info.SecurityPatch = value;
                        break;
                    
                    case "ro.product.device":
                    case "ro.product.vendor.device":
                    case "ro.product.odm.device":
                    case "ro.product.system.device":
                    case "ro.build.product":
                    case "ro.product.board":
                        if (string.IsNullOrEmpty(info.Codename))
                            info.Codename = value;
                        // Also set Device field as a fallback                        if (string.IsNullOrEmpty(info.Device))
                            info.Device = value;
                        break;
                    
                    case "ro.build.id":
                        info.BuildId = value;
                        break;
                    
                    case "ro.build.fingerprint":
                    case "ro.system.build.fingerprint":
                    case "ro.vendor.build.fingerprint":
                        if (string.IsNullOrEmpty(info.Fingerprint))
                            info.Fingerprint = value;
                        break;
                    
                    case "ro.product.name":
                    case "ro.product.vendor.name":
                        if (string.IsNullOrEmpty(info.DeviceName))
                            info.DeviceName = value;
                        // If no Codename, product.name is usually the device codename too                        if (string.IsNullOrEmpty(info.Codename) && !value.Contains(" "))
                            info.Codename = value;
                        break;
                }
            }
            
            // If still no Codename, try extracting from Fingerprint
            // Fingerprint format: Brand/device/device:version/... e.g. Xiaomi/polaris/polaris:10/...            if (string.IsNullOrEmpty(info.Codename) && !string.IsNullOrEmpty(info.Fingerprint))
            {
                var parts = info.Fingerprint.Split('/');
                if (parts.Length >= 3)
                {
                    // Second and third parts usually contain device codename
                    string candidate = parts[1]; // Usually device codename
                    if (!string.IsNullOrEmpty(candidate) && !candidate.Contains(" ") && candidate.Length > 2)
                    {
                        info.Codename = candidate;
                    }
                }
            }

            return info;
        }

        #endregion

        #region LP Metadata Parsing

        /// <summary>
        /// Device read delegate - used to read data from 9008 device at specified offset
        /// </summary>
        public delegate byte[] DeviceReadDelegate(long offsetInSuper, int size);

        /// <summary>
        /// Parse LP Metadata - Read from device on demand (Precise offset version)
        /// </summary>
        /// <param name="readFromDevice">Read delegate (Reading from super partition start)</param>
        /// <param name="superStartSector">Physical start sector of super partition (Obtained from GPT)</param>
        /// <param name="physicalSectorSize">Device physical sector size (Usually 4096 or 512)</param>
        public List<LpPartitionInfo> ParseLpMetadataFromDevice(DeviceReadDelegate readFromDevice, long superStartSector = 0, int physicalSectorSize = 512)
        {
            try
            {
                // 1. Read Geometry (Offset 0x1000 = 4096, Size 4096)
                var geometryData = readFromDevice(4096, 4096);
                if (geometryData == null || geometryData.Length < 52)
                {
                    _log("Cannot read LP Geometry");
                    return null;
                }

                uint magic = BitConverter.ToUInt32(geometryData, 0);
                if (magic != LP_METADATA_GEOMETRY_MAGIC)
                {
                    _log("Invalid LP Geometry magic");
                    return null;
                }

                uint metadataMaxSize = BitConverter.ToUInt32(geometryData, 40);
                uint metadataSlotCount = BitConverter.ToUInt32(geometryData, 44);
                
                // 2. Attempt to find active Metadata Header
                // Possible offsets: 8192 (Slot0, 512B sector), 12288 (Slot0, 4KB sector), 4096 (Early)
                long[] possibleOffsets = { 8192, 12288, 4096, 16384 };
                byte[] metadataData = null;
                uint headerMagic = 0;
                long finalOffset = 0;

                foreach (var offset in possibleOffsets)
                {
                    metadataData = readFromDevice(offset, 4096); // Read 4KB for probing first
                    if (metadataData == null || metadataData.Length < 256) continue;

                    headerMagic = BitConverter.ToUInt32(metadataData, 0);
                    if (headerMagic == LP_METADATA_HEADER_MAGIC || headerMagic == LP_METADATA_HEADER_MAGIC_ALP0)
                    {
                        finalOffset = offset;
                        break;
                    }
                }

                if (finalOffset == 0)
                {
                    _log("Cannot find valid LP Metadata Header");
                    return null;
                }

                // Read complete metadata (According to size in header)
                uint headerSize = BitConverter.ToUInt32(metadataData, 8);
                uint tablesSize = BitConverter.ToUInt32(metadataData, 24);
                int totalToRead = (int)(headerSize + tablesSize);
                
                _logDetail(string.Format("[LP] Header Offset={0}, headerSize={1}, tablesSize={2}, Need to read={3} bytes", 
                    finalOffset, headerSize, tablesSize, totalToRead));
                
                if (totalToRead > metadataData.Length)
                {
                    // Limit single read size to prevent timeout                    if (totalToRead > 1024 * 1024)
                    {
                        _log(string.Format("LP Metadata too large ({0} bytes), limited to 1MB", totalToRead));
                        totalToRead = 1024 * 1024;
                    }
                    
                    metadataData = readFromDevice(finalOffset, totalToRead);
                    if (metadataData == null)
                    {
                        _log(string.Format("Cannot read LP Metadata (Offset={0}, Size={1})", finalOffset, totalToRead));
                        return null;
                    }
                    if (metadataData.Length < totalToRead)
                    {
                        _log(string.Format("LP Metadata read incomplete: Expected {0} bytes, actual {1} bytes", totalToRead, metadataData.Length));
                        // Try to continue parsing with the data read so far
                        if (metadataData.Length < headerSize)
                        {
                            return null;
                        }
                    }
                }

                int tablesBase = (int)headerSize;

                // 3. Parse table descriptors (Offset 0x50)
                int tablesOffset = 0x50;
                uint partOffset = BitConverter.ToUInt32(metadataData, tablesOffset);
                uint partNum = BitConverter.ToUInt32(metadataData, tablesOffset + 4);
                uint partEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 8);
                uint extOffset = BitConverter.ToUInt32(metadataData, tablesOffset + 12);
                uint extNum = BitConverter.ToUInt32(metadataData, tablesOffset + 16);
                uint extEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 20);

                // 4. Parse extents (Physical mapping)
                var extents = new List<Tuple<long, long>>(); // <NumSectors, PhysicalBlockOffset(512B units)>
                for (int i = 0; i < extNum; i++)
                {
                    int entryOffset = tablesBase + (int)extOffset + i * (int)extEntrySize;
                    if (entryOffset + 12 > metadataData.Length) break;

                    long numSectors = BitConverter.ToInt64(metadataData, entryOffset);
                    long targetData = BitConverter.ToInt64(metadataData, entryOffset + 12);
                    extents.Add(Tuple.Create(numSectors, targetData));
                }

                // 5. Parse partitions and convert to physical sectors
                var partitions = new List<LpPartitionInfo>();
                for (int i = 0; i < partNum; i++)
                {
                    int entryOffset = tablesBase + (int)partOffset + i * (int)partEntrySize;
                    if (entryOffset + partEntrySize > metadataData.Length) break;

                    string name = Encoding.UTF8.GetString(metadataData, entryOffset, 36).TrimEnd('\0');
                    if (string.IsNullOrEmpty(name)) continue;

                    uint attrs = BitConverter.ToUInt32(metadataData, entryOffset + 36);
                    uint firstExtent = BitConverter.ToUInt32(metadataData, entryOffset + 40);
                    uint numExtents = BitConverter.ToUInt32(metadataData, entryOffset + 44);

                    if (numExtents > 0 && firstExtent < extents.Count)
                    {
                        var ext = extents[(int)firstExtent];
                        
                        // [Precise calculation logic]
                        // targetData is the offset within LP based on 512 bytes
                        // We need to convert it to the absolute sector of the physical disk
                        long relativeSector = ext.Item2; // 512B sector offset
                        long absoluteSector = superStartSector + (relativeSector * 512 / physicalSectorSize);

                        var lp = new LpPartitionInfo
                        {
                            Name = name,
                            Attrs = attrs,
                            RelativeSector = relativeSector,
                            AbsoluteSector = absoluteSector,
                            SizeInSectors = ext.Item1 * 512 / physicalSectorSize,
                            Size = ext.Item1 * 512
                        };

                        partitions.Add(lp);
                        _logDetail(string.Format("Logical Partition [{0}]: Physical Sector={1}, Size={2}MB", 
                            lp.Name, lp.AbsoluteSector, lp.Size / 1024 / 1024));
                    }
                }

                return partitions;
            }
            catch (Exception ex)
            {
                _log($"Parse LP Metadata failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Detect file system type
        /// </summary>
        public string DetectFileSystem(byte[] data)
        {
            if (data == null || data.Length < 512) 
            {
                _log(string.Format("  Data too short: {0} bytes", data?.Length ?? 0));
                return "unknown";
            }

            // Print debug info
            string debugInfo = "";
            if (data.Length >= 4)
            {
                debugInfo += string.Format("@0={0:X2}{1:X2}{2:X2}{3:X2}", 
                    data[0], data[1], data[2], data[3]);
            }
            if (data.Length >= 1028)
            {
                debugInfo += string.Format(" @1024={0:X2}{1:X2}{2:X2}{3:X2}", 
                    data[1024], data[1025], data[1026], data[1027]);
            }
            if (data.Length >= 1082)
            {
                // EXT4 magic position: 1024 + 56 = 1080
                debugInfo += string.Format(" @1080={0:X2}{1:X2}", 
                    data[1080], data[1081]);
            }
            _logDetail(string.Format("  Magic: {0}", debugInfo));

            // Check Sparse image header (0xED26FF3A)
            if (data.Length >= 4)
            {
                uint magic0 = BitConverter.ToUInt32(data, 0);
                if (magic0 == 0xED26FF3A)
                {
                    _log("  → Sparse image format");
                    return "sparse";
                }
            }

            // EROFS: superblock at offset 1024, magic = 0xE0F5E1E2 (little-endian)
            if (data.Length >= 1024 + 4)
            {
                uint erofsAt1024 = BitConverter.ToUInt32(data, 1024);
                if (erofsAt1024 == EROFS_MAGIC)
                {
                    return "erofs";
                }
            }

            // EROFS at offset 0 (some special cases)
            if (data.Length >= 4)
            {
                uint erofsAt0 = BitConverter.ToUInt32(data, 0);
                if (erofsAt0 == EROFS_MAGIC)
                {
                    _log("  → EROFS at offset 0");
                    return "erofs_raw";
                }
            }

            // EXT4: superblock at offset 1024, magic at offset 56 (0xEF53)
            if (data.Length >= 1024 + 58)
            {
                ushort ext4Magic = BitConverter.ToUInt16(data, 1024 + 56);
                if (ext4Magic == EXT4_MAGIC) 
                {
                    return "ext4";
                }
            }

            // F2FS: magic at offset 1024
            if (data.Length >= 1024 + 4)
            {
                uint f2fsMagic = BitConverter.ToUInt32(data, 1024);
                if (f2fsMagic == 0xF2F52010) return "f2fs";
            }

            // SquashFS: magic = 0x73717368 ("hsqs") 或 0x68737173 ("sqsh")
            if (data.Length >= 4)
            {
                uint sqshMagic = BitConverter.ToUInt32(data, 0);
                if (sqshMagic == 0x73717368 || sqshMagic == 0x68737173) return "squashfs";
            }

            // Detect Android Boot Image (boot, recovery)
            if (data.Length >= 8)
            {
                // ANDROID! magic
                if (data[0] == 'A' && data[1] == 'N' && data[2] == 'D' && data[3] == 'R' &&
                    data[4] == 'O' && data[5] == 'I' && data[6] == 'D' && data[7] == '!')
                {
                    return "android_boot";
                }
            }

            // Detect AVB (Android Verified Boot) footer - might be at the end of the partition
            // AVB signed partitions usually have a special header structure

            // Detect possible encrypted or signed partitions (Xiaomi etc. might use these)
            if (data.Length >= 4)
            {
                // Check if it's all 0x00 (empty partition)
                bool allZero = true;
                for (int i = 0; i < Math.Min(64, data.Length); i++)
                {
                    if (data[i] != 0) { allZero = false; break; }
                }
                if (allZero)
                {
                    _log("  → Partition data is empty");
                    return "empty";
                }
                
                // Detect Xiaomi specific signature header (S72_, S27_ etc.)
                // These signature headers indicate the partition has signature/AVB data, real filesystem follows
                if (data.Length >= 4)
                {
                    char c0 = (char)data[0], c1 = (char)data[1], c2 = (char)data[2], c3 = (char)data[3];
                    // 检查是否看起来像签名头 (ASCII 字母/数字/下划线开头)
                    bool looksLikeSignature = (c0 >= 'A' && c0 <= 'Z') || (c0 >= '0' && c0 <= '9');
                    if (looksLikeSignature && (c3 == '_' || c2 == '_'))
                    {
                        _logDetail(string.Format("  → Signature header detected: {0}{1}{2}{3} (Filesystem might be after)", c0, c1, c2, c3));
                        return "signed";  // Return signed means we need to probe at an offset
                    }
                }
            }

            return "unknown";
        }

        #endregion

        #region 从设备在线读取 build.prop

        /// <summary>
        /// Read build.prop from device online (Adapts to all Android versions)
        /// </summary>
        /// <param name="readPartition">Delegate to read data from specified partition name</param>
        /// <param name="activeSlot">Current active slot (a/b)</param>
        /// <param name="hasSuper">Whether super partition exists</param>
        /// <param name="superStartSector">Super physical start sector</param>
        /// <param name="physicalSectorSize">Sector size</param>
        /// <param name="vendorName">Device manufacturer name (Optional, for filtering partitions)</param>
        /// <returns>Parsed build.prop information</returns>
        public async Task<BuildPropInfo> ReadBuildPropFromDevice(
            Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot = "", 
            bool hasSuper = true,
            long superStartSector = 0,
            int physicalSectorSize = 512,
            string vendorName = "")
        {
            try
            {
                BuildPropInfo finalInfo = null;

                if (hasSuper)
                {
                    _log("Parsing build.prop from Super partition logical volumes...");
                    
                    // Use Task.Run to execute sync operation on thread pool, avoiding UI thread deadlock
                    // Set 5 second timeout to prevent hanging
                    var superReadTask = Task.Run(() => 
                    {
                        // Wrap delegate to match LP parser requirements (reading from super partition offset)
                        DeviceReadDelegate readFromSuper = (offset, size) => {
                            try
                            {
                                var task = readPartition("super", offset, size);
                                // Use Timeout Wait to prevent single read hanging
                                if (!task.Wait(TimeSpan.FromSeconds(10)))
                                {
                                    return null;
                                }
                                return task.Result;
                            }
                            catch (Exception ex)
                            {
                                _logDetail($"Super partition read exception: {ex.Message}");
                                return null;
                            }
                        };
                        return ReadBuildPropFromSuper(readFromSuper, activeSlot, superStartSector, physicalSectorSize);
                    });
                    
                    // Global timeout 30 seconds
                    var completedTask = await Task.WhenAny(superReadTask, Task.Delay(30000)).ConfigureAwait(false);
                    if (completedTask == superReadTask)
                    {
                        // 任务已完成，安全获取结果
                        finalInfo = await superReadTask.ConfigureAwait(false);
                    }
                    else
                    {
                        _log("Read from Super partition timeout (30s), skipping");
                    }
                }

                // If basic info already obtained from Super, only scan enhanced partitions
                // If Super read failed, scan all physical partitions
                bool hasBasicInfo = finalInfo != null && 
                    (!string.IsNullOrEmpty(finalInfo.Model) || !string.IsNullOrEmpty(finalInfo.MarketName));
                
                if (hasBasicInfo)
                {
                    _log("Basic info obtained from Super, skipping physical partition scan");
                    return finalInfo;
                }

                _log("Scanning physical partitions to extract build.prop...");
                var searchPartitions = new List<string>();
                
                // Filter invalid slot values
                string normalizedSlot = activeSlot;
                if (string.IsNullOrEmpty(normalizedSlot) || 
                    normalizedSlot == "undefined" || 
                    normalizedSlot == "unknown" || 
                    normalizedSlot == "nonexistent")
                {
                    normalizedSlot = "";
                }
                string slotSuffix = string.IsNullOrEmpty(normalizedSlot) ? "" : "_" + normalizedSlot.ToLower().TrimStart('_');

                // Traditional partition structure: Prioritize scanning system/vendor partitions
                if (!hasSuper)
                {
                    if (!string.IsNullOrEmpty(slotSuffix))
                    {
                        searchPartitions.Add("system" + slotSuffix);
                        searchPartitions.Add("vendor" + slotSuffix);
                    }
                    searchPartitions.Add("system");
                    searchPartitions.Add("vendor");
                }
                
                // Other standalone physical partitions
                if (!string.IsNullOrEmpty(slotSuffix))
                {
                    searchPartitions.Add("my_manifest" + slotSuffix);
                }
                searchPartitions.Add("my_manifest");
                searchPartitions.Add("cust");
                searchPartitions.Add("lenovocust");
                
                // If it's an old device without super, try more partitions
                if (!hasSuper)
                {
                    // Old Xiaomi devices might have persist partition containing device info
                    searchPartitions.Add("persist");
                    // product partition might contain build.prop
                    if (!string.IsNullOrEmpty(slotSuffix))
                        searchPartitions.Add("product" + slotSuffix);
                    searchPartitions.Add("product");
                    // odm partition
                    if (!string.IsNullOrEmpty(slotSuffix))
                        searchPartitions.Add("odm" + slotSuffix);
                    searchPartitions.Add("odm");
                }
                
                _log(string.Format("  Will scan {0} partitions", searchPartitions.Count));

                foreach (var partName in searchPartitions)
                {
                    var info = await ReadBuildPropFromStandalonePartition(partName, readPartition);
                    if (info != null)
                    {
                        if (finalInfo == null) finalInfo = info;
                        else MergeProperties(finalInfo, info);
                        
                        // If core model already obtained, end early
                        if (!string.IsNullOrEmpty(finalInfo.MarketName) || !string.IsNullOrEmpty(finalInfo.Model))
                            break;
                    }
                }
                return finalInfo;
            }
            catch (Exception ex)
            {
                _log(string.Format("Read build.prop overall process failed: {0}", ex.Message));
            }
            return null;
        }

        /// <summary>
        /// Read build.prop from Super partition logical volumes (Precise merge mode)
        /// </summary>
        private BuildPropInfo ReadBuildPropFromSuper(DeviceReadDelegate readFromSuper, string activeSlot = "", long superStartSector = 0, int physicalSectorSize = 512)
        {
            var masterInfo = new BuildPropInfo();
            try
            {
                // 1. Parse LP Metadata
                var lpPartitions = ParseLpMetadataFromDevice(readFromSuper, superStartSector, physicalSectorSize);
                if (lpPartitions == null || lpPartitions.Count == 0) return null;

                // Filter invalid slot values
                string normalizedSlot = activeSlot;
                if (string.IsNullOrEmpty(normalizedSlot) || 
                    normalizedSlot == "undefined" || 
                    normalizedSlot == "unknown" || 
                    normalizedSlot == "nonexistent")
                {
                    normalizedSlot = "";
                }
                string slotSuffix = string.IsNullOrEmpty(normalizedSlot) ? "" : "_" + normalizedSlot.ToLower().TrimStart('_');
                
                // 2. Priority sorting: Read from low to high, high priority overwrites low priority
                // Order: System -> System_ext -> Product -> Vendor -> ODM -> My_manifest
                var searchList = new List<string> { "system", "system_ext", "product", "vendor", "odm", "my_manifest" };
                
                foreach (var baseName in searchList)
                {
                    // Try with and without slot suffix
                    var possibleNames = new[] { baseName + slotSuffix, baseName };
                    foreach (var name in possibleNames)
                    {
                        var targetPartition = lpPartitions.FirstOrDefault(p => p.Name == name);
                        if (targetPartition == null) continue;

                        _log(string.Format("Parsing build.prop from logical volume {0} (Physical sector: {1})...", 
                            targetPartition.Name, targetPartition.AbsoluteSector));
                        
                        // Convert relative sector to byte offset in super (ParseLpMetadataFromDevice already calculated RelativeSector)
                        long byteOffsetInSuper = targetPartition.RelativeSector * 512;
                        
                        // Try normal filesystem parsing
                        BuildPropInfo partInfo = null;
                        
                        // Logic fix: Since fsType is not defined here, we probe partition header first
                        var headerData = readFromSuper(byteOffsetInSuper, 4096);
                        if (headerData != null && headerData.Length >= 4096)
                        {
                            uint magic = BitConverter.ToUInt32(headerData, 1024); // EROFS magic at 1024
                            if (magic == EROFS_MAGIC)
                                partInfo = ParseErofsAndFindBuildProp(readFromSuper, targetPartition, byteOffsetInSuper);
                            else
                                partInfo = ParseExt4AndFindBuildProp(readFromSuper, targetPartition, byteOffsetInSuper);
                        }

                        // Fallback: If filesystem parsing failed and partition is small (e.g., my_manifest < 2MB), perform brute force property scan
                        if (partInfo == null && targetPartition.Size < 2 * 1024 * 1024)
                        {
                            _logDetail(string.Format("Attempting brute force property scan for logical volume {0}...", targetPartition.Name));
                            byte[] rawData = readFromSuper(byteOffsetInSuper, (int)targetPartition.Size);
                            if (rawData != null)
                            {
                                string content = Encoding.UTF8.GetString(rawData);
                                partInfo = ParseBuildProp(content);
                            }
                        }

                        if (partInfo != null)
                        {
                            MergeProperties(masterInfo, partInfo);
                        }
                        break; // Stop after finding one either with or without slot suffix
                    }
                }
            }
            catch (Exception ex)
            {
                _log("Precise read from Super failed: " + ex.Message);
            }
            // Return if any valid information is present, not just Model
            bool hasValidInfo = !string.IsNullOrEmpty(masterInfo.Model) ||
                               !string.IsNullOrEmpty(masterInfo.MarketName) ||
                               !string.IsNullOrEmpty(masterInfo.Brand) ||
                               !string.IsNullOrEmpty(masterInfo.Device);
            return hasValidInfo ? masterInfo : null;
        }

        /// <summary>
        /// Property merge: High priority overwrites low priority to ensure precision
        /// </summary>
        private void MergeProperties(BuildPropInfo target, BuildPropInfo source)
        {
            if (source == null) return;

            // 1. Brand/Model: These properties are usually most accurate in vendor/odm
            if (!string.IsNullOrEmpty(source.Brand)) target.Brand = source.Brand;
            if (!string.IsNullOrEmpty(source.Model)) target.Model = source.Model;
            if (!string.IsNullOrEmpty(source.MarketName)) target.MarketName = source.MarketName;
            if (!string.IsNullOrEmpty(source.MarketNameEn)) target.MarketNameEn = source.MarketNameEn;
            if (!string.IsNullOrEmpty(source.Device)) target.Device = source.Device;
            if (!string.IsNullOrEmpty(source.Manufacturer)) target.Manufacturer = source.Manufacturer;

            // 2. Version info: system provides Android version, but vendor provides security patch and OTA version
            if (!string.IsNullOrEmpty(source.AndroidVersion)) target.AndroidVersion = source.AndroidVersion;
            if (!string.IsNullOrEmpty(source.SdkVersion)) target.SdkVersion = source.SdkVersion;
            if (!string.IsNullOrEmpty(source.SecurityPatch)) target.SecurityPatch = source.SecurityPatch;
            
            // 3. OTA version precise merge
            if (!string.IsNullOrEmpty(source.DisplayId)) target.DisplayId = source.DisplayId;
            if (!string.IsNullOrEmpty(source.OtaVersion)) target.OtaVersion = source.OtaVersion;
            if (!string.IsNullOrEmpty(source.OtaVersionFull)) target.OtaVersionFull = source.OtaVersionFull;
            if (!string.IsNullOrEmpty(source.BuildDate)) target.BuildDate = source.BuildDate;
            if (!string.IsNullOrEmpty(source.BuildUtc)) target.BuildUtc = source.BuildUtc;
            
            // 4. Manufacturer specific properties
            if (!string.IsNullOrEmpty(source.OplusProject)) target.OplusProject = source.OplusProject;
            if (!string.IsNullOrEmpty(source.OplusNvId)) target.OplusNvId = source.OplusNvId;
            if (!string.IsNullOrEmpty(source.OplusCpuInfo)) target.OplusCpuInfo = source.OplusCpuInfo;
            if (!string.IsNullOrEmpty(source.LenovoSeries)) target.LenovoSeries = source.LenovoSeries;

            // 5. Merge all properties dictionary
            foreach (var kv in source.AllProperties)
            {
                target.AllProperties[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Read build.prop from standalone physical partition
        /// </summary>
        private async Task<BuildPropInfo> ReadBuildPropFromStandalonePartition(string partitionName, Func<string, long, int, Task<byte[]>> readPartition)
        {
            try
            {
                _log(string.Format("尝试从物理分区 {0} 读取...", partitionName));
                _log(string.Format("Attempting to read from physical partition {0}...", partitionName));
                
                // Read header to detect filesystem (with timeout protection)
                byte[] header = await readPartition(partitionName, 0, 4096);
                if (header == null) 
                {
                    _log(string.Format("  → {0}: Cannot read header data", partitionName));
                    return null;
                }

                string fsType = DetectFileSystem(header);
                long fsBaseOffset = 0;  // Actual start offset of the filesystem
                
                // If sparse format detected, try skipping sparse header and re-detecting
                if (fsType == "sparse")
                {
                    _log(string.Format("  → {0}: Sparse format detected, skipping header and re-probing...", partitionName));
                    byte[] moreData = await readPartition(partitionName, 4096, 4096);
                    if (moreData != null && moreData.Length > 1024)
                    {
                        fsType = DetectFileSystem(moreData);
                        if (fsType != "unknown" && fsType != "sparse")
                        {
                            _log(string.Format("  → {0}: Sparse internal is {1} filesystem", partitionName, fsType.ToUpper()));
                            fsBaseOffset = 4096;
                        }
                    }
                }
                
                // erofs_raw indicates EROFS magic at offset 0, need to adjust read offset
                if (fsType == "erofs_raw")
                {
                    fsType = "erofs";
                    _logDetail(string.Format("  → {0}: Unbounded EROFS filesystem detected", partitionName));
                }
                
                // If empty, unknown or signed detected, try searching for filesystem at different offsets
                // Some devices have signature/AVB data at the partition header, real filesystem follows
                if (fsType == "unknown" || fsType == "empty" || fsType == "signed")
                {
                    // Try common offset positions: 4KB, 8KB, 64KB, 1MB, 2MB, 4MB
                    // Manufacturers like Xiaomi might have signature headers (e.g., S72_) at partition header
                    long[] tryOffsets = { 4096, 8192, 65536, 1048576, 2097152, 4194304 };
                    foreach (var offset in tryOffsets)
                    {
                        byte[] dataAtOffset = await readPartition(partitionName, offset, 4096);
                        if (dataAtOffset != null && dataAtOffset.Length >= 2048)
                        {
                            string fsAtOffset = DetectFileSystem(dataAtOffset);
                            if (fsAtOffset != "unknown" && fsAtOffset != "empty" && fsAtOffset != "sparse")
                            {
                                _logDetail(string.Format("  → {0}: Detected {2} filesystem at offset 0x{1:X}", 
                                    partitionName, offset, fsAtOffset.ToUpper()));
                                fsType = fsAtOffset;
                                fsBaseOffset = offset;  // Record the actual offset of the filesystem
                                break;
                            }
                        }
                    }
                }
                
                if (fsType == "unknown" || fsType == "sparse" || fsType == "empty" || fsType == "signed") 
                {
                    _log(string.Format("  → {0}: Unrecognized filesystem format, attempting brute force scan...", partitionName));
                    
                    // Brute force scan: Directly search for build.prop properties in partition data
                    var bruteForceResult = await BruteForceScanPartition(partitionName, readPartition);
                    if (bruteForceResult != null)
                    {
                        _log(string.Format("  → {0}: Brute force scan successful", partitionName));
                        return bruteForceResult;
                    }
                    return null;
                }
                
                _logDetail(string.Format("  → {0}: Detected {1} filesystem (Offset=0x{2:X}), parsing...", 
                    partitionName, fsType.ToUpper(), fsBaseOffset));

                var lpInfo = new LpPartitionInfo { Name = partitionName, RelativeSector = 0, FileSystem = fsType };
                
                // Use Task.Run to avoid sync Wait() leading to UI thread deadlock
                // Set 15 second timeout
                // Critical fix: Use fsBaseOffset to adjust read offset
                long capturedBaseOffset = fsBaseOffset;  // Capture offset for closure
                string capturedPartName = partitionName;  // Capture partition name
                var parseTask = Task.Run(() => 
                {
                    DeviceReadDelegate readDelegate = (offset, size) => {
                        // Add retry mechanism to improve I/O stability
                        for (int retry = 0; retry < 3; retry++)
                        {
                            try
                            {
                                // Critical: Add requested offset to filesystem base offset
                                var t = readPartition(capturedPartName, capturedBaseOffset + offset, size);
                                // System partition reads are slow, increase timeout
                                int readTimeoutSec = capturedPartName.Contains("system") ? 15 : 10;
                                if (!t.Wait(TimeSpan.FromSeconds(readTimeoutSec)))
                                {
                                    if (retry < 2)
                                    {
                                        System.Threading.Thread.Sleep(200);  // Wait briefly then retry
                                        continue;
                                    }
                                    return null;
                                }
                                if (t.Result != null && t.Result.Length > 0)
                                    return t.Result;
                            }
                            catch (Exception ex)
                            {
                                if (retry < 2)
                                {
                                    System.Threading.Thread.Sleep(200);
                                    continue;
                                }
                                _logDetail(string.Format("Read partition data failed: {0}", ex.Message));
                            }
                        }
                        return null;
                    };

                    if (fsType == "erofs")
                        return ParseErofsAndFindBuildProp(readDelegate, lpInfo);
                    else if (fsType == "ext4")
                        return ParseExt4AndFindBuildProp(readDelegate, lpInfo);
                    return null;
                });

                // System partition is large, requires longer timeout
                int timeoutMs = partitionName.Contains("system") ? 30000 : 20000;
                var completedTask = await Task.WhenAny(parseTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (completedTask == parseTask)
                    return await parseTask.ConfigureAwait(false);
                
                _log(string.Format("Partition {0} parse timeout ({1}s)", partitionName, timeoutMs / 1000));
            }
            catch (Exception ex) 
            { 
                _logDetail($"Parse partition build.prop exception: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Brute force scan partition data, directly search for build.prop properties
        /// Used when filesystem is unrecognized
        /// </summary>
        private async Task<BuildPropInfo> BruteForceScanPartition(string partitionName, Func<string, long, int, Task<byte[]>> readPartition)
        {
            try
            {
                // Partition might be large, only scan first 16MB
                const int maxScanSize = 16 * 1024 * 1024;
                const int chunkSize = 512 * 1024;  // Read 512KB each time
                
                var foundProps = new List<string>();
                
                for (long offset = 0; offset < maxScanSize; offset += chunkSize)
                {
                    byte[] chunk = await readPartition(partitionName, offset, chunkSize);
                    if (chunk == null || chunk.Length == 0)
                        break;
                    
                    // Convert to string and search for properties
                    string content = Encoding.UTF8.GetString(chunk);
                    
                    // Search for common build.prop properties
                    var patterns = new[] {
                        @"ro\.product\.model=[^\r\n\x00]+",
                        @"ro\.product\.brand=[^\r\n\x00]+",
                        @"ro\.product\.name=[^\r\n\x00]+",
                        @"ro\.product\.device=[^\r\n\x00]+",
                        @"ro\.product\.manufacturer=[^\r\n\x00]+",
                        @"ro\.product\.marketname=[^\r\n\x00]+",
                        @"ro\.build\.display\.id=[^\r\n\x00]+",
                        @"ro\.build\.version\.release=[^\r\n\x00]+",
                        @"ro\.build\.version\.sdk=[^\r\n\x00]+",
                        @"ro\.miui\.ui\.version\.[^\r\n\x00]+",
                        @"ro\.build\.MiFavor_version=[^\r\n\x00]+"
                    };
                    
                    foreach (var pattern in patterns)
                    {
                        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            if (!foundProps.Contains(m.Value))
                                foundProps.Add(m.Value);
                        }
                    }
                    
                    // If enough properties found, end early                    if (foundProps.Count >= 5)
                        break;
                }
                
                if (foundProps.Count > 0)
                {
                    _log(string.Format("    Brute force scan found {0} properties", foundProps.Count));
                    string combined = string.Join("\n", foundProps);
                    return ParseBuildProp(combined);
                }
            }
            catch (Exception ex)
            {
                _logDetail(string.Format("Brute force scan failed: {0}", ex.Message));
            }
            return null;
        }

        /// <summary>
        /// Parse build.prop from EROFS partition
        /// </summary>
        private BuildPropInfo ParseErofsAndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition, long baseOffset = 0)
        {
            try
            {
                // Create a read delegate that converts offset to absolute offset within partition
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(baseOffset + offset, size);
                };

                // Read EROFS superblock
                var sbData = readFromPartition(1024, 128);
                if (sbData == null || sbData.Length < 128)
                {
                    _log("Cannot read EROFS superblock");
                    return null;
                }

                // Verify EROFS magic
                bool isErofs = (sbData[0] == 0xE2 && sbData[1] == 0xE1 &&
                               sbData[2] == 0xF5 && sbData[3] == 0xE0);
                if (!isErofs)
                {
                    _log("Invalid EROFS superblock");
                    return null;
                }

                // Parse superblock parameters
                byte blkSzBits = sbData[0x0C];
                ushort rootNid = BitConverter.ToUInt16(sbData, 0x0E);
                uint metaBlkAddr = BitConverter.ToUInt32(sbData, 0x28);
                uint blockSize = 1u << blkSzBits;

                _logDetail(string.Format("EROFS: BlockSize={0}, RootNid={1}, MetaBlkAddr={2}", 
                    blockSize, rootNid, metaBlkAddr));

                // Read root directory inode
                long rootInodeOffset = (long)metaBlkAddr * blockSize + (long)rootNid * 32;
                var inodeData = readFromPartition(rootInodeOffset, 64);
                if (inodeData == null || inodeData.Length < 32)
                {
                    _log("Cannot read root directory inode");
                    return null;
                }

                // Parse inode
                ushort format = BitConverter.ToUInt16(inodeData, 0);
                bool isExtended = (format & 1) == 1;
                byte dataLayout = (byte)((format >> 1) & 0x7);
                ushort mode = BitConverter.ToUInt16(inodeData, 0x04);

                // Check if it's a directory
                if ((mode & 0xF000) != 0x4000)
                {
                    _log("根 inode 不是目录");
                    return null;
                }

                long dirSize = isExtended ? BitConverter.ToInt64(inodeData, 0x08) : BitConverter.ToUInt32(inodeData, 0x08);
                uint rawBlkAddr = BitConverter.ToUInt32(inodeData, 0x10);
                int inodeSize = isExtended ? 64 : 32;
                ushort xattrCount = BitConverter.ToUInt16(inodeData, 0x02);
                int xattrSize = xattrCount > 0 ? (xattrCount - 1) * 4 + 12 : 0;
                int inlineDataOffset = inodeSize + xattrSize;

                _log(string.Format("  EROFS root directory: layout={0}, size={1}", dataLayout, dirSize));

                // Read directory data
                byte[] dirData = null;
                if (dataLayout == 2) // FLAT_INLINE - Data is inlined in inode
                {
                    int totalSize = inlineDataOffset + (int)Math.Min(dirSize, blockSize);
                    var inodeAndData = readFromPartition(rootInodeOffset, totalSize);
                    if (inodeAndData != null && inodeAndData.Length > inlineDataOffset)
                    {
                        int dataLen = Math.Min((int)dirSize, inodeAndData.Length - inlineDataOffset);
                        dirData = new byte[dataLen];
                        Array.Copy(inodeAndData, inlineDataOffset, dirData, 0, dataLen);
                    }
                }
                else if (dataLayout == 0) // FLAT_PLAIN - Data is in consecutive blocks
                {
                    long dataOffset = (long)rawBlkAddr * blockSize;
                    dirData = readFromPartition(dataOffset, (int)Math.Min(dirSize, blockSize * 2));
                }
                else if (dataLayout == 3 || dataLayout == 1) // FLAT_COMPR (3) or FLAT_COMPR_LEGACY (1) - Compressed data
                {
                    _log("  Compressed EROFS detected, attempting LZ4 decompression...");
                    dirData = ReadErofsCompressedData(readFromPartition, inodeData, isExtended, rawBlkAddr, blockSize, dirSize, metaBlkAddr);
                }

                if (dirData == null || dirData.Length < 12)
                {
                    _log(string.Format("  Cannot read directory data (layout={0})", dataLayout));
                    return null;
                }

                // Parse directory entries and find build.prop or etc directory
                var entries = ParseErofsDirectoryEntries(dirData, dirSize);
                _log(string.Format("  EROFS root directory contains {0} entries", entries.Count));
                
                // Print first few entries for debugging
                int debugCount = 0;
                foreach (var entry in entries)
                {
                    if (debugCount++ < 8)
                        _logDetail(string.Format("    - {0} (type={1})", entry.Item2, entry.Item3));
                }

                // Look for build.prop in root directory first
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "build.prop" && entry.Item3 == 1)
                    {
                        _logDetail("Found /build.prop");
                        return ReadErofsFile(readFromPartition, metaBlkAddr, blockSize, entry.Item1);
                    }
                }

                // Look for build.prop in subdirectories (Priority: /system > /etc)
                // For system partition, build.prop might be in /system/ subdirectory
                string[] searchDirs = { "system", "etc" };
                foreach (var dirName in searchDirs)
                {
                    foreach (var entry in entries)
                    {
                        if (entry.Item2 == dirName && entry.Item3 == 2)
                        {
                            _log(string.Format("  Entering /{0} directory for search...", dirName));
                            var subEntries = ReadErofsDirectory(readFromPartition, metaBlkAddr, blockSize, entry.Item1);
                            foreach (var subEntry in subEntries)
                            {
                                if (subEntry.Item2 == "build.prop" && subEntry.Item3 == 1)
                                {
                                    _logDetail(string.Format("Found /{0}/build.prop", dirName));
                                    return ReadErofsFile(readFromPartition, metaBlkAddr, blockSize, subEntry.Item1);
                                }
                            }
                        }
                    }
                }

                _logDetail("build.prop not found");
                return null;
            }
            catch (Exception ex)
            {
                _log(string.Format("Parse EROFS failed: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Read EROFS compressed data (FLAT_COMPR/FLAT_COMPR_LEGACY)
        /// Supports LZ4 and LZMA compression
        /// </summary>
        private byte[] ReadErofsCompressedData(DeviceReadDelegate read, byte[] inodeData, bool isExtended, 
            uint rawBlkAddr, uint blockSize, long uncompressedSize, uint metaBlkAddr)
        {
            try
            {
                // EROFS compression format:
                // - Compressed data stored in consecutive blocks
                // - Each compressed block has a cluster header describing compression info
                // - Uses Z_EROFS_COMPR_HEAD_SIZE = 4 bytes header
                
                long dataOffset = (long)rawBlkAddr * blockSize;
                
                // Read enough data (compressed is usually smaller, but we read original size)
                int readSize = (int)Math.Min(uncompressedSize * 2, blockSize * 4);
                byte[] compressedData = read(dataOffset, readSize);
                
                if (compressedData == null || compressedData.Length == 0)
                {
                    _logDetail("Cannot read compressed data");
                    return null;
                }

                // Detect compression type
                // EROFS compressed block format: [compression_header 4 bytes][compressed_data]
                // header: first byte identifies compression algorithm
                // 0x01 = LZ4, 0x02 = LZMA/MicroLZMA

                // Attempt Method 1: Direct LZ4 decompression (no header)
                byte[] result = Lz4Decoder.Decompress(compressedData, (int)uncompressedSize);
                if (result != null && result.Length > 0 && IsValidDirectoryData(result))
                {
                    _log("  LZ4 decompression successful (headerless)");
                    return result;
                }

                // Attempt Method 2: Skip 4 byte compression header
                if (compressedData.Length > 4)
                {
                    result = Lz4Decoder.Decompress(compressedData, 4, compressedData.Length - 4, (int)uncompressedSize);
                    if (result != null && result.Length > 0 && IsValidDirectoryData(result))
                    {
                        _log("  LZ4 decompression successful (4-byte header)");
                        return result;
                    }
                }

                // Attempt Method 3: EROFS block format decompression
                result = Lz4Decoder.DecompressErofsBlock(compressedData, (int)uncompressedSize);
                if (result != null && result.Length > 0 && IsValidDirectoryData(result))
                {
                    _log("  LZ4 decompression successful (EROFS block format)");
                    return result;
                }

                // Attempt Method 4: Scan for LZ4 blocks in data
                for (int offset = 0; offset < Math.Min(32, compressedData.Length - 16); offset++)
                {
                    result = Lz4Decoder.Decompress(compressedData, offset, compressedData.Length - offset, (int)uncompressedSize);
                    if (result != null && result.Length > 0 && IsValidDirectoryData(result))
                    {
                        _log(string.Format("  LZ4 decompression successful (Offset {0})", offset));
                        return result;
                    }
                }

                _log("  LZ4 decompression failed, compression format might not be supported");
                return null;
            }
            catch (Exception ex)
            {
                _logDetail(string.Format("Decompression failed: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Read EROFS compressed file data
        /// </summary>
        private byte[] ReadErofsCompressedFileData(DeviceReadDelegate read, uint rawBlkAddr, uint blockSize, long uncompressedSize)
        {
            try
            {
                long dataOffset = (long)rawBlkAddr * blockSize;
                
                // Read compressed data (build.prop is usually small, even smaller after compression)
                int readSize = (int)Math.Min(uncompressedSize * 2, blockSize * 4);
                byte[] compressedData = read(dataOffset, readSize);
                
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                // Try various decompression methods
                byte[] result;

                // Method 1: Direct LZ4 decompression
                result = Lz4Decoder.Decompress(compressedData, (int)uncompressedSize);
                if (result != null && result.Length > 0 && IsValidTextFile(result))
                    return result;

                // Method 2: Skip 4 byte compression header
                if (compressedData.Length > 4)
                {
                    result = Lz4Decoder.Decompress(compressedData, 4, compressedData.Length - 4, (int)uncompressedSize);
                    if (result != null && result.Length > 0 && IsValidTextFile(result))
                        return result;
                }

                // Method 3: EROFS block format
                result = Lz4Decoder.DecompressErofsBlock(compressedData, (int)uncompressedSize);
                if (result != null && result.Length > 0 && IsValidTextFile(result))
                    return result;

                // Method 4: Scan offsets
                for (int offset = 1; offset < Math.Min(16, compressedData.Length - 16); offset++)
                {
                    result = Lz4Decoder.Decompress(compressedData, offset, compressedData.Length - offset, (int)uncompressedSize);
                    if (result != null && result.Length > 0 && IsValidTextFile(result))
                        return result;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verify if data looks like a text file (build.prop)
        /// </summary>
        private bool IsValidTextFile(byte[] data)
        {
            if (data == null || data.Length < 10)
                return false;

            // build.prop should contain a large amount of printable ASCII characters
            int printableCount = 0;
            int checkLen = Math.Min(data.Length, 256);
            
            for (int i = 0; i < checkLen; i++)
            {
                byte b = data[i];
                if ((b >= 0x20 && b <= 0x7E) || b == 0x0A || b == 0x0D || b == 0x09)
                    printableCount++;
            }

            // At least 80% should be printable characters
            return (printableCount * 100 / checkLen) >= 80;
        }

        /// <summary>
        /// Verify if decompressed data looks like directory data
        /// </summary>
        private bool IsValidDirectoryData(byte[] data)
        {
            if (data == null || data.Length < 12)
                return false;

            // EROFS directory format: First 8 bytes are nid, 8-9 bytes are nameoff
            ushort firstNameOff = BitConverter.ToUInt16(data, 8);
            
            // nameoff should be a multiple of 12 and not 0
            if (firstNameOff == 0 || firstNameOff % 12 != 0 || firstNameOff > data.Length)
                return false;

            // Check if first entry nid is reasonable
            ulong firstNid = BitConverter.ToUInt64(data, 0);
            if (firstNid > 0xFFFFFFFF)  // nid usually not that large
                return false;

            return true;
        }

        /// <summary>
        /// Read EROFS directory
        /// </summary>
        private List<Tuple<ulong, string, byte>> ReadErofsDirectory(DeviceReadDelegate read, uint metaBlkAddr, uint blockSize, ulong nid)
        {
            var entries = new List<Tuple<ulong, string, byte>>();
            try
            {
                long inodeOffset = (long)metaBlkAddr * blockSize + (long)nid * 32;
                var inodeData = read(inodeOffset, 64);
                if (inodeData == null || inodeData.Length < 32) return entries;

                ushort format = BitConverter.ToUInt16(inodeData, 0);
                bool isExtended = (format & 1) == 1;
                byte dataLayout = (byte)((format >> 1) & 0x7);
                ushort mode = BitConverter.ToUInt16(inodeData, 0x04);

                if ((mode & 0xF000) != 0x4000) return entries; // Not a directory

                long dirSize = isExtended ? BitConverter.ToInt64(inodeData, 0x08) : BitConverter.ToUInt32(inodeData, 0x08);
                uint rawBlkAddr = BitConverter.ToUInt32(inodeData, 0x10);
                int inodeSize = isExtended ? 64 : 32;
                ushort xattrCount = BitConverter.ToUInt16(inodeData, 0x02);
                int xattrSize = xattrCount > 0 ? (xattrCount - 1) * 4 + 12 : 0;
                int inlineDataOffset = inodeSize + xattrSize;

                byte[] dirData = null;
                if (dataLayout == 2) // FLAT_INLINE
                {
                    int totalSize = inlineDataOffset + (int)Math.Min(dirSize, blockSize);
                    var inodeAndData = read(inodeOffset, totalSize);
                    if (inodeAndData != null && inodeAndData.Length > inlineDataOffset)
                    {
                        int dataLen = Math.Min((int)dirSize, inodeAndData.Length - inlineDataOffset);
                        dirData = new byte[dataLen];
                        Array.Copy(inodeAndData, inlineDataOffset, dirData, 0, dataLen);
                    }
                }
                else if (dataLayout == 0) // FLAT_PLAIN
                {
                    long dataOffset = (long)rawBlkAddr * blockSize;
                    dirData = read(dataOffset, (int)Math.Min(dirSize, blockSize * 2));
                }
                else if (dataLayout == 3 || dataLayout == 1) // FLAT_COMPR 压缩
                {
                    dirData = ReadErofsCompressedData(read, inodeData, isExtended, rawBlkAddr, blockSize, dirSize, metaBlkAddr);
                }

                if (dirData != null)
                {
                    entries = ParseErofsDirectoryEntries(dirData, dirSize);
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EROFS] Read directory exception: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Parse EROFS directory entries
        /// </summary>
        private List<Tuple<ulong, string, byte>> ParseErofsDirectoryEntries(byte[] dirData, long dirSize)
        {
            var entries = new List<Tuple<ulong, string, byte>>();
            if (dirData == null || dirData.Length < 12) return entries;

            try
            {
                ushort firstNameOff = BitConverter.ToUInt16(dirData, 8);
                if (firstNameOff == 0 || firstNameOff > dirData.Length) return entries;

                int direntCount = firstNameOff / 12;
                var dirents = new List<Tuple<ulong, ushort, byte>>();

                for (int i = 0; i < direntCount && i * 12 + 12 <= dirData.Length; i++)
                {
                    ulong entryNid = BitConverter.ToUInt64(dirData, i * 12);
                    ushort nameOff = BitConverter.ToUInt16(dirData, i * 12 + 8);
                    byte fileType = dirData[i * 12 + 10];
                    dirents.Add(Tuple.Create(entryNid, nameOff, fileType));
                }

                for (int i = 0; i < dirents.Count; i++)
                {
                    var d = dirents[i];
                    int nameEnd = (i + 1 < dirents.Count) ? dirents[i + 1].Item2 : Math.Min(dirData.Length, (int)dirSize);
                    if (d.Item2 >= dirData.Length) continue;

                    int nameLen = 0;
                    for (int j = 0; j < nameEnd - d.Item2 && d.Item2 + j < dirData.Length; j++)
                    {
                        if (dirData[d.Item2 + j] == 0) break;
                        nameLen++;
                    }

                    if (nameLen > 0)
                    {
                        string name = Encoding.UTF8.GetString(dirData, d.Item2, nameLen);
                        entries.Add(Tuple.Create(d.Item1, name, d.Item3));
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EROFS] Parse directory entries exception: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Read EROFS file content and parse as BuildPropInfo
        /// </summary>
        private BuildPropInfo ReadErofsFile(DeviceReadDelegate read, uint metaBlkAddr, uint blockSize, ulong nid)
        {
            try
            {
                long inodeOffset = (long)metaBlkAddr * blockSize + (long)nid * 32;
                var inodeData = read(inodeOffset, 64);
                if (inodeData == null || inodeData.Length < 32) return null;

                ushort format = BitConverter.ToUInt16(inodeData, 0);
                bool isExtended = (format & 1) == 1;
                byte dataLayout = (byte)((format >> 1) & 0x7);
                ushort mode = BitConverter.ToUInt16(inodeData, 0x04);

                if ((mode & 0xF000) != 0x8000) return null; // Not a regular file

                long fileSize = isExtended ? BitConverter.ToInt64(inodeData, 0x08) : BitConverter.ToUInt32(inodeData, 0x08);
                uint rawBlkAddr = BitConverter.ToUInt32(inodeData, 0x10);
                int inodeSize = isExtended ? 64 : 32;
                ushort xattrCount = BitConverter.ToUInt16(inodeData, 0x02);
                int xattrSize = xattrCount > 0 ? (xattrCount - 1) * 4 + 12 : 0;
                int inlineDataOffset = inodeSize + xattrSize;

                // Limit read size
                int readSize = (int)Math.Min(fileSize, 64 * 1024);
                byte[] fileData = null;

                if (dataLayout == 2) // FLAT_INLINE
                {
                    int totalSize = inlineDataOffset + readSize;
                    var inodeAndData = read(inodeOffset, totalSize);
                    if (inodeAndData != null && inodeAndData.Length > inlineDataOffset)
                    {
                        int dataLen = Math.Min(readSize, inodeAndData.Length - inlineDataOffset);
                        fileData = new byte[dataLen];
                        Array.Copy(inodeAndData, inlineDataOffset, fileData, 0, dataLen);
                    }
                }
                else if (dataLayout == 0) // FLAT_PLAIN
                {
                    long dataOffset = (long)rawBlkAddr * blockSize;
                    fileData = read(dataOffset, readSize);
                }
                else if (dataLayout == 3 || dataLayout == 1) // FLAT_COMPR 压缩
                {
                    fileData = ReadErofsCompressedFileData(read, rawBlkAddr, blockSize, fileSize);
                }

                if (fileData != null && fileData.Length > 0)
                {
                    string content = Encoding.UTF8.GetString(fileData);
                    return ParseBuildProp(content);
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EROFS] Read file exception: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Parse build.prop from EXT4 partition
        /// </summary>
        private BuildPropInfo ParseExt4AndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition, long baseOffset = 0)
        {
            try
            {
                // Create read delegate
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(baseOffset + offset, size);
                };

                // 1. Read Superblock (Offset 1024, Size 1024)
                var sbData = readFromPartition(1024, 1024);
                if (sbData == null || sbData.Length < 256)
                {
                    _log("Cannot read EXT4 superblock");
                    return null;
                }

                // Verify magic
                ushort magic = BitConverter.ToUInt16(sbData, 0x38);
                if (magic != EXT4_MAGIC)
                {
                    _log(string.Format("Invalid EXT4 magic: 0x{0:X4}", magic));
                    return null;
                }

                // Parse superblock parameters
                uint sLogBlockSize = BitConverter.ToUInt32(sbData, 0x18);
                uint blockSize = 1024u << (int)sLogBlockSize;
                uint inodesPerGroup = BitConverter.ToUInt32(sbData, 0x28);
                ushort inodeSize = BitConverter.ToUInt16(sbData, 0x58);
                uint firstDataBlock = BitConverter.ToUInt32(sbData, 0x14);
                uint blocksPerGroup = BitConverter.ToUInt32(sbData, 0x20);
                uint featureIncompat = BitConverter.ToUInt32(sbData, 0x60);

                bool hasExtents = (featureIncompat & 0x40) != 0;  // EXT4_FEATURE_INCOMPAT_EXTENTS
                bool is64Bit = (featureIncompat & 0x80) != 0;      // EXT4_FEATURE_INCOMPAT_64BIT

                _logDetail(string.Format("EXT4: BlockSize={0}, InodeSize={1}, Extents={2}, 64bit={3}",
                    blockSize, inodeSize, hasExtents, is64Bit));

                // 2. Read Block Group Descriptor Table
                long bgdtOffset = (firstDataBlock + 1) * blockSize;
                int bgdSize = is64Bit ? 64 : 32;
                var bgdData = readFromPartition(bgdtOffset, bgdSize);
                if (bgdData == null || bgdData.Length < bgdSize)
                {
                    _log("Cannot read Block Group Descriptor");
                    return null;
                }

                // Get first block group inode table location
                uint bgInodeTableLo = BitConverter.ToUInt32(bgdData, 0x08);
                uint bgInodeTableHi = is64Bit ? BitConverter.ToUInt32(bgdData, 0x28) : 0;
                long inodeTableBlock = bgInodeTableLo | ((long)bgInodeTableHi << 32);

                _logDetail(string.Format("Inode Table Block: {0}", inodeTableBlock));

                // 3. Read root directory inode (inode 2)
                long inodeOffset = inodeTableBlock * blockSize + (2 - 1) * inodeSize;
                var rootInode = readFromPartition(inodeOffset, inodeSize);
                if (rootInode == null || rootInode.Length < 128)
                {
                    _log("Cannot read root directory inode");
                    return null;
                }

                ushort iMode = BitConverter.ToUInt16(rootInode, 0x00);
                if ((iMode & 0xF000) != 0x4000) // S_IFDIR
                {
                    _log("Root inode is not a directory");
                    return null;
                }

                uint iSizeLo = BitConverter.ToUInt32(rootInode, 0x04);
                uint iFlags = BitConverter.ToUInt32(rootInode, 0x20);
                bool useExtents = (iFlags & 0x80000) != 0; // EXT4_EXTENTS_FL

                // 4. Read root directory data
                byte[] rootDirData = null;
                if (useExtents)
                {
                    rootDirData = ReadExt4ExtentData(readFromPartition, rootInode, blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                }
                else
                {
                    // Direct block pointer
                    uint block0 = BitConverter.ToUInt32(rootInode, 0x28);
                    if (block0 > 0)
                    {
                        rootDirData = readFromPartition((long)block0 * blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                    }
                }

                if (rootDirData == null || rootDirData.Length < 12)
                {
                    _log("Cannot read root directory data");
                    return null;
                }

                // 5. Parse directory entries
                var entries = ParseExt4DirectoryEntries(rootDirData);
                _logDetail(string.Format("Root directory contains {0} entries", entries.Count));

                // Look for build.prop in root directory first
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "build.prop" && entry.Item3 == 1) // Regular file
                    {
                        _logDetail("Found /build.prop");
                        return ReadExt4FileByInode(readFromPartition, entry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup);
                    }
                }

                // Look for build.prop in subdirectories (Priority: /system > /etc)
                string[] searchDirs = { "system", "etc" };
                foreach (var dirName in searchDirs)
                {
                    foreach (var entry in entries)
                    {
                        if (entry.Item2 == dirName && entry.Item3 == 2) // Directory
                        {
                            _logDetail(string.Format("Entering /{0} directory...", dirName));
                            var subDirData = ReadExt4DirectoryByInode(readFromPartition, entry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup, blocksPerGroup, is64Bit, bgdtOffset, bgdSize);
                            if (subDirData != null)
                            {
                                var subEntries = ParseExt4DirectoryEntries(subDirData);
                                foreach (var subEntry in subEntries)
                                {
                                    if (subEntry.Item2 == "build.prop" && subEntry.Item3 == 1)
                                    {
                                        _logDetail(string.Format("Found /{0}/build.prop", dirName));
                                        return ReadExt4FileByInode(readFromPartition, subEntry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup);
                                    }
                                }
                            }
                        }
                    }
                }

                _logDetail("build.prop not found");
                return null;
            }
            catch (Exception ex)
            {
                _log(string.Format("Parse EXT4 failed: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Read EXT4 extent data - Full support for multi-level extent trees
        /// </summary>
        private byte[] ReadExt4ExtentData(DeviceReadDelegate read, byte[] inode, uint blockSize, int maxSize)
        {
            try
            {
                return ReadExt4ExtentDataRecursive(read, inode, 0x28, blockSize, maxSize, 0);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Recursively read EXT4 extent data
        /// </summary>
        /// <param name="read">Read delegate</param>
        /// <param name="data">Data containing extent header</param>
        /// <param name="headerOffset">Offset of extent header in data</param>
        /// <param name="blockSize">Block size</param>
        /// <param name="maxSize">Maximum read size</param>
        /// <param name="depth">Current recursive depth (to prevent infinite recursion)</param>
        private byte[] ReadExt4ExtentDataRecursive(DeviceReadDelegate read, byte[] data, int headerOffset, uint blockSize, int maxSize, int depth)
        {
            if (depth > 5) return null; // Prevent infinite recursion
            if (data == null || headerOffset + 12 > data.Length) return null;

            // Parse extent header
            ushort ehMagic = BitConverter.ToUInt16(data, headerOffset);
            if (ehMagic != 0xF30A)
            {
                _logDetail(string.Format("EXT4 Extent: Invalid magic 0x{0:X4}", ehMagic));
                return null;
            }

            ushort ehEntries = BitConverter.ToUInt16(data, headerOffset + 2);
            ushort ehMax = BitConverter.ToUInt16(data, headerOffset + 4);
            ushort ehDepth = BitConverter.ToUInt16(data, headerOffset + 6);

            _logDetail(string.Format("EXT4 Extent: depth={0}, entries={1}", ehDepth, ehEntries));

            if (ehDepth == 0)
            {
                // Leaf node - directly contains extent entries
                return ReadExt4LeafExtents(read, data, headerOffset, ehEntries, blockSize, maxSize);
            }
            else
            {
                // Internal node - contains index to next level
                return ReadExt4IndexExtents(read, data, headerOffset, ehEntries, blockSize, maxSize, depth);
            }
        }

        /// <summary>
        /// Read EXT4 leaf node extent data
        /// </summary>
        private byte[] ReadExt4LeafExtents(DeviceReadDelegate read, byte[] data, int headerOffset, int entries, uint blockSize, int maxSize)
        {
            var result = new List<byte>();
            int totalRead = 0;

            for (int i = 0; i < entries && totalRead < maxSize; i++)
            {
                int entryOffset = headerOffset + 12 + i * 12;
                if (entryOffset + 12 > data.Length) break;

                uint eeBlock = BitConverter.ToUInt32(data, entryOffset);      // Logical block number
                ushort eeLen = BitConverter.ToUInt16(data, entryOffset + 4);  // Block count
                ushort eeStartHi = BitConverter.ToUInt16(data, entryOffset + 6);
                uint eeStartLo = BitConverter.ToUInt32(data, entryOffset + 8);

                // Handle uninitialized extent (high bit of length is 1)
                bool uninitialized = (eeLen & 0x8000) != 0;
                int actualLen = eeLen & 0x7FFF;

                if (uninitialized || actualLen == 0) continue;

                long physBlock = eeStartLo | ((long)eeStartHi << 32);
                int readSize = Math.Min((int)(actualLen * blockSize), maxSize - totalRead);

                if (readSize <= 0) break;

                byte[] extentData = read(physBlock * blockSize, readSize);
                if (extentData != null)
                {
                    result.AddRange(extentData);
                    totalRead += extentData.Length;
                }
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// Read EXT4 index node and recursively process
        /// </summary>
        private byte[] ReadExt4IndexExtents(DeviceReadDelegate read, byte[] data, int headerOffset, int entries, uint blockSize, int maxSize, int depth)
        {
            var result = new List<byte>();
            int totalRead = 0;

            for (int i = 0; i < entries && totalRead < maxSize; i++)
            {
                int entryOffset = headerOffset + 12 + i * 12;
                if (entryOffset + 12 > data.Length) break;

                // Extent 索引结构: ei_block(4) + ei_leaf_lo(4) + ei_leaf_hi(2) + unused(2)
                // uint eiBlock = BitConverter.ToUInt32(data, entryOffset);
                uint eiLeafLo = BitConverter.ToUInt32(data, entryOffset + 4);
                ushort eiLeafHi = BitConverter.ToUInt16(data, entryOffset + 8);

                long leafBlock = eiLeafLo | ((long)eiLeafHi << 32);

                // Read next level node
                byte[] nextLevel = read(leafBlock * blockSize, (int)blockSize);
                if (nextLevel == null || nextLevel.Length < 12) continue;

                // Recursive parsing
                byte[] extentData = ReadExt4ExtentDataRecursive(read, nextLevel, 0, blockSize, maxSize - totalRead, depth + 1);
                if (extentData != null)
                {
                    result.AddRange(extentData);
                    totalRead += extentData.Length;
                }
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// Parse EXT4 directory entries
        /// </summary>
        private List<Tuple<uint, string, byte>> ParseExt4DirectoryEntries(byte[] dirData)
        {
            var entries = new List<Tuple<uint, string, byte>>();
            if (dirData == null || dirData.Length < 12) return entries;

            try
            {
                int offset = 0;
                while (offset + 8 <= dirData.Length)
                {
                    uint inode = BitConverter.ToUInt32(dirData, offset);
                    ushort recLen = BitConverter.ToUInt16(dirData, offset + 4);
                    byte nameLen = dirData[offset + 6];
                    byte fileType = dirData[offset + 7];

                    if (recLen < 8 || recLen > dirData.Length - offset) break;
                    if (inode == 0)
                    {
                        offset += recLen;
                        continue;
                    }

                    if (nameLen > 0 && offset + 8 + nameLen <= dirData.Length)
                    {
                        string name = Encoding.UTF8.GetString(dirData, offset + 8, nameLen);
                        if (name != "." && name != "..")
                        {
                            entries.Add(Tuple.Create(inode, name, fileType));
                        }
                    }

                    offset += recLen;
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EXT4] Parse directory entries exception: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Read EXT4 directory data via inode number
        /// </summary>
        private byte[] ReadExt4DirectoryByInode(DeviceReadDelegate read, uint inodeNum,
            long inodeTableBlock, uint blockSize, ushort inodeSize, uint inodesPerGroup,
            uint blocksPerGroup, bool is64Bit, long bgdtOffset, int bgdSize)
        {
            try
            {
                // Calculate block group where inode resides
                uint blockGroup = (inodeNum - 1) / inodesPerGroup;
                uint localIndex = (inodeNum - 1) % inodesPerGroup;

                // If not the first block group, need to read corresponding block group descriptor
                long actualInodeTable = inodeTableBlock;
                if (blockGroup > 0)
                {
                    var bgdData = read(bgdtOffset + blockGroup * bgdSize, bgdSize);
                    if (bgdData != null && bgdData.Length >= bgdSize)
                    {
                        uint bgInodeTableLo = BitConverter.ToUInt32(bgdData, 0x08);
                        uint bgInodeTableHi = is64Bit ? BitConverter.ToUInt32(bgdData, 0x28) : 0;
                        actualInodeTable = bgInodeTableLo | ((long)bgInodeTableHi << 32);
                    }
                }

                // Read inode
                long inodeOffset = actualInodeTable * blockSize + localIndex * inodeSize;
                var inode = read(inodeOffset, inodeSize);
                if (inode == null || inode.Length < 128) return null;

                ushort iMode = BitConverter.ToUInt16(inode, 0x00);
                if ((iMode & 0xF000) != 0x4000) return null; // Not a directory

                uint iSizeLo = BitConverter.ToUInt32(inode, 0x04);
                uint iFlags = BitConverter.ToUInt32(inode, 0x20);
                bool useExtents = (iFlags & 0x80000) != 0;

                if (useExtents)
                {
                    return ReadExt4ExtentData(read, inode, blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                }
                else
                {
                    uint block0 = BitConverter.ToUInt32(inode, 0x28);
                    if (block0 > 0)
                    {
                        return read((long)block0 * blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EXT4] 读取目录数据异常: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 通过 inode 号读取 EXT4 文件并解析为 BuildPropInfo
        /// </summary>
        private BuildPropInfo ReadExt4FileByInode(DeviceReadDelegate read, uint inodeNum,
            long inodeTableBlock, uint blockSize, ushort inodeSize, uint inodesPerGroup)
        {
            try
            {
                // Simplified handling: assume all in first block group
                uint localIndex = (inodeNum - 1) % inodesPerGroup;
                long inodeOffset = inodeTableBlock * blockSize + localIndex * inodeSize;

                var inode = read(inodeOffset, inodeSize);
                if (inode == null || inode.Length < 128) return null;

                ushort iMode = BitConverter.ToUInt16(inode, 0x00);
                if ((iMode & 0xF000) != 0x8000) return null; // Not a regular file

                uint iSizeLo = BitConverter.ToUInt32(inode, 0x04);
                uint iFlags = BitConverter.ToUInt32(inode, 0x20);
                bool useExtents = (iFlags & 0x80000) != 0;

                int fileSize = (int)Math.Min(iSizeLo, 64 * 1024);
                byte[] fileData = null;

                if (useExtents)
                {
                    fileData = ReadExt4ExtentData(read, inode, blockSize, fileSize);
                }
                else
                {
                    uint block0 = BitConverter.ToUInt32(inode, 0x28);
                    if (block0 > 0)
                    {
                        fileData = read((long)block0 * blockSize, fileSize);
                    }
                }

                if (fileData != null && fileData.Length > 0)
                {
                    string content = Encoding.UTF8.GetString(fileData);
                    return ParseBuildProp(content);
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[EXT4] Read file exception: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region DevInfo Partition Parsing

        /// <summary>
        /// Parse hardware info (SN/IMEI) from devinfo partition data
        /// </summary>
        /// <summary>
        /// Parse proinfo partition (Lenovo series specific)
        /// </summary>
        public void ParseProInfo(byte[] data, DeviceFullInfo info)
        {
            if (data == null || data.Length < 1024) return;

            try
            {
                // Serial number in Lenovo proinfo is usually at 0x24 or 0x38 offset
                string sn = ExtractString(data, 0x24, 32);
                if (string.IsNullOrEmpty(sn) || !IsValidSerialNumber(sn))
                    sn = ExtractString(data, 0x38, 32);

                if (IsValidSerialNumber(sn))
                {
                    info.HardwareSn = sn;
                    info.Sources["SN"] = "proinfo";
                    _logDetail(string.Format("Found SN from proinfo: {0}", sn));
                }

                // Lenovo model sometimes also in proinfo
                string model = ExtractString(data, 0x200, 64);
                if (!string.IsNullOrEmpty(model) && model.Contains("Lenovo"))
                {
                    info.Model = model.Replace("Lenovo", "").Trim();
                    info.Brand = "Lenovo";
                    _logDetail(string.Format("Found model from proinfo: {0}", model));
                }
            }
            catch (Exception ex)
            {
                _log("Parse proinfo failed: " + ex.Message);
            }
        }

        public void ParseDevInfo(byte[] data, DeviceFullInfo info)
        {
            if (data == null || data.Length < 512) return;

            try
            {
                // 1. Extract serial number (usually starts at offset 0x00, ends with \0)
                string sn = ExtractString(data, 0, 32);
                if (IsValidSerialNumber(sn))
                {
                    info.Sources["SN"] = "devinfo";
                    // SN here is usually hardware SN, takes priority over SerialHex from Sahara
                    _logDetail(string.Format("Found SN from devinfo: {0}", sn));
                }

                // 2. Try to extract IMEI (offsets vary by manufacturer)
                // Common offsets: 0x400, 0x800, 0x1000
                string imei1 = "";
                if (data.Length >= 0x500) imei1 = ExtractImei(data, 0x400);
                if (string.IsNullOrEmpty(imei1) && data.Length >= 0x900) imei1 = ExtractImei(data, 0x800);

                if (!string.IsNullOrEmpty(imei1))
                {
                    info.Sources["IMEI"] = "devinfo";
                    _logDetail(string.Format("Found IMEI from devinfo: {0}", imei1));
                }
            }
            catch (Exception ex)
            {
                _log("Parse devinfo failed: " + ex.Message);
            }
        }

        private string ExtractString(byte[] data, int offset, int maxLength)
        {
            if (offset >= data.Length) return "";
            int len = 0;
            while (len < maxLength && offset + len < data.Length && data[offset + len] != 0 && data[offset + len] >= 0x20 && data[offset + len] <= 0x7E)
            {
                len++;
            }
            if (len == 0) return "";
            return Encoding.ASCII.GetString(data, offset, len).Trim();
        }

        private string ExtractImei(byte[] data, int offset)
        {
            string s = ExtractString(data, offset, 32);
            if (s.Length >= 14 && s.All(char.IsDigit)) return s;
            return "";
        }

        private bool IsValidSerialNumber(string sn)
        {
            if (string.IsNullOrEmpty(sn) || sn.Length < 8) return false;
            // Simple validation: most SNs are alphanumeric
            return sn.All(c => char.IsLetterOrDigit(c));
        }

        #endregion

        #region Comprehensive Info Retrieval

        /// <summary>
        /// Get full device info from Qualcomm service
        /// </summary>
        public DeviceFullInfo GetInfoFromQualcommService(QualcommService service)
        {
            var info = new DeviceFullInfo();

            if (service == null) return info;

            // 1. Chip info obtained during Sahara stage
            var chipInfo = service.ChipInfo;
            if (chipInfo != null)
            {
                info.ChipSerial = chipInfo.SerialHex;
                info.ChipName = chipInfo.ChipName;
                info.HwId = chipInfo.HwIdHex;
                info.PkHash = chipInfo.PkHash;
                info.Vendor = chipInfo.Vendor;

                // Infer brand from PK Hash
                if (info.Vendor == "Unknown" && !string.IsNullOrEmpty(chipInfo.PkHash))
                {
                    info.Vendor = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                }

                info.Sources["ChipInfo"] = "Sahara";
            }

            // 2. Storage info obtained during Firehose stage
            info.StorageType = service.StorageType;
            info.SectorSize = service.SectorSize;
            info.Sources["Storage"] = "Firehose";

            return info;
        }

        private void MergeInfo(DeviceFullInfo target, DeviceFullInfo source)
        {
            if (!string.IsNullOrEmpty(source.MarketName) && string.IsNullOrEmpty(target.MarketName))
                target.MarketName = source.MarketName;
            if (!string.IsNullOrEmpty(source.Model) && string.IsNullOrEmpty(target.Model))
                target.Model = source.Model;
            if (!string.IsNullOrEmpty(source.ChipName) && string.IsNullOrEmpty(target.ChipName))
                target.ChipName = source.ChipName;
            if (!string.IsNullOrEmpty(source.OtaVersion) && string.IsNullOrEmpty(target.OtaVersion))
                target.OtaVersion = source.OtaVersion;
            if (!string.IsNullOrEmpty(source.OplusProject) && string.IsNullOrEmpty(target.OplusProject))
                target.OplusProject = source.OplusProject;
            if (!string.IsNullOrEmpty(source.OplusNvId) && string.IsNullOrEmpty(target.OplusNvId))
                target.OplusNvId = source.OplusNvId;
        }

        private void MergeFromBuildProp(DeviceFullInfo target, BuildPropInfo source)
        {
            if (!string.IsNullOrEmpty(source.Brand) && (string.IsNullOrEmpty(target.Brand) || target.Brand == "oplus"))
                target.Brand = source.Brand;
            if (!string.IsNullOrEmpty(source.Model) && string.IsNullOrEmpty(target.Model))
                target.Model = source.Model;
            if (!string.IsNullOrEmpty(source.MarketName) && string.IsNullOrEmpty(target.MarketName))
                target.MarketName = source.MarketName;
            if (!string.IsNullOrEmpty(source.MarketNameEn) && string.IsNullOrEmpty(target.MarketNameEn))
                target.MarketNameEn = source.MarketNameEn;
            // Device Codename: Prioritize Codename (ro.product.device/ro.build.product), then Device            if (string.IsNullOrEmpty(target.DeviceCodename))
            {
                if (!string.IsNullOrEmpty(source.Codename))
                    target.DeviceCodename = source.Codename;
                else if (!string.IsNullOrEmpty(source.Device))
                    target.DeviceCodename = source.Device;
                else if (!string.IsNullOrEmpty(source.DeviceName))
                    target.DeviceCodename = source.DeviceName;
            }
            if (!string.IsNullOrEmpty(source.AndroidVersion) && string.IsNullOrEmpty(target.AndroidVersion))
                target.AndroidVersion = source.AndroidVersion;
            if (!string.IsNullOrEmpty(source.SdkVersion) && string.IsNullOrEmpty(target.SdkVersion))
                target.SdkVersion = source.SdkVersion;
            if (!string.IsNullOrEmpty(source.SecurityPatch) && string.IsNullOrEmpty(target.SecurityPatch))
                target.SecurityPatch = source.SecurityPatch;
            if (!string.IsNullOrEmpty(source.BuildId) && string.IsNullOrEmpty(target.BuildId))
                target.BuildId = source.BuildId;
            if (!string.IsNullOrEmpty(source.Fingerprint) && string.IsNullOrEmpty(target.Fingerprint))
                target.Fingerprint = source.Fingerprint;
            if (!string.IsNullOrEmpty(source.DisplayId) && string.IsNullOrEmpty(target.DisplayId))
                target.DisplayId = source.DisplayId;
            if (!string.IsNullOrEmpty(source.OtaVersion) && string.IsNullOrEmpty(target.OtaVersion))
            {
                string ota = source.OtaVersion;
                // Smart merge: if OTA version starts with . (e.g. .610(CN01)), try to complete prefix
                if (ota.StartsWith(".") && !string.IsNullOrEmpty(target.AndroidVersion))
                {
                    ota = target.AndroidVersion + ".0.0" + ota;
                }
                target.OtaVersion = ota;
            }
            if (!string.IsNullOrEmpty(source.BuildDate)) target.BuiltDate = source.BuildDate;
            if (!string.IsNullOrEmpty(source.BuildUtc)) target.BuildTimestamp = source.BuildUtc;
            
            if (!string.IsNullOrEmpty(source.BootSlot) && string.IsNullOrEmpty(target.CurrentSlot))
            {
                target.CurrentSlot = source.BootSlot;
                target.IsAbDevice = true;
            }
            if (!string.IsNullOrEmpty(source.OplusCpuInfo) && string.IsNullOrEmpty(target.OplusCpuInfo))
                target.OplusCpuInfo = source.OplusCpuInfo;
            if (!string.IsNullOrEmpty(source.OplusNvId) && string.IsNullOrEmpty(target.OplusNvId))
                target.OplusNvId = source.OplusNvId;
            if (!string.IsNullOrEmpty(source.OplusProject) && string.IsNullOrEmpty(target.OplusProject))
                target.OplusProject = source.OplusProject;
        }

        #endregion
    }
}
