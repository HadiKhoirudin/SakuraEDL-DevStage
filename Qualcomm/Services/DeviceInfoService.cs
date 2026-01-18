// ============================================================================
// LoveAlways - 设备信息服务
// 支持从 Sahara、Firehose、Super 分区、build.prop 等多种来源获取设备信息
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.Models;

namespace LoveAlways.Qualcomm.Services
{
    #region 数据模型

    /// <summary>
    /// 完整设备信息
    /// </summary>
    public class DeviceFullInfo
    {
        // 基础信息 (Sahara 获取)
        public string ChipSerial { get; set; }
        public string ChipName { get; set; }
        public string HwId { get; set; }
        public string PkHash { get; set; }
        public string Vendor { get; set; }

        // 固件信息 (Firehose/build.prop 获取)
        public string Brand { get; set; }
        public string Model { get; set; }
        public string MarketName { get; set; }
        public string MarketNameEn { get; set; }
        public string DeviceCodename { get; set; }
        public string AndroidVersion { get; set; }
        public string SdkVersion { get; set; }
        public string SecurityPatch { get; set; }
        public string BuildId { get; set; }
        public string Fingerprint { get; set; }
        public string OtaVersion { get; set; }
        public string OtaVersionFull { get; set; } // 新增：完整固件包名称
        public string DisplayId { get; set; }
        public string BuiltDate { get; set; }     // 新增：人类可读构建日期
        public string BuildTimestamp { get; set; } // 新增：Unix 时间戳

        // 存储信息
        public string StorageType { get; set; }
        public int SectorSize { get; set; }
        public bool IsAbDevice { get; set; }
        public string CurrentSlot { get; set; }

        // OPLUS 特有信息
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

        // Lenovo 特有信息
        public string LenovoSeries { get; set; }

        // 硬件识别信息 (devinfo 分区获取)
        public string HardwareSn { get; set; }
        public string Imei1 { get; set; }
        public string Imei2 { get; set; }

        // 信息来源
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
        /// 获取显示用的设备名称
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
        /// 获取格式化的设备信息摘要
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(DisplayName))
                sb.AppendLine($"设备: {DisplayName}");
            if (!string.IsNullOrEmpty(Model) && Model != DisplayName)
                sb.AppendLine($"型号: {Model}");
            if (!string.IsNullOrEmpty(ChipName) && ChipName != "Unknown")
                sb.AppendLine($"芯片: {ChipName}");
            if (!string.IsNullOrEmpty(AndroidVersion))
                sb.AppendLine($"Android: {AndroidVersion}");
            if (!string.IsNullOrEmpty(OtaVersion))
                sb.AppendLine($"版本: {OtaVersion}");
            if (!string.IsNullOrEmpty(StorageType))
                sb.AppendLine($"存储: {StorageType.ToUpper()}");
            if (!string.IsNullOrEmpty(OplusProject))
                sb.AppendLine($"项目ID: {OplusProject}");
            if (!string.IsNullOrEmpty(OplusNvId))
                sb.AppendLine($"NV ID: {OplusNvId}");
            if (!string.IsNullOrEmpty(LenovoSeries))
                sb.AppendLine($"联想系列: {LenovoSeries}");
            if (!string.IsNullOrEmpty(HardwareSn))
                sb.AppendLine($"硬件序列号: {HardwareSn}");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Build.prop 解析结果
    /// </summary>
    public class BuildPropInfo
    {
        public string Brand { get; set; }
        public string Model { get; set; }
        public string Device { get; set; }
        public string MarketName { get; set; }
        public string MarketNameEn { get; set; }
        public string Manufacturer { get; set; }
        public string AndroidVersion { get; set; }
        public string SdkVersion { get; set; }
        public string SecurityPatch { get; set; }
        public string BuildId { get; set; }
        public string Fingerprint { get; set; }
        public string DisplayId { get; set; }
        public string OtaVersion { get; set; }
        public string OtaVersionFull { get; set; } // 新增
        public string Incremental { get; set; }
        public string BuildDate { get; set; }      // 新增
        public string BuildUtc { get; set; }       // 新增
        public string BootSlot { get; set; }

        // OPLUS 特有
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

        // Lenovo 特有
        public string LenovoSeries { get; set; }

        public Dictionary<string, string> AllProperties { get; set; }

        public BuildPropInfo()
        {
            Brand = "";
            Model = "";
            Device = "";
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
            Incremental = "";
            BootSlot = "";
            OplusCpuInfo = "";
            OplusNvId = "";
            OplusProject = "";
            AllProperties = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// LP 分区信息 (增强版 - 支持精准物理偏移)
    /// </summary>
    public class LpPartitionInfo
    {
        public string Name { get; set; }
        public uint Attrs { get; set; }
        public long RelativeSector { get; set; } // 相对于 super 起始的 512B 扇区偏移
        public long AbsoluteSector { get; set; } // 在磁盘上的绝对物理扇区 (根据 physicalSectorSize 换算)
        public long SizeInSectors { get; set; }  // 分区大小 (扇区数)
        public long Size { get; set; }           // 分区大小 (字节)
        public string FileSystem { get; set; }

        public LpPartitionInfo()
        {
            Name = "";
            FileSystem = "unknown";
        }
    }

    /// <summary>
    /// 镜像映射块信息 (用于精准偏移读取)
    /// </summary>
    public class ImageMapBlock
    {
        public long BlockIndex { get; set; }
        public long BlockCount { get; set; }
        public long FileOffset { get; set; } // 在 .img 文件中的偏移
    }

    /// <summary>
    /// 镜像映射表解析器 (针对 OPPO/Realme 固件的 .map 文件)
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
                    
                    // 格式通常为: start_block block_count
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
            catch { }
            return blocks;
        }
    }

    #endregion

    /// <summary>
    /// 设备信息服务 - 支持从多种来源获取设备信息
    /// </summary>
    public class DeviceInfoService
    {
        // LP Metadata 常量
        private const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467;  // "gDla"
        private const uint LP_METADATA_HEADER_MAGIC = 0x41680530;    // 标准: "0\x05hA"
        private const uint LP_METADATA_HEADER_MAGIC_ALP0 = 0x414c5030; // 联想: "0PLA"
        private const ushort EXT4_MAGIC = 0xEF53;
        private const uint EROFS_MAGIC = 0xE0F5E1E2;

        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;

        public DeviceInfoService(Action<string> log = null, Action<string> logDetail = null)
        {
            _log = log ?? delegate { };
            _logDetail = logDetail ?? delegate { };
        }

        #region Build.prop 解析

        /// <summary>
        /// 从 build.prop 文件路径解析设备信息
        /// </summary>
        public BuildPropInfo ParseBuildPropFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _log($"文件不存在: {filePath}");
                    return null;
                }

                var content = File.ReadAllText(filePath, Encoding.UTF8);
                return ParseBuildProp(content);
            }
            catch (Exception ex)
            {
                _log($"解析 build.prop 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 build.prop 内容解析设备信息 (增强版 - 支持属性块探测)
        /// </summary>
        public BuildPropInfo ParseBuildProp(string content)
        {
            var info = new BuildPropInfo();
            if (string.IsNullOrEmpty(content)) return info;

            // 包含正则表达式需要的命名空间
            // 注意：Regex 在 System.Text.RegularExpressions 中
            
            // 如果内容中包含大量的不可打印字符，说明可能是原始分区数据
            // 我们需要提取其中的有效行 (增强版正则表达式)
            string[] lines;
            if (content.Contains("\0"))
            {
                var list = new List<string>();
                // 1. 匹配标准 ro. 属性
                var matches = System.Text.RegularExpressions.Regex.Matches(content, @"(ro|display|persist)\.[a-zA-Z0-9._-]+=[^\r\n\x00\s]+");
                foreach (System.Text.RegularExpressions.Match m in matches) list.Add(m.Value);
                
                // 2. 匹配 OPLUS/Lenovo/Xiaomi/ZTE 特有属性
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

                // 移除可能存在的末尾乱码 (常见于 EROFS 原始块提取)
                if (value.Length > 0 && (value[value.Length - 1] < 32 || value[value.Length - 1] > 126))
                {
                    value = value.TrimEnd('\0', '\r', '\n', '\t', ' ');
                }

                if (string.IsNullOrEmpty(value)) continue;

                info.AllProperties[key] = value;

                // 核心属性映射
                switch (key)
                {
                    case "ro.product.vendor.brand":
                    case "ro.product.brand":
                    case "ro.product.manufacturer":
                        if (string.IsNullOrEmpty(info.Brand) || value != "oplus")
                            info.Brand = value;
                        break;
                    
                    case "ro.vendor.oplus.market.name":
                    case "ro.product.marketname":
                        info.MarketName = value;
                        break;

                    case "ro.product.vendor.marketname":
                    case "ro.product.odm.marketname":
                        info.MarketName = value;
                        break;

                    case "ro.product.model":
                    case "ro.product.vendor.model":
                    case "ro.product.odm.model":
                    case "ro.product.odm.cert":
                    case "ro.lenovo.series":
                        if (string.IsNullOrEmpty(info.Model) || value.Length > info.Model.Length || key == "ro.lenovo.series")
                        {
                            // 如果包含 Y700 或 Legion，优先级最高
                            if (value.Contains("Y700") || value.Contains("Legion"))
                                info.MarketName = value;
                            else
                                info.Model = value;
                        }
                        break;

                    case "ro.miui.ui.version.name":
                    case "ro.miui.ui.version.code":
                        // 优先捕捉 MIUI/HyperOS 展示名: V14.0.x.x 或 OS3.0.x.x
                        if (string.IsNullOrEmpty(info.OtaVersion) || value.StartsWith("V") || value.StartsWith("OS"))
                            info.OtaVersion = value;
                        
                        if (value.Contains("OS3.")) info.AndroidVersion = "16.0";
                        break;

                    case "ro.build.MiFavor_version":
                        // 中兴 NebulaOS/MiFavor 版本
                        if (string.IsNullOrEmpty(info.OtaVersion)) info.OtaVersion = value;
                        break;

                    case "ro.build.version.ota":
                    case "ro.build.display.id":
                    case "ro.system_ext.build.version.incremental":
                        // 联想 ZUXOS 处理: TB321FU_CN_OPEN_USER_Q00011.0_V_ZUI_17.0.10.308_ST_251030
                        if (value.Contains("ZUI") || value.Contains("ZUXOS"))
                        {
                            info.OtaVersionFull = value;
                            // 提取精简版: 1.1.10.308
                            var m = Regex.Match(value, @"\d+\.\d+\.\d+\.\d+");
                            if (m.Success) info.OtaVersion = m.Value;
                        }
                        // 如果是努比亚/红魔，该字段通常包含 RedMagicOSxx 或 NebulaOS
                        else if (value.Contains("RedMagic") || value.Contains("Nebula"))
                        {
                            info.OtaVersion = value;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(info.OtaVersion))
                                info.OtaVersion = value;
                        }
                        break;
                    case "ro.build.version.incremental":
                    case "display.id.show":
                    case "region": // 将 region 映射到 OtaVersion，方便界面展示
                        if (key == "display.id.show" || key == "region" || key.Contains("ota") || string.IsNullOrEmpty(info.OtaVersion))
                        {
                            // 如果是 display.id.show 且包含 (CNxx)，这就是最准确的展示版本
                            if (key == "display.id.show" && value.Contains("(") && value.Contains(")"))
                            {
                                info.OtaVersion = value;
                            }
                            else if (string.IsNullOrEmpty(info.OtaVersion) || key.Contains("ota"))
                            {
                                info.OtaVersion = value;
                            }
                        }
                        break;
                    
                    case "ro.build.oplus_nv_id":
                        info.OplusNvId = value;
                        break;
                    
                    case "ro.lenovo.cpuinfo":
                        info.OplusCpuInfo = value; // 借用 OplusCpuInfo 存储 CPU 信息
                        break;

                    case "ro.build.date":
                        info.BuildDate = value;
                        break;

                    case "ro.build.date.utc":
                        info.BuildUtc = value;
                        break;

                    case "ro.build.version.release":
                        info.AndroidVersion = value;
                        break;
                }
            }

            return info;
        }

        #endregion

        #region LP Metadata 解析

        /// <summary>
        /// 设备读取委托 - 用于从 9008 设备读取指定偏移的数据
        /// </summary>
        public delegate byte[] DeviceReadDelegate(long offsetInSuper, int size);

        /// <summary>
        /// 解析 LP Metadata - 从设备按需读取 (精准偏移版)
        /// </summary>
        /// <param name="readFromDevice">读取委托 (从 super 分区起始开始读取)</param>
        /// <param name="superStartSector">super 分区的起始物理扇区 (从 GPT 获取)</param>
        /// <param name="physicalSectorSize">设备的物理扇区大小 (通常为 4096 或 512)</param>
        public List<LpPartitionInfo> ParseLpMetadataFromDevice(DeviceReadDelegate readFromDevice, long superStartSector = 0, int physicalSectorSize = 512)
        {
            try
            {
                // 1. 读取 Geometry (偏移 0x1000 = 4096，大小 4096)
                var geometryData = readFromDevice(4096, 4096);
                if (geometryData == null || geometryData.Length < 52)
                {
                    _log("无法读取 LP Geometry");
                    return null;
                }

                uint magic = BitConverter.ToUInt32(geometryData, 0);
                if (magic != LP_METADATA_GEOMETRY_MAGIC)
                {
                    _log("无效的 LP Geometry magic");
                    return null;
                }

                uint metadataMaxSize = BitConverter.ToUInt32(geometryData, 40);
                uint metadataSlotCount = BitConverter.ToUInt32(geometryData, 44);
                
                // 2. 尝试寻找活动的 Metadata Header
                // 可能的偏移：8192 (Slot0, 512B扇区), 12288 (Slot0, 4KB扇区), 4096 (早期)
                long[] possibleOffsets = { 8192, 12288, 4096, 16384 };
                byte[] metadataData = null;
                uint headerMagic = 0;
                long finalOffset = 0;

                foreach (var offset in possibleOffsets)
                {
                    metadataData = readFromDevice(offset, 4096); // 先读 4KB 探测
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
                    _log("无法找到有效的 LP Metadata Header");
                    return null;
                }

                // 读取完整的 metadata (根据 header 中的 size)
                uint headerSize = BitConverter.ToUInt32(metadataData, 8);
                uint tablesSize = BitConverter.ToUInt32(metadataData, 24);
                int totalToRead = (int)(headerSize + tablesSize);
                
                if (totalToRead > metadataData.Length)
                {
                    metadataData = readFromDevice(finalOffset, totalToRead);
                }

                int tablesBase = (int)headerSize;

                // 3. 解析表描述符 (偏移 0x50)
                int tablesOffset = 0x50;
                uint partOffset = BitConverter.ToUInt32(metadataData, tablesOffset);
                uint partNum = BitConverter.ToUInt32(metadataData, tablesOffset + 4);
                uint partEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 8);
                uint extOffset = BitConverter.ToUInt32(metadataData, tablesOffset + 12);
                uint extNum = BitConverter.ToUInt32(metadataData, tablesOffset + 16);
                uint extEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 20);

                // 4. 解析 extents (物理映射)
                var extents = new List<Tuple<long, long>>(); // <扇区数, 物理数据块偏移(512B单元)>
                for (int i = 0; i < extNum; i++)
                {
                    int entryOffset = tablesBase + (int)extOffset + i * (int)extEntrySize;
                    if (entryOffset + 12 > metadataData.Length) break;

                    long numSectors = BitConverter.ToInt64(metadataData, entryOffset);
                    long targetData = BitConverter.ToInt64(metadataData, entryOffset + 12);
                    extents.Add(Tuple.Create(numSectors, targetData));
                }

                // 5. 解析分区并换算【物理扇区】
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
                        
                        // 【精准计算逻辑】
                        // targetData 是 LP 内部以 512 字节为基准的偏移
                        // 我们需要将其换算为物理磁盘的绝对扇区
                        long relativeSector = ext.Item2; // 512B 扇区偏移
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
                        _logDetail(string.Format("逻辑分区 [{0}]: 物理扇区={1}, 大小={2}MB", 
                            lp.Name, lp.AbsoluteSector, lp.Size / 1024 / 1024));
                    }
                }

                return partitions;
            }
            catch (Exception ex)
            {
                _log($"解析 LP Metadata 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检测文件系统类型
        /// </summary>
        public string DetectFileSystem(byte[] data)
        {
            if (data == null || data.Length < 2048) return "unknown";

            // EXT4: superblock 在偏移 1024，magic 在 offset 56 (0xEF53)
            if (data.Length >= 1024 + 58)
            {
                ushort ext4Magic = BitConverter.ToUInt16(data, 1024 + 56);
                if (ext4Magic == EXT4_MAGIC) return "ext4";
            }

            // EROFS: superblock 在偏移 1024，magic 在 offset 0
            if (data.Length >= 1024 + 4)
            {
                bool isErofs = (data[1024] == 0xE2 && data[1025] == 0xE1 &&
                               data[1026] == 0xF5 && data[1027] == 0xE0);
                if (isErofs) return "erofs";
            }

            // F2FS
            if (data.Length >= 1024 + 4)
            {
                uint f2fsMagic = BitConverter.ToUInt32(data, 1024);
                if (f2fsMagic == 0xF2F52010) return "f2fs";
            }

            return "unknown";
        }

        #endregion

        #region 从设备在线读取 build.prop

        /// <summary>
        /// 从设备在线读取 build.prop (适配所有安卓版本)
        /// </summary>
        /// <param name="readPartition">从指定分区名称读取数据的委托</param>
        /// <param name="activeSlot">当前活动槽位 (a/b)</param>
        /// <param name="hasSuper">是否存在 super 分区</param>
        /// <param name="superStartSector">super 的物理起始扇区</param>
        /// <param name="physicalSectorSize">扇区大小</param>
        /// <returns>解析后的 build.prop 信息</returns>
        public async Task<BuildPropInfo> ReadBuildPropFromDevice(
            Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot = "", 
            bool hasSuper = true,
            long superStartSector = 0,
            int physicalSectorSize = 512)
        {
            try
            {
                BuildPropInfo finalInfo = null;

                if (hasSuper)
                {
                    _log("正在从 Super 分区逻辑卷解析 build.prop...");
                    // 包装委托以符合 LP 解析器要求的格式 (从 super 分区读取偏移)
                    DeviceReadDelegate readFromSuper = (offset, size) => {
                        var task = readPartition("super", offset, size);
                        task.Wait();
                        return task.Result;
                    };
                    finalInfo = ReadBuildPropFromSuper(readFromSuper, activeSlot, superStartSector, physicalSectorSize);
                }

                // 即使有 super 且读取成功，也尝试扫描关键物理分区进行增强 (适配低版本或特定厂商 my_manifest)
                _log("正在扫描物理分区以提取/增强 build.prop (全版本适配模式)...");
                var searchPartitions = new List<string>();
                string slotSuffix = string.IsNullOrEmpty(activeSlot) ? "" : "_" + activeSlot.ToLower().TrimStart('_');

                if (!string.IsNullOrEmpty(slotSuffix))
                {
                    searchPartitions.Add("my_manifest" + slotSuffix);
                    searchPartitions.Add("vendor" + slotSuffix);
                    searchPartitions.Add("system" + slotSuffix);
                    searchPartitions.Add("odm" + slotSuffix);
                }
                searchPartitions.Add("my_manifest");
                searchPartitions.Add("vendor");
                searchPartitions.Add("system");
                searchPartitions.Add("odm");
                searchPartitions.Add("cust");
                searchPartitions.Add("lenovocust");

                foreach (var partName in searchPartitions)
                {
                    var info = await ReadBuildPropFromStandalonePartition(partName, readPartition);
                    if (info != null)
                    {
                        if (finalInfo == null) finalInfo = info;
                        else MergeProperties(finalInfo, info);
                        
                        // 如果已经拿到了核心型号和市场名，可以提前结束提高速度
                        if (!string.IsNullOrEmpty(finalInfo.MarketName) && !string.IsNullOrEmpty(finalInfo.OtaVersion))
                            break;
                    }
                }
                return finalInfo;
            }
            catch (Exception ex)
            {
                _log(string.Format("读取 build.prop 整体流程失败: {0}", ex.Message));
            }
            return null;
        }

        /// <summary>
        /// 从 Super 分区逻辑卷读取 build.prop (精准合并模式)
        /// </summary>
        private BuildPropInfo ReadBuildPropFromSuper(DeviceReadDelegate readFromSuper, string activeSlot = "", long superStartSector = 0, int physicalSectorSize = 512)
        {
            var masterInfo = new BuildPropInfo();
            try
            {
                // 1. 解析 LP Metadata
                var lpPartitions = ParseLpMetadataFromDevice(readFromSuper, superStartSector, physicalSectorSize);
                if (lpPartitions == null || lpPartitions.Count == 0) return null;

                string slotSuffix = string.IsNullOrEmpty(activeSlot) ? "" : "_" + activeSlot.ToLower().TrimStart('_');
                
                // 2. 优先级排序：从低到高读取，高优先级覆盖低优先级
                // 顺序：System -> System_ext -> Product -> Vendor -> ODM -> My_manifest
                var searchList = new List<string> { "system", "system_ext", "product", "vendor", "odm", "my_manifest" };
                
                foreach (var baseName in searchList)
                {
                    // 尝试带槽位的名称和不带槽位的名称
                    var possibleNames = new[] { baseName + slotSuffix, baseName };
                    foreach (var name in possibleNames)
                    {
                        var targetPartition = lpPartitions.FirstOrDefault(p => p.Name == name);
                        if (targetPartition == null) continue;

                        _log(string.Format("正在从逻辑卷 {0} (物理扇区: {1}) 解析 build.prop...", 
                            targetPartition.Name, targetPartition.AbsoluteSector));
                        
                        // 换算逻辑分区在 super 内的字节偏移 (ParseLpMetadataFromDevice 已经算好了 RelativeSector)
                        long byteOffsetInSuper = targetPartition.RelativeSector * 512;
                        
                        // 尝试正常文件系统解析
                        BuildPropInfo partInfo = null;
                        
                        // 逻辑修改：由于 fsType 在此处未定义，我们需要先探测分区头
                        var headerData = readFromSuper(byteOffsetInSuper, 4096);
                        if (headerData != null && headerData.Length >= 4096)
                        {
                            uint magic = BitConverter.ToUInt32(headerData, 1024); // EROFS magic at 1024
                            if (magic == EROFS_MAGIC)
                                partInfo = ParseErofsAndFindBuildProp(readFromSuper, targetPartition, byteOffsetInSuper);
                            else
                                partInfo = ParseExt4AndFindBuildProp(readFromSuper, targetPartition, byteOffsetInSuper);
                        }

                        // 兜底策略：如果文件系统解析失败，且分区很小 (如 my_manifest < 2MB)，进行暴力属性扫描
                        if (partInfo == null && targetPartition.Size < 2 * 1024 * 1024)
                        {
                            _logDetail(string.Format("尝试对逻辑卷 {0} 进行暴力属性扫描...", targetPartition.Name));
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
                        break; // 找到带/不带槽位的一个即可
                    }
                }
            }
            catch (Exception ex)
            {
                _log("从 Super 精准读取失败: " + ex.Message);
            }
            return string.IsNullOrEmpty(masterInfo.Model) ? null : masterInfo;
        }

        /// <summary>
        /// 属性合并：高优先级覆盖低优先级，确保信息精准
        /// </summary>
        private void MergeProperties(BuildPropInfo target, BuildPropInfo source)
        {
            if (source == null) return;

            // 1. 品牌/型号：这些属性通常在 vendor/odm 中最准
            if (!string.IsNullOrEmpty(source.Brand)) target.Brand = source.Brand;
            if (!string.IsNullOrEmpty(source.Model)) target.Model = source.Model;
            if (!string.IsNullOrEmpty(source.MarketName)) target.MarketName = source.MarketName;
            if (!string.IsNullOrEmpty(source.MarketNameEn)) target.MarketNameEn = source.MarketNameEn;
            if (!string.IsNullOrEmpty(source.Device)) target.Device = source.Device;
            if (!string.IsNullOrEmpty(source.Manufacturer)) target.Manufacturer = source.Manufacturer;

            // 2. 版本信息：system 提供 Android 版本，但 vendor 提供安全补丁和 OTA 版本
            if (!string.IsNullOrEmpty(source.AndroidVersion)) target.AndroidVersion = source.AndroidVersion;
            if (!string.IsNullOrEmpty(source.SdkVersion)) target.SdkVersion = source.SdkVersion;
            if (!string.IsNullOrEmpty(source.SecurityPatch)) target.SecurityPatch = source.SecurityPatch;
            
            // 3. OTA 版本精准合并
            if (!string.IsNullOrEmpty(source.DisplayId)) target.DisplayId = source.DisplayId;
            if (!string.IsNullOrEmpty(source.OtaVersion)) target.OtaVersion = source.OtaVersion;
            if (!string.IsNullOrEmpty(source.OtaVersionFull)) target.OtaVersionFull = source.OtaVersionFull;
            if (!string.IsNullOrEmpty(source.BuildDate)) target.BuildDate = source.BuildDate;
            if (!string.IsNullOrEmpty(source.BuildUtc)) target.BuildUtc = source.BuildUtc;
            
            // 4. 厂商特有属性
            if (!string.IsNullOrEmpty(source.OplusProject)) target.OplusProject = source.OplusProject;
            if (!string.IsNullOrEmpty(source.OplusNvId)) target.OplusNvId = source.OplusNvId;
            if (!string.IsNullOrEmpty(source.OplusCpuInfo)) target.OplusCpuInfo = source.OplusCpuInfo;
            if (!string.IsNullOrEmpty(source.LenovoSeries)) target.LenovoSeries = source.LenovoSeries;

            // 5. 合并全量字典
            foreach (var kv in source.AllProperties)
            {
                target.AllProperties[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// 从独立物理分区读取 build.prop
        /// </summary>
        private async Task<BuildPropInfo> ReadBuildPropFromStandalonePartition(string partitionName, Func<string, long, int, Task<byte[]>> readPartition)
        {
            try
            {
                _log(string.Format("尝试从物理分区 {0} 读取...", partitionName));
                
                // 读取头部探测文件系统
                byte[] header = await readPartition(partitionName, 0, 4096);
                if (header == null) return null;

                string fsType = DetectFileSystem(header);
                if (fsType == "unknown") return null;

                var lpInfo = new LpPartitionInfo { Name = partitionName, RelativeSector = 0, FileSystem = fsType };
                
                // 包装一个针对此分区的读取器
                DeviceReadDelegate readDelegate = (offset, size) => {
                    var t = readPartition(partitionName, offset, size);
                    t.Wait();
                    return t.Result;
                };

                if (fsType == "erofs")
                    return ParseErofsAndFindBuildProp(readDelegate, lpInfo);
                else if (fsType == "ext4")
                    return ParseExt4AndFindBuildProp(readDelegate, lpInfo);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从 EROFS 分区解析 build.prop
        /// </summary>
        private BuildPropInfo ParseErofsAndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition, long baseOffset = 0)
        {
            try
            {
                // 创建一个读取委托，将偏移转换为分区内的绝对偏移
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(baseOffset + offset, size);
                };

                // 读取 EROFS superblock
                var sbData = readFromPartition(1024, 128);
                if (sbData == null || sbData.Length < 128)
                {
                    _log("无法读取 EROFS superblock");
                    return null;
                }

                // 验证 EROFS magic
                bool isErofs = (sbData[0] == 0xE2 && sbData[1] == 0xE1 &&
                               sbData[2] == 0xF5 && sbData[3] == 0xE0);
                if (!isErofs)
                {
                    _log("无效的 EROFS superblock");
                    return null;
                }

                // 解析 superblock 参数
                byte blkSzBits = sbData[0x0C];
                ushort rootNid = BitConverter.ToUInt16(sbData, 0x0E);
                uint metaBlkAddr = BitConverter.ToUInt32(sbData, 0x28);
                uint blockSize = 1u << blkSzBits;

                _logDetail(string.Format("EROFS: BlockSize={0}, RootNid={1}, MetaBlkAddr={2}", 
                    blockSize, rootNid, metaBlkAddr));

                // 读取根目录 inode
                long rootInodeOffset = (long)metaBlkAddr * blockSize + (long)rootNid * 32;
                var inodeData = readFromPartition(rootInodeOffset, 64);
                if (inodeData == null || inodeData.Length < 32)
                {
                    _log("无法读取根目录 inode");
                    return null;
                }

                // 解析 inode
                ushort format = BitConverter.ToUInt16(inodeData, 0);
                bool isExtended = (format & 1) == 1;
                byte dataLayout = (byte)((format >> 1) & 0x7);
                ushort mode = BitConverter.ToUInt16(inodeData, 0x04);

                // 检查是否是目录
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

                _logDetail(string.Format("根目录: layout={0}, size={1}", dataLayout, dirSize));

                // 读取目录数据
                byte[] dirData = null;
                if (dataLayout == 2) // FLAT_INLINE
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
                else if (dataLayout == 0) // FLAT_PLAIN
                {
                    long dataOffset = (long)rawBlkAddr * blockSize;
                    dirData = readFromPartition(dataOffset, (int)Math.Min(dirSize, blockSize * 2));
                }

                if (dirData == null || dirData.Length < 12)
                {
                    _log("无法读取目录数据");
                    return null;
                }

                // 解析目录项并查找 build.prop 或 etc 目录
                var entries = ParseErofsDirectoryEntries(dirData, dirSize);
                _logDetail(string.Format("根目录包含 {0} 个条目", entries.Count));

                // 先在根目录查找 build.prop
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "build.prop" && entry.Item3 == 1)
                    {
                        _log("找到 /build.prop");
                        return ReadErofsFile(readFromPartition, metaBlkAddr, blockSize, entry.Item1);
                    }
                }

                // 在 etc 目录查找 (vendor 分区常见位置)
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "etc" && entry.Item3 == 2)
                    {
                        _logDetail("进入 /etc 目录...");
                        var etcEntries = ReadErofsDirectory(readFromPartition, metaBlkAddr, blockSize, entry.Item1);
                        foreach (var subEntry in etcEntries)
                        {
                            if (subEntry.Item2 == "build.prop" && subEntry.Item3 == 1)
                            {
                                _log("找到 /etc/build.prop");
                                return ReadErofsFile(readFromPartition, metaBlkAddr, blockSize, subEntry.Item1);
                            }
                        }
                    }
                }

                _log("未找到 build.prop");
                return null;
            }
            catch (Exception ex)
            {
                _log(string.Format("解析 EROFS 失败: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 读取 EROFS 目录
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

                if ((mode & 0xF000) != 0x4000) return entries; // 不是目录

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

                if (dirData != null)
                {
                    entries = ParseErofsDirectoryEntries(dirData, dirSize);
                }
            }
            catch { }
            return entries;
        }

        /// <summary>
        /// 解析 EROFS 目录项
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
            catch { }
            return entries;
        }

        /// <summary>
        /// 读取 EROFS 文件内容并解析为 BuildPropInfo
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

                if ((mode & 0xF000) != 0x8000) return null; // 不是普通文件

                long fileSize = isExtended ? BitConverter.ToInt64(inodeData, 0x08) : BitConverter.ToUInt32(inodeData, 0x08);
                uint rawBlkAddr = BitConverter.ToUInt32(inodeData, 0x10);
                int inodeSize = isExtended ? 64 : 32;
                ushort xattrCount = BitConverter.ToUInt16(inodeData, 0x02);
                int xattrSize = xattrCount > 0 ? (xattrCount - 1) * 4 + 12 : 0;
                int inlineDataOffset = inodeSize + xattrSize;

                // 限制读取大小
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

                if (fileData != null && fileData.Length > 0)
                {
                    string content = Encoding.UTF8.GetString(fileData);
                    return ParseBuildProp(content);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从 EXT4 分区解析 build.prop
        /// </summary>
        private BuildPropInfo ParseExt4AndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition, long baseOffset = 0)
        {
            try
            {
                // 创建读取委托
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(baseOffset + offset, size);
                };

                // 1. 读取 Superblock (偏移 1024，大小 1024)
                var sbData = readFromPartition(1024, 1024);
                if (sbData == null || sbData.Length < 256)
                {
                    _log("无法读取 EXT4 superblock");
                    return null;
                }

                // 验证 magic
                ushort magic = BitConverter.ToUInt16(sbData, 0x38);
                if (magic != EXT4_MAGIC)
                {
                    _log(string.Format("无效的 EXT4 magic: 0x{0:X4}", magic));
                    return null;
                }

                // 解析 superblock 参数
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

                // 2. 读取 Block Group Descriptor Table
                long bgdtOffset = (firstDataBlock + 1) * blockSize;
                int bgdSize = is64Bit ? 64 : 32;
                var bgdData = readFromPartition(bgdtOffset, bgdSize);
                if (bgdData == null || bgdData.Length < bgdSize)
                {
                    _log("无法读取 Block Group Descriptor");
                    return null;
                }

                // 获取第一个块组的 inode 表位置
                uint bgInodeTableLo = BitConverter.ToUInt32(bgdData, 0x08);
                uint bgInodeTableHi = is64Bit ? BitConverter.ToUInt32(bgdData, 0x28) : 0;
                long inodeTableBlock = bgInodeTableLo | ((long)bgInodeTableHi << 32);

                _logDetail(string.Format("Inode Table Block: {0}", inodeTableBlock));

                // 3. 读取根目录 inode (inode 2)
                long inodeOffset = inodeTableBlock * blockSize + (2 - 1) * inodeSize;
                var rootInode = readFromPartition(inodeOffset, inodeSize);
                if (rootInode == null || rootInode.Length < 128)
                {
                    _log("无法读取根目录 inode");
                    return null;
                }

                ushort iMode = BitConverter.ToUInt16(rootInode, 0x00);
                if ((iMode & 0xF000) != 0x4000) // S_IFDIR
                {
                    _log("根 inode 不是目录");
                    return null;
                }

                uint iSizeLo = BitConverter.ToUInt32(rootInode, 0x04);
                uint iFlags = BitConverter.ToUInt32(rootInode, 0x20);
                bool useExtents = (iFlags & 0x80000) != 0; // EXT4_EXTENTS_FL

                // 4. 读取根目录数据
                byte[] rootDirData = null;
                if (useExtents)
                {
                    rootDirData = ReadExt4ExtentData(readFromPartition, rootInode, blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                }
                else
                {
                    // 直接块指针
                    uint block0 = BitConverter.ToUInt32(rootInode, 0x28);
                    if (block0 > 0)
                    {
                        rootDirData = readFromPartition((long)block0 * blockSize, (int)Math.Min(iSizeLo, blockSize * 4));
                    }
                }

                if (rootDirData == null || rootDirData.Length < 12)
                {
                    _log("无法读取根目录数据");
                    return null;
                }

                // 5. 解析目录项
                var entries = ParseExt4DirectoryEntries(rootDirData);
                _logDetail(string.Format("根目录包含 {0} 个条目", entries.Count));

                // 在根目录查找 build.prop
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "build.prop" && entry.Item3 == 1) // 普通文件
                    {
                        _log("找到 /build.prop");
                        return ReadExt4FileByInode(readFromPartition, entry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup);
                    }
                }

                // 在 etc 目录查找
                foreach (var entry in entries)
                {
                    if (entry.Item2 == "etc" && entry.Item3 == 2) // 目录
                    {
                        _logDetail("进入 /etc 目录...");
                        var etcDirData = ReadExt4DirectoryByInode(readFromPartition, entry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup, blocksPerGroup, is64Bit, bgdtOffset, bgdSize);
                        if (etcDirData != null)
                        {
                            var etcEntries = ParseExt4DirectoryEntries(etcDirData);
                            foreach (var subEntry in etcEntries)
                            {
                                if (subEntry.Item2 == "build.prop" && subEntry.Item3 == 1)
                                {
                                    _log("找到 /etc/build.prop");
                                    return ReadExt4FileByInode(readFromPartition, subEntry.Item1, inodeTableBlock, blockSize, inodeSize, inodesPerGroup);
                                }
                            }
                        }
                    }
                }

                _log("未找到 build.prop");
                return null;
            }
            catch (Exception ex)
            {
                _log(string.Format("解析 EXT4 失败: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 读取 EXT4 extent 数据 - 完整支持多层 Extent 树
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
        /// 递归读取 EXT4 extent 数据
        /// </summary>
        /// <param name="read">读取委托</param>
        /// <param name="data">包含 extent header 的数据</param>
        /// <param name="headerOffset">extent header 在 data 中的偏移</param>
        /// <param name="blockSize">块大小</param>
        /// <param name="maxSize">最大读取大小</param>
        /// <param name="depth">当前递归深度 (防止无限递归)</param>
        private byte[] ReadExt4ExtentDataRecursive(DeviceReadDelegate read, byte[] data, int headerOffset, uint blockSize, int maxSize, int depth)
        {
            if (depth > 5) return null; // 防止无限递归
            if (data == null || headerOffset + 12 > data.Length) return null;

            // 解析 extent header
            ushort ehMagic = BitConverter.ToUInt16(data, headerOffset);
            if (ehMagic != 0xF30A)
            {
                _logDetail(string.Format("EXT4 Extent: 无效 magic 0x{0:X4}", ehMagic));
                return null;
            }

            ushort ehEntries = BitConverter.ToUInt16(data, headerOffset + 2);
            ushort ehMax = BitConverter.ToUInt16(data, headerOffset + 4);
            ushort ehDepth = BitConverter.ToUInt16(data, headerOffset + 6);

            _logDetail(string.Format("EXT4 Extent: depth={0}, entries={1}", ehDepth, ehEntries));

            if (ehDepth == 0)
            {
                // 叶节点 - 直接包含 extent 条目
                return ReadExt4LeafExtents(read, data, headerOffset, ehEntries, blockSize, maxSize);
            }
            else
            {
                // 内部节点 - 包含指向下一层的索引
                return ReadExt4IndexExtents(read, data, headerOffset, ehEntries, blockSize, maxSize, depth);
            }
        }

        /// <summary>
        /// 读取 EXT4 叶节点 extent 数据
        /// </summary>
        private byte[] ReadExt4LeafExtents(DeviceReadDelegate read, byte[] data, int headerOffset, int entries, uint blockSize, int maxSize)
        {
            var result = new List<byte>();
            int totalRead = 0;

            for (int i = 0; i < entries && totalRead < maxSize; i++)
            {
                int entryOffset = headerOffset + 12 + i * 12;
                if (entryOffset + 12 > data.Length) break;

                uint eeBlock = BitConverter.ToUInt32(data, entryOffset);      // 逻辑块号
                ushort eeLen = BitConverter.ToUInt16(data, entryOffset + 4);  // 块数量
                ushort eeStartHi = BitConverter.ToUInt16(data, entryOffset + 6);
                uint eeStartLo = BitConverter.ToUInt32(data, entryOffset + 8);

                // 处理未初始化的 extent (长度高位为1)
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
        /// 读取 EXT4 索引节点并递归处理
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

                // 读取下一层节点
                byte[] nextLevel = read(leafBlock * blockSize, (int)blockSize);
                if (nextLevel == null || nextLevel.Length < 12) continue;

                // 递归解析
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
        /// 解析 EXT4 目录项
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
            catch { }
            return entries;
        }

        /// <summary>
        /// 通过 inode 号读取 EXT4 目录数据
        /// </summary>
        private byte[] ReadExt4DirectoryByInode(DeviceReadDelegate read, uint inodeNum,
            long inodeTableBlock, uint blockSize, ushort inodeSize, uint inodesPerGroup,
            uint blocksPerGroup, bool is64Bit, long bgdtOffset, int bgdSize)
        {
            try
            {
                // 计算 inode 所在的块组
                uint blockGroup = (inodeNum - 1) / inodesPerGroup;
                uint localIndex = (inodeNum - 1) % inodesPerGroup;

                // 如果不是第一个块组，需要读取对应的 block group descriptor
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

                // 读取 inode
                long inodeOffset = actualInodeTable * blockSize + localIndex * inodeSize;
                var inode = read(inodeOffset, inodeSize);
                if (inode == null || inode.Length < 128) return null;

                ushort iMode = BitConverter.ToUInt16(inode, 0x00);
                if ((iMode & 0xF000) != 0x4000) return null; // 不是目录

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
            catch { }
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
                // 简化处理：假设都在第一个块组
                uint localIndex = (inodeNum - 1) % inodesPerGroup;
                long inodeOffset = inodeTableBlock * blockSize + localIndex * inodeSize;

                var inode = read(inodeOffset, inodeSize);
                if (inode == null || inode.Length < 128) return null;

                ushort iMode = BitConverter.ToUInt16(inode, 0x00);
                if ((iMode & 0xF000) != 0x8000) return null; // 不是普通文件

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
            catch { }
            return null;
        }

        #endregion

        #region DevInfo 分区解析

        /// <summary>
        /// 从 devinfo 分区数据解析硬件信息 (SN/IMEI)
        /// </summary>
        /// <summary>
        /// 解析 proinfo 分区 (联想系列专用)
        /// </summary>
        public void ParseProInfo(byte[] data, DeviceFullInfo info)
        {
            if (data == null || data.Length < 1024) return;

            try
            {
                // 联想 proinfo 中的序列号通常在 0x24 或 0x38 偏移
                string sn = ExtractString(data, 0x24, 32);
                if (string.IsNullOrEmpty(sn) || !IsValidSerialNumber(sn))
                    sn = ExtractString(data, 0x38, 32);

                if (IsValidSerialNumber(sn))
                {
                    info.HardwareSn = sn;
                    info.Sources["SN"] = "proinfo";
                    _logDetail(string.Format("从 proinfo 发现序列号: {0}", sn));
                }

                // 联想型号有时也在 proinfo
                string model = ExtractString(data, 0x200, 64);
                if (!string.IsNullOrEmpty(model) && model.Contains("Lenovo"))
                {
                    info.Model = model.Replace("Lenovo", "").Trim();
                    info.Brand = "Lenovo";
                    _logDetail(string.Format("从 proinfo 发现型号: {0}", model));
                }
            }
            catch (Exception ex)
            {
                _log("解析 proinfo 失败: " + ex.Message);
            }
        }

        public void ParseDevInfo(byte[] data, DeviceFullInfo info)
        {
            if (data == null || data.Length < 512) return;

            try
            {
                // 1. 提取序列号 (通常在偏移 0x00 开始，以 \0 结尾)
                string sn = ExtractString(data, 0, 32);
                if (IsValidSerialNumber(sn))
                {
                    info.Sources["SN"] = "devinfo";
                    // 这里的 SN 通常是硬件 SN，优先于 Sahara 获取的 SerialHex
                    _logDetail(string.Format("从 devinfo 发现序列号: {0}", sn));
                }

                // 2. 尝试提取 IMEI (不同厂家偏移不同)
                // 常见偏移：0x400, 0x800, 0x1000
                string imei1 = "";
                if (data.Length >= 0x500) imei1 = ExtractImei(data, 0x400);
                if (string.IsNullOrEmpty(imei1) && data.Length >= 0x900) imei1 = ExtractImei(data, 0x800);

                if (!string.IsNullOrEmpty(imei1))
                {
                    info.Sources["IMEI"] = "devinfo";
                    _logDetail(string.Format("从 devinfo 发现 IMEI: {0}", imei1));
                }
            }
            catch (Exception ex)
            {
                _log("解析 devinfo 失败: " + ex.Message);
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
            // 简单校验：大部分 SN 是字母+数字
            return sn.All(c => char.IsLetterOrDigit(c));
        }

        #endregion

        #region 综合信息获取

        /// <summary>
        /// 从 Qualcomm 服务获取完整设备信息
        /// </summary>
        public DeviceFullInfo GetInfoFromQualcommService(QualcommService service)
        {
            var info = new DeviceFullInfo();

            if (service == null) return info;

            // 1. Sahara 阶段获取的芯片信息
            var chipInfo = service.ChipInfo;
            if (chipInfo != null)
            {
                info.ChipSerial = chipInfo.SerialHex;
                info.ChipName = chipInfo.ChipName;
                info.HwId = chipInfo.HwIdHex;
                info.PkHash = chipInfo.PkHash;
                info.Vendor = chipInfo.Vendor;

                // 从 PK Hash 推断品牌
                if (info.Vendor == "Unknown" && !string.IsNullOrEmpty(chipInfo.PkHash))
                {
                    info.Vendor = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                }

                info.Sources["ChipInfo"] = "Sahara";
            }

            // 2. Firehose 阶段获取的存储信息
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
            if (!string.IsNullOrEmpty(source.Device) && string.IsNullOrEmpty(target.DeviceCodename))
                target.DeviceCodename = source.Device;
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
                // 智能合成：如果 OTA 版本以 . 开头 (如 .610(CN01))，尝试补全前缀
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
