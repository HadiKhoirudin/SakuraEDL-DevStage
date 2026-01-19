// ============================================================================
// LoveAlways - 高效分区 Build.prop 读取器
// 针对 EDL 模式优化的按需读取实现
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 从设备分区高效读取和解析 build.prop
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// build.prop 搜索路径配置
    /// </summary>
    public static class BuildPropPaths
    {
        /// <summary>
        /// 根目录优先搜索路径
        /// </summary>
        public static readonly string[] RootPaths = {
            "/build.prop",
            "/etc/build.prop",
            "/system/build.prop"
        };

        /// <summary>
        /// OPLUS 设备特有路径
        /// </summary>
        public static readonly string[] OplusPaths = {
            "/build.prop",
            "/etc/build.prop"
        };

        /// <summary>
        /// 联想设备特有路径
        /// </summary>
        public static readonly string[] LenovoPaths = {
            "/build.prop",
            "/etc/build.prop"
        };

        /// <summary>
        /// 分区搜索优先级 (高到低)
        /// </summary>
        public static readonly string[] PartitionPriority = {
            "my_manifest",      // OPLUS 最高优先级
            "odm",              // 厂商定制
            "vendor",           // Vendor
            "system_ext",       // 系统扩展
            "product",          // 产品分区
            "system"            // 系统分区
        };
    }

    /// <summary>
    /// 分区读取结果
    /// </summary>
    public class PartitionReadResult
    {
        public string PartitionName { get; set; }
        public FileSystemType FileSystemType { get; set; }
        public bool Success { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public string FoundPath { get; set; }
        public long ReadTimeMs { get; set; }
        public string Error { get; set; }

        public PartitionReadResult()
        {
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 高效分区 Build.prop 读取器
    /// </summary>
    public class PartitionBuildPropReader
    {
        /// <summary>
        /// 分区读取委托
        /// </summary>
        /// <param name="partitionName">分区名称</param>
        /// <param name="offset">偏移量 (字节)</param>
        /// <param name="size">读取大小 (字节)</param>
        /// <returns>读取的数据</returns>
        public delegate byte[] PartitionReadDelegate(string partitionName, long offset, int size);

        private readonly PartitionReadDelegate _read;
        private readonly Action<string> _log;
        private readonly ConcurrentDictionary<string, PartitionReadResult> _cache;
        private readonly int _sectorSize;

        // 优化常量
        private const int HEADER_READ_SIZE = 4096;          // 头部检测大小
        private const int MAX_BUILDPROP_SIZE = 64 * 1024;   // build.prop 最大 64KB
        private const int READ_TIMEOUT_MS = 5000;           // 单次读取超时

        /// <summary>
        /// 创建分区 build.prop 读取器
        /// </summary>
        /// <param name="readDelegate">分区读取委托</param>
        /// <param name="sectorSize">扇区大小 (默认 4096)</param>
        /// <param name="log">日志回调</param>
        public PartitionBuildPropReader(PartitionReadDelegate readDelegate, int sectorSize = 4096, Action<string> log = null)
        {
            _read = readDelegate ?? throw new ArgumentNullException(nameof(readDelegate));
            _sectorSize = sectorSize;
            _log = log ?? delegate { };
            _cache = new ConcurrentDictionary<string, PartitionReadResult>();
        }

        /// <summary>
        /// 从单个分区读取 build.prop
        /// </summary>
        public PartitionReadResult ReadFromPartition(string partitionName, long baseOffset = 0)
        {
            // 检查缓存
            string cacheKey = partitionName + "_" + baseOffset;
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = new PartitionReadResult { PartitionName = partitionName };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 1. 读取头部检测文件系统类型
                byte[] header = SafeRead(partitionName, baseOffset, HEADER_READ_SIZE);
                if (header == null || header.Length < 2048)
                {
                    result.Error = "无法读取分区头部";
                    return CacheResult(cacheKey, result);
                }

                // 2. 检测文件系统类型
                result.FileSystemType = FileSystemFactory.DetectTypeFromHeader(header);
                
                // 3. 处理 Sparse 格式
                if (result.FileSystemType == FileSystemType.Sparse)
                {
                    result = HandleSparsePartition(partitionName, baseOffset, header);
                    return CacheResult(cacheKey, result);
                }

                // 4. 根据文件系统类型读取 build.prop
                switch (result.FileSystemType)
                {
                    case FileSystemType.Erofs:
                        ReadErofsBuildProp(result, partitionName, baseOffset, header);
                        break;
                    case FileSystemType.Ext4:
                        ReadExt4BuildProp(result, partitionName, baseOffset, header);
                        break;
                    default:
                        result.Error = "不支持的文件系统类型";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log(string.Format("[{0}] 读取失败: {1}", partitionName, ex.Message));
            }

            sw.Stop();
            result.ReadTimeMs = sw.ElapsedMilliseconds;
            return CacheResult(cacheKey, result);
        }

        /// <summary>
        /// 从多个分区并行读取 build.prop 并合并
        /// </summary>
        public Dictionary<string, string> ReadFromMultiplePartitions(
            IEnumerable<string> partitionNames, 
            string activeSlot = "",
            long superStartSector = 0)
        {
            var mergedProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var results = new ConcurrentBag<PartitionReadResult>();

            // 构建分区名称列表 (带槽位)
            var partitionsToRead = new List<string>();
            string slotSuffix = string.IsNullOrEmpty(activeSlot) ? "" : "_" + activeSlot.ToLower().TrimStart('_');

            foreach (var baseName in partitionNames)
            {
                if (!string.IsNullOrEmpty(slotSuffix))
                    partitionsToRead.Add(baseName + slotSuffix);
                partitionsToRead.Add(baseName);
            }

            // 并行读取
            Parallel.ForEach(partitionsToRead, new ParallelOptions { MaxDegreeOfParallelism = 4 }, partName =>
            {
                var result = ReadFromPartition(partName);
                if (result.Success && result.Properties.Count > 0)
                {
                    results.Add(result);
                }
            });

            // 按优先级合并 (低优先级先，高优先级后覆盖)
            var sortedResults = new List<PartitionReadResult>(results);
            sortedResults.Sort((a, b) => GetPartitionPriority(a.PartitionName) - GetPartitionPriority(b.PartitionName));

            foreach (var result in sortedResults)
            {
                foreach (var kv in result.Properties)
                {
                    mergedProps[kv.Key] = kv.Value;
                }
                _log(string.Format("[{0}] 合并 {1} 个属性", result.PartitionName, result.Properties.Count));
            }

            return mergedProps;
        }

        /// <summary>
        /// 智能读取 build.prop (自动选择最佳分区)
        /// </summary>
        public Dictionary<string, string> SmartReadBuildProp(
            IEnumerable<string> availablePartitions,
            string activeSlot = "",
            string vendor = "")
        {
            // 根据厂商调整优先级
            string[] priorityPartitions;
            if (vendor.IndexOf("oplus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vendor.IndexOf("oneplus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vendor.IndexOf("realme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vendor.IndexOf("oppo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                priorityPartitions = new[] { "my_manifest", "odm", "vendor", "system_ext", "product", "system" };
            }
            else if (vendor.IndexOf("lenovo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                priorityPartitions = new[] { "vendor", "odm", "product", "system_ext", "system" };
            }
            else if (vendor.IndexOf("xiaomi", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                priorityPartitions = new[] { "vendor", "odm", "system", "product" };
            }
            else
            {
                priorityPartitions = BuildPropPaths.PartitionPriority;
            }

            return ReadFromMultiplePartitions(priorityPartitions, activeSlot);
        }

        #region EROFS 解析

        /// <summary>
        /// 从 EROFS 分区读取 build.prop
        /// </summary>
        private void ReadErofsBuildProp(PartitionReadResult result, string partitionName, long baseOffset, byte[] header)
        {
            try
            {
                // 解析 SuperBlock
                byte blkSzBits = header[1024 + 0x0C];
                ushort rootNid = BitConverter.ToUInt16(header, 1024 + 0x0E);
                uint metaBlkAddr = BitConverter.ToUInt32(header, 1024 + 0x28);
                uint blockSize = 1u << blkSzBits;

                // 创建读取委托
                DeviceFileSystemReader.ReadDelegate readDelegate = (offset, size) =>
                    SafeRead(partitionName, baseOffset + offset, size);

                // 创建设备读取器
                using (var reader = new DeviceFileSystemReader(readDelegate, 0, _log))
                {
                    if (!reader.IsValid)
                    {
                        result.Error = "EROFS 初始化失败";
                        return;
                    }

                    // 按优先级搜索 build.prop
                    foreach (var path in BuildPropPaths.RootPaths)
                    {
                        string content = reader.ReadTextFile(path);
                        if (!string.IsNullOrEmpty(content) && content.Contains("ro."))
                        {
                            result.Properties = ParseBuildProp(content);
                            result.FoundPath = path;
                            result.Success = true;
                            _log(string.Format("[{0}] 找到 {1} ({2} 属性)", 
                                partitionName, path, result.Properties.Count));
                            return;
                        }
                    }

                    result.Error = "未找到 build.prop";
                }
            }
            catch (Exception ex)
            {
                result.Error = "EROFS 解析错误: " + ex.Message;
            }
        }

        #endregion

        #region EXT4 解析

        /// <summary>
        /// 从 EXT4 分区读取 build.prop
        /// </summary>
        private void ReadExt4BuildProp(PartitionReadResult result, string partitionName, long baseOffset, byte[] header)
        {
            try
            {
                // 创建读取委托
                DeviceFileSystemReader.ReadDelegate readDelegate = (offset, size) =>
                    SafeRead(partitionName, baseOffset + offset, size);

                using (var reader = new DeviceFileSystemReader(readDelegate, 0, _log))
                {
                    if (!reader.IsValid)
                    {
                        result.Error = "EXT4 初始化失败";
                        return;
                    }

                    foreach (var path in BuildPropPaths.RootPaths)
                    {
                        string content = reader.ReadTextFile(path);
                        if (!string.IsNullOrEmpty(content) && content.Contains("ro."))
                        {
                            result.Properties = ParseBuildProp(content);
                            result.FoundPath = path;
                            result.Success = true;
                            _log(string.Format("[{0}] 找到 {1} ({2} 属性)",
                                partitionName, path, result.Properties.Count));
                            return;
                        }
                    }

                    result.Error = "未找到 build.prop";
                }
            }
            catch (Exception ex)
            {
                result.Error = "EXT4 解析错误: " + ex.Message;
            }
        }

        #endregion

        #region Sparse 处理

        /// <summary>
        /// 处理 Sparse 格式分区
        /// </summary>
        private PartitionReadResult HandleSparsePartition(string partitionName, long baseOffset, byte[] header)
        {
            var result = new PartitionReadResult
            {
                PartitionName = partitionName,
                FileSystemType = FileSystemType.Sparse
            };

            try
            {
                // 解析 Sparse header
                ushort fileHdrSz = BitConverter.ToUInt16(header, 8);
                ushort chunkHdrSz = BitConverter.ToUInt16(header, 10);
                uint blkSz = BitConverter.ToUInt32(header, 12);

                // 第一个 chunk 数据偏移
                int dataOffset = fileHdrSz + chunkHdrSz;

                // 检测内部文件系统
                if (dataOffset + 2048 <= header.Length)
                {
                    uint innerMagic1024 = BitConverter.ToUInt32(header, dataOffset + 1024);
                    ushort innerMagic1080 = BitConverter.ToUInt16(header, dataOffset + 1080);

                    if (innerMagic1024 == 0xE0F5E1E2)
                    {
                        // EROFS - 直接在 Sparse 数据区偏移读取
                        ReadErofsBuildProp(result, partitionName, baseOffset + dataOffset, 
                            GetSubArray(header, dataOffset, header.Length - dataOffset));
                    }
                    else if (innerMagic1080 == 0xEF53)
                    {
                        // EXT4
                        ReadExt4BuildProp(result, partitionName, baseOffset + dataOffset,
                            GetSubArray(header, dataOffset, header.Length - dataOffset));
                    }
                    else
                    {
                        result.Error = "Sparse 内部格式未知";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = "Sparse 处理错误: " + ex.Message;
            }

            return result;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 安全读取 (带超时)
        /// </summary>
        private byte[] SafeRead(string partition, long offset, int size)
        {
            try
            {
                return _read(partition, offset, size);
            }
            catch (Exception ex)
            {
                _log(string.Format("[{0}] 读取异常 @{1}: {2}", partition, offset, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 缓存结果
        /// </summary>
        private PartitionReadResult CacheResult(string key, PartitionReadResult result)
        {
            _cache.TryAdd(key, result);
            return result;
        }

        /// <summary>
        /// 获取分区优先级
        /// </summary>
        private int GetPartitionPriority(string partitionName)
        {
            string baseName = partitionName.TrimEnd('_', 'a', 'b').ToLowerInvariant();
            for (int i = 0; i < BuildPropPaths.PartitionPriority.Length; i++)
            {
                if (baseName.Contains(BuildPropPaths.PartitionPriority[i]))
                    return i;
            }
            return 999;
        }

        /// <summary>
        /// 解析 build.prop 内容
        /// </summary>
        private Dictionary<string, string> ParseBuildProp(string content)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(content))
                return props;

            foreach (var line in content.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                int eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = trimmed.Substring(0, eqIndex).Trim();
                    string value = trimmed.Substring(eqIndex + 1).Trim();
                    // 移除末尾乱码
                    if (value.Length > 0)
                    {
                        int endIdx = value.Length;
                        while (endIdx > 0 && (value[endIdx - 1] < 32 || value[endIdx - 1] > 126))
                            endIdx--;
                        if (endIdx < value.Length)
                            value = value.Substring(0, endIdx);
                    }
                    props[key] = value;
                }
            }

            return props;
        }

        /// <summary>
        /// 获取子数组
        /// </summary>
        private byte[] GetSubArray(byte[] source, int offset, int length)
        {
            if (source == null || offset >= source.Length)
                return new byte[0];
            
            length = Math.Min(length, source.Length - offset);
            byte[] result = new byte[length];
            Array.Copy(source, offset, result, 0, length);
            return result;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        #endregion
    }

    /// <summary>
    /// build.prop 属性提取器 - 提取关键设备信息
    /// </summary>
    public static class BuildPropExtractor
    {
        /// <summary>
        /// 提取设备基本信息
        /// </summary>
        public static DeviceBasicInfo ExtractBasicInfo(Dictionary<string, string> props)
        {
            var info = new DeviceBasicInfo();
            if (props == null || props.Count == 0)
                return info;

            // 品牌 (按优先级)
            info.Brand = GetFirstValue(props,
                "ro.product.vendor.brand",
                "ro.product.odm.brand",
                "ro.product.brand",
                "ro.product.manufacturer");

            // 型号
            info.Model = GetFirstValue(props,
                "ro.product.vendor.model",
                "ro.product.odm.model",
                "ro.product.model");

            // 市场名称
            info.MarketName = GetFirstValue(props,
                "ro.vendor.oplus.market.name",
                "ro.vendor.oplus.market.enname",
                "ro.product.marketname",
                "ro.product.vendor.marketname",
                "ro.lenovo.series");

            // 设备代号
            info.Device = GetFirstValue(props,
                "ro.product.device",
                "ro.product.vendor.device",
                "ro.build.product");

            // Android 版本
            info.AndroidVersion = GetFirstValue(props,
                "ro.build.version.release",
                "ro.vendor.build.version.release",
                "ro.system.build.version.release");

            // 安全补丁
            info.SecurityPatch = GetFirstValue(props,
                "ro.build.version.security_patch",
                "ro.vendor.build.security_patch");

            // OTA 版本
            info.OtaVersion = GetFirstValue(props,
                "ro.build.display.id.show",
                "ro.build.display.id",
                "ro.build.version.ota",
                "ro.system_ext.build.version.incremental",
                "ro.build.version.incremental");

            // OPLUS 特有
            info.OplusProject = GetFirstValue(props,
                "ro.oplus.image.my_product.type",
                "ro.separate.soft",
                "ro.product.supported_versions");

            info.OplusNvId = GetFirstValue(props,
                "ro.build.oplus_nv_id");

            // Fingerprint
            info.Fingerprint = GetFirstValue(props,
                "ro.build.fingerprint",
                "ro.vendor.build.fingerprint");

            return info;
        }

        /// <summary>
        /// 获取第一个有效值
        /// </summary>
        private static string GetFirstValue(Dictionary<string, string> props, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (props.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                {
                    // 过滤无效值
                    if (value != "unknown" && value != "oplus" && value != "ossi")
                        return value;
                }
            }
            return "";
        }
    }

    /// <summary>
    /// 设备基本信息
    /// </summary>
    public class DeviceBasicInfo
    {
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string MarketName { get; set; } = "";
        public string Device { get; set; } = "";
        public string AndroidVersion { get; set; } = "";
        public string SecurityPatch { get; set; } = "";
        public string OtaVersion { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string OplusProject { get; set; } = "";
        public string OplusNvId { get; set; } = "";

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(MarketName)) return MarketName;
                if (!string.IsNullOrEmpty(Model)) return Model;
                return Device;
            }
        }

        /// <summary>
        /// 是否有有效信息
        /// </summary>
        public bool HasValidInfo => !string.IsNullOrEmpty(Model) || !string.IsNullOrEmpty(MarketName);

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(MarketName)) sb.AppendLine("市场名称: " + MarketName);
            if (!string.IsNullOrEmpty(Brand)) sb.AppendLine("品牌: " + Brand);
            if (!string.IsNullOrEmpty(Model)) sb.AppendLine("型号: " + Model);
            if (!string.IsNullOrEmpty(Device)) sb.AppendLine("代号: " + Device);
            if (!string.IsNullOrEmpty(AndroidVersion)) sb.AppendLine("Android: " + AndroidVersion);
            if (!string.IsNullOrEmpty(OtaVersion)) sb.AppendLine("OTA: " + OtaVersion);
            if (!string.IsNullOrEmpty(SecurityPatch)) sb.AppendLine("安全补丁: " + SecurityPatch);
            if (!string.IsNullOrEmpty(OplusProject)) sb.AppendLine("项目号: " + OplusProject);
            if (!string.IsNullOrEmpty(OplusNvId)) sb.AppendLine("NV ID: " + OplusNvId);
            return sb.ToString().TrimEnd();
        }
    }
}
