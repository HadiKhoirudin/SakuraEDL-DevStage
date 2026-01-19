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
        /// 提取设备基本信息 (增强版 - 支持小米/OPLUS/联想等)
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
                "ro.product.brand_for_attestation",
                "ro.product.manufacturer");

            // 型号
            info.Model = GetFirstValue(props,
                "ro.product.vendor.model",
                "ro.product.odm.model",
                "ro.product.model",
                "ro.product.model_for_attestation",
                "ro.product.odm.cert");

            // 市场名称 (按优先级: 中兴 -> 小米 -> OPLUS -> 通用 -> 联想)
            info.MarketName = GetFirstValue(props,
                "ro.vendor.product.ztename",      // 中兴/努比亚: "红魔10 Pro/Pro+"
                "ro.product.odm.marketname",      // 小米: "Xiaomi 17"
                "ro.vendor.oplus.market.name",    // OPLUS
                "ro.vendor.oplus.market.enname",
                "ro.product.marketname",
                "ro.product.vendor.marketname",
                "ro.lenovo.series");              // 联想

            // 设备代号
            info.Device = GetFirstValue(props,
                "ro.product.device",
                "ro.product.vendor.device",
                "ro.product.odm.device",
                "ro.product.device_for_attestation",
                "ro.build.product");

            // Fingerprint (先获取，用于后续解析)
            info.Fingerprint = GetFirstValue(props,
                "ro.build.fingerprint",
                "ro.vendor.build.fingerprint",
                "ro.system.build.fingerprint",
                "ro.odm.build.fingerprint");

            // Android 版本
            info.AndroidVersion = GetFirstValue(props,
                "ro.build.version.release",
                "ro.vendor.build.version.release",
                "ro.system.build.version.release");

            // 从 Fingerprint 推断 Android 版本 (如果属性中没有)
            if (string.IsNullOrEmpty(info.AndroidVersion) && !string.IsNullOrEmpty(info.Fingerprint))
            {
                info.AndroidVersion = ParseAndroidVersionFromFingerprint(info.Fingerprint);
            }

            // 安全补丁
            info.SecurityPatch = GetFirstValue(props,
                "ro.build.version.security_patch",
                "ro.vendor.build.security_patch");

            // 从 Fingerprint 推断安全补丁 (如果属性中没有)
            if (string.IsNullOrEmpty(info.SecurityPatch) && !string.IsNullOrEmpty(info.Fingerprint))
            {
                info.SecurityPatch = ParseSecurityPatchFromFingerprint(info.Fingerprint);
            }

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

            // 编译日期
            info.BuildDate = GetFirstValue(props,
                "ro.build.date",
                "ro.vendor.build.date",
                "ro.system.build.date");

            // 编译描述
            info.BuildDescription = GetFirstValue(props,
                "ro.build.description",
                "ro.vendor.build.description");

            // SDK 版本
            info.SdkVersion = GetFirstValue(props,
                "ro.build.version.sdk",
                "ro.system.build.version.sdk",
                "ro.product.first_api_level");

            // 基带版本
            info.BasebandVersion = GetFirstValue(props,
                "gsm.version.baseband",
                "ro.build.expect.baseband");

            // ========== 小米/HyperOS 特有 ==========
            info.MiuiVersion = GetFirstValue(props,
                "ro.miui.ui.version.name",        // MIUI V14.x.x
                "ro.mi.os.version.name");          // HyperOS OS1.0.x

            info.MiuiOtaVersion = GetFirstValue(props,
                "ro.mi.os.version.incremental",   // HyperOS: OS3.0.36.0.WPCCNXM
                "ro.build.version.incremental",
                "ro.system.build.version.incremental");

            info.MiuiRegion = GetFirstValue(props,
                "ro.miui.region",
                "ro.boot.hwc");

            // ========== ColorOS/realmeUI 特有 ==========
            info.ColorOsVersion = GetFirstValue(props,
                "ro.build.display.id",
                "ro.oplus.version");

            // ========== 中兴/努比亚 (ZTE/Nubia) 特有 ==========
            info.ZteInternalBrand = GetFirstValue(props,
                "ro.vendor.feature.brand.internal");  // RedMagic

            info.ZteBaselineVersion = GetFirstValue(props,
                "ro.build.MiFavor_version",          // NebulaOS1.0 (中兴系统基线)
                "ro.vendor.feature.baseline_name");

            info.ZteInternalVersion = GetFirstValue(props,
                "ro.build.sw_internal_version");      // GEN_CN_NX789JV1.0.0B12MR1

            info.ZteRegion = GetFirstValue(props,
                "ro.vendor.feature.zte_region_name",  // China
                "ro.vendor.feature.transform_default_variant_id");  // GEN_CN

            // ========== 联想 (Lenovo) 特有 ==========
            info.LenovoSeries = GetFirstValue(props,
                "ro.lenovo.series",
                "ro.config.versatility");

            return info;
        }

        /// <summary>
        /// 从 Fingerprint 解析 Android 版本
        /// 格式: Brand/Name/Device:AndroidVer/BuildId/Incremental:Type/Keys
        /// 例如: Xiaomi/pudding_in/pudding:16/BQ2A.250705.001/OS3.0.36.0.WPCCNXM:user/release-keys
        /// </summary>
        private static string ParseAndroidVersionFromFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
                return "";

            try
            {
                var parts = fingerprint.Split('/');
                if (parts.Length >= 3)
                {
                    // parts[2] 格式: device:androidVer
                    string deviceVer = parts[2];
                    int colonIdx = deviceVer.IndexOf(':');
                    if (colonIdx > 0 && colonIdx < deviceVer.Length - 1)
                    {
                        return deviceVer.Substring(colonIdx + 1);
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 从 Fingerprint 解析安全补丁日期
        /// 格式中 Build ID 包含日期: BQ2A.250705.001 -> 2025-07-05
        /// </summary>
        private static string ParseSecurityPatchFromFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
                return "";

            try
            {
                // 查找 YYMMDD.NNN 格式
                var match = System.Text.RegularExpressions.Regex.Match(fingerprint, @"(\d{6})\.\d{3}");
                if (match.Success)
                {
                    string dateStr = match.Groups[1].Value;
                    if (dateStr.Length == 6)
                    {
                        string year = "20" + dateStr.Substring(0, 2);
                        string month = dateStr.Substring(2, 2);
                        string day = dateStr.Substring(4, 2);
                        return string.Format("{0}-{1}-{2}", year, month, day);
                    }
                }
            }
            catch { }
            return "";
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
    /// 设备基本信息 (增强版)
    /// </summary>
    public class DeviceBasicInfo
    {
        // 基本信息
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string MarketName { get; set; } = "";
        public string Device { get; set; } = "";
        
        // 版本信息
        public string AndroidVersion { get; set; } = "";
        public string SecurityPatch { get; set; } = "";
        public string OtaVersion { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string BuildDate { get; set; } = "";
        public string BuildDescription { get; set; } = "";
        public string SdkVersion { get; set; } = "";
        public string BasebandVersion { get; set; } = "";
        
        // OPLUS 特有
        public string OplusProject { get; set; } = "";
        public string OplusNvId { get; set; } = "";
        public string ColorOsVersion { get; set; } = "";
        
        // 小米/HyperOS 特有
        public string MiuiVersion { get; set; } = "";
        public string MiuiOtaVersion { get; set; } = "";
        public string MiuiRegion { get; set; } = "";

        // 中兴/努比亚 (ZTE/Nubia) 特有
        public string ZteInternalBrand { get; set; } = "";    // RedMagic
        public string ZteBaselineVersion { get; set; } = "";  // NebulaOS1.0
        public string ZteInternalVersion { get; set; } = "";  // GEN_CN_NX789JV1.0.0B12MR1
        public string ZteRegion { get; set; } = "";           // China / GEN_CN

        // 联想 (Lenovo) 特有
        public string LenovoSeries { get; set; } = "";

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

        /// <summary>
        /// 获取完整 OTA 版本 (智能选择)
        /// </summary>
        public string FullOtaVersion
        {
            get
            {
                // MIUI/HyperOS 优先 (格式如 OS3.0.36.0.WPCCNXM)
                if (!string.IsNullOrEmpty(MiuiOtaVersion) && 
                    (MiuiOtaVersion.StartsWith("OS") || MiuiOtaVersion.StartsWith("V")))
                    return MiuiOtaVersion;
                // ColorOS
                if (!string.IsNullOrEmpty(ColorOsVersion) && ColorOsVersion.Length > 5)
                    return ColorOsVersion;
                // 通用 OTA
                return OtaVersion;
            }
        }

        /// <summary>
        /// 获取系统类型名称
        /// </summary>
        public string SystemTypeName
        {
            get
            {
                string brandLower = (Brand ?? "").ToLowerInvariant();
                string internalBrand = (ZteInternalBrand ?? "").ToLowerInvariant();
                
                // 小米系
                if (brandLower.Contains("xiaomi") || brandLower.Contains("redmi") || brandLower.Contains("poco"))
                {
                    // 判断 HyperOS 还是 MIUI
                    if (!string.IsNullOrEmpty(MiuiOtaVersion) && MiuiOtaVersion.StartsWith("OS"))
                        return "HyperOS";
                    if (!string.IsNullOrEmpty(MiuiVersion) && MiuiVersion.StartsWith("OS"))
                        return "HyperOS";
                    return "MIUI";
                }
                
                // OPLUS 系
                if (brandLower.Contains("oppo") || brandLower.Contains("oneplus") || brandLower.Contains("realme"))
                    return "ColorOS";
                
                // 中兴/努比亚 (使用 RedMagicOS 或 NebulaOS)
                if (brandLower.Contains("nubia") || brandLower.Contains("zte") || 
                    internalBrand.Contains("redmagic"))
                {
                    // RedMagic 设备
                    if (internalBrand.Contains("redmagic") || 
                        (!string.IsNullOrEmpty(ColorOsVersion) && ColorOsVersion.Contains("RedMagicOS")))
                        return "RedMagicOS";
                    // 普通 ZTE/Nubia
                    if (!string.IsNullOrEmpty(ZteBaselineVersion))
                        return ZteBaselineVersion;  // NebulaOS1.0
                    return "MiFavor";
                }
                
                // 联想
                if (brandLower.Contains("lenovo") || brandLower.Contains("motorola"))
                    return "ZUI";
                
                return "Android";
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("           设 备 详 细 信 息            ");
            sb.AppendLine("═══════════════════════════════════════");
            
            // 基本信息
            if (!string.IsNullOrEmpty(MarketName)) sb.AppendLine("  市场名称 : " + MarketName);
            if (!string.IsNullOrEmpty(Brand)) sb.AppendLine("  品    牌 : " + Brand);
            if (!string.IsNullOrEmpty(Model)) sb.AppendLine("  型    号 : " + Model);
            if (!string.IsNullOrEmpty(Device)) sb.AppendLine("  代    号 : " + Device);
            
            sb.AppendLine("───────────────────────────────────────");
            
            // 系统版本
            if (!string.IsNullOrEmpty(AndroidVersion)) sb.AppendLine("  Android  : " + AndroidVersion);
            if (!string.IsNullOrEmpty(SdkVersion)) sb.AppendLine("  SDK 版本 : " + SdkVersion);
            
            // 系统类型 + OTA 版本 (智能显示)
            string sysType = SystemTypeName;
            string otaDisplay = FullOtaVersion;
            if (!string.IsNullOrEmpty(otaDisplay))
            {
                sb.AppendLine(string.Format("  {0,-8} : {1}", sysType, otaDisplay));
            }
            
            // MIUI/HyperOS 版本名
            if (!string.IsNullOrEmpty(MiuiVersion) && MiuiVersion != otaDisplay) 
                sb.AppendLine("  版 本 名 : " + MiuiVersion);
            
            // 区域 (小米/中兴)
            string regionDisplay = !string.IsNullOrEmpty(MiuiRegion) ? MiuiRegion : ZteRegion;
            if (!string.IsNullOrEmpty(regionDisplay))
                sb.AppendLine("  区    域 : " + regionDisplay);
            
            // ColorOS (如果不同于 OTA)
            if (!string.IsNullOrEmpty(ColorOsVersion) && ColorOsVersion != otaDisplay) 
                sb.AppendLine("  ColorOS  : " + ColorOsVersion);
            
            // 中兴/努比亚 内部品牌
            if (!string.IsNullOrEmpty(ZteInternalBrand))
                sb.AppendLine("  内部品牌 : " + ZteInternalBrand);
            
            // 中兴/努比亚 内部版本
            if (!string.IsNullOrEmpty(ZteInternalVersion))
                sb.AppendLine("  内部版本 : " + ZteInternalVersion);
            
            if (!string.IsNullOrEmpty(SecurityPatch)) sb.AppendLine("  安全补丁 : " + SecurityPatch);
            if (!string.IsNullOrEmpty(BuildDate)) sb.AppendLine("  编译日期 : " + BuildDate);
            
            sb.AppendLine("───────────────────────────────────────");
            
            // 项目信息
            if (!string.IsNullOrEmpty(OplusProject)) sb.AppendLine("  项 目 号 : " + OplusProject);
            if (!string.IsNullOrEmpty(OplusNvId)) sb.AppendLine("  NV ID    : " + OplusNvId);
            if (!string.IsNullOrEmpty(LenovoSeries)) sb.AppendLine("  联想系列 : " + LenovoSeries);
            if (!string.IsNullOrEmpty(BasebandVersion)) sb.AppendLine("  基带版本 : " + BasebandVersion);
            
            // Fingerprint (截断显示)
            if (!string.IsNullOrEmpty(Fingerprint))
            {
                string fp = Fingerprint.Length > 60 ? Fingerprint.Substring(0, 57) + "..." : Fingerprint;
                sb.AppendLine("  Fingerprint: " + fp);
            }
            
            sb.AppendLine("═══════════════════════════════════════");
            return sb.ToString();
        }
    }
}
