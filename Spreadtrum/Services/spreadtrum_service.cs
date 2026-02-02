// ============================================================================
// LoveAlways - Spreadtrum Flashing Service
// Spreadtrum/Unisoc Flashing Service
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Common;
using LoveAlways.Spreadtrum.Common;
using LoveAlways.Spreadtrum.Exploit;
using LoveAlways.Spreadtrum.Protocol;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Spreadtrum.Services
{
    /// <summary>
    /// Spreadtrum Flashing Service - Main Service Class
    /// </summary>
    public class SpreadtrumService : IDisposable
    {
        private FdlClient _client;
        private SprdPortDetector _portDetector;
        private PacParser _pacParser;
        private CancellationTokenSource _cts;
        private SprdExploitService _exploitService;

        // Events
        public event Action<string, Color> OnLog;
        public event Action<int, int> OnProgress;
        public event Action<SprdDeviceState> OnStateChanged;
        public event Action<SprdDeviceInfo> OnDeviceConnected;
        public event Action<SprdDeviceInfo> OnDeviceDisconnected;
        public event Action<SprdVulnerabilityCheckResult> OnVulnerabilityDetected;
        public event Action<SprdExploitResult> OnExploitCompleted;

        // Properties
        public bool IsConnected => _client?.IsConnected ?? false;
        public bool IsBromMode => _client?.IsBromMode ?? true;
        public FdlStage CurrentStage => _client?.CurrentStage ?? FdlStage.None;
        public SprdDeviceState State => _client?.State ?? SprdDeviceState.Disconnected;

        // Current loaded PAC information
        public PacInfo CurrentPac { get; private set; }

        // Device partition table cache
        public List<SprdPartitionInfo> CachedPartitions { get; private set; }

        // Chip ID (0 for auto-detection)
        public uint ChipId { get; private set; }

        // Custom FDL configuration
        public string CustomFdl1Path { get; private set; }
        public string CustomFdl2Path { get; private set; }
        public uint CustomFdl1Address { get; private set; }
        public uint CustomFdl2Address { get; private set; }

        /// <summary>
        /// Set Chip ID
        /// </summary>
        public void SetChipId(uint chipId)
        {
            ChipId = chipId;
            if (_client != null)
            {
                _client.SetChipId(chipId);
            }
        }

        /// <summary>
        /// Set custom FDL1
        /// </summary>
        public void SetCustomFdl1(string filePath, uint address)
        {
            CustomFdl1Path = filePath;
            CustomFdl1Address = address;
            _client?.SetCustomFdl1(filePath, address);
        }

        /// <summary>
        /// Set custom FDL2
        /// </summary>
        public void SetCustomFdl2(string filePath, uint address)
        {
            CustomFdl2Path = filePath;
            CustomFdl2Address = address;
            _client?.SetCustomFdl2(filePath, address);
        }

        /// <summary>
        /// Clear custom FDL configuration
        /// </summary>
        public void ClearCustomFdl()
        {
            CustomFdl1Path = null;
            CustomFdl2Path = null;
            CustomFdl1Address = 0;
            CustomFdl2Address = 0;
            _client?.ClearCustomFdl();
        }

        // Watchdog
        private Watchdog _watchdog;

        public SpreadtrumService()
        {
            _pacParser = new PacParser(msg => Log(msg, Color.Gray));
            _portDetector = new SprdPortDetector();
            _exploitService = new SprdExploitService((msg, color) => Log(msg, color));

            _portDetector.OnLog += msg => Log(msg, Color.Gray);
            _portDetector.OnDeviceConnected += dev => OnDeviceConnected?.Invoke(dev);
            _portDetector.OnDeviceDisconnected += dev => OnDeviceDisconnected?.Invoke(dev);

            // Exploit events
            _exploitService.OnVulnerabilityDetected += result => OnVulnerabilityDetected?.Invoke(result);
            _exploitService.OnExploitCompleted += result => OnExploitCompleted?.Invoke(result);

            // Initialize watchdog
            _watchdog = new Watchdog("Spreadtrum", WatchdogManager.DefaultTimeouts.Spreadtrum,
                msg => Log(msg, Color.Gray));
            _watchdog.OnTimeout += OnWatchdogTimeout;
        }

        /// <summary>
        /// Watchdog timeout handling
        /// </summary>
        private void OnWatchdogTimeout(object sender, WatchdogTimeoutEventArgs e)
        {
            Log($"[Spreadtrum] Watchdog timeout: {e.OperationName} (Waiting {e.ElapsedTime.TotalSeconds:F1}s)", Color.Orange);

            if (e.TimeoutCount >= 3)
            {
                Log("[Spreadtrum] Multiple timeouts, disconnecting", Color.Red);
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

        #region Device Connection

        /// <summary>
        /// Start listening for devices
        /// </summary>
        public void StartDeviceMonitor()
        {
            _portDetector.StartWatching();
        }

        /// <summary>
        /// Stop listening for devices
        /// </summary>
        public void StopDeviceMonitor()
        {
            _portDetector.StopWatching();
        }

        /// <summary>
        /// Get currently connected device list
        /// </summary>
        public IReadOnlyList<SprdDeviceInfo> GetConnectedDevices()
        {
            return _portDetector.ConnectedDevices;
        }

        /// <summary>
        /// Connect device
        /// </summary>
        public async Task<bool> ConnectAsync(string comPort, int baudRate = 115200)
        {
            try
            {
                Disconnect();

                _client = new FdlClient();
                _client.OnLog += msg => Log(msg, Color.White);
                _client.OnProgress += (current, total) => OnProgress?.Invoke(current, total);
                _client.OnStateChanged += state => OnStateChanged?.Invoke(state);

                // Apply saved settings to new client
                ApplyClientConfiguration();

                Log(string.Format("[Spreadtrum] Connecting device: {0}", comPort), Color.Cyan);

                bool success = await _client.ConnectAsync(comPort, baudRate);

                if (success)
                {
                    Log("[Spreadtrum] Device connected successfully", Color.Green);
                }
                else
                {
                    Log("[Spreadtrum] Device connection failed", Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Connection error: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Apply saved settings to FdlClient
        /// </summary>
        private void ApplyClientConfiguration()
        {
            if (_client == null) return;

            // Apply chip ID (this will automatically set exec_addr)
            if (ChipId > 0)
            {
                _client.SetChipId(ChipId);
            }

            // Apply custom FDL configuration
            if (!string.IsNullOrEmpty(CustomFdl1Path) || CustomFdl1Address > 0)
            {
                _client.SetCustomFdl1(CustomFdl1Path, CustomFdl1Address);
            }

            if (!string.IsNullOrEmpty(CustomFdl2Path) || CustomFdl2Address > 0)
            {
                _client.SetCustomFdl2(CustomFdl2Path, CustomFdl2Address);
            }
        }

        /// <summary>
        /// Wait for device and automatically connect
        /// </summary>
        public async Task<bool> WaitAndConnectAsync(int timeoutMs = 30000)
        {
            Log("[Spreadtrum] Waiting for device connection...", Color.Yellow);

            ResetCancellationToken();
            var device = await _portDetector.WaitForDeviceAsync(timeoutMs, _cts.Token);

            if (device != null)
            {
                return await ConnectAsync(device.ComPort);
            }

            Log("[Spreadtrum] Waiting for device timeout", Color.Orange);
            return false;
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _client?.Disconnect();
            _client?.Dispose();
            _client = null;
        }

        /// <summary>
        /// Initialize device - Automatically detect mode and download FDL
        /// </summary>
        /// <returns>true: Already entered FDL2 mode, ready for partition operations; false: Initialization failed</returns>
        public async Task<bool> InitializeDeviceAsync()
        {
            if (!IsConnected)
            {
                Log("[Spreadtrum] Device not connected", Color.Red);
                return false;
            }

            // Check current status
            if (CurrentStage == FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device already in FDL2 mode", Color.Green);
                return true;
            }

            // If in BROM mode, need to download FDL
            if (IsBromMode || CurrentStage == FdlStage.None)
            {
                Log("[Spreadtrum] Device in BROM mode, starting FDL download...", Color.Yellow);

                // Get FDL1 data and address
                byte[] fdl1Data = null;
                uint fdl1Addr = 0;

                // Prioritize custom FDL1
                if (!string.IsNullOrEmpty(CustomFdl1Path) && File.Exists(CustomFdl1Path))
                {
                    fdl1Data = File.ReadAllBytes(CustomFdl1Path);
                    fdl1Addr = CustomFdl1Address;
                    Log($"[Spreadtrum] Using custom FDL1: {Path.GetFileName(CustomFdl1Path)}", Color.Cyan);
                }
                // Next use FDL1 from PAC
                else if (CurrentPac != null)
                {
                    var fdl1Entry = _pacParser.GetFdl1(CurrentPac);
                    if (fdl1Entry != null)
                    {
                        string tempFdl1 = Path.Combine(Path.GetTempPath(), "fdl1_temp.bin");
                        _pacParser.ExtractFile(CurrentPac.FilePath, fdl1Entry, tempFdl1);
                        fdl1Data = File.ReadAllBytes(tempFdl1);
                        fdl1Addr = fdl1Entry.Address != 0 ? fdl1Entry.Address : CustomFdl1Address;
                        File.Delete(tempFdl1);
                        Log($"[Spreadtrum] Using PAC built-in FDL1", Color.Cyan);
                    }
                }
                // Finally use chip default address
                else if (CustomFdl1Address != 0)
                {
                    fdl1Addr = CustomFdl1Address;
                    Log($"[Spreadtrum] Using chip default FDL1 address: 0x{fdl1Addr:X}", Color.Yellow);
                }

                // If no FDL1 data but have address, try getting from database
                if (fdl1Data == null && ChipId != 0)
                {
                    var chipInfo = Database.SprdFdlDatabase.GetChipById(ChipId);
                    if (chipInfo != null)
                    {
                        fdl1Addr = chipInfo.Fdl1Address;
                        Log($"[Spreadtrum] Chip {chipInfo.ChipName} FDL1 Address: 0x{fdl1Addr:X}", Color.Cyan);

                        // Try finding device specific FDL
                        var deviceFdls = Database.SprdFdlDatabase.GetDeviceNames(chipInfo.ChipName);
                        if (deviceFdls.Length > 0)
                        {
                            Log($"[Spreadtrum] Hint: {deviceFdls.Length} FDLs available for {chipInfo.ChipName} in database", Color.Gray);
                        }
                    }
                }

                // Download FDL1
                if (fdl1Data != null && fdl1Addr != 0)
                {
                    Log("[Spreadtrum] Downloading FDL1...", Color.White);
                    if (!await _client.DownloadFdlAsync(fdl1Data, fdl1Addr, FdlStage.FDL1))
                    {
                        Log("[Spreadtrum] FDL1 download failed", Color.Red);
                        return false;
                    }
                    Log("[Spreadtrum] FDL1 download success", Color.Green);
                }
                else
                {
                    Log("[Spreadtrum] Missing FDL1 data or address, please load PAC or select chip model", Color.Orange);
                    return false;
                }
            }

            // Download FDL2
            if (CurrentStage == FdlStage.FDL1)
            {
                byte[] fdl2Data = null;
                uint fdl2Addr = 0;

                // Prioritize custom FDL2
                if (!string.IsNullOrEmpty(CustomFdl2Path) && File.Exists(CustomFdl2Path))
                {
                    fdl2Data = File.ReadAllBytes(CustomFdl2Path);
                    fdl2Addr = CustomFdl2Address;
                    Log($"[Spreadtrum] Using custom FDL2: {Path.GetFileName(CustomFdl2Path)}", Color.Cyan);
                }
                // Next use FDL2 from PAC
                else if (CurrentPac != null)
                {
                    var fdl2Entry = _pacParser.GetFdl2(CurrentPac);
                    if (fdl2Entry != null)
                    {
                        string tempFdl2 = Path.Combine(Path.GetTempPath(), "fdl2_temp.bin");
                        _pacParser.ExtractFile(CurrentPac.FilePath, fdl2Entry, tempFdl2);
                        fdl2Data = File.ReadAllBytes(tempFdl2);
                        fdl2Addr = fdl2Entry.Address != 0 ? fdl2Entry.Address : CustomFdl2Address;
                        File.Delete(tempFdl2);
                        Log($"[Spreadtrum] Using PAC built-in FDL2", Color.Cyan);
                    }
                }
                else if (CustomFdl2Address != 0)
                {
                    fdl2Addr = CustomFdl2Address;
                }

                // Get FDL2 address from database
                if (fdl2Data == null && ChipId != 0)
                {
                    var chipInfo = Database.SprdFdlDatabase.GetChipById(ChipId);
                    if (chipInfo != null)
                    {
                        fdl2Addr = chipInfo.Fdl2Address;
                        Log($"[Spreadtrum] Chip {chipInfo.ChipName} FDL2 Address: 0x{fdl2Addr:X}", Color.Cyan);
                    }
                }

                // Download FDL2
                if (fdl2Data != null && fdl2Addr != 0)
                {
                    Log("[Spreadtrum] Downloading FDL2...", Color.White);
                    if (!await _client.DownloadFdlAsync(fdl2Data, fdl2Addr, FdlStage.FDL2))
                    {
                        Log("[Spreadtrum] FDL2 download failed", Color.Red);
                        return false;
                    }
                    Log("[Spreadtrum] FDL2 download success", Color.Green);
                }
                else
                {
                    Log("[Spreadtrum] Missing FDL2 data or address, please load PAC or select chip model", Color.Orange);
                    return false;
                }
            }

            // Verify final state
            if (CurrentStage == FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device initialization complete, already in FDL2 mode", Color.Green);
                OnStateChanged?.Invoke(SprdDeviceState.Fdl2Loaded);
                return true;
            }

            Log("[Spreadtrum] Device initialization failed", Color.Red);
            return false;
        }

        /// <summary>
        /// Connect and initialize device (One-click operation)
        /// </summary>
        public async Task<bool> ConnectAndInitializeAsync(string comPort, int baudRate = 115200)
        {
            if (!await ConnectAsync(comPort, baudRate))
            {
                return false;
            }

            return await InitializeDeviceAsync();
        }

        #endregion

        #region PAC Firmware Operations

        /// <summary>
        /// Load PAC firmware package
        /// </summary>
        public PacInfo LoadPac(string pacFilePath)
        {
            try
            {
                Log(string.Format("[Spreadtrum] Loading PAC: {0}", Path.GetFileName(pacFilePath)), Color.Cyan);

                CurrentPac = _pacParser.Parse(pacFilePath);

                Log(string.Format("[Spreadtrum] Product: {0}", CurrentPac.Header.ProductName), Color.White);
                Log(string.Format("[Spreadtrum] Firmware: {0}", CurrentPac.Header.FirmwareName), Color.White);
                Log(string.Format("[Spreadtrum] Version: {0}", CurrentPac.Header.Version), Color.White);
                Log(string.Format("[Spreadtrum] File Count: {0}", CurrentPac.Files.Count), Color.White);

                // Parse XML configuration
                _pacParser.ParseXmlConfigs(CurrentPac);

                if (CurrentPac.XmlConfig != null)
                {
                    Log(string.Format("[Spreadtrum] XML Config: {0}", CurrentPac.XmlConfig.ConfigType), Color.Gray);

                    if (CurrentPac.XmlConfig.Fdl1Config != null)
                    {
                        Log(string.Format("[Spreadtrum] FDL1: {0} @ 0x{1:X}",
                            CurrentPac.XmlConfig.Fdl1Config.FileName,
                            CurrentPac.XmlConfig.Fdl1Config.Address), Color.Gray);
                    }

                    if (CurrentPac.XmlConfig.Fdl2Config != null)
                    {
                        Log(string.Format("[Spreadtrum] FDL2: {0} @ 0x{1:X}",
                            CurrentPac.XmlConfig.Fdl2Config.FileName,
                            CurrentPac.XmlConfig.Fdl2Config.Address), Color.Gray);
                    }

                    if (CurrentPac.XmlConfig.EraseConfig != null)
                    {
                        Log(string.Format("[Spreadtrum] Erase Config: All={0}, UserData={1}",
                            CurrentPac.XmlConfig.EraseConfig.EraseAll,
                            CurrentPac.XmlConfig.EraseConfig.EraseUserData), Color.Gray);
                    }
                }

                return CurrentPac;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Load PAC failed: {0}", ex.Message), Color.Red);
                return null;
            }
        }

        /// <summary>
        /// Extract PAC files
        /// </summary>
        public async Task ExtractPacAsync(string outputDir, CancellationToken cancellationToken = default)
        {
            if (CurrentPac == null)
            {
                Log("[Spreadtrum] PAC file not loaded", Color.Orange);
                return;
            }

            await Task.Run(() =>
            {
                _pacParser.ExtractAll(CurrentPac, outputDir, (current, total, name) =>
                {
                    Log(string.Format("[Spreadtrum] Extracting ({0}/{1}): {2}", current, total, name), Color.Gray);
                    OnProgress?.Invoke(current, total);
                });
            }, cancellationToken);

            Log("[Spreadtrum] PAC extraction complete", Color.Green);
        }

        #endregion

        #region Flashing Operations

        /// <summary>
        /// Full flashing process
        /// </summary>
        public async Task<bool> FlashPacAsync(List<string> selectedPartitions = null, CancellationToken cancellationToken = default)
        {
            if (CurrentPac == null)
            {
                Log("[Spreadtrum] PAC file not loaded", Color.Orange);
                return false;
            }

            if (!IsConnected)
            {
                Log("[Spreadtrum] Device not connected", Color.Orange);
                return false;
            }

            try
            {
                Log("[Spreadtrum] Starting flash process...", Color.Cyan);

                // 1. Download FDL1
                var fdl1Entry = _pacParser.GetFdl1(CurrentPac);
                if (fdl1Entry != null)
                {
                    Log("[Spreadtrum] Downloading FDL1...", Color.White);

                    string tempFdl1 = Path.Combine(Path.GetTempPath(), "fdl1.bin");
                    _pacParser.ExtractFile(CurrentPac.FilePath, fdl1Entry, tempFdl1);

                    byte[] fdl1Data = File.ReadAllBytes(tempFdl1);
                    uint fdl1Addr = fdl1Entry.Address != 0 ? fdl1Entry.Address : SprdPlatform.GetFdl1Address(0);

                    if (!await _client.DownloadFdlAsync(fdl1Data, fdl1Addr, FdlStage.FDL1))
                    {
                        Log("[Spreadtrum] FDL1 download failed", Color.Red);
                        return false;
                    }

                    File.Delete(tempFdl1);
                }

                // 2. Download FDL2
                var fdl2Entry = _pacParser.GetFdl2(CurrentPac);
                if (fdl2Entry != null)
                {
                    Log("[Spreadtrum] Downloading FDL2...", Color.White);

                    string tempFdl2 = Path.Combine(Path.GetTempPath(), "fdl2.bin");
                    _pacParser.ExtractFile(CurrentPac.FilePath, fdl2Entry, tempFdl2);

                    byte[] fdl2Data = File.ReadAllBytes(tempFdl2);
                    uint fdl2Addr = fdl2Entry.Address != 0 ? fdl2Entry.Address : SprdPlatform.GetFdl2Address(0);

                    if (!await _client.DownloadFdlAsync(fdl2Data, fdl2Addr, FdlStage.FDL2))
                    {
                        Log("[Spreadtrum] FDL2 download failed", Color.Red);
                        return false;
                    }

                    File.Delete(tempFdl2);
                }

                // 3. Read device info
                string version = await _client.ReadVersionAsync();
                if (!string.IsNullOrEmpty(version))
                {
                    Log(string.Format("[Spreadtrum] Device version: {0}", version), Color.Cyan);
                }

                // 4. Flash partitions
                int totalPartitions = 0;
                int currentPartition = 0;

                // Filter partitions to be flashed
                var partitionsToFlash = new List<PacFileEntry>();
                foreach (var entry in CurrentPac.Files)
                {
                    // Skip FDL, XML, etc.
                    if (entry.Type == PacFileType.FDL1 ||
                        entry.Type == PacFileType.FDL2 ||
                        entry.Type == PacFileType.XML ||
                        entry.Size == 0)
                    {
                        continue;
                    }

                    // If partition list specified, check if included
                    if (selectedPartitions != null &&
                        !selectedPartitions.Contains(entry.PartitionName))
                    {
                        continue;
                    }

                    partitionsToFlash.Add(entry);
                }

                totalPartitions = partitionsToFlash.Count;
                Log(string.Format("[Spreadtrum] Preparing to flash {0} partitions", totalPartitions), Color.White);

                foreach (var entry in partitionsToFlash)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Log("[Spreadtrum] Flash cancelled", Color.Orange);
                        return false;
                    }

                    currentPartition++;
                    Log(string.Format("[Spreadtrum] Flashing partition ({0}/{1}): {2}",
                        currentPartition, totalPartitions, entry.PartitionName), Color.White);

                    // Extract partition data
                    string tempFile = Path.Combine(Path.GetTempPath(), entry.FileName);
                    _pacParser.ExtractFile(CurrentPac.FilePath, entry, tempFile);

                    // Handle Sparse Image
                    string dataFile = tempFile;
                    bool isSparse = SparseHandler.IsSparseImage(tempFile);

                    if (isSparse)
                    {
                        Log(string.Format("[Spreadtrum] Sparse Image detected, decompressing..."), Color.Gray);
                        string rawFile = tempFile + ".raw";
                        var sparseHandler = new SparseHandler(msg => Log(msg, Color.Gray));
                        sparseHandler.Decompress(tempFile, rawFile, (current, total) =>
                        {
                            // Decompression progress
                        });
                        dataFile = rawFile;
                        File.Delete(tempFile);
                    }

                    byte[] partitionData = File.ReadAllBytes(dataFile);

                    // Write partition
                    bool success = await _client.WritePartitionAsync(entry.PartitionName, partitionData, cancellationToken);

                    // Clean temporary files
                    if (isSparse)
                    {
                        if (File.Exists(dataFile))
                            File.Delete(dataFile);
                    }
                    else
                    {
                        if (File.Exists(tempFile))
                            File.Delete(tempFile);
                    }

                    if (!success)
                    {
                        Log(string.Format("[Spreadtrum] Partition {0} flash failed", entry.PartitionName), Color.Red);
                        return false;
                    }
                }

                Log("[Spreadtrum] Flash complete!", Color.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Flash exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Flash single partition
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partitionName, string filePath)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready (FDL2 needs to be loaded)", Color.Orange);
                return false;
            }

            if (!File.Exists(filePath))
            {
                Log(string.Format("[Spreadtrum] File not found: {0}", filePath), Color.Red);
                return false;
            }

            try
            {
                Log(string.Format("[Spreadtrum] Flashing partition: {0}", partitionName), Color.White);

                byte[] data = File.ReadAllBytes(filePath);
                bool success = await _client.WritePartitionAsync(partitionName, data);

                if (success)
                {
                    Log(string.Format("[Spreadtrum] Partition {0} flash success", partitionName), Color.Green);
                }
                else
                {
                    Log(string.Format("[Spreadtrum] Partition {0} flash failed", partitionName), Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Flash exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Read partition
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, uint size)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            try
            {
                Log(string.Format("[Spreadtrum] Reading partition: {0}", partitionName), Color.White);

                byte[] data = await _client.ReadPartitionAsync(partitionName, size);

                if (data != null)
                {
                    File.WriteAllBytes(outputPath, data);
                    Log(string.Format("[Spreadtrum] Partition {0} read complete: {1}", partitionName, outputPath), Color.Green);
                    return true;
                }

                Log(string.Format("[Spreadtrum] Partition {0} read failed", partitionName), Color.Red);
                return false;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Read exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            try
            {
                Log(string.Format("[Spreadtrum] Erasing partition: {0}", partitionName), Color.White);

                bool success = await _client.ErasePartitionAsync(partitionName);

                if (success)
                {
                    Log(string.Format("[Spreadtrum] Partition {0} erase success", partitionName), Color.Green);
                }
                else
                {
                    Log(string.Format("[Spreadtrum] Partition {0} erase failed", partitionName), Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Erase exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reboot device
        /// </summary>
        public async Task<bool> RebootAsync()
        {
            if (!IsConnected)
                return false;

            Log("[Spreadtrum] Rebooting device...", Color.White);
            return await _client.ResetDeviceAsync();
        }

        /// <summary>
        /// Power off
        /// </summary>
        public async Task<bool> PowerOffAsync()
        {
            if (!IsConnected)
                return false;

            Log("[Spreadtrum] Powering off device...", Color.White);
            return await _client.PowerOffAsync();
        }

        /// <summary>
        /// Read partition table
        /// </summary>
        public async Task<List<SprdPartitionInfo>> ReadPartitionTableAsync()
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return null;
            }

            var partitions = await _client.ReadPartitionTableAsync();
            if (partitions != null && partitions.Count > 0)
            {
                CachedPartitions = partitions;
                Log($"[Spreadtrum] Partition table cached: {partitions.Count} partitions", Color.Cyan);
            }
            return partitions;
        }

        /// <summary>
        /// Get partition size (from cache)
        /// </summary>
        public uint GetPartitionSize(string partitionName)
        {
            if (CachedPartitions == null)
                return 0;

            var partition = CachedPartitions.FirstOrDefault(p =>
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
            return partition?.Size ?? 0;
        }

        /// <summary>
        /// Read chip information
        /// </summary>
        public async Task<uint> ReadChipTypeAsync()
        {
            if (!IsConnected)
                return 0;

            return await _client.ReadChipTypeAsync();
        }

        #endregion

        #region Security Functions

        /// <summary>
        /// Unlock device
        /// </summary>
        public async Task<bool> UnlockAsync(byte[] unlockData = null)
        {
            if (!IsConnected)
                return false;

            Log("[Spreadtrum] Unlocking device...", Color.Yellow);
            return await _client.UnlockAsync(unlockData);
        }

        /// <summary>
        /// Read public key
        /// </summary>
        public async Task<byte[]> ReadPublicKeyAsync()
        {
            if (!IsConnected)
                return null;

            return await _client.ReadPublicKeyAsync();
        }

        /// <summary>
        /// Send signature
        /// </summary>
        public async Task<bool> SendSignatureAsync(byte[] signature)
        {
            if (!IsConnected)
                return false;

            Log("[Spreadtrum] Sending signature verification...", Color.Yellow);
            return await _client.SendSignatureAsync(signature);
        }

        /// <summary>
        /// Read eFuse
        /// </summary>
        public async Task<byte[]> ReadEfuseAsync(uint blockId = 0)
        {
            if (!IsConnected)
                return null;

            return await _client.ReadEfuseAsync(blockId);
        }

        #endregion

        #region NV Operations

        /// <summary>
        /// Read NV item
        /// </summary>
        public async Task<byte[]> ReadNvItemAsync(ushort itemId)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
                return null;

            return await _client.ReadNvItemAsync(itemId);
        }

        /// <summary>
        /// Write NV item
        /// </summary>
        public async Task<bool> WriteNvItemAsync(ushort itemId, byte[] data)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
                return false;

            return await _client.WriteNvItemAsync(itemId, data);
        }

        /// <summary>
        /// Read IMEI
        /// </summary>
        public async Task<string> ReadImeiAsync()
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
                return null;

            return await _client.ReadImeiAsync();
        }

        /// <summary>
        /// Write IMEI
        /// </summary>
        public async Task<bool> WriteImeiAsync(string newImei)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            if (string.IsNullOrEmpty(newImei) || newImei.Length != 15)
            {
                Log("[Spreadtrum] Invalid IMEI format", Color.Red);
                return false;
            }

            Log(string.Format("[Spreadtrum] Writing IMEI: {0}", newImei), Color.Yellow);

            // Convert IMEI string to NV data format
            byte[] imeiData = ConvertImeiToNvData(newImei);

            // Write NV item 0 (IMEI)
            bool result = await _client.WriteNvItemAsync(0, imeiData);

            if (result)
            {
                Log("[Spreadtrum] IMEI write success", Color.Green);
            }
            else
            {
                Log("[Spreadtrum] IMEI write failed", Color.Red);
            }

            return result;
        }

        /// <summary>
        /// Convert IMEI string to NV data format
        /// </summary>
        private byte[] ConvertImeiToNvData(string imei)
        {
            // IMEI storage format: BCD encoding
            // 15-digit IMEI -> 8 bytes (first byte is length or flag)
            byte[] data = new byte[9];
            data[0] = 0x08;  // Length flag

            for (int i = 0; i < 15; i += 2)
            {
                int high = imei[i] - '0';
                int low = (i + 1 < 15) ? (imei[i + 1] - '0') : 0xF;
                data[1 + i / 2] = (byte)((low << 4) | high);
            }

            return data;
        }

        #endregion

        #region Flash Information

        /// <summary>
        /// Read Flash information
        /// </summary>
        public async Task<SprdFlashInfo> ReadFlashInfoAsync()
        {
            if (!IsConnected)
                return null;

            return await _client.ReadFlashInfoAsync();
        }

        /// <summary>
        /// Repartition
        /// </summary>
        public async Task<bool> RepartitionAsync(byte[] partitionTableData)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            Log("[Spreadtrum] Executing repartition...", Color.Red);
            return await _client.RepartitionAsync(partitionTableData);
        }

        #endregion

        #region Baud Rate

        /// <summary>
        /// Set baud rate
        /// </summary>
        public async Task<bool> SetBaudRateAsync(int baudRate)
        {
            if (!IsConnected)
                return false;

            return await _client.SetBaudRateAsync(baudRate);
        }

        #endregion

        #region Exploit

        /// <summary>
        /// Check device vulnerability
        /// </summary>
        public SprdVulnerabilityCheckResult CheckVulnerability(string pkHash = null)
        {
            uint chipId = _client?.ChipId ?? ChipId;
            return _exploitService.CheckVulnerability(chipId, pkHash);
        }

        /// <summary>
        /// Attempt automatic exploit
        /// </summary>
        public async Task<SprdExploitResult> TryExploitAsync(
            SerialPort port = null,
            string pkHash = null,
            CancellationToken ct = default(CancellationToken))
        {
            uint chipId = _client?.ChipId ?? ChipId;
            SerialPort targetPort = port ?? _client?.GetPort();

            if (targetPort == null)
            {
                Log("[Exploit] No available serial port", Color.Red);
                return new SprdExploitResult
                {
                    Success = false,
                    Message = "No available serial port connection"
                };
            }

            return await _exploitService.TryExploitAsync(targetPort, chipId, pkHash, ct);
        }

        /// <summary>
        /// Check and attempt exploit
        /// </summary>
        public async Task<SprdExploitResult> CheckAndExploitAsync(CancellationToken ct = default(CancellationToken))
        {
            // 1. Detect vulnerability first
            var vulnResult = CheckVulnerability();

            if (!vulnResult.HasVulnerability)
            {
                return new SprdExploitResult
                {
                    Success = false,
                    Message = "No available vulnerabilities detected"
                };
            }

            // 2. Attempt exploitation
            return await TryExploitAsync(ct: ct);
        }

        /// <summary>
        /// Get exploit service
        /// </summary>
        public SprdExploitService GetExploitService()
        {
            return _exploitService;
        }

        #endregion

        #region Individual Partition Flashing

        /// <summary>
        /// Flash single IMG file to specified partition (does not depend on PAC)
        /// </summary>
        public async Task<bool> FlashImageFileAsync(string partitionName, string imageFilePath, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready (FDL2 needs to be loaded)", Color.Orange);
                return false;
            }

            if (!File.Exists(imageFilePath))
            {
                Log(string.Format("[Spreadtrum] File not found: {0}", imageFilePath), Color.Red);
                return false;
            }

            try
            {
                Log(string.Format("[Spreadtrum] Flashing partition: {0} <- {1}", partitionName, Path.GetFileName(imageFilePath)), Color.Cyan);

                string dataFile = imageFilePath;
                bool needCleanup = false;

                // Detect and handle Sparse Image
                if (SparseHandler.IsSparseImage(imageFilePath))
                {
                    Log("[Spreadtrum] Sparse Image detected, decompressing...", Color.Gray);
                    string rawFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(imageFilePath) + ".raw");
                    var sparseHandler = new SparseHandler(msg => Log(msg, Color.Gray));
                    sparseHandler.Decompress(imageFilePath, rawFile, (c, t) => OnProgress?.Invoke((int)c, (int)t));
                    dataFile = rawFile;
                    needCleanup = true;
                }

                // Read file data
                byte[] data = File.ReadAllBytes(dataFile);
                Log(string.Format("[Spreadtrum] Data size: {0}", FormatSize((ulong)data.Length)), Color.Gray);

                // Write partition
                bool success = await _client.WritePartitionAsync(partitionName, data, ct);

                // Cleanup temporary files
                if (needCleanup && File.Exists(dataFile))
                {
                    File.Delete(dataFile);
                }

                if (success)
                {
                    Log(string.Format("[Spreadtrum] Partition {0} flash success", partitionName), Color.Green);
                }
                else
                {
                    Log(string.Format("[Spreadtrum] Partition {0} flash failed", partitionName), Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Flash exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Batch flash multiple partitions
        /// </summary>
        public async Task<bool> FlashMultipleImagesAsync(Dictionary<string, string> partitionFiles, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            int total = partitionFiles.Count;
            int current = 0;
            int success = 0;

            foreach (var kvp in partitionFiles)
            {
                ct.ThrowIfCancellationRequested();

                current++;
                Log(string.Format("[Spreadtrum] Flash progress ({0}/{1}): {2}", current, total, kvp.Key), Color.White);

                if (await FlashImageFileAsync(kvp.Key, kvp.Value, ct))
                {
                    success++;
                }
            }

            Log(string.Format("[Spreadtrum] Batch flash complete: {0}/{1} success", success, total),
                success == total ? Color.Green : Color.Orange);

            return success == total;
        }

        #endregion

        #region Calibration Data Backup/Restore

        // Calibration data partition names
        private static readonly string[] CalibrationPartitions = new[]
        {
            "nvitem", "nv", "nvram",           // NV data
            "wcnmodem", "wcn",                  // WiFi/BT calibration
            "l_modem", "modem",                 // RF calibration
            "l_fixnv1", "l_fixnv2",            // Fixed NV
            "l_runtimenv1", "l_runtimenv2",    // Runtime NV
            "prodnv", "prodinfo",              // Product information
            "miscdata",                         // Misc data
            "factorydata"                       // Factory data
        };

        /// <summary>
        /// Backup calibration data
        /// </summary>
        public async Task<bool> BackupCalibrationDataAsync(string outputDir, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            Log("[Spreadtrum] Starting backup calibration data...", Color.Cyan);

            // Get device partition table
            var partitions = await _client.ReadPartitionTableAsync();
            if (partitions == null || partitions.Count == 0)
            {
                Log("[Spreadtrum] Unable to read partition table", Color.Red);
                return false;
            }

            int backed = 0;

            foreach (var partition in partitions)
            {
                ct.ThrowIfCancellationRequested();

                // Check if calibration partition
                bool isCalibration = CalibrationPartitions.Any(c =>
                    partition.Name.ToLower().Contains(c.ToLower()));

                if (!isCalibration)
                    continue;

                Log(string.Format("[Spreadtrum] Backing up: {0}", partition.Name), Color.White);

                string outputPath = Path.Combine(outputDir, partition.Name + ".bin");

                // Read partition data
                byte[] data = await _client.ReadPartitionAsync(partition.Name, partition.Size, ct);

                if (data != null && data.Length > 0)
                {
                    File.WriteAllBytes(outputPath, data);
                    backed++;
                    Log(string.Format("[Spreadtrum] {0} backup success ({1})", partition.Name, FormatSize((ulong)data.Length)), Color.Gray);
                }
                else
                {
                    Log(string.Format("[Spreadtrum] {0} backup failed", partition.Name), Color.Orange);
                }
            }

            Log(string.Format("[Spreadtrum] Calibration data backup complete: {0} partitions", backed), Color.Green);
            return backed > 0;
        }

        /// <summary>
        /// Restore calibration data
        /// </summary>
        public async Task<bool> RestoreCalibrationDataAsync(string inputDir, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            if (!Directory.Exists(inputDir))
            {
                Log("[Spreadtrum] Backup directory does not exist", Color.Red);
                return false;
            }

            Log("[Spreadtrum] Starting restore calibration data...", Color.Cyan);

            var backupFiles = Directory.GetFiles(inputDir, "*.bin");
            if (backupFiles.Length == 0)
            {
                Log("[Spreadtrum] No backup files found", Color.Orange);
                return false;
            }

            int restored = 0;

            foreach (var backupFile in backupFiles)
            {
                ct.ThrowIfCancellationRequested();

                string partitionName = Path.GetFileNameWithoutExtension(backupFile);

                // Verify if calibration partition
                bool isCalibration = CalibrationPartitions.Any(c =>
                    partitionName.ToLower().Contains(c.ToLower()));

                if (!isCalibration)
                {
                    Log(string.Format("[Spreadtrum] Skipping non-calibration partition: {0}", partitionName), Color.Gray);
                    continue;
                }

                Log(string.Format("[Spreadtrum] Restoring: {0}", partitionName), Color.White);

                bool success = await FlashImageFileAsync(partitionName, backupFile, ct);

                if (success)
                {
                    restored++;
                }
            }

            Log(string.Format("[Spreadtrum] Calibration data restore complete: {0} partitions", restored), Color.Green);
            return restored > 0;
        }

        /// <summary>
        /// Get calibration partition list
        /// </summary>
        public string[] GetCalibrationPartitionNames()
        {
            return CalibrationPartitions;
        }

        #endregion

        #region Force Download Mode

        /// <summary>
        /// Enter Force Download mode
        /// </summary>
        public async Task<bool> EnterForceDownloadModeAsync()
        {
            Log("[Spreadtrum] Attempting to enter force download mode...", Color.Yellow);

            // Force download mode usually requires:
            // 1. Sending a special reset command
            // 2. Or holding a specific key while device is power off

            if (_client == null || !_client.IsConnected)
            {
                Log("[Spreadtrum] Please ensure device is connected", Color.Orange);
                return false;
            }

            try
            {
                // Send force download command
                bool result = await _client.EnterForceDownloadAsync();

                if (result)
                {
                    Log("[Spreadtrum] Entered force download mode", Color.Green);
                }
                else
                {
                    Log("[Spreadtrum] Failed to enter force download mode", Color.Red);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        #endregion

        #region Factory Reset

        /// <summary>
        /// Factory reset
        /// </summary>
        public async Task<bool> FactoryResetAsync(bool eraseUserData = true, bool eraseCache = true, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not ready", Color.Orange);
                return false;
            }

            Log("[Spreadtrum] Performing factory reset...", Color.Yellow);

            try
            {
                // Get partition table
                var partitions = await _client.ReadPartitionTableAsync();
                if (partitions == null)
                {
                    Log("[Spreadtrum] Unable to read partition table", Color.Red);
                    return false;
                }

                // Partitions that need to be erased
                var partitionsToErase = new List<string>();

                // Erase userdata
                if (eraseUserData)
                {
                    var userData = partitions.Find(p =>
                        p.Name.ToLower().Contains("userdata") ||
                        p.Name.ToLower() == "data");
                    if (userData != null)
                    {
                        partitionsToErase.Add(userData.Name);
                    }
                }

                // Erase cache
                if (eraseCache)
                {
                    var cache = partitions.Find(p => p.Name.ToLower().Contains("cache"));
                    if (cache != null)
                    {
                        partitionsToErase.Add(cache.Name);
                    }
                }

                // Erase metadata (Android 10+)
                var metadata = partitions.Find(p => p.Name.ToLower() == "metadata");
                if (metadata != null)
                {
                    partitionsToErase.Add(metadata.Name);
                }

                // Execute erase
                int erased = 0;
                foreach (var partName in partitionsToErase)
                {
                    ct.ThrowIfCancellationRequested();

                    Log(string.Format("[Spreadtrum] Erasing: {0}", partName), Color.White);
                    bool success = await _client.ErasePartitionAsync(partName);

                    if (success)
                    {
                        erased++;
                        Log(string.Format("[Spreadtrum] {0} erased", partName), Color.Gray);
                    }
                    else
                    {
                        Log(string.Format("[Spreadtrum] {0} erase failed", partName), Color.Orange);
                    }
                }

                Log(string.Format("[Spreadtrum] Factory reset complete: Erased {0} partitions", erased), Color.Green);
                return erased > 0;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Factory reset exception: {0}", ex.Message), Color.Red);
                return false;
            }
        }

        #endregion

        #region Security Info

        /// <summary>
        /// Get device security information
        /// </summary>
        public async Task<SprdSecurityInfo> GetSecurityInfoAsync()
        {
            if (!IsConnected)
            {
                Log("[Spreadtrum] Device not connected", Color.Orange);
                return null;
            }

            Log("[Spreadtrum] Reading security info...", Color.Cyan);

            try
            {
                var info = new SprdSecurityInfo();

                // Read eFuse data
                var efuseData = await _client.ReadEfuseAsync(0);
                if (efuseData != null)
                {
                    info.RawEfuseData = efuseData;
                    ParseEfuseData(efuseData, info);
                }

                // Read public key
                var pubKey = await _client.ReadPublicKeyAsync();
                if (pubKey != null && pubKey.Length > 0)
                {
                    info.PublicKeyHash = ComputeHash(pubKey);
                    Log(string.Format("[Spreadtrum] Public key hash: {0}...", info.PublicKeyHash.Substring(0, 16)), Color.Gray);
                }

                // Determine security status
                if (string.IsNullOrEmpty(info.PublicKeyHash) ||
                    info.PublicKeyHash.All(c => c == '0' || c == 'F' || c == 'f'))
                {
                    info.IsSecureBootEnabled = false;
                    Log("[Spreadtrum] Secure Boot: Not Enabled (Unfused)", Color.Yellow);
                }
                else
                {
                    info.IsSecureBootEnabled = true;
                    Log("[Spreadtrum] Secure Boot: Enabled", Color.White);
                }

                return info;
            }
            catch (Exception ex)
            {
                Log(string.Format("[Spreadtrum] Read security info failed: {0}", ex.Message), Color.Red);
                return null;
            }
        }

        private void ParseEfuseData(byte[] efuseData, SprdSecurityInfo info)
        {
            if (efuseData.Length < 4)
                return;

            // Parse eFuse flags
            uint flags = BitConverter.ToUInt32(efuseData, 0);

            info.IsEfuseLocked = (flags & 0x01) != 0;
            info.IsAntiRollbackEnabled = (flags & 0x02) != 0;

            if (efuseData.Length >= 8)
            {
                info.SecurityVersion = BitConverter.ToUInt32(efuseData, 4);
            }

            Log(string.Format("[Spreadtrum] eFuse Locked: {0}", info.IsEfuseLocked ? "Yes" : "No"), Color.Gray);
            Log(string.Format("[Spreadtrum] Anti-Rollback: {0}", info.IsAntiRollbackEnabled ? "Yes" : "No"), Color.Gray);
            Log(string.Format("[Spreadtrum] Security Version: {0}", info.SecurityVersion), Color.Gray);
        }

        private string ComputeHash(byte[] data)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        /// <summary>
        /// Get Flash information
        /// </summary>
        public async Task<SprdFlashInfo> GetFlashInfoAsync()
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                return null;
            }

            return await _client.ReadFlashInfoAsync();
        }

        #endregion

        #region Bootloader Unlock

        /// <summary>
        /// Get Bootloader status
        /// </summary>
        public async Task<SprdBootloaderStatus> GetBootloaderStatusAsync()
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                return null;
            }

            try
            {
                var status = new SprdBootloaderStatus();

                // Read eFuse for lock status
                var efuseData = await _client.ReadEfuseAsync();
                if (efuseData != null && efuseData.Length >= 4)
                {
                    // Check Secure Boot and unlock flags
                    uint efuseFlags = BitConverter.ToUInt32(efuseData, 0);
                    status.IsSecureBootEnabled = (efuseFlags & 0x01) != 0;
                    status.IsUnlocked = (efuseFlags & 0x10) != 0;  // BL unlock bit
                    status.IsUnfused = (efuseFlags & 0x01) == 0;   // Unfused
                }

                // Read public key hash to determine if Unfused
                var pubKey = await _client.ReadPublicKeyAsync();
                if (pubKey != null)
                {
                    string pkHash = ComputeHash(pubKey);
                    // Check if it's a known Unfused hash
                    if (SprdExploitDatabase.IsUnfusedDevice(pkHash))
                    {
                        status.IsUnfused = true;
                    }
                }

                // Read security version
                if (efuseData != null && efuseData.Length >= 8)
                {
                    status.SecurityVersion = BitConverter.ToUInt32(efuseData, 4);
                }

                // Get device model
                var flashInfo = await _client.ReadFlashInfoAsync();
                if (flashInfo != null)
                {
                    status.DeviceModel = flashInfo.ChipModel ?? "Unknown";
                }

                Log($"[Spreadtrum] BL Status: {(status.IsUnlocked ? "Unlocked" : "Locked")}, Unfused: {(status.IsUnfused ? "Yes" : "No")}", Color.Cyan);

                return status;
            }
            catch (Exception ex)
            {
                Log($"[Spreadtrum] Get BL status failed: {ex.Message}", Color.Red);
                return null;
            }
        }

        /// <summary>
        /// Unlock Bootloader (via exploit or direct unlock)
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync(bool useExploit = false)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not connected or not in FDL2", Color.Red);
                return false;
            }

            try
            {
                if (useExploit)
                {
                    // Use signature bypass exploit to unlock
                    Log("[Spreadtrum] Attempting signature bypass unlock...", Color.Yellow);

                    // First check device vulnerability
                    var vulnCheck = _exploitService.CheckVulnerability(0, "");
                    if (vulnCheck.HasVulnerability)
                    {
                        var exploitResult = await _exploitService.TryExploitAsync(
                            _client.GetPort(),
                            0);  // chipId=0 means auto-detection

                        if (exploitResult.Success)
                        {
                            // Send unlock command
                            return await SendUnlockCommandAsync();
                        }
                        else
                        {
                            Log($"[Spreadtrum] Exploit failed: {exploitResult.Message}", Color.Red);
                            return false;
                        }
                    }
                    else
                    {
                        Log("[Spreadtrum] No available exploit detected", Color.Orange);
                        // Try sending unlock command directly
                        return await SendUnlockCommandAsync();
                    }
                }
                else
                {
                    // Directly attempt to unlock (requires device support)
                    return await SendUnlockCommandAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"[Info] Unlock fail: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Unlock Bootloader with code
        /// </summary>
        public async Task<bool> UnlockBootloaderWithCodeAsync(string unlockCode)
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not connected or not in FDL2", Color.Red);
                return false;
            }

            try
            {
                Log($"[Spreadtrum] Unlocking with code...", Color.Yellow);

                // Convert hex string to byte array
                byte[] codeBytes = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    codeBytes[i] = Convert.ToByte(unlockCode.Substring(i * 2, 2), 16);
                }

                // Send unlock command
                return await _client.UnlockAsync(codeBytes);
            }
            catch (Exception ex)
            {
                Log($"[Spreadtrum] Unlock failed: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Relock Bootloader
        /// </summary>
        public async Task<bool> RelockBootloaderAsync()
        {
            if (!IsConnected || CurrentStage != FdlStage.FDL2)
            {
                Log("[Spreadtrum] Device not connected or not in FDL2", Color.Red);
                return false;
            }

            try
            {
                Log("[Spreadtrum] Relocking Bootloader...", Color.Yellow);

                // Send lock command
                // Use all zeros as lock identifier
                byte[] lockCode = new byte[8];
                return await _client.UnlockAsync(lockCode, true);  // true = relock
            }
            catch (Exception ex)
            {
                Log($"[Spreadtrum] Lock failed: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Send unlock command
        /// </summary>
        private async Task<bool> SendUnlockCommandAsync()
        {
            try
            {
                // Write unlock flag to specific partition or eFuse
                // Spreadtrum devices usually store unlock status in misc or dedicated partition

                // Option 1: Write unlock flag to misc partition
                byte[] unlockFlag = new byte[16];
                unlockFlag[0] = 0x55;  // Unlock magic
                unlockFlag[1] = 0x4E;  // 'U'
                unlockFlag[2] = 0x4C;  // 'N'
                unlockFlag[3] = 0x4B;  // 'L'
                unlockFlag[4] = 0x4F;  // 'O'
                unlockFlag[5] = 0x43;  // 'C'
                unlockFlag[6] = 0x4B;  // 'K'
                unlockFlag[7] = 0x45;  // 'E'
                unlockFlag[8] = 0x44;  // 'D'

                // Write to FRP (Factory Reset Protection) partition
                bool frpResult = await _client.WritePartitionAsync("frp", unlockFlag);

                // Write to misc partition
                bool miscResult = await _client.WritePartitionAsync("misc", unlockFlag);

                if (frpResult || miscResult)
                {
                    Log("[Spreadtrum] Unlock flag write success", Color.Green);
                    return true;
                }
                else
                {
                    // Attempt FDL direct unlock command
                    return await _client.UnlockAsync(unlockFlag);
                }
            }
            catch (Exception ex)
            {
                Log($"[Spreadtrum] Failed to send unlock command: {ex.Message}", Color.Red);
                return false;
            }
        }

        #endregion

        #region Helper Methods

        private string FormatSize(ulong size)
        {
            if (size >= 1024UL * 1024 * 1024)
                return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
            if (size >= 1024 * 1024)
                return string.Format("{0:F2} MB", size / (1024.0 * 1024));
            if (size >= 1024)
                return string.Format("{0:F2} KB", size / 1024.0);
            return string.Format("{0} B", size);
        }

        private void Log(string message, Color color)
        {
            OnLog?.Invoke(message, color);
        }

        public void Dispose()
        {
            Disconnect();
            _portDetector?.Dispose();

            // Release CancellationTokenSource (ignore exceptions, ensure full cleanup)
            if (_cts != null)
            {
                try { _cts.Cancel(); }
                catch (ObjectDisposedException) { /* Already disposed, ignore */ }
                try { _cts.Dispose(); }
                catch (ObjectDisposedException) { /* Already disposed, ignore */ }
                _cts = null;
            }

            // Release watchdog
            _watchdog?.Dispose();
        }

        /// <summary>
        /// Safely reset CancellationTokenSource
        /// </summary>
        private void ResetCancellationToken()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); }
                catch (ObjectDisposedException) { /* Already disposed, ignore */ }
                catch (Exception ex) { Log($"[Spreadtrum] Cancel token exception: {ex.Message}", Color.Gray); }
                try { _cts.Dispose(); }
                catch (Exception ex) { Log($"[Spreadtrum] Dispose token exception: {ex.Message}", Color.Gray); }
            }
            _cts = new CancellationTokenSource();
        }

        #endregion
    }
}
