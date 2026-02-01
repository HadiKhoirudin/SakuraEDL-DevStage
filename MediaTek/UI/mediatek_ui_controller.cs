// ============================================================================
// LoveAlways - MediaTek UI Controller
// MediaTek UI Controller
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
using LoveAlways.MediaTek.Common;
using LoveAlways.MediaTek.Database;
using LoveAlways.MediaTek.Exploit;
using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;
using LoveAlways.MediaTek.Services;

namespace LoveAlways.MediaTek.UI
{
    /// <summary>
    /// MediaTek UI Controller
    /// </summary>
    public class MediatekUIController : IDisposable
    {
        private readonly MediatekService _service;
        private readonly MtkPortDetector _portDetector;
        private readonly Action<string, Color> _logCallback;
        private readonly Action<string> _detailLogCallback;
        private CancellationTokenSource _operationCts;

        // Events
        public event Action<int, int> OnProgress;
        public event Action<MtkDeviceState> OnStateChanged;
        public event Action<MtkDeviceInfo> OnDeviceConnected;
        public event Action<MtkDeviceInfo> OnDeviceDisconnected;
        public event Action<List<MtkPartitionInfo>> OnPartitionTableLoaded;

        // Properties
        public bool IsConnected => _service.IsConnected;
        public bool IsBromMode => _service.IsBromMode;
        public MtkDeviceState State => _service.State;
        public MtkChipInfo ChipInfo => _service.ChipInfo;
        public MtkDeviceInfo CurrentDevice => _service.CurrentDevice;

        // Cached Partition Table
        public List<MtkPartitionInfo> CachedPartitions { get; private set; }

        // Port Detection Events
        public event Action<MtkPortInfo> OnPortDetected;
        public event Action<string> OnPortRemoved;

        public MediatekUIController(Action<string, Color> logCallback, Action<string> detailLogCallback = null)
        {
            _logCallback = logCallback;
            _detailLogCallback = detailLogCallback;

            _service = new MediatekService();
            _service.OnLog += Log;
            _service.OnProgress += (c, t) => OnProgress?.Invoke(c, t);
            _service.OnStateChanged += state => OnStateChanged?.Invoke(state);
            _service.OnDeviceConnected += dev => OnDeviceConnected?.Invoke(dev);
            _service.OnDeviceDisconnected += dev => OnDeviceDisconnected?.Invoke(dev);

            // Initialize Port Detector
            _portDetector = new MtkPortDetector(msg => Log(msg, Color.Gray));
            _portDetector.OnDeviceArrived += port =>
            {
                Log($"[MTK] Device Detected: {port.ComPort} ({port.Description})", Color.Cyan);
                OnPortDetected?.Invoke(port);
            };
            _portDetector.OnDeviceRemoved += portName =>
            {
                Log($"[MTK] Device Removed: {portName}", Color.Orange);
                OnPortRemoved?.Invoke(portName);
            };
        }

        #region Port Detection

        /// <summary>
        /// Get All MTK Device Ports
        /// </summary>
        public List<MtkPortInfo> GetMtkPorts()
        {
            return _portDetector.GetMtkPorts();
        }

        /// <summary>
        /// Start Device Monitoring
        /// </summary>
        public void StartPortMonitoring()
        {
            _portDetector.StartMonitoring();
            Log("[MTK] Device Monitoring Started", Color.Gray);
        }

        /// <summary>
        /// Stop Device Monitoring
        /// </summary>
        public void StopPortMonitoring()
        {
            _portDetector.StopMonitoring();
        }

        /// <summary>
        /// Wait for MTK Device Connection
        /// </summary>
        public async Task<MtkPortInfo> WaitForDeviceAsync(int timeoutMs = 60000)
        {
            ResetOperationCts();
            return await _portDetector.WaitForDeviceAsync(timeoutMs, _operationCts.Token);
        }

        /// <summary>
        /// Wait for BROM Device Connection
        /// </summary>
        public async Task<MtkPortInfo> WaitForBromDeviceAsync(int timeoutMs = 60000)
        {
            ResetOperationCts();
            return await _portDetector.WaitForBromDeviceAsync(timeoutMs, _operationCts.Token);
        }

        /// <summary>
        /// Auto Connect First Detected Device
        /// </summary>
        public async Task<bool> AutoConnectAsync(int waitTimeoutMs = 60000)
        {
            Log("[MTK] Waiting for device connection...", Color.Cyan);

            var port = await WaitForDeviceAsync(waitTimeoutMs);
            if (port == null)
            {
                Log("[MTK] No device detected", Color.Red);
                return false;
            }

            return await ConnectDeviceAsync(port.ComPort);
        }

        #endregion

        #region Device Connection

        /// <summary>
        /// Connect Device
        /// </summary>
        public async Task<bool> ConnectDeviceAsync(string comPort, int baudRate = 115200)
        {
            ResetOperationCts();
            return await _service.ConnectAsync(comPort, baudRate, _operationCts.Token);
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _operationCts?.Cancel();
            _service.Disconnect();
            CachedPartitions = null;
        }

        #endregion

        #region DA Loading

        /// <summary>
        /// Set DA File Path
        /// </summary>
        public void SetDaFilePath(string filePath)
        {
            _service.SetDaFilePath(filePath);
        }

        /// <summary>
        /// Set Custom DA1
        /// </summary>
        public void SetCustomDa1(string filePath)
        {
            _service.SetCustomDa1(filePath);
        }

        /// <summary>
        /// Set Custom DA2
        /// </summary>
        public void SetCustomDa2(string filePath)
        {
            _service.SetCustomDa2(filePath);
        }

        /// <summary>
        /// Load DA
        /// </summary>
        public async Task<bool> LoadDaAsync()
        {
            ResetOperationCts();
            return await _service.LoadDaAsync(_operationCts.Token);
        }

        /// <summary>
        /// Connect and Load DA (One-click)
        /// </summary>
        public async Task<bool> ConnectAndLoadDaAsync(string comPort)
        {
            ResetOperationCts();

            Log("[MTK] Connecting device and loading DA...", Color.Cyan);

            if (!await _service.ConnectAsync(comPort, 115200, _operationCts.Token))
            {
                return false;
            }

            return await _service.LoadDaAsync(_operationCts.Token);
        }

        #endregion

        #region Partition Operations

        /// <summary>
        /// Read GPT
        /// </summary>
        public async Task ReadPartitionTableAsync()
        {
            ResetOperationCts();

            var partitions = await _service.ReadPartitionTableAsync(_operationCts.Token);
            if (partitions != null)
            {
                CachedPartitions = partitions;
                OnPartitionTableLoaded?.Invoke(partitions);
            }
        }

        /// <summary>
        /// Read Partition to File
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, ulong size)
        {
            ResetOperationCts();
            return await _service.ReadPartitionAsync(partitionName, outputPath, size, _operationCts.Token);
        }

        /// <summary>
        /// Write Partition
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, string filePath)
        {
            ResetOperationCts();
            return await _service.WritePartitionAsync(partitionName, filePath, _operationCts.Token);
        }

        /// <summary>
        /// Erase Partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            ResetOperationCts();
            return await _service.ErasePartitionAsync(partitionName, _operationCts.Token);
        }

        /// <summary>
        /// Batch Flash
        /// </summary>
        public async Task<bool> FlashMultipleAsync(Dictionary<string, string> partitionFiles)
        {
            ResetOperationCts();

            Log("========================================", Color.White);
            Log("[MTK] Starting Flash Process", Color.Cyan);
            Log("========================================", Color.White);

            bool result = await _service.FlashMultipleAsync(partitionFiles, _operationCts.Token);

            if (result)
            {
                Log("========================================", Color.Green);
                Log("[MTK] Flash Complete!", Color.Green);
                Log("========================================", Color.Green);
            }
            else
            {
                Log("========================================", Color.Red);
                Log("[MTK] Flash Failed", Color.Red);
                Log("========================================", Color.Red);
            }

            return result;
        }

        /// <summary>
        /// Get Partition Size
        /// </summary>
        public ulong GetPartitionSize(string partitionName)
        {
            if (CachedPartitions == null)
                return 0;

            var partition = CachedPartitions.FirstOrDefault(p =>
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

            return partition?.Size ?? 0;
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
        /// Shutdown Device
        /// </summary>
        public async Task ShutdownDeviceAsync()
        {
            await _service.ShutdownAsync();
        }

        /// <summary>
        /// Get Flash Info
        /// </summary>
        public async Task<MtkFlashInfo> GetFlashInfoAsync()
        {
            var info = await _service.GetFlashInfoAsync();
            if (info != null)
            {
                Log($"[MTK] Flash: {info.FlashType} {info.CapacityDisplay}", Color.Cyan);
            }
            return info;
        }

        #endregion

        #region Security Features

        /// <summary>
        /// Check Vulnerability
        /// </summary>
        public bool CheckVulnerability()
        {
            bool isVulnerable = _service.CheckVulnerability();
            if (isVulnerable)
            {
                Log("[MTK] ✓ Device vulnerability detected (Carbonara), signature bypass possible", Color.Yellow);
            }
            else
            {
                Log("[MTK] No known vulnerability detected", Color.Gray);
            }
            return isVulnerable;
        }

        /// <summary>
        /// Get Security Info
        /// </summary>
        public MtkSecurityInfo GetSecurityInfo()
        {
            var info = _service.GetSecurityInfo();
            if (info != null)
            {
                Log($"[MTK] Secure Boot: {(info.SecureBootEnabled ? "Enabled" : "Disabled")}", Color.Cyan);
                Log($"[MTK] SLA: {(info.SlaEnabled ? "Enabled" : "Disabled")}", Color.Cyan);
                Log($"[MTK] DAA: {(info.DaaEnabled ? "Enabled" : "Disabled")}", Color.Cyan);
                if (!string.IsNullOrEmpty(info.MeId))
                    Log($"[MTK] ME ID: {info.MeId}", Color.Gray);
            }
            return info;
        }

        #endregion

        #region Chip Database

        /// <summary>
        /// Get All Supported Chips
        /// </summary>
        public string[] GetSupportedChips()
        {
            return MtkChipDatabase.GetAllChips()
                .Select(c => $"{c.ChipName} (0x{c.HwCode:X4})")
                .ToArray();
        }

        /// <summary>
        /// Get Exploitable Chips
        /// </summary>
        public string[] GetExploitableChips()
        {
            return MtkChipDatabase.GetExploitableChips()
                .Select(c => $"{c.ChipName} (0x{c.HwCode:X4})")
                .ToArray();
        }

        /// <summary>
        /// Get Chip Info
        /// </summary>
        public MtkChipRecord GetChipRecord(ushort hwCode)
        {
            return MtkChipDatabase.GetChip(hwCode);
        }

        /// <summary>
        /// Get Database Statistics
        /// </summary>
        public (int chipCount, int exploitCount) GetDatabaseStats()
        {
            var chips = MtkChipDatabase.GetAllChips();
            var exploitable = MtkChipDatabase.GetExploitableChips();
            return (chips.Count, exploitable.Count);
        }

        #endregion

        #region ALLINONE-SIGNATURE Exploit

        /// <summary>
        /// Get chips supporting ALLINONE-SIGNATURE exploit
        /// </summary>
        public string[] GetAllinoneSignatureChips()
        {
            return MtkChipDatabase.GetAllinoneSignatureChips()
                .Select(c => $"{c.ChipName} - {c.Description} (0x{c.HwCode:X4})")
                .ToArray();
        }

        /// <summary>
        /// Check if current device supports ALLINONE-SIGNATURE exploit
        /// </summary>
        public bool IsCurrentDeviceAllinoneSignatureSupported()
        {
            if (!IsConnected || ChipInfo == null)
                return false;

            return MtkChipDatabase.IsAllinoneSignatureSupported(ChipInfo.HwCode);
        }

        /// <summary>
        /// Get current device exploit type
        /// </summary>
        public string GetCurrentDeviceExploitType()
        {
            if (!IsConnected || ChipInfo == null)
                return "None";

            return MtkChipDatabase.GetExploitType(ChipInfo.HwCode);
        }

        /// <summary>
        /// Execute ALLINONE-SIGNATURE Exploit
        /// Only for chips supporting this exploit like MT6989/MT6983/MT6985
        /// </summary>
        /// <param name="shellcodePath">Shellcode path (Optional, default built-in)</param>
        /// <param name="pointerTablePath">Pointer table path (Optional, auto generated)</param>
        /// <returns>Success or not</returns>
        public async Task<bool> RunAllinoneSignatureExploitAsync(
            string shellcodePath = null,
            string pointerTablePath = null)
        {
            // Check if device supports this exploit
            if (!IsCurrentDeviceAllinoneSignatureSupported())
            {
                string chipName = ChipInfo?.ChipName ?? "Unknown";
                ushort hwCode = ChipInfo?.HwCode ?? 0;
                Log($"[MTK] Current device {chipName} (0x{hwCode:X4}) does not support ALLINONE-SIGNATURE exploit", Color.Red);
                Log("[MTK] This exploit only supports following chips:", Color.Yellow);
                foreach (var chip in GetAllinoneSignatureChips())
                {
                    Log($"[MTK]   • {chip}", Color.Yellow);
                }
                return false;
            }

            ResetOperationCts();

            Log("[MTK] ═══════════════════════════════════════", Color.Yellow);
            Log($"[MTK] Starting ALLINONE-SIGNATURE Exploit", Color.Yellow);
            Log($"[MTK] Target Chip: {ChipInfo?.ChipName} (0x{ChipInfo?.HwCode:X4})", Color.Yellow);
            Log("[MTK] ═══════════════════════════════════════", Color.Yellow);

            try
            {
                bool success = await _service.RunAllinoneSignatureExploitAsync(
                    shellcodePath,
                    pointerTablePath,
                    _operationCts.Token);

                if (success)
                {
                    Log("[MTK] ✓ ALLINONE-SIGNATURE Exploit Success!", Color.Green);
                    Log("[MTK] Device security disabled, you can now perform any operation", Color.Green);
                }
                else
                {
                    Log("[MTK] ✗ ALLINONE-SIGNATURE Exploit Failed", Color.Red);
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                Log("[MTK] Exploit operation cancelled", Color.Orange);
                return false;
            }
            catch (Exception ex)
            {
                Log($"[MTK] Exploit Exception: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Get Exploit Info
        /// </summary>
        public MtkExploitInfo GetExploitInfo()
        {
            var info = new MtkExploitInfo
            {
                IsConnected = IsConnected,
                ChipName = ChipInfo?.ChipName ?? "Unknown",
                HwCode = ChipInfo?.HwCode ?? 0,
                ExploitType = GetCurrentDeviceExploitType(),
                IsAllinoneSignatureSupported = IsCurrentDeviceAllinoneSignatureSupported(),
                IsCarbonaraSupported = _service.CheckVulnerability()
            };

            // Get supported exploit chip list
            info.AllinoneSignatureChips = MtkChipDatabase.GetAllinoneSignatureChips()
                .Select(c => new MtkChipExploitInfo
                {
                    ChipName = c.ChipName,
                    HwCode = c.HwCode,
                    Description = c.Description
                }).ToArray();

            return info;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Cancel Operation
        /// </summary>
        public void CancelOperation()
        {
            _operationCts?.Cancel();
            Log("[MTK] Operation Cancelled", Color.Orange);
        }

        private void Log(string message, Color color)
        {
            _logCallback?.Invoke(message, color);
            _detailLogCallback?.Invoke(message);
        }

        private void ResetOperationCts()
        {
            if (_operationCts != null)
            {
                try { _operationCts.Cancel(); } catch { /* Cancel might be delayed, ignore */ }
                try { _operationCts.Dispose(); } catch { /* Dispose failure can be ignored */ }
            }
            _operationCts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _operationCts?.Cancel();
            _portDetector?.Dispose();
            _service?.Dispose();
        }

        #endregion
    }
}
