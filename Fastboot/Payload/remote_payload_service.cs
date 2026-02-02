
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Payload
{
    /// <summary>
    /// Cloud Payload Service
    /// Supports direct parsing and extraction of partitions from OTA packages via remote URLs
    /// Reference: oplus_ota_tool_gui.py implementation
    /// </summary>
    public class RemotePayloadService : IDisposable
    {
        #region Constants

        private const uint ZIP_LOCAL_FILE_HEADER_SIG = 0x04034B50;
        private const uint ZIP_CENTRAL_DIR_SIG = 0x02014B50;
        private const uint ZIP_EOCD_SIG = 0x06054B50;
        private const uint ZIP64_EOCD_SIG = 0x06064B50;
        private const uint ZIP64_EOCD_LOCATOR_SIG = 0x07064B50;

        private const uint PAYLOAD_MAGIC = 0x43724155; // "CrAU" in big-endian

        // InstallOperation Types
        private const int OP_REPLACE = 0;
        private const int OP_REPLACE_BZ = 1;
        private const int OP_REPLACE_XZ = 8;
        private const int OP_ZERO = 6;

        #endregion

        #region Fields

        private HttpClient _httpClient;
        private string _currentUrl;
        private long _totalSize;
        private long _payloadDataOffset;
        private long _dataStartOffset;
        private uint _blockSize = 4096;
        private List<RemotePayloadPartition> _partitions = new List<RemotePayloadPartition>();
        private bool _disposed;

        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<long, long> _progress;

        #endregion

        #region Properties

        public bool IsLoaded { get; private set; }
        public string CurrentUrl => _currentUrl;
        public long TotalSize => _totalSize;
        public IReadOnlyList<RemotePayloadPartition> Partitions => _partitions;
        public uint BlockSize => _blockSize;

        #endregion

        #region Events

        public event EventHandler<RemoteExtractProgress> ExtractProgressChanged;

        #endregion

        #region Constructor

        public RemotePayloadService(Action<string> log = null, Action<long, long> progress = null, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _progress = progress;
            _logDetail = logDetail ?? (msg => { });

            // Create HttpClientHandler to handle automatic redirects and decompression
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false, // Handle redirects manually
                AutomaticDecompression = DecompressionMethods.None // No automatic decompression
            };

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
            SetUserAgent(null); // Use default User-Agent
        }

        /// <summary>
        /// Set custom User-Agent
        /// </summary>
        public void SetUserAgent(string userAgent)
        {
            _httpClient.DefaultRequestHeaders.Clear();

            string ua = string.IsNullOrEmpty(userAgent)
                ? "LoveAlways/1.0 (Payload Extractor)"
                : userAgent;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Get real download URL (handle redirects)
        /// </summary>
        public async Task<(string RealUrl, DateTime? ExpiresTime)> GetRedirectUrlAsync(string url, CancellationToken ct = default)
        {
            // If not a downloadCheck link, return directly
            if (!url.Contains("downloadCheck?"))
            {
                return (url, ParseExpiresTime(url));
            }

            _log("Getting real download link...");

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    // Check for redirects
                    if ((int)response.StatusCode >= 301 && (int)response.StatusCode <= 308)
                    {
                        var location = response.Headers.Location?.ToString();
                        if (!string.IsNullOrEmpty(location))
                        {
                            _log("✓ Successfully obtained real link");
                            return (location, ParseExpiresTime(location));
                        }
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        return (url, ParseExpiresTime(url));
                    }
                }
                catch (TaskCanceledException)
                {
                    _log($"Timeout, retrying {attempt + 1}/3...");
                }
                catch (Exception ex)
                {
                    _log($"Request failed: {ex.Message}");
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Load Payload info from URL (does not download the entire file)
        /// </summary>
        public async Task<bool> LoadFromUrlAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url))
            {
                _log("URL cannot be empty");
                return false;
            }

            try
            {
                _currentUrl = url;
                _partitions.Clear();
                IsLoaded = false;

                _log($"Connecting: {GetUrlHost(url)}");

                // 1. Get total file size (using HEAD request)
                var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!headResponse.IsSuccessStatusCode)
                {
                    // HEAD request failed, try GET request and read only the header
                    _logDetail("HEAD request failed, trying GET request...");
                    var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (getResponse.Content.Headers.ContentRange?.Length != null)
                    {
                        _totalSize = getResponse.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (!getResponse.IsSuccessStatusCode)
                    {
                        _log($"URL inaccessible: HTTP {(int)headResponse.StatusCode}");
                        return false;
                    }
                    else
                    {
                        _totalSize = getResponse.Content.Headers.ContentLength ?? 0;
                    }
                }
                else
                {
                    _totalSize = headResponse.Content.Headers.ContentLength ?? 0;
                }
                if (_totalSize == 0)
                {
                    _log("Unable to get file size");
                    return false;
                }

                _log($"File size: {FormatSize(_totalSize)}");

                // 2. Determine if it's ZIP or direct payload.bin
                string urlPath = url.Split('?')[0].ToLowerInvariant();
                bool isZip = urlPath.EndsWith(".zip") || urlPath.Contains("ota");

                if (isZip)
                {
                    _log("Parsing ZIP structure...");
                    await ParseZipStructureAsync(url, ct);
                }
                else
                {
                    // Direct payload.bin
                    _payloadDataOffset = 0;
                }

                // 3. Parse Payload header and Manifest
                await ParsePayloadHeaderAsync(url, ct);

                IsLoaded = true;
                _log($"✓ Successfully parsed: {_partitions.Count} partitions");

                return true;
            }
            catch (Exception ex)
            {
                _log($"Load failed: {ex.Message}");
                _logDetail($"Load error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Extract partition from cloud to local file
        /// </summary>
        public async Task<bool> ExtractPartitionAsync(string partitionName, string outputPath,
            CancellationToken ct = default)
        {
            if (!IsLoaded)
            {
                _log("Please load Payload first");
                return false;
            }

            var partition = _partitions.FirstOrDefault(p =>
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

            if (partition == null)
            {
                _log($"Partition not found: {partitionName}");
                return false;
            }

            try
            {
                _log($"Starting to extract '{partitionName}' ({FormatSize((long)partition.Size)})");

                string outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                int totalOps = partition.Operations.Count;
                int processedOps = 0;
                long downloadedBytes = 0;

                // Speed calculation related
                var startTime = DateTime.Now;
                var lastSpeedUpdateTime = startTime;
                long lastSpeedUpdateBytes = 0;
                double currentSpeed = 0;

                using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    // Pre-allocate file size
                    outputStream.SetLength((long)partition.Size);

                    foreach (var operation in partition.Operations)
                    {
                        ct.ThrowIfCancellationRequested();

                        byte[] decompressedData = null;

                        if (operation.DataLength > 0)
                        {
                            // Download operation data
                            long absStart = _dataStartOffset + (long)operation.DataOffset;
                            long absEnd = absStart + (long)operation.DataLength - 1;

                            byte[] compressedData = await FetchRangeAsync(_currentUrl, absStart, absEnd, ct);
                            downloadedBytes += compressedData.Length;

                            // Decompress data
                            decompressedData = DecompressData(operation.Type, compressedData,
                                (long)operation.DstNumBlocks * _blockSize);
                        }
                        else if (operation.Type == OP_ZERO)
                        {
                            // ZERO operation
                            long totalBlocks = (long)operation.DstNumBlocks;
                            decompressedData = new byte[totalBlocks * _blockSize];
                        }

                        if (decompressedData != null)
                        {
                            // Write to target position
                            long dstOffset = (long)operation.DstStartBlock * _blockSize;
                            outputStream.Seek(dstOffset, SeekOrigin.Begin);
                            outputStream.Write(decompressedData, 0,
                                Math.Min(decompressedData.Length, (int)((long)operation.DstNumBlocks * _blockSize)));
                        }

                        processedOps++;
                        double percent = 100.0 * processedOps / totalOps;

                        // Calculate speed (update once per second)
                        var now = DateTime.Now;
                        var timeSinceLastUpdate = (now - lastSpeedUpdateTime).TotalSeconds;
                        if (timeSinceLastUpdate >= 1.0)
                        {
                            long bytesSinceLastUpdate = downloadedBytes - lastSpeedUpdateBytes;
                            currentSpeed = bytesSinceLastUpdate / timeSinceLastUpdate;
                            lastSpeedUpdateTime = now;
                            lastSpeedUpdateBytes = downloadedBytes;
                        }
                        else if (currentSpeed == 0 && downloadedBytes > 0)
                        {
                            // Initial speed estimate
                            var elapsed = (now - startTime).TotalSeconds;
                            if (elapsed > 0.1)
                            {
                                currentSpeed = downloadedBytes / elapsed;
                            }
                        }

                        var elapsedTime = now - startTime;

                        _progress?.Invoke(processedOps, totalOps);
                        ExtractProgressChanged?.Invoke(this, new RemoteExtractProgress
                        {
                            PartitionName = partitionName,
                            CurrentOperation = processedOps,
                            TotalOperations = totalOps,
                            DownloadedBytes = downloadedBytes,
                            Percent = percent,
                            SpeedBytesPerSecond = currentSpeed,
                            ElapsedTime = elapsedTime
                        });

                        if (processedOps % 50 == 0)
                        {
                            _logDetail($"Progress: {processedOps}/{totalOps} ({percent:F1}%)");
                        }
                    }
                }

                var totalTime = DateTime.Now - startTime;
                double avgSpeed = downloadedBytes / Math.Max(totalTime.TotalSeconds, 0.1);
                _log($"✓ Extraction complete: {Path.GetFileName(outputPath)}");
                _log($"Downloaded data: {FormatSize(downloadedBytes)}, time taken: {totalTime.TotalSeconds:F1}s, average speed: {FormatSize((long)avgSpeed)}/s");

                return true;
            }
            catch (OperationCanceledException)
            {
                _log("Extraction cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _log($"Extraction failed: {ex.Message}");
                _logDetail($"Extraction error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Stream flash event arguments
        /// </summary>
        public class StreamFlashProgressEventArgs : EventArgs
        {
            public string PartitionName { get; set; }
            public StreamFlashPhase Phase { get; set; }
            public double Percent { get; set; }
            public long DownloadedBytes { get; set; }
            public long TotalBytes { get; set; }
            public double DownloadSpeedBytesPerSecond { get; set; }
            public double FlashSpeedBytesPerSecond { get; set; }

            public string DownloadSpeedFormatted => FormatSpeed(DownloadSpeedBytesPerSecond);
            public string FlashSpeedFormatted => FormatSpeed(FlashSpeedBytesPerSecond);

            private static string FormatSpeed(double speed)
            {
                if (speed <= 0) return "Calculating...";
                string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
                int unitIndex = 0;
                while (speed >= 1024 && unitIndex < units.Length - 1)
                {
                    speed /= 1024;
                    unitIndex++;
                }
                return $"{speed:F2} {units[unitIndex]}";
            }
        }

        public enum StreamFlashPhase
        {
            Downloading,
            Flashing,
            Completed
        }

        /// <summary>
        /// Stream flash progress event
        /// </summary>
        public event EventHandler<StreamFlashProgressEventArgs> StreamFlashProgressChanged;

        /// <summary>
        /// Extract partition from cloud and flash directly to device
        /// </summary>
        /// <param name="partitionName">Partition name</param>
        /// <param name="flashCallback">Flash callback, parameters: temporary file path, returns: whether successful, bytes flashed, time taken</param>
        /// <param name="ct">Cancellation token</param>
        public async Task<bool> ExtractAndFlashPartitionAsync(
            string partitionName,
            Func<string, Task<(bool success, long bytesFlashed, double elapsedSeconds)>> flashCallback,
            CancellationToken ct = default)
        {
            if (!IsLoaded)
            {
                _log("Please load Payload first");
                return false;
            }

            var partition = _partitions.FirstOrDefault(p =>
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

            if (partition == null)
            {
                _log($"Partition not found: {partitionName}");
                return false;
            }

            // Create temporary file
            string tempPath = Path.Combine(Path.GetTempPath(), $"payload_{partitionName}_{Guid.NewGuid():N}.img");

            try
            {
                _log($"Starting download '{partitionName}' ({FormatSize((long)partition.Size)})");

                // Download phase
                var downloadStartTime = DateTime.Now;
                long downloadedBytes = 0;
                int totalOps = partition.Operations.Count;
                int processedOps = 0;
                double downloadSpeed = 0;
                var lastSpeedUpdateTime = downloadStartTime;
                long lastSpeedUpdateBytes = 0;

                using (var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    outputStream.SetLength((long)partition.Size);

                    foreach (var operation in partition.Operations)
                    {
                        ct.ThrowIfCancellationRequested();

                        byte[] decompressedData = null;

                        if (operation.DataLength > 0)
                        {
                            long absStart = _dataStartOffset + (long)operation.DataOffset;
                            long absEnd = absStart + (long)operation.DataLength - 1;

                            byte[] compressedData = await FetchRangeAsync(_currentUrl, absStart, absEnd, ct);
                            downloadedBytes += compressedData.Length;

                            decompressedData = DecompressData(operation.Type, compressedData,
                                (long)operation.DstNumBlocks * _blockSize);
                        }
                        else if (operation.Type == OP_ZERO)
                        {
                            long totalBlocks = (long)operation.DstNumBlocks;
                            decompressedData = new byte[totalBlocks * _blockSize];
                        }

                        if (decompressedData != null)
                        {
                            long dstOffset = (long)operation.DstStartBlock * _blockSize;
                            outputStream.Seek(dstOffset, SeekOrigin.Begin);
                            outputStream.Write(decompressedData, 0,
                                Math.Min(decompressedData.Length, (int)((long)operation.DstNumBlocks * _blockSize)));
                        }

                        processedOps++;

                        // Calculate download speed
                        var now = DateTime.Now;
                        var timeSinceLastUpdate = (now - lastSpeedUpdateTime).TotalSeconds;
                        if (timeSinceLastUpdate >= 1.0)
                        {
                            long bytesSinceLastUpdate = downloadedBytes - lastSpeedUpdateBytes;
                            downloadSpeed = bytesSinceLastUpdate / timeSinceLastUpdate;
                            lastSpeedUpdateTime = now;
                            lastSpeedUpdateBytes = downloadedBytes;
                        }
                        else if (downloadSpeed == 0 && downloadedBytes > 0)
                        {
                            var elapsed = (now - downloadStartTime).TotalSeconds;
                            if (elapsed > 0.1) downloadSpeed = downloadedBytes / elapsed;
                        }

                        double downloadPercent = 50.0 * processedOps / totalOps; // Download takes 50%

                        StreamFlashProgressChanged?.Invoke(this, new StreamFlashProgressEventArgs
                        {
                            PartitionName = partitionName,
                            Phase = StreamFlashPhase.Downloading,
                            Percent = downloadPercent,
                            DownloadedBytes = downloadedBytes,
                            TotalBytes = (long)partition.Size,
                            DownloadSpeedBytesPerSecond = downloadSpeed,
                            FlashSpeedBytesPerSecond = 0
                        });
                    }
                }

                var downloadTime = DateTime.Now - downloadStartTime;
                _log($"Download complete: {FormatSize(downloadedBytes)}, time taken: {downloadTime.TotalSeconds:F1}s");

                // Flash phase
                _log($"Starting to flash '{partitionName}'...");

                StreamFlashProgressChanged?.Invoke(this, new StreamFlashProgressEventArgs
                {
                    PartitionName = partitionName,
                    Phase = StreamFlashPhase.Flashing,
                    Percent = 50,
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = (long)partition.Size,
                    DownloadSpeedBytesPerSecond = downloadSpeed,
                    FlashSpeedBytesPerSecond = 0
                });

                var (flashSuccess, bytesFlashed, flashElapsed) = await flashCallback(tempPath);

                double flashSpeed = flashElapsed > 0 ? bytesFlashed / flashElapsed : 0;

                StreamFlashProgressChanged?.Invoke(this, new StreamFlashProgressEventArgs
                {
                    PartitionName = partitionName,
                    Phase = StreamFlashPhase.Completed,
                    Percent = 100,
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = (long)partition.Size,
                    DownloadSpeedBytesPerSecond = downloadSpeed,
                    FlashSpeedBytesPerSecond = flashSpeed
                });

                if (flashSuccess)
                {
                    _log($"✓ Flash successful: {partitionName} (Fastboot speed: {FormatSize((long)flashSpeed)}/s)");
                }
                else
                {
                    _log($"✗ Flash failed: {partitionName}");
                }

                return flashSuccess;
            }
            catch (OperationCanceledException)
            {
                _log("Operation cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _log($"Operation failed: {ex.Message}");
                _logDetail($"Error details: {ex}");
                return false;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }

        /// <summary>
        /// Get summary info
        /// </summary>
        public RemotePayloadSummary GetSummary()
        {
            if (!IsLoaded) return null;

            return new RemotePayloadSummary
            {
                Url = _currentUrl,
                TotalSize = _totalSize,
                BlockSize = _blockSize,
                PartitionCount = _partitions.Count,
                Partitions = _partitions.ToList()
            };
        }

        /// <summary>
        /// Close
        /// </summary>
        public void Close()
        {
            _currentUrl = null;
            _totalSize = 0;
            _partitions.Clear();
            IsLoaded = false;
        }

        #endregion

        #region Private Methods - ZIP Parsing

        private async Task ParseZipStructureAsync(string url, CancellationToken ct)
        {
            // Read file tail to find EOCD
            int readSize = (int)Math.Min(65536, _totalSize);
            long startOffset = _totalSize - readSize;
            byte[] tailData = await FetchRangeAsync(url, startOffset, _totalSize - 1, ct);

            // Find EOCD signature
            int eocdPos = -1;
            for (int i = tailData.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tailData, i) == ZIP_EOCD_SIG)
                {
                    eocdPos = i;
                    break;
                }
            }

            if (eocdPos < 0)
                throw new Exception("Unable to find ZIP EOCD record");

            long eocdOffset = startOffset + eocdPos;
            bool isZip64 = false;
            long zip64EocdOffset = 0;

            // Check for ZIP64
            if (eocdPos >= 20)
            {
                int locatorStart = eocdPos - 20;
                if (BitConverter.ToUInt32(tailData, locatorStart) == ZIP64_EOCD_LOCATOR_SIG)
                {
                    zip64EocdOffset = (long)BitConverter.ToUInt64(tailData, locatorStart + 8);
                    isZip64 = true;
                }
            }

            // Parse EOCD to get central directory location
            long centralDirOffset, centralDirSize;

            if (isZip64)
            {
                byte[] zip64EocdData = await FetchRangeAsync(url, zip64EocdOffset, zip64EocdOffset + 100, ct);
                if (BitConverter.ToUInt32(zip64EocdData, 0) != ZIP64_EOCD_SIG)
                    throw new Exception("ZIP64 EOCD signature mismatch");

                centralDirSize = (long)BitConverter.ToUInt64(zip64EocdData, 40);
                centralDirOffset = (long)BitConverter.ToUInt64(zip64EocdData, 48);
            }
            else
            {
                centralDirSize = BitConverter.ToUInt32(tailData, eocdPos + 12);
                centralDirOffset = BitConverter.ToUInt32(tailData, eocdPos + 16);
            }

            // Download central directory
            byte[] centralDirData = await FetchRangeAsync(url, centralDirOffset,
                centralDirOffset + centralDirSize - 1, ct);

            // Find payload.bin
            int pos = 0;
            while (pos < centralDirData.Length - 4)
            {
                if (BitConverter.ToUInt32(centralDirData, pos) != ZIP_CENTRAL_DIR_SIG)
                    break;

                uint compressedSize = BitConverter.ToUInt32(centralDirData, pos + 20);
                uint uncompressedSize = BitConverter.ToUInt32(centralDirData, pos + 24);
                ushort filenameLen = BitConverter.ToUInt16(centralDirData, pos + 28);
                ushort extraLen = BitConverter.ToUInt16(centralDirData, pos + 30);
                ushort commentLen = BitConverter.ToUInt16(centralDirData, pos + 32);
                uint localHeaderOffset = BitConverter.ToUInt32(centralDirData, pos + 42);

                string filename = Encoding.UTF8.GetString(centralDirData, pos + 46, filenameLen);

                // Process ZIP64 extra fields
                if (uncompressedSize == 0xFFFFFFFF || compressedSize == 0xFFFFFFFF || localHeaderOffset == 0xFFFFFFFF)
                {
                    int extraStart = pos + 46 + filenameLen;
                    int extraEnd = extraStart + extraLen;
                    int extraPos = extraStart;

                    while (extraPos + 4 <= extraEnd)
                    {
                        ushort headerId = BitConverter.ToUInt16(centralDirData, extraPos);
                        ushort dataSize = BitConverter.ToUInt16(centralDirData, extraPos + 2);

                        if (headerId == 0x0001) // ZIP64 extra field
                        {
                            int fieldPos = extraPos + 4;
                            if (uncompressedSize == 0xFFFFFFFF && fieldPos + 8 <= extraPos + 4 + dataSize)
                            {
                                uncompressedSize = (uint)BitConverter.ToUInt64(centralDirData, fieldPos);
                                fieldPos += 8;
                            }
                            if (compressedSize == 0xFFFFFFFF && fieldPos + 8 <= extraPos + 4 + dataSize)
                            {
                                compressedSize = (uint)BitConverter.ToUInt64(centralDirData, fieldPos);
                                fieldPos += 8;
                            }
                            if (localHeaderOffset == 0xFFFFFFFF && fieldPos + 8 <= extraPos + 4 + dataSize)
                            {
                                localHeaderOffset = (uint)BitConverter.ToUInt64(centralDirData, fieldPos);
                            }
                        }
                        extraPos += 4 + dataSize;
                    }
                }

                if (filename.Equals("payload.bin", StringComparison.OrdinalIgnoreCase))
                {
                    // Read local file header
                    byte[] lfhData = await FetchRangeAsync(url, localHeaderOffset, localHeaderOffset + 30, ct);

                    if (BitConverter.ToUInt32(lfhData, 0) != ZIP_LOCAL_FILE_HEADER_SIG)
                        throw new Exception("Local file header signature mismatch");

                    ushort lfhFilenameLen = BitConverter.ToUInt16(lfhData, 26);
                    ushort lfhExtraLen = BitConverter.ToUInt16(lfhData, 28);

                    _payloadDataOffset = localHeaderOffset + 30 + lfhFilenameLen + lfhExtraLen;
                    _logDetail($"payload.bin data offset: 0x{_payloadDataOffset:X}");
                    return;
                }

                pos += 46 + filenameLen + extraLen + commentLen;
            }

            throw new Exception("payload.bin not found in ZIP");
        }

        #endregion

        #region Private Methods - Payload Parsing

        private async Task ParsePayloadHeaderAsync(string url, CancellationToken ct)
        {
            // Read Payload header (24 bytes for v2)
            byte[] headerData = await FetchRangeAsync(url, _payloadDataOffset, _payloadDataOffset + 23, ct);

            // Verify Magic
            uint magic = ReadBigEndianUInt32(headerData, 0);
            if (magic != PAYLOAD_MAGIC)
                throw new Exception($"Invalid Payload magic: 0x{magic:X8}");

            ulong version = ReadBigEndianUInt64(headerData, 4);
            ulong manifestLen = ReadBigEndianUInt64(headerData, 12);
            uint metadataSignatureLen = version >= 2 ? ReadBigEndianUInt32(headerData, 20) : 0;
            int payloadHeaderLen = version >= 2 ? 24 : 20;

            _logDetail($"Payload version: {version}");
            _logDetail($"Manifest size: {manifestLen} bytes");

            // Download Manifest
            long manifestOffset = _payloadDataOffset + payloadHeaderLen;
            _log($"Downloading Manifest ({FormatSize((long)manifestLen)})...");
            byte[] manifestData = await FetchRangeAsync(url, manifestOffset,
                manifestOffset + (long)manifestLen - 1, ct);

            // Parse Manifest
            ParseManifest(manifestData);

            // Calculate data start position
            _dataStartOffset = _payloadDataOffset + payloadHeaderLen + (long)manifestLen + metadataSignatureLen;
            _logDetail($"Data start offset: 0x{_dataStartOffset:X}");
        }

        private void ParseManifest(byte[] data)
        {
            int pos = 0;

            while (pos < data.Length)
            {
                var (fieldNumber, wireType, value, newPos) = ReadProtobufField(data, pos);
                if (fieldNumber == 0) break;
                pos = newPos;

                switch (fieldNumber)
                {
                    case 3: // block_size
                        _blockSize = (uint)(ulong)value;
                        break;
                    case 13: // partitions
                        if (wireType == 2)
                        {
                            var partition = ParsePartitionUpdate((byte[])value);
                            if (partition != null)
                                _partitions.Add(partition);
                        }
                        break;
                }
            }
        }

        private RemotePayloadPartition ParsePartitionUpdate(byte[] data)
        {
            var partition = new RemotePayloadPartition();
            int pos = 0;

            while (pos < data.Length)
            {
                var (fieldNumber, wireType, value, newPos) = ReadProtobufField(data, pos);
                if (fieldNumber == 0) break;
                pos = newPos;

                switch (fieldNumber)
                {
                    case 1: // partition_name
                        if (wireType == 2)
                            partition.Name = Encoding.UTF8.GetString((byte[])value);
                        break;
                    case 7: // new_partition_info
                        if (wireType == 2)
                            ParsePartitionInfo((byte[])value, partition);
                        break;
                    case 8: // operations
                        if (wireType == 2)
                        {
                            var op = ParseInstallOperation((byte[])value);
                            if (op != null)
                                partition.Operations.Add(op);
                        }
                        break;
                }
            }

            return string.IsNullOrEmpty(partition.Name) ? null : partition;
        }

        private void ParsePartitionInfo(byte[] data, RemotePayloadPartition partition)
        {
            int pos = 0;
            while (pos < data.Length)
            {
                var (fieldNumber, wireType, value, newPos) = ReadProtobufField(data, pos);
                if (fieldNumber == 0) break;
                pos = newPos;

                if (fieldNumber == 1) // size
                    partition.Size = (ulong)value;
                else if (fieldNumber == 2 && wireType == 2) // hash
                    partition.Hash = (byte[])value;
            }
        }

        private RemotePayloadOperation ParseInstallOperation(byte[] data)
        {
            var op = new RemotePayloadOperation();
            int pos = 0;

            while (pos < data.Length)
            {
                var (fieldNumber, wireType, value, newPos) = ReadProtobufField(data, pos);
                if (fieldNumber == 0) break;
                pos = newPos;

                switch (fieldNumber)
                {
                    case 1: // type
                        op.Type = (int)(ulong)value;
                        break;
                    case 2: // data_offset
                        op.DataOffset = (ulong)value;
                        break;
                    case 3: // data_length
                        op.DataLength = (ulong)value;
                        break;
                    case 6: // dst_extents
                        if (wireType == 2)
                            ParseExtent((byte[])value, op);
                        break;
                }
            }

            return op;
        }

        private void ParseExtent(byte[] data, RemotePayloadOperation op)
        {
            int pos = 0;
            while (pos < data.Length)
            {
                var (fieldNumber, wireType, value, newPos) = ReadProtobufField(data, pos);
                if (fieldNumber == 0) break;
                pos = newPos;

                if (fieldNumber == 1) // start_block
                    op.DstStartBlock = (ulong)value;
                else if (fieldNumber == 2) // num_blocks
                    op.DstNumBlocks = (ulong)value;
            }
        }

        #endregion

        #region Private Methods - Helpers

        private async Task<byte[]> FetchRangeAsync(string url, long start, long end, CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

            // Use ResponseHeadersRead to avoid pre-buffering the entire response
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode != HttpStatusCode.PartialContent && response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"HTTP {(int)response.StatusCode}");

            // Calculate the number of bytes to read
            long bytesToRead = end - start + 1;

            // If the server supports Range requests (206), read content directly
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[bytesToRead];
                    int totalRead = 0;
                    while (totalRead < bytesToRead)
                    {
                        int read = await stream.ReadAsync(buffer, totalRead, (int)Math.Min(bytesToRead - totalRead, 81920), ct);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    if (totalRead < bytesToRead)
                    {
                        Array.Resize(ref buffer, totalRead);
                    }
                    return buffer;
                }
            }
            else
            {
                // Server returns 200 OK (Range not supported), need to skip and read only the specified range
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    // Skip bytes before start
                    byte[] skipBuffer = new byte[81920];
                    long skipped = 0;
                    while (skipped < start)
                    {
                        int toSkip = (int)Math.Min(start - skipped, skipBuffer.Length);
                        int read = await stream.ReadAsync(skipBuffer, 0, toSkip, ct);
                        if (read == 0) throw new Exception("Unable to skip to specified position");
                        skipped += read;
                    }

                    // Read data in the specified range
                    byte[] buffer = new byte[bytesToRead];
                    int totalRead = 0;
                    while (totalRead < bytesToRead)
                    {
                        int read = await stream.ReadAsync(buffer, totalRead, (int)Math.Min(bytesToRead - totalRead, 81920), ct);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    if (totalRead < bytesToRead)
                    {
                        Array.Resize(ref buffer, totalRead);
                    }
                    return buffer;
                }
            }
        }

        private (int fieldNumber, int wireType, object value, int newPos) ReadProtobufField(byte[] data, int pos)
        {
            if (pos >= data.Length)
                return (0, 0, null, pos);

            ulong tag = ReadVarint(data, ref pos);
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x7);

            object value = null;

            switch (wireType)
            {
                case 0: // Varint
                    value = ReadVarint(data, ref pos);
                    break;
                case 1: // 64-bit
                    value = BitConverter.ToUInt64(data, pos);
                    pos += 8;
                    break;
                case 2: // Length-delimited
                    int length = (int)ReadVarint(data, ref pos);
                    value = new byte[length];
                    Array.Copy(data, pos, (byte[])value, 0, length);
                    pos += length;
                    break;
                case 5: // 32-bit
                    value = BitConverter.ToUInt32(data, pos);
                    pos += 4;
                    break;
                default:
                    throw new Exception($"Unknown wire type: {wireType}");
            }

            return (fieldNumber, wireType, value, pos);
        }

        private ulong ReadVarint(byte[] data, ref int pos)
        {
            ulong result = 0;
            int shift = 0;

            while (pos < data.Length)
            {
                byte b = data[pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }

            return result;
        }

        private uint ReadBigEndianUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                          (data[offset + 2] << 8) | data[offset + 3]);
        }

        private ulong ReadBigEndianUInt64(byte[] data, int offset)
        {
            return ((ulong)data[offset] << 56) | ((ulong)data[offset + 1] << 48) |
                   ((ulong)data[offset + 2] << 40) | ((ulong)data[offset + 3] << 32) |
                   ((ulong)data[offset + 4] << 24) | ((ulong)data[offset + 5] << 16) |
                   ((ulong)data[offset + 6] << 8) | data[offset + 7];
        }

        private byte[] DecompressData(int opType, byte[] data, long expectedLength)
        {
            switch (opType)
            {
                case OP_REPLACE:
                    return data;

                case OP_REPLACE_XZ:
                    // XZ decompression
                    try
                    {
                        using (var input = new MemoryStream(data))
                        using (var output = new MemoryStream())
                        {
                            // Use simple LZMA decompression (requires System.IO.Compression or third-party library)
                            // Here we return raw data, actual use requires implementing XZ decompression
                            _logDetail("XZ decompression not yet implemented, returning raw data");
                            return data;
                        }
                    }
                    catch
                    {
                        return data;
                    }

                case OP_REPLACE_BZ:
                    // BZip2 decompression
                    _logDetail("BZip2 decompression not yet implemented, returning raw data");
                    return data;

                case OP_ZERO:
                    return new byte[expectedLength];

                default:
                    return data;
            }
        }

        private DateTime? ParseExpiresTime(string url)
        {
            try
            {
                var uri = new Uri(url);
                var queryParams = ParseQueryString(uri.Query);

                string expiresStr = null;
                if (queryParams.TryGetValue("Expires", out string expires))
                    expiresStr = expires;
                else if (queryParams.TryGetValue("x-oss-expires", out string ossExpires))
                    expiresStr = ossExpires;

                if (!string.IsNullOrEmpty(expiresStr) && long.TryParse(expiresStr, out long timestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Simple URL query string parsing
        /// </summary>
        private Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;

            // Remove leading ?
            if (query.StartsWith("?"))
                query = query.Substring(1);

            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = Uri.UnescapeDataString(parts[0]);
                    string value = Uri.UnescapeDataString(parts[1]);
                    result[key] = value;
                }
                else if (parts.Length == 1 && !string.IsNullOrEmpty(parts[0]))
                {
                    result[Uri.UnescapeDataString(parts[0])] = "";
                }
            }
            return result;
        }

        private string GetUrlHost(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return url;
            }
        }

        private string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Remote Payload Partition Information
    /// </summary>
    public class RemotePayloadPartition
    {
        public string Name { get; set; }
        public ulong Size { get; set; }
        public byte[] Hash { get; set; }
        public List<RemotePayloadOperation> Operations { get; set; } = new List<RemotePayloadOperation>();

        public string SizeFormatted
        {
            get
            {
                string[] units = { "B", "KB", "MB", "GB" };
                double size = Size;
                int unitIndex = 0;
                while (size >= 1024 && unitIndex < units.Length - 1)
                {
                    size /= 1024;
                    unitIndex++;
                }
                return $"{size:F2} {units[unitIndex]}";
            }
        }
    }

    /// <summary>
    /// Remote Payload Operation
    /// </summary>
    public class RemotePayloadOperation
    {
        public int Type { get; set; }
        public ulong DataOffset { get; set; }
        public ulong DataLength { get; set; }
        public ulong DstStartBlock { get; set; }
        public ulong DstNumBlocks { get; set; }
    }

    /// <summary>
    /// Remote Extraction Progress
    /// </summary>
    public class RemoteExtractProgress
    {
        public string PartitionName { get; set; }
        public int CurrentOperation { get; set; }
        public int TotalOperations { get; set; }
        public long DownloadedBytes { get; set; }
        public double Percent { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// Formatted speed display
        /// </summary>
        public string SpeedFormatted
        {
            get
            {
                if (SpeedBytesPerSecond <= 0) return "Calculating...";

                string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
                double speed = SpeedBytesPerSecond;
                int unitIndex = 0;
                while (speed >= 1024 && unitIndex < units.Length - 1)
                {
                    speed /= 1024;
                    unitIndex++;
                }
                return $"{speed:F2} {units[unitIndex]}";
            }
        }
    }

    /// <summary>
    /// Remote Payload Summary
    /// </summary>
    public class RemotePayloadSummary
    {
        public string Url { get; set; }
        public long TotalSize { get; set; }
        public uint BlockSize { get; set; }
        public int PartitionCount { get; set; }
        public List<RemotePayloadPartition> Partitions { get; set; }

        public string TotalSizeFormatted
        {
            get
            {
                string[] units = { "B", "KB", "MB", "GB" };
                double size = TotalSize;
                int unitIndex = 0;
                while (size >= 1024 && unitIndex < units.Length - 1)
                {
                    size /= 1024;
                    unitIndex++;
                }
                return $"{size:F2} {units[unitIndex]}";
            }
        }
    }

    #endregion
}
