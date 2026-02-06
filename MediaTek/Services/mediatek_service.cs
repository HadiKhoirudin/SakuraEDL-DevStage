// ============================================================================
// LoveAlways - MediaTek Flashing Service
// MediaTek Flashing Service
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.MediaTek.Database;
using LoveAlways.MediaTek.Exploit;
using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaEntry = LoveAlways.MediaTek.Models.DaEntry;

namespace LoveAlways.MediaTek.Services
{
    /// <summary>
    /// Protocol Type
    /// </summary>
    public enum MtkProtocolType
    {
        Auto,       // Auto selection
        Xml,        // XML V6 protocol
        XFlash      // XFlash binary protocol
    }

    /// <summary>
    /// MediaTek Flashing Service - Main Service Class
    /// </summary>
    public class MediatekService : IDisposable
    {
        private BromClient _bromClient;
        private XmlDaClient _xmlClient;
        private XFlashClient _xflashClient;
        private DaLoader _daLoader;
        private CancellationTokenSource _cts;

        // Protocol Types
        private MtkProtocolType _protocolType = MtkProtocolType.Auto;
        private bool _useXFlash = false;

        // Events
        public event Action<string, Color> OnLog;
        public event Action<int, int> OnProgress;
        public event Action<MtkDeviceState> OnStateChanged;
        public event Action<MtkDeviceInfo> OnDeviceConnected;
        public event Action<MtkDeviceInfo> OnDeviceDisconnected;

        // Properties
        public bool IsConnected => _bromClient?.IsConnected ?? false;
        public bool IsBromMode => _bromClient?.IsBromMode ?? false;
        public MtkDeviceState State => _bromClient?.State ?? MtkDeviceState.Disconnected;
        public MtkChipInfo ChipInfo => _bromClient?.ChipInfo;
        public MtkProtocolType Protocol => _protocolType;
        public bool IsXFlashMode => _useXFlash;

        // Current device info
        public MtkDeviceInfo CurrentDevice { get; private set; }

        // DA file paths
        public string DaFilePath { get; private set; }

        // Custom DA config
        public string CustomDa1Path { get; private set; }
        public string CustomDa2Path { get; private set; }

        public MediatekService()
        {
            _bromClient = new BromClient(
                msg => Log(msg, Color.White),
                msg => Log(msg, Color.Gray),
                progress => OnProgress?.Invoke((int)progress, 100)
            );

            _daLoader = new DaLoader(
                _bromClient,
                msg => Log(msg, Color.White),
                progress => OnProgress?.Invoke((int)progress, 100)
            );
        }

        #region Device Connection

        /// <summary>
        /// Connect device
        /// </summary>
        public async Task<bool> ConnectAsync(string comPort, int baudRate = 115200, CancellationToken ct = default)
        {
            try
            {
                Log($"[MediaTek] Connecting device: {comPort}", Color.Cyan);

                if (!await _bromClient.ConnectAsync(comPort, baudRate, ct))
                {
                    Log("[MediaTek] Failed to open serial port", Color.Red);
                    return false;
                }

                // Execute handshake
                if (!await _bromClient.HandshakeAsync(100, ct))
                {
                    Log("[MediaTek] BROM handshake failed", Color.Red);
                    return false;
                }

                // Initialize device
                if (!await _bromClient.InitializeAsync(false, ct))
                {
                    Log("[MediaTek] Device initialization failed", Color.Red);
                    return false;
                }

                // Create device info
                CurrentDevice = new MtkDeviceInfo
                {
                    ComPort = comPort,
                    IsDownloadMode = true,
                    ChipInfo = _bromClient.ChipInfo,
                    MeId = _bromClient.MeId,
                    SocId = _bromClient.SocId
                };

                Log($"[MediaTek] ✓ Connection successful: {_bromClient.ChipInfo.GetChipName()}", Color.Green);

                // Check DAA status and notify user
                bool daaEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.DaaEnabled);
                bool slaEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.SlaEnabled);
                bool sbcEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.SbcEnabled);

                if (daaEnabled)
                {
                    Log("[MediaTek] ⚠ Warning: Device enabled DAA (Download Agent Authentication)", Color.Orange);
                    Log("[MediaTek] ⚠ Requires officially signed DA or bypass via exploit", Color.Orange);
                }
                if (slaEnabled)
                {
                    Log("[MediaTek] ⚠ Warning: Device enabled SLA (Secure Link Auth)", Color.Yellow);
                }
                if (sbcEnabled && !_bromClient.IsBromMode)
                {
                    Log("[MediaTek] Note: Preloader mode + SBC enabled, might need Carbonara exploit", Color.Cyan);
                }

                OnDeviceConnected?.Invoke(CurrentDevice);
                OnStateChanged?.Invoke(_bromClient.State);

                return true;
            }
            catch (Exception ex)
            {
                Log($"[MediaTek] Connection exception: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _bromClient?.Disconnect();
            _xmlClient?.Dispose();
            _xmlClient = null;
            _xflashClient?.Dispose();
            _xflashClient = null;
            _useXFlash = false;

            if (CurrentDevice != null)
            {
                OnDeviceDisconnected?.Invoke(CurrentDevice);
                CurrentDevice = null;
            }

            Log("[MediaTek] Disconnected", Color.Gray);
        }

        /// <summary>
        /// Set protocol type
        /// </summary>
        public void SetProtocol(MtkProtocolType protocol)
        {
            _protocolType = protocol;
            Log($"[MediaTek] Protocol set: {protocol}", Color.Cyan);
        }

        /// <summary>
        /// Enable CRC32 checksum (XFlash protocol only)
        /// </summary>
        public async Task<bool> EnableChecksumAsync(CancellationToken ct = default)
        {
            if (_xflashClient != null)
            {
                return await _xflashClient.SetChecksumLevelAsync(ChecksumAlgorithm.CRC32, ct);
            }
            return false;
        }

        /// <summary>
        /// Initialize XFlash client
        /// </summary>
        private async Task InitializeXFlashClientAsync(CancellationToken ct = default)
        {
            // Decide whether to use XFlash based on protocol settings
            if (_protocolType == MtkProtocolType.Xml)
            {
                Log("[MediaTek] Using XML protocol (user specified)", Color.Gray);
                _useXFlash = false;
                return;
            }

            try
            {
                Log("[MediaTek] Initializing XFlash client...", Color.Gray);

                // Create XFlash client (shared port lock)
                _xflashClient = new XFlashClient(
                    _bromClient.GetPort(),
                    msg => Log(msg, Color.White),
                    progress => OnProgress?.Invoke((int)progress, 100),
                    _bromClient.GetPortLock()
                );

                // Try to detect storage type
                if (await _xflashClient.DetectStorageAsync(ct))
                {
                    Log($"[MediaTek] ✓ XFlash client ready (Storage: {_xflashClient.Storage})", Color.Green);

                    // Get packet length
                    int packetLen = await _xflashClient.GetPacketLengthAsync(ct);
                    if (packetLen > 0)
                    {
                        Log($"[MediaTek] Packet size: {packetLen} bytes", Color.Gray);
                    }

                    // If auto mode, enable XFlash
                    if (_protocolType == MtkProtocolType.Auto || _protocolType == MtkProtocolType.XFlash)
                    {
                        _useXFlash = true;
                        Log("[MediaTek] ✓ XFlash binary protocol enabled", Color.Cyan);
                    }
                }
                else
                {
                    Log("[MediaTek] XFlash storage detection failed, using XML protocol", Color.Orange);
                    _useXFlash = false;
                    _xflashClient?.Dispose();
                    _xflashClient = null;
                }
            }
            catch (Exception ex)
            {
                Log($"[MediaTek] XFlash initialization failed: {ex.Message}", Color.Orange);
                Log("[MediaTek] Falling back to XML protocol", Color.Gray);
                _useXFlash = false;
                _xflashClient?.Dispose();
                _xflashClient = null;
            }
        }

        /// <summary>
        /// Switch to XFlash protocol
        /// </summary>
        public async Task<bool> SwitchToXFlashAsync(CancellationToken ct = default)
        {
            if (_xflashClient != null && _xflashClient.IsConnected)
            {
                _useXFlash = true;
                Log("[MediaTek] Switched to XFlash protocol", Color.Cyan);
                return true;
            }

            // Try initialization
            _protocolType = MtkProtocolType.XFlash;
            await InitializeXFlashClientAsync(ct);
            return _useXFlash;
        }

        /// <summary>
        /// Switch to XML protocol
        /// </summary>
        public void SwitchToXml()
        {
            _useXFlash = false;
            _protocolType = MtkProtocolType.Xml;
            Log("[MediaTek] Switched to XML protocol", Color.Cyan);
        }

        #endregion

        #region BROM Exploit

        /// <summary>
        /// Execute BROM Exploit to disable security protection
        /// Reference: SP Flash Tool and mtkclient flow
        /// Note: SEND_CERT (0xE0) command is only valid in BROM mode
        /// </summary>
        public async Task<bool> RunBromExploitAsync(CancellationToken ct = default)
        {
            if (!IsConnected || _bromClient.HwCode == 0)
            {
                Log("[MediaTek] Device not connected", Color.Red);
                return false;
            }

            ushort hwCode = _bromClient.HwCode;
            uint targetConfig = (uint)_bromClient.TargetConfig;

            Log($"[MediaTek] ═══════════════════════════════════════", Color.Yellow);
            Log($"[MediaTek] Current Target Config: 0x{targetConfig:X8}", Color.Yellow);
            Log($"[MediaTek] ═══════════════════════════════════════", Color.Yellow);

            // Check device mode - BROM Exploit can only be executed in BROM mode
            if (!_bromClient.IsBromMode)
            {
                Log("[MediaTek] ⚠ Device in Preloader mode, BROM Exploit (SEND_CERT) not applicable", Color.Orange);
                Log("[MediaTek] Note: Preloader mode requires DA2 level exploit (e.g. ALLINONE-SIGNATURE)", Color.Yellow);
                Log("[MediaTek] Note: Or try setting device into BROM mode (short TP)", Color.Yellow);
                return false;  // Not an error, just not applicable
            }

            // Check if exploit is needed
            if (targetConfig == 0)
            {
                Log("[MediaTek] ✓ Device has no security protection, no Exploit needed", Color.Green);
                return true;
            }

            Log("[MediaTek] Device in BROM mode, trying BROM Exploit...", Color.Cyan);

            // Set Payload Manager logger
            ExploitPayloadManager.SetLogger(msg => Log(msg, Color.Gray));

            // Get payload from embedded resources or file
            byte[] payload = ExploitPayloadManager.GetPayload(hwCode);
            if (payload == null || payload.Length == 0)
            {
                Log($"[MediaTek] ⚠ Could not find Exploit Payload for HW Code 0x{hwCode:X4} ({ExploitPayloadManager.GetChipName(hwCode)})", Color.Orange);
                Log("[MediaTek] ⚠ Attempting to continue, might fail", Color.Orange);
                return false;
            }

            Log($"[MediaTek] Using Payload: {ExploitPayloadManager.GetChipName(hwCode)} ({payload.Length} bytes)", Color.Cyan);

            // Save current port info
            string originalPort = _bromClient.PortName;

            // Send exploit payload
            bool sendResult = await _bromClient.SendExploitPayloadAsync(payload, ct);
            if (!sendResult)
            {
                Log("[MediaTek] Failed to send Exploit Payload", Color.Red);
                return false;
            }

            Log("[MediaTek] ✓ Exploit Payload sent, waiting for device re-enumeration...", Color.Yellow);

            // Disconnect current connection
            _bromClient.Disconnect();

            // Wait for device re-enumeration
            await Task.Delay(2000, ct);

            // Try to reconnect
            Log("[MediaTek] Attempting to reconnect...", Color.Cyan);

            string newPort = await WaitForNewMtkPortAsync(ct, 10000);
            if (string.IsNullOrEmpty(newPort))
            {
                Log("[MediaTek] ⚠ No new port detected, trying to reconnect using original port", Color.Yellow);
                newPort = originalPort;
            }
            else
            {
                Log($"[MediaTek] Detected new port: {newPort}", Color.Cyan);
            }

            // Reconnect
            bool reconnected = await ConnectAsync(newPort, 115200, ct);
            if (!reconnected)
            {
                Log("[MediaTek] Reconnection failed", Color.Red);
                return false;
            }

            // Check new Target Config
            uint newTargetConfig = (uint)_bromClient.TargetConfig;
            Log($"[MediaTek] ═══════════════════════════════════════", Color.Green);
            Log($"[MediaTek] New Target Config: 0x{newTargetConfig:X8}", Color.Green);
            Log($"[MediaTek] ═══════════════════════════════════════", Color.Green);

            if (newTargetConfig == 0)
            {
                Log("[MediaTek] ✓ Exploit successful! Security protection disabled", Color.Green);
                return true;
            }
            else if (newTargetConfig < targetConfig)
            {
                Log("[MediaTek] ✓ Exploit partially successful, some protection disabled", Color.Yellow);
                return true;
            }
            else
            {
                Log("[MediaTek] ⚠ Exploit might not have taken effect, Target Config unchanged", Color.Orange);
                return false;
            }
        }

        #endregion

        #region DA Loading

        /// <summary>
        /// Set DA file path
        /// </summary>
        public void SetDaFilePath(string filePath)
        {
            if (File.Exists(filePath))
            {
                Debug.WriteLine($"Da File : {filePath}");
                DaFilePath = filePath;
                MtkDaDatabase.SetDaFilePath(filePath);
                Log($"[MediaTek] DA file: {Path.GetFileName(filePath)}", Color.Cyan);
            }
            else
            {
                Debug.WriteLine($"Da File : Not found!");
            }
        }

        /// <summary>
        /// Set custom DA1
        /// </summary>
        public void SetCustomDa1(string filePath)
        {
            if (File.Exists(filePath))
            {
                CustomDa1Path = filePath;
                Log($"[MediaTek] Custom DA1: {Path.GetFileName(filePath)}", Color.Cyan);
            }
        }

        /// <summary>
        /// Set custom DA2
        /// </summary>
        public void SetCustomDa2(string filePath)
        {
            if (File.Exists(filePath))
            {
                CustomDa2Path = filePath;
                Log($"[MediaTek] Custom DA2: {Path.GetFileName(filePath)}", Color.Cyan);
            }
        }

        /// <summary>
        /// Load DA
        /// </summary>
        public async Task<bool> LoadDaAsync(CancellationToken ct = default)
        {
            if (!IsConnected || _bromClient.HwCode == 0)
            {
                Log("[MediaTek] Device not connected", Color.Red);
                return false;
            }

            ushort hwCode = _bromClient.HwCode;
            Log($"[MediaTek] Loading DA (HW Code: 0x{hwCode:X4})", Color.Cyan);

            DaEntry da1 = null;
            DaEntry da2 = null;

            // 1. Try using custom DA
            if (!string.IsNullOrEmpty(CustomDa1Path) && File.Exists(CustomDa1Path))
            {
                byte[] da1Data = File.ReadAllBytes(CustomDa1Path);

                // Process DA data (send full file, do not truncate)
                // According to ChimeraTool packet analysis: although declared size is small, full file is sent
                da1Data = ProcessDaData(da1Data);

                // Detect DA format (Legacy vs V6)
                // Legacy DA: Starts with ARM instructions (0xEA = B instruction, 0xEB = BL instruction)
                // V6 DA: Usually begins with "MTK_", "hvea" or other signatures
                // ELF DA: Starts with 0x7F 'E' 'L' 'F'

                bool isLegacyDa = false;
                bool isElfDa = false;

                if (da1Data.Length > 4)
                {
                    // Check if it is ELF format
                    if (da1Data[0] == 0x7F && da1Data[1] == 'E' && da1Data[2] == 'L' && da1Data[3] == 'F')
                    {
                        isElfDa = true;
                        Log("[MediaTek] Detected ELF DA format", Color.Yellow);
                    }
                    // Check if it is ARM branch instruction (Legacy DA feature)
                    else if (da1Data[3] == 0xEA || da1Data[3] == 0xEB)
                    {
                        isLegacyDa = true;
                    }
                    // Check for V6 features
                    else if (da1Data.Length > 8)
                    {
                        string header = System.Text.Encoding.ASCII.GetString(da1Data, 0, Math.Min(8, da1Data.Length));
                        if (header.Contains("MTK") || header.Contains("hvea"))
                        {
                            Log($"[MediaTek] Detected V6 DA feature: {header.Substring(0, 4)}", Color.Yellow);
                        }
                    }
                }

                int sigLen;
                int daType;

                // Detect signature length: check if there is a valid signature at the end of the file
                // Official signed DA usually has 0x1000 (4096) bytes signature
                sigLen = DetectDaSignatureLength(da1Data);
                Log($"[MediaTek] Signature detection result: 0x{sigLen:X} ({sigLen} bytes)", Color.Gray);

                // Prioritize judgment of format based on signature length
                if (sigLen == 0x1000)
                {
                    // Official signed DA (e.g. extracted from SP Flash Tool)
                    // Even if its header looks like Legacy (ARM branch), it should be handled as V6
                    daType = (int)DaMode.Xml;
                    Log($"[MediaTek] DA Format: Official signed DA (V6, Signature length: 0x{sigLen:X})", Color.Yellow);
                }
                else if (isElfDa)
                {
                    // ELF format is usually V6 or newer
                    if (sigLen == 0)
                        sigLen = MtkDaDatabase.GetSignatureLength(hwCode, false);
                    daType = (int)MtkDaDatabase.GetDaMode(hwCode);
                    Log($"[MediaTek] DA Format: ELF/V6 (Signature length: 0x{sigLen:X})", Color.Yellow);
                }
                else if (isLegacyDa && sigLen == 0)
                {
                    // Pure Legacy DA (ARM header, no official signature)
                    sigLen = 0x100;
                    daType = (int)DaMode.Legacy;
                    Log("[MediaTek] DA Format: Legacy (Signature length: 0x100)", Color.Yellow);
                }
                else
                {
                    // Default to using the chip's recommended mode
                    if (sigLen == 0)
                        sigLen = MtkDaDatabase.GetSignatureLength(hwCode, false);
                    daType = (int)MtkDaDatabase.GetDaMode(hwCode);
                    Log($"[MediaTek] DA Format: Auto detected {(DaMode)daType} (Signature length: 0x{sigLen:X})", Color.Yellow);
                }

                // Prioritize device-reported address, fallback to database address
                uint da1Addr = _bromClient.ChipInfo?.DaPayloadAddr ?? MtkDaDatabase.GetDa1Address(hwCode);
                if (da1Addr == 0)
                    da1Addr = MtkDaDatabase.GetDa1Address(hwCode);

                da1 = new DaEntry
                {
                    Name = "Custom_DA1",
                    LoadAddr = da1Addr,
                    SignatureLen = sigLen,
                    Data = da1Data,
                    DaType = daType
                };
                Log($"[MediaTek] Using custom DA1 (Load address: 0x{da1Addr:X})", Color.Yellow);
            }

            if (!string.IsNullOrEmpty(CustomDa2Path) && File.Exists(CustomDa2Path))
            {
                byte[] da2Data = File.ReadAllBytes(CustomDa2Path);

                // DA2 format is determined by DA1 mode, not the header
                // V6/XML protocol: DA2 is uploaded via XML commands, no separate signature
                // Legacy protocol: DA2 is uploaded via BROM, might have signature

                int sigLen;
                int daType;

                // Get chip's DA mode
                var da2ChipMode = MtkDaDatabase.GetDaMode(hwCode);

                if (da2ChipMode == DaMode.Xml || da2ChipMode == DaMode.XFlash)
                {
                    // V6/XFlash: DA2 is uploaded via XML boot_to or UPLOAD_DA commands
                    // No separate signature (signature verification is done in DA1)
                    sigLen = 0;
                    daType = (int)da2ChipMode;
                    Log($"[MediaTek] DA2 Format: V6/XML (No separate signature, uploaded via XML protocol)", Color.Yellow);
                }
                else
                {
                    // Legacy: DA2 might have a signature
                    sigLen = MtkDaDatabase.GetSignatureLength(hwCode, true);
                    daType = (int)DaMode.Legacy;
                    Log($"[MediaTek] DA2 Format: Legacy (Signature: 0x{sigLen:X})", Color.Yellow);
                }

                da2 = new DaEntry
                {
                    Name = "Custom_DA2",
                    LoadAddr = MtkDaDatabase.GetDa2Address(hwCode),
                    SignatureLen = sigLen,
                    Data = da2Data,
                    DaType = daType
                };
                Log($"[MediaTek] Using custom DA2: {da2Data.Length} bytes, Address: 0x{da2.LoadAddr:X8}", Color.Yellow);
            }

            // 2. If no custom DA, extract from AllInOne DA file
            if (da1 == null && !string.IsNullOrEmpty(DaFilePath))
            {
                var daResult = _daLoader.ParseDaFile(DaFilePath, hwCode);
                if (daResult.HasValue)
                {
                    da1 = daResult.Value.da1;
                    da2 = da2 ?? daResult.Value.da2;
                }
            }

            if (da1 == null)
            {
                Log("[MediaTek] No available DA1 found", Color.Red);
                return false;
            }

            // Check if DA mode matches
            //var chipDaMode = MtkDaDatabase.GetDaMode(hwCode);
            var daDaMode = (DaMode)da1.DaType;

            //if (daDaMode != chipDaMode)
            //{
            //    Log($"[MediaTek] ⚠ DA mode mismatch: Chip requires {chipDaMode}, DA file is {daDaMode}", Color.Orange);
            //    Log("[MediaTek] Suggest using correct format DA file", Color.Orange);
            //}

            Log($"[MediaTek] DA Mode: {daDaMode}, Load address: 0x{da1.LoadAddr:X8}", Color.Gray);

            // ═══════════════════════════════════════════════════════════════════
            // Correct flow (Reference: SP Flash Tool and mtkclient):
            // 0. If device has security protection (Target Config != 0), execute BROM Exploit first
            // 1. Regardless of BROM or Preloader mode, upload DA1
            // 2. After DA1 runs, detect device source via get_connection_agent
            // 3. If connagent=="preloader" and SBC enabled, use Carbonara
            // ═══════════════════════════════════════════════════════════════════

            // 0. Check if BROM Exploit needs to be executed
            uint targetConfig = (uint)_bromClient.TargetConfig;
            bool isBromMode = _bromClient.IsBromMode;

            if (targetConfig != 0)
            {
                Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);
                Log($"[MediaTek] Security protection detected (Target Config: 0x{targetConfig:X8})", Color.Yellow);

                if (isBromMode)
                {
                    // BROM mode: Try BROM Exploit
                    Log("[MediaTek] Attempting BROM Exploit to disable protection...", Color.Yellow);
                    Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);

                    bool exploitResult = await RunBromExploitAsync(ct);
                    if (exploitResult)
                    {
                        // After successful Exploit, targetConfig should become 0
                        targetConfig = (uint)_bromClient.TargetConfig;
                        if (targetConfig == 0)
                        {
                            Log("[MediaTek] ✓ Security protection successfully disabled!", Color.Green);
                        }
                    }
                    else
                    {
                        Log("[MediaTek] ⚠ BROM Exploit failed, proceeding with DA upload...", Color.Orange);
                        Log("[MediaTek] ⚠ Might fail if device has DAA enabled", Color.Orange);
                    }
                }
                else
                {
                    // Preloader mode: BROM Exploit not applicable
                    Log("[MediaTek] Device mode: Preloader (BROM Exploit not applicable)", Color.Yellow);
                    Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);

                    // Check for DA2 level exploit support
                    string exploitType = MtkChipDatabase.GetExploitType(_bromClient.HwCode);
                    if (!string.IsNullOrEmpty(exploitType))
                    {
                        Log($"[MediaTek] ✓ This chip supports {exploitType} exploit (DA2 level)", Color.Green);
                        Log("[MediaTek] Attempting to upload DA, DA2 exploit can be executed after successful upload...", Color.Cyan);
                    }
                    else
                    {
                        Log("[MediaTek] ⚠ Preloader mode + DAA enabled", Color.Orange);
                        Log("[MediaTek] Requires officially signed DA or setting device into BROM mode", Color.Orange);
                    }
                }
            }

            // Record initial mode (detected during handshake)
            bool initialIsBrom = _bromClient.IsBromMode;
            Log($"[MediaTek] Initial mode: {(initialIsBrom ? "BROM" : "Preloader")}", Color.Gray);

            // 1. Upload DA1 (Both modes require this!)
            Log("[MediaTek] Uploading Stage1 DA...", Color.Cyan);
            if (!await _daLoader.UploadDa1Async(da1, ct))
            {
                Log("[MediaTek] DA1 upload failed", Color.Red);
                return false;
            }

            Log("[MediaTek] ✓ Stage1 DA upload successful", Color.Green);

            // 2. Check if port is still open (might be closed due to USB re-enumeration)
            if (!_bromClient.IsPortOpen)
            {
                Log("[MediaTek] ⚠ Port closed, device is re-enumerating USB...", Color.Yellow);
                Log("[MediaTek] Waiting for new COM port...", Color.Gray);

                // Wait for new port and reconnect
                string newPort = await WaitForNewMtkPortAsync(ct, 15000);
                if (string.IsNullOrEmpty(newPort))
                {
                    Log("[MediaTek] No new MTK port detected", Color.Red);
                    return false;
                }

                Log($"[MediaTek] Detected new port: {newPort}", Color.Cyan);

                // Reconnect to new port (no handshake needed, directly to DA)
                if (!await _bromClient.ConnectAsync(newPort, 115200, ct))
                {
                    Log("[MediaTek] Reconnection failed", Color.Red);
                    return false;
                }

                // Set state to DA1 loaded
                _bromClient.State = MtkDeviceState.Da1Loaded;
                Log("[MediaTek] ✓ Reconnected successful (DA mode)", Color.Green);
            }

            // 3. Create XML DA client (shared port lock to ensure thread safety)
            _xmlClient = new XmlDaClient(
                _bromClient.GetPort(),
                msg => Log(msg, Color.White),
                msg => Log(msg, Color.Gray),
                progress => OnProgress?.Invoke((int)progress, 100),
                _bromClient.GetPortLock()  // Shared port lock
            );

            // 4. Wait for DA1 ready (waiting for sync signal)
            Log("[MediaTek] Waiting for DA1 ready...", Color.Gray);
            if (!await _xmlClient.WaitForDaReadyAsync(30000, ct))
            {
                Log("[MediaTek] Timeout waiting for DA1 ready", Color.Red);
                return false;
            }

            Log("[MediaTek] ✓ DA1 ready", Color.Green);

            // 4. Send runtime parameters (Required, reference: ChimeraTool)
            Log("[MediaTek] Sending runtime parameters...", Color.Gray);
            bool runtimeParamsSet = await _xmlClient.SetRuntimeParametersAsync(ct);
            if (!runtimeParamsSet)
            {
                Log("[MediaTek] ⚠ Failed to set runtime parameters, continuing...", Color.Orange);
            }

            // 5. Judge device source based on initial handshake mode
            // Preloader mode means starting from Preloader
            bool isPreloaderSource = !_bromClient.IsBromMode;

            Log($"[MediaTek] Device source: {(isPreloaderSource ? "Preloader" : "BROM")}", Color.Cyan);

            if (isPreloaderSource)
            {
                Log("[MediaTek] DA1 detected: Device started from Preloader", Color.Yellow);
            }
            else
            {
                Log("[MediaTek] DA1 detected: Device started from BROM", Color.Cyan);

                // BROM boot requires sending EMI configuration (DRAM initialization)
                if (Common.MtkEmiConfig.IsRequired(hwCode))
                {
                    Log("[MediaTek] Detected EMI configuration needed...", Color.Yellow);

                    var emiConfig = Common.MtkEmiConfig.GetConfig(hwCode);
                    if (emiConfig != null && emiConfig.ConfigData.Length > 0)
                    {
                        Log($"[MediaTek] Sending EMI config: {emiConfig.ConfigLength} bytes", Color.Cyan);

                        bool emiSuccess = await _bromClient.SendEmiConfigAsync(emiConfig.ConfigData, ct);
                        if (!emiSuccess)
                        {
                            Log("[MediaTek] Warning: Failed to send EMI config, device might not work correctly", Color.Orange);
                            // Do not terminate flow, as some devices might continue even if EMI fails
                        }
                        else
                        {
                            Log("[MediaTek] ✓ EMI config sent successful", Color.Green);
                        }
                    }
                    else
                    {
                        Log("[MediaTek] Warning: EMI configuration data not found", Color.Orange);
                        Log("[MediaTek] Note: If device doesn't work, please provide device EMI config file", Color.Gray);
                    }
                }
                else
                {
                    Log("[MediaTek] This chip does not require EMI config", Color.Gray);
                }
            }

            // 5. Check if Carbonara exploit needs to be used
            // Conditions: connagent=="preloader" AND SBC enabled AND DA2 available
            bool sbcEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.SbcEnabled);
            bool useExploit = isPreloaderSource && sbcEnabled && da2 != null;

            Log($"[MediaTek] SBC Status: {(sbcEnabled ? "Enabled" : "Disabled")}", Color.Gray);

            if (useExploit)
            {
                Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);
                Log("[MediaTek] Carbonara conditions met: Preloader + SBC", Color.Yellow);
                Log("[MediaTek] Executing Carbonara runtime exploit...", Color.Yellow);
                Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);

                var exploit = new CarbonaraExploit(msg => Log(msg, Color.Yellow));

                // Check if device is patched by vendor
                if (exploit.IsDevicePatched(da1.Data))
                {
                    Log("[MediaTek] ⚠ DA1 has been patched by vendor, trying normal DA2 upload", Color.Orange);
                    useExploit = false;
                }
                else
                {
                    // Prepare exploit data
                    var exploitData = exploit.PrepareExploit(
                        da1.Data,
                        da2.Data,
                        da1.LoadAddr,
                        da1.SignatureLen,
                        da2.SignatureLen,
                        isV6: true
                    );

                    if (exploitData != null)
                    {
                        var (newHash, hashOffset, patchedDa2) = exploitData.Value;

                        // Execute runtime exploit
                        bool exploitSuccess = await _xmlClient.ExecuteCarbonaraAsync(
                            da1.LoadAddr,
                            hashOffset,
                            newHash,
                            da2.LoadAddr,
                            patchedDa2,
                            ct
                        );

                        if (exploitSuccess)
                        {
                            Log("[MediaTek] ✓ Carbonara exploit successful", Color.Green);
                            OnStateChanged?.Invoke(MtkDeviceState.Da2Loaded);
                            return true;
                        }
                        else
                        {
                            Log("[MediaTek] Carbonara exploit failed, trying normal upload", Color.Orange);
                            useExploit = false;
                        }
                    }
                    else
                    {
                        Log("[MediaTek] Could not prepare exploit data, trying normal upload", Color.Orange);
                        useExploit = false;
                    }
                }
            }

            // 6. Normal upload DA2 (if exploit not used or failed)
            if (!useExploit && da2 != null)
            {
                Log("[MediaTek] Uploading Stage2 DA (normal mode)...", Color.Cyan);
                if (!await _daLoader.UploadDa2Async(da2, _xmlClient, ct))
                {
                    Log("[MediaTek] DA2 upload failed", Color.Red);
                    return false;
                }
            }

            Log("[MediaTek] ✓ DA loading completed", Color.Green);
            OnStateChanged?.Invoke(MtkDeviceState.Da2Loaded);

            // 7. Initialize XFlash client (if needed)
            //await InitializeXFlashClientAsync(ct);

            // 8. Check and execute AllinoneSignature exploit (DA2 level)
            string chipExploitType = MtkChipDatabase.GetExploitType(_bromClient.HwCode);
            if (chipExploitType == "AllinoneSignature" && IsAllinoneSignatureVulnerable())
            {
                Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);
                Log("[MediaTek] Supported exploit detected: AllinoneSignature", Color.Yellow);
                Log("[MediaTek] Attempting DA2 level exploit...", Color.Yellow);
                Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);

                bool exploitSuccess = await RunAllinoneSignatureExploitAsync(null, null, ct);
                if (exploitSuccess)
                {
                    Log("[MediaTek] ✓ AllinoneSignature exploit successful", Color.Green);
                    Log("[MediaTek] Device security restrictions disabled", Color.Green);
                }
                else
                {
                    Log("[MediaTek] ⚠ AllinoneSignature exploit failed", Color.Orange);
                    Log("[MediaTek] Continuing with normal operations...", Color.Gray);
                }
            }

            return true;
        }

        #endregion

        #region Flash Operations

        /// <summary>
        /// Read partition table (Supports XML and XFlash protocols)
        /// </summary>
        public async Task<List<MtkPartitionInfo>> ReadPartitionTableAsync(CancellationToken ct = default)
        {
            // Prioritize XFlash binary protocol
            if (_useXFlash && _xflashClient != null)
            {
                Log("[MediaTek] Reading partition table using XFlash protocol...", Color.Gray);
                var xflashPartitions = await _xflashClient.ReadPartitionTableAsync(ct);
                if (xflashPartitions != null)
                {
                    Log($"[MediaTek] ✓ Read {xflashPartitions.Count} partitions (XFlash)", Color.Cyan);
                    return xflashPartitions;
                }
            }

            // Fallback to XML protocol
            if (_xmlClient == null || !_xmlClient.IsConnected)
            {
                Log("[MediaTek] DA not loaded", Color.Red);
                return null;
            }

            Log("[MediaTek] Reading partition table using XML protocol...", Color.Gray);
            var partitions = await _xmlClient.ReadPartitionTableAsync(ct);
            if (partitions != null)
            {
                Log($"[MediaTek] ✓ Read {partitions.Length} partitions (XML)", Color.Cyan);
                return new List<MtkPartitionInfo>(partitions);
            }

            return null;
        }

        /// <summary>
        /// Read partition (Supports XML and XFlash protocols)
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, ulong size, CancellationToken ct = default)
        {
            Log($"[MediaTek] Reading partition: {partitionName} ({FormatSize(size)})", Color.Cyan);

            byte[] data = null;

            // Prioritize XFlash binary protocol
            if (_useXFlash && _xflashClient != null)
            {
                Log("[MediaTek] Using XFlash protocol...", Color.Gray);
                data = await _xflashClient.ReadPartitionAsync(partitionName, 0, size, EmmcPartitionType.User, ct);
            }
            else if (_xmlClient != null && _xmlClient.IsConnected)
            {
                Log("[MediaTek] Using XML protocol...", Color.Gray);
                data = await _xmlClient.ReadPartitionAsync(partitionName, size, ct);
            }
            else
            {
                Log("[MediaTek] DA not loaded", Color.Red);
                return false;
            }

            if (data != null && data.Length > 0)
            {
                File.WriteAllBytes(outputPath, data);
                Log($"[MediaTek] ✓ Partition {partitionName} saved ({FormatSize((ulong)data.Length)})", Color.Green);
                return true;
            }

            Log($"[MediaTek] Failed to read partition {partitionName}", Color.Red);
            return false;
        }

        /// <summary>
        /// Write partition (Supports XML and XFlash protocols)
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, string filePath, CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
            {
                Log($"[MediaTek] File not found: {filePath}", Color.Red);
                return false;
            }

            byte[] data = File.ReadAllBytes(filePath);
            Log($"[MediaTek] Writing partition: {partitionName} ({FormatSize((ulong)data.Length)})", Color.Cyan);

            bool success = false;

            // Prioritize XFlash binary protocol
            if (_useXFlash && _xflashClient != null)
            {
                Log("[MediaTek] Using XFlash protocol...", Color.Gray);
                success = await _xflashClient.WritePartitionAsync(partitionName, 0, data, EmmcPartitionType.User, ct);
            }
            else if (_xmlClient != null && _xmlClient.IsConnected)
            {
                Log("[MediaTek] Using XML protocol...", Color.Gray);
                success = await _xmlClient.WritePartitionAsync(partitionName, data, ct);
            }
            else
            {
                Log("[MediaTek] DA not loaded", Color.Red);
                return false;
            }

            if (success)
            {
                Log($"[MediaTek] ✓ Partition {partitionName} written successfully", Color.Green);
            }
            else
            {
                Log($"[MediaTek] Failed to write partition {partitionName}", Color.Red);
            }

            return success;
        }

        /// <summary>
        /// Format size display
        /// </summary>
        private string FormatSize(ulong size)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            double sizeD = size;

            while (sizeD >= 1024 && unitIndex < units.Length - 1)
            {
                sizeD /= 1024;
                unitIndex++;
            }

            return $"{sizeD:F2} {units[unitIndex]}";
        }

        /// <summary>
        /// Erase partition (Supports XML and XFlash protocols)
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            Log($"[MediaTek] Erasing partition: {partitionName}", Color.Yellow);

            bool success = false;

            // Favor XFlash binary protocol
            if (_useXFlash && _xflashClient != null)
            {
                success = await _xflashClient.FormatPartitionAsync(partitionName, ct);
            }
            else if (_xmlClient != null && _xmlClient.IsConnected)
            {
                success = await _xmlClient.ErasePartitionAsync(partitionName, ct);
            }
            else
            {
                Log("[MediaTek] DA not loaded", Color.Red);
                return false;
            }

            if (success)
            {
                Log($"[MediaTek] ✓ Partition {partitionName} erased", Color.Green);
            }
            else
            {
                Log($"[MediaTek] Failed to erase partition {partitionName}", Color.Red);
            }

            return success;
        }

        /// <summary>
        /// Batch flashing
        /// </summary>
        public async Task<bool> FlashMultipleAsync(Dictionary<string, string> partitionFiles, CancellationToken ct = default)
        {
            if (_xmlClient == null || !_xmlClient.IsConnected)
            {
                Log("[MediaTek] DA not loaded", Color.Red);
                return false;
            }

            Log($"[MediaTek] Starting flashing of {partitionFiles.Count} partitions...", Color.Cyan);

            int success = 0;
            int total = partitionFiles.Count;

            foreach (var kvp in partitionFiles)
            {
                if (ct.IsCancellationRequested)
                {
                    Log("[MediaTek] Flashing cancelled", Color.Orange);
                    break;
                }

                if (await WritePartitionAsync(kvp.Key, kvp.Value, ct))
                {
                    success++;
                }

                OnProgress?.Invoke(success, total);
            }

            Log($"[MediaTek] Flashing completed: {success}/{total}", success == total ? Color.Green : Color.Orange);
            return success == total;
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reboot device
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (_xmlClient != null && _xmlClient.IsConnected)
            {
                Log("[MediaTek] Rebooting device...", Color.Cyan);
                return await _xmlClient.RebootAsync(ct);
            }
            return false;
        }

        /// <summary>
        /// Shutdown device
        /// </summary>
        public async Task<bool> ShutdownAsync(CancellationToken ct = default)
        {
            if (_xmlClient != null && _xmlClient.IsConnected)
            {
                Log("[MediaTek] Shutting down device...", Color.Cyan);
                return await _xmlClient.ShutdownAsync(ct);
            }
            return false;
        }

        /// <summary>
        /// Get Flash Info
        /// </summary>
        public async Task<MtkFlashInfo> GetFlashInfoAsync(CancellationToken ct = default)
        {
            if (_xmlClient != null && _xmlClient.IsConnected)
            {
                return await _xmlClient.GetFlashInfoAsync(ct);
            }
            return null;
        }

        #endregion

        #region Security Features

        /// <summary>
        /// Detect vulnerability
        /// </summary>
        public bool CheckVulnerability()
        {
            if (_bromClient == null || _bromClient.HwCode == 0)
                return false;

            var exploit = new CarbonaraExploit();
            return exploit.IsVulnerable(_bromClient.HwCode);
        }

        /// <summary>
        /// Get security info
        /// </summary>
        public MtkSecurityInfo GetSecurityInfo()
        {
            if (_bromClient == null)
                return null;

            return new MtkSecurityInfo
            {
                SecureBootEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.SbcEnabled),
                SlaEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.SlaEnabled),
                DaaEnabled = _bromClient.TargetConfig.HasFlag(TargetConfigFlags.DaaEnabled),
                MeId = _bromClient.MeId != null ? BitConverter.ToString(_bromClient.MeId).Replace("-", "") : "",
                SocId = _bromClient.SocId != null ? BitConverter.ToString(_bromClient.SocId).Replace("-", "") : ""
            };
        }

        /// <summary>
        /// Execute MT6989 ALLINONE-SIGNATURE exploit
        /// Suitable for disabling security checks after DA2 is loaded
        /// </summary>
        /// <param name="shellcodePath">Shellcode file path (optional)</param>
        /// <param name="pointerTablePath">Pointer table file path (optional)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success or not</returns>
        public async Task<bool> RunAllinoneSignatureExploitAsync(
            string shellcodePath = null,
            string pointerTablePath = null,
            CancellationToken ct = default)
        {
            if (_xmlClient == null || !_xmlClient.IsConnected)
            {
                Log("[MediaTek] DA2 not loaded, cannot execute ALLINONE-SIGNATURE exploit", Color.Red);
                return false;
            }

            Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);
            Log("[MediaTek] Executing ALLINONE-SIGNATURE exploit...", Color.Yellow);
            Log("[MediaTek] ═══════════════════════════════════════", Color.Yellow);

            try
            {
                // Create exploit instance
                var exploit = new AllinoneSignatureExploit(
                    _bromClient.GetPort(),
                    msg => Log(msg, Color.Yellow),
                    msg => Log(msg, Color.Gray),
                    progress => OnProgress?.Invoke((int)progress, 100),
                    _xmlClient.GetPortLock()
                );

                // Execute exploit (New version auto-loads pointer_table and source_file_trigger)
                bool success = await exploit.ExecuteExploitAsync(ct);

                if (success)
                {
                    Log("[MediaTek] ✓ ALLINONE-SIGNATURE exploit successful", Color.Green);
                    Log("[MediaTek] Device security checks disabled", Color.Green);
                }
                else
                {
                    Log("[MediaTek] ✗ ALLINONE-SIGNATURE exploit failed", Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log($"[MediaTek] ALLINONE-SIGNATURE exploit exception: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Check if ALLINONE-SIGNATURE exploit is supported
        /// Uses chip database for determination, no longer hardcoded chip list
        /// </summary>
        public bool IsAllinoneSignatureVulnerable()
        {
            if (_bromClient == null || _bromClient.HwCode == 0)
                return false;

            // Use database directly
            return MtkChipDatabase.IsAllinoneSignatureSupported(_bromClient.HwCode);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Process DA data
        /// Note: According to ChimeraTool packet analysis, although declared size in SEND_DA might be small,
        /// the actual sent data is the full DA file (including tail metadata)
        /// Checksum 0xDE21 = XOR16 of entire file 864752 bytes
        /// </summary>
        private byte[] ProcessDaData(byte[] daData)
        {
            // Do not truncate, return full data
            // ChimeraTool although declares 863728 bytes, but actually sent 864752 bytes
            Log($"[MediaTek] DA data size: {daData.Length} bytes (sent in full)", Color.Gray);
            return daData;
        }

        /// <summary>
        /// Detect DA file signature length
        /// </summary>
        /// <remarks>
        /// Official signed DA usually has the following signature formats:
        /// - 0x1000 (4096) bytes: Full certificate chain signature (SP Flash Tool DA)
        /// - 0x100 (256) bytes: RSA-2048 signature (Legacy DA)
        /// - 0x30 (48) bytes: V6 short signature
        /// </remarks>
        private int DetectDaSignatureLength(byte[] daData)
        {
            if (daData == null || daData.Length < 0x200)
                return 0;

            // Method 1: Check if file size matches known DA size + signature
            // DA extracted from SP Flash Tool is usually: DA code + 0x1000 signature
            // Our extracted DA1 is 216440 bytes, where signature is 4096 bytes

            // Method 2: Check characteristics of the last 0x1000 bytes
            if (daData.Length >= 0x1000)
            {
                int sigStart = daData.Length - 0x1000;

                // Stat signature area characteristics
                int zeroCount = 0;
                int ffCount = 0;
                var seen = new System.Collections.Generic.HashSet<byte>();

                // Check multiple sampling points
                int sampleSize = Math.Min(512, 0x1000);
                for (int i = 0; i < sampleSize; i++)
                {
                    byte b = daData[sigStart + i];
                    if (b == 0x00) zeroCount++;
                    if (b == 0xFF) ffCount++;
                    seen.Add(b);
                }

                int uniqueBytes = seen.Count;

                // Signature data characteristics:
                // 1. Enough diversity (uniqueBytes > 30)
                // 2. Not all padding values (0x00 or 0xFF not exceeding 80%)
                bool looksLikeSignature = uniqueBytes > 30 &&
                                          zeroCount < sampleSize * 0.8 &&
                                          ffCount < sampleSize * 0.8;

                if (looksLikeSignature)
                {
                    // Extra check: before signature should be code end or padding
                    // But don't be too strict, as compiler outputs vary
                    return 0x1000;
                }
            }

            // Method 3: Check 0x100 signature (Legacy)
            if (daData.Length >= 0x100)
            {
                int sigStart = daData.Length - 0x100;
                var seen = new System.Collections.Generic.HashSet<byte>();
                int zeroCount = 0;
                int ffCount = 0;

                for (int i = sigStart; i < daData.Length; i++)
                {
                    byte b = daData[i];
                    if (b == 0x00) zeroCount++;
                    if (b == 0xFF) ffCount++;
                    seen.Add(b);
                }

                int uniqueBytes = seen.Count;

                if (uniqueBytes > 20 && zeroCount < 200 && ffCount < 200)
                {
                    return 0x100;
                }
            }

            return 0;  // Detection failed, let caller decide
        }

        /// <summary>
        /// Wait for new MTK port to appear (after USB re-enumeration)
        /// </summary>
        private async Task<string> WaitForNewMtkPortAsync(CancellationToken ct, int timeoutMs = 15000)
        {
            var startTime = DateTime.Now;
            string oldPort = _bromClient.PortName;

            // Wait for old port to disappear first
            await Task.Delay(500, ct);

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (ct.IsCancellationRequested)
                    return null;

                // Get all current COM ports
                string[] ports = System.IO.Ports.SerialPort.GetPortNames();

                foreach (string port in ports)
                {
                    // Skip old port
                    if (port == oldPort)
                        continue;

                    try
                    {
                        // Try to open port and detect if it is MTK device
                        using (var testPort = new System.IO.Ports.SerialPort(port, 115200))
                        {
                            testPort.ReadTimeout = 500;
                            testPort.WriteTimeout = 500;
                            testPort.Open();

                            // Wait a short time for device stabilization
                            await Task.Delay(100, ct);

                            // Try to send simple probe command or directly return port
                            // DA will respond on specific port after running
                            // Simplified here, assuming any new port is DA port
                            testPort.Close();
                            return port;
                        }
                    }
                    catch
                    {
                        // Port unavailable, continue to next one
                    }
                }

                await Task.Delay(500, ct);
            }

            return null;
        }

        private void Log(string message, Color color)
        {
            OnLog?.Invoke(message, color);
        }

        private void ResetCancellationToken()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); }
                catch (ObjectDisposedException) { /* Disposed, ignore */ }
                catch (Exception ex) { Log($"[MediaTek] Cancel token exception: {ex.Message}", Color.Gray); }
                try { _cts.Dispose(); }
                catch (Exception ex) { Log($"[MediaTek] Dispose token exception: {ex.Message}", Color.Gray); }
            }
            _cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            Disconnect();
            _bromClient?.Dispose();
        }

        #endregion
    }
}
