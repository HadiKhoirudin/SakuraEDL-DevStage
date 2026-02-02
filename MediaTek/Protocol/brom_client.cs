// ============================================================================
// LoveAlways - MediaTek BROM Protocol Client
// MediaTek Boot ROM Protocol Client
// ============================================================================
// Reference: mtkclient project mtk_preloader.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.MediaTek.Common;
using LoveAlways.MediaTek.DA;
using LoveAlways.MediaTek.Models;
using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.MediaTek.Protocol
{
    /// <summary>
    /// BROM Protocol Client - Responsible for handshake, device info reading, and DA upload
    /// </summary>
    public class BromClient : IDisposable, IBromClient
    {
        private SerialPort _port;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<double> _progressCallback;
        private readonly MtkLogger _logger;
        private bool _disposed;

        // Thread safety: Port lock
        private readonly SemaphoreSlim _portLock = new SemaphoreSlim(1, 1);

        // Configuration
        private const int DEFAULT_TIMEOUT_MS = 5000;
        private const int HANDSHAKE_TIMEOUT_MS = 30000;
        private const int MAX_PACKET_SIZE = 4096;

        // Protocol Status
        public bool IsConnected { get; private set; }
        public bool IsBromMode { get; private set; }
        public MtkDeviceState State { get; internal set; }

        /// <summary>
        /// Last upload status code
        /// 0x0000 = Success
        /// 0x7015 = DAA signature verification failed
        /// 0x7017 = DAA security error (DAA protection enabled)
        /// </summary>
        public ushort LastUploadStatus { get; private set; }

        // Device Information
        public ushort HwCode { get; private set; }
        public ushort HwVer { get; private set; }
        public ushort HwSubCode { get; private set; }
        public ushort SwVer { get; private set; }
        public byte BromVer { get; private set; }
        public byte BlVer { get; private set; }
        public byte[] MeId { get; private set; }
        public byte[] SocId { get; private set; }
        public TargetConfigFlags TargetConfig { get; private set; }
        public MtkChipInfo ChipInfo { get; private set; }

        public BromClient(Action<string> log = null, Action<string> logDetail = null, Action<double> progressCallback = null)
        {
            _log = log ?? delegate { };
            _logDetail = logDetail ?? _log;
            _progressCallback = progressCallback;
            _logger = null;  // Use legacy callbacks
            State = MtkDeviceState.Disconnected;
            ChipInfo = new MtkChipInfo();
        }

        /// <summary>
        /// Constructor (using MtkLogger)
        /// </summary>
        public BromClient(MtkLogger logger, Action<double> progressCallback = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _log = msg => _logger.Info(msg, LogCategory.Brom);
            _logDetail = msg => _logger.Verbose(msg, LogCategory.Brom);
            _progressCallback = progressCallback;
            State = MtkDeviceState.Disconnected;
            ChipInfo = new MtkChipInfo();
        }

        #region Connection Management

        /// <summary>
        /// Connect to serial port
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, int baudRate = 921600, CancellationToken ct = default)
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.Close();
                }

                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = DEFAULT_TIMEOUT_MS,
                    WriteTimeout = DEFAULT_TIMEOUT_MS,
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadBufferSize = 16 * 1024 * 1024,  // 16MB buffer
                    WriteBufferSize = 16 * 1024 * 1024
                };

                _port.Open();
                await Task.Delay(100, ct);

                // Clear buffers
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();

                IsConnected = true;
                State = MtkDeviceState.Handshaking;
                _log($"[MediaTek] Serial port opened: {portName}");

                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.Close();
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[MediaTek] Exception during disconnect: {ex.Message}");
            }

            IsConnected = false;
            State = MtkDeviceState.Disconnected;
        }

        /// <summary>
        /// Get internal serial port (for exploits)
        /// </summary>
        public SerialPort GetPort() => _port;

        /// <summary>
        /// Check if port is open
        /// </summary>
        public bool IsPortOpen => _port != null && _port.IsOpen;

        /// <summary>
        /// Get current port name
        /// </summary>
        public string PortName => _port?.PortName ?? "";

        #endregion

        #region Handshake (BROM/Preloader common)

        /// <summary>
        /// Perform handshake (BROM and Preloader modes use the same sequence)
        /// </summary>
        public async Task<bool> HandshakeAsync(int maxTries = 100, CancellationToken ct = default)
        {
            if (!IsConnected || _port == null)
                return false;

            _log("[MediaTek] Starting handshake...");
            State = MtkDeviceState.Handshaking;

            // Clear buffer before handshake to prevent interference
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            for (int tries = 0; tries < maxTries; tries++)
            {
                if (ct.IsCancellationRequested)
                    return false;

                try
                {
                    // Send handshake byte 0xA0
                    _port.Write(new byte[] { BromHandshake.HANDSHAKE_SEND }, 0, 1);

                    await Task.Delay(10, ct);

                    // Check response
                    if (_port.BytesToRead > 0)
                    {
                        byte[] response = new byte[_port.BytesToRead];
                        _port.Read(response, 0, response.Length);

                        // Check if 0x5F is received
                        foreach (byte b in response)
                        {
                            if (b == BromHandshake.HANDSHAKE_RESPONSE)
                            {
                                // Continue sending the rest of the sequence
                                bool success = await CompleteHandshakeAsync(ct);
                                if (success)
                                {
                                    _log("[MediaTek] ✓ Handshake successful");
                                    // Clear buffer after success
                                    _port.DiscardInBuffer();
                                    _port.DiscardOutBuffer();
                                    // Note: Actual mode (BROM/Preloader) is set in InitializeAsync based on BL Ver
                                    return true;
                                }
                            }
                        }
                    }

                    if (tries % 20 == 0 && tries > 0)
                    {
                        _logDetail($"[MediaTek] Retrying handshake... ({tries}/{maxTries})");
                        // Clear buffer every 20 retries
                        _port.DiscardInBuffer();
                        _port.DiscardOutBuffer();
                    }

                    // Dynamic retry interval: fast initially, then longer
                    int delayMs = tries < 20 ? 50 : (tries < 50 ? 100 : 200);
                    await Task.Delay(delayMs, ct);
                }
                catch (TimeoutException)
                {
                    // Timeout, continue retrying
                }
                catch (Exception ex)
                {
                    _logDetail($"[MediaTek] Handshake exception: {ex.Message}");
                }
            }

            _log("[MediaTek] ❌ Handshake timeout");
            State = MtkDeviceState.Error;

            // Clear buffer on failure
            try
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BROM] Exception clearing buffer: {ex.Message}"); }

            return false;
        }

        /// <summary>
        /// Complete handshake sequence
        /// </summary>
        private async Task<bool> CompleteHandshakeAsync(CancellationToken ct)
        {
            try
            {
                // Received 0x5F, continue with 0x0A
                _port.Write(new byte[] { 0x0A }, 0, 1);
                await Task.Delay(10, ct);

                // Expecting 0xF5
                byte[] resp1 = await ReadBytesAsync(1, 1000, ct);
                if (resp1 == null || resp1[0] != 0xF5)
                {
                    _logDetail($"[MediaTek] Handshake sequence error: expected 0xF5, received 0x{resp1?[0]:X2}");
                    return false;
                }

                // Send 0x50
                _port.Write(new byte[] { 0x50 }, 0, 1);
                await Task.Delay(10, ct);

                // Expecting 0xAF
                byte[] resp2 = await ReadBytesAsync(1, 1000, ct);
                if (resp2 == null || resp2[0] != 0xAF)
                {
                    _logDetail($"[MediaTek] Handshake sequence error: expected 0xAF, received 0x{resp2?[0]:X2}");
                    return false;
                }

                // Send 0x05
                _port.Write(new byte[] { 0x05 }, 0, 1);
                await Task.Delay(10, ct);

                // Expect 0xFA
                byte[] resp3 = await ReadBytesAsync(1, 1000, ct);
                if (resp3 == null || resp3[0] != 0xFA)
                {
                    _logDetail($"[MediaTek] Handshake sequence error: expected 0xFA, received 0x{resp3?[0]:X2}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logDetail($"[MediaTek] Handshake sequence exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Device Info Reading

        /// <summary>
        /// Initialize device (Read chip info)
        /// </summary>
        public async Task<bool> InitializeAsync(bool skipWdt = false, CancellationToken ct = default)
        {
            // Initialization only after successful handshake
            if (State == MtkDeviceState.Disconnected || State == MtkDeviceState.Error)
                return false;

            try
            {
                // 1. Get HW Code
                var hwInfo = await GetHwCodeAsync(ct);
                if (hwInfo != null)
                {
                    HwCode = hwInfo.Value.hwCode;
                    HwVer = hwInfo.Value.hwVer;
                    _log($"[MediaTek] HW Code: 0x{HwCode:X4}");
                    _log($"[MediaTek] HW Ver: 0x{HwVer:X4}");

                    // Load full chip info from database
                    var chipRecord = Database.MtkChipDatabase.GetChip(HwCode);
                    if (chipRecord != null)
                    {
                        ChipInfo = Database.MtkChipDatabase.ToChipInfo(chipRecord);
                        ChipInfo.HwVer = HwVer;  // Keep device-reported version

                        // Output format reference mtkclient
                        _log($"\tCPU:\t{ChipInfo.ChipName}({ChipInfo.Description})");
                        _log($"\tHW version:\t0x{HwVer:X}");
                        _log($"\tWDT:\t\t0x{ChipInfo.WatchdogAddr:X}");
                        _log($"\tUART:\t\t0x{ChipInfo.UartAddr:X}");
                        _log($"\tBrom Payload Address:\t0x{ChipInfo.BromPayloadAddr:X}");
                        _log($"\tDA Payload Address:\t0x{ChipInfo.DaPayloadAddr:X}");
                        if (ChipInfo.CqDmaBase.HasValue)
                            _log($"\tCQ_DMA Address:\t0x{ChipInfo.CqDmaBase.Value:X}");
                        _log($"\tVar1:\t\t0xA");  // Default value
                    }
                    else
                    {
                        // Unknown chip, use defaults
                        ChipInfo.HwCode = HwCode;
                        ChipInfo.HwVer = HwVer;
                        ChipInfo.WatchdogAddr = 0x10007000;
                        ChipInfo.UartAddr = 0x11002000;
                        ChipInfo.BromPayloadAddr = 0x100A00;
                        ChipInfo.DaPayloadAddr = 0x200000;  // Default address

                        _log($"[MediaTek] Unknown chip: 0x{HwCode:X4} (using default config)");
                        _log($"\tWDT:\t\t0x{ChipInfo.WatchdogAddr:X}");
                        _log($"\tDA Payload Address:\t0x{ChipInfo.DaPayloadAddr:X}");
                    }
                }

                // 2. Send heartbeat/sync (ChimeraTool sends a0 * 20)
                _log("[MediaTek] Sending synchronization heartbeat...");
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        _port.Write(new byte[] { 0xA0 }, 0, 1);
                        await Task.Delay(5, ct);
                        if (_port.BytesToRead > 0)
                        {
                            byte[] resp = new byte[_port.BytesToRead];
                            _port.Read(resp, 0, resp.Length);
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BROM] Exception clearing state: {ex.Message}"); }
                }

                // 3. Get target config (ChimeraTool execution)
                var config = await GetTargetConfigAsync(ct);
                if (config != null)
                {
                    TargetConfig = config.Value;
                    _log($"[MediaTek] Target Config: 0x{(uint)TargetConfig:X8}");
                    LogTargetConfig(TargetConfig);
                }

                // 4. Get BL version (determine mode)
                BlVer = await GetBlVerAsync(ct);
                IsBromMode = (BlVer == BromCommands.CMD_GET_BL_VER);

                if (IsBromMode)
                {
                    _log("[MediaTek] Mode: BROM (Boot ROM)");
                    State = MtkDeviceState.Brom;
                }
                else
                {
                    _log($"[MediaTek] Mode: Preloader (BL Ver: {BlVer})");
                    State = MtkDeviceState.Preloader;
                }

                // 5. Get ME ID (ChimeraTool execution)
                MeId = await GetMeIdAsync(ct);
                if (MeId != null && MeId.Length > 0)
                {
                    _logDetail($"[MediaTek] ME ID: {BitConverter.ToString(MeId).Replace("-", "")}");
                }

                // 6. Other info (Optional, for display)
                BromVer = await GetBromVerAsync(ct);
                var hwSwVer = await GetHwSwVerAsync(ct);
                if (hwSwVer != null)
                {
                    HwSubCode = hwSwVer.Value.hwSubCode;
                    HwVer = hwSwVer.Value.hwVer;
                    SwVer = hwSwVer.Value.swVer;
                }
                SocId = await GetSocIdAsync(ct);

                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get HW Code
        /// </summary>
        public async Task<(ushort hwCode, ushort hwVer)?> GetHwCodeAsync(CancellationToken ct = default)
        {
            if (!await EchoAsync(BromCommands.CMD_GET_HW_CODE, ct))
                return null;

            var response = await ReadBytesAsync(4, DEFAULT_TIMEOUT_MS, ct);
            if (response == null || response.Length < 4)
                return null;

            ushort hwCode = MtkDataPacker.UnpackUInt16BE(response, 0);
            ushort hwVer = MtkDataPacker.UnpackUInt16BE(response, 2);

            return (hwCode, hwVer);
        }

        /// <summary>
        /// Get Target Config
        /// </summary>
        public async Task<TargetConfigFlags?> GetTargetConfigAsync(CancellationToken ct = default)
        {
            if (!await EchoAsync(BromCommands.CMD_GET_TARGET_CONFIG, ct))
                return null;

            var response = await ReadBytesAsync(6, DEFAULT_TIMEOUT_MS, ct);
            if (response == null || response.Length < 6)
                return null;

            uint config = MtkDataPacker.UnpackUInt32BE(response, 0);
            ushort status = MtkDataPacker.UnpackUInt16BE(response, 4);

            if (status > 0xFF)
                return null;

            return (TargetConfigFlags)config;
        }

        /// <summary>
        /// Get BL Version (with thread safety)
        /// </summary>
        public async Task<byte> GetBlVerAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(new byte[] { BromCommands.CMD_GET_BL_VER }, 0, 1);
                var response = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                return response != null && response.Length > 0 ? response[0] : (byte)0;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Get BROM Version (with thread safety)
        /// </summary>
        public async Task<byte> GetBromVerAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(new byte[] { BromCommands.CMD_GET_VERSION }, 0, 1);
                var response = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                return response != null && response.Length > 0 ? response[0] : (byte)0;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Get HW/SW Version
        /// </summary>
        public async Task<(ushort hwSubCode, ushort hwVer, ushort swVer, ushort reserved)?> GetHwSwVerAsync(CancellationToken ct = default)
        {
            var response = await SendCmdAsync(BromCommands.CMD_GET_HW_SW_VER, 8, ct);
            if (response == null || response.Length < 8)
                return null;

            return (
                MtkDataPacker.UnpackUInt16BE(response, 0),
                MtkDataPacker.UnpackUInt16BE(response, 2),
                MtkDataPacker.UnpackUInt16BE(response, 4),
                MtkDataPacker.UnpackUInt16BE(response, 6)
            );
        }

        /// <summary>
        /// Get ME ID (with thread safety)
        /// </summary>
        public async Task<byte[]> GetMeIdAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                // First check BL version
                _port.Write(new byte[] { BromCommands.CMD_GET_BL_VER }, 0, 1);
                var blResp = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (blResp == null) return null;

                // Send GET_ME_ID command
                _port.Write(new byte[] { BromCommands.CMD_GET_ME_ID }, 0, 1);
                var cmdResp = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (cmdResp == null || cmdResp[0] != BromCommands.CMD_GET_ME_ID)
                    return null;

                // Read length
                var lenResp = await ReadBytesInternalAsync(4, DEFAULT_TIMEOUT_MS, ct);
                if (lenResp == null) return null;

                uint length = MtkDataPacker.UnpackUInt32BE(lenResp, 0);
                if (length == 0 || length > 64) return null;

                // Read ME ID
                var meId = await ReadBytesInternalAsync((int)length, DEFAULT_TIMEOUT_MS, ct);
                if (meId == null) return null;

                // Read status
                var statusResp = await ReadBytesInternalAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null) return null;

                ushort status = MtkDataPacker.UnpackUInt16LE(statusResp, 0);
                if (status != 0) return null;

                return meId;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Get SOC ID (with thread safety)
        /// </summary>
        public async Task<byte[]> GetSocIdAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                // First check BL version
                _port.Write(new byte[] { BromCommands.CMD_GET_BL_VER }, 0, 1);
                var blResp = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (blResp == null)
                {
                    // Clear potential residual data
                    await Task.Delay(50, ct);
                    if (_port.BytesToRead > 0)
                    {
                        byte[] junk = new byte[_port.BytesToRead];
                        _port.Read(junk, 0, junk.Length);
                    }
                    return null;
                }

                // Send GET_SOC_ID command
                _port.Write(new byte[] { BromCommands.CMD_GET_SOC_ID }, 0, 1);
                var cmdResp = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (cmdResp == null || cmdResp[0] != BromCommands.CMD_GET_SOC_ID)
                {
                    // Device may not support this command, clear residual data
                    await Task.Delay(50, ct);
                    if (_port.BytesToRead > 0)
                    {
                        byte[] junk = new byte[_port.BytesToRead];
                        _port.Read(junk, 0, junk.Length);
                    }
                    return null;
                }

                // Read length
                var lenResp = await ReadBytesInternalAsync(4, DEFAULT_TIMEOUT_MS, ct);
                if (lenResp == null) return null;

                uint length = MtkDataPacker.UnpackUInt32BE(lenResp, 0);
                if (length == 0 || length > 64) return null;

                // Read SOC ID
                var socId = await ReadBytesInternalAsync((int)length, DEFAULT_TIMEOUT_MS, ct);
                if (socId == null) return null;

                // Read status
                var statusResp = await ReadBytesInternalAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null) return null;

                ushort status = MtkDataPacker.UnpackUInt16LE(statusResp, 0);
                if (status != 0) return null;

                return socId;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Log target config details
        /// </summary>
        private void LogTargetConfig(TargetConfigFlags config)
        {
            bool sbc = config.HasFlag(TargetConfigFlags.SbcEnabled);
            bool sla = config.HasFlag(TargetConfigFlags.SlaEnabled);
            bool daa = config.HasFlag(TargetConfigFlags.DaaEnabled);

            // Output main security status
            _log($"\tSBC (Secure Boot):\t{sbc}");
            _log($"\tSLA (Secure Link Auth):\t{sla}");
            _log($"\tDAA (Download Agent Auth):\t{daa}");

            // Detect protection status
            if (sbc || daa)
            {
                _log("Device is in protected state");
            }
        }

        #endregion

        #region Memory Read/Write

        /// <summary>
        /// Read 32-bit data
        /// </summary>
        public async Task<uint[]> Read32Async(uint address, int count = 1, CancellationToken ct = default)
        {
            if (!await EchoAsync(BromCommands.CMD_READ32, ct))
                return null;

            // Send address
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE(address), ct))
                return null;

            // Send count
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)count), ct))
                return null;

            // Read status
            var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            if (statusResp == null) return null;

            ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
            if (!BromErrorHelper.IsSuccess(status))
                return null;

            // Read data
            uint[] result = new uint[count];
            for (int i = 0; i < count; i++)
            {
                var data = await ReadBytesAsync(4, DEFAULT_TIMEOUT_MS, ct);
                if (data == null) return null;
                result[i] = MtkDataPacker.UnpackUInt32BE(data, 0);
            }

            // Read final status
            var status2Resp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            if (status2Resp == null) return null;

            return result;
        }

        /// <summary>
        /// Write 32-bit data
        /// </summary>
        public async Task<bool> Write32Async(uint address, uint[] values, CancellationToken ct = default)
        {
            if (!await EchoAsync(BromCommands.CMD_WRITE32, ct))
                return false;

            // Send address
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE(address), ct))
                return false;

            // Send count
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)values.Length), ct))
                return false;

            // Read status
            var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            if (statusResp == null) return false;

            ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
            if (!BromErrorHelper.IsSuccess(status))
            {
                _log($"[MediaTek] Write32 status error: {BromErrorHelper.GetErrorMessage(status)}");
                return false;
            }

            // Write data
            foreach (uint value in values)
            {
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE(value), ct))
                    return false;
            }

            // Read final status
            var status2Resp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            if (status2Resp == null) return false;

            ushort status2 = MtkDataPacker.UnpackUInt16BE(status2Resp, 0);
            return BromErrorHelper.IsSuccess(status2);
        }

        /// <summary>
        /// Disable watchdog
        /// </summary>
        private async Task<bool> DisableWatchdogAsync(CancellationToken ct = default)
        {
            // Default watchdog address and value (common chips like MT6765)
            uint wdtAddr = 0x10007000;
            uint wdtValue = 0x22000000;

            // Adjust based on HW Code
            switch (HwCode)
            {
                case 0x6261:  // MT6261
                case 0x2523:  // MT2523
                case 0x7682:  // MT7682
                case 0x7686:  // MT7686
                    // 16-bit write
                    return await Write16Async(0xA2050000, new ushort[] { 0x2200 }, ct);

                default:
                    // 32-bit write
                    return await Write32Async(wdtAddr, new uint[] { wdtValue }, ct);
            }
        }

        /// <summary>
        /// Write 16-bit data
        /// </summary>
        public async Task<bool> Write16Async(uint address, ushort[] values, CancellationToken ct = default)
        {
            if (!await EchoAsync(BromCommands.CMD_WRITE16, ct))
                return false;

            // Send address
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE(address), ct))
                return false;

            // Send count
            if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)values.Length), ct))
                return false;

            // Read status
            var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            if (statusResp == null) return false;

            ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
            if (!BromErrorHelper.IsSuccess(status))
                return false;

            // Write data
            foreach (ushort value in values)
            {
                if (!await EchoAsync(MtkDataPacker.PackUInt16BE(value), ct))
                    return false;
            }

            // Read final status
            var status2Resp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
            return status2Resp != null;
        }

        #endregion

        #region Exploit Payload Operations

        /// <summary>
        /// Send BROM Exploit Payload (using SEND_CERT command)
        /// Reference: SP Flash Tool and mtkclient send_root_cert
        /// </summary>
        /// <param name="payload">Exploit payload data</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SendExploitPayloadAsync(byte[] payload, CancellationToken ct = default)
        {
            try
            {
                _log($"[MediaTek] Sending Exploit Payload, size: {payload.Length} bytes (0x{payload.Length:X})");

                // 1. Send SEND_CERT command (0xE0)
                if (!await EchoAsync(BromCommands.CMD_SEND_CERT, ct))
                {
                    _log("[MediaTek] SEND_CERT command echo failed");
                    return false;
                }

                // 2. Send payload length (Big-Endian)
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)payload.Length), ct))
                {
                    _log("[MediaTek] Payload length echo failed");
                    return false;
                }

                // 3. Read status
                var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null)
                {
                    _log("[MediaTek] Failed to read SEND_CERT status");
                    return false;
                }

                ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
                _log($"[MediaTek] SEND_CERT status: 0x{status:X4}");

                if (status > 0xFF)
                {
                    _log($"[MediaTek] SEND_CERT rejected: {BromErrorHelper.GetErrorMessage(status)}");
                    return false;
                }

                // 4. Calculate checksum
                ushort checksum = 0;
                foreach (byte b in payload)
                {
                    checksum += b;
                }

                // 5. Upload payload data (reference mtkclient upload_data)
                int chunkSize = 0x400;  // 1KB chunks
                int pos = 0;
                int flushCounter = 0;

                await _portLock.WaitAsync(ct);
                try
                {
                    while (pos < payload.Length)
                    {
                        int remaining = payload.Length - pos;
                        int size = Math.Min(remaining, chunkSize);

                        _port.Write(payload, pos, size);
                        pos += size;
                        flushCounter += size;

                        // Flush every 0x2000 bytes
                        if (flushCounter >= 0x2000)
                        {
                            _port.Write(new byte[0], 0, 0);  // Empty packet flush
                            flushCounter = 0;
                        }
                    }

                    // 6. Send empty packet as end marker
                    _port.Write(new byte[0], 0, 0);
                }
                finally
                {
                    _portLock.Release();
                }

                // 7. Wait briefly (Mtk reference: 10ms is sufficient)
                await Task.Delay(10, ct);

                // 8. Read checksum response
                var checksumResp = await ReadBytesAsync(2, 2000, ct);
                if (checksumResp != null)
                {
                    ushort receivedChecksum = MtkDataPacker.UnpackUInt16BE(checksumResp, 0);
                    _log($"[MediaTek] Payload Checksum: received 0x{receivedChecksum:X4}, expected 0x{checksum:X4}");
                }

                // 9. Read final status
                var finalStatusResp = await ReadBytesAsync(2, 2000, ct);
                if (finalStatusResp != null)
                {
                    ushort finalStatus = MtkDataPacker.UnpackUInt16BE(finalStatusResp, 0);
                    _log($"[MediaTek] Payload Upload Status: 0x{finalStatus:X4}");

                    if (finalStatus <= 0xFF)
                    {
                        _log("[MediaTek] ✓ Exploit Payload uploaded successfully");
                        return true;
                    }
                    else
                    {
                        _log($"[MediaTek] Payload upload failed: {BromErrorHelper.GetErrorMessage(finalStatus)}");
                    }
                }

                return true;  // Some devices may not return status but still execute payload
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] SendExploitPayloadAsync exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load and send BROM Exploit Payload
        /// </summary>
        /// <param name="payloadPath">Payload file path</param>
        /// <param name="ct">Cancellation token</param>
        public async Task<bool> SendExploitPayloadFromFileAsync(string payloadPath, CancellationToken ct = default)
        {
            try
            {
                if (!System.IO.File.Exists(payloadPath))
                {
                    _log($"[MediaTek] Payload file does not exist: {payloadPath}");
                    return false;
                }

                byte[] payload = System.IO.File.ReadAllBytes(payloadPath);
                _log($"[MediaTek] Loaded Payload: {System.IO.Path.GetFileName(payloadPath)}, {payload.Length} bytes");

                return await SendExploitPayloadAsync(payload, ct);
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Failed to load payload: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region DA Operations

        /// <summary>
        /// Send DA (Download Agent)
        /// </summary>
        public async Task<bool> SendDaAsync(uint address, byte[] data, int sigLen = 0, CancellationToken ct = default)
        {
            try
            {
                _log($"[MediaTek] Sending DA to address 0x{address:X8}, size {data.Length} bytes, signature length: 0x{sigLen:X}");

                // Prepare data and checksum
                byte[] dataWithoutSig = data;
                byte[] signature = null;
                if (sigLen > 0)
                {
                    if (data.Length < sigLen)
                    {
                        _log($"[MediaTek] Error: Data length {data.Length} smaller than signature length {sigLen}");
                        return false;
                    }
                    dataWithoutSig = new byte[data.Length - sigLen];
                    Array.Copy(data, 0, dataWithoutSig, 0, data.Length - sigLen);
                    signature = new byte[sigLen];
                    Array.Copy(data, data.Length - sigLen, signature, 0, sigLen);
                    _log($"[MediaTek] Data split: Main {dataWithoutSig.Length} bytes, Signature {signature.Length} bytes");
                }
                var (checksum, processedData) = MtkChecksum.PrepareData(
                    dataWithoutSig,
                    signature,
                    data.Length - sigLen
                );
                _log($"[MediaTek] Processed data: {processedData.Length} bytes, XOR checksum: 0x{checksum:X4}");

                // Send SEND_DA command (reference MtkPreloader.cs)
                _log("[MediaTek] Sending SEND_DA command (0xD7)...");

                // Clear residual data in buffer
                if (_port.BytesToRead > 0)
                {
                    byte[] junk = new byte[_port.BytesToRead];
                    _port.Read(junk, 0, junk.Length);
                    _log($"[MediaTek] Clearing buffer: {junk.Length} bytes ({BitConverter.ToString(junk)})");
                }

                // Send command and check response
                await WriteBytesAsync(new byte[] { BromCommands.CMD_SEND_DA }, ct);
                var cmdResp = await ReadBytesAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (cmdResp == null || cmdResp.Length == 0)
                {
                    _log("[MediaTek] No command response");
                    return false;
                }

                bool useAlternativeProtocol = false;

                if (cmdResp[0] == BromCommands.CMD_SEND_DA)
                {
                    // Standard echo, continue normal flow
                    _log("[MediaTek] ✓ SEND_DA command confirmed (Standard echo)");
                }
                else if (cmdResp[0] == 0xE7)
                {
                    // Likely status response (0xE7 might mean command accepted)
                    _log("[MediaTek] Received response 0xE7, checking status...");

                    // Read status code
                    var statusData1 = await ReadBytesAsync(2, 500, ct);
                    if (statusData1 != null && statusData1.Length >= 2)
                    {
                        ushort respStatus1 = MtkDataPacker.UnpackUInt16BE(statusData1, 0);
                        _log($"[MediaTek] Status code: 0x{respStatus1:X4}");

                        if (respStatus1 == 0x0000)
                        {
                            // Status 0x0000 means command accepted, try alternative protocol
                            _log("[MediaTek] Status 0x0000, trying alternative protocol flow...");
                            useAlternativeProtocol = true;
                        }
                        else
                        {
                            _log($"[MediaTek] Command rejected, status: 0x{respStatus1:X4}");
                            LastUploadStatus = respStatus1;
                            return false;
                        }
                    }
                }
                else if (cmdResp[0] == 0x00)
                {
                    // Device might return status directly
                    var statusData2 = await ReadBytesAsync(1, 500, ct);
                    if (statusData2 != null && statusData2.Length >= 1)
                    {
                        ushort respStatus2 = (ushort)((cmdResp[0] << 8) | statusData2[0]);
                        _log($"[MediaTek] Device returned status: 0x{respStatus2:X4}");
                        LastUploadStatus = respStatus2;

                        if (respStatus2 == 0x0000)
                        {
                            _log("[MediaTek] Status 0x0000, trying alternative protocol flow...");
                            useAlternativeProtocol = true;
                        }
                        else
                        {
                            _log($"[MediaTek] Command failed: 0x{respStatus2:X4}");
                            return false;
                        }
                    }
                    return false;
                }
                else
                {
                    _log($"[MediaTek] Unknown response: 0x{cmdResp[0]:X2}");
                    // Try to read more data for bypass diagnosis
                    var moreData = await ReadBytesAsync(4, 200, ct);
                    if (moreData != null && moreData.Length > 0)
                    {
                        _log($"[MediaTek] Extra data: {BitConverter.ToString(moreData)}");
                    }
                    return false;
                }

                if (useAlternativeProtocol)
                {
                    // Alternative protocol: Device may already be waiting for data
                    // Try sending parameters directly (without expecting echo)
                    _log("[MediaTek] Using alternative protocol: Sending parameters directly");

                    // Send address
                    await WriteBytesAsync(MtkDataPacker.PackUInt32BE(address), ct);
                    await Task.Delay(10, ct);

                    // Send size
                    await WriteBytesAsync(MtkDataPacker.PackUInt32BE((uint)processedData.Length), ct);
                    await Task.Delay(10, ct);

                    // Send signature length
                    await WriteBytesAsync(MtkDataPacker.PackUInt32BE((uint)sigLen), ct);
                    await Task.Delay(10, ct);

                    // Read status
                    var altStatus = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                    if (altStatus != null && altStatus.Length >= 2)
                    {
                        ushort altRespStatus = MtkDataPacker.UnpackUInt16BE(altStatus, 0);
                        _log($"[MediaTek] Alternative protocol status: 0x{altRespStatus:X4}");

                        if (altRespStatus != 0x0000)
                        {
                            LastUploadStatus = altRespStatus;
                            _log($"[MediaTek] Alternative protocol failed: 0x{altRespStatus:X4}");
                            return false;
                        }
                    }

                    // Send data
                    _log($"[MediaTek] Sending DA data ({processedData.Length} bytes)...");
                    await WriteBytesAsync(processedData, ct);
                    await Task.Delay(100, ct);

                    // Read checksum/status
                    var finalResp = await ReadBytesAsync(4, DEFAULT_TIMEOUT_MS, ct);
                    if (finalResp != null && finalResp.Length >= 2)
                    {
                        ushort recvChecksum = MtkDataPacker.UnpackUInt16BE(finalResp, 0);
                        _log($"[MediaTek] Device checksum: 0x{recvChecksum:X4}, expected: 0x{checksum:X4}");

                        if (finalResp.Length >= 4)
                        {
                            ushort finalStatus = MtkDataPacker.UnpackUInt16BE(finalResp, 2);
                            _log($"[MediaTek] Final status: 0x{finalStatus:X4}");
                            LastUploadStatus = finalStatus;
                            return finalStatus == 0x0000;
                        }
                        return recvChecksum == checksum;
                    }

                    _log("[MediaTek] Alternative protocol: No final response");
                    return false;
                }

                // Send address and wait for echo
                _log($"[MediaTek] Sending address: 0x{address:X8}");
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE(address), ct))
                {
                    _log("[MediaTek] Sending address failed");
                    return false;
                }

                // Send size and wait for echo
                _log($"[MediaTek] Sending size: {processedData.Length} bytes");
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)processedData.Length), ct))
                {
                    _log("[MediaTek] Sending size failed");
                    return false;
                }

                // Send signature length and wait for echo
                _log($"[MediaTek] Sending signature length: 0x{sigLen:X} ({sigLen} bytes)");
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)sigLen), ct))
                {
                    _log("[MediaTek] Sending signature length failed");
                    return false;
                }

                // Read status (2 bytes)
                _log("[MediaTek] Waiting for device response status...");
                var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null)
                {
                    _log("[MediaTek] Reading status failed (Timeout or no response)");
                    return false;
                }

                ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
                _log($"[MediaTek] SEND_DA status: 0x{status:X4}");

                LastUploadStatus = status;  // Save status for caller

                // Check status code 0x0010 or 0x0011 (Preloader mode Auth requirement)
                if (status == (ushort)BromStatus.AuthRequired || status == (ushort)BromStatus.PreloaderAuth)
                {
                    _log($"[MediaTek] ⚠ Preloader mode requires AUTH (status: 0x{status:X4})");
                    _log("[MediaTek] Device has DAA protection enabled in Preloader mode");
                    _log("[MediaTek] Official signed DA or DA2-level exploit (ALLINONE-SIGNATURE) required");
                    LastUploadStatus = status;
                    return false;
                }

                // Check if SLA authentication required (status code 0x1D0D)
                if (status == (ushort)BromStatus.SlaRequired)
                {
                    _log("[MediaTek] SLA authentication required...");

                    // Execute SLA authentication
                    var slaAuth = new MtkSlaAuth(msg => _log(msg));
                    bool authSuccess = await slaAuth.AuthenticateAsync(
                        async (authData, len, token) =>
                        {
                            _port.Write(authData, 0, len);
                            return true;
                        },
                        async (count, timeout, token) => await ReadBytesInternalAsync(count, timeout, token),
                        HwCode,
                        ct
                    );

                    if (!authSuccess)
                    {
                        _log("[MediaTek] SLA authentication failed");
                        return false;
                    }

                    _log("[MediaTek] ✓ SLA authentication successful");
                    status = 0;  // Reset status after successful auth
                }

                // Status code check (mtkclient: 0 <= status <= 0xFF indicates success)
                if (status > 0xFF)
                {
                    _log($"[MediaTek] SEND_DA status error: 0x{status:X4} ({BromErrorHelper.GetErrorMessage(status)})");
                    return false;
                }

                _log($"[MediaTek] ✓ SEND_DA status normal: 0x{status:X4}");
                _log($"[MediaTek] Preparing to upload data: {processedData.Length} bytes, checksum: 0x{checksum:X4}");

                // Upload data
                _log("[MediaTek] Calling UploadDataAsync...");
                bool uploadResult = false;
                try
                {
                    uploadResult = await UploadDataAsync(processedData, checksum, ct);
                }
                catch (Exception uploadEx)
                {
                    _log($"[MediaTek] UploadDataAsync exception: {uploadEx.Message}");
                    return false;
                }
                _log($"[MediaTek] Upload data result: {uploadResult}");

                if (!uploadResult)
                {
                    _log("[MediaTek] Data upload failed");
                    return false;
                }

                _log("[MediaTek] ✓ DA sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] SendDaAsync exception: {ex.Message}");
                _log($"[MediaTek] Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Jump to DA execution
        /// Reference mtkclient: jump_da()
        /// </summary>
        public async Task<bool> JumpDaAsync(uint address, CancellationToken ct = default)
        {
            _log($"[MediaTek] Jumping to DA address 0x{address:X8}");

            try
            {
                // 1. Send JUMP_DA command and wait for echo
                if (!await EchoAsync(BromCommands.CMD_JUMP_DA, ct))
                {
                    _log("[MediaTek] JUMP_DA command echo failed");
                    return false;
                }

                // 2. Send address (mtkclient: usbwrite, not echo)
                await _portLock.WaitAsync(ct);
                try
                {
                    _port.Write(MtkDataPacker.PackUInt32BE(address), 0, 4);
                }
                finally
                {
                    _portLock.Release();
                }

                // 3. Read address echo
                var addrResp = await ReadBytesAsync(4, DEFAULT_TIMEOUT_MS, ct);
                if (addrResp == null)
                {
                    _log("[MediaTek] Reading address echo timeout");
                    return false;
                }

                uint respAddr = MtkDataPacker.UnpackUInt32BE(addrResp, 0);
                if (respAddr != address)
                {
                    _log($"[MediaTek] Address mismatch: expected 0x{address:X8}, received 0x{respAddr:X8}");
                    return false;
                }

                // 4. Read status (mtkclient: read status then sleep immediately, don't handle trailing data)
                var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null)
                {
                    _log("[MediaTek] Reading status timeout");
                    return false;
                }

                ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
                _log($"[MediaTek] JUMP_DA status: 0x{status:X4}");

                // mtkclient: if status == 0: return True
                if (status != 0)
                {
                    _log($"[MediaTek] JUMP_DA status error: 0x{status:X4} ({BromErrorHelper.GetErrorMessage(status)})");
                    return false;
                }

                // 5. Wait for DA to start (mtkclient: time.sleep(0.1))
                await Task.Delay(100, ct);

                _log("[MediaTek] ✓ JUMP_DA successful");
                State = MtkDeviceState.Da1Loaded;
                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] JumpDaAsync exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to detect if DA is ready (by reading sync signal)
        /// </summary>
        public async Task<bool> TryDetectDaReadyAsync(CancellationToken ct = default)
        {
            try
            {
                await _portLock.WaitAsync(ct);
                try
                {
                    // Clear receive buffer
                    _port.DiscardInBuffer();

                    // Try reading DA sync signal
                    // DA typically sends "SYNC" (0x434E5953) or specific byte sequence after starting
                    byte[] buffer = new byte[64];
                    int totalRead = 0;

                    // Wait up to 2 seconds
                    var timeout = DateTime.Now.AddMilliseconds(2000);
                    while (DateTime.Now < timeout && totalRead < buffer.Length)
                    {
                        if (ct.IsCancellationRequested)
                            return false;

                        if (_port.BytesToRead > 0)
                        {
                            int read = _port.Read(buffer, totalRead, Math.Min(_port.BytesToRead, buffer.Length - totalRead));
                            totalRead += read;

                            // Check for DA ready signals
                            // V6 DA typically sends "SYNC" or 0xC0
                            if (totalRead >= 4)
                            {
                                // Check "SYNC" magic
                                if (buffer[0] == 'S' && buffer[1] == 'Y' && buffer[2] == 'N' && buffer[3] == 'C')
                                {
                                    _log("[MediaTek] Detected DA SYNC signal");
                                    State = MtkDeviceState.Da1Loaded;
                                    return true;
                                }

                                // Check reverse SYNC
                                uint sync = (uint)(buffer[0] << 24 | buffer[1] << 16 | buffer[2] << 8 | buffer[3]);
                                if (sync == 0x434E5953)  // "CNYS" (little endian SYNC)
                                {
                                    _log("[MediaTek] Detected DA SYNC signal (LE)");
                                    State = MtkDeviceState.Da1Loaded;
                                    return true;
                                }
                            }

                            // Check single-byte ready signal
                            if (buffer[0] == 0xC0)
                            {
                                _log("[MediaTek] Detected DA ready signal (0xC0)");
                                State = MtkDeviceState.Da1Loaded;
                                return true;
                            }
                        }
                        else
                        {
                            await Task.Delay(50, ct);
                        }
                    }

                    if (totalRead > 0)
                    {
                        _log($"[MediaTek] Received {totalRead} bytes: {BitConverter.ToString(buffer, 0, Math.Min(totalRead, 16))}");
                    }

                    return false;
                }
                finally
                {
                    _portLock.Release();
                }
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Detect DA ready exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to send DA SYNC command
        /// DA protocol uses 0xEFEEEEFE magic
        /// </summary>
        public async Task<bool> TrySendDaSyncAsync(CancellationToken ct = default)
        {
            try
            {
                await _portLock.WaitAsync(ct);
                try
                {
                    _log("[MediaTek] Sending DA SYNC command...");

                    // DA command format: EFEEEEFE + cmd(4B) + length(4B) + payload
                    // SYNC command: cmd=0x01, length=4, payload="SYNC"
                    byte[] syncCmd = new byte[]
                    {
                        0xEF, 0xEE, 0xEE, 0xFE,  // Magic
                        0x01, 0x00, 0x00, 0x00,  // CMD = 1 (SYNC)
                        0x04, 0x00, 0x00, 0x00   // Length = 4
                    };
                    byte[] syncPayload = System.Text.Encoding.ASCII.GetBytes("SYNC");

                    // Send command header
                    _port.Write(syncCmd, 0, syncCmd.Length);

                    // Send SYNC payload
                    _port.Write(syncPayload, 0, syncPayload.Length);

                    // Wait for response
                    await Task.Delay(200, ct);

                    // Read response
                    byte[] buffer = new byte[32];
                    int totalRead = 0;

                    var timeout = DateTime.Now.AddMilliseconds(2000);
                    while (DateTime.Now < timeout && totalRead < buffer.Length)
                    {
                        if (_port.BytesToRead > 0)
                        {
                            int read = _port.Read(buffer, totalRead, Math.Min(_port.BytesToRead, buffer.Length - totalRead));
                            totalRead += read;

                            // Check for DA response
                            if (totalRead >= 4)
                            {
                                // Check magic
                                if (buffer[0] == 0xEF && buffer[1] == 0xEE)
                                {
                                    _log($"[MediaTek] Received DA response: {BitConverter.ToString(buffer, 0, Math.Min(totalRead, 12))}");
                                    State = MtkDeviceState.Da1Loaded;
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            await Task.Delay(50, ct);
                        }
                    }

                    if (totalRead > 0)
                    {
                        _log($"[MediaTek] Received response but magic mismatch: {BitConverter.ToString(buffer, 0, totalRead)}");
                    }
                    else
                    {
                        _log("[MediaTek] DA SYNC no response");
                    }

                    return false;
                }
                finally
                {
                    _portLock.Release();
                }
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] DA SYNC exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Upload data (with thread safety protection)
        /// </summary>
        private async Task<bool> UploadDataAsync(byte[] data, ushort expectedChecksum, CancellationToken ct = default)
        {
            _log($"[MediaTek] Starting data upload: {data.Length} bytes, expected checksum: 0x{expectedChecksum:X4}");

            await _portLock.WaitAsync(ct);
            try
            {
                int bytesWritten = 0;
                // mtkclient uses 0x400 (1KB) as max packet size
                int maxPacketSize = 0x400;

                while (bytesWritten < data.Length)
                {
                    if (ct.IsCancellationRequested)
                    {
                        _log("[MediaTek] Data upload cancelled");
                        return false;
                    }

                    int chunkSize = Math.Min(maxPacketSize, data.Length - bytesWritten);
                    _port.Write(data, bytesWritten, chunkSize);
                    bytesWritten += chunkSize;

                    // mtkclient: flush every 0x2000 bytes
                    if (bytesWritten % 0x2000 == 0)
                    {
                        _port.Write(new byte[0], 0, 0);  // Flush
                    }

                    // Update progress (every 64KB to avoid frequency)
                    if (bytesWritten % 0x10000 == 0 || bytesWritten == data.Length)
                    {
                        double progress = (double)bytesWritten * 100 / data.Length;
                        _progressCallback?.Invoke(progress);
                    }
                }

                _log($"[MediaTek] Data transmission complete: {bytesWritten} bytes");

                // mtkclient: send empty bytes after finishing then wait
                // Note: Mtk reference implementation uses 10ms, mtkclient uses 120ms
                // Using 10ms for speed; increase if issues occur
                _port.Write(new byte[0], 0, 0);
                await Task.Delay(10, ct);

                // Read checksum (2 bytes, Big-Endian)
                var checksumResp = await ReadBytesInternalAsync(2, DEFAULT_TIMEOUT_MS * 2, ct);
                if (checksumResp == null || checksumResp.Length < 2)
                {
                    _log($"[MediaTek] Failed to read checksum (received: {checksumResp?.Length ?? 0} bytes)");
                    return false;
                }

                ushort receivedChecksum = (ushort)((checksumResp[0] << 8) | checksumResp[1]);
                _log($"[MediaTek] Received checksum: 0x{receivedChecksum:X4}, expected: 0x{expectedChecksum:X4}");

                if (receivedChecksum != expectedChecksum && receivedChecksum != 0)
                {
                    _log($"[MediaTek] Warning: Checksum mismatch");
                }

                // Read final status (2 bytes)
                var statusResp = await ReadBytesInternalAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null || statusResp.Length < 2)
                {
                    _log($"[MediaTek] Failed to read status (received: {statusResp?.Length ?? 0} bytes)");
                    return false;
                }

                ushort status = (ushort)((statusResp[0] << 8) | statusResp[1]);
                _log($"[MediaTek] Upload status: 0x{status:X4}");

                // Save upload status for subsequent use
                LastUploadStatus = status;

                // Use improved status check
                if (!BromErrorHelper.IsSuccess(status))
                {
                    _log($"[MediaTek] Upload status error: 0x{status:X4} ({BromErrorHelper.GetErrorMessage(status)})");
                    return false;
                }

                // Special handling: 0x7017/0x7015 indicates DAA security protection
                if (status == 0x7017 || status == 0x7015)
                {
                    _log($"[MediaTek] Data transmission complete (Status 0x{status:X4})");
                    _log("[MediaTek] ⚠ DAA Security protection triggered - device may re-enumerate");
                }
                else
                {
                    _log("[MediaTek] ✓ Data upload successful");
                }

                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Data upload exception: {ex.Message}");
                return false;
            }
            finally
            {
                _portLock.Release();
            }
        }

        #endregion

        #region EMI Configuration

        /// <summary>
        /// Send EMI Configuration (used for DRAM initialization in BROM mode)
        /// </summary>
        public async Task<bool> SendEmiConfigAsync(byte[] emiConfig, CancellationToken ct = default)
        {
            if (emiConfig == null || emiConfig.Length == 0)
            {
                _log("[MediaTek] EMI configuration data is empty");
                return false;
            }

            _log($"[MediaTek] Sending EMI configuration: {emiConfig.Length} bytes");

            try
            {
                // Send SEND_ENV_PREPARE command (0xD9)
                if (!await EchoAsync(BromCommands.CMD_SEND_ENV_PREPARE, ct))
                {
                    _log("[MediaTek] SEND_ENV_PREPARE command failed");
                    return false;
                }

                // Send EMI configuration length
                if (!await EchoAsync(MtkDataPacker.PackUInt32BE((uint)emiConfig.Length), ct))
                {
                    _log("[MediaTek] Failed to send EMI configuration length");
                    return false;
                }

                // Read status
                var statusResp = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (statusResp == null)
                {
                    _log("[MediaTek] Failed to read status");
                    return false;
                }

                ushort status = MtkDataPacker.UnpackUInt16BE(statusResp, 0);
                if (!BromErrorHelper.IsSuccess(status))
                {
                    _log($"[MediaTek] EMI configuration status error: 0x{status:X4} ({BromErrorHelper.GetErrorMessage(status)})");
                    return false;
                }

                // Send EMI configuration data
                await _portLock.WaitAsync(ct);
                try
                {
                    _port.Write(emiConfig, 0, emiConfig.Length);
                    await Task.Delay(50, ct);
                }
                finally
                {
                    _portLock.Release();
                }

                // Read final status
                var finalStatus = await ReadBytesAsync(2, DEFAULT_TIMEOUT_MS, ct);
                if (finalStatus == null)
                {
                    _log("[MediaTek] Failed to read final status");
                    return false;
                }

                ushort finalStatusCode = MtkDataPacker.UnpackUInt16BE(finalStatus, 0);
                if (!BromErrorHelper.IsSuccess(finalStatusCode))
                {
                    _log($"[MediaTek] EMI configuration final status error: 0x{finalStatusCode:X4}");
                    return false;
                }

                _log("[MediaTek] ✓ EMI configuration sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] Exception sending EMI configuration: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Send command and expect echo
        /// </summary>
        private async Task<bool> EchoAsync(byte cmd, CancellationToken ct = default)
        {
            return await EchoAsync(new byte[] { cmd }, ct);
        }

        /// <summary>
        /// Send data and expect echo (with thread safety)
        /// </summary>
        private async Task<bool> EchoAsync(byte[] data, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(data, 0, data.Length);

                var response = await ReadBytesInternalAsync(data.Length, DEFAULT_TIMEOUT_MS, ct);
                if (response == null)
                {
                    _logDetail("[MediaTek] Echo: Reading response timeout");
                    return false;
                }

                // Compare echo
                for (int i = 0; i < data.Length; i++)
                {
                    if (response[i] != data[i])
                    {
                        _logDetail($"[MediaTek] Echo mismatch: position {i}, expected 0x{data[i]:X2}, received 0x{response[i]:X2}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logDetail($"[MediaTek] Echo exception: {ex.Message}");
                return false;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Send command and expect echo (with diagnostic info)
        /// Note: Preloader mode may not echo some commands, returning status code directly
        /// </summary>
        private async Task<bool> EchoAsyncWithDiag(byte cmd, string cmdName, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(new byte[] { cmd }, 0, 1);

                var response = await ReadBytesInternalAsync(1, DEFAULT_TIMEOUT_MS, ct);
                if (response == null)
                {
                    _log($"[MediaTek] {cmdName} command failed: No response");
                    return false;
                }

                if (response[0] == cmd)
                {
                    // Normal echo
                    return true;
                }

                // Check if it's a status code response (device didn't echo command)
                if (response[0] == 0x00)
                {
                    // Read second byte to check if it's a status code
                    var extra = await ReadBytesInternalAsync(1, 100, ct);
                    if (extra != null && extra.Length > 0)
                    {
                        ushort status = (ushort)((response[0] << 8) | extra[0]);
                        _log($"[MediaTek] {cmdName} command: Device returned status 0x{status:X4} (no echo)");

                        if (status == (ushort)BromStatus.SlaRequired)
                            _log("[MediaTek] Device requires SLA authentication");
                        else if (status == (ushort)BromStatus.AuthRequired || status == (ushort)BromStatus.PreloaderAuth)
                            _log("[MediaTek] Preloader requires AUTH");
                    }
                    else
                    {
                        _log($"[MediaTek] {cmdName} command rejected (DAA might be required)");
                    }
                }
                else
                {
                    _log($"[MediaTek] {cmdName} echo mismatch: expected 0x{cmd:X2}, received 0x{response[0]:X2}");
                }
                return false;
            }
            catch (Exception ex)
            {
                _log($"[MediaTek] {cmdName} command exception: {ex.Message}");
                return false;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Send command and read response (with thread safety)
        /// </summary>
        private async Task<byte[]> SendCmdAsync(byte cmd, int responseLen, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(new byte[] { cmd }, 0, 1);
                return await ReadBytesInternalAsync(responseLen, DEFAULT_TIMEOUT_MS, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Read specified number of bytes (Public method, with thread safety)
        /// </summary>
        public async Task<byte[]> ReadBytesAsync(int count, int timeoutMs, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                return await ReadBytesInternalAsync(count, timeoutMs, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Read specified number of bytes (Internal method, no lock)
        /// </summary>
        private async Task<byte[]> ReadBytesInternalAsync(int count, int timeoutMs, CancellationToken ct = default)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            DateTime start = DateTime.Now;

            while (read < count)
            {
                if (ct.IsCancellationRequested)
                    return null;

                if ((DateTime.Now - start).TotalMilliseconds > timeoutMs)
                    return null;

                if (_port.BytesToRead > 0)
                {
                    int toRead = Math.Min(_port.BytesToRead, count - read);
                    int actualRead = _port.Read(buffer, read, toRead);
                    read += actualRead;
                }
                else
                {
                    await Task.Delay(10, ct);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Get port lock (for external use, e.g., XmlDaClient)
        /// </summary>
        public SemaphoreSlim GetPortLock() => _portLock;

        #endregion

        #region IBromClient Interface Implementation

        /// <summary>
        /// Send boot_to command to load code to specified address (for DA Extensions)
        /// </summary>
        public async Task SendBootTo(uint address, byte[] data)
        {
            if (_logger != null)
            {
                _logger.Info($"boot_to: Address=0x{address:X8}, size={data?.Length ?? 0}", LogCategory.Protocol);
            }
            else
            {
                _log($"[MediaTek] boot_to: 0x{address:X8} ({data?.Length ?? 0} bytes)");
            }

            // TODO: Actual boot_to command implementation
            // This needs to be executed in DA mode, not a BROM command
            throw new NotImplementedException("boot_to command needs to be implemented in DA mode");
        }

        /// <summary>
        /// Send DA command
        /// </summary>
        public async Task SendDaCommand(uint command, byte[] data = null)
        {
            if (_logger != null)
            {
                _logger.LogCommand($"DA Command", command, LogCategory.Da);
            }

            // TODO: Actual DA command sending logic
            throw new NotImplementedException("DA command sending needs to be implemented in DA mode");
        }

        /// <summary>
        /// Receive DA response
        /// </summary>
        public async Task<byte[]> ReceiveDaResponse(int length)
        {
            // TODO: Actual DA response receiving logic
            throw new NotImplementedException("DA response receiving needs to be implemented in DA mode");
        }

        #endregion

        #region Kamakiri2 Helper Methods (Public for exploit use)

        /// <summary>
        /// Public single-byte Echo (for Kamakiri2 exploit)
        /// </summary>
        public async Task<bool> EchoByteAsync(byte cmd, CancellationToken ct = default)
        {
            return await EchoAsync(cmd, ct);
        }

        /// <summary>
        /// Public multi-byte Echo (for Kamakiri2 exploit)
        /// </summary>
        public async Task<bool> EchoBytesAsync(byte[] data, CancellationToken ct = default)
        {
            return await EchoAsync(data, ct);
        }

        // Note: ReadBytesAsync is already defined in the class, no need to redefine

        /// <summary>
        /// Public byte write method (for Kamakiri2 exploit)
        /// </summary>
        public async Task WriteBytesAsync(byte[] data, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(data, 0, data.Length);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Clear serial port buffers (for Kamakiri2 exploit)
        /// </summary>
        public void DiscardBuffers()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BROM] DiscardBuffers exception: {ex.Message}"); }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _port?.Dispose();
                _portLock?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
