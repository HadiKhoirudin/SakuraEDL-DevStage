
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Common;
using LoveAlways.Fastboot.Common;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Services
{
    /// <summary>
    /// Fastboot Service Layer
    /// Implemented using native C# protocol, no dependency on external fastboot.exe
    /// </summary>
    public class FastbootService : IDisposable
    {
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<int, int> _progress;

        private FastbootNativeService _nativeService;
        private bool _disposed;

        // Watchdog
        private Watchdog _watchdog;

        /// <summary>
        /// Current connected device serial number
        /// </summary>
        public string CurrentSerial => _nativeService?.CurrentSerial;

        /// <summary>
        /// Current device information
        /// </summary>
        public FastbootDeviceInfo DeviceInfo => _nativeService?.DeviceInfo;

        /// <summary>
        /// Whether a device is connected
        /// </summary>
        public bool IsConnected => _nativeService?.IsConnected ?? false;

        /// <summary>
        /// Flashing progress event
        /// </summary>
        public event Action<FlashProgress> FlashProgressChanged;

        public FastbootService(Action<string> log, Action<int, int> progress = null, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _progress = progress;
            _logDetail = logDetail ?? (msg => { });

            // Initialize watchdog
            _watchdog = new Watchdog("Fastboot", WatchdogManager.DefaultTimeouts.Fastboot, _logDetail);
            _watchdog.OnTimeout += OnWatchdogTimeout;
        }

        /// <summary>
        /// Watchdog timeout handler
        /// </summary>
        private void OnWatchdogTimeout(object sender, WatchdogTimeoutEventArgs e)
        {
            _log($"[Fastboot] Watchdog timeout: {e.OperationName}");

            if (e.TimeoutCount >= 2)
            {
                _log("[Fastboot] Multiple timeouts, disconnecting");
                e.ShouldReset = false;
                Disconnect();
            }
        }

        /// <summary>
        /// Feed watchdog
        /// </summary>
        public void FeedWatchdog() => _watchdog?.Feed();

        /// <summary>
        /// Start watchdog
        /// </summary>
        public void StartWatchdog(string operation) => _watchdog?.Start(operation);

        /// <summary>
        /// Stop watchdog
        /// </summary>
        public void StopWatchdog() => _watchdog?.Stop();

        #region Device Detection

        /// <summary>
        /// Get Fastboot device list (using native protocol)
        /// </summary>
        public Task<List<FastbootDeviceListItem>> GetDevicesAsync(CancellationToken ct = default)
        {
            var devices = new List<FastbootDeviceListItem>();

            try
            {
                // Use native USB enumeration
                var nativeDevices = FastbootClient.GetDevices();

                foreach (var device in nativeDevices)
                {
                    devices.Add(new FastbootDeviceListItem
                    {
                        Serial = device.Serial ?? $"{device.VendorId:X4}:{device.ProductId:X4}",
                        Status = "fastboot"
                    });
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] Failed to get device list: {ex.Message}");
            }

            return Task.FromResult(devices);
        }

        /// <summary>
        /// Select device and get device info
        /// </summary>
        public async Task<bool> SelectDeviceAsync(string serial, CancellationToken ct = default)
        {
            _log($"[Fastboot] Selecting device: {serial}");

            // Disconnect old connection
            Disconnect();

            // Create new native service
            _nativeService = new FastbootNativeService(_log, _logDetail);
            _nativeService.ProgressChanged += OnNativeProgressChanged;

            // Connect to device
            bool success = await _nativeService.ConnectAsync(serial, ct);

            if (success)
            {
                _log($"[Fastboot] Device: {DeviceInfo?.Product ?? "Unknown"}");
                _log($"[Fastboot] Secure Boot: {(DeviceInfo?.SecureBoot == true ? "Enabled" : "Disabled")}");

                if (DeviceInfo?.HasABPartition == true)
                {
                    _log($"[Fastboot] Current Slot: {DeviceInfo.CurrentSlot}");
                }

                _log($"[Fastboot] Fastbootd Mode: {(DeviceInfo?.IsFastbootd == true ? "Yes" : "No")}");
                _log($"[Fastboot] Partition Count: {DeviceInfo?.PartitionSizes?.Count ?? 0}");
            }

            return success;
        }

        /// <summary>
        /// Native progress callback
        /// </summary>
        private void OnNativeProgressChanged(object sender, FastbootNativeProgressEventArgs e)
        {
            // Convert to FlashProgress and trigger event
            var progress = new FlashProgress
            {
                PartitionName = e.Partition,
                Phase = e.Stage,
                CurrentChunk = e.CurrentChunk,
                TotalChunks = e.TotalChunks,
                SizeKB = e.TotalBytes / 1024,
                SpeedKBps = e.SpeedBps / 1024.0,
                Percent = e.Percent  // Pass actual progress value
            };

            FlashProgressChanged?.Invoke(progress);
        }

        /// <summary>
        /// Refresh device information
        /// </summary>
        public async Task<bool> RefreshDeviceInfoAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Reading device info...");
                bool result = await _nativeService.RefreshDeviceInfoAsync(ct);

                if (result && DeviceInfo != null)
                {
                    _log($"[Fastboot] Device: {DeviceInfo.Product ?? "Unknown"}");
                    _log($"[Fastboot] Unlock Status: {(DeviceInfo.Unlocked == true ? "Unlocked" : DeviceInfo.Unlocked == false ? "Locked" : "Unknown")}");
                    _log($"[Fastboot] Fastbootd: {(DeviceInfo.IsFastbootd ? "Yes" : "No")}");
                    if (!string.IsNullOrEmpty(DeviceInfo.CurrentSlot))
                        _log($"[Fastboot] Current Slot: {DeviceInfo.CurrentSlot}");
                    _log($"[Fastboot] Variable Count: {DeviceInfo.RawVariables?.Count ?? 0}");
                    _log($"[Fastboot] Partition Count: {DeviceInfo.PartitionSizes?.Count ?? 0}");

                    // Hint about bootloader mode limitations
                    if (!DeviceInfo.IsFastbootd && DeviceInfo.PartitionSizes?.Count == 0)
                    {
                        _log("[Fastboot] Hint: Bootloader mode does not support reading partition list. Enter Fastbootd mode to view them.");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to read device info: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnect device
        /// </summary>
        public void Disconnect()
        {
            if (_nativeService != null)
            {
                _nativeService.ProgressChanged -= OnNativeProgressChanged;
                _nativeService.Disconnect();
                _nativeService.Dispose();
                _nativeService = null;
            }
        }

        #endregion

        #region Partition Operations

        /// <summary>
        /// Flash partition
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partitionName, string imagePath,
            bool disableVerity = false, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            if (!File.Exists(imagePath))
            {
                _log($"[Fastboot] Image file does not exist: {imagePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(imagePath);
                _log($"[Fastboot] Flashing {partitionName} ({FormatSize(fileInfo.Length)})...");

                bool result = await _nativeService.FlashPartitionAsync(partitionName, imagePath, disableVerity, ct);

                if (result)
                {
                    _log($"[Fastboot] {partitionName} flashed successfully");
                }
                else
                {
                    _log($"[Fastboot] {partitionName} flashing failed");
                }

                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Flashing exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Format file size
        /// </summary>
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

        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log($"[Fastboot] Erasing {partitionName}...");

                bool result = await _nativeService.ErasePartitionAsync(partitionName, ct);

                if (result)
                {
                    _log($"[Fastboot] {partitionName} erased successfully");
                }
                else
                {
                    _log($"[Fastboot] {partitionName} erase failed");
                }

                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Erase exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Batch flash partitions
        /// </summary>
        public async Task<int> FlashPartitionsAsync(List<Tuple<string, string>> partitions, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return 0;
            }

            int success = 0;
            int total = partitions.Count;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (partName, imagePath) = partitions[i];

                _progress?.Invoke(i, total);

                if (await FlashPartitionAsync(partName, imagePath, false, ct))
                {
                    success++;
                }
            }

            _progress?.Invoke(total, total);
            return success;
        }

        #endregion

        #region Reboot Operations

        /// <summary>
        /// Reboot to system
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Rebooting...");
                return await _nativeService.RebootAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Reboot failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reboot to Bootloader
        /// </summary>
        public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Rebooting to Bootloader...");
                return await _nativeService.RebootBootloaderAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Reboot failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reboot to Recovery
        /// </summary>
        public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Rebooting to Recovery...");
                return await _nativeService.RebootRecoveryAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Reboot failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reboot to Fastbootd
        /// </summary>
        public async Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Rebooting to Fastbootd...");
                return await _nativeService.RebootFastbootdAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Reboot failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Bootloader Unlock/Lock

        /// <summary>
        /// Unlock Bootloader
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Unlocking Bootloader...");
                return await _nativeService.UnlockBootloaderAsync(method, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Unlock failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Lock Bootloader
        /// </summary>
        public async Task<bool> LockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Locking Bootloader...");
                return await _nativeService.LockBootloaderAsync(method, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Lock failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region A/B Slot

        /// <summary>
        /// Set active slot
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log($"[Fastboot] Setting active slot: {slot}...");
                return await _nativeService.SetActiveSlotAsync(slot, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to set slot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Switch A/B slot
        /// </summary>
        public async Task<bool> SwitchSlotAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                return await _nativeService.SwitchSlotAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to switch slot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get current slot
        /// </summary>
        public async Task<string> GetCurrentSlotAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return null;
            }

            try
            {
                return await _nativeService.GetCurrentSlotAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to get slot: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region OEM Commands

        /// <summary>
        /// Execute OEM command
        /// </summary>
        public async Task<string> ExecuteOemCommandAsync(string command, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return null;
            }

            try
            {
                _log($"[Fastboot] Executing OEM: {command}");
                return await _nativeService.ExecuteOemCommandAsync(command, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] OEM command failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// OEM EDL - Xiaomi kick to EDL (fastboot oem edl)
        /// </summary>
        public async Task<bool> OemEdlAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Executing OEM EDL...");
                string result = await _nativeService.ExecuteOemCommandAsync("edl", ct);
                return result != null;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] OEM EDL failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Erase FRP partition (Google Lock)
        /// </summary>
        public async Task<bool> EraseFrpAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Erasing FRP partition...");
                return await _nativeService.ErasePartitionAsync("frp", ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Erase FRP failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get variable value
        /// </summary>
        public async Task<string> GetVariableAsync(string name, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                return null;
            }

            try
            {
                return await _nativeService.GetVariableAsync(name, ct);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Execute arbitrary command (used for shortcut command feature)
        /// </summary>
        public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return null;
            }

            try
            {
                _log($"[Fastboot] Executing: {command}");
                string result = null;

                // Parse command
                if (command.StartsWith("getvar ", StringComparison.OrdinalIgnoreCase))
                {
                    string varName = command.Substring(7).Trim();
                    result = await _nativeService.GetVariableAsync(varName, ct);
                    _log($"[Fastboot] {varName}: {result ?? "(Empty)"}");
                }
                else if (command.StartsWith("oem ", StringComparison.OrdinalIgnoreCase))
                {
                    string oemCmd = command.Substring(4).Trim();
                    result = await _nativeService.ExecuteOemCommandAsync(oemCmd, ct);
                    _log($"[Fastboot] OEM response: {result ?? "OKAY"}");
                }
                else if (command == "reboot")
                {
                    await _nativeService.RebootAsync(ct);
                    _log("[Fastboot] Device is rebooting...");
                    return "OKAY";
                }
                else if (command == "reboot-bootloader" || command == "reboot bootloader")
                {
                    await _nativeService.RebootBootloaderAsync(ct);
                    _log("[Fastboot] Device is rebooting to Bootloader...");
                    return "OKAY";
                }
                else if (command == "reboot-recovery" || command == "reboot recovery")
                {
                    await _nativeService.RebootRecoveryAsync(ct);
                    _log("[Fastboot] Device is rebooting to Recovery...");
                    return "OKAY";
                }
                else if (command == "reboot-fastboot" || command == "reboot fastboot")
                {
                    await _nativeService.RebootFastbootdAsync(ct);
                    _log("[Fastboot] Device is rebooting to Fastbootd...");
                    return "OKAY";
                }
                else if (command == "devices" || command == "device")
                {
                    // Display current connected device info
                    var info = DeviceInfo;
                    if (info != null)
                    {
                        string deviceInfo = $"{info.Serial ?? "Unknown"}\tfastboot";
                        _log($"[Fastboot] {deviceInfo}");
                        return deviceInfo;
                    }
                    return "Device not connected";
                }
                else if (command.StartsWith("erase ", StringComparison.OrdinalIgnoreCase))
                {
                    string partition = command.Substring(6).Trim();
                    bool success = await _nativeService.ErasePartitionAsync(partition, ct);
                    result = success ? "OKAY" : "FAILED";
                    _log($"[Fastboot] Erasing {partition}: {result}");
                }
                else if (command == "flashing unlock")
                {
                    result = await UnlockBootloaderAsync("flashing unlock", ct) ? "OKAY" : "FAILED";
                }
                else if (command == "flashing lock")
                {
                    result = await LockBootloaderAsync("flashing lock", ct) ? "OKAY" : "FAILED";
                }
                else if (command.StartsWith("set_active ", StringComparison.OrdinalIgnoreCase))
                {
                    string slot = command.Substring(11).Trim();
                    bool success = await SetActiveSlotAsync(slot, ct);
                    result = success ? "OKAY" : "FAILED";
                    _log($"[Fastboot] Set active slot {slot}: {result}");
                }
                else
                {
                    // Other commands are executed as OEM commands
                    result = await _nativeService.ExecuteOemCommandAsync(command, ct);
                    _log($"[Fastboot] Response: {result ?? "OKAY"}");
                }

                return result ?? "OKAY";
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Command execution failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region OnePlus/OPPO Dynamic Partition Operations

        /// <summary>
        /// OnePlus/OPPO logical partition list
        /// </summary>
        public static readonly HashSet<string> LogicalPartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "odm", "vendor", "product", "system_ext", "system_dlkm", "vendor_dlkm", "odm_dlkm",
            "my_bigball", "my_carrier", "my_company", "my_engineering", "my_heytap", "my_manifest",
            "my_preload", "my_product", "my_region", "my_stock"
        };

        /// <summary>
        /// Detect if in FastbootD mode
        /// </summary>
        public async Task<bool> IsFastbootdModeAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
                return false;

            try
            {
                // Check is-userspace variable or super-partition-name
                string isUserspace = await _nativeService.GetVariableAsync("is-userspace", ct);
                if (!string.IsNullOrEmpty(isUserspace) && isUserspace.ToLower() == "yes")
                    return true;

                string superPartition = await _nativeService.GetVariableAsync("super-partition-name", ct);
                return !string.IsNullOrEmpty(superPartition);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detect device platform type
        /// Qualcomm devices: bootloader contains "abl"
        /// MediaTek devices: bootloader contains "lk"
        /// </summary>
        public enum DevicePlatform
        {
            Unknown,
            Qualcomm,   // Qualcomm (abl)
            MediaTek    // MediaTek (lk)
        }

        /// <summary>
        /// Get device platform type
        /// </summary>
        public async Task<DevicePlatform> GetDevicePlatformAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
                return DevicePlatform.Unknown;

            try
            {
                // Get bootloader version info
                string bootloader = await _nativeService.GetVariableAsync("version-bootloader", ct);
                if (string.IsNullOrEmpty(bootloader))
                {
                    bootloader = await _nativeService.GetVariableAsync("bootloader-version", ct);
                }

                if (!string.IsNullOrEmpty(bootloader))
                {
                    string bl = bootloader.ToLower();
                    // Qualcomm devices use ABL (Android Boot Loader)
                    if (bl.Contains("abl"))
                        return DevicePlatform.Qualcomm;
                    // MediaTek devices use LK (Little Kernel)
                    if (bl.Contains("lk"))
                        return DevicePlatform.MediaTek;
                }

                // Fallback detection: via product or hardware information
                string hardware = await _nativeService.GetVariableAsync("hw-revision", ct);
                string product = DeviceInfo?.Product ?? await _nativeService.GetVariableAsync("product", ct);

                if (!string.IsNullOrEmpty(product))
                {
                    string p = product.ToLower();
                    // Common Qualcomm chipset prefixes
                    if (p.Contains("sdm") || p.Contains("sm") || p.Contains("msm") || p.Contains("qcom") || p.Contains("snapdragon"))
                        return DevicePlatform.Qualcomm;
                    // Common MediaTek chipset prefixes
                    if (p.Contains("mt") || p.Contains("mtk") || p.Contains("mediatek") || p.Contains("helio") || p.Contains("dimensity"))
                        return DevicePlatform.MediaTek;
                }

                return DevicePlatform.Unknown;
            }
            catch
            {
                return DevicePlatform.Unknown;
            }
        }

        /// <summary>
        /// Detect if it's a Qualcomm device
        /// </summary>
        public async Task<bool> IsQualcommDeviceAsync(CancellationToken ct = default)
        {
            var platform = await GetDevicePlatformAsync(ct);
            return platform == DevicePlatform.Qualcomm;
        }

        /// <summary>
        /// Detect if it's a MediaTek device
        /// </summary>
        public async Task<bool> IsMediaTekDeviceAsync(CancellationToken ct = default)
        {
            var platform = await GetDevicePlatformAsync(ct);
            return platform == DevicePlatform.MediaTek;
        }

        /// <summary>
        /// Delete logical partition
        /// </summary>
        public async Task<bool> DeleteLogicalPartitionAsync(string partitionName, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log($"[Fastboot] Deleting logical partition: {partitionName}");
                string response = await _nativeService.ExecuteOemCommandAsync($"delete-logical-partition {partitionName}", ct);
                bool success = response == null || !response.ToLower().Contains("fail");
                return success;
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] Failed to delete logical partition: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create logical partition
        /// </summary>
        public async Task<bool> CreateLogicalPartitionAsync(string partitionName, long size = 0, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log($"[Fastboot] Creating logical partition: {partitionName} (Size: {size})");
                string response = await _nativeService.ExecuteOemCommandAsync($"create-logical-partition {partitionName} {size}", ct);
                bool success = response == null || !response.ToLower().Contains("fail");
                return success;
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] Failed to create logical partition: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete COW snapshot partitions (used for OTA update recovery)
        /// </summary>
        public async Task<bool> DeleteCowPartitionsAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Deleting COW snapshot partitions...");

                // COW partition naming rules: PartitionName_cow, PartitionName_cow-img
                var cowSuffixes = new[] { "_cow", "_cow-img" };
                int deletedCount = 0;

                foreach (var basePart in LogicalPartitions)
                {
                    foreach (var suffix in new[] { "_a", "_b" })
                    {
                        foreach (var cowSuffix in cowSuffixes)
                        {
                            string cowPartName = $"{basePart}{suffix}{cowSuffix}";
                            try
                            {
                                string response = await _nativeService.ExecuteOemCommandAsync($"delete-logical-partition {cowPartName}", ct);
                                if (response == null || !response.ToLower().Contains("fail"))
                                {
                                    deletedCount++;
                                    _logDetail($"[Fastboot] Deleted COW partition: {cowPartName}");
                                }
                            }
                            catch
                            {
                                // Ignore non-existent partitions
                            }
                        }
                    }
                }

                _log($"[Fastboot] COW snapshot partition cleanup complete, deleted {deletedCount}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to delete COW partitions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Flash partition to specified slot
        /// </summary>
        public async Task<bool> FlashPartitionToSlotAsync(string partitionName, string imagePath, string slot,
            Action<long, long> progressCallback = null, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            if (!File.Exists(imagePath))
            {
                _log($"[Fastboot] Image file does not exist: {imagePath}");
                return false;
            }

            try
            {
                // Build partition name with slot
                string targetPartition = $"{partitionName}_{slot}";

                var fileInfo = new FileInfo(imagePath);
                _log($"[Fastboot] Flashing {Path.GetFileName(imagePath)} -> {targetPartition} ({FormatSize(fileInfo.Length)})");

                // Subscribe to progress events
                EventHandler<FastbootNativeProgressEventArgs> handler = null;
                if (progressCallback != null)
                {
                    handler = (s, e) => progressCallback(e.BytesSent, e.TotalBytes);
                    _nativeService.ProgressChanged += handler;
                }

                try
                {
                    bool result = await _nativeService.FlashPartitionAsync(targetPartition, imagePath, false, ct);

                    if (result)
                    {
                        _logDetail($"[Fastboot] {targetPartition} flashed successfully");
                    }
                    else
                    {
                        _log($"[Fastboot] {targetPartition} flashing failed");
                    }

                    return result;
                }
                finally
                {
                    if (handler != null)
                    {
                        _nativeService.ProgressChanged -= handler;
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Flashing exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rebuild logical partition structure (for A/B cross-flashing)
        /// </summary>
        public async Task<bool> RebuildLogicalPartitionsAsync(string targetSlot, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log($"[Fastboot] Rebuilding logical partition structure (Target slot: {targetSlot})...");

                // Delete all A/B logical partitions
                foreach (var name in LogicalPartitions)
                {
                    await DeleteLogicalPartitionAsync($"{name}_a", ct);
                    await DeleteLogicalPartitionAsync($"{name}_b", ct);
                }

                // Create logical partitions only for the target slot (size 0, will be adjusted automatically during flashing)
                foreach (var name in LogicalPartitions)
                {
                    string targetName = $"{name}_{targetSlot}";
                    await CreateLogicalPartitionAsync(targetName, 0, ct);
                }

                _log("[Fastboot] Logical partition structure rebuild complete");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Failed to rebuild logical partitions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wipe user data (userdata + metadata + -w)
        /// </summary>
        public async Task<bool> WipeDataAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] Device not connected");
                return false;
            }

            try
            {
                _log("[Fastboot] Wiping user data...");

                // 1. erase userdata
                bool eraseUserdata = await _nativeService.ErasePartitionAsync("userdata", ct);
                _logDetail($"[Fastboot] Erase userdata: {(eraseUserdata ? "Success" : "Failed")}");

                // 2. erase metadata
                bool eraseMetadata = await _nativeService.ErasePartitionAsync("metadata", ct);
                _logDetail($"[Fastboot] Erase metadata: {(eraseMetadata ? "Success" : "Failed")}");

                // 3. Format userdata (-w equivalent)
                // Note: Native protocol might not support -w, need to implement via format command

                bool success = eraseUserdata || eraseMetadata;
                _log(success ? "[Fastboot] User data wipe complete" : "[Fastboot] User data wipe failed");

                return success;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] Data wipe failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if partition is a logical partition
        /// </summary>
        public static bool IsLogicalPartition(string partitionName)
        {
            // Remove slot suffix
            string baseName = partitionName;
            if (baseName.EndsWith("_a") || baseName.EndsWith("_b"))
            {
                baseName = baseName.Substring(0, baseName.Length - 2);
            }
            return LogicalPartitions.Contains(baseName);
        }

        /// <summary>
        /// Check if it's a Modem partition (special handling for Qualcomm devices)
        /// </summary>
        public static bool IsModemPartition(string partitionName)
        {
            string name = partitionName.ToLower();
            return name.Contains("modem") || name == "radio";
        }

        #endregion

        #region IDisposable

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
}
