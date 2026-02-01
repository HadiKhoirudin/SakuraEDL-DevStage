// ============================================================================
// LoveAlways - Spreadtrum UI Controller
// Spreadtrum/Unisoc UI Controller
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Spreadtrum.Common;
using LoveAlways.Spreadtrum.Database;
using LoveAlways.Spreadtrum.Exploit;
using LoveAlways.Spreadtrum.Protocol;
using LoveAlways.Spreadtrum.Services;

namespace LoveAlways.Spreadtrum.UI
{
    /// <summary>
    /// Spreadtrum UI Controller
    /// </summary>
    public class SpreadtrumUIController : IDisposable
    {
        private readonly SpreadtrumService _service;
        private readonly Action<string, Color> _logCallback;
        private readonly Action<string> _detailLogCallback;
        private CancellationTokenSource _operationCts;

        // Events
        public event Action<int, int> OnProgress;
        public event Action<SprdDeviceState> OnStateChanged;
        public event Action<SprdDeviceInfo> OnDeviceConnected;
        public event Action<SprdDeviceInfo> OnDeviceDisconnected;
        public event Action<PacInfo> OnPacLoaded;
        public event Action<List<SprdPartitionInfo>> OnPartitionTableLoaded;

        // Properties
        public bool IsConnected => _service.IsConnected;
        public bool IsBromMode => _service.IsBromMode;
        public FdlStage CurrentStage => _service.CurrentStage;
        public SprdDeviceState State => _service.State;
        public PacInfo CurrentPac => _service.CurrentPac;

        public SpreadtrumUIController(Action<string, Color> logCallback, Action<string> detailLogCallback = null)
        {
            _logCallback = logCallback;
            _detailLogCallback = detailLogCallback;

            _service = new SpreadtrumService();
            _service.OnLog += Log;
            _service.OnProgress += (c, t) => OnProgress?.Invoke(c, t);
            _service.OnStateChanged += state => OnStateChanged?.Invoke(state);
            _service.OnDeviceConnected += dev => OnDeviceConnected?.Invoke(dev);
            _service.OnDeviceDisconnected += dev => OnDeviceDisconnected?.Invoke(dev);
        }

        #region Chip Configuration

        /// <summary>
        /// Set Chip ID (0 means auto detect)
        /// </summary>
        public void SetChipId(uint chipId)
        {
            _service.SetChipId(chipId);
            if (chipId > 0)
            {
                string platform = SprdPlatform.GetPlatformName(chipId);
                Log(string.Format("[Spreadtrum] Chip Config: {0}", platform), Color.Gray);
            }
        }

        /// <summary>
        /// Get Current Chip ID
        /// </summary>
        public uint GetChipId()
        {
            return _service.ChipId;
        }

        #endregion

        #region Custom FDL Configuration

        /// <summary>
        /// Set Custom FDL1 File and Address
        /// </summary>
        public void SetCustomFdl1(string filePath, uint address)
        {
            _service.SetCustomFdl1(filePath, address);
            if (!string.IsNullOrEmpty(filePath) || address > 0)
            {
                string addrStr = address > 0 ? string.Format("0x{0:X}", address) : "Default";
                string fileStr = !string.IsNullOrEmpty(filePath) ? System.IO.Path.GetFileName(filePath) : "PAC Built-in";
                Log(string.Format("[Spreadtrum] FDL1 Config: {0} @ {1}", fileStr, addrStr), Color.Gray);
            }
        }

        /// <summary>
        /// Set Custom FDL2 File and Address
        /// </summary>
        public void SetCustomFdl2(string filePath, uint address)
        {
            _service.SetCustomFdl2(filePath, address);
            if (!string.IsNullOrEmpty(filePath) || address > 0)
            {
                string addrStr = address > 0 ? string.Format("0x{0:X}", address) : "Default";
                string fileStr = !string.IsNullOrEmpty(filePath) ? System.IO.Path.GetFileName(filePath) : "PAC Built-in";
                Log(string.Format("[Spreadtrum] FDL2 Config: {0} @ {1}", fileStr, addrStr), Color.Gray);
            }
        }

        /// <summary>
        /// Clear Custom FDL Config
        /// </summary>
        public void ClearCustomFdl()
        {
            _service.ClearCustomFdl();
            Log("[Spreadtrum] Custom FDL Config Cleared", Color.Gray);
        }

        #endregion

        #region Device Management

        /// <summary>
        /// Start Device Monitor
        /// </summary>
        public void StartDeviceMonitor()
        {
            Log("[Spreadtrum] Starting device monitor...", Color.Gray);
            _service.StartDeviceMonitor();
        }

        /// <summary>
        /// Stop Device Monitor
        /// </summary>
        public void StopDeviceMonitor()
        {
            _service.StopDeviceMonitor();
        }

        /// <summary>
        /// Get Device List
        /// </summary>
        public IReadOnlyList<SprdDeviceInfo> GetDeviceList()
        {
            return _service.GetConnectedDevices();
        }

        /// <summary>
        /// Manually refresh device list (Scan ports)
        /// </summary>
        public IReadOnlyList<SprdDeviceInfo> RefreshDevices()
        {
            Log("[Spreadtrum] Scanning ports...", Color.Gray);
            _service.StartDeviceMonitor(); // Restarting monitor will trigger scan
            var devices = _service.GetConnectedDevices();
            if (devices.Count > 0)
            {
                foreach (var dev in devices)
                {
                    Log($"[Spreadtrum] Device Found: {dev.ComPort} ({dev.Mode})", Color.Cyan);
                    OnDeviceConnected?.Invoke(dev);
                }
            }
            else
            {
                Log("[Spreadtrum] No Spreadtrum device detected, please ensure device is in Download Mode", Color.Orange);
            }
            return devices;
        }

        /// <summary>
        /// Connect Device
        /// </summary>
        public async Task<bool> ConnectDeviceAsync(string comPort)
        {
            Log(string.Format("[Spreadtrum] Connecting device: {0}", comPort), Color.Cyan);
            return await _service.ConnectAsync(comPort);
        }

        /// <summary>
        /// Initialize Device (Download FDL1/FDL2)
        /// </summary>
        public async Task<bool> InitializeDeviceAsync()
        {
            return await _service.InitializeDeviceAsync();
        }

        /// <summary>
        /// Connect and Initialize Device (One-click operation)
        /// </summary>
        public async Task<bool> ConnectAndInitializeAsync(string comPort)
        {
            Log(string.Format("[Spreadtrum] Connecting device: {0}", comPort), Color.Cyan);
            return await _service.ConnectAndInitializeAsync(comPort);
        }

        /// <summary>
        /// Wait and Auto Connect
        /// </summary>
        public async Task<bool> WaitAndConnectAsync(int timeoutSeconds = 30)
        {
            Log("[Spreadtrum] Waiting for device connection...", Color.Yellow);
            Log("[Spreadtrum] Please connect device to PC (Hold Volume Down to enter Download Mode)", Color.Yellow);
            return await _service.WaitAndConnectAsync(timeoutSeconds * 1000);
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _operationCts?.Cancel();
            _service.Disconnect();
            Log("[Spreadtrum] Disconnected", Color.Gray);
        }

        #endregion

        #region PAC Operations

        /// <summary>
        /// Load PAC Firmware
        /// </summary>
        public bool LoadPacFirmware(string pacFilePath)
        {
            var pac = _service.LoadPac(pacFilePath);
            if (pac != null)
            {
                OnPacLoaded?.Invoke(pac);
                Log(string.Format("[Spreadtrum] PAC Loaded: {0} files", pac.Files.Count), Color.Green);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Extract PAC Firmware
        /// </summary>
        public async Task ExtractPacAsync(string outputDir)
        {
            if (CurrentPac == null)
            {
                Log("[Spreadtrum] Please load PAC file first", Color.Orange);
                return;
            }

            ResetOperationCts();
            await _service.ExtractPacAsync(outputDir, _operationCts.Token);
        }

        #endregion

        #region Flash Operations

        /// <summary>
        /// Start Flash
        /// </summary>
        public async Task<bool> StartFlashAsync(List<string> selectedPartitions = null)
        {
            if (CurrentPac == null)
            {
                Log("[Spreadtrum] Please load PAC file first", Color.Orange);
                return false;
            }

            if (!IsConnected)
            {
                Log("[Spreadtrum] Please connect device first", Color.Orange);
                return false;
            }

            ResetOperationCts();

            Log("========================================", Color.White);
            Log("[Spreadtrum] Starting Flash Process", Color.Cyan);
            Log("========================================", Color.White);

            bool result = await _service.FlashPacAsync(selectedPartitions, _operationCts.Token);

            if (result)
            {
                Log("========================================", Color.Green);
                Log("[Spreadtrum] Flash Complete!", Color.Green);
                Log("========================================", Color.Green);
            }
            else
            {
                Log("========================================", Color.Red);
                Log("[Spreadtrum] Flash Failed", Color.Red);
                Log("========================================", Color.Red);
            }

            return result;
        }

        /// <summary>
        /// Flash Single Partition
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partitionName, string filePath)
        {
            return await _service.FlashPartitionAsync(partitionName, filePath);
        }

        /// <summary>
        /// Read Partition
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, uint size)
        {
            return await _service.ReadPartitionAsync(partitionName, outputPath, size);
        }

        /// <summary>
        /// Erase Partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            return await _service.ErasePartitionAsync(partitionName);
        }

        /// <summary>
        /// Cancel Operation
        /// </summary>
        public void CancelOperation()
        {
            if (_operationCts != null)
            {
                _operationCts.Cancel();
                _operationCts.Dispose();
                _operationCts = null;
            }
            Log("[Spreadtrum] Operation Cancelled", Color.Orange);
        }

        /// <summary>
        /// Safely reset CancellationTokenSource
        /// </summary>
        private void ResetOperationCts()
        {
            if (_operationCts != null)
            {
                try { _operationCts.Cancel(); } catch { /* Cancel might be delayed, ignore */ }
                try { _operationCts.Dispose(); } catch { /* Dispose failure can be ignored */ }
            }
            _operationCts = new CancellationTokenSource();
        }

        #endregion

        #region Device Info

        /// <summary>
        /// Read GPT
        /// </summary>
        public async Task ReadPartitionTableAsync()
        {
            var partitions = await _service.ReadPartitionTableAsync();
            if (partitions != null)
            {
                OnPartitionTableLoaded?.Invoke(partitions);
                Log(string.Format("[Spreadtrum] Read {0} partitions", partitions.Count), Color.Green);
            }
        }

        /// <summary>
        /// Get Cached Partition Table
        /// </summary>
        public List<SprdPartitionInfo> CachedPartitions => _service.CachedPartitions;

        /// <summary>
        /// Get Partition Size (From Cache)
        /// </summary>
        public uint GetPartitionSize(string partitionName)
        {
            return _service.GetPartitionSize(partitionName);
        }

        /// <summary>
        /// Read Chip Info
        /// </summary>
        public async Task<string> ReadChipInfoAsync()
        {
            uint chipId = await _service.ReadChipTypeAsync();
            if (chipId != 0)
            {
                string platform = SprdPlatform.GetPlatformName(chipId);
                Log(string.Format("[Spreadtrum] Chip: {0} (0x{1:X})", platform, chipId), Color.Cyan);
                return platform;
            }
            return null;
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reboot Device
        /// </summary>
        public async Task RebootDeviceAsync()
        {
            await _service.RebootAsync();
        }

        /// <summary>
        /// Power Off Device
        /// </summary>
        public async Task PowerOffDeviceAsync()
        {
            await _service.PowerOffAsync();
        }

        #endregion

        #region Security Features

        /// <summary>
        /// Unlock Device
        /// </summary>
        public async Task<bool> UnlockAsync(byte[] unlockData = null)
        {
            Log("[Spreadtrum] Attempting to unlock device...", Color.Yellow);
            bool result = await _service.UnlockAsync(unlockData);
            if (result)
                Log("[Spreadtrum] Device Unlock Success", Color.Green);
            else
                Log("[Spreadtrum] Device Unlock Failed", Color.Red);
            return result;
        }

        /// <summary>
        /// Read Public Key
        /// </summary>
        public async Task<byte[]> ReadPublicKeyAsync()
        {
            return await _service.ReadPublicKeyAsync();
        }

        /// <summary>
        /// Send Signature Verification
        /// </summary>
        public async Task<bool> SendSignatureAsync(byte[] signature)
        {
            return await _service.SendSignatureAsync(signature);
        }

        /// <summary>
        /// Read eFuse
        /// </summary>
        public async Task<byte[]> ReadEfuseAsync(uint blockId = 0)
        {
            return await _service.ReadEfuseAsync(blockId);
        }

        #endregion

        #region NV Operations

        /// <summary>
        /// Read NV Item
        /// </summary>
        public async Task<byte[]> ReadNvItemAsync(ushort itemId)
        {
            return await _service.ReadNvItemAsync(itemId);
        }

        /// <summary>
        /// Write NV Item
        /// </summary>
        public async Task<bool> WriteNvItemAsync(ushort itemId, byte[] data)
        {
            return await _service.WriteNvItemAsync(itemId, data);
        }

        /// <summary>
        /// Read IMEI
        /// </summary>
        public async Task<string> ReadImeiAsync()
        {
            string imei = await _service.ReadImeiAsync();
            if (!string.IsNullOrEmpty(imei))
                Log(string.Format("[Spreadtrum] IMEI: {0}", imei), Color.Cyan);
            return imei;
        }

        /// <summary>
        /// Write IMEI
        /// </summary>
        public async Task<bool> WriteImeiAsync(string newImei)
        {
            Log(string.Format("[Spreadtrum] Writing IMEI: {0}...", newImei), Color.Yellow);
            bool result = await _service.WriteImeiAsync(newImei);
            if (result)
                Log("[Spreadtrum] IMEI Write Success", Color.Green);
            else
                Log("[Spreadtrum] IMEI Write Failed", Color.Red);
            return result;
        }

        #endregion

        #region Flash Info

        /// <summary>
        /// Read Flash Info
        /// </summary>
        public async Task<SprdFlashInfo> ReadFlashInfoAsync()
        {
            var info = await _service.ReadFlashInfoAsync();
            if (info != null)
                Log(string.Format("[Spreadtrum] Flash: {0}", info), Color.Cyan);
            return info;
        }

        /// <summary>
        /// Repartition (Dangerous Operation)
        /// </summary>
        public async Task<bool> RepartitionAsync(byte[] partitionTableData)
        {
            Log("[Spreadtrum] WARNING: Repartitioning...", Color.Red);
            return await _service.RepartitionAsync(partitionTableData);
        }

        #endregion

        #region Baud Rate

        /// <summary>
        /// Set Baud Rate
        /// </summary>
        public async Task<bool> SetBaudRateAsync(int baudRate)
        {
            Log(string.Format("[Spreadtrum] Switching Baud Rate: {0}", baudRate), Color.Gray);
            return await _service.SetBaudRateAsync(baudRate);
        }

        #endregion

        #region Partition Single Flash

        /// <summary>
        /// Flash single image file to partition
        /// </summary>
        public async Task<bool> FlashImageFileAsync(string partitionName, string imageFilePath)
        {
            return await _service.FlashImageFileAsync(partitionName, imageFilePath);
        }

        /// <summary>
        /// Batch flash multiple partitions
        /// </summary>
        public async Task<bool> FlashMultipleImagesAsync(Dictionary<string, string> partitionFiles)
        {
            return await _service.FlashMultipleImagesAsync(partitionFiles);
        }

        #endregion

        #region Security Info

        /// <summary>
        /// Get Security Info
        /// </summary>
        public async Task<SprdSecurityInfo> GetSecurityInfoAsync()
        {
            return await _service.GetSecurityInfoAsync();
        }

        /// <summary>
        /// Get Flash Info
        /// </summary>
        public async Task<SprdFlashInfo> GetFlashInfoAsync()
        {
            return await _service.GetFlashInfoAsync();
        }

        /// <summary>
        /// Check Vulnerability
        /// </summary>
        public SprdVulnerabilityCheckResult CheckVulnerability()
        {
            return _service.CheckVulnerability();
        }

        #endregion

        #region Partition Backup

        /// <summary>
        /// Read Partition to File
        /// </summary>
        public async Task<bool> ReadPartitionToFileAsync(string partitionName, string outputPath, uint size)
        {
            return await _service.ReadPartitionAsync(partitionName, outputPath, size);
        }

        #endregion

        #region Calibration Data and Factory Reset

        /// <summary>
        /// Backup Calibration Data
        /// </summary>
        public async Task<bool> BackupCalibrationDataAsync(string outputDir)
        {
            Log("[Spreadtrum] Starting backup calibration data...", Color.Cyan);
            return await _service.BackupCalibrationDataAsync(outputDir);
        }

        /// <summary>
        /// Restore Calibration Data
        /// </summary>
        public async Task<bool> RestoreCalibrationDataAsync(string inputDir)
        {
            Log("[Spreadtrum] Starting restore calibration data...", Color.Yellow);
            return await _service.RestoreCalibrationDataAsync(inputDir);
        }

        /// <summary>
        /// Factory Reset
        /// </summary>
        public async Task<bool> FactoryResetAsync()
        {
            Log("[Spreadtrum] Performing factory reset...", Color.Yellow);
            return await _service.FactoryResetAsync();
        }

        #endregion

        #region Bootloader Unlock

        /// <summary>
        /// Get Bootloader Status
        /// </summary>
        public async Task<SprdBootloaderStatus> GetBootloaderStatusAsync()
        {
            Log("[Spreadtrum] Getting Bootloader Status...", Color.Cyan);
            return await _service.GetBootloaderStatusAsync();
        }

        /// <summary>
        /// Unlock Bootloader (Use Exploit)
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync(bool useExploit = false)
        {
            Log("[Spreadtrum] Attempting to unlock Bootloader...", Color.Yellow);
            return await _service.UnlockBootloaderAsync(useExploit);
        }

        /// <summary>
        /// Unlock Bootloader with Code
        /// </summary>
        public async Task<bool> UnlockBootloaderWithCodeAsync(string unlockCode)
        {
            Log("[Spreadtrum] Unlocking Bootloader with code...", Color.Yellow);
            return await _service.UnlockBootloaderWithCodeAsync(unlockCode);
        }

        /// <summary>
        /// Relock Bootloader
        /// </summary>
        public async Task<bool> RelockBootloaderAsync()
        {
            Log("[Spreadtrum] Relocking Bootloader...", Color.Yellow);
            return await _service.RelockBootloaderAsync();
        }

        #endregion

        #region FDL Database

        /// <summary>
        /// Get all supported chip names
        /// </summary>
        public string[] GetSupportedChips()
        {
            return Database.SprdFdlDatabase.GetChipNames();
        }

        /// <summary>
        /// Get device list for chip
        /// </summary>
        public string[] GetDevicesForChip(string chipName)
        {
            return Database.SprdFdlDatabase.GetDeviceNames(chipName);
        }

        /// <summary>
        /// Get Chip Info by Name
        /// </summary>
        public Database.SprdChipInfo GetChipInfo(string chipName)
        {
            return Database.SprdFdlDatabase.GetChipByName(chipName);
        }

        /// <summary>
        /// Get Device FDL Info
        /// </summary>
        public Database.SprdDeviceFdl GetDeviceFdl(string chipName, string deviceName)
        {
            var fdls = Database.SprdFdlDatabase.GetDeviceFdlsByChip(chipName);
            return fdls.Find(f => f.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Apply Chip Config
        /// </summary>
        public void ApplyChipConfig(string chipName)
        {
            if (chipName == "Auto Detect")
            {
                SetChipId(0);
                SetCustomFdl1(null, 0);
                SetCustomFdl2(null, 0);
                Log("[Spreadtrum] Using Auto Detect Mode", Color.Gray);
                return;
            }

            var chip = GetChipInfo(chipName);
            if (chip != null)
            {
                SetChipId(chip.ChipId);
                SetCustomFdl1(null, chip.Fdl1Address);
                SetCustomFdl2(null, chip.Fdl2Address);
                
                string exploitInfo = chip.HasExploit ? $" (Has Exploit: {chip.ExploitId})" : "";
                Log($"[Spreadtrum] Selected Chip: {chip.DisplayName}{exploitInfo}", Color.Cyan);
                Log($"[Spreadtrum] FDL1 Addr: {chip.Fdl1AddressHex}, FDL2 Addr: {chip.Fdl2AddressHex}", Color.Gray);
            }
        }

        /// <summary>
        /// Get Chip Database Statistics
        /// </summary>
        public (int chipCount, int deviceCount, int exploitCount) GetDatabaseStats()
        {
            var chips = Database.SprdFdlDatabase.Chips;
            var devices = Database.SprdFdlDatabase.DeviceFdls;
            var exploits = chips.Count(c => c.HasExploit);
            return (chips.Count, devices.Count, exploits);
        }

        #endregion

        #region Helper Methods

        private void Log(string message, Color color)
        {
            _logCallback?.Invoke(message, color);
            _detailLogCallback?.Invoke(message);
        }

        public void Dispose()
        {
            _operationCts?.Cancel();
            _service?.Dispose();
        }

        #endregion
    }
}
