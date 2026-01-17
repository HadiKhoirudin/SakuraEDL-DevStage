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

        #region VIP 认证

        /// <summary>
        /// VIP 认证 (OPPO/OnePlus/Realme)
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath, CancellationToken ct = default(CancellationToken))
        {
            if (!File.Exists(digestPath) || !File.Exists(signaturePath))
            {
                _log("[VIP] 缺少验证文件");
                return false;
            }

            _log("[VIP] 开始安全验证...");

            try
            {
                PurgeBuffer();

                // Step 1: 发送 Digest
                var digestData = File.ReadAllBytes(digestPath);
                _log(string.Format("[VIP] Step 1: 发送 Digest ({0} 字节)...", digestData.Length));
                _port.Write(digestData);
                await Task.Delay(500, ct);
                await ReadAndLogDeviceResponseAsync(ct, 3000);

                // Step 2: TransferCfg
                _log("[VIP] Step 2: 发送 TransferCfg...");
                string transferCfgXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><transfercfg reboot_type=\"off\" timeout_in_sec=\"90\" /></data>";
                _port.Write(Encoding.UTF8.GetBytes(transferCfgXml));
                await Task.Delay(300, ct);
                await ReadAndLogDeviceResponseAsync(ct, 2000);

                // Step 3: Verify
                _log("[VIP] Step 3: 发送 Verify...");
                string verifyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><verify value=\"ping\" EnableVip=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(verifyXml));
                await Task.Delay(300, ct);
                await ReadAndLogDeviceResponseAsync(ct, 2000);

                // Step 4: Signature
                var sigData = File.ReadAllBytes(signaturePath);
                _log(string.Format("[VIP] Step 4: 发送 Signature ({0} 字节)...", sigData.Length));
                _port.Write(sigData);
                await Task.Delay(500, ct);
                await ReadAndLogDeviceResponseAsync(ct, 3000);

                // Step 5: SHA256Init
                _log("[VIP] Step 5: 发送 SHA256Init...");
                string sha256Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><sha256init Verbose=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(sha256Xml));
                await Task.Delay(300, ct);
                await ReadAndLogDeviceResponseAsync(ct, 2000);

                _log("[VIP] VIP 验证流程完成");
                return true;
            }
            catch (Exception ex)
            {
                _log(string.Format("[VIP] 验证异常: {0}", ex.Message));
                return false;
            }
        }

        private async Task<string> ReadAndLogDeviceResponseAsync(CancellationToken ct, int timeoutMs)
        {
            var startTime = DateTime.Now;
            var sb = new StringBuilder();

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

                        var content = sb.ToString();

                        // 提取设备日志
                        var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                        foreach (Match m in logMatches)
                        {
                            if (m.Groups.Count > 1)
                                _log("[Device] " + m.Groups[1].Value);
                        }

                        if (content.Contains("<response") || content.Contains("</data>"))
                        {
                            if (content.Contains("value=\"ACK\"") || content.Contains("verify passed"))
                                return content;
                            if (content.Contains("NAK") || content.Contains("ERROR"))
                                return content;
                        }
                    }
                }

                await Task.Delay(50, ct);
            }

            return sb.ToString();
        }

        #endregion

        #region 读取分区表

        /// <summary>
        /// 读取 GPT 分区表 (支持多 LUN)
        /// </summary>
        public async Task<List<PartitionInfo>> ReadGptPartitionsAsync(bool useVipMode = false, CancellationToken ct = default(CancellationToken))
        {
            var partitions = new List<PartitionInfo>();
            
            // 重置槽位检测状态，准备合并所有 LUN 的结果
            ResetSlotDetection();

            for (int lun = 0; lun < 6; lun++)
            {
                byte[] gptData = null;

                // GPT 头在 LBA 1，分区条目从 LBA 2 开始
                // 标准 GPT 有 128 个条目，每个 128 字节 = 16KB
                // 对于 4096 字节扇区: 16KB / 4096 = 4 个扇区 + 2 (MBR+Header) = 6 个
                // 对于 512 字节扇区: 16KB / 512 = 32 个扇区 + 2 = 34 个
                // 读取 128 个扇区确保覆盖所有可能的分区条目
                int gptSectors = 128;

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
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("镜像文件不存在", imagePath);

            var fileInfo = new FileInfo(imagePath);
            _log(string.Format("[Firehose] 写入分区: {0} ({1:N0} 字节)", partition.Name, fileInfo.Length));

            var totalBytes = fileInfo.Length;
            var sectorsPerChunk = _maxPayloadSize / _sectorSize;
            var bytesPerChunk = sectorsPerChunk * _sectorSize;
            var totalWritten = 0L;

            StartTransferTimer(totalBytes);

            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, FILE_BUFFER_SIZE))
            {
                var buffer = new byte[bytesPerChunk];
                var currentSector = partition.StartSector;

                while (totalWritten < totalBytes)
                {
                    if (ct.IsCancellationRequested) return false;

                    var bytesToRead = (int)Math.Min(bytesPerChunk, totalBytes - totalWritten);
                    var bytesRead = fs.Read(buffer, 0, bytesToRead);
                    if (bytesRead == 0) break;

                    // 补齐到扇区边界
                    var paddedSize = ((bytesRead + _sectorSize - 1) / _sectorSize) * _sectorSize;
                    if (paddedSize > bytesRead)
                        Array.Clear(buffer, bytesRead, paddedSize - bytesRead);

                    var sectorsToWrite = paddedSize / _sectorSize;

                    if (!await WriteSectorsAsync(partition.Lun, currentSector, buffer, paddedSize, partition.Name, useOppoMode, ct))
                    {
                        _log(string.Format("[Firehose] 写入失败 @ sector {0}", currentSector));
                        return false;
                    }

                    totalWritten += bytesRead;
                    currentSector += sectorsToWrite;

                    if (_progress != null)
                        _progress(totalWritten, totalBytes);
                }
            }

            StopTransferTimer("写入", totalWritten);
            _log(string.Format("[Firehose] 分区 {0} 写入完成: {1:N0} 字节", partition.Name, totalWritten));
            return true;
        }

        /// <summary>
        /// 写入扇区数据
        /// </summary>
        private async Task<bool> WriteSectorsAsync(int lun, long startSector, byte[] data, int length, string label, bool useOppoMode, CancellationToken ct)
        {
            int numSectors = length / _sectorSize;
            string fileName = string.Format("gpt_backup{0}.bin", lun);
            string labelName = "BackupGPT";

            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>" +
                "<program SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"{2}\" " +
                "num_partition_sectors=\"{3}\" physical_partition_number=\"{4}\" start_sector=\"{5}\" />" +
                "</data>",
                _sectorSize, fileName, labelName, numSectors, lun, startSector);

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
        public async Task<bool> FlashPartitionFromFileAsync(string partitionName, string filePath, int lun, long startSector, IProgress<int> progress, CancellationToken ct, bool useVipMode = false)
        {
            if (!File.Exists(filePath))
            {
                _log("Firehose: 文件不存在 - " + filePath);
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;
            int numSectors = (int)Math.Ceiling((double)fileSize / _sectorSize);

            _log(string.Format("Firehose: 刷写 {0} -> {1} ({2:F2} MB){3}", 
                Path.GetFileName(filePath), partitionName, fileSize / 1024.0 / 1024.0,
                useVipMode ? " [VIP模式]" : ""));

            // VIP 模式使用伪装策略
            if (useVipMode)
            {
                return await FlashPartitionVipModeAsync(partitionName, filePath, lun, startSector, numSectors, fileSize, progress, ct);
            }

            // 标准模式
            string xml = string.Format(
                "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\" filename=\"{4}\"/></data>",
                _sectorSize, numSectors, lun, startSector, partitionName);

            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (!await WaitForRawDataModeAsync(ct))
            {
                _log("Firehose: Program 命令被拒绝");
                return false;
            }

            return await SendFileDataAsync(filePath, fileSize, progress, ct);
        }

        /// <summary>
        /// VIP 模式刷写分区 (使用伪装策略)
        /// </summary>
        private async Task<bool> FlashPartitionVipModeAsync(string partitionName, string filePath, int lun, long startSector, int numSectors, long fileSize, IProgress<int> progress, CancellationToken ct)
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

                // VIP 模式 program 命令
                string xml = string.Format(
                    "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" filename=\"{4}\" label=\"{5}\" " +
                    "partofsingleimage=\"true\" sparse=\"false\"/></data>",
                    _sectorSize, numSectors, lun, startSector, spoofFilename, spoofLabel);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (await WaitForRawDataModeAsync(ct))
                {
                    _log(string.Format("[VIP Write] 伪装 {0} 成功，开始传输数据...", spoofLabel));
                    
                    bool success = await SendFileDataAsync(filePath, fileSize, progress, ct);
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
        /// 发送文件数据 (极速优化版)
        /// </summary>
        private async Task<bool> SendFileDataAsync(string filePath, long fileSize, IProgress<int> progress, CancellationToken ct)
        {
            long sent = 0;
            // 使用与设备 MaxPayload 匹配的缓冲区
            byte[] buffer = new byte[_maxPayloadSize];
            int lastPercent = -1;

            // 使用大缓冲区、顺序扫描优化、无缓存写入
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FILE_BUFFER_SIZE, FileOptions.SequentialScan))
            {
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0 && !ct.IsCancellationRequested)
                {
                    // 补齐到扇区边界 (仅最后一块)
                    int actualWrite = read;
                    if (read < buffer.Length && read % _sectorSize != 0)
                    {
                        actualWrite = ((read / _sectorSize) + 1) * _sectorSize;
                        Array.Clear(buffer, read, actualWrite - read);
                    }

                    // 直接写入串口，不等待
                    _port.Write(buffer, 0, actualWrite);
                    sent += read; // 记录实际文件字节数

                    // 调用 _progress 回调传递字节进度（用于速度计算）
                    if (_progress != null)
                        _progress(sent, fileSize);
                    
                    // 每 1% 更新一次 UI 进度
                    int currentPercent = (int)(100.0 * sent / fileSize);
                    if (progress != null && currentPercent > lastPercent)
                    {
                        progress.Report(currentPercent);
                        lastPercent = currentPercent;
                    }

                    // 不再等待，直接继续发送下一块
                    // USB 有硬件流控，无需软件节流
                }
            }

            // 等待设备确认
            return await WaitForAckAsync(ct, 180); // 大文件给更长超时
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
        /// 应用单个补丁
        /// </summary>
        public async Task<bool> ApplyPatchAsync(int lun, long startSector, int byteOffset, int sizeInBytes, string value, CancellationToken ct = default(CancellationToken))
        {
            // 跳过空补丁
            if (string.IsNullOrEmpty(value) || sizeInBytes == 0)
                return true;

            _log(string.Format("[Firehose] 应用补丁: LUN{0} Sector{1} Offset{2} Size{3}", lun, startSector, byteOffset, sizeInBytes));

            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>\n" +
                "<patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" filename=\"DISK\" " +
                "physical_partition_number=\"{2}\" size_in_bytes=\"{3}\" start_sector=\"{4}\" value=\"{5}\" />\n</data>\n",
                _sectorSize, byteOffset, lun, sizeInBytes, startSector, value);

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
                    if (startSectorAttr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        long.TryParse(startSectorAttr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out startSector);
                    else
                        long.TryParse(startSectorAttr, out startSector);

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
        /// 接收 read 命令的响应数据 (极速优化版)
        /// </summary>
        private async Task<bool> ReceiveDataAfterAckAsync(byte[] buffer, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int totalBytes = buffer.Length;
                    int received = 0;
                    bool headerFound = false;
                    // 使用 1MB 缓冲区以匹配 USB 传输速度
                    byte[] tempBuf = new byte[1024 * 1024];

                    while (received < totalBytes)
                    {
                        if (ct.IsCancellationRequested) return false;

                        // 根据阶段调整请求大小
                        // 头部阶段: 小请求找 XML 结尾
                        // 数据阶段: 大请求最大化吞吐量
                        int requestSize = headerFound 
                            ? Math.Min(tempBuf.Length, totalBytes - received) 
                            : 16384;

                        byte[] readData = _port.TryReadExactAsync(requestSize, 15000, ct).Result;
                        if (readData == null || readData.Length == 0)
                        {
                            _log("[Read] 超时，无数据");
                            return false;
                        }

                        int read = readData.Length;
                        Array.Copy(readData, tempBuf, read);

                        int dataStartOffset = 0;

                        if (!headerFound)
                        {
                            string content = Encoding.UTF8.GetString(tempBuf, 0, read);

                            int ackIndex = content.IndexOf("rawmode=\"true\"", StringComparison.OrdinalIgnoreCase);
                            if (ackIndex == -1)
                                ackIndex = content.IndexOf("rawmode='true'", StringComparison.OrdinalIgnoreCase);

                            if (ackIndex >= 0)
                            {
                                int xmlEndIndex = content.IndexOf("</data>", ackIndex);
                                if (xmlEndIndex >= 0)
                                {
                                    headerFound = true;
                                    dataStartOffset = xmlEndIndex + 7;

                                    while (dataStartOffset < read && (tempBuf[dataStartOffset] == '\n' || tempBuf[dataStartOffset] == '\r'))
                                        dataStartOffset++;

                                    if (dataStartOffset >= read) continue;
                                }
                            }
                            else if (content.Contains("NAK"))
                            {
                                _log(string.Format("[Read] 设备拒绝: {0}", content.Substring(0, Math.Min(content.Length, 100))));
                                return false;
                            }
                            else
                            {
                                // 跳过日志消息，不打印避免阻塞
                                continue;
                            }
                        }

                        int dataLength = read - dataStartOffset;
                        if (dataLength > 0)
                        {
                            if (received + dataLength > buffer.Length)
                                dataLength = buffer.Length - received;
                            Array.Copy(tempBuf, dataStartOffset, buffer, received, dataLength);
                            received += dataLength;
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    _log(string.Format("[Read] 异常: {0}", ex.Message));
                    return false;
                }
            }, ct);
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
