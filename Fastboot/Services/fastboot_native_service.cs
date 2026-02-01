
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Fastboot.Image;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Protocol;
using LoveAlways.Fastboot.Transport;

namespace LoveAlways.Fastboot.Services
{
    /// <summary>
    /// Fastboot Native Service
    /// Pure C# implementation of Fastboot protocol, no dependency on external fastboot.exe
    /// 
    /// Advantages:
    /// - Real-time progress percentage callback
    /// - Complete control over the transfer process
    /// - No external dependencies
    /// - Better error handling
    /// </summary>
    public class FastbootNativeService : IDisposable
    {
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        
        private FastbootClient _client;
        private bool _disposed;
        
        /// <summary>
        /// Whether connected
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;
        
        /// <summary>
        /// Current device serial number
        /// </summary>
        public string CurrentSerial => _client?.Serial;
        
        /// <summary>
        /// Device information
        /// </summary>
        public FastbootDeviceInfo DeviceInfo { get; private set; }
        
        /// <summary>
        /// Progress update event
        /// </summary>
        public event EventHandler<FastbootNativeProgressEventArgs> ProgressChanged;
        
        public FastbootNativeService(Action<string> log, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _logDetail = logDetail ?? (msg => { });
        }
        
        #region Device Operations
        
        /// <summary>
        /// Get all Fastboot devices
        /// </summary>
        public List<FastbootDeviceListItem> GetDevices()
        {
            var nativeDevices = FastbootClient.GetDevices();
            
            return nativeDevices.Select(d => new FastbootDeviceListItem
            {
                Serial = d.Serial ?? $"{d.VendorId:X4}:{d.ProductId:X4}",
                Status = "fastboot"
            }).ToList();
        }
        
        /// <summary>
        /// Connect to device
        /// </summary>
        public async Task<bool> ConnectAsync(string serial, CancellationToken ct = default)
        {
            Disconnect();
            
            _client = new FastbootClient(_log, _logDetail);
            _client.ProgressChanged += OnClientProgressChanged;
            
            // Find device
            var devices = FastbootClient.GetDevices();
            var device = devices.FirstOrDefault(d => 
                d.Serial == serial || 
                $"{d.VendorId:X4}:{d.ProductId:X4}" == serial);
            
            if (device == null)
            {
                _log($"Device not found: {serial}");
                return false;
            }
            
            bool success = await _client.ConnectAsync(device, ct);
            
            if (success)
            {
                // Build device info
                DeviceInfo = BuildDeviceInfo();
            }
            
            return success;
        }
        
        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            if (_client != null)
            {
                _client.ProgressChanged -= OnClientProgressChanged;
                _client.Disconnect();
                _client.Dispose();
                _client = null;
            }
            DeviceInfo = null;
        }
        
        /// <summary>
        /// Refresh device information
        /// </summary>
        public async Task<bool> RefreshDeviceInfoAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            
            await _client.RefreshDeviceInfoAsync(ct);
            DeviceInfo = BuildDeviceInfo();
            
            return true;
        }
        
        private FastbootDeviceInfo BuildDeviceInfo()
        {
            if (_client?.Variables == null) return null;
            
            var info = new FastbootDeviceInfo();
            
            // Copy all variables to RawVariables and parse partition info
            foreach (var kv in _client.Variables)
            {
                string key = kv.Key.ToLowerInvariant();
                string value = kv.Value;
                
                info.RawVariables[key] = value;
                
                // Parse partition size: partition-size:boot_a: 0x4000000
                if (key.StartsWith("partition-size:"))
                {
                    string partName = key.Substring("partition-size:".Length);
                    if (TryParseHexOrDecimal(value, out long size))
                    {
                        info.PartitionSizes[partName] = size;
                    }
                }
                // Parse logical partition: is-logical:system_a: yes
                else if (key.StartsWith("is-logical:"))
                {
                    string partName = key.Substring("is-logical:".Length);
                    info.PartitionIsLogical[partName] = value.ToLower() == "yes";
                }
            }
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_PRODUCT, out string product))
                info.Product = product;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_SERIALNO, out string serial))
                info.Serial = serial;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_SECURE, out string secure))
                info.SecureBoot = secure.ToLower() == "yes";
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_UNLOCKED, out string unlocked))
                info.Unlocked = unlocked.ToLower() == "yes";
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_CURRENT_SLOT, out string slot))
                info.CurrentSlot = slot;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_IS_USERSPACE, out string userspace))
                info.IsFastbootd = userspace.ToLower() == "yes";
            
            // Parse other device info
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VERSION_BOOTLOADER, out string blVersion))
                info.BootloaderVersion = blVersion;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VERSION_BASEBAND, out string bbVersion))
                info.BasebandVersion = bbVersion;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_HW_REVISION, out string hwRev))
                info.HardwareVersion = hwRev;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VARIANT, out string variant))
                info.Variant = variant;
            
            info.MaxDownloadSize = _client.MaxDownloadSize;
            
            return info;
        }
        
        /// <summary>
        /// Try to parse hex or decimal number
        /// </summary>
        private static bool TryParseHexOrDecimal(string value, out long result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;

            value = value.Trim();
            
            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    result = Convert.ToInt64(value.Substring(2), 16);
                    return true;
                }
                else
                {
                    return long.TryParse(value, out result);
                }
            }
            catch
            {
                return false;
            }
        }
        
        #endregion
        
        #region Flashing Operations
        
        /// <summary>
        /// Flash partition
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partition, string imagePath, 
            bool disableVerity = false, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                _log("Device not connected");
                return false;
            }
            
            if (!File.Exists(imagePath))
            {
                _log($"File does not exist: {imagePath}");
                return false;
            }
            
            var progress = new Progress<FastbootProgressEventArgs>(args =>
            {
                ReportProgress(new FastbootNativeProgressEventArgs
                {
                    Partition = args.Partition,
                    Stage = args.Stage.ToString(),
                    CurrentChunk = args.CurrentChunk,
                    TotalChunks = args.TotalChunks,
                    BytesSent = args.BytesSent,
                    TotalBytes = args.TotalBytes,
                    Percent = args.Percent,
                    SpeedBps = args.SpeedBps
                });
            });
            
            try
            {
                return await _client.FlashAsync(partition, imagePath, progress, ct);
            }
            catch (OutOfMemoryException ex)
            {
                _log($"Insufficient memory, unable to flash {partition}: File is too large, please try closing other programs and try again");
                _logDetail($"OutOfMemoryException: {ex.Message}");
                
                // Try to free memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                return false;
            }
        }
        
        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partition, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                _log("Device not connected");
                return false;
            }
            
            return await _client.EraseAsync(partition, ct);
        }
        
        /// <summary>
        /// Batch flash partitions
        /// </summary>
        public async Task<int> FlashPartitionsBatchAsync(
            List<Tuple<string, string>> partitions, 
            CancellationToken ct = default)
        {
            int success = 0;
            int total = partitions.Count;
            
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var (partName, imagePath) = partitions[i];
                
                // Report overall progress
                ReportProgress(new FastbootNativeProgressEventArgs
                {
                    Partition = partName,
                    Stage = "Preparing",
                    CurrentChunk = i + 1,
                    TotalChunks = total,
                    Percent = i * 100.0 / total
                });
                
                if (await FlashPartitionAsync(partName, imagePath, false, ct))
                {
                    success++;
                }
            }
            
            return success;
        }
        
        #endregion
        
        #region Reboot Operations
        
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootAsync(ct);
        }
        
        public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootBootloaderAsync(ct);
        }
        
        public async Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootFastbootdAsync(ct);
        }
        
        public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootRecoveryAsync(ct);
        }
        
        #endregion
        
        #region Unlock/Lock
        
        public async Task<bool> UnlockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.UnlockAsync(ct);
        }
        
        public async Task<bool> LockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.LockAsync(ct);
        }
        
        #endregion
        
        #region A/B Slots
        
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.SetActiveSlotAsync(slot, ct);
        }
        
        public async Task<bool> SwitchSlotAsync(CancellationToken ct = default)
        {
            if (!IsConnected || DeviceInfo == null) return false;
            
            string currentSlot = DeviceInfo.CurrentSlot;
            string newSlot = currentSlot == "a" ? "b" : "a";
            
            return await SetActiveSlotAsync(newSlot, ct);
        }
        
        public async Task<string> GetCurrentSlotAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            
            // Prefer cached device info
            if (DeviceInfo != null && !string.IsNullOrEmpty(DeviceInfo.CurrentSlot))
                return DeviceInfo.CurrentSlot;
            
            // Otherwise query directly
            return await _client.GetVariableAsync("current-slot", ct);
        }
        
        #endregion
        
        #region Variable Operations
        
        public async Task<string> GetVariableAsync(string name, CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            return await _client.GetVariableAsync(name, ct);
        }
        
        #endregion
        
        #region OEM Commands
        
        public async Task<string> ExecuteOemCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            var response = await _client.OemCommandAsync(command, ct);
            return response?.Message;
        }
        
        #endregion
        
        #region Helper Methods
        
        private void OnClientProgressChanged(object sender, FastbootProgressEventArgs e)
        {
            ReportProgress(new FastbootNativeProgressEventArgs
            {
                Partition = e.Partition,
                Stage = e.Stage.ToString(),
                CurrentChunk = e.CurrentChunk,
                TotalChunks = e.TotalChunks,
                BytesSent = e.BytesSent,
                TotalBytes = e.TotalBytes,
                Percent = e.Percent,
                SpeedBps = e.SpeedBps
            });
        }
        
        private void ReportProgress(FastbootNativeProgressEventArgs args)
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
    /// Native Fastboot Progress Event Arguments
    /// </summary>
    public class FastbootNativeProgressEventArgs : EventArgs
    {
        public string Partition { get; set; }
        public string Stage { get; set; }
        public int CurrentChunk { get; set; }
        public int TotalChunks { get; set; }
        public long BytesSent { get; set; }
        public long TotalBytes { get; set; }
        public double Percent { get; set; }
        public double SpeedBps { get; set; }
        
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
        
        public string StatusText
        {
            get
            {
                if (TotalChunks > 1)
                {
                    return $"{Stage} '{Partition}' ({CurrentChunk}/{TotalChunks}) {PercentFormatted}";
                }
                return $"{Stage} '{Partition}' {PercentFormatted}";
            }
        }
    }
}
