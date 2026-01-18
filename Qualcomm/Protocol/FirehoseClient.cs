// ============================================================================
// LoveAlways - Firehose 协议完整实现
// Firehose Protocol - 高通 EDL 模式 XML 刷写协议
// ============================================================================
// 模块: Qualcomm.Protocol
// 功能: 读写分区、VIP 认证、GPT 操作、设备控制
// 支持: UFS/eMMC 存储、Sparse 格式、动态伪装
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Models;

namespace LoveAlways.Qualcomm.Protocol
{
    #region 错误处理

    /// <summary>
    /// Firehose 错误码助手
    /// </summary>
    public static class FirehoseErrorHelper
    {
        public static void ParseNakError(string errorText, out string message, out string suggestion, out bool isFatal, out bool canRetry)
        {
            message = "未知错误";
            suggestion = "请重试操作";
            isFatal = false;
            canRetry = true;

            if (string.IsNullOrEmpty(errorText))
                return;

            string lower = errorText.ToLowerInvariant();

            if (lower.Contains("authentication") || lower.Contains("auth failed"))
            {
                message = "认证失败";
                suggestion = "设备需要特殊认证";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("signature") || lower.Contains("sign"))
            {
                message = "签名验证失败";
                suggestion = "镜像签名不正确";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("hash") && (lower.Contains("mismatch") || lower.Contains("fail")))
            {
                message = "Hash 校验失败";
                suggestion = "数据完整性验证失败";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("partition not found"))
            {
                message = "分区未找到";
                suggestion = "设备上不存在此分区";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("invalid lun"))
            {
                message = "无效的 LUN";
                suggestion = "指定的 LUN 不存在";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("write protect"))
            {
                message = "写保护";
                suggestion = "存储设备处于写保护状态";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("timeout"))
            {
                message = "超时";
                suggestion = "操作超时，建议重试";
                isFatal = false;
                canRetry = true;
            }
            else if (lower.Contains("busy"))
            {
                message = "设备忙";
                suggestion = "设备正在处理其他操作";
                isFatal = false;
                canRetry = true;
            }
            else
            {
                message = "设备错误: " + errorText;
                suggestion = "请查看完整错误信息";
            }
        }
    }

    #endregion

    #region VIP 伪装策略

    /// <summary>
    /// VIP 伪装策略
    /// </summary>
    public struct VipSpoofStrategy
    {
        public string Filename { get; private set; }
        public string Label { get; private set; }
        public int Priority { get; private set; }

        public VipSpoofStrategy(string filename, string label, int priority)
        {
            Filename = filename;
            Label = label;
            Priority = priority;
        }

        public override string ToString()
        {
            return string.Format("{0}/{1}", Label, Filename);
        }
    }

    #endregion

    /// <summary>
    /// Firehose 协议客户端 - 完整版
    /// </summary>
    public class FirehoseClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string> _log;
        private readonly Action<long, long> _progress;
        private bool _disposed;
        private readonly StringBuilder _rxBuffer = new StringBuilder();

        // 配置 - 速度优化
        private int _sectorSize = 4096;
        private int _maxPayloadSize = 16777216; // 16MB 默认 payload

        private const int ACK_TIMEOUT_MS = 15000;          // 大文件需要更长超时
        private const int FILE_BUFFER_SIZE = 4 * 1024 * 1024;  // 4MB 文件缓冲 (提高读取速度)
        private const int OPTIMAL_PAYLOAD_REQUEST = 16 * 1024 * 1024; // 请求 16MB payload (设备可能返回较小值)

        // 公开属性
        public string StorageType { get; private set; }
        public int SectorSize { get { return _sectorSize; } }
        public int MaxPayloadSize { get { return _maxPayloadSize; } }
        public List<string> SupportedFunctions { get; private set; }

        // 芯片信息
        public string ChipSerial { get; set; }
        public string ChipHwId { get; set; }
        public string ChipPkHash { get; set; }

        // 每个 LUN 的 GPT Header 信息 (用于负扇区转换)
        private Dictionary<int, GptHeaderInfo> _lunHeaders = new Dictionary<int, GptHeaderInfo>();

        /// <summary>
        /// 获取 LUN 的总扇区数 (用于负扇区转换)
        /// </summary>
        public long GetLunTotalSectors(int lun)
        {
            GptHeaderInfo header;
            if (_lunHeaders.TryGetValue(lun, out header))
            {
                // AlternateLba 是备份 GPT Header 的位置 (通常是磁盘最后一个扇区)
                // 总扇区数 = AlternateLba + 1
                return (long)(header.AlternateLba + 1);
            }
            return -1; // 未知
        }

        /// <summary>
        /// 将负扇区转换为绝对扇区 (负数表示从磁盘末尾倒数)
        /// </summary>
        public long ResolveNegativeSector(int lun, long sector)
        {
            if (sector >= 0) return sector;
            
            long totalSectors = GetLunTotalSectors(lun);
            if (totalSectors <= 0)
            {
                _log(string.Format("[GPT] 无法解析负扇区: LUN{0} 总扇区数未知", lun));
                return -1;
            }
            
            // 负数扇区表示从末尾倒数
            // 例如: -5 表示 totalSectors - 5
            long absoluteSector = totalSectors + sector;
            _log(string.Format("[GPT] 负扇区转换: LUN{0} sector {1} -> {2} (总扇区: {3})", 
                lun, sector, absoluteSector, totalSectors));
            return absoluteSector;
        }

        // OnePlus 认证参数 (认证成功后保存，写入时附带)
        public string OnePlusProgramToken { get; set; }
        public string OnePlusProgramPk { get; set; }
        public string OnePlusProjId { get; set; }
        public bool IsOnePlusAuthenticated { get { return !string.IsNullOrEmpty(OnePlusProgramToken); } }

        // 分区缓存
        private List<PartitionInfo> _cachedPartitions = null;

        // 速度统计
        private Stopwatch _transferStopwatch;
        private long _transferTotalBytes;

        public bool IsConnected { get { return _port.IsOpen; } }

        public FirehoseClient(SerialPortManager port, Action<string> log = null, Action<long, long> progress = null)
        {
            _port = port;
            _log = log ?? delegate { };
            _progress = progress;
            StorageType = "ufs";
            SupportedFunctions = new List<string>();
            ChipSerial = "";
            ChipHwId = "";
            ChipPkHash = "";
        }

        /// <summary>
        /// 报告字节级进度 (用于速度计算)
        /// </summary>
        public void ReportProgress(long current, long total)
        {
            if (_progress != null)
                _progress(current, total);
        }

        #region 动态伪装策略

        /// <summary>
        /// 获取动态伪装策略列表
        /// </summary>
        public static List<VipSpoofStrategy> GetDynamicSpoofStrategies(int lun, long startSector, string partitionName, bool isGptRead)
        {
            var strategies = new List<VipSpoofStrategy>();

            // GPT 区域特殊处理
            if (isGptRead || startSector <= 33)
            {
                strategies.Add(new VipSpoofStrategy(string.Format("gpt_backup{0}.bin", lun), "BackupGPT", 0));
                strategies.Add(new VipSpoofStrategy(string.Format("gpt_main{0}.bin", lun), "PrimaryGPT", 1));
            }

            // 通用 backup 伪装
            strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", "BackupGPT", 2));

            // 分区名称伪装
            if (!string.IsNullOrEmpty(partitionName))
            {
                string safeName = SanitizePartitionName(partitionName);
                strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", safeName, 3));
                strategies.Add(new VipSpoofStrategy(safeName + ".bin", safeName, 4));
            }

            // 通用伪装
            strategies.Add(new VipSpoofStrategy("ssd", "ssd", 5));
            strategies.Add(new VipSpoofStrategy("gpt_main0.bin", "gpt_main0.bin", 6));
            strategies.Add(new VipSpoofStrategy("buffer.bin", "buffer", 8));

            // 无伪装
            strategies.Add(new VipSpoofStrategy("", "", 99));

            return strategies;
        }

        private static string SanitizePartitionName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "rawdata";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                bool isValid = true;
                foreach (char inv in invalid)
                {
                    if (c == inv) { isValid = false; break; }
                }
                if (isValid) sb.Append(c);
            }

            string safeName = sb.ToString().ToLowerInvariant();
            if (safeName.Length > 32) safeName = safeName.Substring(0, 32);
            return string.IsNullOrEmpty(safeName) ? "rawdata" : safeName;
        }

        #endregion

        #region 基础配置

        /// <summary>
        /// 配置 Firehose
        /// </summary>
        public async Task<bool> ConfigureAsync(string storageType = "ufs", int preferredPayloadSize = 0, CancellationToken ct = default(CancellationToken))
        {
            StorageType = storageType.ToLower();
            _sectorSize = (StorageType == "emmc") ? 512 : 4096;

            int requestedPayload = preferredPayloadSize > 0 ? preferredPayloadSize : OPTIMAL_PAYLOAD_REQUEST;

            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data><configure MemoryName=\"{0}\" Verbose=\"0\" " +
                "AlwaysValidate=\"0\" MaxPayloadSizeToTargetInBytes=\"{1}\" ZlpAwareHost=\"0\" " +
                "SkipStorageInit=\"0\" CheckDevinfo=\"0\" EnableFlash=\"1\" /></data>",
                storageType, requestedPayload);

            _log("[Firehose] 配置设备...");
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            for (int i = 0; i < 50; i++)
            {
                if (ct.IsCancellationRequested) return false;

                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    string val = resp.Attribute("value") != null ? resp.Attribute("value").Value : "";
                    bool isAck = val.Equals("ACK", StringComparison.OrdinalIgnoreCase);

                    if (isAck || val.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                    {
                        var ssAttr = resp.Attribute("SectorSizeInBytes");
                        if (ssAttr != null)
                        {
                            int size;
                            if (int.TryParse(ssAttr.Value, out size)) _sectorSize = size;
                        }

                        var mpAttr = resp.Attribute("MaxPayloadSizeToTargetInBytes");
                        if (mpAttr != null)
                        {
                            int maxPayload;
                            if (int.TryParse(mpAttr.Value, out maxPayload) && maxPayload > 0)
                                _maxPayloadSize = Math.Max(64 * 1024, Math.Min(maxPayload, 16 * 1024 * 1024));
                        }

                        _log(string.Format("[Firehose] 配置成功 - SectorSize:{0}, MaxPayload:{1}KB", _sectorSize, _maxPayloadSize / 1024));
                        return true;
                    }
                }
                await Task.Delay(50, ct);
            }
            return false;
        }

        /// <summary>
        /// 设置存储扇区大小
        /// </summary>
        public void SetSectorSize(int size)
        {
            _sectorSize = size;
        }

        #endregion

        #region 读取分区表

        /// <summary>
        /// 读取 GPT 分区表 (支持多 LUN)
        /// </summary>
        public async Task<List<PartitionInfo>> ReadGptPartitionsAsync(bool useVipMode = false, CancellationToken ct = default(CancellationToken), IProgress<int> lunProgress = null)
        {
            var partitions = new List<PartitionInfo>();
            
            // 重置槽位检测状态，准备合并所有 LUN 的结果
            ResetSlotDetection();

            for (int lun = 0; lun < 6; lun++)
            {
                // 报告当前 LUN 进度
                if (lunProgress != null) lunProgress.Report(lun);
                byte[] gptData = null;

                // GPT 头在 LBA 1，分区条目从 LBA 2 开始
                // 小米/Redmi 设备可能有超过 128 个分区条目（最多 256 个）
                // 256 个条目 * 128 字节 = 32KB
                // 对于 512 字节扇区: 32KB / 512 = 64 个扇区 + 2 (MBR+Header) = 66 个
                // 对于 4096 字节扇区: 32KB / 4096 = 8 个扇区 + 2 = 10 个
                // 读取 256 个扇区确保覆盖所有可能的分区条目（包括小米设备）
                // 对于 512B 扇区 = 128KB，对于 4KB 扇区 = 1MB
                int gptSectors = 256;

                if (useVipMode)
                {
                    var readStrategies = new string[,]
                    {
                        { "PrimaryGPT", string.Format("gpt_main{0}.bin", lun) },
                        { "BackupGPT", string.Format("gpt_backup{0}.bin", lun) },
                        { "ssd", "ssd" }
                    };

                    for (int i = 0; i < readStrategies.GetLength(0); i++)
                    {
                        try
                        {
                            gptData = await ReadGptPacketAsync(lun, 0, gptSectors, readStrategies[i, 0], readStrategies[i, 1], ct);
                            if (gptData != null && gptData.Length >= 512)
                            {
                                _log(string.Format("[GPT] LUN{0} 使用伪装 {1} 成功", lun, readStrategies[i, 0]));
                                break;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    try
                    {
                        PurgeBuffer();
                        if (lun > 0) await Task.Delay(50, ct);

                        _log(string.Format("[GPT] 读取 LUN{0}...", lun));
                        gptData = await ReadSectorsAsync(lun, 0, gptSectors, ct);
                        if (gptData != null && gptData.Length >= 512)
                        {
                            _log(string.Format("[GPT] LUN{0} 读取成功 ({1} 字节)", lun, gptData.Length));
                        }
                    }
                    catch (Exception ex)
                    {
                        _log(string.Format("[GPT] LUN{0} 读取异常: {1}", lun, ex.Message));
                    }
                }

                if (gptData == null || gptData.Length < 512)
                    continue;

                var lunPartitions = ParseGptPartitions(gptData, lun);
                if (lunPartitions.Count > 0)
                {
                    partitions.AddRange(lunPartitions);
                    _log(string.Format("[Firehose] LUN {0}: {1} 个分区", lun, lunPartitions.Count));
                }
            }

            if (partitions.Count > 0)
            {
                _cachedPartitions = partitions;
                _log(string.Format("[Firehose] 共读取 {0} 个分区", partitions.Count));
                
                // 输出合并后的槽位状态
                if (_mergedSlot != "nonexistent")
                {
                    _log(string.Format("[Firehose] 设备槽位: {0} (A激活={1}, B激活={2})", 
                        _mergedSlot, _slotACount, _slotBCount));
                }
            }

            return partitions;
        }

        /// <summary>
        /// 读取 GPT 数据包 (使用伪装)
        /// </summary>
        public async Task<byte[]> ReadGptPacketAsync(int lun, long startSector, int numSectors, string label, string filename, CancellationToken ct)
        {
            double sizeKB = (numSectors * _sectorSize) / 1024.0;
            long startByte = startSector * _sectorSize;

            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>\n" +
                "<read SECTOR_SIZE_IN_BYTES=\"{0}\" file_sector_offset=\"0\" filename=\"{1}\" " +
                "label=\"{2}\" num_partition_sectors=\"{3}\" partofsingleimage=\"true\" " +
                "physical_partition_number=\"{4}\" readbackverify=\"false\" size_in_KB=\"{5:F1}\" " +
                "sparse=\"false\" start_byte_hex=\"0x{6:X}\" start_sector=\"{7}\" />\n</data>\n",
                _sectorSize, filename, label, numSectors, lun, sizeKB, startByte, startSector);

            _log(string.Format("[GPT] 读取 LUN{0} (伪装: {1}/{2})...", lun, label, filename));
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            var buffer = new byte[numSectors * _sectorSize];
            if (await ReceiveDataAfterAckAsync(buffer, ct))
            {
                await WaitForAckAsync(ct);
                _log(string.Format("[GPT] LUN{0} 读取成功 ({1} 字节)", lun, buffer.Length));
                return buffer;
            }

            _log(string.Format("[GPT] LUN{0} 读取失败", lun));
            return null;
        }

        /// <summary>
        /// 最后一次解析的 GPT 结果 (包含槽位信息)
        /// </summary>
        public GptParseResult LastGptResult { get; private set; }

        /// <summary>
        /// 合并后的槽位状态 (来自所有 LUN)
        /// </summary>
        private string _mergedSlot = "nonexistent";
        private int _slotACount = 0;
        private int _slotBCount = 0;

        /// <summary>
        /// 当前槽位 ("a", "b", "undefined", "nonexistent") - 合并所有 LUN 的结果
        /// </summary>
        public string CurrentSlot
        {
            get { return _mergedSlot; }
        }

        /// <summary>
        /// 重置槽位检测状态 (在开始新的 GPT 读取前调用)
        /// </summary>
        public void ResetSlotDetection()
        {
            _mergedSlot = "nonexistent";
            _slotACount = 0;
            _slotBCount = 0;
        }

        /// <summary>
        /// 合并 LUN 的槽位检测结果
        /// </summary>
        private void MergeSlotInfo(GptParseResult result)
        {
            if (result?.SlotInfo == null) return;
            
            var slotInfo = result.SlotInfo;
            
            // 如果这个 LUN 有 A/B 分区
            if (slotInfo.HasAbPartitions)
            {
                // 至少有 A/B 分区存在
                if (_mergedSlot == "nonexistent")
                    _mergedSlot = "undefined";
                
                // 统计激活的槽位
                if (slotInfo.CurrentSlot == "a")
                    _slotACount++;
                else if (slotInfo.CurrentSlot == "b")
                    _slotBCount++;
            }
            
            // 根据统计结果确定最终槽位
            if (_slotACount > _slotBCount && _slotACount > 0)
                _mergedSlot = "a";
            else if (_slotBCount > _slotACount && _slotBCount > 0)
                _mergedSlot = "b";
            else if (_slotACount > 0 && _slotBCount > 0)
                _mergedSlot = "unknown";  // 冲突
            // 否则保持 "undefined" 或 "nonexistent"
        }

        /// <summary>
        /// 解析 GPT 分区 (使用增强版 GptParser)
        /// </summary>
        public List<PartitionInfo> ParseGptPartitions(byte[] gptData, int lun)
        {
            var parser = new GptParser(_log);
            var result = parser.Parse(gptData, lun, _sectorSize);
            
            // 保存解析结果
            LastGptResult = result;
            
            // 合并槽位检测结果
            MergeSlotInfo(result);

            if (result.Success && result.Header != null)
            {
                // 存储 LUN 的 Header 信息 (用于负扇区转换)
                _lunHeaders[lun] = result.Header;

                // 自动更新扇区大小
                if (result.Header.SectorSize > 0 && result.Header.SectorSize != _sectorSize)
                {
                    _log(string.Format("[GPT] 更新扇区大小: {0} -> {1}", _sectorSize, result.Header.SectorSize));
                    _sectorSize = result.Header.SectorSize;
                }

                // 输出详细信息
                _log(string.Format("[GPT] 磁盘 GUID: {0}", result.Header.DiskGuid));
                _log(string.Format("[GPT] 分区数据区: LBA {0} - {1}", 
                    result.Header.FirstUsableLba, result.Header.LastUsableLba));
                _log(string.Format("[GPT] CRC: {0}", result.Header.CrcValid ? "有效" : "无效"));
                
                if (result.SlotInfo.HasAbPartitions)
                {
                    _log(string.Format("[GPT] 当前槽位: {0}", result.SlotInfo.CurrentSlot));
                }
            }
            else if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _log(string.Format("[GPT] 解析失败: {0}", result.ErrorMessage));
            }

            return result.Partitions;
        }

        /// <summary>
        /// 生成 rawprogram.xml
        /// </summary>
        public string GenerateRawprogramXml()
        {
            if (_cachedPartitions == null || _cachedPartitions.Count == 0)
                return null;

            var parser = new GptParser(_log);
            return parser.GenerateRawprogramXml(_cachedPartitions, _sectorSize);
        }

        /// <summary>
        /// 生成 partition.xml
        /// </summary>
        public string GeneratePartitionXml()
        {
            if (_cachedPartitions == null || _cachedPartitions.Count == 0)
                return null;

            var parser = new GptParser(_log);
            return parser.GeneratePartitionXml(_cachedPartitions, _sectorSize);
        }

        #endregion

        #region 读取分区

        /// <summary>
        /// 读取分区到文件
        /// </summary>
        public async Task<bool> ReadPartitionAsync(PartitionInfo partition, string savePath, CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] 读取分区: {0}", partition.Name));

            var totalSectors = partition.NumSectors;
            var sectorsPerChunk = _maxPayloadSize / _sectorSize;
            var totalRead = 0L;

            StartTransferTimer(partition.Size);

            using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, FILE_BUFFER_SIZE))
            {
                for (long sector = 0; sector < totalSectors; sector += sectorsPerChunk)
                {
                    if (ct.IsCancellationRequested) return false;

                    var sectorsToRead = Math.Min(sectorsPerChunk, totalSectors - sector);
                    var startSector = partition.StartSector + sector;

                    var data = await ReadSectorsAsync(partition.Lun, startSector, (int)sectorsToRead, ct);
                    if (data == null)
                    {
                        _log(string.Format("[Firehose] 读取失败 @ sector {0}", startSector));
                        return false;
                    }

                    fs.Write(data, 0, data.Length);
                    totalRead += data.Length;

                    if (_progress != null)
                        _progress(totalRead, partition.Size);
                }
            }

            StopTransferTimer("读取", totalRead);
            _log(string.Format("[Firehose] 分区 {0} 读取完成: {1:N0} 字节", partition.Name, totalRead));
            return true;
        }

        /// <summary>
        /// 读取扇区数据
        /// </summary>
        public async Task<byte[]> ReadSectorsAsync(int lun, long startSector, int numSectors, CancellationToken ct, bool useVipMode = false, string partitionName = null)
        {
            if (useVipMode)
            {
                bool isGptRead = startSector <= 33;
                var strategies = GetDynamicSpoofStrategies(lun, startSector, partitionName, isGptRead);

                foreach (var strategy in strategies)
                {
                    try
                    {
                        if (ct.IsCancellationRequested) return null;
                        PurgeBuffer();

                        string xml;
                        double sizeKB = (numSectors * _sectorSize) / 1024.0;

                        if (string.IsNullOrEmpty(strategy.Label))
                        {
                            xml = string.Format(
                                "<?xml version=\"1.0\" ?><data>\n" +
                                "<read SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                                "physical_partition_number=\"{2}\" size_in_KB=\"{3:F1}\" start_sector=\"{4}\" />\n</data>\n",
                                _sectorSize, numSectors, lun, sizeKB, startSector);
                        }
                        else
                        {
                            xml = string.Format(
                                "<?xml version=\"1.0\" ?><data>\n" +
                                "<read SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"{2}\" " +
                                "num_partition_sectors=\"{3}\" physical_partition_number=\"{4}\" " +
                                "size_in_KB=\"{5:F1}\" sparse=\"false\" start_sector=\"{6}\" />\n</data>\n",
                                _sectorSize, strategy.Filename, strategy.Label, numSectors, lun, sizeKB, startSector);
                        }

                        _port.Write(Encoding.UTF8.GetBytes(xml));

                        int expectedSize = numSectors * _sectorSize;
                        var buffer = new byte[expectedSize];

                        if (await ReceiveDataAfterAckAsync(buffer, ct))
                        {
                            await WaitForAckAsync(ct);
                            return buffer;
                        }
                    }
                    catch { }
                }

                return null;
            }
            else
            {
                try
                {
                    PurgeBuffer();

                    double sizeKB = (numSectors * _sectorSize) / 1024.0;

                    string xml = string.Format(
                        "<?xml version=\"1.0\" ?><data>\n" +
                        "<read SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                        "physical_partition_number=\"{2}\" size_in_KB=\"{3:F1}\" start_sector=\"{4}\" />\n</data>\n",
                        _sectorSize, numSectors, lun, sizeKB, startSector);

                    _port.Write(Encoding.UTF8.GetBytes(xml));

                    int expectedSize = numSectors * _sectorSize;
                    var buffer = new byte[expectedSize];

                    if (await ReceiveDataAfterAckAsync(buffer, ct))
                    {
                        await WaitForAckAsync(ct);
                        return buffer;
                    }
                }
                catch (Exception ex)
                {
                    _log(string.Format("[Read] 异常: {0}", ex.Message));
                }

                return null;
            }
        }

        #endregion

        #region 写入分区

        /// <summary>
        /// 写入分区数据
        /// </summary>
        public async Task<bool> WritePartitionAsync(PartitionInfo partition, string imagePath, bool useOppoMode = false, CancellationToken ct = default(CancellationToken))
        {
            return await WritePartitionAsync(partition.Lun, partition.StartSector, _sectorSize, imagePath, partition.Name, useOppoMode, ct);
        }

        /// <summary>
        /// 写入分区数据 (指定起始扇区)
        /// </summary>
        public async Task<bool> WritePartitionAsync(int lun, long startSector, int sectorSize, string imagePath, string label = "Partition", bool useOppoMode = false, CancellationToken ct = default(CancellationToken))
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("镜像文件不存在", imagePath);

            // 检查是否为 Sparse 镜像
            bool isSparse = SparseStream.IsSparseFile(imagePath);
            
            if (isSparse)
            {
                // 智能 Sparse 写入：只写入有数据的部分，跳过 DONT_CARE
                return await WriteSparsePartitionSmartAsync(lun, startSector, sectorSize, imagePath, label, useOppoMode, ct);
            }
            
            _log(string.Format("[Firehose] 写入分区: {0} ({1})", label, Path.GetFileName(imagePath)));

            using (Stream sourceStream = File.OpenRead(imagePath))
            {
                var totalBytes = sourceStream.Length;
                var sectorsPerChunk = _maxPayloadSize / sectorSize;
                var bytesPerChunk = sectorsPerChunk * sectorSize;
                var totalWritten = 0L;

                StartTransferTimer(totalBytes);

                var buffer = new byte[bytesPerChunk];
                var currentSector = startSector;

                while (totalWritten < totalBytes)
                {
                    if (ct.IsCancellationRequested) return false;

                    var bytesToRead = (int)Math.Min(bytesPerChunk, totalBytes - totalWritten);
                    var bytesRead = sourceStream.Read(buffer, 0, bytesToRead);
                    if (bytesRead == 0) break;

                    // 补齐到扇区边界
                    var paddedSize = ((bytesRead + sectorSize - 1) / sectorSize) * sectorSize;
                    if (paddedSize > bytesRead)
                        Array.Clear(buffer, bytesRead, paddedSize - bytesRead);

                    var sectorsToWrite = paddedSize / sectorSize;

                    if (!await WriteSectorsAsync(lun, currentSector, buffer, paddedSize, label, useOppoMode, ct))
                    {
                        _log(string.Format("[Firehose] 写入失败 @ sector {0}", currentSector));
                        return false;
                    }

                    totalWritten += bytesRead;
                    currentSector += sectorsToWrite;

                    if (_progress != null)
                        _progress(totalWritten, totalBytes);
                }

                StopTransferTimer("写入", totalWritten);
                _log(string.Format("[Firehose] 分区 {0} 写入完成: {1:N0} 字节", label, totalWritten));
                return true;
            }
        }

        /// <summary>
        /// 智能写入 Sparse 镜像（只写有数据的 chunks，跳过 DONT_CARE）
        /// </summary>
        private async Task<bool> WriteSparsePartitionSmartAsync(int lun, long startSector, int sectorSize, string imagePath, string label, bool useOppoMode, CancellationToken ct)
        {
            using (var sparse = SparseStream.Open(imagePath, _log))
            {
                var totalExpandedSize = sparse.Length;
                var realDataSize = sparse.GetRealDataSize();
                var dataRanges = sparse.GetDataRanges();
                
                _log(string.Format("[Firehose] 写入分区: {0} ({1}) [Sparse 智能模式]", label, Path.GetFileName(imagePath)));
                _log(string.Format("[Sparse] 展开大小: {0:N0} MB, 实际数据: {1:N0} MB, 节省: {2:P1}", 
                    totalExpandedSize / 1024.0 / 1024.0, 
                    realDataSize / 1024.0 / 1024.0,
                    1.0 - (double)realDataSize / totalExpandedSize));
                
                if (dataRanges.Count == 0)
                {
                    // 空 Sparse 镜像: 使用 erase 命令清空分区
                    _log(string.Format("[Sparse] 镜像无实际数据，擦除分区 {0}...", label));
                    long numSectors = totalExpandedSize / sectorSize;
                    bool eraseOk = await EraseSectorsAsync(lun, startSector, numSectors, ct);
                    if (eraseOk)
                        _log(string.Format("[Sparse] 分区 {0} 擦除完成 ({1:F2} MB)", label, totalExpandedSize / 1024.0 / 1024.0));
                    else
                        _log(string.Format("[Sparse] 分区 {0} 擦除失败", label));
                    return eraseOk;
                }
                
                var sectorsPerChunk = _maxPayloadSize / sectorSize;
                var bytesPerChunk = sectorsPerChunk * sectorSize;
                var buffer = new byte[bytesPerChunk];
                var totalWritten = 0L;
                
                StartTransferTimer(realDataSize);
                
                // 逐个写入有数据的范围
                foreach (var range in dataRanges)
                {
                    if (ct.IsCancellationRequested) return false;
                    
                    var rangeOffset = range.Item1;
                    var rangeSize = range.Item2;
                    var rangeStartSector = startSector + (rangeOffset / sectorSize);
                    
                    // 定位到该范围
                    sparse.Seek(rangeOffset, SeekOrigin.Begin);
                    var rangeWritten = 0L;
                    
                    while (rangeWritten < rangeSize)
                    {
                        if (ct.IsCancellationRequested) return false;
                        
                        var bytesToRead = (int)Math.Min(bytesPerChunk, rangeSize - rangeWritten);
                        var bytesRead = sparse.Read(buffer, 0, bytesToRead);
                        if (bytesRead == 0) break;
                        
                        // 补齐到扇区边界
                        var paddedSize = ((bytesRead + sectorSize - 1) / sectorSize) * sectorSize;
                        if (paddedSize > bytesRead)
                            Array.Clear(buffer, bytesRead, paddedSize - bytesRead);
                        
                        var sectorsToWrite = paddedSize / sectorSize;
                        var currentSector = rangeStartSector + (rangeWritten / sectorSize);
                        
                        if (!await WriteSectorsAsync(lun, currentSector, buffer, paddedSize, label, useOppoMode, ct))
                        {
                            _log(string.Format("[Firehose] 写入失败 @ sector {0}", currentSector));
                            return false;
                        }
                        
                        rangeWritten += bytesRead;
                        totalWritten += bytesRead;
                        
                        if (_progress != null)
                            _progress(totalWritten, realDataSize);
                    }
                }
                
                StopTransferTimer("写入", totalWritten);
                _log(string.Format("[Firehose] 分区 {0} 写入完成: {1:N0} 字节 (跳过 {2:N0} MB 空白)", 
                    label, totalWritten, (totalExpandedSize - realDataSize) / 1024.0 / 1024.0));
                return true;
            }
        }

        /// <summary>
        /// 写入扇区数据
        /// </summary>
        private async Task<bool> WriteSectorsAsync(int lun, long startSector, byte[] data, int length, string label, bool useOppoMode, CancellationToken ct)
        {
            int numSectors = length / _sectorSize;
            
            // 使用实际的分区名称，而不是硬编码的 GPT 值
            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>" +
                "<program SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                "physical_partition_number=\"{2}\" start_sector=\"{3}\" label=\"{4}\" />" +
                "</data>",
                _sectorSize, numSectors, lun, startSector, label);

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (!await WaitForRawDataModeAsync(ct))
            {
                _log("[Firehose] Program 命令未确认");
                return false;
            }

            _port.Write(data, 0, length);

            return await WaitForAckAsync(ct, 10);
        }

        /// <summary>
        /// 从文件刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionFromFileAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct, bool useVipMode = false)
        {
            if (!File.Exists(filePath))
            {
                _log("Firehose: 文件不存在 - " + filePath);
                return false;
            }

            // 检查是否为 Sparse 镜像
            bool isSparse = SparseStream.IsSparseFile(filePath);
            
            // Sparse 镜像使用智能写入，跳过 DONT_CARE
            if (isSparse)
            {
                return await FlashSparsePartitionSmartAsync(partitionName, filePath, lun, startSector, progress, ct, useVipMode);
            }
            
            // Raw 镜像的常规写入
            using (Stream sourceStream = File.OpenRead(filePath))
            {
                long fileSize = sourceStream.Length;
                int numSectors = (int)Math.Ceiling((double)fileSize / _sectorSize);

                _log(string.Format("Firehose: 刷写 {0} -> {1} ({2:F2} MB){3}", 
                    Path.GetFileName(filePath), partitionName, fileSize / 1024.0 / 1024.0,
                    useVipMode ? " [VIP模式]" : ""));

                // VIP 模式使用伪装策略
                if (useVipMode)
                {
                    return await FlashPartitionVipModeAsync(partitionName, sourceStream, lun, startSector, numSectors, fileSize, progress, ct);
                }

                // 标准模式 (支持 OnePlus Token 认证)
                string xml;
                if (IsOnePlusAuthenticated)
                {
                    // OnePlus 设备需要附带认证 Token - 添加 label 和 read_back_verify 符合官方协议
                    xml = string.Format(
                        "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                        "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                        "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                        "read_back_verify=\"true\" token=\"{5}\" pk=\"{6}\"/></data>",
                        _sectorSize, numSectors, lun, startSector, partitionName,
                        OnePlusProgramToken, OnePlusProgramPk);
                    _log("[OnePlus] 使用认证令牌写入");
                }
                else
                {
                    // 标准模式 - 添加 label 属性符合官方协议
                    xml = string.Format(
                        "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                        "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                        "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                        "read_back_verify=\"true\"/></data>",
                        _sectorSize, numSectors, lun, startSector, partitionName);
                }

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (!await WaitForRawDataModeAsync(ct))
                {
                    _log("Firehose: Program 命令被拒绝");
                    return false;
                }

                return await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
            }
        }

        /// <summary>
        /// 使用官方 NUM_DISK_SECTORS-N 负扇区格式刷写分区
        /// 用于 BackupGPT 等需要写入磁盘末尾的分区
        /// </summary>
        public async Task<bool> FlashPartitionWithNegativeSectorAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                _log("Firehose: 文件不存在 - " + filePath);
                return false;
            }

            // 负扇区不支持 Sparse 镜像
            if (SparseStream.IsSparseFile(filePath))
            {
                _log("Firehose: 负扇区格式不支持 Sparse 镜像");
                return false;
            }
            
            using (Stream sourceStream = File.OpenRead(filePath))
            {
                long fileSize = sourceStream.Length;
                int numSectors = (int)Math.Ceiling((double)fileSize / _sectorSize);

                // 格式化负扇区: NUM_DISK_SECTORS-N. (官方格式，注意尾部的点)
                string startSectorStr;
                if (startSector < 0)
                {
                    startSectorStr = string.Format("NUM_DISK_SECTORS{0}.", startSector);
                }
                else
                {
                    startSectorStr = startSector.ToString();
                }

                _log(string.Format("Firehose: 刷写 {0} -> {1} ({2:F2} MB) @ {3}", 
                    Path.GetFileName(filePath), partitionName, fileSize / 1024.0 / 1024.0, startSectorStr));

                // 构造 program XML，使用官方负扇区格式
                string xml = string.Format(
                    "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                    "read_back_verify=\"true\"/></data>",
                    _sectorSize, numSectors, lun, startSectorStr, partitionName);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (!await WaitForRawDataModeAsync(ct))
                {
                    _log("Firehose: Program 命令被拒绝 (负扇区格式)");
                    return false;
                }

                return await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
            }
        }

        /// <summary>
        /// 智能刷写 Sparse 镜像（只写有数据的 chunks）
        /// </summary>
        private async Task<bool> FlashSparsePartitionSmartAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct, bool useVipMode)
        {
            using (var sparse = SparseStream.Open(filePath, _log))
            {
                var totalExpandedSize = sparse.Length;
                var realDataSize = sparse.GetRealDataSize();
                var dataRanges = sparse.GetDataRanges();
                
                _log(string.Format("Firehose: 刷写 {0} -> {1} [Sparse 智能模式]{2}", 
                    Path.GetFileName(filePath), partitionName, useVipMode ? " [VIP模式]" : ""));
                _log(string.Format("[Sparse] 展开: {0:F2} MB, 实际数据: {1:F2} MB, 节省: {2:P1}", 
                    totalExpandedSize / 1024.0 / 1024.0, 
                    realDataSize / 1024.0 / 1024.0,
                    realDataSize > 0 ? (1.0 - (double)realDataSize / totalExpandedSize) : 1.0));
                
                if (dataRanges.Count == 0)
                {
                    // 空 Sparse 镜像 (如 userdata): 使用 erase 命令清空分区
                    _log(string.Format("[Sparse] 镜像无实际数据，擦除分区 {0}...", partitionName));
                    long numSectors = totalExpandedSize / _sectorSize;
                    bool eraseOk = await EraseSectorsAsync(lun, startSector, numSectors, ct);
                    if (progress != null) progress.Report(100.0);
                    if (eraseOk)
                        _log(string.Format("[Sparse] 分区 {0} 擦除完成 ({1:F2} MB)", partitionName, totalExpandedSize / 1024.0 / 1024.0));
                    else
                        _log(string.Format("[Sparse] 分区 {0} 擦除失败", partitionName));
                    return eraseOk;
                }
                
                var totalWritten = 0L;
                var rangeIndex = 0;
                
                // 逐个写入有数据的范围
                foreach (var range in dataRanges)
                {
                    if (ct.IsCancellationRequested) return false;
                    rangeIndex++;
                    
                    var rangeOffset = range.Item1;
                    var rangeSize = range.Item2;
                    var rangeStartSector = startSector + (rangeOffset / _sectorSize);
                    var numSectors = (int)Math.Ceiling((double)rangeSize / _sectorSize);
                    
                    // 定位到该范围
                    sparse.Seek(rangeOffset, SeekOrigin.Begin);
                    
                    // 构建 program 命令
                    string xml;
                    if (useVipMode)
                    {
                        // VIP 模式伪装
                        xml = string.Format(
                            "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                            "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                            "start_sector=\"{3}\" filename=\"gpt_main{2}.bin\" label=\"PrimaryGPT\" " +
                            "read_back_verify=\"true\"/></data>",
                            _sectorSize, numSectors, lun, rangeStartSector);
                    }
                    else if (IsOnePlusAuthenticated)
                    {
                        xml = string.Format(
                            "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                            "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                            "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                            "read_back_verify=\"true\" token=\"{5}\" pk=\"{6}\"/></data>",
                            _sectorSize, numSectors, lun, rangeStartSector, partitionName,
                            OnePlusProgramToken, OnePlusProgramPk);
                    }
                    else
                    {
                        xml = string.Format(
                            "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                            "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                            "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                            "read_back_verify=\"true\"/></data>",
                            _sectorSize, numSectors, lun, rangeStartSector, partitionName);
                    }
                    
                    _port.Write(Encoding.UTF8.GetBytes(xml));
                    
                    if (!await WaitForRawDataModeAsync(ct))
                    {
                        _log(string.Format("[Sparse] 第 {0}/{1} 段 Program 命令被拒绝", rangeIndex, dataRanges.Count));
                        return false;
                    }
                    
                    // 发送该范围的数据
                    var sent = 0L;
                    var chunkSize = Math.Min(_maxPayloadSize, 1 * 1024 * 1024); // 最大 1MB per chunk
                    var buffer = new byte[chunkSize];
                    
                    while (sent < rangeSize)
                    {
                        if (ct.IsCancellationRequested) return false;
                        
                        var toRead = (int)Math.Min(chunkSize, rangeSize - sent);
                        var read = sparse.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        
                        // 补齐到扇区边界
                        var paddedSize = ((read + _sectorSize - 1) / _sectorSize) * _sectorSize;
                        if (paddedSize > read)
                            Array.Clear(buffer, read, paddedSize - read);
                        
                        await _port.WriteAsync(buffer, 0, paddedSize, ct);
                        
                        sent += read;
                        totalWritten += read;
                        
                        if (progress != null && realDataSize > 0)
                            progress.Report(totalWritten * 100.0 / realDataSize);
                    }
                    
                    if (!await WaitForAckAsync(ct, 30))
                    {
                        _log(string.Format("[Sparse] 第 {0}/{1} 段写入未确认", rangeIndex, dataRanges.Count));
                        return false;
                    }
                }
                
                _log(string.Format("[Sparse] {0} 写入完成: {1:N0} 字节 (跳过 {2:N0} MB 空白)", 
                    partitionName, totalWritten, (totalExpandedSize - realDataSize) / 1024.0 / 1024.0));
                return true;
            }
        }

        /// <summary>
        /// VIP 模式刷写分区 (使用伪装策略)
        /// </summary>
        private async Task<bool> FlashPartitionVipModeAsync(string partitionName, Stream sourceStream, int lun, long startSector, int numSectors, long fileSize, IProgress<double> progress, CancellationToken ct)
        {
            // 获取伪装策略
            var strategies = GetDynamicSpoofStrategies(lun, startSector, partitionName, false);
            
            foreach (var strategy in strategies)
            {
                if (ct.IsCancellationRequested) break;

                string spoofLabel = string.IsNullOrEmpty(strategy.Label) ? partitionName : strategy.Label;
                string spoofFilename = string.IsNullOrEmpty(strategy.Filename) ? partitionName : strategy.Filename;

                _log(string.Format("[VIP Write] 尝试伪装: {0}/{1}", spoofLabel, spoofFilename));
                PurgeBuffer();

                // VIP 模式 program 命令 - 添加 read_back_verify 符合官方协议
                string xml = string.Format(
                    "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" filename=\"{4}\" label=\"{5}\" " +
                    "partofsingleimage=\"true\" read_back_verify=\"true\" sparse=\"false\"/></data>",
                    _sectorSize, numSectors, lun, startSector, spoofFilename, spoofLabel);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (await WaitForRawDataModeAsync(ct))
                {
                    _log(string.Format("[VIP Write] 伪装 {0} 成功，开始传输数据...", spoofLabel));
                    
                    // 每次尝试前重置流位置
                    sourceStream.Position = 0;
                    bool success = await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
                    if (success)
                    {
                        _log(string.Format("[VIP Write] {0} 写入成功", partitionName));
                        return true;
                    }
                }

                await Task.Delay(100, ct);
            }

            _log(string.Format("[VIP Write] {0} 所有伪装策略都失败", partitionName));
            return false;
        }

        /// <summary>
        /// 发送流数据 (通用的极速发送逻辑)
        /// </summary>
        private async Task<bool> SendStreamDataAsync(Stream stream, long streamSize, IProgress<double> progress, CancellationToken ct)
        {
            long sent = 0;
            byte[] buffer = new byte[_maxPayloadSize];
            double lastPercent = -1;
            DateTime lastProgressTime = DateTime.MinValue;

            while (sent < streamSize)
            {
                if (ct.IsCancellationRequested) return false;

                int toRead = (int)Math.Min(_maxPayloadSize, streamSize - sent);
                int read = await stream.ReadAsync(buffer, 0, toRead);
                if (read <= 0) break;

                // 补齐扇区
                int toWrite = read;
                if (read % _sectorSize != 0)
                {
                    toWrite = ((read / _sectorSize) + 1) * _sectorSize;
                    Array.Clear(buffer, read, toWrite - read);
                }

                if (!await _port.WriteAsync(buffer, 0, toWrite, ct))
                {
                    _log("Firehose: 数据写入失败");
                    return false;
                }

                    sent += read;

                // 节流进度报告：每 100ms 或每 0.1% 更新一次
                var now = DateTime.Now;
                double currentPercent = (100.0 * sent / streamSize);
                if (currentPercent > lastPercent + 0.1 || (now - lastProgressTime).TotalMilliseconds > 100)
                {
                    if (_progress != null) _progress(sent, streamSize);
                    if (progress != null) progress.Report(currentPercent);
                    
                    lastPercent = currentPercent;
                    lastProgressTime = now;
                }
            }

            // 确保最后一次进度报告
            if (_progress != null) _progress(streamSize, streamSize);
            if (progress != null) progress.Report(100.0);

            // 等待最终 ACK
            return await WaitForAckAsync(ct, 180);
        }

        #endregion

        #region 擦除分区

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(PartitionInfo partition, CancellationToken ct = default(CancellationToken), bool useVipMode = false)
        {
            _log(string.Format("[Firehose] 擦除分区: {0}{1}", partition.Name, useVipMode ? " [VIP模式]" : ""));

            if (useVipMode)
            {
                return await ErasePartitionVipModeAsync(partition, ct);
            }

            var xml = string.Format(
                "<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\" /></data>",
                _sectorSize, partition.NumSectors, partition.Lun, partition.StartSector);

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct))
            {
                _log(string.Format("[Firehose] 分区 {0} 擦除完成", partition.Name));
                return true;
            }

            _log("[Firehose] 擦除失败");
            return false;
        }

        /// <summary>
        /// VIP 模式擦除分区
        /// </summary>
        private async Task<bool> ErasePartitionVipModeAsync(PartitionInfo partition, CancellationToken ct)
        {
            var strategies = GetDynamicSpoofStrategies(partition.Lun, partition.StartSector, partition.Name, false);

            foreach (var strategy in strategies)
            {
                if (ct.IsCancellationRequested) break;

                string spoofLabel = string.IsNullOrEmpty(strategy.Label) ? partition.Name : strategy.Label;
                string spoofFilename = string.IsNullOrEmpty(strategy.Filename) ? partition.Name : strategy.Filename;

                _log(string.Format("[VIP Erase] 尝试伪装: {0}/{1}", spoofLabel, spoofFilename));
                PurgeBuffer();

                var xml = string.Format(
                    "<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" label=\"{4}\" filename=\"{5}\" /></data>",
                    _sectorSize, partition.NumSectors, partition.Lun, partition.StartSector, spoofLabel, spoofFilename);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (await WaitForAckAsync(ct))
                {
                    _log(string.Format("[VIP Erase] {0} 擦除成功", partition.Name));
                    return true;
                }

                await Task.Delay(100, ct);
            }

            _log(string.Format("[VIP Erase] {0} 所有伪装策略都失败", partition.Name));
            return false;
        }

        /// <summary>
        /// 擦除分区 (参数版本)
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, int lun, long startSector, long numSectors, CancellationToken ct, bool useVipMode = false)
        {
            _log(string.Format("Firehose: 擦除分区 {0}{1}", partitionName, useVipMode ? " [VIP模式]" : ""));

            if (useVipMode)
            {
                var partition = new PartitionInfo
                {
                    Name = partitionName,
                    Lun = lun,
                    StartSector = startSector,
                    NumSectors = numSectors,
                    SectorSize = _sectorSize
                };
                return await ErasePartitionVipModeAsync(partition, ct);
            }

            string xml = string.Format(
                "<?xml version=\"1.0\"?><data><erase SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\"/></data>",
                _sectorSize, numSectors, lun, startSector);

            _port.Write(Encoding.UTF8.GetBytes(xml));
            bool success = await WaitForAckAsync(ct, 100);
            _log(success ? "Firehose: 擦除成功" : "Firehose: 擦除失败");

            return success;
        }

        /// <summary>
        /// 擦除指定扇区范围 (简化版)
        /// </summary>
        public async Task<bool> EraseSectorsAsync(int lun, long startSector, long numSectors, CancellationToken ct)
        {
            string xml = string.Format(
                "<?xml version=\"1.0\"?><data><erase SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\"/></data>",
                _sectorSize, numSectors, lun, startSector);

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct, 120);
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> ResetAsync(string mode = "reset", CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] 重启设备 (模式: {0})", mode));

            var xml = string.Format("<?xml version=\"1.0\" ?><data><power value=\"{0}\" /></data>", mode);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 关机
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[Firehose] 关机...");

            string xml = "<?xml version=\"1.0\"?><data><power value=\"off\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 进入 EDL 模式
        /// </summary>
        public async Task<bool> RebootToEdlAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\"?><data><power value=\"edl\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 设置活动槽位 (A/B)
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] 设置活动 Slot: {0}", slot));

            var xml = string.Format("<?xml version=\"1.0\" ?><data><setactiveslot slot=\"{0}\" /></data>", slot);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 修复 GPT
        /// </summary>
        public async Task<bool> FixGptAsync(int lun = -1, bool growLastPartition = true, CancellationToken ct = default(CancellationToken))
        {
            string lunValue = (lun == -1) ? "all" : lun.ToString();
            string growValue = growLastPartition ? "1" : "0";

            _log(string.Format("[Firehose] 修复 GPT (LUN={0})...", lunValue));
            var xml = string.Format("<?xml version=\"1.0\" ?><data><fixgpt lun=\"{0}\" grow_last_partition=\"{1}\" /></data>", lunValue, growValue);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct, 10))
            {
                _log("[Firehose] GPT 修复成功");
                return true;
            }

            _log("[Firehose] GPT 修复失败");
            return false;
        }

        /// <summary>
        /// 设置启动 LUN
        /// </summary>
        public async Task<bool> SetBootLunAsync(int lun, CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] 设置启动 LUN: {0}", lun));
            var xml = string.Format("<?xml version=\"1.0\" ?><data><setbootablestoragedrive value=\"{0}\" /></data>", lun);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 应用单个补丁 (支持官方 NUM_DISK_SECTORS-N 负扇区格式)
        /// </summary>
        public async Task<bool> ApplyPatchAsync(int lun, long startSector, int byteOffset, int sizeInBytes, string value, CancellationToken ct = default(CancellationToken))
        {
            // 跳过空补丁
            if (string.IsNullOrEmpty(value) || sizeInBytes == 0)
                return true;

            // 格式化 start_sector: 负数使用官方格式 NUM_DISK_SECTORS-N.
            string startSectorStr;
            if (startSector < 0)
            {
                startSectorStr = string.Format("NUM_DISK_SECTORS{0}.", startSector);
                _log(string.Format("[Firehose] 应用补丁: LUN{0} Sector {1} Offset{2} Size{3}", lun, startSectorStr, byteOffset, sizeInBytes));
            }
            else
            {
                startSectorStr = startSector.ToString();
                _log(string.Format("[Firehose] 应用补丁: LUN{0} Sector{1} Offset{2} Size{3}", lun, startSector, byteOffset, sizeInBytes));
            }

            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>\n" +
                "<patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" filename=\"DISK\" " +
                "physical_partition_number=\"{2}\" size_in_bytes=\"{3}\" start_sector=\"{4}\" value=\"{5}\" />\n</data>\n",
                _sectorSize, byteOffset, lun, sizeInBytes, startSectorStr, value);

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 从 Patch XML 文件应用所有补丁
        /// </summary>
        public async Task<int> ApplyPatchXmlAsync(string patchXmlPath, CancellationToken ct = default(CancellationToken))
        {
            if (!System.IO.File.Exists(patchXmlPath))
            {
                _log(string.Format("[Firehose] Patch 文件不存在: {0}", patchXmlPath));
                return 0;
            }

            _log(string.Format("[Firehose] 应用 Patch 文件: {0}", System.IO.Path.GetFileName(patchXmlPath)));

            int successCount = 0;
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(patchXmlPath);
                var root = doc.Root;
                if (root == null) return 0;

                foreach (var elem in root.Elements("patch"))
                {
                    if (ct.IsCancellationRequested) break;

                    string value = elem.Attribute("value")?.Value ?? "";
                    if (string.IsNullOrEmpty(value)) continue;

                    int lun = 0;
                    int.TryParse(elem.Attribute("physical_partition_number")?.Value ?? "0", out lun);
                    
                    long startSector = 0;
                    var startSectorAttr = elem.Attribute("start_sector")?.Value ?? "0";
                    
                    // 处理 NUM_DISK_SECTORS-N 形式的负扇区 (保持负数，让 ApplyPatchAsync 使用官方格式发送)
                    if (startSectorAttr.Contains("NUM_DISK_SECTORS"))
                    {
                        if (startSectorAttr.Contains("-"))
                        {
                            string offsetStr = startSectorAttr.Split('-')[1].TrimEnd('.');
                            long offset;
                            if (long.TryParse(offsetStr, out offset))
                                startSector = -offset; // 负数，ApplyPatchAsync 会使用官方格式
                        }
                        else
                        {
                            startSector = -1;
                        }
                        // 不再尝试客户端转换，直接使用负数让设备计算
                    }
                    else if (startSectorAttr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        long.TryParse(startSectorAttr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out startSector);
                    }
                    else
                    {
                        // 移除可能的尾随点号 (如 "5.")
                        if (startSectorAttr.EndsWith("."))
                            startSectorAttr = startSectorAttr.Substring(0, startSectorAttr.Length - 1);
                        long.TryParse(startSectorAttr, out startSector);
                    }

                    int byteOffset = 0;
                    int.TryParse(elem.Attribute("byte_offset")?.Value ?? "0", out byteOffset);

                    int sizeInBytes = 0;
                    int.TryParse(elem.Attribute("size_in_bytes")?.Value ?? "0", out sizeInBytes);

                    if (sizeInBytes == 0) continue;

                    if (await ApplyPatchAsync(lun, startSector, byteOffset, sizeInBytes, value, ct))
                        successCount++;
                    else
                        _log(string.Format("[Firehose] 补丁失败: LUN{0} Sector{1}", lun, startSector));
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Firehose] 应用 Patch 异常: {0}", ex.Message));
            }

            _log(string.Format("[Firehose] 成功应用 {0} 个补丁", successCount));
            return successCount;
        }

        /// <summary>
        /// Ping/NOP 测试连接
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default(CancellationToken))
        {
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" ?><data><nop /></data>"));
            return await WaitForAckAsync(ct, 3);
        }

        #endregion

        #region 分区缓存

        public void SetPartitionCache(List<PartitionInfo> partitions)
        {
            _cachedPartitions = partitions;
        }

        public PartitionInfo FindPartition(string name)
        {
            if (_cachedPartitions == null) return null;
            foreach (var p in _cachedPartitions)
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        #endregion

        #region 通信方法

        private async Task<XElement> ProcessXmlResponseAsync(CancellationToken ct, int timeoutMs = 5000)
        {
            try
            {
                var sb = new StringBuilder();
                var startTime = DateTime.Now;
                int emptyReads = 0;

                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    if (ct.IsCancellationRequested) return null;

                    int available = _port.BytesToRead;
                    if (available > 0)
                    {
                        emptyReads = 0;
                        byte[] buffer = new byte[Math.Min(available, 65536)];
                        int read = _port.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

                            var content = sb.ToString();

                            // 提取设备日志
                            if (content.Contains("<log "))
                            {
                                var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                                foreach (Match m in logMatches)
                                {
                                    if (m.Groups.Count > 1)
                                        _log("[Device] " + m.Groups[1].Value);
                                }
                            }

                            if (content.Contains("</data>") || content.Contains("<response"))
                            {
                                int start = content.IndexOf("<response");
                                if (start >= 0)
                                {
                                    int end = content.IndexOf("/>", start);
                                    if (end > start)
                                    {
                                        var respXml = content.Substring(start, end - start + 2);
                                        return XElement.Parse(respXml);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        emptyReads++;
                        // 快速轮询前几次，之后逐渐增加等待时间
                        if (emptyReads < 10)
                            await Task.Delay(1, ct);
                        else if (emptyReads < 50)
                            await Task.Delay(2, ct);
                        else
                            await Task.Delay(5, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Firehose] 响应解析异常: {0}", ex.Message));
            }
            return null;
        }

        private async Task<bool> WaitForAckAsync(CancellationToken ct, int maxRetries = 50)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (ct.IsCancellationRequested) return false;

                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    var valAttr = resp.Attribute("value");
                    string val = valAttr != null ? valAttr.Value : "";

                    if (val.Equals("ACK", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (val.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorAttr = resp.Attribute("error");
                        string errorDesc = errorAttr != null ? errorAttr.Value : resp.ToString();
                        string message, suggestion;
                        bool isFatal, canRetry;
                        FirehoseErrorHelper.ParseNakError(errorDesc, out message, out suggestion, out isFatal, out canRetry);
                        _log(string.Format("[Firehose] NAK: {0}", message));
                        if (!string.IsNullOrEmpty(suggestion))
                            _log(string.Format("[Firehose] {0}", suggestion));
                        return false;
                    }
                }
            }

            _log("[Firehose] 等待 ACK 超时");
            return false;
        }

        /// <summary>
        /// 接收数据响应 (极速流水线版)
        /// </summary>
        private async Task<bool> ReceiveDataAfterAckAsync(byte[] buffer, CancellationToken ct)
            {
                try
                {
                    int totalBytes = buffer.Length;
                    int received = 0;
                    bool headerFound = false;

                // 探测缓冲区
                byte[] probeBuf = new byte[16384];
                int probeIdx = 0;

                    while (received < totalBytes)
                    {
                        if (ct.IsCancellationRequested) return false;

                        if (!headerFound)
                        {
                        // 1. 寻找 XML 头部
                        int read = await _port.ReadAsync(probeBuf, probeIdx, probeBuf.Length - probeIdx, ct);
                        if (read <= 0) return false;
                        probeIdx += read;

                        string content = Encoding.UTF8.GetString(probeBuf, 0, probeIdx);
                            int ackIndex = content.IndexOf("rawmode=\"true\"", StringComparison.OrdinalIgnoreCase);
                        if (ackIndex == -1) ackIndex = content.IndexOf("rawmode='true'", StringComparison.OrdinalIgnoreCase);

                            if (ackIndex >= 0)
                            {
                                int xmlEndIndex = content.IndexOf("</data>", ackIndex);
                                if (xmlEndIndex >= 0)
                                {
                                    headerFound = true;
                                int dataStart = xmlEndIndex + 7;
                                // 跳过空白符
                                while (dataStart < probeIdx && (probeBuf[dataStart] == '\n' || probeBuf[dataStart] == '\r'))
                                    dataStart++;

                                // 将探测缓冲区中剩余的数据存入目标 buffer
                                int leftover = probeIdx - dataStart;
                                if (leftover > 0)
                                {
                                    Array.Copy(probeBuf, dataStart, buffer, 0, Math.Min(leftover, totalBytes));
                                    received = leftover;
                                }
                                }
                            }
                            else if (content.Contains("NAK"))
                            {
                                return false;
                        }
                        
                        if (probeIdx >= probeBuf.Length && !headerFound) probeIdx = 0; // 防止溢出
                            }
                            else
                            {
                        // 2. 极速读取原始数据块
                        int toRead = Math.Min(totalBytes - received, 1024 * 1024); // 每次最多读 1MB
                        int read = await _port.ReadAsync(buffer, received, toRead, ct);
                        if (read <= 0) break;
                        received += read;
                    }
                }
                return received >= totalBytes;
                }
                catch (Exception ex)
                {
                _log("[Read] 极速解析异常: " + ex.Message);
                    return false;
                }
        }

        /// <summary>
        /// 等待设备进入 Raw 数据模式
        /// </summary>
        private async Task<bool> WaitForRawDataModeAsync(CancellationToken ct, int timeoutMs = 5000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var buffer = new byte[4096];
                    var sb = new StringBuilder();
                    var startTime = DateTime.Now;

                    while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (ct.IsCancellationRequested) return false;

                        if (_port.BytesToRead > 0)
                        {
                            int read = _port.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                                string response = sb.ToString();

                                if (response.Contains("NAK"))
                                {
                                    _log(string.Format("[Write] 设备拒绝: {0}", response.Substring(0, Math.Min(response.Length, 100))));
                                    return false;
                                }

                                if (response.Contains("rawmode=\"true\"") || response.Contains("rawmode='true'"))
                                {
                                    if (response.Contains("</data>"))
                                        return true;
                                }

                                if (response.Contains("ACK") && response.Contains("</data>"))
                                    return true;
                            }
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _log(string.Format("[Write] 等待异常: {0}", ex.Message));
                    return false;
                }
            }, ct);
        }

        private void PurgeBuffer()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _rxBuffer.Clear();
        }

        #endregion

        #region 速度统计

        private void StartTransferTimer(long totalBytes)
        {
            _transferStopwatch = Stopwatch.StartNew();
            _transferTotalBytes = totalBytes;
        }

        private void StopTransferTimer(string operationName, long bytesTransferred)
        {
            if (_transferStopwatch == null) return;

            _transferStopwatch.Stop();
            double seconds = _transferStopwatch.Elapsed.TotalSeconds;

            if (seconds > 0.1 && bytesTransferred > 0)
            {
                double mbps = (bytesTransferred / 1024.0 / 1024.0) / seconds;
                double mbTotal = bytesTransferred / 1024.0 / 1024.0;

                if (mbTotal >= 1)
                    _log(string.Format("[速度] {0}: {1:F1}MB 用时 {2:F1}s ({3:F2} MB/s)", operationName, mbTotal, seconds, mbps));
            }

            _transferStopwatch = null;
        }

        #endregion

        #region 认证支持方法

        public async Task<string> SendRawXmlAsync(string xmlOrCommand, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                PurgeBuffer();
                string xml = xmlOrCommand;
                if (!xmlOrCommand.TrimStart().StartsWith("<?xml"))
                    xml = string.Format("<?xml version=\"1.0\" ?><data><{0} /></data>", xmlOrCommand);

                _port.Write(Encoding.UTF8.GetBytes(xml));
                return await ReadRawResponseAsync(5000, ct);
            }
            catch { return null; }
        }

        public async Task<string> SendRawBytesAndGetResponseAsync(byte[] data, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                PurgeBuffer();
                _port.Write(data, 0, data.Length);
                await Task.Delay(100, ct);
                return await ReadRawResponseAsync(5000, ct);
            }
            catch { return null; }
        }

        public async Task<string> SendXmlCommandWithAttributeResponseAsync(string xml, string attrName, int maxRetries = 10, CancellationToken ct = default(CancellationToken))
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (ct.IsCancellationRequested) return null;
                try
                {
                    PurgeBuffer();
                    _port.Write(Encoding.UTF8.GetBytes(xml));
                    string response = await ReadRawResponseAsync(3000, ct);
                    if (string.IsNullOrEmpty(response)) continue;

                    string pattern = string.Format("{0}=\"([^\"]*)\"", attrName);
                    var match = Regex.Match(response, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1)
                        return match.Groups[1].Value;
                }
                catch { }
                await Task.Delay(100, ct);
            }
            return null;
        }

        private async Task<string> ReadRawResponseAsync(int timeoutMs, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (ct.IsCancellationRequested) break;
                if (_port.BytesToRead > 0)
                {
                    byte[] buffer = new byte[_port.BytesToRead];
                    int read = _port.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        string content = sb.ToString();
                        if (content.Contains("</data>") || content.Contains("/>"))
                            return content;
                    }
                }
                await Task.Delay(20, ct);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        #endregion

        #region OPLUS (OPPO/Realme/OnePlus) VIP 认证

        /// <summary>
        /// 执行 VIP 认证流程 (基于 Digest 和 Signature 文件)
        /// 完整 6 步流程 (参考 qdl-gpt 和 edl_vip_auth.py):
        /// 1. Digest → 2. TransferCfg → 3. Verify(EnableVip=1) → 4. Signature → 5. SHA256Init → 6. Configure
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath, CancellationToken ct = default(CancellationToken))
        {
            if (!File.Exists(digestPath) || !File.Exists(signaturePath))
            {
                _log("[VIP] 认证失败：缺少 Digest 或 Signature 文件");
                return false;
            }

            _log("[VIP] 开始安全验证 (6步流程)...");
            
            try
            {
                // 清空缓冲区
                PurgeBuffer();

                // ========== Step 1: 直接发送 Digest (二进制数据，不使用 program 命令) ==========
                byte[] digestData = File.ReadAllBytes(digestPath);
                _log(string.Format("[VIP] Step 1/6: 发送 Digest ({0} 字节)...", digestData.Length));
                await _port.WriteAsync(digestData, 0, digestData.Length, ct);
                await Task.Delay(500, ct);
                string resp1 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                if (resp1.Contains("NAK") || resp1.Contains("ERROR"))
                {
                    _log("[VIP] ⚠ Digest 被拒绝，尝试继续...");
                }

                // ========== Step 2: 发送 TransferCfg (关键步骤！) ==========
                _log("[VIP] Step 2/6: 发送 TransferCfg...");
                string transferCfgXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><transfercfg reboot_type=\"off\" timeout_in_sec=\"90\" /></data>";
                _port.Write(Encoding.UTF8.GetBytes(transferCfgXml));
                await Task.Delay(300, ct);
                string resp2 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp2.Contains("NAK") || resp2.Contains("ERROR"))
                {
                    _log("[VIP] ⚠ TransferCfg 失败，尝试继续...");
                }

                // ========== Step 3: 发送 Verify (启用 VIP 模式) ==========
                _log("[VIP] Step 3/6: 发送 Verify (EnableVip=1)...");
                string verifyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><verify value=\"ping\" EnableVip=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(verifyXml));
                await Task.Delay(300, ct);
                string resp3 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp3.Contains("NAK") || resp3.Contains("ERROR"))
                {
                    _log("[VIP] ⚠ Verify 失败，尝试继续...");
                }

                // ========== Step 4: 直接发送 Signature (二进制数据，不使用 program 命令) ==========
                byte[] sigData = File.ReadAllBytes(signaturePath);
                _log(string.Format("[VIP] Step 4/6: 发送 Signature ({0} 字节)...", sigData.Length));
                await _port.WriteAsync(sigData, 0, sigData.Length, ct);
                await Task.Delay(500, ct);
                string resp4 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                if (resp4.Contains("NAK") || resp4.Contains("ERROR"))
                {
                    _log("[VIP] ⚠ Signature 被拒绝");
                    // 签名失败是严重错误，但仍尝试继续
                }

                // ========== Step 5: 发送 SHA256Init ==========
                _log("[VIP] Step 5/6: 发送 SHA256Init...");
                string sha256Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><sha256init Verbose=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(sha256Xml));
                await Task.Delay(300, ct);
                string resp5 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp5.Contains("NAK") || resp5.Contains("ERROR"))
                {
                    _log("[VIP] ⚠ SHA256Init 失败，尝试继续...");
                }

                // Step 6: Configure 将在外部调用
                _log("[VIP] ✓ VIP 验证流程完成 (5/6 步)，等待 Configure...");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[VIP] 验证被取消");
                throw;
            }
            catch (Exception ex)
            {
                _log(string.Format("[VIP] 验证异常: {0}", ex.Message));
                return false;
            }
        }
        
        /// <summary>
        /// 读取并记录设备响应 (异步非阻塞)
        /// </summary>
        private async Task<string> ReadAndLogDeviceResponseAsync(CancellationToken ct, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            var sb = new StringBuilder();
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                
                // 检查可用数据
                int bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    byte[] buffer = new byte[bytesToRead];
                    int read = _port.Read(buffer, 0, bytesToRead);
                    
                    if (read > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        
                        var content = sb.ToString();
                        
                        // 提取并显示设备日志
                        var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                        foreach (Match m in logMatches)
                        {
                            if (m.Groups.Count > 1)
                                _log(string.Format("[Device] {0}", m.Groups[1].Value));
                        }
                        
                        // 检查响应
                        if (content.Contains("<response") || content.Contains("</data>"))
                        {
                            if (content.Contains("value=\"ACK\"") || content.Contains("verify passed"))
                            {
                                return content; // 成功
                            }
                            if (content.Contains("NAK") || content.Contains("ERROR"))
                            {
                                return content; // 失败但返回响应
                            }
                        }
                    }
                }
                
                await Task.Delay(50, ct);
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 获取设备当前的挑战码 (用于在线签名)
        /// </summary>
        public async Task<string> GetVipChallengeAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[VIP] 正在获取设备挑战码 (getsigndata)...");
            string xml = "<?xml version=\"1.0\" ?><data>\n<getsigndata value=\"ping\" />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            // 尝试从返回的 INFO 日志中提取 NV 数据
            var response = await ReadRawResponseAsync(3000, ct);
            if (response != null && response.Contains("NV:"))
            {
                var match = Regex.Match(response, "NV:([^;\\s]+)");
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        /// <summary>
        /// 初始化 SHA256 (OPLUS 分区写入前需要)
        /// </summary>
        public async Task<bool> Sha256InitAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\" ?><data>\n<sha256init />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 完成 SHA256 (OPLUS 分区写入后需要)
        /// </summary>
        public async Task<bool> Sha256FinalAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\" ?><data>\n<sha256final />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
