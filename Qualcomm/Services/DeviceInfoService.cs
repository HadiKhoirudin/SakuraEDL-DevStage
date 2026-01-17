// ============================================================================
// LoveAlways - 设备信息服务
// 支持从 Sahara、Firehose、Super 分区、build.prop 等多种来源获取设备信息
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public string DisplayId { get; set; }

        // 存储信息
        public string StorageType { get; set; }
        public int SectorSize { get; set; }
        public bool IsAbDevice { get; set; }
        public string CurrentSlot { get; set; }

        // OPLUS 特有信息
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

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
        public string Incremental { get; set; }
        public string BootSlot { get; set; }

        // OPLUS 特有
        public string OplusCpuInfo { get; set; }
        public string OplusNvId { get; set; }
        public string OplusProject { get; set; }

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
    /// LP 分区信息
    /// </summary>
    public class LpPartitionInfo
    {
        public string Name { get; set; }
        public uint Attrs { get; set; }
        public uint FirstExtent { get; set; }
        public uint NumExtents { get; set; }
        public uint GroupIndex { get; set; }
        public long StartOffset { get; set; }
        public long Size { get; set; }
        public string FileSystem { get; set; }

        public LpPartitionInfo()
        {
            Name = "";
            FileSystem = "unknown";
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
        private const uint LP_METADATA_HEADER_MAGIC = 0x414c5030;    // "0PLA"
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
        /// 从 build.prop 内容解析设备信息
        /// </summary>
        public BuildPropInfo ParseBuildProp(string content)
        {
            var info = new BuildPropInfo();
            if (string.IsNullOrEmpty(content)) return info;

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;

                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();

                // 跳过包含无效字符的行
                if (key.Contains(" ") || string.IsNullOrEmpty(value)) continue;

                info.AllProperties[key] = value;

                // 按优先级解析各属性
                switch (key)
                {
                    // 品牌
                    case "ro.product.vendor.brand":
                    case "ro.product.odm.brand":
                        if (string.IsNullOrEmpty(info.Brand) || info.Brand == "oplus")
                            info.Brand = value;
                        break;
                    case "ro.product.brand":
                    case "ro.product.system.brand":
                        if (string.IsNullOrEmpty(info.Brand))
                            info.Brand = value;
                        break;

                    // 型号
                    case "ro.product.vendor.model":
                    case "ro.product.odm.model":
                    case "ro.product.model":
                        if (string.IsNullOrEmpty(info.Model))
                            info.Model = value;
                        break;

                    // 设备代号
                    case "ro.product.vendor.device":
                    case "ro.product.odm.device":
                    case "ro.product.device":
                        if (string.IsNullOrEmpty(info.Device))
                            info.Device = value;
                        break;

                    // 市场名称 (中文)
                    case "ro.vendor.oplus.market.name":
                    case "ro.product.marketname":
                    case "ro.config.marketing_name":
                        if (string.IsNullOrEmpty(info.MarketName))
                            info.MarketName = value;
                        break;

                    // 市场名称 (英文)
                    case "ro.vendor.oplus.market.enname":
                        if (string.IsNullOrEmpty(info.MarketNameEn))
                            info.MarketNameEn = value;
                        break;

                    // 制造商
                    case "ro.product.manufacturer":
                    case "ro.product.vendor.manufacturer":
                        if (string.IsNullOrEmpty(info.Manufacturer))
                            info.Manufacturer = value;
                        break;

                    // Android 版本
                    case "ro.build.version.release":
                    case "ro.system.build.version.release":
                    case "ro.product.build.version.release":
                        if (string.IsNullOrEmpty(info.AndroidVersion))
                            info.AndroidVersion = value;
                        break;

                    // SDK 版本
                    case "ro.build.version.sdk":
                    case "ro.system.build.version.sdk":
                        if (string.IsNullOrEmpty(info.SdkVersion))
                            info.SdkVersion = value;
                        break;

                    // 安全补丁
                    case "ro.build.version.security_patch":
                    case "ro.vendor.build.security_patch":
                        if (string.IsNullOrEmpty(info.SecurityPatch))
                            info.SecurityPatch = value;
                        break;

                    // Build ID
                    case "ro.build.id":
                        if (string.IsNullOrEmpty(info.BuildId))
                            info.BuildId = value;
                        break;

                    // Fingerprint
                    case "ro.build.fingerprint":
                    case "ro.vendor.build.fingerprint":
                        if (string.IsNullOrEmpty(info.Fingerprint))
                            info.Fingerprint = value;
                        break;

                    // Display ID
                    case "ro.build.display.id":
                    case "ro.build.display.id.show":
                        if (string.IsNullOrEmpty(info.DisplayId))
                            info.DisplayId = value;
                        break;

                    // OTA 版本
                    case "ro.build.display.full_id":
                    case "ro.build.version.ota":
                        if (string.IsNullOrEmpty(info.OtaVersion))
                            info.OtaVersion = value;
                        break;

                    // 版本号
                    case "ro.build.version.incremental":
                    case "ro.vendor.build.version.incremental":
                        if (string.IsNullOrEmpty(info.Incremental))
                            info.Incremental = value;
                        break;

                    // 启动槽位
                    case "ro.boot.slot_suffix":
                        info.BootSlot = value.TrimStart('_');
                        break;

                    // OPLUS 特有
                    case "ro.product.oplus.cpuinfo":
                    case "ro.vendor.oplus.cpuinfo":
                        if (string.IsNullOrEmpty(info.OplusCpuInfo))
                            info.OplusCpuInfo = value;
                        break;

                    case "ro.build.oplus_nv_id":
                        if (string.IsNullOrEmpty(info.OplusNvId))
                            info.OplusNvId = value;
                        break;

                    case "ro.separate.soft":
                        if (string.IsNullOrEmpty(info.OplusProject))
                            info.OplusProject = value;
                        break;
                }
            }

            // 后处理：如果品牌为空但制造商不为空
            if (string.IsNullOrEmpty(info.Brand) && !string.IsNullOrEmpty(info.Manufacturer))
            {
                info.Brand = info.Manufacturer;
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
        /// 解析 LP Metadata - 从设备按需读取
        /// </summary>
        public List<LpPartitionInfo> ParseLpMetadataFromDevice(DeviceReadDelegate readFromDevice)
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
                _logDetail($"Geometry Magic: 0x{magic:X8}");

                if (magic != LP_METADATA_GEOMETRY_MAGIC)
                {
                    _log("无效的 LP Geometry magic");
                    return null;
                }

                uint metadataMaxSize = BitConverter.ToUInt32(geometryData, 40);
                uint metadataSlotCount = BitConverter.ToUInt32(geometryData, 44);
                uint logicalBlockSize = BitConverter.ToUInt32(geometryData, 48);

                _logDetail($"Metadata Max Size: {metadataMaxSize}, Slot Count: {metadataSlotCount}");

                // 2. 读取 Metadata (偏移 0x3000 = 12288)
                long metadataOffset = 4096 + 4096 * 2;
                int initialReadSize = 4096;
                var metadataData = readFromDevice(metadataOffset, initialReadSize);

                if (metadataData == null || metadataData.Length < 256)
                {
                    _log("无法读取 LP Metadata");
                    return null;
                }

                uint headerMagic = BitConverter.ToUInt32(metadataData, 0);
                _logDetail($"Metadata Header Magic: 0x{headerMagic:X8}");

                if (headerMagic != LP_METADATA_HEADER_MAGIC)
                {
                    _log("无效的 LP Metadata magic");
                    return null;
                }

                // 读取完整 metadata
                if (metadataData.Length < metadataMaxSize && metadataMaxSize <= 65536)
                {
                    var fullMetadata = readFromDevice(metadataOffset, (int)metadataMaxSize);
                    if (fullMetadata != null && fullMetadata.Length >= metadataData.Length)
                    {
                        metadataData = fullMetadata;
                    }
                }

                uint headerSize = BitConverter.ToUInt32(metadataData, 8);

                // 3. 解析表描述符
                int tablesOffset = 0x50;
                uint partOffset = BitConverter.ToUInt32(metadataData, tablesOffset);
                uint partNum = BitConverter.ToUInt32(metadataData, tablesOffset + 4);
                uint partEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 8);
                uint extOffset = BitConverter.ToUInt32(metadataData, tablesOffset + 12);
                uint extNum = BitConverter.ToUInt32(metadataData, tablesOffset + 16);
                uint extEntrySize = BitConverter.ToUInt32(metadataData, tablesOffset + 20);

                _logDetail($"Partitions: count={partNum}, Extents: count={extNum}");

                // 4. 解析 extents
                var extents = new List<Tuple<long, uint, long, uint>>();
                int tablesBase = (int)headerSize;

                for (int i = 0; i < extNum; i++)
                {
                    int entryOffset = tablesBase + (int)extOffset + i * (int)extEntrySize;
                    if (entryOffset + extEntrySize > metadataData.Length) break;

                    long numSectors = BitConverter.ToInt64(metadataData, entryOffset);
                    uint targetType = BitConverter.ToUInt32(metadataData, entryOffset + 8);
                    long targetData = BitConverter.ToInt64(metadataData, entryOffset + 12);
                    uint targetSource = BitConverter.ToUInt32(metadataData, entryOffset + 20);

                    extents.Add(Tuple.Create(numSectors, targetType, targetData, targetSource));
                }

                // 5. 解析分区
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
                    uint groupIndex = BitConverter.ToUInt32(metadataData, entryOffset + 48);

                    var partition = new LpPartitionInfo
                    {
                        Name = name,
                        Attrs = attrs,
                        FirstExtent = firstExtent,
                        NumExtents = numExtents,
                        GroupIndex = groupIndex
                    };

                    if (numExtents > 0 && firstExtent < extents.Count)
                    {
                        var ext = extents[(int)firstExtent];
                        partition.StartOffset = ext.Item3 * 512;
                        partition.Size = ext.Item1 * 512;
                    }

                    partitions.Add(partition);
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
        /// 从设备 Super 分区在线读取 build.prop
        /// </summary>
        /// <param name="readFromSuper">读取 Super 分区数据的委托</param>
        /// <returns>解析后的 build.prop 信息</returns>
        public BuildPropInfo ReadBuildPropFromDevice(DeviceReadDelegate readFromSuper)
        {
            try
            {
                _log("开始从设备读取 build.prop...");

                // 1. 解析 LP Metadata
                var lpPartitions = ParseLpMetadataFromDevice(readFromSuper);
                if (lpPartitions == null || lpPartitions.Count == 0)
                {
                    _log("无法解析 LP Metadata");
                    return null;
                }

                _log(string.Format("发现 {0} 个 LP 分区", lpPartitions.Count));

                // 2. 查找 system_a 或 vendor_a 分区
                LpPartitionInfo targetPartition = null;
                foreach (var p in lpPartitions)
                {
                    // 优先使用 vendor_a (通常包含更准确的设备信息)
                    if (p.Name == "vendor_a" && p.NumExtents > 0)
                    {
                        targetPartition = p;
                        break;
                    }
                    if (p.Name == "system_a" && p.NumExtents > 0 && targetPartition == null)
                    {
                        targetPartition = p;
                    }
                }

                // 尝试非 A/B 分区
                if (targetPartition == null)
                {
                    foreach (var p in lpPartitions)
                    {
                        if (p.Name == "vendor" && p.NumExtents > 0)
                        {
                            targetPartition = p;
                            break;
                        }
                        if (p.Name == "system" && p.NumExtents > 0 && targetPartition == null)
                        {
                            targetPartition = p;
                        }
                    }
                }

                if (targetPartition == null)
                {
                    _log("未找到可读取的 system/vendor 分区");
                    return null;
                }

                _log(string.Format("目标分区: {0}, 偏移: 0x{1:X}, 大小: {2}MB",
                    targetPartition.Name, targetPartition.StartOffset, targetPartition.Size / 1024 / 1024));

                // 3. 读取分区头部检测文件系统
                byte[] partitionData = readFromSuper(targetPartition.StartOffset, 8192);
                if (partitionData == null || partitionData.Length < 2048)
                {
                    _log("无法读取分区数据");
                    return null;
                }

                string fsType = DetectFileSystem(partitionData);
                targetPartition.FileSystem = fsType;
                _log(string.Format("文件系统类型: {0}", fsType));

                // 4. 根据文件系统类型解析 build.prop
                if (fsType == "erofs")
                {
                    return ParseErofsAndFindBuildProp(readFromSuper, targetPartition);
                }
                else if (fsType == "ext4")
                {
                    return ParseExt4AndFindBuildProp(readFromSuper, targetPartition);
                }
                else
                {
                    _log(string.Format("不支持的文件系统: {0}", fsType));
                    return null;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("读取 build.prop 失败: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 从 EROFS 分区解析 build.prop
        /// </summary>
        private BuildPropInfo ParseErofsAndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition)
        {
            try
            {
                // 创建一个读取委托，将偏移转换为分区内的绝对偏移
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(partition.StartOffset + offset, size);
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
        private BuildPropInfo ParseExt4AndFindBuildProp(DeviceReadDelegate readFromSuper, LpPartitionInfo partition)
        {
            try
            {
                // 创建读取委托
                DeviceReadDelegate readFromPartition = (offset, size) =>
                {
                    return readFromSuper(partition.StartOffset + offset, size);
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
                target.OtaVersion = source.OtaVersion;
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
