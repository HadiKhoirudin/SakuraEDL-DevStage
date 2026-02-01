// ============================================================================
// LoveAlways - Sahara Protocol Complete Implementation
// Sahara Protocol - Qualcomm EDL Mode First Stage Boot Protocol
// ============================================================================
// Module: Qualcomm.Protocol
// Features: Handle Sahara handshake, chip info reading, Programmer upload
// Support: V1/V2/V3 protocol versions
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Common;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;

namespace LoveAlways.Qualcomm.Protocol
{
    #region Protocol Enum Definitions

    /// <summary>
    /// Sahara Command ID
    /// </summary>
    public enum SaharaCommand : uint
    {
        Hello = 0x01,
        HelloResponse = 0x02,
        ReadData = 0x03,            // 32-bit read (Old devices)
        EndImageTransfer = 0x04,
        Done = 0x05,
        DoneResponse = 0x06,
        Reset = 0x07,               // Hard reset (Reboot device)
        ResetResponse = 0x08,
        MemoryDebug = 0x09,
        MemoryRead = 0x0A,
        CommandReady = 0x0B,        // Command mode ready
        SwitchMode = 0x0C,          // Switch mode
        Execute = 0x0D,             // Execute command
        ExecuteData = 0x0E,         // Command data response
        ExecuteResponse = 0x0F,     // Command response acknowledgment
        MemoryDebug64 = 0x10,
        MemoryRead64 = 0x11,
        ReadData64 = 0x12,          // 64-bit read (New devices)
        ResetStateMachine = 0x13    // State machine reset (Soft reset)
    }

    /// <summary>
    /// Sahara Mode
    /// </summary>
    public enum SaharaMode : uint
    {
        ImageTransferPending = 0x0,
        ImageTransferComplete = 0x1,
        MemoryDebug = 0x2,
        Command = 0x3               // Command mode (Read info)
    }

    /// <summary>
    /// Sahara Exec Command ID
    /// </summary>
    public enum SaharaExecCommand : uint
    {
        SerialNumRead = 0x01,       // Serial number
        MsmHwIdRead = 0x02,         // HWID (V1/V2 only)
        OemPkHashRead = 0x03,       // PK Hash
        SblInfoRead = 0x06,         // SBL info (V3)
        SblSwVersion = 0x07,        // SBL version (V1/V2)
        PblSwVersion = 0x08,        // PBL version
        ChipIdV3Read = 0x0A,        // V3 chip info (includes HWID)
        SerialNumRead64 = 0x14      // 64-bit serial number
    }

    /// <summary>
    /// Sahara Status Code
    /// </summary>
    public enum SaharaStatus : uint
    {
        Success = 0x00,
        InvalidCommand = 0x01,
        ProtocolMismatch = 0x02,
        InvalidTargetProtocol = 0x03,
        InvalidHostProtocol = 0x04,
        InvalidPacketSize = 0x05,
        UnexpectedImageId = 0x06,
        InvalidHeaderSize = 0x07,
        InvalidDataSize = 0x08,
        InvalidImageType = 0x09,
        InvalidTransmitLength = 0x0A,
        InvalidReceiveLength = 0x0B,
        GeneralTransmitReceiveError = 0x0C,
        ReadDataError = 0x0D,
        UnsupportedNumProgramHeaders = 0x0E,
        InvalidProgramHeaderSize = 0x0F,
        MultipleSharedSegments = 0x10,
        UninitializedProgramHeaderLocation = 0x11,
        InvalidDestAddress = 0x12,
        InvalidImageHeaderDataSize = 0x13,
        InvalidElfHeader = 0x14,
        UnknownHostError = 0x15,
        ReceiveTimeout = 0x16,
        TransmitTimeout = 0x17,
        InvalidHostMode = 0x18,
        InvalidMemoryRead = 0x19,
        InvalidDataSizeRequest = 0x1A,
        MemoryDebugNotSupported = 0x1B,
        InvalidModeSwitch = 0x1C,
        CommandExecuteFailure = 0x1D,
        ExecuteCommandInvalidParam = 0x1E,
        AccessDenied = 0x1F,
        InvalidClientCommand = 0x20,
        HashTableAuthFailure = 0x21,    // Loader signature mismatch
        HashVerificationFailure = 0x22, // Image tampered
        HashTableNotFound = 0x23,       // Image unsigned
        MaxErrors = 0x29
    }

    #endregion

    #region Protocol Structures

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaHelloResponse
    {
        public uint Command;
        public uint Length;
        public uint Version;
        public uint VersionSupported;
        public uint Status;
        public uint Mode;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaDonePacket
    {
        public uint Command;
        public uint Length;
    }

    #endregion

    /// <summary>
    /// Sahara Status Helper Class
    /// </summary>
    public static class SaharaStatusHelper
    {
        public static string GetErrorMessage(SaharaStatus status)
        {
            switch (status)
            {
                case SaharaStatus.Success: return "Success";
                case SaharaStatus.InvalidCommand: return "Invalid command";
                case SaharaStatus.ProtocolMismatch: return "Protocol mismatch";
                case SaharaStatus.UnexpectedImageId: return "Image ID mismatch";
                case SaharaStatus.ReceiveTimeout: return "Receive timeout";
                case SaharaStatus.TransmitTimeout: return "Transmit timeout";
                case SaharaStatus.HashTableAuthFailure: return "Signature verification failed: Loader and device mismatch";
                case SaharaStatus.HashVerificationFailure: return "Integrity check failed: Image may be tampered";
                case SaharaStatus.HashTableNotFound: return "Signature data not found: Image unsigned";
                case SaharaStatus.CommandExecuteFailure: return "Command execution failed";
                case SaharaStatus.AccessDenied: return "Command not supported";
                default: return string.Format("Unknown error (0x{0:X2})", (uint)status);
            }
        }

        public static bool IsFatalError(SaharaStatus status)
        {
            switch (status)
            {
                case SaharaStatus.HashTableAuthFailure:
                case SaharaStatus.HashVerificationFailure:
                case SaharaStatus.HashTableNotFound:
                case SaharaStatus.InvalidElfHeader:
                case SaharaStatus.ProtocolMismatch:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Sahara Protocol Client - Full Version (Support V1/V2/V3)
    /// </summary>
    public class SaharaClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private bool _disposed;

        // Configuration
        private const int MAX_BUFFER_SIZE = 4096;
        private const int READ_TIMEOUT_MS = 30000;
        private const int HELLO_TIMEOUT_MS = 30000;
        
        // Watchdog configuration
        private const int WATCHDOG_TIMEOUT_SECONDS = 45;  // Watchdog timeout
        private const int WATCHDOG_STALL_THRESHOLD = 3;   // Consecutive no-response count threshold
        private Watchdog _watchdog;
        private volatile int _watchdogStallCount = 0;       // Use volatile for thread visibility
        private volatile bool _watchdogTriggeredReset = false; // Use volatile for thread visibility

        // Protocol state
        public uint ProtocolVersion { get; private set; }
        public uint ProtocolVersionSupported { get; private set; }
        public SaharaMode CurrentMode { get; private set; }
        public bool IsConnected { get; private set; }

        // Chip info
        public string ChipSerial { get; private set; }
        public string ChipHwId { get; private set; }
        public string ChipPkHash { get; private set; }
        public QualcommChipInfo ChipInfo { get; private set; }

        private bool _chipInfoRead = false;
        private bool _doneSent = false;
        private bool _skipCommandMode = false;

        // Pre-read Hello data
        private byte[] _pendingHelloData = null;
        
        /// <summary>
        /// Watchdog timeout event (External can subscribe for notifications)
        /// </summary>
        public event EventHandler<WatchdogTimeoutEventArgs> OnWatchdogTimeout;
        
        /// <summary>
        /// Whether to skip command mode (Some devices don't support command mode, force skip to avoid InvalidCommand error)
        /// </summary>
        public bool SkipCommandMode 
        { 
            get { return _skipCommandMode; } 
            set { _skipCommandMode = value; } 
        }

        // Transfer progress
        private long _totalSent = 0;
        private Action<double> _progressCallback;

        public SaharaClient(SerialPortManager port, Action<string> log = null, Action<string> logDetail = null, Action<double> progressCallback = null)
        {
            _port = port;
            _log = log ?? delegate { };
            _logDetail = logDetail ?? _log;  // If not specified, default to _log
            _progressCallback = progressCallback;
            ProtocolVersion = 2;
            ProtocolVersionSupported = 1;
            CurrentMode = SaharaMode.ImageTransferPending;
            ChipSerial = "";
            ChipHwId = "";
            ChipPkHash = "";
            ChipInfo = new QualcommChipInfo();
            
            // Initialize watchdog
            InitializeWatchdog();
        }
        
        /// <summary>
        /// Initialize watchdog
        /// </summary>
        private void InitializeWatchdog()
        {
            _watchdog = new Watchdog("Sahara", TimeSpan.FromSeconds(WATCHDOG_TIMEOUT_SECONDS), _logDetail);
            _watchdog.OnTimeout += HandleWatchdogTimeout;
        }
        
        /// <summary>
        /// Watchdog timeout handler
        /// </summary>
        private void HandleWatchdogTimeout(object sender, WatchdogTimeoutEventArgs e)
        {
            _watchdogStallCount++;
            _log($"[Sahara] ⚠ Watchdog detected freeze (#{_watchdogStallCount}, waited {e.ElapsedTime.TotalSeconds:F0}s)");
            
            // Notify external subscribers
            OnWatchdogTimeout?.Invoke(this, e);
            
            if (_watchdogStallCount >= WATCHDOG_STALL_THRESHOLD)
            {
                _log("[Sahara] Watchdog triggering auto reset...");
                _watchdogTriggeredReset = true;
                e.ShouldReset = false; // Stop watchdog, let reset logic take over
            }
            else
            {
                // Try sending reset command but continue monitoring
                _logDetail("[Sahara] Watchdog attempting soft reset...");
                try
                {
                    SendReset(); // Send ResetStateMachine
                }
                catch (Exception ex)
                {
                    _logDetail($"[Sahara] Soft reset failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Feed watchdog - Call when valid data received
        /// </summary>
        private void FeedWatchdog()
        {
            _watchdog?.Feed();
            _watchdogStallCount = 0; // Valid data received, reset freeze count
        }

        /// <summary>
        /// Set pre-read Hello data
        /// </summary>
        public void SetPendingHelloData(byte[] data)
        {
            _pendingHelloData = data;
        }

        /// <summary>
        /// Get device info only (Don't upload Loader)
        /// Used for cloud auto-matching
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Whether successfully obtained device info</returns>
        public async Task<bool> GetDeviceInfoOnlyAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                _logDetail("[Sahara] Getting device info (without uploading Loader)...");
                
                // Read Hello packet
                byte[] header = null;
                
                // Check if there's pre-read Hello data
                if (_pendingHelloData != null && _pendingHelloData.Length >= 8)
                {
                    header = new byte[8];
                    Array.Copy(_pendingHelloData, 0, header, 0, 8);
                }
                else
                {
                    header = await ReadBytesAsync(8, READ_TIMEOUT_MS * 3, ct);
                }

                if (header == null)
                {
                    _logDetail("[Sahara] Cannot receive Hello packet");
                    return false;
                }

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)cmdId !=SaharaCommand.Hello)
                {
                    _logDetail($"[Sahara] Received non-Hello packet: 0x{cmdId:X}");
                    return false;
                }

                // Process Hello packet to get device info
                await HandleHelloAsync(pktLen, ct);
                
                // Verify chip info obtained
                if (ChipInfo == null || string.IsNullOrEmpty(ChipInfo.PkHash))
                {
                    _logDetail("[Sahara] Chip info incomplete");
                    return false;
                }
                
                _logDetail("[Sahara] ✓ Device info obtained successfully");
                _deviceInfoObtained = true;
                return true;
            }
            catch (Exception ex)
            {
                _logDetail($"[Sahara] Device info retrieval exception: {ex.Message}");
                return false;
            }
        }
        
        // Flag whether device info obtained
        private bool _deviceInfoObtained = false;
        
        /// <summary>
        /// Continue uploading Loader (Call after GetDeviceInfoOnlyAsync)
        /// </summary>
        /// <param name="loaderData">Loader data</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Whether successful</returns>
        public async Task<bool> UploadLoaderAsync(byte[] loaderData, CancellationToken ct = default(CancellationToken))
        {
            if (!_deviceInfoObtained)
            {
                _log("[Sahara] Please call GetDeviceInfoOnlyAsync first");
                return false;
            }
            
            if (loaderData == null || loaderData.Length == 0)
            {
                _log("[Sahara] Loader data is empty");
                return false;
            }
            
            _log($"[Sahara] Uploading Loader ({loaderData.Length / 1024} KB)...");
            
            try
            {
                // Send Hello Response to start transfer
                await SendHelloResponseAsync(2, 1, SaharaMode.ImageTransferPending, ct);
                
                // Continue Sahara data transfer loop
                bool done = false;
                int loopGuard = 0;
                int endImageTxCount = 0;
                int timeoutCount = 0;
                _doneSent = false;
                _totalSent = 0;

                while (!done && loopGuard++ < 1000)
                {
                    if (ct.IsCancellationRequested)
                        return false;

                    byte[] header = await ReadBytesAsync(8, READ_TIMEOUT_MS, ct);

                    if (header == null)
                    {
                        timeoutCount++;
                        if (timeoutCount >= 5)
                        {
                            _log("[Sahara] Device not responding");
                            return false;
                        }
                        await Task.Delay(500, ct);
                        continue;
                    }

                    timeoutCount = 0;
                    uint cmdId = BitConverter.ToUInt32(header, 0);
                    uint pktLen = BitConverter.ToUInt32(header, 4);

                    switch ((SaharaCommand)cmdId)
                    {
                    case SaharaCommand.ReadData:
                        await HandleReadData32Async(pktLen, loaderData, ct);
                        break;

                    case SaharaCommand.ReadData64:
                        await HandleReadData64Async(pktLen, loaderData, ct);
                        break;

                    case SaharaCommand.EndImageTransfer:
                        bool success;
                        bool isDone;
                        int newCount;
                        HandleEndImageTransferResult(await HandleEndImageTransferAsync(pktLen, endImageTxCount, ct), out success, out isDone, out newCount);
                        endImageTxCount = newCount;
                        if (!success) return false;
                        if (isDone) done = true;
                        break;

                    case SaharaCommand.DoneResponse:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log("[Sahara] ✅ Loader uploaded successfully");
                        done = true;
                        IsConnected = true;
                        break;

                    default:
                        if (pktLen > 8)
                            await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        break;
                    }
                }

                return done && IsConnected;
            }
            catch (Exception ex)
            {
                _log($"[Sahara] Loader upload exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handshake and upload Loader (Auto retry reset on failure) - From memory data
        /// </summary>
        /// <param name="loaderData">Boot file data (byte[])</param>
        /// <param name="loaderName">Boot name (for log display)</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="maxRetries">Max retry count</param>
        public async Task<bool> HandshakeAndUploadAsync(byte[] loaderData, string loaderName, CancellationToken ct = default(CancellationToken), int maxRetries = 2)
        {
            if (loaderData == null || loaderData.Length == 0)
                throw new ArgumentException("Boot data is empty");

            _log(string.Format("[Sahara] Loading embedded boot: {0} ({1} KB)", loaderName, loaderData.Length / 1024));
            return await HandshakeAndUploadCoreAsync(loaderData, ct, maxRetries);
        }

        /// <summary>
        /// Handshake and upload Loader (Auto retry reset on failure) - From file path
        /// </summary>
        /// <param name="loaderPath">Boot file path</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="maxRetries">Max retry count (default 2, max 3 attempts)</param>
        public async Task<bool> HandshakeAndUploadAsync(string loaderPath, CancellationToken ct = default(CancellationToken), int maxRetries = 2)
        {
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("Boot file does not exist", loaderPath);

            byte[] fileBytes = File.ReadAllBytes(loaderPath);
            _log(string.Format("[Sahara] Loading boot: {0} ({1} KB)", Path.GetFileName(loaderPath), fileBytes.Length / 1024));
            return await HandshakeAndUploadCoreAsync(fileBytes, ct, maxRetries);
        }

        /// <summary>
        /// Handshake upload core logic
        /// </summary>
        private async Task<bool> HandshakeAndUploadCoreAsync(byte[] fileBytes, CancellationToken ct, int maxRetries)
        {
            // If watchdog triggered reset, increase extra retry count
            int effectiveMaxRetries = maxRetries;
            
            // Attempt handshake, auto reset and retry on failure
            for (int attempt = 0; attempt <= effectiveMaxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) return false;
                
                if (attempt > 0)
                {
                    // Check if reset triggered by watchdog
                    bool wasWatchdogReset = _watchdogTriggeredReset;
                    
                    if (wasWatchdogReset)
                    {
                        _log($"[Sahara] Watchdog triggered reset, executing hard reset (retry #{attempt})...");
                        
                        // Use more aggressive reset when watchdog triggered
                        PurgeBuffer();
                        SendHardReset(); // Send hard reset command
                        await Task.Delay(1000, ct); // Wait for device reboot
                        
                        // Add extra retry opportunity
                        if (attempt == effectiveMaxRetries && effectiveMaxRetries < maxRetries + 2)
                        {
                            effectiveMaxRetries++;
                            _log("[Sahara] Watchdog triggered, adding extra retry opportunity");
                        }
                    }
                    else
                    {
                        _log(string.Format("[Sahara] Handshake failed, attempting Sahara state reset (retry #{0})...", attempt));
                    }
                    
                    // Attempt Sahara state machine reset
                    bool resetOk = await TryResetSaharaAsync(ct);
                    if (resetOk)
                    {
                        _log("[Sahara] ✓ State machine reset successful, restarting handshake...");
                    }
                    else
                    {
                        _log("[Sahara] State machine reset not confirmed, continuing handshake attempt...");
                    }
                    
                    // Reset internal state
                    _chipInfoRead = false;
                    _pendingHelloData = null;
                    _doneSent = false;
                    _totalSent = 0;
                    IsConnected = false;
                    _watchdogTriggeredReset = false;
                    _watchdogStallCount = 0;
                    
                    await Task.Delay(300, ct);
                }
                
                bool success = await HandshakeAndLoadInternalAsync(fileBytes, ct);
                if (success)
                {
                    if (attempt > 0)
                        _log(string.Format("[Sahara] ✓ Retry successful (attempt #{0})", attempt + 1));
                    return true;
                }
            }
            
            _log("[Sahara] ❌ Multiple handshake attempts failed, may need to power cycle device");
            return false;
        }

        /// <summary>
        /// Internal handshake and load
        /// </summary>
        private async Task<bool> HandshakeAndLoadInternalAsync(byte[] fileBytes, CancellationToken ct)
        {
            bool done = false;
            int loopGuard = 0;
            int endImageTxCount = 0;
            int timeoutCount = 0;
            _doneSent = false;
            _totalSent = 0;
            _watchdogTriggeredReset = false;
            _watchdogStallCount = 0;
            var sw = Stopwatch.StartNew();
            
            // Start watchdog
            _watchdog?.Start("Sahara handshake");

            try
            {
                while (!done && loopGuard++ < 1000)
                {
                    if (ct.IsCancellationRequested)
                        return false;
                    
                    // Check if watchdog triggered reset
                    if (_watchdogTriggeredReset)
                    {
                        _log("[Sahara] Watchdog triggered reset, exiting handshake loop");
                        return false; // Return false to let outer retry logic take over
                    }

                    byte[] header = null;

                    // Check if there's pre-read Hello data
                    if (loopGuard == 1 && _pendingHelloData != null && _pendingHelloData.Length >= 8)
                    {
                        header = new byte[8];
                        Array.Copy(_pendingHelloData, 0, header, 0, 8);
                        FeedWatchdog(); // Has pre-read data, feed watchdog
                    }
                    else
                    {
                        int currentTimeout = (loopGuard == 1) ? READ_TIMEOUT_MS * 2 : READ_TIMEOUT_MS;
                        header = await ReadBytesAsync(8, currentTimeout, ct);
                    }

                    if (header == null)
                    {
                        timeoutCount++;
                        if (timeoutCount >= 5)
                        {
                            _log("[Sahara] Device not responding");
                            return false;
                        }

                        int available = _port.BytesToRead;
                        if (available > 0)
                            await ReadBytesAsync(available, 1000, ct);

                        await Task.Delay(500, ct);
                        continue;
                    }

                    // Valid data received, feed watchdog
                    FeedWatchdog();
                    timeoutCount = 0;
                    uint cmdId = BitConverter.ToUInt32(header, 0);
                    uint pktLen = BitConverter.ToUInt32(header, 4);

                    if (pktLen < 8 || pktLen > MAX_BUFFER_SIZE * 4)
                    {
                        PurgeBuffer();
                        await Task.Delay(50, ct);
                        continue;
                    }

                    // Debug log: Display received command (Except ReadData, too frequent)
                    if ((SaharaCommand)cmdId != SaharaCommand.ReadData && 
                        (SaharaCommand)cmdId != SaharaCommand.ReadData64)
                    {
                        _logDetail(string.Format("[Sahara] Received: Cmd=0x{0:X2} ({1}), Len={2}", 
                            cmdId, (SaharaCommand)cmdId, pktLen));
                    }

                    switch ((SaharaCommand)cmdId)
                    {
                    case SaharaCommand.Hello:
                        await HandleHelloAsync(pktLen, ct);
                        break;

                    case SaharaCommand.ReadData:
                        await HandleReadData32Async(pktLen, fileBytes, ct);
                        break;

                    case SaharaCommand.ReadData64:
                        await HandleReadData64Async(pktLen, fileBytes, ct);
                        break;

                    case SaharaCommand.EndImageTransfer:
                        bool success;
                        bool isDone;
                        int newCount;
                        HandleEndImageTransferResult(await HandleEndImageTransferAsync(pktLen, endImageTxCount, ct), out success, out isDone, out newCount);
                        endImageTxCount = newCount;
                        if (!success) return false;
                        if (isDone) done = true;
                        break;

                    case SaharaCommand.DoneResponse:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log("[Sahara] ✅ Boot loaded successfully");
                        done = true;
                        IsConnected = true;
                        FeedWatchdog(); // Successfully completed, feed watchdog
                        break;

                    case SaharaCommand.CommandReady:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log("[Sahara] Received CommandReady, switching to transfer mode");
                        SendSwitchMode(SaharaMode.ImageTransferPending);
                        FeedWatchdog();
                        break;

                    default:
                        _log(string.Format("[Sahara] Unknown command: 0x{0:X2}", cmdId));
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        break;
                    }
                }

                return done;
            }
            finally
            {
                // Stop watchdog
                _watchdog?.Stop();
            }
        }

        private void HandleEndImageTransferResult(Tuple<bool, bool, int> result, out bool success, out bool isDone, out int newCount)
        {
            success = result.Item1;
            isDone = result.Item2;
            newCount = result.Item3;
        }

        /// <summary>
        /// Handle Hello packet (Optimized per tools project)
        /// </summary>
        private async Task HandleHelloAsync(uint pktLen, CancellationToken ct)
        {
            byte[] body = null;

            if (_pendingHelloData != null && _pendingHelloData.Length >= pktLen)
            {
                body = new byte[pktLen - 8];
                Array.Copy(_pendingHelloData, 8, body, 0, (int)pktLen - 8);
                _pendingHelloData = null;
            }
            else
            {
                body = await ReadBytesAsync((int)pktLen - 8, 5000, ct);
                _pendingHelloData = null;
            }

            if (body == null) return;

            ProtocolVersion = BitConverter.ToUInt32(body, 0);
            uint deviceMode = body.Length >= 12 ? BitConverter.ToUInt32(body, 12) : 0;
            
            // Detailed log (Aligned with tools project)
            _logDetail(string.Format("[Sahara] Received HELLO (Version={0}, Mode={1})", ProtocolVersion, deviceMode));

            // Try reading chip info (First time only, and device in transfer mode)
            if (!_chipInfoRead && deviceMode == (uint)SaharaMode.ImageTransferPending)
            {
                _chipInfoRead = true;
                bool enteredCommandMode = await TryReadChipInfoSafeAsync(ct);
                
                if (enteredCommandMode)
                {
                    // Successfully entered command mode and read info, already sent SwitchMode
                    // Device will resend Hello, don't send HelloResponse here
                    _logDetail("[Sahara] Waiting for device to resend Hello...");
                    return;
                }
            }

            // Send HelloResponse to enter transfer mode
            _logDetail("[Sahara] Sending HelloResponse (Transfer mode)");
            SendHelloResponse(SaharaMode.ImageTransferPending);
        }

        /// <summary>
        /// Handle 32-bit read request
        /// </summary>
        private async Task HandleReadData32Async(uint pktLen, byte[] fileBytes, CancellationToken ct)
        {
            var body = await ReadBytesAsync(12, 5000, ct);
            if (body == null) return;

            uint imageId = BitConverter.ToUInt32(body, 0);
            uint offset = BitConverter.ToUInt32(body, 4);
            uint length = BitConverter.ToUInt32(body, 8);

            if (offset + length > fileBytes.Length) return;

            _port.Write(fileBytes, (int)offset, (int)length);

            _totalSent += length;
            double percent = (double)_totalSent * 100 / fileBytes.Length;
            
            // Call progress callback (Progress bar display, no log needed)
            if (_progressCallback != null)
                _progressCallback(percent);
        }

        /// <summary>
        /// Handle 64-bit read request
        /// </summary>
        private async Task HandleReadData64Async(uint pktLen, byte[] fileBytes, CancellationToken ct)
        {
            var body = await ReadBytesAsync(24, 5000, ct);
            if (body == null) return;

            ulong imageId = BitConverter.ToUInt64(body, 0);
            ulong offset = BitConverter.ToUInt64(body, 8);
            ulong length = BitConverter.ToUInt64(body, 16);

            if ((long)offset + (long)length > fileBytes.Length) return;

            _port.Write(fileBytes, (int)offset, (int)length);

            _totalSent += (long)length;
            double percent = (double)_totalSent * 100 / fileBytes.Length;
            
            // Call progress callback (Progress bar display, no log needed)
            if (_progressCallback != null)
                _progressCallback(percent);
        }

        /// <summary>
        /// Handle end of image transfer (Optimized per tools project)
        /// </summary>
        private async Task<Tuple<bool, bool, int>> HandleEndImageTransferAsync(uint pktLen, int endImageTxCount, CancellationToken ct)
        {
            endImageTxCount++;
            
            if (endImageTxCount > 10) 
            {
                _log("[Sahara] Received too many EndImageTransfer commands");
                return Tuple.Create(false, false, endImageTxCount);
            }

            uint endStatus = 0;
            uint imageId = 0;
            if (pktLen >= 16)
            {
                var body = await ReadBytesAsync(8, 5000, ct);
                if (body != null) 
                {
                    imageId = BitConverter.ToUInt32(body, 0);
                    endStatus = BitConverter.ToUInt32(body, 4);
                }
            }

            if (endStatus != 0)
            {
                var status = (SaharaStatus)endStatus;
                _log(string.Format("[Sahara] ❌ Transfer failed: {0}", SaharaStatusHelper.GetErrorMessage(status)));
                
                // [Critical] If InvalidCommand, may be state desync caused by command mode
                // Try skipping command mode on next connection
                if (status == SaharaStatus.InvalidCommand)
                {
                    _log("[Sahara] Hint: This error usually caused by device state residue, will auto-recover on retry");
                }
                
                return Tuple.Create(false, false, endImageTxCount);
            }

            if (!_doneSent)
            {
                _logDetail("[Sahara] Image transfer complete, sending Done");
                SendDone();
                _doneSent = true;
            }

            return Tuple.Create(true, false, endImageTxCount);
        }

        /// <summary>
        /// Safe chip info reading -  Support V1/V2/V3 (Optimized per tools project)
        /// </summary>
        private async Task<bool> TryReadChipInfoSafeAsync(CancellationToken ct)
        {
            if (_skipCommandMode) 
            {
                _logDetail("[Sahara] Skip ping command mode");
                return false;
            }

            try
            {
                // Send HelloResponse to request command mode entry
                _logDetail(string.Format("[Sahara] Attempting command mode entry (v{0})...", ProtocolVersion));
                SendHelloResponse(SaharaMode.Command);

                // Wait for response
                var header = await ReadBytesAsync(8, 2000, ct);
                if (header == null) 
                {
                    _logDetail("[Sahara] Command mode no response");
                    return false;
                }

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)cmdId == SaharaCommand.CommandReady)
                {
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _logDetail("[Sahara] Device accepted command mode");
                    
                    await ReadChipInfoCommandsAsync(ct);
                    
                    // Switch back to transfer mode
                    _logDetail("[Sahara] Switching back to transfer mode...");
                    SendSwitchMode(SaharaMode.ImageTransferPending);
                    await Task.Delay(50, ct);
                    return true;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.ReadData ||
                         (SaharaCommand)cmdId == SaharaCommand.ReadData64)
                {
                    // Device rejected command mode, directly start data transfer
                    _logDetail(string.Format("[Sahara] Device rejected command mode (v{0})", ProtocolVersion));
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.EndImageTransfer)
                {
                    // [Critical Fix] Device may be in residual state, directly sent EndImageTransfer
                    // In this case need to reset state machine
                    _logDetail("[Sahara] Device state abnormal (received EndImageTransfer), needs reset");
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else
                {
                    _logDetail(string.Format("[Sahara] Command mode unknown response: 0x{0:X2}", cmdId));
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Sahara] Chip info reading failed: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Read chip info - V1/V2/V3 version distinction
        /// [Critical] Silently skip when all commands fail, don't affect handshake
        /// </summary>
        private async Task ReadChipInfoCommandsAsync(CancellationToken ct)
        {
            // 1. First display protocol version
            _log(string.Format("- Sahara version  : {0}", ProtocolVersion));
            
            // 2. Read serial number (cmd=0x01)
            var serialData = await ExecuteCommandSafeAsync(SaharaExecCommand.SerialNumRead, ct);
            if (serialData != null && serialData.Length >= 4)
            {
                uint serial = BitConverter.ToUInt32(serialData, 0);
                ChipSerial = serial.ToString("x8");
                ChipInfo.SerialHex = "0x" + ChipSerial.ToUpperInvariant();
                ChipInfo.SerialDec = serial;
                _log(string.Format("- Chip Serial Number : {0}", ChipSerial));
            }

            // 3. Read PK Hash (cmd=0x03)
            var pkhash = await ExecuteCommandSafeAsync(SaharaExecCommand.OemPkHashRead, ct);
            if (pkhash != null && pkhash.Length > 0)
            {
                int hashLen = Math.Min(pkhash.Length, 48);
                ChipPkHash = BitConverter.ToString(pkhash, 0, hashLen).Replace("-", "").ToLower();
                ChipInfo.PkHash = ChipPkHash;
                ChipInfo.PkHashInfo = QualcommDatabase.GetPkHashInfo(ChipPkHash);
                _log(string.Format("- OEM PKHASH : {0}", ChipPkHash));
                
                if (!string.IsNullOrEmpty(ChipInfo.PkHashInfo) && ChipInfo.PkHashInfo != "Unknown" && ChipInfo.PkHashInfo != "Custom OEM")
                {
                    _log(string.Format("- SecBoot : {0}", ChipInfo.PkHashInfo));
                }
            }

            // 4. Read HWID - V1/V2 and V3 use different commands
            if (ProtocolVersion < 3)
            {
                // V1/V2: Use cmd=0x02 (MsmHwIdRead)
                var hwidData = await ExecuteCommandSafeAsync(SaharaExecCommand.MsmHwIdRead, ct);
                if (hwidData != null && hwidData.Length >= 8)
                    ProcessHwIdData(hwidData);
                    
                // V1/V2: Read SBL version (cmd=0x07), skip on failure
                var sblVer = await ExecuteCommandSafeAsync(SaharaExecCommand.SblSwVersion, ct);
                if (sblVer != null && sblVer.Length >= 4)
                {
                    uint version = BitConverter.ToUInt32(sblVer, 0);
                    _log(string.Format("- SBL SW Version : 0x{0:X8}", version));
                }
            }
            else
            {
                // V3: Try cmd=0x0A, skip on failure
                var extInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.ChipIdV3Read, ct);
                if (extInfo != null && extInfo.Length >= 44)
                {
                    ProcessV3ExtendedInfo(extInfo);
                }
                else
                {
                    // cmd=0x0A not supported, try inferring vendor from PK Hash
                    if (!string.IsNullOrEmpty(ChipPkHash))
                    {
                        ChipInfo.Vendor = QualcommDatabase.GetVendorByPkHash(ChipPkHash);
                        if (!string.IsNullOrEmpty(ChipInfo.Vendor) && ChipInfo.Vendor != "Unknown")
                        {
                            _log(string.Format("- Vendor (by PK Hash) : {0}", ChipInfo.Vendor));
                        }
                    }
                }
                
                // V3: Read SBL info (cmd=0x06), skip on failure
                var sblInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.SblInfoRead, ct);
                if (sblInfo != null && sblInfo.Length >= 4)
                {
                    ProcessSblInfo(sblInfo);
                }
            }
            // Note: PBL version read (cmd=0x08) removed, some devices don't support it and cause handshake failure
        }
        
        /// <summary>
        /// Process SBL info (V3 specific, cmd=0x06)
        /// </summary>
        private void ProcessSblInfo(byte[] sblInfo)
        {
            // SBL Info return format:
            // Offset 0: Serial Number (4 bytes)
            // Offset 4: MSM HW ID (8 bytes) - V3 may include
            // Offset 12+: Other extended info
            
            if (sblInfo.Length >= 4)
            {
                uint sblSerial = BitConverter.ToUInt32(sblInfo, 0);
                _log(string.Format("- SBL Serial : 0x{0:X8}", sblSerial));
            }
            
            if (sblInfo.Length >= 8)
            {
                uint sblVersion = BitConverter.ToUInt32(sblInfo, 4);
                if (sblVersion != 0 && sblVersion != 0xFFFFFFFF)
                {
                    _log(string.Format("- SBL Version : 0x{0:X8}", sblVersion));
                }
            }
            
            // If more data available, try parsing OEM info
            if (sblInfo.Length >= 16)
            {
                uint oemField1 = BitConverter.ToUInt32(sblInfo, 8);
                uint oemField2 = BitConverter.ToUInt32(sblInfo, 12);
                if (oemField1 != 0 || oemField2 != 0)
                {
                    _log(string.Format("- SBL OEM Data : 0x{0:X8} 0x{1:X8}", oemField1, oemField2));
                }
            }
        }
        
        /// <summary>
        /// Display chip info summary before uploading boot
        /// Note: Detailed info already output in ReadChipInfoCommandsAsync
        /// </summary>
        private void LogChipInfoBeforeUpload()
        {
            if (ChipInfo == null) return;
            
            // Only output summary, detailed info already output during reading
            _logDetail("[Sahara] Chip info reading complete");
        }

        /// <summary>
        /// Process V1/V2 HWID data (Reference tools project)
        /// </summary>
        private void ProcessHwIdData(byte[] hwidData)
        {
            ulong hwid = BitConverter.ToUInt64(hwidData, 0);
            ChipHwId = hwid.ToString("x16");
            ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

            // V1/V2 HWID format:
            // Bits 0-31:  MSM_ID (Chip ID, full 32 bits)
            // Bits 32-47: OEM_ID (Vendor ID)
            // Bits 48-63: MODEL_ID (Model ID)
            uint msmId = (uint)(hwid & 0xFFFFFFFF);  // Full 32 bits
            ushort oemId = (ushort)((hwid >> 32) & 0xFFFF);
            ushort modelId = (ushort)((hwid >> 48) & 0xFFFF);

            ChipInfo.MsmId = msmId;
            ChipInfo.OemId = oemId;
            ChipInfo.ModelId = modelId;
            ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
            ChipInfo.Vendor = QualcommDatabase.GetVendorName(oemId);

            // Log output (Format aligned with tools project)
            _log(string.Format("- MSM HWID : 0x{0:x} | model_id:0x{1:x4} | oem_id:{2:X4} {3}",
                msmId, modelId, oemId, ChipInfo.Vendor));

            if (ChipInfo.ChipName != "Unknown")
                _log(string.Format("- CHIP : {0}",ChipInfo.ChipName));

            _log(string.Format("- HW_ID : {0}", ChipHwId));
        }

        /// <summary>
        /// [Critical] Process V3 extended info (cmd=0x0A return)
        /// Reference: Standard implementation from tools project
        /// V3 returns 84 bytes data:
        /// - Offset 0: Chip Identifier V3 (4 bytes)
        /// - Offset 36: MSM_ID (4 bytes)
        /// - Offset 40: OEM_ID (2 bytes)
        /// - Offset 42: MODEL_ID (2 bytes)
        /// - Offset 44: Backup OEM_ID (if offset 40 is 0)
        /// </summary>
        private void ProcessV3ExtendedInfo(byte[] extInfo)
        {
            // Read Chip Identifier V3
            uint chipIdV3 = BitConverter.ToUInt32(extInfo, 0);
            if (chipIdV3 != 0)
            {
                _log(string.Format("- Chip Identifier V3 : {0:x8}", chipIdV3));
            }

            // V3 standard format: Offset 36-44
            if (extInfo.Length >= 44)
            {
                uint rawMsm = BitConverter.ToUInt32(extInfo, 36);
                ushort rawOem = BitConverter.ToUInt16(extInfo, 40);
                ushort rawModel = BitConverter.ToUInt16(extInfo, 42);

                uint msmId = rawMsm;  // Use full 32-bit MSM ID

                // Check backup OEM_ID position (offset 44)
                if (rawOem == 0 && extInfo.Length >= 46)
                {
                    ushort altOemId = BitConverter.ToUInt16(extInfo, 44);
                    if (altOemId > 0 && altOemId < 0x1000)
                        rawOem = altOemId;
                }

                if (msmId != 0 || rawOem != 0)
                {
                    // Save to ChipInfo
                    ChipInfo.MsmId = msmId;
                    ChipInfo.OemId = rawOem;
                    ChipInfo.ModelId = rawModel;
                    ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
                    ChipInfo.Vendor = QualcommDatabase.GetVendorName(rawOem);

                    ChipHwId = string.Format("00{0:x6}{1:x4}{2:x4}", msmId, rawOem, rawModel).ToLower();
                    ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

                    // Log output (Format aligned with tools project)
                    _log(string.Format("- MSM HWID : 0x{0:x} | model_id:0x{1:x4} | oem_id:{2:X4} {3}",
                        msmId, rawModel, rawOem, ChipInfo.Vendor));

                    if (ChipInfo.ChipName != "Unknown")
                        _log(string.Format("- CHIP : {0}", ChipInfo.ChipName));

                    _log(string.Format("- HW_ID : {0}", ChipHwId));
                }
            }
        }

        /// <summary>
        /// Safe command execution
        /// </summary>
        private async Task<byte[]> ExecuteCommandSafeAsync(SaharaExecCommand cmd, CancellationToken ct)
        {
            try
            {
                int timeout = cmd == SaharaExecCommand.SblInfoRead ? 5000 : 2000;

                // Send Execute
                var execPacket = new byte[12];
                WriteUInt32(execPacket, 0, (uint)SaharaCommand.Execute);
                WriteUInt32(execPacket, 4, 12);
                WriteUInt32(execPacket, 8, (uint)cmd);
                _port.Write(execPacket);

                // Read response header
                var header = await ReadBytesAsync(8, timeout, ct);
                if (header == null) return null;

                uint respCmd = BitConverter.ToUInt32(header, 0);
                uint respLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)respCmd != SaharaCommand.ExecuteData)
                {
                    if (respLen > 8) await ReadBytesAsync((int)respLen - 8, 1000, ct);
                    return null;
                }

                if (respLen <= 8) return null;
                var body = await ReadBytesAsync((int)respLen - 8, timeout, ct);
                if (body == null || body.Length < 8) return null;

                uint dataCmd = BitConverter.ToUInt32(body, 0);
                uint dataLen = BitConverter.ToUInt32(body, 4);

                if (dataCmd != (uint)cmd || dataLen == 0) return null;

                // Send confirmation
                var respPacket = new byte[12];
                WriteUInt32(respPacket, 0, (uint)SaharaCommand.ExecuteResponse);
                WriteUInt32(respPacket, 4, 12);
                WriteUInt32(respPacket, 8, (uint)cmd);
                _port.Write(respPacket);

                int dataTimeout = dataLen > 1000 ? 10000 : timeout;
                return await ReadBytesAsync((int)dataLen, dataTimeout, ct);
            }
            catch
            {
                return null;
            }
        }

        #region Send Methods

        private void SendHelloResponse(SaharaMode mode)
        {
            var resp = new byte[48];
            WriteUInt32(resp, 0, (uint)SaharaCommand.HelloResponse);
            WriteUInt32(resp, 4, 48);
            WriteUInt32(resp, 8, 2);  // Version
            WriteUInt32(resp, 12, 1); // VersionSupported
            WriteUInt32(resp, 16, (uint)SaharaStatus.Success);
            WriteUInt32(resp, 20, (uint)mode);
            _port.Write(resp);
        }

        private void SendDone()
        {
            var done = new byte[8];
            WriteUInt32(done, 0, (uint)SaharaCommand.Done);
            WriteUInt32(done, 4, 8);
            _port.Write(done);
        }

        private void SendSwitchMode(SaharaMode mode)
        {
            var packet = new byte[12];
            WriteUInt32(packet, 0, (uint)SaharaCommand.SwitchMode);
            WriteUInt32(packet, 4, 12);
            WriteUInt32(packet, 8, (uint)mode);
            _port.Write(packet);
        }

        /// <summary>
        /// Send soft reset command (ResetStateMachine) - Reset state machine, device will resend Hello
        /// </summary>
        public void SendReset()
        {
            var packet = new byte[8];
            WriteUInt32(packet, 0, (uint)SaharaCommand.ResetStateMachine);
            WriteUInt32(packet, 4, 8);
            _port.Write(packet);
        }
        
        /// <summary>
        /// Send hard reset command (Reset) - Completely restart device
        /// </summary>
        public void SendHardReset()
        {
            var packet = new byte[8];
            WriteUInt32(packet, 0, (uint)SaharaCommand.Reset);
            WriteUInt32(packet, 4, 8);
            _port.Write(packet);
        }
        
        /// <summary>
        /// Try resetting stuck Sahara state
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Whether successfully received new Hello packet</returns>
        public async Task<bool> TryResetSaharaAsync(CancellationToken ct = default(CancellationToken))
        {
            _logDetail("[Sahara] Attempting Sahara state reset...");
            
            // Method 1: Send ResetStateMachine command
            _logDetail("[Sahara] Method 1: Sending ResetStateMachine...");
            PurgeBuffer();
            SendReset();
            await Task.Delay(500, ct);
            
            // Check if new Hello received
            var hello = await TryReadHelloAsync(2000, ct);
            if (hello != null)
            {
                _logDetail("[Sahara] ✓ Received new Hello packet, state reset");
                return true;
            }
            
            // Method 2: Send Hello Response to attempt resync
            _logDetail("[Sahara] Method 2: Sending Hello Response to attempt resync...");
            PurgeBuffer();
            await SendHelloResponseAsync(2, 1, SaharaMode.ImageTransferPending, ct);
            await Task.Delay(300, ct);
            
            hello = await TryReadHelloAsync(2000, ct);
            if (hello != null)
            {
                _logDetail("[Sahara] ✓ Received new Hello packet, state reset");
                return true;
            }
            
            // Method 3: Port signal reset (DTR/RTS)
            _logDetail("[Sahara] Method 3: Port signal reset...");
            try
            {
                _port.Close();
                await Task.Delay(200, ct);
                
                // Reopen port and clear buffer
                string portName = _port.PortName;
                if (!string.IsNullOrEmpty(portName))
                {
                    await _port.OpenAsync(portName, 3, true, ct);
                    await Task.Delay(500, ct);
                    
                    hello = await TryReadHelloAsync(3000, ct);
                    if (hello != null)
                    {
                        _logDetail("[Sahara] ✓ Received Hello packet after port reset");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail("[Sahara] Port reset exception: " + ex.Message);
            }
            
            _log("[Sahara] ❌ Cannot reset Sahara state, device may need power cycle");
            return false;
        }
        
        /// <summary>
        /// Try reading Hello packet (For detecting state reset)
        /// </summary>
        private async Task<byte[]> TryReadHelloAsync(int timeoutMs, CancellationToken ct)
        {
            var data = await ReadBytesAsync(48, timeoutMs, ct);
            if (data == null || data.Length < 8)
                return null;
                
            uint cmd = BitConverter.ToUInt32(data, 0);
            if (cmd == (uint)SaharaCommand.Hello)
                return data;
                
            return null;
        }
        
        /// <summary>
        /// Send Hello Response
        /// </summary>
        private async Task SendHelloResponseAsync(uint version, uint versionSupported, SaharaMode mode, CancellationToken ct)
        {
            var response = new SaharaHelloResponse
            {
                Command = (uint)SaharaCommand.HelloResponse,
                Length = 48,
                Version = version,
                VersionSupported = versionSupported,
                Status = 0,
                Mode = (uint)mode
            };
            
            byte[] packet = StructToBytes(response);
            _port.Write(packet);
            await Task.Delay(50, ct);
        }
        
        private static byte[] StructToBytes<T>(T obj) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(obj, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
        }

        #endregion

        #region Utility Methods

        private async Task<byte[]> ReadBytesAsync(int count, int timeoutMs, CancellationToken ct)
        {
            return await _port.TryReadExactAsync(count, timeoutMs, ct);
        }

        private void PurgeBuffer()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                // Release watchdog
                if (_watchdog != null)
                {
                    _watchdog.OnTimeout -= HandleWatchdogTimeout;
                    _watchdog.Dispose();
                    _watchdog = null;
                }
            }
        }
    }
}
