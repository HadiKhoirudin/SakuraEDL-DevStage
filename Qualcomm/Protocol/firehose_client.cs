// ============================================================================
// LoveAlways - Firehose Protocol Complete Implementation
// Firehose Protocol - Qualcomm EDL Mode XML Flash Protocol
// ============================================================================
// Module: Qualcomm.Protocol
// Features: Read/Write Partitions, VIP Auth, GPT Operations, Device Control
// Support: UFS/eMMC Storage, Sparse Format, Dynamic Masquerade
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Concurrent;
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
    #region Error Handling

    /// <summary>
    /// Firehose Error Code Helper
    /// </summary>
    public static class FirehoseErrorHelper
    {
        public static void ParseNakError(string errorText, out string message, out string suggestion, out bool isFatal, out bool canRetry)
        {
            message = "Unknown error";
            suggestion = "Please retry operation";
            isFatal = false;
            canRetry = true;

            if (string.IsNullOrEmpty(errorText))
                return;

            string lower = errorText.ToLowerInvariant();

            if (lower.Contains("authentication") || lower.Contains("auth failed"))
            {
                message = "Authentication failed";
                suggestion = "Device requires special authentication";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("signature") || lower.Contains("sign"))
            {
                message = "Signature verification failed";
                suggestion = "Image signature incorrect";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("hash") && (lower.Contains("mismatch") || lower.Contains("fail")))
            {
                message = "Hash verification failed";
                suggestion = "Data integrity check failed";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("partition not found"))
            {
                message = "Partition not found";
                suggestion = "This partition does not exist on device";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("invalid lun"))
            {
                message = "Invalid LUN";
                suggestion = "Specified LUN does not exist";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("write protect"))
            {
                message = "Write protected";
                suggestion = "Storage device is write protected";
                isFatal = true;
                canRetry = false;
            }
            else if (lower.Contains("timeout"))
            {
                message = "Timeout";
                suggestion = "Operation timeout, retry recommended";
                isFatal = false;
                canRetry = true;
            }
            else if (lower.Contains("busy"))
            {
                message = "Device busy";
                suggestion = "Device processing other operations";
                isFatal = false;
                canRetry = true;
            }
            else
            {
                message = "Device error: " + errorText;
                suggestion = "Please check full error message";
            }
        }
    }

    #endregion

    #region VIP Masquerade Strategy

    /// <summary>
    /// VIP Masquerade Strategy
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

    #region Simple Buffer Pool

    /// <summary>
    /// Simple Byte Array Buffer Pool (Reduce GC pressure)
    /// </summary>
    internal static class SimpleBufferPool
    {
        private static readonly ConcurrentBag<byte[]> _pool16MB = new ConcurrentBag<byte[]>();
        private static readonly ConcurrentBag<byte[]> _pool4MB = new ConcurrentBag<byte[]>();
        private const int SIZE_16MB = 16 * 1024 * 1024;
        private const int SIZE_4MB = 4 * 1024 * 1024;

        public static byte[] Rent(int minSize)
        {
            if (minSize <= SIZE_4MB)
            {
                if (_pool4MB.TryTake(out byte[] buf4))
                    return buf4;
                return new byte[SIZE_4MB];
            }
            if (minSize <= SIZE_16MB)
            {
                if (_pool16MB.TryTake(out byte[] buf16))
                    return buf16;
                return new byte[SIZE_16MB];
            }
            // Very large buffers not pooled
            return new byte[minSize];
        }

        public static void Return(byte[] buffer)
        {
            if (buffer == null) return;
            // Only pool standard sizes
            if (buffer.Length == SIZE_4MB && _pool4MB.Count < 4)
                _pool4MB.Add(buffer);
            else if (buffer.Length == SIZE_16MB && _pool16MB.Count < 2)
                _pool16MB.Add(buffer);
            // Other sizes let GC collect
        }
    }

    #endregion

    /// <summary>
    /// Firehose Protocol Client - Full Version
    /// </summary>
    public class FirehoseClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;  // Detailed debug log (File only)
        private readonly Action<long, long> _progress;
        private bool _disposed;
        private readonly StringBuilder _rxBuffer = new StringBuilder();

        // Configuration - Speed optimization
        private int _sectorSize = 4096;
        private int _maxPayloadSize = 16777216; // 16MB default payload

        private const int ACK_TIMEOUT_MS = 15000;          // Large files need longer timeout
        private const int FILE_BUFFER_SIZE = 4 * 1024 * 1024;  // 4MB file buffer (Improve read speed)
        private const int OPTIMAL_PAYLOAD_REQUEST = 16 * 1024 * 1024; // Request 16MB payload (Device may return smaller)

        // Chunk transfer config (Default 0 = No chunking, use max device payload)
        private int _customChunkSize = 0;

        // Public properties
        public string StorageType { get; private set; }
        public int SectorSize { get { return _sectorSize; } }
        public int MaxPayloadSize { get { return _maxPayloadSize; } }
        
        /// <summary>
        /// Get current effective chunk size
        /// </summary>
        public int EffectiveChunkSize 
        { 
            get 
            { 
                if (_customChunkSize > 0)
                    return Math.Min(_customChunkSize, _maxPayloadSize);
                return _maxPayloadSize;
            } 
        }
        
        /// <summary>
        /// Set custom chunk size (0 = Use default)
        /// </summary>
        /// <param name="chunkSize">Chunk size (Bytes), must be multiple of sector size</param>
        public void SetChunkSize(int chunkSize)
        {
            if (chunkSize < 0)
                throw new ArgumentException("Chunk size cannot be negative");
                
            if (chunkSize > 0)
            {
                // Ensure multiple of sector size
                chunkSize = (chunkSize / _sectorSize) * _sectorSize;
                if (chunkSize < _sectorSize)
                    chunkSize = _sectorSize;
                    
                // Cannot exceed device max payload
                chunkSize = Math.Min(chunkSize, _maxPayloadSize);
            }
            
            _customChunkSize = chunkSize;
            if (chunkSize == 0)
                _logDetail(string.Format("[Firehose] Chunk mode: Off (Using device max {0})", FormatSize(_maxPayloadSize)));
            else
                _logDetail(string.Format("[Firehose] Chunk mode: On ({0}/chunk)", FormatSize(chunkSize)));
        }
        
        /// <summary>
        /// Set chunk size (In MB)
        /// </summary>
        public void SetChunkSizeMB(int megabytes)
        {
            SetChunkSize(megabytes * 1024 * 1024);
        }
        
        /// <summary>
        /// Format size display
        /// </summary>
        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB", bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024 * 1024)
                return string.Format("{0:F1} MB", bytes / (1024.0 * 1024));
            if (bytes >= 1024)
                return string.Format("{0:F0} KB", bytes / 1024.0);
            return string.Format("{0} B", bytes);
        }
        public List<string> SupportedFunctions { get; private set; }

        // Chip info
        public string ChipSerial { get; set; }
        public string ChipHwId { get; set; }
        public string ChipPkHash { get; set; }

        // GPT Header info for each LUN (For negative sector conversion)
        private Dictionary<int, GptHeaderInfo> _lunHeaders = new Dictionary<int, GptHeaderInfo>();

        /// <summary>
        /// Get LUN total sectors (For negative sector conversion)
        /// </summary>
        public long GetLunTotalSectors(int lun)
        {
            GptHeaderInfo header;
            if (_lunHeaders.TryGetValue(lun, out header))
            {
                // AlternateLba is backup GPT Header position (Usually last sector of disk)
                // Total sectors = AlternateLba + 1
                return (long)(header.AlternateLba + 1);
            }
            return -1; // Unknown
        }

        /// <summary>
        /// Convert negative sector to absolute sector (Negative means count from end)
        /// </summary>
        public long ResolveNegativeSector(int lun, long sector)
        {
            if (sector >= 0) return sector;
            
            long totalSectors = GetLunTotalSectors(lun);
            if (totalSectors <= 0)
            {
                _logDetail(string.Format("[GPT] Cannot resolve negative sector: LUN{0} total sectors unknown", lun));
                return -1;
            }
            
            // Negative sector means count from end
            // Example: -5 means totalSectors - 5
            long absoluteSector = totalSectors + sector;
            _logDetail(string.Format("[GPT] Negative sector conversion: LUN{0} sector {1} -> {2} (Total sectors: {3})", 
                lun, sector, absoluteSector, totalSectors));
            return absoluteSector;
        }

        // OnePlus auth parameters (Save after auth success, attach when writing)
        public string OnePlusProgramToken { get; set; }
        public string OnePlusProgramPk { get; set; }
        public string OnePlusProjId { get; set; }
        public bool IsOnePlusAuthenticated { get { return !string.IsNullOrEmpty(OnePlusProgramToken); } }

        // Partition cache
        private List<PartitionInfo> _cachedPartitions = null;

        // Speed statistics
        private Stopwatch _transferStopwatch;
        private long _transferTotalBytes;

        public bool IsConnected { get { return _port.IsOpen; } }

        public FirehoseClient(SerialPortManager port, Action<string> log = null, Action<long, long> progress = null, Action<string> logDetail = null)
        {
            _port = port;
            _log = log ?? delegate { };
            _logDetail = logDetail ?? delegate { };
            _progress = progress;
            StorageType = "ufs";
            SupportedFunctions = new List<string>();
            ChipSerial = "";
            ChipHwId = "";
            ChipPkHash = "";
        }

        /// <summary>
        /// Report byte-level progress (For speed calculation)
        /// </summary>
        public void ReportProgress(long current, long total)
        {
            if (_progress != null)
                _progress(current, total);
        }

        #region Dynamic Masquerade Strategy

        /// <summary>
        /// Get dynamic masquerade strategy list
        /// </summary>
        public static List<VipSpoofStrategy> GetDynamicSpoofStrategies(int lun, long startSector, string partitionName, bool isGptRead)
        {
            var strategies = new List<VipSpoofStrategy>();

            // GPT area special handling
            if (isGptRead || startSector <= 33)
            {
                strategies.Add(new VipSpoofStrategy(string.Format("gpt_backup{0}.bin", lun), "BackupGPT", 0));
                strategies.Add(new VipSpoofStrategy(string.Format("gpt_main{0}.bin", lun), "PrimaryGPT", 1));
            }

            // Generic backup masquerade
            strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", "BackupGPT", 2));

            // Partition name masquerade
            if (!string.IsNullOrEmpty(partitionName))
            {
                string safeName = SanitizePartitionName(partitionName);
                strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", safeName, 3));
                strategies.Add(new VipSpoofStrategy(safeName + ".bin", safeName, 4));
            }

            // Generic masquerade
            strategies.Add(new VipSpoofStrategy("ssd", "ssd", 5));
            strategies.Add(new VipSpoofStrategy("gpt_main0.bin", "gpt_main0.bin", 6));
            strategies.Add(new VipSpoofStrategy("buffer.bin", "buffer", 8));

            // No masquerade
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

        #region Basic Configuration

        /// <summary>
        /// Configure Firehose
        /// </summary>
        public async Task<bool> ConfigureAsync(string storageType = "ufs", int preferredPayloadSize = 0, CancellationToken ct = default(CancellationToken))
        {
            StorageType = storageType.ToLower();
            _sectorSize = (StorageType == "emmc") ? 512 : 4096;

            int requestedPayload = preferredPayloadSize > 0 ? preferredPayloadSize : OPTIMAL_PAYLOAD_REQUEST;

            // Optimization: Request larger bidirectional transfer buffer
            // MaxPayloadSizeToTargetInBytes - Max size per block when writing
            // MaxPayloadSizeFromTargetInBytes - Max size per block when reading (Critical optimization!)
            // AckRawDataEveryNumPackets=0 - No per-packet ack needed, speeds up transfer
            // ZlpAwareHost=1 - Enable zero-length packet awareness, improves USB efficiency
            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data><configure MemoryName=\"{0}\" Verbose=\"0\" " +
                "AlwaysValidate=\"0\" MaxPayloadSizeToTargetInBytes=\"{1}\" " +
                "MaxPayloadSizeFromTargetInBytes=\"{1}\" " +
                "AckRawDataEveryNumPackets=\"0\" ZlpAwareHost=\"1\" " +
                "SkipStorageInit=\"0\" /></data>",
                storageType, requestedPayload);

            _log("[Firehose] Configuring device...");
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

                        _logDetail(string.Format("[Firehose] Configure success - SectorSize:{0}, MaxPayload:{1}KB", _sectorSize, _maxPayloadSize / 1024));
                        return true;
                    }
                }
                await Task.Delay(50, ct);
            }
            return false;
        }

        /// <summary>
        /// Set storage sector size
        /// </summary>
        public void SetSectorSize(int size)
        {
            _sectorSize = size;
        }

        #endregion

        #region Read GPT

        /// <summary>
        /// Read GPT partition table (Support multiple LUN)
        /// </summary>
        public async Task<List<PartitionInfo>> ReadGptPartitionsAsync(bool useVipMode = false, CancellationToken ct = default(CancellationToken), IProgress<int> lunProgress = null)
        {
            var partitions = new List<PartitionInfo>();
            
            // Reset slot detection state, prepare to merge results from all LUNs
            ResetSlotDetection();

            for (int lun = 0; lun < 6; lun++)
            {
                // Report current LUN progress
                if (lunProgress != null) lunProgress.Report(lun);
                byte[] gptData = null;

                // GPT header at LBA 1, partition entries start from LBA 2
                // Xiaomi/Redmi devices may have over 128 partition entries (up to 256)
                // 256 entries * 128 bytes = 32KB
                // For 512 byte sectors: 32KB / 512 = 64 sectors + 2 (MBR+Header) = 66
                // For 4096 byte sectors: 32KB / 4096 = 8 sectors + 2 = 10
                // Read 256 sectors to ensure covering all possible partition entries (including Xiaomi devices)
                // For 512B sectors = 128KB, for 4KB sectors = 1MB
                int gptSectors = 256;

                if (useVipMode)
                {
                    // VIP mode GPT read - Waterfall strategy (Reference tools project)
                    // ⚠️ OPPO/Realme devices MUST prioritize BackupGPT masquerade, otherwise will freeze
                    // UFS devices only need to read 6 sectors (24KB), eMMC read 34 sectors
                    int vipGptSectors = (_sectorSize == 4096) ? 6 : 34;
                    
                    _log(string.Format("[GPT] VIP mode reading LUN{0} ({1} sectors, sector size={2})...", lun, vipGptSectors, _sectorSize));
                    
                    var readStrategies = new string[,]
                    {
                        { "BackupGPT", string.Format("gpt_backup{0}.bin", lun) },  // Priority 1
                        { "BackupGPT", "gpt_backup0.bin" },                         // Priority 2
                        { "PrimaryGPT", string.Format("gpt_main{0}.bin", lun) },   // Priority 3
                        { "ssd", "ssd" }                                            // Priority 4
                    };

                    for (int i = 0; i < readStrategies.GetLength(0); i++)
                    {
                        try
                        {
                            _logDetail(string.Format("[GPT] LUN{0} trying strategy {1}: {2}/{3}", lun, i + 1, readStrategies[i, 0], readStrategies[i, 1]));
                            gptData = await ReadGptPacketWithTimeoutAsync(lun, 0, vipGptSectors, readStrategies[i, 0], readStrategies[i, 1], ct, 15000);
                            if (gptData != null && gptData.Length >= 512)
                            {
                                _log(string.Format("[GPT] LUN{0} masquerade {1} success", lun, readStrategies[i, 0]));
                                break;
                            }
                        }
                        catch (TimeoutException)
                        {
                            _log(string.Format("[GPT] LUN{0} strategy {1} timeout, trying next...", lun, readStrategies[i, 0]));
                        }
                        catch (Exception ex)
                        {
                            _logDetail(string.Format("[GPT] LUN{0} strategy {1} exception: {2}", lun, readStrategies[i, 0], ex.Message));
                        }
                        
                        await Task.Delay(200, ct); // Strategy switch interval increased to 200ms
                    }
                }
                else
                {
                    try
                    {
                        PurgeBuffer();
                        if (lun > 0) await Task.Delay(50, ct);

                        _logDetail(string.Format("[GPT] Reading LUN{0}...", lun));
                        gptData = await ReadSectorsAsync(lun, 0, gptSectors, ct);
                        if (gptData != null && gptData.Length >= 512)
                        {
                            _logDetail(string.Format("[GPT] LUN{0} read success ({1} bytes)", lun, gptData.Length));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDetail(string.Format("[GPT] LUN{0} read exception: {1}", lun, ex.Message));
                    }
                }

                if (gptData == null || gptData.Length < 512)
                    continue;

                var lunPartitions = ParseGptPartitions(gptData, lun);
                if (lunPartitions.Count > 0)
                {
                    partitions.AddRange(lunPartitions);
                    _logDetail(string.Format("[Firehose] LUN {0}: {1} partitions", lun, lunPartitions.Count));
                }
            }

            if (partitions.Count > 0)
            {
                _cachedPartitions = partitions;
                _log(string.Format("[Firehose] Total {0} partitions read", partitions.Count));
                
                // Output merged slot state
                if (_mergedSlot != "nonexistent")
                {
                    _logDetail(string.Format("[Firehose] Device slot: {0} (A active={1}, B active={2})", 
                        _mergedSlot, _slotACount, _slotBCount));
                }
            }

            return partitions;
        }

        /// <summary>
        /// Read GPT packet (using masquerade)
        /// </summary>
        public async Task<byte[]> ReadGptPacketAsync(int lun, long startSector, int numSectors, string label, string filename, CancellationToken ct)
        {
            return await ReadGptPacketWithTimeoutAsync(lun, startSector, numSectors, label, filename, ct, 30000);
        }

        /// <summary>
        /// Read GPT packet (with timeout protection to prevent hang)
        /// </summary>
        public async Task<byte[]> ReadGptPacketWithTimeoutAsync(int lun, long startSector, int numSectors, string label, string filename, CancellationToken ct, int timeoutMs = 10000)
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

            _logDetail(string.Format("[GPT] Reading LUN{0} (Masquerade: {1}/{2})...", lun, label, filename));
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            var buffer = new byte[numSectors * _sectorSize];
            
            // Use timeout protection to prevent hanging if device does not respond
            using (var timeoutCts = new CancellationTokenSource(timeoutMs))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
            {
                try
                {
                    // Use receiving method with timeout
                    var receiveTask = ReceiveDataAfterAckAsync(buffer, linkedCts.Token);
                    var delayTask = Task.Delay(timeoutMs, ct);
                    
                    var completedTask = await Task.WhenAny(receiveTask, delayTask);
                    
                    if (completedTask == delayTask)
                    {
                        _logDetail(string.Format("[GPT] LUN{0} read timeout ({1}ms)", lun, timeoutMs));
                        throw new TimeoutException(string.Format("GPT read timeout: LUN{0}", lun));
                    }
                    
                    if (await receiveTask)
                    {
                        await WaitForAckAsync(linkedCts.Token, 10);
                        _logDetail(string.Format("[GPT] LUN{0} read success ({1} bytes)", lun, buffer.Length));
                        return buffer;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    _logDetail(string.Format("[GPT] LUN{0} read timeout ({1}ms)", lun, timeoutMs));
                    throw new TimeoutException(string.Format("GPT read timeout: LUN{0}", lun));
                }
                catch (TimeoutException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logDetail(string.Format("[GPT] LUN{0} read exception: {1}", lun, ex.Message));
                }
            }

            _logDetail(string.Format("[GPT] LUN{0} read failed", lun));
            return null;
        }

        /// <summary>
        /// Last parsed GPT results (including slot info)
        /// </summary>
        public GptParseResult LastGptResult { get; private set; }

        /// <summary>
        /// Merged slot state (from all LUNs)
        /// </summary>
        private string _mergedSlot = "nonexistent";
        private int _slotACount = 0;
        private int _slotBCount = 0;

        /// <summary>
        /// Current slot ("a", "b", "undefined", "nonexistent") - merged results from all LUNs
        /// </summary>
        public string CurrentSlot
        {
            get { return _mergedSlot; }
        }

        /// <summary>
        /// Reset slot detection state (call before starting new GPT read)
        /// </summary>
        public void ResetSlotDetection()
        {
            _mergedSlot = "nonexistent";
            _slotACount = 0;
            _slotBCount = 0;
        }

        /// <summary>
        /// Merge LUN slot detection results
        /// </summary>
        private void MergeSlotInfo(GptParseResult result)
        {
            if (result?.SlotInfo == null) return;
            
            var slotInfo = result.SlotInfo;
            
            // If this LUN has A/B partitions
            if (slotInfo.HasAbPartitions)
            {
                // At least one A/B partition exists
                if (_mergedSlot == "nonexistent")
                    _mergedSlot = "undefined";
                
                // Count active slots
                if (slotInfo.CurrentSlot == "a")
                    _slotACount++;
                else if (slotInfo.CurrentSlot == "b")
                    _slotBCount++;
            }
            
            // Determine final slot based on statistics
            if (_slotACount > _slotBCount && _slotACount > 0)
                _mergedSlot = "a";
            else if (_slotBCount > _slotACount && _slotBCount > 0)
                _mergedSlot = "b";
            else if (_slotACount > 0 && _slotBCount > 0)
                _mergedSlot = "unknown";  // Conflict
            // Otherwise keep "undefined" or "nonexistent"
        }

        /// <summary>
        /// Parse GPT partitions (using enhanced GptParser)
        /// </summary>
        public List<PartitionInfo> ParseGptPartitions(byte[] gptData, int lun)
        {
            var parser = new GptParser(_log, _logDetail);
            var result = parser.Parse(gptData, lun, _sectorSize);
            
            // Save parsing result
            LastGptResult = result;
            
            // Merge slot detection results
            MergeSlotInfo(result);

            if (result.Success && result.Header != null)
            {
                // Store LUN Header info (for negative sector conversion)
                _lunHeaders[lun] = result.Header;

                // Automatically update sector size
                if (result.Header.SectorSize > 0 && result.Header.SectorSize != _sectorSize)
                {
                    _logDetail(string.Format("[GPT] Update sector size: {0} -> {1}", _sectorSize, result.Header.SectorSize));
                    _sectorSize = result.Header.SectorSize;
                }

                // Output detailed info (to log file only)
                _logDetail(string.Format("[GPT] Disk GUID: {0}", result.Header.DiskGuid));
                _logDetail(string.Format("[GPT] Partition Data Area: LBA {0} - {1}", 
                    result.Header.FirstUsableLba, result.Header.LastUsableLba));
                _logDetail(string.Format("[GPT] CRC: {0}", result.Header.CrcValid ? "Valid" : "Invalid"));
                
                if (result.SlotInfo.HasAbPartitions)
                {
                    _logDetail(string.Format("[GPT] Current Slot: {0}", result.SlotInfo.CurrentSlot));
                }
            }
            else if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _logDetail(string.Format("[GPT] Parse failed: {0}", result.ErrorMessage));
            }

            return result.Partitions;
        }

        /// <summary>
        /// Generate rawprogram.xml
        /// </summary>
        public string GenerateRawprogramXml()
        {
            if (_cachedPartitions == null || _cachedPartitions.Count == 0)
                return null;

            var parser = new GptParser(_log, _logDetail);
            return parser.GenerateRawprogramXml(_cachedPartitions, _sectorSize);
        }

        /// <summary>
        /// Generate partition.xml
        /// </summary>
        public string GeneratePartitionXml()
        {
            if (_cachedPartitions == null || _cachedPartitions.Count == 0)
                return null;

            var parser = new GptParser(_log, _logDetail);
            return parser.GeneratePartitionXml(_cachedPartitions, _sectorSize);
        }

        #endregion

        #region Read Partition

        /// <summary>
        /// Read partition to file (Supports custom chunking)
        /// </summary>
        /// <param name="partition">Partition info</param>
        /// <param name="savePath">Save path</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="chunkProgress">Chunk progress callback (current chunk index, total chunks, chunk bytes)</param>
        public async Task<bool> ReadPartitionAsync(PartitionInfo partition, string savePath, 
            CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            return await ReadPartitionChunkedAsync(partition.Lun, partition.StartSector, 
                partition.NumSectors, partition.SectorSize, savePath, partition.Name, ct, chunkProgress);
        }

        /// <summary>
        /// Read partition to file in chunks (Core implementation)
        /// </summary>
        /// <param name="lun">LUN number</param>
        /// <param name="startSector">Start sector</param>
        /// <param name="numSectors">Number of sectors</param>
        /// <param name="sectorSize">Sector size</param>
        /// <param name="savePath">Save path</param>
        /// <param name="label">Partition name (for logging)</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="chunkProgress">Chunk progress callback</param>
        public async Task<bool> ReadPartitionChunkedAsync(int lun, long startSector, long numSectors, 
            int sectorSize, string savePath, string label,
            CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            _log(string.Format("[Firehose] Reading: {0} ({1})", label, FormatSize(numSectors * sectorSize)));

            // Use effective chunk size (default use device max, no chunking)
            int chunkSize = EffectiveChunkSize;
            long sectorsPerChunk = chunkSize / sectorSize;
            
            // Calculate total chunks
            int totalChunks = (int)Math.Ceiling((double)numSectors / sectorsPerChunk);
            long totalSize = numSectors * sectorSize;
            long totalRead = 0L;
            
            // Only show chunk info when custom chunking is enabled
            if (_customChunkSize > 0)
            {
                _logDetail(string.Format("[Firehose] Chunk transfer: {0}/chunk, total {1} chunks", 
                    FormatSize(chunkSize), totalChunks));
            }

            StartTransferTimer(totalSize);

            using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, FILE_BUFFER_SIZE))
            {
                for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
                {
                    if (ct.IsCancellationRequested) 
                    {
                        _log("[Firehose] Read cancelled");
                        return false;
                    }

                    long sectorOffset = chunkIndex * sectorsPerChunk;
                    long sectorsToRead = Math.Min(sectorsPerChunk, numSectors - sectorOffset);
                    long currentStartSector = startSector + sectorOffset;

                    // Chunk progress callback
                    chunkProgress?.Invoke(chunkIndex + 1, totalChunks, sectorsToRead * sectorSize);

                    var data = await ReadSectorsAsync(lun, currentStartSector, (int)sectorsToRead, ct);
                    if (data == null)
                    {
                        _log(string.Format("[Firehose] Read failed @ chunk {0}/{1}, sector {2}", 
                            chunkIndex + 1, totalChunks, currentStartSector));
                        return false;
                    }

                    await fs.WriteAsync(data, 0, data.Length, ct);
                    totalRead += data.Length;

                    // Total progress callback
                    _progress?.Invoke(totalRead, totalSize);
                }
            }

            StopTransferTimer("Read", totalRead);
            _log(string.Format("[Firehose] {0} read complete: {1}", label, FormatSize(totalRead)));
            return true;
        }

        /// <summary>
        /// Read to memory in chunks (Suitable for small partitions)
        /// </summary>
        public async Task<byte[]> ReadPartitionToMemoryAsync(PartitionInfo partition, 
            CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            return await ReadToMemoryChunkedAsync(partition.Lun, partition.StartSector, 
                partition.NumSectors, partition.Name, ct, chunkProgress);
        }

        /// <summary>
        /// Read to memory in chunks (Core implementation)
        /// </summary>
        public async Task<byte[]> ReadToMemoryChunkedAsync(int lun, long startSector, long numSectors,
            string label, CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            int chunkSize = EffectiveChunkSize;
            long sectorsPerChunk = chunkSize / _sectorSize;
            int totalChunks = (int)Math.Ceiling((double)numSectors / sectorsPerChunk);
            long totalSize = numSectors * _sectorSize;

            using (var ms = new MemoryStream((int)Math.Min(totalSize, int.MaxValue)))
            {
                long totalRead = 0L;

                for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
                {
                    if (ct.IsCancellationRequested) return null;

                    long sectorOffset = chunkIndex * sectorsPerChunk;
                    long sectorsToRead = Math.Min(sectorsPerChunk, numSectors - sectorOffset);
                    long currentStartSector = startSector + sectorOffset;

                    chunkProgress?.Invoke(chunkIndex + 1, totalChunks, sectorsToRead * _sectorSize);

                    var data = await ReadSectorsAsync(lun, currentStartSector, (int)sectorsToRead, ct);
                    if (data == null) return null;

                    ms.Write(data, 0, data.Length);
                    totalRead += data.Length;

                    _progress?.Invoke(totalRead, totalSize);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Read sector data
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
                    catch (Exception ex)
                    {
                        // VIP strategy attempt failed, continue to next strategy
                        _logDetail(string.Format("[Firehose] VIP strategy {0} failed: {1}", strategy.Label ?? "Direct read", ex.Message));
                    }
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
                    _log(string.Format("[Read] Exception: {0}", ex.Message));
                }

                return null;
            }
        }

        #endregion

        #region Write Partition

        /// <summary>
        /// Write partition data (Supports custom chunking)
        /// </summary>
        /// <param name="partition">Partition info</param>
        /// <param name="imagePath">Image file path</param>
        /// <param name="useOppoMode">Whether to use OPPO mode</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="chunkProgress">Chunk progress callback (current chunk index, total chunks, chunk bytes)</param>
        public async Task<bool> WritePartitionAsync(PartitionInfo partition, string imagePath, 
            bool useOppoMode = false, CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            return await WritePartitionChunkedAsync(partition.Lun, partition.StartSector, _sectorSize, 
                imagePath, partition.Name, useOppoMode, ct, chunkProgress);
        }

        /// <summary>
        /// Write partition data in chunks (Core implementation)
        /// </summary>
        public async Task<bool> WritePartitionChunkedAsync(int lun, long startSector, int sectorSize, 
            string imagePath, string label = "Partition", bool useOppoMode = false, 
            CancellationToken ct = default(CancellationToken),
            Action<int, int, long> chunkProgress = null)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image file does not exist", imagePath);

            // Check if it is a Sparse image
            bool isSparse = SparseStream.IsSparseFile(imagePath);
            
            if (isSparse)
            {
                // Smart Sparse write: only write parts with data, skip DONT_CARE
                return await WriteSparsePartitionSmartAsync(lun, startSector, sectorSize, imagePath, label, useOppoMode, ct);
            }
            
            long fileSize = new FileInfo(imagePath).Length;
            _log(string.Format("[Firehose] Writing: {0} ({1})", label, FormatSize(fileSize)));

            // Use effective chunk size (default use device maximum, no chunking)
            int chunkSize = EffectiveChunkSize;
            long sectorsPerChunk = chunkSize / sectorSize;
            long bytesPerChunk = sectorsPerChunk * sectorSize;
            
            // Calculate total chunks
            int totalChunks = (int)Math.Ceiling((double)fileSize / bytesPerChunk);
            
            // Only show chunk info when custom chunking is enabled
            if (_customChunkSize > 0)
            {
                _logDetail(string.Format("[Firehose] Chunk transfer: {0}/chunk, total {1} chunks", 
                    FormatSize(chunkSize), totalChunks));
            }

            using (Stream sourceStream = File.OpenRead(imagePath))
            {
                var totalBytes = sourceStream.Length;
                var totalWritten = 0L;
                int currentChunk = 0;

                StartTransferTimer(totalBytes);

                // Use SimpleBufferPool to reduce GC pressure
                var buffer = SimpleBufferPool.Rent((int)bytesPerChunk);
                try
                {
                    var currentSector = startSector;

                    while (totalWritten < totalBytes)
                    {
                        if (ct.IsCancellationRequested) 
                        {
                            _log("[Firehose] Write cancelled");
                            return false;
                        }

                        currentChunk++;
                        var bytesToRead = (int)Math.Min(bytesPerChunk, totalBytes - totalWritten);
                        var bytesRead = sourceStream.Read(buffer, 0, bytesToRead);
                        if (bytesRead == 0) break;

                        // Chunk progress callback
                        chunkProgress?.Invoke(currentChunk, totalChunks, bytesRead);

                        // Pad to sector boundary
                        var paddedSize = ((bytesRead + sectorSize - 1) / sectorSize) * sectorSize;
                        if (paddedSize > bytesRead)
                            Array.Clear(buffer, bytesRead, paddedSize - bytesRead);

                        var sectorsToWrite = paddedSize / sectorSize;

                        if (!await WriteSectorsAsync(lun, currentSector, buffer, paddedSize, label, useOppoMode, ct))
                        {
                            _log(string.Format("[Firehose] Write failed @ chunk {0}/{1}, sector {2}", 
                                currentChunk, totalChunks, currentSector));
                            return false;
                        }

                        totalWritten += bytesRead;
                        currentSector += sectorsToWrite;

                        // Total progress callback
                        _progress?.Invoke(totalWritten, totalBytes);
                    }

                    StopTransferTimer("Write", totalWritten);
                    _log(string.Format("[Firehose] {0} write complete: {1}", label, FormatSize(totalWritten)));
                    return true;
                }
                finally
                {
                    SimpleBufferPool.Return(buffer);
                }
            }
        }

        /// <summary>
        /// Smart write Sparse image (only write chunks with data, skip DONT_CARE)
        /// </summary>
        private async Task<bool> WriteSparsePartitionSmartAsync(int lun, long startSector, int sectorSize, string imagePath, string label, bool useOppoMode, CancellationToken ct)
        {
            using (var sparse = SparseStream.Open(imagePath, _log))
            {
                var totalExpandedSize = sparse.Length;
                var realDataSize = sparse.GetRealDataSize();
                var dataRanges = sparse.GetDataRanges();
                
                // Main log shows write info
                _log(string.Format("[Firehose] Writing: {0} ({1}) [Sparse]", label, FormatFileSize(realDataSize)));
                _logDetail(string.Format("[Sparse] Expanded size: {0:N0} MB, Real data: {1:N0} MB, Saved: {2:P1}", 
                    totalExpandedSize / 1024.0 / 1024.0, 
                    realDataSize / 1024.0 / 1024.0,
                    1.0 - (double)realDataSize / totalExpandedSize));
                
                if (dataRanges.Count == 0)
                {
                    // Empty Sparse image: use erase command to clear partition
                    _logDetail(string.Format("[Sparse] Image has no data, erasing partition {0}...", label));
                    long numSectors = totalExpandedSize / sectorSize;
                    bool eraseOk = await EraseSectorsAsync(lun, startSector, numSectors, ct);
                    if (eraseOk)
                        _logDetail(string.Format("[Sparse] Partition {0} erase complete ({1:F2} MB)", label, totalExpandedSize / 1024.0 / 1024.0));
                    else
                        _log(string.Format("[Sparse] Partition {0} erase failed", label));
                    return eraseOk;
                }
                
                var sectorsPerChunk = _maxPayloadSize / sectorSize;
                var bytesPerChunk = sectorsPerChunk * sectorSize;
                var totalWritten = 0L;
                
                StartTransferTimer(realDataSize);
                
                // Use SimpleBufferPool to reduce GC pressure
                var buffer = SimpleBufferPool.Rent(bytesPerChunk);
                try
                {
                    // Write data ranges one by one
                    foreach (var range in dataRanges)
                    {
                        if (ct.IsCancellationRequested) return false;
                        
                        var rangeOffset = range.Item1;
                        var rangeSize = range.Item2;
                        var rangeStartSector = startSector + (rangeOffset / sectorSize);
                        
                        // Seek to the range
                        sparse.Seek(rangeOffset, SeekOrigin.Begin);
                        var rangeWritten = 0L;
                        
                        while (rangeWritten < rangeSize)
                        {
                            if (ct.IsCancellationRequested) return false;
                            
                            var bytesToRead = (int)Math.Min(bytesPerChunk, rangeSize - rangeWritten);
                            var bytesRead = sparse.Read(buffer, 0, bytesToRead);
                            if (bytesRead == 0) break;
                            
                            // Pad to sector boundary
                            var paddedSize = ((bytesRead + sectorSize - 1) / sectorSize) * sectorSize;
                            if (paddedSize > bytesRead)
                                Array.Clear(buffer, bytesRead, paddedSize - bytesRead);
                            
                            var sectorsToWrite = paddedSize / sectorSize;
                            var currentSector = rangeStartSector + (rangeWritten / sectorSize);
                            
                            if (!await WriteSectorsAsync(lun, currentSector, buffer, paddedSize, label, useOppoMode, ct))
                            {
                                _log(string.Format("[Firehose] Write failed @ sector {0}", currentSector));
                                return false;
                            }
                            
                            rangeWritten += bytesRead;
                            totalWritten += bytesRead;
                            
                            if (_progress != null)
                                _progress(totalWritten, realDataSize);
                        }
                    }
                    
                    StopTransferTimer("Write", totalWritten);
                    _logDetail(string.Format("[Firehose] {0} complete: {1:N0} bytes (skipped {2:N0} MB blank)", 
                        label, totalWritten, (totalExpandedSize - realDataSize) / 1024.0 / 1024.0));
                    return true;
                }
                finally
                {
                    SimpleBufferPool.Return(buffer);
                }
            }
        }

        /// <summary>
        /// Write sector data (High-speed optimized version)
        /// </summary>
        private async Task<bool> WriteSectorsAsync(int lun, long startSector, byte[] data, int length, string label, bool useOppoMode, CancellationToken ct)
        {
            int numSectors = length / _sectorSize;
            
            // Use the actual partition name instead of hardcoded GPT values
            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data>" +
                "<program SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                "physical_partition_number=\"{2}\" start_sector=\"{3}\" label=\"{4}\" />" +
                "</data>",
                _sectorSize, numSectors, lun, startSector, label);

            // Send command (using asynchronous write)
            _port.DiscardInBuffer(); // Only clear the input buffer, not the output
            await _port.WriteAsync(Encoding.UTF8.GetBytes(xml), 0, xml.Length, ct);

            if (!await WaitForRawDataModeAsync(ct))
            {
                _log("[Firehose] Program command not acknowledged");
                return false;
            }

            // Use async data write
            if (!await _port.WriteAsync(data, 0, length, ct))
            {
                _log("[Firehose] Data write failed");
                return false;
            }

            return await WaitForAckAsync(ct, 10);
        }

        /// <summary>
        /// Flash partition from file
        /// </summary>
        public async Task<bool> FlashPartitionFromFileAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct, bool useVipMode = false)
        {
            if (!File.Exists(filePath))
            {
                _log("Firehose: File does not exist - " + filePath);
                return false;
            }

            // Check if it's a Sparse image
            bool isSparse = SparseStream.IsSparseFile(filePath);
            
            // Sparse image uses smart write, skipping DONT_CARE
            if (isSparse)
            {
                return await FlashSparsePartitionSmartAsync(partitionName, filePath, lun, startSector, progress, ct, useVipMode);
            }
            
            // Regular write for Raw image
            using (Stream sourceStream = File.OpenRead(filePath))
            {
                long fileSize = sourceStream.Length;
                int numSectors = (int)Math.Ceiling((double)fileSize / _sectorSize);

                _log(string.Format("Firehose: Flashing {0} -> {1} ({2}){3}", 
                    Path.GetFileName(filePath), partitionName, FormatFileSize(fileSize),
                    useVipMode ? " [VIP Mode]" : ""));

                // VIP mode uses masquerade strategy
                if (useVipMode)
                {
                    return await FlashPartitionVipModeAsync(partitionName, sourceStream, lun, startSector, numSectors, fileSize, progress, ct);
                }

                // Standard mode (supports OnePlus Token authentication)
                string xml;
                if (IsOnePlusAuthenticated)
                {
                    // OnePlus devices need an authentication token - adding label and read_back_verify to comply with official protocol
                    xml = string.Format(
                        "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                        "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                        "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                        "read_back_verify=\"true\" token=\"{5}\" pk=\"{6}\"/></data>",
                        _sectorSize, numSectors, lun, startSector, partitionName,
                        OnePlusProgramToken, OnePlusProgramPk);
                    _log("[OnePlus] Writing using authentication token");
                }
                else
                {
                    // Standard mode - adding label attribute to comply with official protocol
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
                    _log("Firehose: Program command rejected");
                    return false;
                }

                return await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
            }
        }

        /// <summary>
        /// Flash partition using official NUM_DISK_SECTORS-N negative sector format
        /// Used for partitions like BackupGPT that need to be written at the end of the disk
        /// </summary>
        public async Task<bool> FlashPartitionWithNegativeSectorAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                _log("Firehose: File does not exist - " + filePath);
                return false;
            }

            // Negative sector does not support Sparse image
            if (SparseStream.IsSparseFile(filePath))
            {
                _log("Firehose: Negative sector format does not support Sparse image");
                return false;
            }
            
            using (Stream sourceStream = File.OpenRead(filePath))
            {
                long fileSize = sourceStream.Length;
                int numSectors = (int)Math.Ceiling((double)fileSize / _sectorSize);

                // Format negative sector: NUM_DISK_SECTORS-N. (Official format, note the trailing dot)
                string startSectorStr;
                if (startSector < 0)
                {
                    startSectorStr = string.Format("NUM_DISK_SECTORS{0}.", startSector);
                }
                else
                {
                    startSectorStr = startSector.ToString();
                }

                _log(string.Format("Firehose: Flashing {0} -> {1} ({2}) @ {3}", 
                    Path.GetFileName(filePath), partitionName, FormatFileSize(fileSize), startSectorStr));

                // Construct program XML using official negative sector format
                string xml = string.Format(
                    "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" filename=\"{4}\" label=\"{4}\" " +
                    "read_back_verify=\"true\"/></data>",
                    _sectorSize, numSectors, lun, startSectorStr, partitionName);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (!await WaitForRawDataModeAsync(ct))
                {
                    _log("Firehose: Program command rejected (Negative sector format)");
                    return false;
                }

                return await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
            }
        }

        /// <summary>
        /// Smart flash Sparse image (only write chunks with data)
        /// </summary>
        private async Task<bool> FlashSparsePartitionSmartAsync(string partitionName, string filePath, int lun, long startSector, IProgress<double> progress, CancellationToken ct, bool useVipMode)
        {
            using (var sparse = SparseStream.Open(filePath, _log))
            {
                var totalExpandedSize = sparse.Length;
                var realDataSize = sparse.GetRealDataSize();
                var dataRanges = sparse.GetDataRanges();
                
                // Main log shows flashing info
                _log(string.Format("Firehose: Flashing {0} -> {1} ({2}) [Sparse]{3}", 
                    Path.GetFileName(filePath), partitionName, FormatFileSize(realDataSize), useVipMode ? " [VIP]" : ""));
                _logDetail(string.Format("[Sparse] Expanded: {0:F2} MB, Real data: {1:F2} MB, Saved: {2:P1}", 
                    totalExpandedSize / 1024.0 / 1024.0, 
                    realDataSize / 1024.0 / 1024.0,
                    realDataSize > 0 ? (1.0 - (double)realDataSize / totalExpandedSize) : 1.0));
                
                if (dataRanges.Count == 0)
                {
                    // Empty Sparse image (e.g., userdata): use erase command to clear partition
                    _logDetail(string.Format("[Sparse] Image has no data, erasing partition {0}...", partitionName));
                    long numSectors = totalExpandedSize / _sectorSize;
                    bool eraseOk = await EraseSectorsAsync(lun, startSector, numSectors, ct);
                    if (progress != null) progress.Report(100.0);
                    if (eraseOk)
                        _logDetail(string.Format("[Sparse] Partition {0} erase complete ({1:F2} MB)", partitionName, totalExpandedSize / 1024.0 / 1024.0));
                    else
                        _log(string.Format("[Sparse] Partition {0} erase failed", partitionName));
                    return eraseOk;
                }
                
                var totalWritten = 0L;
                var rangeIndex = 0;
                
                // Write data ranges one by one
                foreach (var range in dataRanges)
                {
                    if (ct.IsCancellationRequested) return false;
                    rangeIndex++;
                    
                    var rangeOffset = range.Item1;
                    var rangeSize = range.Item2;
                    var rangeStartSector = startSector + (rangeOffset / _sectorSize);
                    var numSectors = (int)Math.Ceiling((double)rangeSize / _sectorSize);
                    
                    // Seek to the range
                    sparse.Seek(rangeOffset, SeekOrigin.Begin);
                    
                    // Construct program command
                    string xml;
                    if (useVipMode)
                    {
                        // VIP mode masquerade
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
                        _logDetail(string.Format("[Sparse] Segment {0}/{1} Program command rejected", rangeIndex, dataRanges.Count));
                        return false;
                    }
                    
                    // Send data for this range (using optimized block size for maximum performance)
                    var sent = 0L;
                    // Use 4MB block size to improve USB 3.0 transmission efficiency
                    const int OPTIMAL_CHUNK = 4 * 1024 * 1024;
                    var chunkSize = Math.Min(OPTIMAL_CHUNK, _maxPayloadSize);
                    var buffer = new byte[chunkSize];
                    DateTime lastProgressTime = DateTime.MinValue;
                    
                    while (sent < rangeSize)
                    {
                        if (ct.IsCancellationRequested) return false;
                        
                        var toRead = (int)Math.Min(chunkSize, rangeSize - sent);
                        var read = sparse.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        
                        // Pad to sector boundary
                        var paddedSize = ((read + _sectorSize - 1) / _sectorSize) * _sectorSize;
                        if (paddedSize > read)
                            Array.Clear(buffer, read, paddedSize - read);
                        
                        // Use synchronous write to improve efficiency
                        _port.Write(buffer, 0, paddedSize);
                        
                        sent += read;
                        totalWritten += read;
                        
                        // Throttle progress reports
                        var now = DateTime.Now;
                        if (progress != null && realDataSize > 0 && (now - lastProgressTime).TotalMilliseconds > 200)
                        {
                            progress.Report(totalWritten * 100.0 / realDataSize);
                            lastProgressTime = now;
                        }
                    }
                    
                    if (!await WaitForAckAsync(ct, 30))
                    {
                        _logDetail(string.Format("[Sparse] Segment {0}/{1} write unacknowledged", rangeIndex, dataRanges.Count));
                        return false;
                    }
                }
                
                _logDetail(string.Format("[Sparse] {0} Write complete: {1:N0} bytes (skipped {2:N0} MB blank)", 
                    partitionName, totalWritten, (totalExpandedSize - realDataSize) / 1024.0 / 1024.0));
                return true;
            }
        }

        /// <summary>
        /// VIP mode flash partition (using masquerade strategy)
        /// </summary>
        private async Task<bool> FlashPartitionVipModeAsync(string partitionName, Stream sourceStream, int lun, long startSector, int numSectors, long fileSize, IProgress<double> progress, CancellationToken ct)
        {
            // Get masquerade strategy
            var strategies = GetDynamicSpoofStrategies(lun, startSector, partitionName, false);
            
            foreach (var strategy in strategies)
            {
                if (ct.IsCancellationRequested) break;

                string spoofLabel = string.IsNullOrEmpty(strategy.Label) ? partitionName : strategy.Label;
                string spoofFilename = string.IsNullOrEmpty(strategy.Filename) ? partitionName : strategy.Filename;

                _logDetail(string.Format("[VIP Write] Attempting masquerade: {0}/{1}", spoofLabel, spoofFilename));
                PurgeBuffer();

                // VIP mode program command - adding read_back_verify to comply with official protocol
                string xml = string.Format(
                    "<?xml version=\"1.0\"?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" filename=\"{4}\" label=\"{5}\" " +
                    "partofsingleimage=\"true\" read_back_verify=\"true\" sparse=\"false\"/></data>",
                    _sectorSize, numSectors, lun, startSector, spoofFilename, spoofLabel);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (await WaitForRawDataModeAsync(ct))
                {
                    _logDetail(string.Format("[VIP Write] Masquerade {0} success, starting data transfer...", spoofLabel));
                    
                    // Reset stream position before each attempt
                    sourceStream.Position = 0;
                    bool success = await SendStreamDataAsync(sourceStream, fileSize, progress, ct);
                    if (success)
                    {
                        _logDetail(string.Format("[VIP Write] {0} Write success", partitionName));
                        return true;
                    }
                }

                await Task.Delay(100, ct);
            }

            _log(string.Format("[VIP Write] {0} All masquerade strategies failed", partitionName));
            return false;
        }

        /// <summary>
        /// Send stream data (Extremely optimized version - uses double buffering and larger chunk size)
        /// Optimization points:
        /// 1. Increase chunk size to 4MB (Optimal for USB 3.0)
        /// 2. Double buffering parallel read/write
        /// 3. Reduce progress update frequency
        /// 4. Use Buffer.BlockCopy instead of Array.Clear
        /// </summary>
        private async Task<bool> SendStreamDataAsync(Stream stream, long streamSize, IProgress<double> progress, CancellationToken ct)
        {
            long sent = 0;
            
            // Use double buffering for read/write parallelism
            // Chunk size optimization: 4MB is the optimal chunk size for USB 3.0 environments
            // 2MB is better for USB 2.0 environments, but 4MB also works normally
            const int OPTIMAL_CHUNK_SIZE = 4 * 1024 * 1024; // 4MB chunk (Increased from 2MB)
            int chunkSize = Math.Min(OPTIMAL_CHUNK_SIZE, _maxPayloadSize);
            
            byte[] buffer1 = new byte[chunkSize];
            byte[] buffer2 = new byte[chunkSize];
            byte[] currentBuffer = buffer1;
            byte[] nextBuffer = buffer2;
            
            double lastPercent = -1;
            DateTime lastProgressTime = DateTime.MinValue;
            const int PROGRESS_INTERVAL_MS = 200; // Reduce progress update frequency to 200ms
            
            // Pre-read the first chunk
            int currentRead = stream.Read(currentBuffer, 0, (int)Math.Min(chunkSize, streamSize));
            if (currentRead <= 0) return await WaitForAckAsync(ct, 60);

            while (sent < streamSize)
            {
                if (ct.IsCancellationRequested) return false;

                // Calculate remaining data
                long remaining = streamSize - sent - currentRead;
                
                // Start asynchronous read of the next chunk (if there is still data)
                Task<int> readTask = null;
                if (remaining > 0)
                {
                    int nextToRead = (int)Math.Min(chunkSize, remaining);
                    readTask = stream.ReadAsync(nextBuffer, 0, nextToRead, ct);
                }

                // Pad to sector boundary
                int toWrite = currentRead;
                if (currentRead % _sectorSize != 0)
                {
                    toWrite = ((currentRead / _sectorSize) + 1) * _sectorSize;
                    Array.Clear(currentBuffer, currentRead, toWrite - currentRead);
                }

                // Send current chunk (use synchronous write for efficiency)
                try
                {
                    _port.Write(currentBuffer, 0, toWrite);
                }
                catch (Exception ex)
                {
                    _log(string.Format("Firehose: Data write failed - {0}", ex.Message));
                    return false;
                }

                sent += currentRead;

                // Throttled progress reporting: update every 200ms or 1%
                var now = DateTime.Now;
                double currentPercent = (100.0 * sent / streamSize);
                if (currentPercent > lastPercent + 1.0 || (now - lastProgressTime).TotalMilliseconds > PROGRESS_INTERVAL_MS)
                {
                    if (_progress != null) _progress(sent, streamSize);
                    if (progress != null) progress.Report(currentPercent);
                    
                    lastPercent = currentPercent;
                    lastProgressTime = now;
                }

                // Wait for next chunk read to complete and swap buffers
                if (readTask != null)
                {
                    currentRead = await readTask;
                    if (currentRead <= 0) break;
                    
                    // Swap buffers
                    var temp = currentBuffer;
                    currentBuffer = nextBuffer;
                    nextBuffer = temp;
                }
                else
                {
                    break;
                }
            }

            // Ensure the final progress report
            if (_progress != null) _progress(streamSize, streamSize);
            if (progress != null) progress.Report(100.0);

            // Wait for final ACK (Reduce retries, speed up response)
            return await WaitForAckAsync(ct, 60);
        }

        #endregion

        #region Erase Partition

        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(PartitionInfo partition, CancellationToken ct = default(CancellationToken), bool useVipMode = false)
        {
            _log(string.Format("[Firehose] Erasing partition: {0}{1}", partition.Name, useVipMode ? " [VIP Mode]" : ""));

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
                _log(string.Format("[Firehose] Partition {0} erase complete", partition.Name));
                return true;
            }

            _log("[Firehose] Erase failed");
            return false;
        }

        /// <summary>
        /// VIP mode erase partition
        /// </summary>
        private async Task<bool> ErasePartitionVipModeAsync(PartitionInfo partition, CancellationToken ct)
        {
            var strategies = GetDynamicSpoofStrategies(partition.Lun, partition.StartSector, partition.Name, false);

            foreach (var strategy in strategies)
            {
                if (ct.IsCancellationRequested) break;

                string spoofLabel = string.IsNullOrEmpty(strategy.Label) ? partition.Name : strategy.Label;
                string spoofFilename = string.IsNullOrEmpty(strategy.Filename) ? partition.Name : strategy.Filename;

                _log(string.Format("[VIP Erase] Attempting masquerade: {0}/{1}", spoofLabel, spoofFilename));
                PurgeBuffer();

                var xml = string.Format(
                    "<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "num_partition_sectors=\"{1}\" physical_partition_number=\"{2}\" " +
                    "start_sector=\"{3}\" label=\"{4}\" filename=\"{5}\" /></data>",
                    _sectorSize, partition.NumSectors, partition.Lun, partition.StartSector, spoofLabel, spoofFilename);

                _port.Write(Encoding.UTF8.GetBytes(xml));

                if (await WaitForAckAsync(ct))
                {
                    _log(string.Format("[VIP Erase] {0} Erase success", partition.Name));
                    return true;
                }

                await Task.Delay(100, ct);
            }

            _log(string.Format("[VIP Erase] {0} All masquerade strategies failed", partition.Name));
            return false;
        }

        /// <summary>
        /// Erase partition (Parameter version)
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, int lun, long startSector, long numSectors, CancellationToken ct, bool useVipMode = false)
        {
            _log(string.Format("Firehose: Erasing partition {0}{1}", partitionName, useVipMode ? " [VIP Mode]" : ""));

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
            _log(success ? "Firehose: Erase success" : "Firehose: Erase failed");

            return success;
        }

        /// <summary>
        /// Erase specified sector range (Simplified version)
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

        #region Device Control

        /// <summary>
        /// Reset device
        /// </summary>
        public async Task<bool> ResetAsync(string mode = "reset", CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] Reset device (Mode: {0})", mode));

            var xml = string.Format("<?xml version=\"1.0\" ?><data><power value=\"{0}\" /></data>", mode);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// Power off
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[Firehose] Powering off...");

            string xml = "<?xml version=\"1.0\"?><data><power value=\"off\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// Enter EDL mode
        /// </summary>
        public async Task<bool> RebootToEdlAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\"?><data><power value=\"edl\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// Set active slot (A/B) - Complete implementation
        /// Prioritize setactiveslot command, fallback to patch method if it fails
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default(CancellationToken))
        {
            slot = slot?.ToLower() ?? "a";
            if (slot != "a" && slot != "b")
            {
                _log("[Firehose] Error: Slot must be 'a' or 'b'");
                return false;
            }

            _log(string.Format("[Firehose] Set active slot: {0}", slot));

            // Method 1: Try setactiveslot command (supported by some devices)
            var xml = string.Format("<?xml version=\"1.0\" ?><data><setactiveslot slot=\"{0}\" /></data>", slot);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct, 3))
            {
                _log("[Firehose] setactiveslot command success");
                return true;
            }

            // Method 2: Fallback to patch method to modify GPT attributes
            _log("[Firehose] setactiveslot not supported, using patch method...");
            return await SetActiveSlotViaPatchAsync(slot, ct);
        }

        /// <summary>
        /// Set active slot by modifying GPT partition attributes via patch command
        /// </summary>
        private async Task<bool> SetActiveSlotViaPatchAsync(string targetSlot, CancellationToken ct)
        {
            if (_cachedPartitions == null || _cachedPartitions.Count == 0)
            {
                _log("[Firehose] Error: No cached partition info, please read partition table first");
                return false;
            }

            // Core A/B partitions that need modification (per boot order)
            string[] coreAbPartitions = {
                "boot", "dtbo", "vbmeta", "vendor_boot", "init_boot"
            };

            // Optional A/B partitions
            string[] optionalAbPartitions = {
                "system", "vendor", "product", "odm", "system_ext",
                "vendor_dlkm", "odm_dlkm", "system_dlkm"
            };

            string activeSuffix = "_" + targetSlot;
            string inactiveSuffix = targetSlot == "a" ? "_b" : "_a";
            
            int patchCount = 0;
            int failCount = 0;

            // 1. Handle core partitions (must succeed)
            foreach (var baseName in coreAbPartitions)
            {
                var result = await PatchSlotPairAsync(baseName, activeSuffix, inactiveSuffix, ct);
                if (result > 0) patchCount += result;
                else if (result < 0) failCount++;
            }

            // 2. Handle optional partitions (fails do not affect overall outcome)
            foreach (var baseName in optionalAbPartitions)
            {
                var result = await PatchSlotPairAsync(baseName, activeSuffix, inactiveSuffix, ct, true);
                if (result > 0) patchCount += result;
            }

            if (patchCount == 0)
            {
                _log("[Firehose] No A/B partitions found");
                return false;
            }

            _log(string.Format("[Firehose] Modified {0} partition attributes", patchCount));

            // 3. Fix GPT to save changes
            _log("[Firehose] Saving GPT changes...");
            bool fixResult = await FixGptAsync(-1, false, ct);
            
            if (fixResult)
                _log(string.Format("[Firehose] Active slot switched to: {0}", targetSlot));
            else
                _log("[Firehose] Warning: GPT fix failed, changes may not be saved");

            return fixResult && failCount == 0;
        }

        /// <summary>
        /// Modify attributes for a pair of A/B partitions
        /// </summary>
        /// <returns>Number of partitions modified, -1 for failure</returns>
        private async Task<int> PatchSlotPairAsync(string baseName, string activeSuffix, string inactiveSuffix, 
            CancellationToken ct, bool optional = false)
        {
            int count = 0;

            // Activate target slot
            var activePart = _cachedPartitions.Find(p => 
                p.Name.Equals(baseName + activeSuffix, StringComparison.OrdinalIgnoreCase));
            
            if (activePart != null)
            {
                ulong newAttr = SetSlotFlags(activePart.Attributes, active: true, priority: 3, successful: false, unbootable: false);
                
                if (await PatchPartitionAttributesAsync(activePart, newAttr, ct))
                {
                    _logDetail(string.Format("[Firehose] {0}: Activated (attr=0x{1:X16})", activePart.Name, newAttr));
                    count++;
                }
                else if (!optional)
                {
                    _log(string.Format("[Firehose] Error: Unable to modify {0} attributes", activePart.Name));
                    return -1;
                }
            }

            // Deactivate the other slot
            var inactivePart = _cachedPartitions.Find(p => 
                p.Name.Equals(baseName + inactiveSuffix, StringComparison.OrdinalIgnoreCase));
            
            if (inactivePart != null)
            {
                ulong newAttr = SetSlotFlags(inactivePart.Attributes, active: false, priority: 1, successful: null, unbootable: null);
                
                if (await PatchPartitionAttributesAsync(inactivePart, newAttr, ct))
                {
                    _logDetail(string.Format("[Firehose] {0}: Deactivated (attr=0x{1:X16})", inactivePart.Name, newAttr));
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Use patch command to modify partition attributes
        /// </summary>
        private async Task<bool> PatchPartitionAttributesAsync(PartitionInfo partition, ulong newAttributes, CancellationToken ct)
        {
            // GPT Entry structure (128 bytes):
            // Offset 0-15:  Type GUID (16 bytes)
            // Offset 16-31: Unique GUID (16 bytes)
            // Offset 32-39: Start LBA (8 bytes)
            // Offset 40-47: End LBA (8 bytes)
            // Offset 48-55: Attributes (8 bytes) <-- We modify here
            // Offset 56-127: Name (72 bytes)
            
            const int GPT_ENTRY_SIZE = 128;
            const int ATTR_OFFSET_IN_ENTRY = 48;
            
            // Convert attributes to little-endian hex string
            byte[] attrBytes = BitConverter.GetBytes(newAttributes);
            string attrHex = BitConverter.ToString(attrBytes).Replace("-", "");

            // Method 1: Patch using partition name (supported by some devices)
            string xml1 = string.Format(
                "<?xml version=\"1.0\" ?><data>" +
                "<patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" " +
                "filename=\"{2}\" physical_partition_number=\"{3}\" " +
                "size_in_bytes=\"8\" start_sector=\"0\" value=\"{4}\" what=\"attributes\" />" +
                "</data>",
                _sectorSize, ATTR_OFFSET_IN_ENTRY, partition.Name, partition.Lun, attrHex);

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml1));

            if (await WaitForAckAsync(ct, 3))
                return true;

            // Method 2: Use precise GPT entry position (requires EntryIndex)
            if (partition.EntryIndex >= 0)
            {
                // Calculate precise position of GPT entry on disk
                // GPT entries start at LBA 2 (usually), each entry 128 bytes
                long gptEntriesStartByte = partition.GptEntriesStartSector * _sectorSize;
                long entryByteOffset = gptEntriesStartByte + (partition.EntryIndex * GPT_ENTRY_SIZE);
                long attrByteOffset = entryByteOffset + ATTR_OFFSET_IN_ENTRY;
                
                // Calculate sector and byte offset
                long startSector = attrByteOffset / _sectorSize;
                int byteOffset = (int)(attrByteOffset % _sectorSize);

                _logDetail(string.Format("[Firehose] Patch {0}: Entry#{1}, Sector={2}, Offset={3}", 
                    partition.Name, partition.EntryIndex, startSector, byteOffset));

                string xml2 = string.Format(
                    "<?xml version=\"1.0\" ?><data>" +
                    "<patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" " +
                    "filename=\"DISK\" physical_partition_number=\"{2}\" " +
                    "size_in_bytes=\"8\" start_sector=\"{3}\" value=\"{4}\" />" +
                    "</data>",
                    _sectorSize, byteOffset, partition.Lun, startSector, attrHex);

                PurgeBuffer();
                _port.Write(Encoding.UTF8.GetBytes(xml2));

                if (await WaitForAckAsync(ct, 3))
                    return true;

                _logDetail(string.Format("[Firehose] Method 2 failed: {0}", partition.Name));
            }
            else
            {
                _logDetail(string.Format("[Firehose] {0} missing EntryIndex, skipping precise patch", partition.Name));
            }

            // Method 3: Try setactivepartition command (supported by some devices)
            string xml3 = string.Format(
                "<?xml version=\"1.0\" ?><data>" +
                "<setactivepartition name=\"{0}\" slot=\"{1}\" /></data>",
                partition.Name.TrimEnd('_', 'a', 'b'),
                partition.Name.EndsWith("_a") ? "a" : "b");

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml3));

            if (await WaitForAckAsync(ct, 2))
                return true;

            _logDetail(string.Format("[Firehose] All patch methods failed: {0}", partition.Name));
            return false;
        }

        #region A/B Slot Attribute Bit Operations

        /// <summary>
        /// Set slot flags
        /// </summary>
        /// <param name="attr">Original attributes</param>
        /// <param name="active">Whether to activate (null = no change)</param>
        /// <param name="priority">Priority 0-3 (null = no change)</param>
        /// <param name="successful">Successful boot flag (null = no change)</param>
        /// <param name="unbootable">Unbootable flag (null = no change)</param>
        private ulong SetSlotFlags(ulong attr, bool? active = null, int? priority = null, 
            bool? successful = null, bool? unbootable = null)
        {
            // A/B attribute bit layout (in Attributes field bits 48-55):
            // Bit 48-49: Priority (0-3)
            // Bit 50: Active
            // Bit 51: Successful
            // Bit 52: Unbootable
            
            const ulong PRIORITY_MASK = 3UL << 48;
            const ulong ACTIVE_BIT = 1UL << 50;
            const ulong SUCCESSFUL_BIT = 1UL << 51;
            const ulong UNBOOTABLE_BIT = 1UL << 52;

            if (priority.HasValue)
            {
                attr &= ~PRIORITY_MASK;
                attr |= ((ulong)(priority.Value & 3) << 48);
            }

            if (active.HasValue)
            {
                if (active.Value)
                    attr |= ACTIVE_BIT;
                else
                    attr &= ~ACTIVE_BIT;
            }

            if (successful.HasValue)
            {
                if (successful.Value)
                    attr |= SUCCESSFUL_BIT;
                else
                    attr &= ~SUCCESSFUL_BIT;
            }

            if (unbootable.HasValue)
            {
                if (unbootable.Value)
                    attr |= UNBOOTABLE_BIT;
                else
                    attr &= ~UNBOOTABLE_BIT;
            }

            return attr;
        }

        /// <summary>
        /// Check if slot is active
        /// </summary>
        public bool IsSlotActive(ulong attributes)
        {
            return (attributes & (1UL << 50)) != 0;
        }

        /// <summary>
        /// Get slot priority
        /// </summary>
        public int GetSlotPriority(ulong attributes)
        {
            return (int)((attributes >> 48) & 3);
        }

        #endregion

        /// <summary>
        /// Fix GPT
        /// </summary>
        public async Task<bool> FixGptAsync(int lun = -1, bool growLastPartition = true, CancellationToken ct = default(CancellationToken))
        {
            string lunValue = (lun == -1) ? "all" : lun.ToString();
            string growValue = growLastPartition ? "1" : "0";

            _log(string.Format("[Firehose] Fix GPT (LUN={0})...", lunValue));
            var xml = string.Format("<?xml version=\"1.0\" ?><data><fixgpt lun=\"{0}\" grow_last_partition=\"{1}\" /></data>", lunValue, growValue);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct, 10))
            {
                _log("[Firehose] GPT fix success");
                return true;
            }

            _log("[Firehose] GPT fix failed");
            return false;
        }

        /// <summary>
        /// Set boot LUN
        /// </summary>
        public async Task<bool> SetBootLunAsync(int lun, CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[Firehose] Set boot LUN: {0}", lun));
            var xml = string.Format("<?xml version=\"1.0\" ?><data><setbootablestoragedrive value=\"{0}\" /></data>", lun);
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        #region UFS Provision (Storage Config)

        /// <summary>
        /// Provision feature toggle (Disabled by default, as this is a dangerous operation)
        /// Must be explicitly set to true to use Provision functionality
        /// </summary>
        public bool EnableProvision { get; set; } = false;

        /// <summary>
        /// Send UFS global configuration (Provision Step 1)
        /// Warning: This is a dangerous operation, incorrect configuration may brick the device!
        /// </summary>
        public async Task<bool> SendUfsGlobalConfigAsync(
            byte bNumberLU, byte bBootEnable, byte bDescrAccessEn, byte bInitPowerMode,
            byte bHighPriorityLUN, byte bSecureRemovalType, byte bInitActiveICCLevel,
            short wPeriodicRTCUpdate, byte bConfigDescrLock,
            CancellationToken ct = default(CancellationToken))
        {
            if (!EnableProvision)
            {
                _log("[Provision] Feature disabled, please set EnableProvision = true first");
                return false;
            }

            _log(string.Format("[Provision] Sending UFS global config (LUN count={0}, Boot={1})...", bNumberLU, bBootEnable));
            
            var xml = string.Format(
                "<?xml version=\"1.0\" ?><data><ufs bNumberLU=\"{0}\" bBootEnable=\"{1}\" " +
                "bDescrAccessEn=\"{2}\" bInitPowerMode=\"{3}\" bHighPriorityLUN=\"{4}\" " +
                "bSecureRemovalType=\"{5}\" bInitActiveICCLevel=\"{6}\" wPeriodicRTCUpdate=\"{7}\" " +
                "bConfigDescrLock=\"{8}\" /></data>",
                bNumberLU, bBootEnable, bDescrAccessEn, bInitPowerMode,
                bHighPriorityLUN, bSecureRemovalType, bInitActiveICCLevel,
                wPeriodicRTCUpdate, bConfigDescrLock);
            
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            
            bool ack = await WaitForAckAsync(ct, 30); // 30s timeout
            if (ack)
                _logDetail("[Provision] Global config sent");
            else
                _log("[Provision] Global config send failed");
            
            return ack;
        }

        /// <summary>
        /// Send UFS LUN configuration (Provision Step 2, call once for each LUN)
        /// Warning: This is a dangerous operation, incorrect configuration may brick the device!
        /// </summary>
        public async Task<bool> SendUfsLunConfigAsync(
            byte luNum, byte bLUEnable, byte bBootLunID, long sizeInKB,
            byte bDataReliability, byte bLUWriteProtect, byte bMemoryType,
            byte bLogicalBlockSize, byte bProvisioningType, short wContextCapabilities,
            CancellationToken ct = default(CancellationToken))
        {
            if (!EnableProvision)
            {
                _log("[Provision] Feature disabled");
                return false;
            }

            string sizeStr = sizeInKB >= 1024 * 1024 ? 
                string.Format("{0:F1}GB", sizeInKB / (1024.0 * 1024)) : 
                string.Format("{0}MB", sizeInKB / 1024);
            
            _logDetail(string.Format("[Provision] Config LUN{0}: {1}, Enable={2}, Boot={3}",
                luNum, sizeStr, bLUEnable, bBootLunID));
            
            var xml = string.Format(
                "<?xml version=\"1.0\" ?><data><ufs LUNum=\"{0}\" bLUEnable=\"{1}\" " +
                "bBootLunID=\"{2}\" size_in_kb=\"{3}\" bDataReliability=\"{4}\" " +
                "bLUWriteProtect=\"{5}\" bMemoryType=\"{6}\" bLogicalBlockSize=\"{7}\" " +
                "bProvisioningType=\"{8}\" wContextCapabilities=\"{9}\" /></data>",
                luNum, bLUEnable, bBootLunID, sizeInKB,
                bDataReliability, bLUWriteProtect, bMemoryType,
                bLogicalBlockSize, bProvisioningType, wContextCapabilities);
            
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            
            return await WaitForAckAsync(ct, 30);
        }

        /// <summary>
        /// Commit UFS Provision configuration (Final step)
        /// Warning: This operation may be OTP (One Time Programming), once executed it cannot be undone!
        /// </summary>
        public async Task<bool> CommitUfsProvisionAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!EnableProvision)
            {
                _log("[Provision] Feature disabled, unable to commit config");
                return false;
            }

            _log("[Provision] Committing UFS config (This operation may be irreversible!)...");
            
            var xml = "<?xml version=\"1.0\" ?><data><ufs commit=\"true\" /></data>";
            
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            
            bool ack = await WaitForAckAsync(ct, 60); // 60s timeout, Provision may be slow
            if (ack)
                _log("[Provision] UFS configuration committed successfully");
            else
                _log("[Provision] UFS configuration commit failed");
            
            return ack;
        }

        /// <summary>
        /// Read current UFS storage info (if supported by device)
        /// </summary>
        public async Task<bool> GetStorageInfoAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[Provision] Reading storage info...");
            
            // Try getstorageinfo command (not all devices support it)
            var xml = "<?xml version=\"1.0\" ?><data><getstorageinfo /></data>";
            
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            
            // Wait for response
            bool result = await WaitForAckAsync(ct, 10);
            if (!result)
                _logDetail("[Provision] getstorageinfo command may not be supported");
            
            return result;
        }

        #endregion

        /// <summary>
        /// Apply single patch (Supports official NUM_DISK_SECTORS-N negative sector format)
        /// </summary>
        public async Task<bool> ApplyPatchAsync(int lun, long startSector, int byteOffset, int sizeInBytes, string value, CancellationToken ct = default(CancellationToken))
        {
            // Skip empty patch
            if (string.IsNullOrEmpty(value) || sizeInBytes == 0)
                return true;

            // Format start_sector: negative numbers use official format NUM_DISK_SECTORS-N.
            string startSectorStr;
            if (startSector < 0)
            {
                startSectorStr = string.Format("NUM_DISK_SECTORS{0}.", startSector);
                _logDetail(string.Format("[Patch] LUN{0} Sector {1} Offset{2} Size{3}", lun, startSectorStr, byteOffset, sizeInBytes));
            }
            else
            {
                startSectorStr = startSector.ToString();
                _logDetail(string.Format("[Patch] LUN{0} Sector{1} Offset{2} Size{3}", lun, startSector, byteOffset, sizeInBytes));
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
        /// Apply all patches from Patch XML file
        /// </summary>
        public async Task<int> ApplyPatchXmlAsync(string patchXmlPath, CancellationToken ct = default(CancellationToken))
        {
            if (!System.IO.File.Exists(patchXmlPath))
            {
                _log(string.Format("[Firehose] Patch file does not exist: {0}", patchXmlPath));
                return 0;
            }

            _logDetail(string.Format("[Firehose] Applying Patch: {0}", System.IO.Path.GetFileName(patchXmlPath)));

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
                    
                    // Handle negative sectors in NUM_DISK_SECTORS-N format (keep negative, let ApplyPatchAsync send using official format)
                    if (startSectorAttr.Contains("NUM_DISK_SECTORS"))
                    {
                        if (startSectorAttr.Contains("-"))
                        {
                            string offsetStr = startSectorAttr.Split('-')[1].TrimEnd('.');
                            long offset;
                            if (long.TryParse(offsetStr, out offset))
                                startSector = -offset; // Negative number, ApplyPatchAsync will use official format
                        }
                        else
                        {
                            startSector = -1;
                        }
                        // No longer attempting client-side conversion, use negative number directly for device calculation
                    }
                    else if (startSectorAttr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        long.TryParse(startSectorAttr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out startSector);
                    }
                    else
                    {
                        // Remove possible trailing dot (e.g., "5.")
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
                        _logDetail(string.Format("[Patch] Failed: LUN{0} Sector{1}", lun, startSector));
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Patch] Application exception: {0}", ex.Message));
            }

            _logDetail(string.Format("[Patch] {0} successfully applied {1} patches", System.IO.Path.GetFileName(patchXmlPath), successCount));
            return successCount;
        }

        /// <summary>
        /// Ping/NOP test connection
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default(CancellationToken))
        {
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" ?><data><nop /></data>"));
            return await WaitForAckAsync(ct, 3);
        }

        #endregion

        #region Partition Cache

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

        #region Communication Methods

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

                            // Extract device logs (detailed logs, not displayed on main interface)
                            if (content.Contains("<log "))
                            {
                                var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                                foreach (Match m in logMatches)
                                {
                                    if (m.Groups.Count > 1)
                                        _logDetail("[Device] " + m.Groups[1].Value);
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
                        // Use spin wait instead of Task.Delay to reduce context switching
                        if (emptyReads < 20)
                            Thread.SpinWait(500);  // Quick spin
                        else if (emptyReads < 100)
                            Thread.Yield();  // Yield time slice
                        else if (emptyReads < 500)
                            await Task.Yield();  // Async yield
                        else
                            await Task.Delay(1, ct);  // Short wait
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is normal (including TaskCanceledException), do not log
                return null;
            }
            catch (Exception ex)
            {
                _logDetail(string.Format("[Firehose] Response parsing exception: {0}", ex.Message));
            }
            return null;
        }

        private async Task<bool> WaitForAckAsync(CancellationToken ct, int maxRetries = 50)
        {
            int emptyCount = 0;
            int totalWaitMs = 0;
            const int MAX_WAIT_MS = 30000; // Max wait 30 seconds
            
            for (int i = 0; i < maxRetries && totalWaitMs < MAX_WAIT_MS; i++)
            {
                if (ct.IsCancellationRequested) return false;

                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    emptyCount = 0; // Reset empty response count
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
                else
                {
                    // Use spin wait + progressive backoff for empty response
                    emptyCount++;
                    int waitMs;
                    if (emptyCount < 50)
                    {
                        // Fast spin for first 50 times (approx 0.5ms each)
                        Thread.SpinWait(1000);
                        waitMs = 0;
                    }
                    else if (emptyCount < 200)
                    {
                        // Medium wait (1ms)
                        await Task.Yield(); // Yield time slice but don't actually wait
                        waitMs = 1;
                    }
                    else
                    {
                        // Longer wait (5ms)
                        await Task.Delay(5, ct);
                        waitMs = 5;
                    }
                    totalWaitMs += waitMs;
                }
            }

            _log("[Firehose] Wait for ACK timeout");
            return false;
        }

        /// <summary>
        /// Receive data response (High-speed pipeline version - Extremely optimized)
        /// Optimization points:
        /// 1. Use larger probe buffer (256KB) to reduce I/O calls
        /// 2. Batch read data chunks (max 8MB) to increase throughput
        /// 3. Reduce string parsing overhead, use byte-level scanning
        /// 4. Zero-copy design, write directly to target buffer
        /// </summary>
        private async Task<bool> ReceiveDataAfterAckAsync(byte[] buffer, CancellationToken ct)
        {
            try
            {
                int totalBytes = buffer.Length;
                int received = 0;
                bool headerFound = false;

                // Increase probe buffer (256KB) - Many devices send response header + data at once
                byte[] probeBuf = new byte[256 * 1024];
                int probeIdx = 0;
                
                // Patterns for fast byte matching
                byte[] rawmodePattern = Encoding.ASCII.GetBytes("rawmode=\"true\"");
                byte[] dataEndPattern = Encoding.ASCII.GetBytes("</data>");
                byte[] nakPattern = Encoding.ASCII.GetBytes("NAK");

                var sw = Stopwatch.StartNew();
                const int TIMEOUT_MS = 30000; // 30s timeout

                while (received < totalBytes && sw.ElapsedMilliseconds < TIMEOUT_MS)
                {
                    if (ct.IsCancellationRequested) return false;

                    if (!headerFound)
                    {
                        // 1. Find XML header - Batch read
                        int toRead = probeBuf.Length - probeIdx;
                        if (toRead <= 0) { probeIdx = 0; toRead = probeBuf.Length; }
                        
                        int read = await _port.ReadAsync(probeBuf, probeIdx, toRead, ct);
                        if (read <= 0)
                        {
                            // Retry after short wait
                            await Task.Delay(1, ct);
                            continue;
                        }
                        probeIdx += read;

                        // Fast byte-level scanning - avoid string conversion overhead
                        int ackIndex = IndexOfPattern(probeBuf, 0, probeIdx, rawmodePattern);
                        
                        if (ackIndex >= 0)
                        {
                            int xmlEndIndex = IndexOfPattern(probeBuf, ackIndex, probeIdx - ackIndex, dataEndPattern);
                            if (xmlEndIndex >= 0)
                            {
                                headerFound = true;
                                int dataStart = xmlEndIndex + dataEndPattern.Length;
                                
                                // Skip whitespace (newlines, etc.)
                                while (dataStart < probeIdx && (probeBuf[dataStart] == '\n' || probeBuf[dataStart] == '\r' || probeBuf[dataStart] == ' '))
                                    dataStart++;

                                // Zero-copy: Store remaining data in probe buffer directly into target buffer
                                int leftover = probeIdx - dataStart;
                                if (leftover > 0)
                                {
                                    int toCopy = Math.Min(leftover, totalBytes);
                                    Buffer.BlockCopy(probeBuf, dataStart, buffer, 0, toCopy);
                                    received = toCopy;
                                }
                            }
                        }
                        else if (IndexOfPattern(probeBuf, 0, probeIdx, nakPattern) >= 0)
                        {
                            // Device rejected
                            return false;
                        }
                    }
                    else
                    {
                        // 2. High-speed reading of raw data blocks - use larger blocks (8MB)
                        // USB 3.0 theoretical bandwidth is 5Gbps, actual throughput approx 400MB/s
                        // Using large blocks maximizes USB bandwidth utilization
                        int toRead = Math.Min(totalBytes - received, 8 * 1024 * 1024);
                        
                        int read = await _port.ReadAsync(buffer, received, toRead, ct);
                        if (read <= 0)
                        {
                            // Retry after short wait
                            await Task.Delay(1, ct);
                            continue;
                        }
                        received += read;
                    }
                }
                
                return received >= totalBytes;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logDetail("[Read] High-speed read exception: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Efficient byte pattern matching (Boyer-Moore simplified version)
        /// </summary>
        private static int IndexOfPattern(byte[] data, int start, int length, byte[] pattern)
        {
            if (pattern.Length == 0 || length < pattern.Length) return -1;
            
            int end = start + length - pattern.Length;
            for (int i = start; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        /// <summary>
        /// Wait for device to enter Raw data mode (Extremely optimized version)
        /// Optimization points:
        /// 1. Use byte-level scanning instead of string operations
        /// 2. Larger buffer (16KB) to reduce I/O calls
        /// 3. More aggressive spin strategy to reduce latency
        /// </summary>
        private async Task<bool> WaitForRawDataModeAsync(CancellationToken ct, int timeoutMs = 5000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use a larger buffer
                    var buffer = new byte[16384];
                    int bufferPos = 0;
                    var sw = Stopwatch.StartNew();
                    int spinCount = 0;
                    
                    // Predefine byte patterns (avoid runtime allocation)
                    byte[] rawmodePattern = { (byte)'r', (byte)'a', (byte)'w', (byte)'m', (byte)'o', (byte)'d', (byte)'e', (byte)'=', (byte)'"', (byte)'t', (byte)'r', (byte)'u', (byte)'e', (byte)'"' };
                    byte[] dataEndPattern = { (byte)'<', (byte)'/', (byte)'d', (byte)'a', (byte)'t', (byte)'a', (byte)'>' };
                    byte[] nakPattern = { (byte)'N', (byte)'A', (byte)'K' };
                    byte[] ackPattern = { (byte)'A', (byte)'C', (byte)'K' };

                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        if (ct.IsCancellationRequested) return false;

                        int bytesAvailable = _port.BytesToRead;
                        if (bytesAvailable > 0)
                        {
                            // Read as much data as possible
                            int toRead = Math.Min(buffer.Length - bufferPos, bytesAvailable);
                            if (toRead <= 0)
                            {
                                // Buffer full, reset
                                bufferPos = 0;
                                toRead = Math.Min(buffer.Length, bytesAvailable);
                            }
                            
                            int read = _port.Read(buffer, bufferPos, toRead);
                            if (read > 0)
                            {
                                bufferPos += read;
                                
                                // Fast byte-level check
                                if (IndexOfPattern(buffer, 0, bufferPos, nakPattern) >= 0)
                                {
                                    _logDetail("[Write] Device rejected (NAK)");
                                    return false;
                                }

                                // Check for rawmode or ACK
                                bool hasRawMode = IndexOfPattern(buffer, 0, bufferPos, rawmodePattern) >= 0;
                                bool hasAck = IndexOfPattern(buffer, 0, bufferPos, ackPattern) >= 0;
                                bool hasDataEnd = IndexOfPattern(buffer, 0, bufferPos, dataEndPattern) >= 0;
                                
                                if ((hasRawMode || hasAck) && hasDataEnd)
                                    return true;
                                
                                spinCount = 0;
                            }
                        }
                        else
                        {
                            // Aggressive spin strategy: reduce context switching
                            spinCount++;
                            if (spinCount < 500)
                            {
                                Thread.SpinWait(50); // CPU spin
                            }
                            else if (spinCount < 2000)
                            {
                                Thread.Yield();
                            }
                            else
                            {
                                Thread.Sleep(0);
                            }
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logDetail(string.Format("[Write] Wait exception: {0}", ex.Message));
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

        #region Speed Statistics

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
                    _log(string.Format("[Speed] {0}: {1:F1}MB took {2:F1}s ({3:F2} MB/s)", operationName, mbTotal, seconds, mbps));
            }

            _transferStopwatch = null;
        }

        #endregion

        #region Authentication Support Methods

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
            catch (Exception ex)
            {
                _logDetail(string.Format("[Firehose] Exception sending raw XML: {0}", ex.Message));
                return null;
            }
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
            catch (Exception ex)
            {
                _logDetail(string.Format("[Firehose] Exception sending raw bytes: {0}", ex.Message));
                return null;
            }
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
                catch (Exception ex)
                {
                    // Retrying, log detailed message
                    _logDetail(string.Format("[Firehose] Get attribute {0} retry {1}/{2}: {3}", attrName, i + 1, maxRetries, ex.Message));
                }
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

        #region OPLUS (OPPO/Realme/OnePlus) VIP Authentication

        /// <summary>
        /// Perform VIP authentication process (based on Digest and Signature files)
        /// 6-step process: 1. Digest → 2. TransferCfg → 3. Verify(EnableVip=1) → 4. Signature → 5. SHA256Init → 6. Configure
        /// Reference edl_vip_auth.py and qdl-gpt implementations
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath, CancellationToken ct = default(CancellationToken))
        {
            if (!File.Exists(digestPath) || !File.Exists(signaturePath))
            {
                _log("[VIP] Authentication failed: Missing Digest or Signature file");
                return false;
            }

            _log("[VIP] Performing security verification...");
            _logDetail(string.Format("[VIP] Digest: {0}", digestPath));
            _logDetail(string.Format("[VIP] Signature: {0}", signaturePath));
            
            bool hasError = false;
            string errorDetail = "";
            
            try
            {
                // Purge buffers
                PurgeBuffer();

                // ========== Step 1: Send Digest directly (binary data) ==========
                byte[] digestData = File.ReadAllBytes(digestPath);
                _logDetail(string.Format("[VIP] Step 1/6: Digest ({0} bytes)", digestData.Length));
                if (digestData.Length >= 16)
                {
                    _logDetail(string.Format("[VIP] Digest header: {0}", BitConverter.ToString(digestData, 0, 16)));
                }
                await _port.WriteAsync(digestData, 0, digestData.Length, ct);
                await Task.Delay(500, ct);
                string resp1 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                _logDetail(string.Format("[VIP] Step 1 response: {0}", TruncateResponse(resp1)));
                if (resp1.Contains("NAK"))
                {
                    hasError = true;
                    errorDetail = "Digest rejected";
                }

                // ========== Step 2: Send TransferCfg (Critical step!) ==========
                _logDetail("[VIP] Step 2/6: TransferCfg");
                string transferCfgXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><transfercfg reboot_type=\"off\" timeout_in_sec=\"90\" /></data>";
                _port.Write(Encoding.UTF8.GetBytes(transferCfgXml));
                await Task.Delay(300, ct);
                string resp2 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                _logDetail(string.Format("[VIP] Step 2 response: {0}", TruncateResponse(resp2)));

                // ========== Step 3: Send Verify (Enable VIP mode) ==========
                _logDetail("[VIP] Step 3/6: Verify (EnableVip=1)");
                string verifyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><verify value=\"ping\" EnableVip=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(verifyXml));
                await Task.Delay(300, ct);
                string resp3 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                _logDetail(string.Format("[VIP] Step 3 response: {0}", TruncateResponse(resp3)));

                // ========== Step 4: Send Signature directly (binary data) ==========
                byte[] sigData = File.ReadAllBytes(signaturePath);
                _logDetail(string.Format("[VIP] Step 4/6: Signature ({0} bytes)", sigData.Length));
                if (sigData.Length >= 16)
                {
                    _logDetail(string.Format("[VIP] Signature header: {0}", BitConverter.ToString(sigData, 0, 16)));
                }
                
                // When rawmode="true", device expects data to be received at sector size (4096 bytes)
                bool isRawMode = resp3.Contains("rawmode=\"true\"");
                int targetSize = isRawMode ? 4096 : sigData.Length;
                
                byte[] sigDataPadded;
                if (sigData.Length < targetSize)
                {
                    sigDataPadded = new byte[targetSize];
                    Array.Copy(sigData, 0, sigDataPadded, 0, sigData.Length);
                    _logDetail(string.Format("[VIP] Signature padding: {0} → {1} bytes", sigData.Length, targetSize));
                }
                else
                {
                    sigDataPadded = sigData;
                }
                
                await _port.WriteAsync(sigDataPadded, 0, sigDataPadded.Length, ct);
                await Task.Delay(500, ct);
                string resp4 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                _logDetail(string.Format("[VIP] Step 4 response: {0}", TruncateResponse(resp4)));
                
                // Check response - distinguish real errors from warnings
                if (resp4.Contains("NAK"))
                {
                    hasError = true;
                    errorDetail = "Signature rejected by device (NAK)";
                    _log("[VIP] ⚠ " + errorDetail);
                    _log(string.Format("[VIP] Detailed response: {0}", resp4));
                }
                else if (resp4.Contains("ERROR") && !resp4.Contains("ACK"))
                {
                    hasError = true;
                    errorDetail = "Signature transfer error";
                    _log("[VIP] ⚠ " + errorDetail);
                    _log(string.Format("[VIP] Detailed response: {0}", resp4));
                }

                // ========== Step 5: Send SHA256Init ==========
                _logDetail("[VIP] Step 5/6: SHA256Init");
                string sha256Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><sha256init Verbose=\"1\"/></data>";
                _port.Write(Encoding.UTF8.GetBytes(sha256Xml));
                await Task.Delay(300, ct);
                string respSha = await ReadAndLogDeviceResponseAsync(ct, 2000);
                _logDetail(string.Format("[VIP] Step 5 response: {0}", TruncateResponse(respSha)));

                // Step 6: Configure will be called externally
                if (!hasError)
                {
                    _log("[VIP] ✓ Security verification complete");
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[VIP] Verification cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _log(string.Format("[VIP] Verification exception: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Perform VIP authentication process (using byte[] data, no files required)
        /// </summary>
        /// <param name="digestData">Digest data (Hash Segment)</param>
        /// <param name="signatureData">Signature data (256-byte RSA-2048)</param>
        public async Task<bool> PerformVipAuthAsync(byte[] digestData, byte[] signatureData, CancellationToken ct = default(CancellationToken))
        {
            if (digestData == null || digestData.Length == 0)
            {
                _log("[VIP] Authentication failed: Missing Digest data");
                return false;
            }
            if (signatureData == null || signatureData.Length == 0)
            {
                _log("[VIP] Authentication failed: Missing Signature data");
                return false;
            }

            _log("[VIP] Starting security verification (memory data mode)...");

            try
            {
                // Step 1: Send Digest
                await SendVipDigestAsync(digestData, ct);

                // Step 2-3: Prepare VIP mode
                await PrepareVipModeAsync(ct);

                // Step 4: Send signature (256 bytes)
                await SendVipSignatureAsync(signatureData, ct);

                // Step 5: Finalize authentication
                await FinalizeVipAuthAsync(ct);

                // As long as the process completes, assume success (signature response detection may be inaccurate)
                _log("[VIP] VIP authentication process complete");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[VIP] Verification cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _log(string.Format("[VIP] Verification exception: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Step 1: Send VIP Digest (Hash Segment)
        /// </summary>
        public async Task<bool> SendVipDigestAsync(byte[] digestData, CancellationToken ct = default(CancellationToken))
        {
            _log(string.Format("[VIP] Step 1: Sending Digest ({0} bytes)...", digestData.Length));
            PurgeBuffer();

            await _port.WriteAsync(digestData, 0, digestData.Length, ct);
            await Task.Delay(500, ct);

            string resp = await ReadAndLogDeviceResponseAsync(ct, 3000);
            if (resp.Contains("NAK") || resp.Contains("ERROR"))
            {
                _log("[VIP] Digest response abnormal, attempting to continue...");
            }
            return true;
        }

        /// <summary>
        /// Step 2-3: Prepare VIP mode (TransferCfg + Verify)
        /// TransferCfg is a critical step, refer to edl_vip_auth.py
        /// </summary>
        public async Task<bool> PrepareVipModeAsync(CancellationToken ct = default(CancellationToken))
        {
            // Step 2: TransferCfg (Critical step!)
            _log("[VIP] Step 2: Sending TransferCfg...");
            string transferCfgXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                "<data><transfercfg reboot_type=\"off\" timeout_in_sec=\"90\" /></data>";
            _port.Write(Encoding.UTF8.GetBytes(transferCfgXml));
            await Task.Delay(300, ct);
            string resp2 = await ReadAndLogDeviceResponseAsync(ct, 2000);
            if (resp2.Contains("NAK") || resp2.Contains("ERROR"))
            {
                _log("[VIP] TransferCfg failed, attempting to continue...");
            }

            // Step 3: Verify (Enable VIP mode)
            _log("[VIP] Step 3: Sending Verify (EnableVip=1)...");
            string verifyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                "<data><verify value=\"ping\" EnableVip=\"1\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(verifyXml));
            await Task.Delay(300, ct);
            string resp3 = await ReadAndLogDeviceResponseAsync(ct, 2000);
            if (resp3.Contains("NAK") || resp3.Contains("ERROR"))
            {
                _log("[VIP] Verify failed, attempting to continue...");
            }

            return true;
        }

        /// <summary>
        /// Step 4: Send VIP signature (256-byte RSA-2048, requires padding to 4096 bytes in rawmode)
        /// This is the core method: write signature after sending Digest
        /// </summary>
        /// <param name="signatureData">Signature data (256 bytes)</param>
        /// <param name="padTo4096">Whether to pad to 4096 bytes (required in rawmode)</param>
        public async Task<bool> SendVipSignatureAsync(byte[] signatureData, CancellationToken ct = default(CancellationToken), bool padTo4096 = true)
        {
            // Handle signature data size
            byte[] sig;
            if (signatureData.Length == 256)
            {
                // Already correct size
                sig = signatureData;
            }
            else if (signatureData.Length > 256)
            {
                // Extract first 256 bytes (handle sign.bin with padding)
                sig = new byte[256];
                Array.Copy(signatureData, 0, sig, 0, 256);
                _log(string.Format("[VIP] Extracted 256-byte signature from {0} bytes of data", signatureData.Length));
            }
            else
            {
                // Insufficient data, pad with zeros
                sig = new byte[256];
                Array.Copy(signatureData, 0, sig, 0, signatureData.Length);
                _log(string.Format("[VIP] Warning: Signature data less than 256 bytes (actual {0})", signatureData.Length));
            }
            
            // Device expects 4096 bytes (sector size) in rawmode
            byte[] sigPadded;
            if (padTo4096 && sig.Length < 4096)
            {
                sigPadded = new byte[4096];
                Array.Copy(sig, 0, sigPadded, 0, sig.Length);
                _log(string.Format("[VIP] Step 4: Sending Signature ({0} → {1} bytes, rawmode padding)...", sig.Length, sigPadded.Length));
            }
            else
            {
                sigPadded = sig;
                _log(string.Format("[VIP] Step 4: Sending Signature ({0} bytes)...", sig.Length));
            }
            
            await _port.WriteAsync(sigPadded, 0, sigPadded.Length, ct);
            await Task.Delay(500, ct);

            string resp = await ReadAndLogDeviceResponseAsync(ct, 3000);
            
            // Check response - distinguish real errors from warnings
            bool success = false;
            if (resp.Contains("NAK"))
            {
                _log("[VIP] Signature rejected by device (NAK)");
            }
            else if (resp.Contains("ACK"))
            {
                // ACK indicates success, even if there are ERROR logs they might just be warnings
                _log("[VIP] ✓ Signature accepted");
                success = true;
            }
            else if (resp.Contains("ERROR") && !resp.Contains("ACK"))
            {
                _log("[VIP] Signature transfer error");
            }
            else if (string.IsNullOrEmpty(resp))
            {
                _log("[VIP] Signature send complete (no response)");
                success = true; // No response might also be success
            }
            else
            {
                _log("[VIP] Signature send complete");
                success = true;
            }

            return success;
        }

        /// <summary>
        /// Step 5: Complete VIP authentication (SHA256Init)
        /// </summary>
        public async Task<bool> FinalizeVipAuthAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[VIP] Step 5: Sending SHA256Init...");
            string sha256Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                "<data><sha256init Verbose=\"1\"/></data>";
            _port.Write(Encoding.UTF8.GetBytes(sha256Xml));
            await Task.Delay(300, ct);
            string resp = await ReadAndLogDeviceResponseAsync(ct, 2000);
            if (resp.Contains("NAK") || resp.Contains("ERROR"))
            {
                _log("[VIP] SHA256Init failed, attempting to continue...");
            }

            _log("[VIP] VIP verification process complete");
            return true;
        }
        
        /// <summary>
        /// Truncate response string for log display
        /// </summary>
        private string TruncateResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "(Empty)";
            
            // Remove newlines for easier display
            string clean = response.Replace("\r", "").Replace("\n", " ").Trim();
            
            // Truncate overly long response
            if (clean.Length > 300)
                return clean.Substring(0, 300) + "...";
            
            return clean;
        }

        /// <summary>
        /// Read and log device response (Asynchronous non-blocking)
        /// </summary>
        private async Task<string> ReadAndLogDeviceResponseAsync(CancellationToken ct, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            var sb = new StringBuilder();
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                
                // Check for available data
                int bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    byte[] buffer = new byte[bytesToRead];
                    int read = _port.Read(buffer, 0, bytesToRead);
                    
                    if (read > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        
                        var content = sb.ToString();
                        
                        // Extract device logs (detailed logs, not displayed on main interface)
                        var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                        foreach (Match m in logMatches)
                        {
                            if (m.Groups.Count > 1)
                                _logDetail(string.Format("[Device] {0}", m.Groups[1].Value));
                        }
                        
                        // Check response
                        if (content.Contains("<response") || content.Contains("</data>"))
                        {
                            if (content.Contains("value=\"ACK\"") || content.Contains("verify passed"))
                            {
                                return content; // Success
                            }
                            if (content.Contains("NAK") || content.Contains("ERROR"))
                            {
                                return content; // Failed but returned response
                            }
                        }
                    }
                }
                
                await Task.Delay(50, ct);
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Get device's current challenge code (for online signing)
        /// </summary>
        public async Task<string> GetVipChallengeAsync(CancellationToken ct = default(CancellationToken))
        {
            _log("[VIP] Getting device challenge code (getsigndata)...");
            string xml = "<?xml version=\"1.0\" ?><data>\n<getsigndata value=\"ping\" />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));

            // Try to extract NV data from returned INFO logs
            var response = await ReadRawResponseAsync(3000, ct);
            if (response != null && response.Contains("NV:"))
            {
                var match = Regex.Match(response, "NV:([^;\\s]+)");
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        /// <summary>
        /// Initialize SHA256 (required before OPLUS partition write)
        /// </summary>
        public async Task<bool> Sha256InitAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\" ?><data>\n<sha256init />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// Finalize SHA256 (required after OPLUS partition write)
        /// </summary>
        public async Task<bool> Sha256FinalAsync(CancellationToken ct = default(CancellationToken))
        {
            string xml = "<?xml version=\"1.0\" ?><data>\n<sha256final />\n</data>\n";
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Format file size (KB if less than 1MB, GB if more than 1GB)
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return string.Format("{0:F2} GB", bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024 * 1024)
                return string.Format("{0:F2} MB", bytes / (1024.0 * 1024));
            if (bytes >= 1024)
                return string.Format("{0:F0} KB", bytes / 1024.0);
            return string.Format("{0} B", bytes);
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
