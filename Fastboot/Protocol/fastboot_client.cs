
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Fastboot.Image;
using LoveAlways.Fastboot.Transport;

namespace LoveAlways.Fastboot.Protocol
{
    /// <summary>
    /// Fastboot Client Core Class
    /// C# implementation rewritten based on Google AOSP fastboot source code
    /// 
    /// Supported Features:
    /// - Device detection and connection
    /// - Variable reading (getvar)
    /// - Partition flashing (flash) - Supports Sparse images
    /// - Partition erasing (erase)
    /// - Reboot operations (reboot)
    /// - A/B slot switching
    /// - Bootloader unlock/lock
    /// - Real-time progress callback
    /// </summary>
    public class FastbootClient : IDisposable
    {
        private IFastbootTransport _transport;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private bool _disposed;
        
        // Device info cache
        private Dictionary<string, string> _variables;
        private long _maxDownloadSize = 512 * 1024 * 1024; // Default 512MB
        
        /// <summary>
        /// Whether connected
        /// </summary>
        public bool IsConnected => _transport?.IsConnected ?? false;
        
        /// <summary>
        /// Device serial number
        /// </summary>
        public string Serial => _transport?.DeviceId;
        
        /// <summary>
        /// Maximum download size
        /// </summary>
        public long MaxDownloadSize => _maxDownloadSize;
        
        /// <summary>
        /// Device variables
        /// </summary>
        public IReadOnlyDictionary<string, string> Variables => _variables;
        
        /// <summary>
        /// Progress update event
        /// </summary>
        public event EventHandler<FastbootProgressEventArgs> ProgressChanged;
        
        public FastbootClient(Action<string> log = null, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _logDetail = logDetail ?? (msg => { });
            _variables = new Dictionary<string, string>();
        }
        
        #region Device Connection
        
        /// <summary>
        /// Enumerate all Fastboot devices
        /// </summary>
        public static List<FastbootDeviceDescriptor> GetDevices()
        {
            return UsbTransport.EnumerateDevices();
        }
        
        /// <summary>
        /// Connect to device
        /// </summary>
        public async Task<bool> ConnectAsync(FastbootDeviceDescriptor device, CancellationToken ct = default)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            
            Disconnect();
            
            _log($"Connecting device: {device}");
            
            if (device.Type == TransportType.Usb)
            {
                _transport = new UsbTransport(device);
            }
            else
            {
                throw new NotSupportedException("TCP connections are not supported for now");
            }
            
            if (!await _transport.ConnectAsync(ct))
            {
                _log("Connection failed");
                return false;
            }
            
            _log("Connection successful");
            
            // Read device info
            await RefreshDeviceInfoAsync(ct);
            
            return true;
        }
        
        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _transport?.Disconnect();
            _transport?.Dispose();
            _transport = null;
            _variables.Clear();
        }
        
        #endregion
        
        #region Basic Commands
        
        /// <summary>
        /// Send command and wait for response
        /// </summary>
        public async Task<FastbootResponse> SendCommandAsync(string command, int timeoutMs = FastbootProtocol.DEFAULT_TIMEOUT_MS, CancellationToken ct = default)
        {
            EnsureConnected();
            
            _logDetail($">>> {command}");
            
            byte[] cmdBytes = FastbootProtocol.BuildCommand(command);
            byte[] response = await _transport.TransferAsync(cmdBytes, timeoutMs, ct);
            
            if (response == null || response.Length == 0)
            {
                return new FastbootResponse { Type = ResponseType.Fail, Message = "No response" };
            }
            
            var result = FastbootProtocol.ParseResponse(response, response.Length);
            _logDetail($"<<< {result}");
            
            // Handle INFO messages (there may be multiple)
            while (result.IsInfo)
            {
                _log($"INFO: {result.Message}");
                
                // Continue reading next response
                response = await ReceiveResponseAsync(timeoutMs, ct);
                if (response == null) break;
                
                result = FastbootProtocol.ParseResponse(response, response.Length);
                _logDetail($"<<< {result}");
            }
            
            return result;
        }
        
        private async Task<byte[]> ReceiveResponseAsync(int timeoutMs, CancellationToken ct)
        {
            byte[] buffer = new byte[FastbootProtocol.MAX_RESPONSE_LENGTH];
            int received = await _transport.ReceiveAsync(buffer, 0, buffer.Length, timeoutMs, ct);
            
            if (received > 0)
            {
                byte[] result = new byte[received];
                Array.Copy(buffer, result, received);
                return result;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get variable value
        /// </summary>
        public async Task<string> GetVariableAsync(string name, CancellationToken ct = default)
        {
            var response = await SendCommandAsync($"{FastbootProtocol.CMD_GETVAR}:{name}", FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            
            if (response.IsSuccess)
            {
                return response.Message;
            }
            
            return null;
        }
        
        /// <summary>
        /// Refresh device information
        /// </summary>
        public async Task RefreshDeviceInfoAsync(CancellationToken ct = default)
        {
            _variables.Clear();
            
            // Try to get all variables using getvar:all
            bool gotAllVars = await TryGetAllVariablesAsync(ct);
            
            // If getvar:all fails or doesn't get enough variables, read important variables individually
            if (!gotAllVars || _variables.Count < 5)
            {
                string[] importantVars = {
                    FastbootProtocol.VAR_PRODUCT,
                    FastbootProtocol.VAR_SERIALNO,
                    FastbootProtocol.VAR_SECURE,
                    FastbootProtocol.VAR_UNLOCKED,
                    FastbootProtocol.VAR_MAX_DOWNLOAD_SIZE,
                    FastbootProtocol.VAR_CURRENT_SLOT,
                    FastbootProtocol.VAR_SLOT_COUNT,
                    FastbootProtocol.VAR_IS_USERSPACE,
                    FastbootProtocol.VAR_VERSION_BOOTLOADER,
                    FastbootProtocol.VAR_VERSION_BASEBAND,
                    FastbootProtocol.VAR_HW_REVISION,
                    FastbootProtocol.VAR_VARIANT
                };
                
                foreach (var varName in importantVars)
                {
                    if (_variables.ContainsKey(varName)) continue;
                    
                    try
                    {
                        string value = await GetVariableAsync(varName, ct);
                        if (!string.IsNullOrEmpty(value))
                        {
                            _variables[varName] = value;
                        }
                    }
                    catch { }
                }
            }
            
            // Parse max-download-size
            if (_variables.TryGetValue(FastbootProtocol.VAR_MAX_DOWNLOAD_SIZE, out string maxDlSize))
            {
                if (maxDlSize.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    _maxDownloadSize = Convert.ToInt64(maxDlSize.Substring(2), 16);
                }
                else if (long.TryParse(maxDlSize, out long size))
                {
                    _maxDownloadSize = size;
                }
            }
            
            _log($"Device: {GetVariableValue(FastbootProtocol.VAR_PRODUCT, "Unknown")}");
            _log($"Serial: {GetVariableValue(FastbootProtocol.VAR_SERIALNO, "Unknown")}");
            _log($"Max download: {_maxDownloadSize / 1024 / 1024} MB");
        }
        
        /// <summary>
        /// Try to get all variables using getvar:all
        /// </summary>
        private async Task<bool> TryGetAllVariablesAsync(CancellationToken ct)
        {
            try
            {
                EnsureConnected();
                
                _logDetail(">>> getvar:all");
                
                byte[] cmdBytes = FastbootProtocol.BuildCommand($"{FastbootProtocol.CMD_GETVAR}:{FastbootProtocol.VAR_ALL}");
                
                // Send command using TransferAsync and get the first response
                byte[] response = await _transport.TransferAsync(cmdBytes, 2000, ct);
                
                if (response == null || response.Length == 0)
                {
                    _logDetail("getvar:all no response");
                    return false;
                }
                
                // Read all responses (INFO messages)
                int timeout = 15000; // 15s timeout
                var startTime = DateTime.Now;
                int varCount = 0;
                
                while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    if (response == null || response.Length == 0) break;
                    
                    var result = FastbootProtocol.ParseResponse(response, response.Length);
                    _logDetail($"<<< {result.Type}: {result.Message}");
                    
                    if (result.IsInfo)
                    {
                        // Parse INFO message format: "key: value" or "(bootloader) key: value"
                        if (ParseVariableFromInfo(result.Message))
                            varCount++;
                    }
                    else if (result.IsSuccess)
                    {
                        // OKAY indicates command finished successfully
                        _logDetail($"getvar:all complete, got {varCount} variables");
                        break;
                    }
                    else if (result.IsFail)
                    {
                        // FAIL indicates command failed
                        _logDetail($"getvar:all failed: {result.Message}");
                        break;
                    }
                    
                    // Continue reading next response
                    response = await ReceiveResponseAsync(1000, ct);
                }
                
                _logDetail($"Total { _variables.Count } variables obtained");
                return _variables.Count > 0;
            }
            catch (Exception ex)
            {
                _logDetail($"getvar:all exception: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Parse variable from INFO message
        /// Supported formats:
        /// - key: value
        /// - partition-size:boot_a: 0x4000000
        /// - is-logical:system_a: yes
        /// - (bootloader) key: value
        /// </summary>
        private bool ParseVariableFromInfo(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            
            string line = message.Trim();
            
            // Remove (bootloader) prefix
            if (line.StartsWith("(bootloader)"))
            {
                line = line.Substring(12).Trim();
            }
            
            // Parse using regex: supports partition-size:xxx: value and key: value formats
            // Format: key: value or prefix:name: value
            // Regex: match "key: value" where key can contain "prefix:name" format
            var match = System.Text.RegularExpressions.Regex.Match(line, 
                @"^([a-zA-Z0-9_-]+(?::[a-zA-Z0-9_-]+)?):\s*(.+)$");
            
            if (match.Success)
            {
                string key = match.Groups[1].Value.Trim().ToLowerInvariant();
                string value = match.Groups[2].Value.Trim();
                
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    _variables[key] = value;
                    return true;
                }
            }
            
            return false;
        }
        
        private string GetVariableValue(string key, string defaultValue = null)
        {
            if (_variables.TryGetValue(key, out string value))
                return value;
            return defaultValue;
        }
        
        #endregion
        
        #region Flashing Operations
        
        /// <summary>
        /// Flash partition
        /// </summary>
        /// <param name="partition">Partition name</param>
        /// <param name="imagePath">Image file path</param>
        /// <param name="progress">Progress callback</param>
        /// <param name="ct">Cancellation token</param>
        public async Task<bool> FlashAsync(string partition, string imagePath, 
            IProgress<FastbootProgressEventArgs> progress = null, CancellationToken ct = default)
        {
            if (!File.Exists(imagePath))
            {
                _log($"File does not exist: {imagePath}");
                return false;
            }
            
            using (var image = new SparseImage(imagePath))
            {
                return await FlashAsync(partition, image, progress, ct);
            }
        }
        
        /// <summary>
        /// Flash partition (from SparseImage)
        /// </summary>
        public async Task<bool> FlashAsync(string partition, SparseImage image,
            IProgress<FastbootProgressEventArgs> progress = null, CancellationToken ct = default)
        {
            EnsureConnected();
            
            long totalSize = image.SparseSize;
            _log($"Flashing {partition}: {totalSize / 1024} KB ({(image.IsSparse ? "Sparse" : "Raw")})");
            
            // If file is larger than max-download-size, it needs to be chunked
            if (totalSize > _maxDownloadSize && !image.IsSparse)
            {
                _log("File is too large, need Resparse");
                // TODO: Implement resparse
                return false;
            }
            
            // Chunked transfer - use max-download-size reported by device (consistent with official fastboot)
            int chunkIndex = 0;
            int totalChunks = 0;
            long totalSent = 0;
            
            // Speed calculation variables
            var speedStopwatch = System.Diagnostics.Stopwatch.StartNew();
            long lastSpeedBytes = 0;
            DateTime lastSpeedTime = DateTime.Now;
            double currentSpeed = 0;
            const int speedUpdateIntervalMs = 200; // Update speed every 200ms
            
            foreach (var chunk in image.SplitForTransfer(_maxDownloadSize))
            {
                ct.ThrowIfCancellationRequested();
                
                if (totalChunks == 0)
                    totalChunks = chunk.TotalChunks;
                
                // Report progress: Sending
                // Progress is always based on bytes sent (0-95%)
                var progressArgs = new FastbootProgressEventArgs
                {
                    Partition = partition,
                    Stage = ProgressStage.Sending,
                    CurrentChunk = chunkIndex + 1,
                    TotalChunks = totalChunks,
                    BytesSent = totalSent,
                    TotalBytes = totalSize,
                    Percent = totalSent * 95.0 / totalSize,
                    SpeedBps = currentSpeed
                };
                
                // Send download command
                var downloadResponse = await SendCommandAsync(
                    $"{FastbootProtocol.CMD_DOWNLOAD}:{chunk.Size:x8}",
                    FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
                
                if (!downloadResponse.IsData)
                {
                    _log($"Download failed: {downloadResponse.Message}");
                    return false;
                }
                
                // Send data
                long expectedSize = downloadResponse.DataSize;
                if (expectedSize != chunk.Size)
                {
                    _log($"Data size mismatch: expected {expectedSize}, actual {chunk.Size}");
                }
                
                // Send data in chunks
                int offset = 0;
                int blockSize = 64 * 1024; // 64KB block, more frequent updates
                long lastProgressBytes = totalSent;
                const int progressIntervalBytes = 256 * 1024; // Report progress every 256KB
                
                while (offset < chunk.Size)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    int toSend = Math.Min(blockSize, chunk.Size - offset);
                    await _transport.SendAsync(chunk.Data, offset, toSend, ct);
                    
                    offset += toSend;
                    totalSent += toSend;
                    
                    // Calculate real-time speed
                    var now = DateTime.Now;
                    var timeSinceLastSpeedUpdate = (now - lastSpeedTime).TotalMilliseconds;
                    
                    if (timeSinceLastSpeedUpdate >= speedUpdateIntervalMs)
                    {
                        long bytesSinceLastUpdate = totalSent - lastSpeedBytes;
                        currentSpeed = bytesSinceLastUpdate / (timeSinceLastSpeedUpdate / 1000.0);
                        lastSpeedBytes = totalSent;
                        lastSpeedTime = now;
                    }
                    
                    // Report progress every 256KB or at the end of chunk
                    bool isChunkEnd = (offset >= chunk.Size);
                    bool shouldReport = (totalSent - lastProgressBytes) >= progressIntervalBytes || isChunkEnd;
                    
                    if (shouldReport)
                    {
                        lastProgressBytes = totalSent;
                        progressArgs.BytesSent = totalSent;
                        progressArgs.Percent = totalSent * 95.0 / totalSize;
                        progressArgs.SpeedBps = currentSpeed;
                        ReportProgress(progressArgs);
                        progress?.Report(progressArgs);
                    }
                }
                
                // Wait for OKAY
                var dataResponse = await ReceiveResponseAsync(FastbootProtocol.DATA_TIMEOUT_MS, ct);
                if (dataResponse == null)
                {
                    _log("Data transfer timeout");
                    return false;
                }
                
                var dataResult = FastbootProtocol.ParseResponse(dataResponse, dataResponse.Length);
                if (!dataResult.IsSuccess)
                {
                    _log($"Data transfer failed: {dataResult.Message}");
                    return false;
                }
                
                // Send flash command
                progressArgs.Stage = ProgressStage.Writing;
                // Writing stage takes 95-100%
                progressArgs.Percent = 95 + (chunkIndex + 1) * 5.0 / totalChunks;
                ReportProgress(progressArgs);
                progress?.Report(progressArgs);
                
                // Standard Fastboot protocol: flash command is always flash:partition
                string flashCmd = $"{FastbootProtocol.CMD_FLASH}:{partition}";
                
                var flashResponse = await SendCommandAsync(flashCmd, FastbootProtocol.DATA_TIMEOUT_MS, ct);
                
                if (!flashResponse.IsSuccess)
                {
                    _log($"Flash failed: {flashResponse.Message}");
                    return false;
                }
                
                chunkIndex++;
            }
            
            // Finish
            var completeArgs = new FastbootProgressEventArgs
            {
                Partition = partition,
                Stage = ProgressStage.Complete,
                CurrentChunk = totalChunks,
                TotalChunks = totalChunks,
                BytesSent = totalSize,
                TotalBytes = totalSize,
                Percent = 100
            };
            ReportProgress(completeArgs);
            progress?.Report(completeArgs);
            
            _log($"Flash {partition} complete");
            return true;
        }
        
        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> EraseAsync(string partition, CancellationToken ct = default)
        {
            EnsureConnected();
            
            _log($"Erasing {partition}...");
            
            var response = await SendCommandAsync(
                $"{FastbootProtocol.CMD_ERASE}:{partition}",
                FastbootProtocol.DATA_TIMEOUT_MS, ct);
            
            if (response.IsSuccess)
            {
                _log($"Erase {partition} complete");
                return true;
            }
            
            _log($"Erase failed: {response.Message}");
            return false;
        }
        
        #endregion
        
        #region Reboot Operations
        
        /// <summary>
        /// Reboot to system
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            _log("Rebooting to system...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_REBOOT, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            Disconnect();
            return response.IsSuccess;
        }
        
        /// <summary>
        /// Reboot to Bootloader
        /// </summary>
        public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        {
            _log("Rebooting to Bootloader...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_REBOOT_BOOTLOADER, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            return response.IsSuccess;
        }
        
        /// <summary>
        /// Reboot to Fastbootd
        /// </summary>
        public async Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
        {
            _log("Rebooting to Fastbootd...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_REBOOT_FASTBOOT, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            return response.IsSuccess;
        }
        
        /// <summary>
        /// Reboot to Recovery
        /// </summary>
        public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        {
            _log("Rebooting to Recovery...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_REBOOT_RECOVERY, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            Disconnect();
            return response.IsSuccess;
        }
        
        #endregion
        
        #region Unlock/Lock
        
        /// <summary>
        /// Unlock Bootloader
        /// </summary>
        public async Task<bool> UnlockAsync(CancellationToken ct = default)
        {
            _log("Unlocking Bootloader...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_FLASHING_UNLOCK, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            return response.IsSuccess;
        }
        
        /// <summary>
        /// Lock Bootloader
        /// </summary>
        public async Task<bool> LockAsync(CancellationToken ct = default)
        {
            _log("Locking Bootloader...");
            var response = await SendCommandAsync(FastbootProtocol.CMD_FLASHING_LOCK, FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            return response.IsSuccess;
        }
        
        #endregion
        
        #region A/B Slots
        
        /// <summary>
        /// Set active slot
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default)
        {
            _log($"Set active slot: {slot}");
            var response = await SendCommandAsync(
                $"{FastbootProtocol.CMD_SET_ACTIVE}:{slot}",
                FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
            return response.IsSuccess;
        }
        
        /// <summary>
        /// Get current slot
        /// </summary>
        public async Task<string> GetCurrentSlotAsync(CancellationToken ct = default)
        {
            return await GetVariableAsync(FastbootProtocol.VAR_CURRENT_SLOT, ct);
        }
        
        #endregion
        
        #region OEM Commands
        
        /// <summary>
        /// Execute OEM command
        /// </summary>
        public async Task<FastbootResponse> OemCommandAsync(string command, CancellationToken ct = default)
        {
            return await SendCommandAsync($"{FastbootProtocol.CMD_OEM} {command}", FastbootProtocol.DEFAULT_TIMEOUT_MS, ct);
        }
        
        #endregion
        
        #region Helper Methods
        
        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Device not connected");
        }
        
        private void ReportProgress(FastbootProgressEventArgs args)
        {
            ProgressChanged?.Invoke(this, args);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Progress Stage
    /// </summary>
    public enum ProgressStage
    {
        Idle,
        Sending,
        Writing,
        Complete,
        Failed
    }
    
    /// <summary>
    /// Progress Event Arguments
    /// </summary>
    public class FastbootProgressEventArgs : EventArgs
    {
        public string Partition { get; set; }
        public ProgressStage Stage { get; set; }
        public int CurrentChunk { get; set; }
        public int TotalChunks { get; set; }
        public long BytesSent { get; set; }
        public long TotalBytes { get; set; }
        public double Percent { get; set; }
        public double SpeedBps { get; set; }
        public string Message { get; set; }
        
        public string PercentFormatted => $"{Percent:F1}%";
        
        public string SpeedFormatted
        {
            get
            {
                if (SpeedBps >= 1024 * 1024)
                    return $"{SpeedBps / 1024 / 1024:F2} MB/s";
                if (SpeedBps >= 1024)
                    return $"{SpeedBps / 1024:F2} KB/s";
                return $"{SpeedBps:F0} B/s";
            }
        }
    }
}
