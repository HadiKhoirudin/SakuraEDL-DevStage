// ============================================================================
// LoveAlways - Spreadtrum FDL Flashing Client
// Spreadtrum/Unisoc FDL (Flash Download) Client - Pure C# Implementation
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Spreadtrum.Protocol
{
    /// <summary>
    /// FDL Flashing Client (Pure C# implementation, no external tool dependencies)
    /// </summary>
    public class FdlClient : IDisposable
    {
        private SerialPort _port;
        private readonly HdlcProtocol _hdlc;
        private FdlStage _stage = FdlStage.None;
        private SprdDeviceState _state = SprdDeviceState.Disconnected;
        private readonly SemaphoreSlim _portLock = new SemaphoreSlim(1, 1);  // Use SemaphoreSlim instead of lock
        private CancellationTokenSource _cts;
        private volatile bool _isDisposed = false;  // Flag whether it is disposed
        
        // Port info (for reconnection)
        private string _portName;
        private int _baudRate = 115200;

        // Buffers
        private readonly byte[] _readBuffer = new byte[65536];
        private int _readBufferLength = 0;

        // Configuration
        public int DefaultTimeout { get; set; } = 10000;    // Increased default timeout
        public int MaxOperationTimeout { get; set; } = 60000;  // Max operation timeout (prevent hang)
        public int DataChunkSize { get; set; } = 528;       // BROM mode chunk size (Reference: sprdproto)
        public const int BROM_CHUNK_SIZE = 528;             // BROM protocol: 528 bytes
        public const int FDL_CHUNK_SIZE = 2112;             // FDL protocol: 2112 bytes
        public int HandshakeRetries { get; set; } = 50;
        public int CommandRetries { get; set; } = 3;        // Command retry attempts
        public int RetryDelayMs { get; set; } = 500;        // Retry interval (ms)

        // Events
        public event Action<string> OnLog;
        public event Action<int, int> OnProgress;
        public event Action<SprdDeviceState> OnStateChanged;

        // Properties
        public bool IsConnected => _port != null && _port.IsOpen;
        public FdlStage CurrentStage => _stage;
        public SprdDeviceState State => _state;
        public string PortName => _port?.PortName;

        /// <summary>
        /// Get current serial port (for exploit use)
        /// </summary>
        public SerialPort GetPort() => _port;

        // Chip ID (0 means auto-detect, used to determine FDL loading address)
        public uint ChipId { get; private set; }

        public FdlClient()
        {
            _hdlc = new HdlcProtocol(msg => OnLog?.Invoke(msg));
        }

        // Custom FDL configuration
        public string CustomFdl1Path { get; private set; }
        public string CustomFdl2Path { get; private set; }
        public uint CustomFdl1Address { get; private set; }
        public uint CustomFdl2Address { get; private set; }
        
        // Custom execution address (used for signature verification bypass)
        public uint CustomExecAddress { get; private set; }
        public bool UseExecNoVerify { get; set; } = true;  // Enabled by default

        /// <summary>
        /// Set chip ID (affects FDL loading address and exec_addr)
        /// </summary>
        public void SetChipId(uint chipId)
        {
            ChipId = chipId;
            if (chipId > 0)
            {
                string platform = SprdPlatform.GetPlatformName(chipId);
                uint execAddr = SprdPlatform.GetExecAddress(chipId);
                
                // Set exec_addr automatically
                if (CustomExecAddress == 0 && execAddr > 0)
                {
                    CustomExecAddress = execAddr;
                }
                
                Log("[FDL] Chip config: {0}", platform);
                Log("[FDL]   FDL1: 0x{0:X8}, FDL2: 0x{1:X8}", 
                    SprdPlatform.GetFdl1Address(chipId), SprdPlatform.GetFdl2Address(chipId));
                
                if (execAddr > 0)
                {
                    Log("[FDL]   exec_addr: 0x{0:X8} (signature bypass required)", execAddr);
                }
                else
                {
                    Log("[FDL]   signature bypass not required");
                }
            }
            else
            {
                Log("[FDL] Chip set to auto-detect");
            }
        }

        /// <summary>
        /// Set custom FDL1
        /// </summary>
        public void SetCustomFdl1(string filePath, uint address)
        {
            CustomFdl1Path = filePath;
            CustomFdl1Address = address;
        }

        /// <summary>
        /// Set custom execution address (used for signature verification bypass)
        /// </summary>
        public void SetCustomExecAddress(uint execAddr)
        {
            CustomExecAddress = execAddr;
            if (execAddr > 0)
            {
                Log("[FDL] Set exec_addr: 0x{0:X8}", execAddr);
            }
        }
        
        // Custom exec_no_verify file path
        public string CustomExecNoVerifyPath { get; set; }
        
        /// <summary>
        /// Set exec_no_verify file path
        /// </summary>
        public void SetExecNoVerifyFile(string filePath)
        {
            CustomExecNoVerifyPath = filePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                Log("[FDL] Set exec_no_verify file: {0}", System.IO.Path.GetFileName(filePath));
            }
        }
        
        /// <summary>
        /// Find custom_exec_no_verify file
        /// Search order: specified path > FDL1 directory > application directory
        /// </summary>
        private byte[] LoadExecNoVerifyPayload(uint execAddr)
        {
            string execFileName = string.Format("custom_exec_no_verify_{0:x}.bin", execAddr);
            
            // 1. Use specified file
            if (!string.IsNullOrEmpty(CustomExecNoVerifyPath) && System.IO.File.Exists(CustomExecNoVerifyPath))
            {
                Log("[FDL] Using specified exec_no_verify: {0}", System.IO.Path.GetFileName(CustomExecNoVerifyPath));
                return System.IO.File.ReadAllBytes(CustomExecNoVerifyPath);
            }
            
            // 2. Search in FDL1 directory (spd_dump format)
            if (!string.IsNullOrEmpty(CustomFdl1Path))
            {
                string fdl1Dir = System.IO.Path.GetDirectoryName(CustomFdl1Path);
                string execPath = System.IO.Path.Combine(fdl1Dir, execFileName);
                
                if (System.IO.File.Exists(execPath))
                {
                    Log("[FDL] Found exec_no_verify: {0} (FDL directory)", execFileName);
                    return System.IO.File.ReadAllBytes(execPath);
                }
                
                // Also search for simplified name
                execPath = System.IO.Path.Combine(fdl1Dir, "exec_no_verify.bin");
                if (System.IO.File.Exists(execPath))
                {
                    Log("[FDL] Found exec_no_verify.bin (FDL directory)");
                    return System.IO.File.ReadAllBytes(execPath);
                }
            }
            
            // 3. Search in application directory
            string appDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string appExecPath = System.IO.Path.Combine(appDir, execFileName);
            
            if (System.IO.File.Exists(appExecPath))
            {
                Log("[FDL] Found exec_no_verify: {0} (App directory)", execFileName);
                return System.IO.File.ReadAllBytes(appExecPath);
            }
            
            // Also search for simplified name
            appExecPath = System.IO.Path.Combine(appDir, "exec_no_verify.bin");
            if (System.IO.File.Exists(appExecPath))
            {
                Log("[FDL] Found exec_no_verify.bin (App directory)");
                return System.IO.File.ReadAllBytes(appExecPath);
            }
            
            // 4. File not found
            Log("[FDL] exec_no_verify file not found, skipping signature bypass");
            Log("[FDL] Tips: Required {0}", execFileName);
            return null;
        }
        
        /// <summary>
        /// Send custom_exec_no_verify payload
        /// Reference spd_dump: Send after fdl1 sent, before EXEC
        /// </summary>
        private async Task<bool> SendExecNoVerifyPayloadAsync(uint execAddr)
        {
            if (execAddr == 0) return true;  // No need to send
            
            // Load payload
            byte[] payload = LoadExecNoVerifyPayload(execAddr);
            
            if (payload == null || payload.Length == 0)
            {
                // exec_no_verify file not found, skip
                return true;
            }
            
            Log("[FDL] Sending custom_exec_no_verify to 0x{0:X8} ({1} bytes)...", execAddr, payload.Length);
            
                                                                                               // Note: spd_dump sends the second FDL continuously without re-CONNECT
            // Ensure BROM mode (CRC16)
            _hdlc.SetBromMode();
            
            // Send START_DATA
            var startPayload = new byte[8];
            startPayload[0] = (byte)((execAddr >> 24) & 0xFF);
            startPayload[1] = (byte)((execAddr >> 16) & 0xFF);
            startPayload[2] = (byte)((execAddr >> 8) & 0xFF);
            startPayload[3] = (byte)(execAddr & 0xFF);
            startPayload[4] = (byte)((payload.Length >> 24) & 0xFF);
            startPayload[5] = (byte)((payload.Length >> 16) & 0xFF);
            startPayload[6] = (byte)((payload.Length >> 8) & 0xFF);
            startPayload[7] = (byte)(payload.Length & 0xFF);
            
            Log("[FDL] exec_no_verify START_DATA: addr=0x{0:X8}, size={1}", execAddr, payload.Length);
            var startFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_START_DATA, startPayload);
            await WriteFrameAsync(startFrame);
            
            if (!await WaitAckAsync(5000))
            {
                Log("[FDL] exec_no_verify START_DATA failed");
                return false;
            }
            Log("[FDL] exec_no_verify START_DATA OK");
            
            // Send data - exec_no_verify payload is usually small, send directly
            var midstFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_MIDST_DATA, payload);
            await WriteFrameAsync(midstFrame);
            
            if (!await WaitAckAsync(5000))
            {
                Log("[FDL] exec_no_verify MIDST_DATA failed");
                return false;
            }
            Log("[FDL] exec_no_verify MIDST_DATA OK");
            
            // Send END_DATA
            var endFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_END_DATA);
            await WriteFrameAsync(endFrame);
            
            // Read END_DATA response and log details
            var endResp = await ReadFrameAsyncSafe(5000);
            if (endResp != null && endResp.Length > 0)
            {
                Log("[FDL] exec_no_verify END_DATA response: {0}", BitConverter.ToString(endResp).Replace("-", " "));
                try
                {
                    var parsed = _hdlc.ParseFrame(endResp);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        Log("[FDL] exec_no_verify END_DATA OK");
                    }
                    else
                    {
                        string errorMsg = GetBslErrorMessage(parsed.Type);
                        Log("[FDL] exec_no_verify END_DATA error: 0x{0:X2} ({1})", parsed.Type, errorMsg);
                        Log("[FDL] Warning: END_DATA failed, but continuing attempt to EXEC...");
                    }
                }
                catch (Exception ex)
                {
                    Log("[FDL] exec_no_verify END_DATA parse failed: {0}", ex.Message);
                }
            }
            else
            {
                Log("[FDL] exec_no_verify END_DATA no response");
                Log("[FDL] Warning: END_DATA no response, but continuing attempt to EXEC...");
            }
            
            Log("[FDL] exec_no_verify payload sent successfully");
            return true;
        }

        /// <summary>
        /// Set custom FDL2
        /// </summary>
        public void SetCustomFdl2(string filePath, uint address)
        {
            CustomFdl2Path = filePath;
            CustomFdl2Address = address;
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
        }

        /// <summary>
        /// Get FDL1 loading address (prioritize custom address)
        /// </summary>
        public uint GetFdl1Address()
        {
            if (CustomFdl1Address > 0)
                return CustomFdl1Address;
            return SprdPlatform.GetFdl1Address(ChipId);
        }

        /// <summary>
        /// Get FDL2 loading address (prioritize custom address)
        /// </summary>
        public uint GetFdl2Address()
        {
            if (CustomFdl2Address > 0)
                return CustomFdl2Address;
            return SprdPlatform.GetFdl2Address(ChipId);
        }

        /// <summary>
        /// Get FDL1 file path (prioritize custom path)
        /// </summary>
        public string GetFdl1Path(string defaultPath)
        {
            if (!string.IsNullOrEmpty(CustomFdl1Path) && File.Exists(CustomFdl1Path))
                return CustomFdl1Path;
            return defaultPath;
        }

        /// <summary>
        /// Get FDL2 file path (prioritize custom path)
        /// </summary>
        public string GetFdl2Path(string defaultPath)
        {
            if (!string.IsNullOrEmpty(CustomFdl2Path) && File.Exists(CustomFdl2Path))
                return CustomFdl2Path;
            return defaultPath;
        }

        #region Connection Management

        /// <summary>
        /// Connect device
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, int baudRate = 115200)
        {
            try
            {
                Log("[FDL] Connecting port: {0}, Baudrate: {1}", portName, baudRate);

                // Save port info for reconnection
                _portName = portName;
                _baudRate = baudRate;

                _port = new SerialPort(portName)
                {
                    BaudRate = baudRate,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    ReadTimeout = DefaultTimeout,
                    WriteTimeout = DefaultTimeout,
                    ReadBufferSize = 65536,
                    WriteBufferSize = 65536
                };

                _port.Open();
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();

                // Handshake
                bool success = await HandshakeAsync();
                if (success)
                {
                    SetState(SprdDeviceState.Connected);
                    Log("[FDL] Connection successful");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log("[FDL] Connection failed: {0}", ex.Message);
                SetState(SprdDeviceState.Error);
                return false;
            }
        }
        
        /// <summary>
        /// Safely close port
        /// </summary>
        private void ClosePortSafe()
        {
            try
            {
                if (_port != null)
                {
                    if (_port.IsOpen)
                    {
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }
                        catch { }
                        _port.Close();
                    }
                    _port.Dispose();
                    _port = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _cts?.Cancel();
                
                if (_port != null && _port.IsOpen)
                {
                    _port.Close();
                }
                _port?.Dispose();
                _port = null;
                
                _stage = FdlStage.None;
                SetState(SprdDeviceState.Disconnected);
                
                Log("[FDL] Disconnected");
            }
            catch (Exception ex)
            {
                Log("[FDL] Disconnect exception: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handshake (Reference sprdproto implementation)
        /// </summary>
        private async Task<bool> HandshakeAsync()
        {
            Log("[FDL] Starting handshake...");

            // Ensure CRC16 mode (BROM phase)
            _hdlc.SetBromMode();

            // Method 1: Send single 0x7E (Reference sprdproto)
            // sprdproto only sends one 0x7E then waits for BSL_REP_VER
            await WriteFrameAsyncSafe(new byte[] { 0x7E }, 1000);
            await Task.Delay(100);
            
            // Check for response data
            var response = await ReadFrameAsync(2000);
            if (response != null)
            {
                Log("[FDL] Received raw data ({0} bytes): {1}", response.Length, BitConverter.ToString(response).Replace("-", " "));
                
                try
                {
                    var frame = _hdlc.ParseFrame(response);
                    
                    if (frame.Type == (byte)BslCommand.BSL_REP_VER)
                    {
                        // BROM returns version info, extract version string
                        string version = frame.Payload != null 
                            ? System.Text.Encoding.ASCII.GetString(frame.Payload).TrimEnd('\0')
                            : "Unknown";
                        Log("[FDL] BROM version: {0}", version);
                        _isBromMode = true;
                        _bromVersion = version;
                        SetState(SprdDeviceState.Connected);
                        return true;
                    }
                    else if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        Log("[FDL] Handshake successful (FDL mode)");
                        _isBromMode = false;
                        return true;
                    }
                    else
                    {
                        Log("[FDL] Unknown response type: 0x{0:X2}", frame.Type);
                    }
                }
                catch (Exception ex)
                {
                    Log("[FDL] Parse response failed: {0}", ex.Message);
                }
            }
            
            // Method 2: If single 0x7E no response, try sending multiple
            Log("[FDL] Trying multi-byte sync...");
            try { _port?.DiscardInBuffer(); } 
            catch (Exception ex) { LogDebug("[FDL] Discard buffer exception: {0}", ex.Message); }
            
            for (int i = 0; i < 3; i++)
            {
                await WriteFrameAsyncSafe(new byte[] { 0x7E }, 500);
                await Task.Delay(50);
            }
            await Task.Delay(100);
            
            response = await ReadFrameAsync(2000);
            if (response != null)
            {
                Log("[FDL] Received response ({0} bytes): {1}", response.Length, BitConverter.ToString(response).Replace("-", " "));
                
                try
                {
                    var frame = _hdlc.ParseFrame(response);
                    if (frame.Type == (byte)BslCommand.BSL_REP_VER)
                    {
                        string version = frame.Payload != null 
                            ? System.Text.Encoding.ASCII.GetString(frame.Payload).TrimEnd('\0')
                            : "Unknown";
                        Log("[FDL] BROM version: {0}", version);
                        _isBromMode = true;
                        _bromVersion = version;
                        SetState(SprdDeviceState.Connected);
                        return true;
                    }
                    else if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        Log("[FDL] Handshake successful (FDL mode)");
                        _isBromMode = false;
                        return true;
                    }
                }
                catch (Exception ex) 
                { 
                    LogDebug("[FDL] Parse multi-byte response exception: {0}", ex.Message); 
                }
            }
            
            // Method 3: Try to send CONNECT command
            Log("[FDL] Trying to send CONNECT command...");
            _port.DiscardInBuffer();

            var connectFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_CONNECT);
            Log("[FDL] CONNECT frame: {0}", BitConverter.ToString(connectFrame).Replace("-", " "));
            await WriteFrameAsync(connectFrame);

            response = await ReadFrameAsync(3000);
            if (response != null)
            {
                Log("[FDL] CONNECT response ({0} bytes): {1}", response.Length, BitConverter.ToString(response).Replace("-", " "));
                
                try
                {
                    var frame = _hdlc.ParseFrame(response);
                    if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        // Important: If initial connection (FDL not downloaded), ACK should also be treated as BROM mode
                        // because BROM might return ACK for CONNECT
                        if (_stage == FdlStage.None)
                        {
                            Log("[FDL] CONNECT ACK (Initial connection, assuming BROM mode)");
                            _isBromMode = true;
                            SetState(SprdDeviceState.Connected);
                        }
                        else
                        {
                            Log("[FDL] CONNECT ACK (FDL mode)");
                        _isBromMode = false;
                        }
                        return true;
                    }
                    else if (frame.Type == (byte)BslCommand.BSL_REP_VER)
                    {
                        string version = frame.Payload != null 
                            ? System.Text.Encoding.ASCII.GetString(frame.Payload).TrimEnd('\0')
                            : "Unknown";
                        Log("[FDL] BROM version: {0}", version);
                        _isBromMode = true;
                        _bromVersion = version;
                        SetState(SprdDeviceState.Connected);
                        return true;
                    }
                    Log("[FDL] CONNECT response type: 0x{0:X2}", frame.Type);
                }
                catch (Exception ex)
                {
                    Log("[FDL] Parse CONNECT response failed: {0}", ex.Message);
                    // Assume connection success if there is any response
                    Log("[FDL] Assuming BROM mode");
                    _isBromMode = true;
                    SetState(SprdDeviceState.Connected);
                    return true;
                }
            }

            Log("[FDL] Handshake failed - no response");
            return false;
        }
        
        private string _bromVersion = "";
        
        /// <summary>
        /// Whether it's in BROM mode (requires FDL download)
        /// </summary>
        public bool IsBromMode => _isBromMode;
        private bool _isBromMode = true;

        #endregion

        #region FDL Download

        /// <summary>
        /// Download FDL
        /// </summary>
        public async Task<bool> DownloadFdlAsync(byte[] fdlData, uint baseAddr, FdlStage stage)
        {
            if (!IsConnected)
            {
                Log("[FDL] Device not connected");
                return false;
            }

            Log("[FDL] Downloading {0}, Address: 0x{1:X8}, Size: {2} bytes", stage, baseAddr, fdlData.Length);

            try
            {
                // Set correct chunk size based on stage
                if (stage == FdlStage.FDL1)
                {
                    DataChunkSize = BROM_CHUNK_SIZE;
                    _hdlc.SetBromMode();
                    Log("[FDL] BROM mode: CRC16, ChunkSize={0}", DataChunkSize);
                }

                // 0. BROM mode needs to send CONNECT command first to establish communication
                if (_isBromMode || stage == FdlStage.FDL1)
                {
                    Log("[FDL] Sending CONNECT command...");
                    _port.DiscardInBuffer();

                    var connectFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_CONNECT);
                    await WriteFrameAsync(connectFrame);

                    var connectResp = await ReadFrameAsync(3000);
                    if (connectResp != null)
                    {
                        try
                        {
                            var frame = _hdlc.ParseFrame(connectResp);
                            if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                            {
                                Log("[FDL] CONNECT ACK received");
                            }
                            else if (frame.Type == (byte)BslCommand.BSL_REP_VER)
                            {
                                Log("[FDL] BROM returned version response, continuing...");
                                // Send CONNECT again
                                await WriteFrameAsync(connectFrame);
                                if (!await WaitAckAsync(3000))
                                {
                                    Log("[FDL] Second CONNECT no ACK, trying to continue...");
                                }
                            }
                            else
                            {
                                Log("[FDL] CONNECT response: 0x{0:X2}", frame.Type);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("[FDL] Parse CONNECT response failed: {0}", ex.Message);
                        }
                    }
                    else
                    {
                        Log("[FDL] CONNECT no response, trying to continue...");
                    }
                }

                // 1. START_DATA - Send address and size (Big-Endian format, matching BROM protocol)
                var startPayload = new byte[8];
                // Write address and size using Big-Endian format
                WriteBigEndian32(startPayload, 0, baseAddr);
                WriteBigEndian32(startPayload, 4, (uint)fdlData.Length);

                Log("[FDL] Send START_DATA: Address=0x{0:X8}, Size={1}", baseAddr, fdlData.Length);
                Log("[FDL] START_DATA payload (BE): {0}", BitConverter.ToString(startPayload).Replace("-", " "));
                var startFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_START_DATA, startPayload);
                Log("[FDL] START_DATA frame: {0}", BitConverter.ToString(startFrame).Replace("-", " "));
                await WriteFrameAsync(startFrame);

                if (!await WaitAckWithDetailAsync(5000, "START_DATA"))
                {
                    // Retry once
                    Log("[FDL] START_DATA no response, retrying...");
                    _port.DiscardInBuffer();
                    await Task.Delay(100);
                    await WriteFrameAsync(startFrame);

                    if (!await WaitAckWithDetailAsync(5000, "START_DATA (Retry)"))
                    {
                        Log("[FDL] START_DATA failed");
                        Log("[FDL] Tips: 0x8B check error usually indicates FDL file mismatch with chip or incorrect address");
                        return false;
                    }
                }
                Log("[FDL] START_DATA OK");

                // 2. MIDST_DATA - Send data in chunks
                int totalChunks = (fdlData.Length + DataChunkSize - 1) / DataChunkSize;
                Log("[FDL] Starting data transfer: {0} chunks, {1} bytes per chunk", totalChunks, DataChunkSize);

                for (int i = 0; i < totalChunks; i++)
                {
                    int offset = i * DataChunkSize;
                    int length = Math.Min(DataChunkSize, fdlData.Length - offset);

                    var chunk = new byte[length];
                    Array.Copy(fdlData, offset, chunk, 0, length);

                    var midstFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_MIDST_DATA, chunk);
                    await WriteFrameAsync(midstFrame);

                    // Wait for ACK, with retry
                    bool ackReceived = false;
                    for (int retry = 0; retry < 3 && !ackReceived; retry++)
                    {
                        if (retry > 0)
                        {
                            Log("[FDL] Retrying chunk {0}...", i + 1);
                            await Task.Delay(100);
                            await WriteFrameAsync(midstFrame);
                        }
                        ackReceived = await WaitAckAsync(10000);  // 10s timeout
                    }

                    if (!ackReceived)
                    {
                        Log("[FDL] MIDST_DATA Chunk {0}/{1} failed", i + 1, totalChunks);
                        return false;
                    }

                    OnProgress?.Invoke(i + 1, totalChunks);

                    // Output progress every 10 chunks
                    if ((i + 1) % 10 == 0 || i + 1 == totalChunks)
                    {
                        Log("[FDL] Progress: {0}/{1} chunks ({2}%)", i + 1, totalChunks, (i + 1) * 100 / totalChunks);
                    }
                }
                Log("[FDL] MIDST_DATA OK ({0} chunks)", totalChunks);

                // 3. END_DATA
                var endFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_END_DATA);
                await WriteFrameAsync(endFrame);

                var endResp = await ReadFrameAsync(10000);
                if (endResp != null)
                {
                    try
                    {
                        var endParsed = _hdlc.ParseFrame(endResp);
                        if (endParsed.Type == (byte)BslCommand.BSL_REP_ACK)
                        {
                            Log("[FDL] END_DATA OK");
                        }
                        else
                        {
                            string errorMsg = GetBslErrorMessage(endParsed.Type);
                            Log("[FDL] END_DATA error: 0x{0:X2} ({1})", endParsed.Type, errorMsg);
                            Log("[FDL] Tips: Possible FDL file mismatch with chip, please use device-specific FDL");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("[FDL] END_DATA parse exception: {0}", ex.Message);
                        return false;
                    }
                }
                else
                {
                    Log("[FDL] END_DATA no response");
                    return false;
                }

                // 3.5. Send custom_exec_no_verify payload (FDL1 only, reference spd_dump)
                if (stage == FdlStage.FDL1 && UseExecNoVerify && CustomExecAddress > 0)
                {
                    Log("[FDL] Sending signature verification bypass payload...");
                    if (!await SendExecNoVerifyPayloadAsync(CustomExecAddress))
                    {
                        Log("[FDL] Warning: exec_no_verify send failed, continuing attempt to execute...");
                        // Do not return failure directly, try to continue
                    }
                }

                // 4. EXEC_DATA - Execute FDL (Reference spd_dump.c)
                Log("[FDL] Sending EXEC_DATA...");
                var execFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_EXEC_DATA);
                await WriteFrameAsync(execFrame);

                // Wait for EXEC_DATA response
                var execResp = await ReadFrameAsyncSafe(5000);
                if (execResp != null)
                {
                    try
                    {
                        var execParsed = _hdlc.ParseFrame(execResp);
                        if (execParsed.Type == (byte)BslCommand.BSL_REP_ACK)
                        {
                            Log("[FDL] EXEC_DATA ACK received");
                        }
                        else if (execParsed.Type == (byte)BslCommand.BSL_REP_INCOMPATIBLE_PARTITION)
                        {
                            // FDL2 execution successful, but returned incompatible partition (Reference spd_dump.c L1282-1283)
                            Log("[FDL] FDL2: Incompatible partition warning (Normal)");
                            if (stage == FdlStage.FDL2)
                            {
                                // Disable transcode (Required step for FDL2)
                                await DisableTranscodeAsync();

                                _stage = stage;
                                SetState(SprdDeviceState.Fdl2Loaded);
                                Log("[FDL] FDL2 downloaded and executed successfully");
                                return true;
                            }
                        }
                        else
                        {
                            Log("[FDL] EXEC_DATA response: 0x{0:X2}", execParsed.Type);
                        }
                    }
                    catch { }
                }
                else
                {
                    Log("[FDL] EXEC_DATA no response");
                }

                // Switch to FDL mode after FDL1 execution (Reference SPRDClientCore)
                if (stage == FdlStage.FDL1)
                {
                    // Need to wait for device initialization after FDL1 execution
                    // Reference spd_dump: CHECK_BAUD fails first time, this is normal
                    Log("[FDL] Waiting for FDL1 initialization...");

                    string portName = _port?.PortName ?? _portName;

                    // Check port status
                    bool portValid = _port != null && _port.IsOpen;
                    Log("[FDL] Current port status: {0}, Port name: {1}", portValid ? "Open" : "Closed/Invalid", portName);

                    // Wait for device stability (Device might reset USB after EXEC)
                    await Task.Delay(1000);

                    // If port is closed, try to reopen
                    if (_port != null && !_port.IsOpen)
                    {
                        Log("[FDL] Port is closed, trying to reopen: {0}", portName);
                        try
                        {
                            _port.Open();
                            Log("[FDL] Port reopened successfully");
                            await Task.Delay(200);
                        }
                        catch (Exception ex)
                        {
                            Log("[FDL] Port reopen failed: {0}", ex.Message);
                            // Try to create new port
                            try
                            {
                                _port = new SerialPort(portName, _baudRate);
                                _port.ReadTimeout = 3000;
                                _port.WriteTimeout = 3000;
                                _port.Open();
                                Log("[FDL] Create new port successful");
                            }
                            catch (Exception ex2)
                            {
                                Log("[FDL] Create new port failed: {0}", ex2.Message);
                            }
                        }
                    }

                    // Discard buffers
                    if (_port != null && _port.IsOpen)
                    {
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                            Log("[FDL] Port buffers discarded");
                        }
                        catch (Exception ex)
                        {
                            Log("[FDL] Discard buffer failed: {0}", ex.Message);
                        }
                    }

                    // Switch to FDL mode: Use checksum (Disable CRC16)
                    _hdlc.SetFdlMode();
                    DataChunkSize = FDL_CHUNK_SIZE;
                    _isBromMode = false;
                    Log("[FDL] Switch to FDL mode: Checksum, ChunkSize={0}", DataChunkSize);

                    // Reference spd_dump: Send CHECK_BAUD and retry
                    // CHECK_BAUD fails first time is normal (device still initializing)
                    Log("[FDL] Sending CHECK_BAUD waiting for FDL1 response...");
                    byte[] checkBaud = new byte[] { 0x7E, 0x7E, 0x7E, 0x7E };
                    byte[] lastSentPacket = checkBaud;
                    bool reconnected = false;

                    for (int i = 0; i < 20; i++)
                    {
                        try
                        {
                            if (i > 0)
                            {
                                Log("[FDL] Retry {0}/20...", i + 1);
                            }

                            // Try to reconnect port on 3rd attempt
                            if (i == 3 && !reconnected && !string.IsNullOrEmpty(portName))
                            {
                                Log("[FDL] Trying to reconnect port: {0}", portName);
                                try
                                {
                                    ClosePortSafe();
                                    await Task.Delay(500);

                                    _port = new SerialPort(portName, _baudRate);
                                    _port.ReadTimeout = 3000;
                                    _port.WriteTimeout = 3000;
                                    _port.Open();
                                    _port.DiscardInBuffer();
                                    _port.DiscardOutBuffer();

                                    Log("[FDL] Port reconnect successful");
                                    reconnected = true;
                                }
                                catch (Exception ex)
                                {
                                    Log("[FDL] Port reconnect failed: {0}", ex.Message);
                                }
                            }

                            // Try to switch baud rate on 8th attempt
                            if (i == 8 && _port != null && _port.IsOpen)
                            {
                                Log("[FDL] Trying to switch baud rate to 921600...");
                                try
                                {
                                    _port.Close();
                                    _port.BaudRate = 921600;
                                    _port.Open();
                                    _port.DiscardInBuffer();
                                }
                                catch { }
                            }

                            // Try to switch back and use CRC16 on 13th attempt
                            if (i == 13 && _port != null && _port.IsOpen)
                            {
                                Log("[FDL] Trying to switch back to 115200 and use CRC16...");
                                try
                                {
                                    _port.Close();
                                    _port.BaudRate = 115200;
                                    _port.Open();
                                    _port.DiscardInBuffer();
                                    _hdlc.SetBromMode();  // Switch back to CRC16
                                }
                                catch { }
                            }

                            // Send data (Reference SPRDClientCore's resend mechanism)
                            if (!SafeWriteToPort(lastSentPacket))
                            {
                                Log("[FDL] Serial port write failed");
                                await Task.Delay(300);
                                continue;
                            }

                            // Read response (Using BytesToRead polling method)
                            byte[] response = await SafeReadFromPortAsync(2000);

                            if (response != null && response.Length > 0)
                            {
                                Log("[FDL] Received response ({0} bytes): {1}", response.Length,
                                    BitConverter.ToString(response).Replace("-", " "));

                                try
                                {
                                    var parsed = _hdlc.ParseFrame(response);

                                    if (parsed.Type == (byte)BslCommand.BSL_REP_VER)
                                    {
                                        string version = parsed.Payload != null
                                            ? System.Text.Encoding.ASCII.GetString(parsed.Payload).TrimEnd('\0')
                                            : "Unknown";
                                        Log("[FDL] FDL1 version: {0}", version);

                                        // Send CONNECT command
                                        var connectFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_CONNECT);
                                        lastSentPacket = connectFrame;
                                        SafeWriteToPort(connectFrame);

                                        response = await SafeReadFromPortAsync(2000);
                                        if (response != null && response.Length > 0)
                                        {
                                            try
                                            {
                                                parsed = _hdlc.ParseFrame(response);
                                                if (parsed.Type == (byte)BslCommand.BSL_REP_ACK)
                                                {
                                                    Log("[FDL] CONNECT ACK received");
                                                }
                                            }
                                            catch { }
                                        }

                                        _stage = stage;
                                        SetState(SprdDeviceState.Fdl1Loaded);
                                        Log("[FDL] FDL1 downloaded and executed successfully");
                                        return true;
                                    }
                                    else if (parsed.Type == (byte)BslCommand.BSL_REP_ACK)
                                    {
                                        Log("[FDL] ACK received, FDL1 loaded");
                                        _stage = stage;
                                        SetState(SprdDeviceState.Fdl1Loaded);
                                        return true;
                                    }
                                    else if (parsed.Type == (byte)BslCommand.BSL_REP_VERIFY_ERROR)
                                    {
                                        // Verification failed, may need to switch checksum mode
                                        Log("[FDL] Verification error, trying to switch checksum mode");
                                        _hdlc.ToggleChecksumMode();
                                        continue;
                                    }
                                }
                                catch (Exception parseEx)
                                {
                                    Log("[FDL] Parse response failed: {0}", parseEx.Message);
                                }
                            }

                            // No response, resend after delay
                            await Task.Delay(150);
                        }
                        catch (Exception ex)
                        {
                            Log("[FDL] Exception: {0}", ex.Message);
                            await Task.Delay(200);
                        }
                    }

                    Log("[FDL] FDL1 execution verification failed (20 attempts)");
                    Log("[FDL] Tips: FDL1 file may be incompatible with chip, or incorrect FDL1 address");
                    return false;
                }
                else
                {
                    // Verification after FDL2 execution
                    await Task.Delay(500);

                    // FDL2 might return response directly
                    var fdl2Resp = await ReadFrameAsyncSafe(2000);
                    if (fdl2Resp != null)
                    {
                        try
                        {
                            var parsed = _hdlc.ParseFrame(fdl2Resp);
                            if (parsed.Type == (byte)BslCommand.BSL_REP_ACK ||
                                parsed.Type == (byte)BslCommand.BSL_REP_INCOMPATIBLE_PARTITION)
                            {
                                if (parsed.Type == (byte)BslCommand.BSL_REP_INCOMPATIBLE_PARTITION)
                                {
                                    Log("[FDL] FDL2: Incompatible partition warning (Normal)");
                                }

                                // Send DISABLE_TRANSCODE (Reference spd_dump.c and SPRDClientCore)
                                // This is required for FDL2, otherwise subsequent commands may fail
                                await DisableTranscodeAsync();

                                _stage = stage;
                                SetState(SprdDeviceState.Fdl2Loaded);
                                Log("[FDL] FDL2 downloaded and executed successfully");
                                return true;
                            }
                        }
                        catch { }
                    }

                    Log("[FDL] FDL2 OperationVerify fail");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("[FDL] Download abnormal: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Download FDL from file
        /// </summary>
        public async Task<bool> DownloadFdlFromFileAsync(string filePath, uint baseAddr, FdlStage stage)
        {
            if (!File.Exists(filePath))
            {
                Log("[FDL] File not found: {0}", filePath);
                return false;
            }

            byte[] data = File.ReadAllBytes(filePath);
            return await DownloadFdlAsync(data, baseAddr, stage);
        }

        #endregion

        #region Partition Operations

        /// <summary>
        /// Write partition (with retry mechanism, reference SPRDClientCore)
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, byte[] data, CancellationToken cancellationToken = default)
        {
            if (_stage != FdlStage.FDL2)
            {
                Log("[FDL] FDL2 must be loaded first");
                return false;
            }

            Log("[FDL] Writing partition: {0}, Size: {1}", partitionName, FormatSize((uint)data.Length));

            try
            {
                // Determine if 64-bit mode is needed
                ulong size = (ulong)data.Length;
                bool useMode64 = (size >> 32) != 0;
                
                // Build START_DATA payload (Reference SPRDClientCore)
                // Format: [PartitionName 72-byte Unicode] + [Size 4-byte LE] + [SizeHigh32 4-byte LE, 64-bit only]
                int payloadSize = useMode64 ? 80 : 76;
                var startPayload = new byte[payloadSize];
                
                // Partition name: Unicode encoding
                var nameBytes = Encoding.Unicode.GetBytes(partitionName);
                Array.Copy(nameBytes, 0, startPayload, 0, Math.Min(nameBytes.Length, 72));
                
                // Size: Little Endian
                BitConverter.GetBytes((uint)(size & 0xFFFFFFFF)).CopyTo(startPayload, 72);
                if (useMode64)
                {
                    BitConverter.GetBytes((uint)(size >> 32)).CopyTo(startPayload, 76);
                }

                // START_DATA with retry
                if (!await SendCommandWithRetryAsync((byte)BslCommand.BSL_CMD_START_DATA, startPayload))
                {
                    Log("[FDL] Partition {0} START failed", partitionName);
                    return false;
                }

                // Write in chunks
                int totalChunks = (data.Length + DataChunkSize - 1) / DataChunkSize;
                int failedChunks = 0;
                const int maxConsecutiveFailures = 3;

                for (int i = 0; i < totalChunks; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Log("[FDL] Write cancelled");
                        return false;
                    }

                    int offset = i * DataChunkSize;
                    int length = Math.Min(DataChunkSize, data.Length - offset);

                    var chunk = new byte[length];
                    Array.Copy(data, offset, chunk, 0, length);

                    // Data block with retry
                    if (!await SendDataWithRetryAsync((byte)BslCommand.BSL_CMD_MIDST_DATA, chunk))
                    {
                        failedChunks++;
                        Log("[FDL] Partition {0} Chunk {1}/{2} write failed (Accumulated failures: {3})", 
                            partitionName, i + 1, totalChunks, failedChunks);
                        
                        if (failedChunks >= maxConsecutiveFailures)
                        {
                            Log("[FDL] Too many consecutive failures, terminating write");
                            return false;
                        }
                        
                        // Try skipping this chunk and continue (Supported by some devices)
                        continue;
                    }
                    else
                    {
                        failedChunks = 0;  // Reset consecutive failures count
                    }

                    OnProgress?.Invoke(i + 1, totalChunks);
                }

                // END_DATA with retry
                if (!await SendCommandWithRetryAsync((byte)BslCommand.BSL_CMD_END_DATA, null))
                {
                    Log("[FDL] Partition {0} END failed", partitionName);
                    return false;
                }

                Log("[FDL] Partition {0} write successful", partitionName);
                return true;
            }
            catch (Exception ex)
            {
                Log("[FDL] Write partition exception: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Read partition (Reference spd_dump and SPRDClientCore)
        /// </summary>
        public async Task<byte[]> ReadPartitionAsync(string partitionName, uint size, CancellationToken cancellationToken = default)
        {
            if (_stage != FdlStage.FDL2)
            {
                Log("[FDL] FDL2 must be loaded first");
                return null;
            }

            Log("[FDL] Reading partition: {0}, Size: {1}", partitionName, FormatSize(size));

            try
            {
                // Determine if 64-bit mode is needed
                bool useMode64 = (size >> 32) != 0;
                
                // Build READ_START payload (Reference SPRDClientCore)
                // Format: [PartitionName 72-byte Unicode] + [Size 4-byte LE] + [SizeHigh32 4-byte LE, 64-bit only]
                int payloadSize = useMode64 ? 80 : 76;
                var payload = new byte[payloadSize];
                
                // Partition name: Unicode encoding, max 36 characters (72 bytes)
                var nameBytes = Encoding.Unicode.GetBytes(partitionName);
                Array.Copy(nameBytes, 0, payload, 0, Math.Min(nameBytes.Length, 72));
                
                // Size: Little Endian
                BitConverter.GetBytes((uint)(size & 0xFFFFFFFF)).CopyTo(payload, 72);
                if (useMode64)
                {
                    BitConverter.GetBytes((uint)(size >> 32)).CopyTo(payload, 76);
                }

                Log("[FDL] READ_START payload: Partition={0}, Size=0x{1:X}, Mode={2}", 
                    partitionName, size, useMode64 ? "64-bit" : "32-bit");

                // Start reading (with retry)
                if (!await SendCommandWithRetryAsync((byte)BslCommand.BSL_CMD_READ_START, payload))
                {
                    Log("[FDL] Read {0} start failed", partitionName);
                    return null;
                }

                // Receive data
                using (var ms = new MemoryStream())
                {
                    ulong offset = 0;
                    int consecutiveErrors = 0;
                    const int maxConsecutiveErrors = 5;
                    uint readChunkSize = (uint)DataChunkSize;  // Chunk size for each read

                    while (offset < size)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Log("[FDL] Read cancelled");
                            break;
                        }

                        // Calculate current read size
                        uint nowReadSize = (uint)Math.Min(readChunkSize, size - offset);
                        
                        // Build READ_MIDST payload (Reference spd_dump)
                        // Format: [ReadSize 4-byte LE] + [Offset 4-byte LE] + [OffsetHigh32 4-byte LE, 64-bit only]
                        int midstPayloadSize = useMode64 ? 12 : 8;
                        var midstPayload = new byte[midstPayloadSize];
                        BitConverter.GetBytes(nowReadSize).CopyTo(midstPayload, 0);
                        BitConverter.GetBytes((uint)(offset & 0xFFFFFFFF)).CopyTo(midstPayload, 4);
                        if (useMode64)
                        {
                            BitConverter.GetBytes((uint)(offset >> 32)).CopyTo(midstPayload, 8);
                        }

                        // Data block read with retry
                        byte[] chunkData = null;
                        for (int retry = 0; retry <= CommandRetries; retry++)
                        {
                            if (retry > 0)
                            {
                                Log("[FDL] Retry reading offset=0x{0:X} ({1}/{2})", offset, retry, CommandRetries);
                                await Task.Delay(RetryDelayMs / 2);
                                try { _port?.DiscardInBuffer(); } catch { }
                            }

                            var midstFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_MIDST, midstPayload);
                            if (!await WriteFrameAsyncSafe(midstFrame))
                            {
                                continue;  // Write failed, retry
                            }

                            var response = await ReadFrameAsyncSafe(15000);  // Reading data might take a long time
                            if (response != null)
                            {
                                HdlcFrame frame;
                                HdlcParseError parseError;
                                if (_hdlc.TryParseFrame(response, out frame, out parseError))
                                {
                                    // Response type: BSL_REP_READ_FLASH (0xBD)
                                    if (frame.Type == (byte)BslCommand.BSL_REP_READ_FLASH && frame.Payload != null)
                                    {
                                        chunkData = frame.Payload;
                                        break;  // Success
                                    }
                                    else if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                                    {
                                        // Some FDLs return ACK to indicate end of read
                                        Log("[FDL] ACK received, possibly end of read");
                                        break;
                                    }
                                    else
                                    {
                                        Log("[FDL] Unexpected response: 0x{0:X2}", frame.Type);
                                    }
                                }
                                else if (parseError == HdlcParseError.CrcMismatch)
                                {
                                    Log("[FDL] CRC error, retrying...");
                                    continue;
                                }
                            }
                        }

                        if (chunkData == null)
                        {
                            consecutiveErrors++;
                            Log("[FDL] Read offset=0x{0:X} failed (Consecutive errors: {1})", offset, consecutiveErrors);
                            
                            if (consecutiveErrors >= maxConsecutiveErrors)
                            {
                                Log("[FDL] Too many consecutive errors, terminating read");
                                break;
                            }
                            // Try continuing to the next block
                            offset += nowReadSize;
                            continue;
                        }
                        
                        consecutiveErrors = 0;  // Reset error count
                        ms.Write(chunkData, 0, chunkData.Length);
                        offset += (uint)chunkData.Length;

                        // Progress callback
                        OnProgress?.Invoke((int)offset, (int)size);
                        
                        // Output log every 10%
                        int progressPercent = (int)(offset * 100 / size);
                        if (progressPercent % 10 == 0 && progressPercent > 0)
                        {
                            Log("[FDL] Read progress: {0}% ({1}/{2})", progressPercent, FormatSize((uint)offset), FormatSize(size));
                        }
                    }

                    // End reading
                    var endFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_END);
                    await WriteFrameAsyncSafe(endFrame);
                    await WaitAckAsyncSafe(3000);

                    Log("[FDL] Partition {0} read complete, Actual size: {1}", partitionName, FormatSize((uint)ms.Length));
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log("[FDL] Read partition exception: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Erase partition (Reference SPRDClientCore)
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (_stage != FdlStage.FDL2)
            {
                Log("[FDL] FDL2 must be loaded first");
                return false;
            }

            Log("[FDL] Erasing partition: {0}", partitionName);

            // Erase command payload: [PartitionName 72-byte Unicode]
            var payload = new byte[72];
            var nameBytes = Encoding.Unicode.GetBytes(partitionName);
            Array.Copy(nameBytes, 0, payload, 0, Math.Min(nameBytes.Length, 72));

            if (!await SendCommandWithRetryAsync((byte)BslCommand.BSL_CMD_ERASE_FLASH, payload, 60000))  // Erasing might take a long time
            {
                Log("[FDL] Partition {0} erase failed", partitionName);
                return false;
            }

                Log("[FDL] Partition {0} erase successful", partitionName);
                return true;
            }

        #endregion

        #region FDL2 Initialization

        /// <summary>
        /// Disable transcode (Required step for FDL2)
        /// Reference: spd_dump.c disable_transcode command, SPRDClientCore
        /// Transcode adds 0x7D escape byte before 0x7D and 0x7E bytes.
        /// Transcode must be disabled after FDL2 execution, otherwise subsequent commands might fail.
        /// </summary>
        public async Task<bool> DisableTranscodeAsync()
        {
            Log("[FDL] Disabling transcode...");
            
            try
            {
                var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_DISABLE_TRANSCODE);
                await WriteFrameAsync(frame);
                
                var response = await ReadFrameAsyncSafe(3000);
                if (response != null)
                {
                    try
                    {
                        var parsed = _hdlc.ParseFrame(response);
                        if (parsed.Type == (byte)BslCommand.BSL_REP_ACK)
                        {
                            _hdlc.DisableTranscode();
                            Log("[FDL] Transcode disabled");
                            return true;
                        }
                        else if (parsed.Type == (byte)BslCommand.BSL_REP_UNSUPPORTED_COMMAND)
                        {
                            // Some FDLs do not support this command, this is normal
                            Log("[FDL] FDL does not support DISABLE_TRANSCODE (Normal)");
                            return true;
                        }
                        else
                        {
                            Log("[FDL] DISABLE_TRANSCODE response: 0x{0:X2}", parsed.Type);
                        }
                    }
                    catch { }
                }
                
                // Even if no response, attempt to disable transcode
                _hdlc.DisableTranscode();
                Log("[FDL] Transcode status set to disabled (No response)");
                return true;
            }
            catch (Exception ex)
            {
                Log("[FDL] Disable transcode exception: {0}", ex.Message);
                // Continue trying
                _hdlc.DisableTranscode();
                return true;
            }
        }

        #endregion

        #region Device Information

        /// <summary>
        /// Read version information
        /// </summary>
        public async Task<string> ReadVersionAsync()
        {
            Log("[FDL] Reading version information...");

            try
            {
            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_VERSION);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_VER && parsed.Payload != null)
                    {
                        string version = Encoding.UTF8.GetString(parsed.Payload).TrimEnd('\0');
                        Log("[FDL] Version: {0}", version);
                        return version;
                    }
                }
                catch { }
            }

            Log("[FDL] Read version failed");
            return null;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read version exception: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Read chip type
        /// </summary>
        public async Task<uint> ReadChipTypeAsync()
        {
            Log("[FDL] Reading chip type...");

            try
            {
            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_CHIP_TYPE);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Payload != null && parsed.Payload.Length >= 4)
                    {
                        uint chipId = BitConverter.ToUInt32(parsed.Payload, 0);
                        Log("[FDL] Chip type: 0x{0:X8} ({1})", chipId, SprdPlatform.GetPlatformName(chipId));
                        return chipId;
                    }
                }
                catch { }
            }

            Log("[FDL] Read chip type failed");
            return 0;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read chip type exception: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Read partition table
        /// </summary>
        /// <summary>
        /// Common partition names list (Reference SPRDClientCore)
        /// Used for traversal detection when READ_PARTITION command is not supported.
        /// </summary>
        private static readonly string[] CommonPartitions = {
            "splloader", "prodnv", "miscdata", "recovery", "misc", "trustos", "trustos_bak",
            "sml", "sml_bak", "uboot", "uboot_bak", "logo", "fbootlogo",
            "l_fixnv1", "l_fixnv2", "l_runtimenv1", "l_runtimenv2",
            "gpsgl", "gpsbd", "wcnmodem", "persist", "l_modem",
            "l_deltanv", "l_gdsp", "l_ldsp", "pm_sys", "boot",
            "system", "cache", "vendor", "uboot_log", "userdata", "dtb", "socko", "vbmeta",
            "super", "metadata", "user_partition"
        };

        public async Task<List<SprdPartitionInfo>> ReadPartitionTableAsync()
        {
            Log("[FDL] Reading partition table...");

            try
            {
                // Method 1: Use READ_PARTITION command
            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_PARTITION);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(10000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_PARTITION && parsed.Payload != null)
                    {
                            Log("[FDL] READ_PARTITION success");
                        return ParsePartitionTable(parsed.Payload);
                    }
                        else if (parsed.Type == (byte)BslCommand.BSL_REP_UNSUPPORTED_COMMAND)
                        {
                            // FDL2 does not support READ_PARTITION command, use fallback method
                            Log("[FDL] READ_PARTITION not supported, using traverse method...");
                            return await ReadPartitionTableByTraverseAsync();
                        }
                        else
                        {
                            Log("[FDL] Partition table response type: 0x{0:X2}", parsed.Type);
                    }
                }
                catch (Exception ex)
                {
                    Log("[FDL] Parse partition table failed: {0}", ex.Message);
                }
            }

                // Method 2: Traverse common partition names
                Log("[FDL] Trying traverse method...");
                return await ReadPartitionTableByTraverseAsync();
            }
            catch (Exception ex)
            {
                Log("[FDL] Read partition table exception: {0}", ex.Message);
            return null;
            }
        }

        /// <summary>
        /// Obtain partition table by traversing common partition names (Reference SPRDClientCore TraverseCommonPartitions)
        /// </summary>
        private async Task<List<SprdPartitionInfo>> ReadPartitionTableByTraverseAsync()
        {
            var partitions = new List<SprdPartitionInfo>();
            Log("[FDL] Traversing to detect common partitions...");

            // Global timeout protection (max 30 seconds)
            using (var globalCts = new CancellationTokenSource(30000))
            {
                return await ReadPartitionTableByTraverseInternalAsync(partitions, globalCts.Token);
            }
        }

        /// <summary>
        /// Partition traversal internal implementation (with cancellation support)
        /// </summary>
        private async Task<List<SprdPartitionInfo>> ReadPartitionTableByTraverseInternalAsync(
            List<SprdPartitionInfo> partitions, CancellationToken cancellationToken)
        {
            int failCount = 0;
            int maxConsecutiveFails = 5;  // Considered unsupported after 5 consecutive failures
            
            // Priority common partitions (sorted by probability)
            string[] priorityPartitions = { "boot", "system", "userdata", "cache", "recovery", "misc" };
            
            // Detect priority partitions first to confirm if device supports this command
            foreach (var partName in priorityPartitions)
            {
                // Check cancellation token
                if (cancellationToken.IsCancellationRequested)
                {
                    Log("[FDL] Partition traversal cancelled (Global timeout)");
                    break;
                }

                try
                {
                    Log("[FDL] Detecting partition: {0}...", partName);
                    var result = await CheckPartitionExistWithTimeoutAsync(partName, 3000);
                    if (result == true)
                    {
                        partitions.Add(new SprdPartitionInfo { Name = partName, Offset = 0, Size = 0 });
                        Log("[FDL] + Found partition: {0}", partName);
                        failCount = 0;
                    }
                    else if (result == false)
                    {
                        Log("[FDL] - Partition does not exist: {0}", partName);
                        failCount = 0;  // Explicitly returning false is not a failure
                    }
                    else  // result == null
                    {
                        // Timeout or communication error
                        failCount++;
                        Log("[FDL] ? Partition detection timeout: {0} (Failure {1}/{2})", partName, failCount, maxConsecutiveFails);
                        if (failCount >= maxConsecutiveFails)
                        {
                            Log("[FDL] Partition traversal not supported (Consecutive timeouts)");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log("[FDL] Partition detection exception: {0} - {1}", partName, ex.Message);
                    if (failCount >= maxConsecutiveFails)
                    {
                        Log("[FDL] Partition traversal abnormally terminated");
                        break;
                    }
                }
            }

            // If there are too many consecutive failures, do not continue
            if (failCount >= maxConsecutiveFails || cancellationToken.IsCancellationRequested)
            {
                if (partitions.Count > 0)
                {
                    Log("[FDL] Partial traversal complete, found {0} partitions", partitions.Count);
                    return partitions;
                }
                Log("[FDL] Device does not support partition traversal commands");
                return null;
            }

            // Detect remaining partitions
            foreach (var partName in CommonPartitions)
            {
                // Check cancellation token
                if (cancellationToken.IsCancellationRequested)
                {
                    Log("[FDL] Partition traversal cancelled");
                    break;
                }

                if (priorityPartitions.Contains(partName))
                    continue;  // Skip already detected ones
                    
                try
                {
                    var result = await CheckPartitionExistWithTimeoutAsync(partName, 1500);
                    if (result == true)
                    {
                        partitions.Add(new SprdPartitionInfo { Name = partName, Offset = 0, Size = 0 });
                        Log("[FDL] Found partition: {0}", partName);
                    }
                }
                catch
                {
                    // Ignore single partition detection errors
                }
            }

            if (partitions.Count > 0)
            {
                Log("[FDL] Traversal complete, found {0} partitions", partitions.Count);
                return partitions;
            }

            Log("[FDL] No partitions found");
            return null;
        }
        
        /// <summary>
        /// Partition existence detection with timeout
        /// </summary>
        /// <returns>true=exists, false=does not exist, null=timeout/error</returns>
        private async Task<bool?> CheckPartitionExistWithTimeoutAsync(string partitionName, int timeoutMs)
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                {
                    var task = CheckPartitionExistAsync(partitionName);
                    var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token));
                    
                    if (completedTask == task)
                    {
                        cts.Cancel();
                        return await task;
                    }
                    
                    return null;  // Timeout
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if partition exists (Reference SPRDClientCore CheckPartitionExist)
        /// Full process: READ_START -> READ_MIDST -> READ_END
        /// </summary>
        public async Task<bool> CheckPartitionExistAsync(string partitionName)
        {
            try
            {
                // 1. Build READ_START request: PartitionName (Unicode, 72 bytes) + Size (4 bytes)
                var payload = new byte[76];
                var nameBytes = Encoding.Unicode.GetBytes(partitionName);
                Array.Copy(nameBytes, 0, payload, 0, Math.Min(nameBytes.Length, 72));
                // Set size to 8 bytes, just to test existence
                BitConverter.GetBytes((uint)8).CopyTo(payload, 72);

                var startFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_START, payload);
                if (!await WriteFrameAsyncSafe(startFrame))
                    return false;

                var startResponse = await ReadFrameAsyncSafe(2000);
                if (startResponse == null)
                    return false;
                
                var startParsed = _hdlc.ParseFrame(startResponse);
                
                if (startParsed.Type == (byte)BslCommand.BSL_REP_ACK)
                {
                    // 2. READ_START success, send READ_MIDST to read 8 bytes for verification
                    // Format: [ReadSize 4-byte LE] + [Offset 4-byte LE]
                    var midstPayload = new byte[8];
                    BitConverter.GetBytes((uint)8).CopyTo(midstPayload, 0);  // ReadSize = 8
                    BitConverter.GetBytes((uint)0).CopyTo(midstPayload, 4);  // Offset = 0
                    
                    var midstFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_MIDST, midstPayload);
                    if (!await WriteFrameAsyncSafe(midstFrame))
                    {
                        // Send READ_END for cleanup
                        await SendReadEndAsync();
                        return false;
                    }
                    
                    var midstResponse = await ReadFrameAsyncSafe(2000);
                    
                    // 3. Send READ_END to finish
                    await SendReadEndAsync();
                    
                    if (midstResponse != null)
                    {
                        var midstParsed = _hdlc.ParseFrame(midstResponse);
                        // If READ_FLASH data is returned, the partition exists
                        return midstParsed.Type == (byte)BslCommand.BSL_REP_READ_FLASH;
                    }
                }
                else
                {
                    // READ_START failed, still need to send READ_END
                    await SendReadEndAsync();
                }

                return false;
            }
            catch
            {
                // Try sending READ_END to clean up state on exception
                try { await SendReadEndAsync(); } catch { }
                return false;
            }
        }
        
        /// <summary>
        /// Send READ_END command
        /// </summary>
        private async Task SendReadEndAsync()
        {
            try
            {
                var endFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_END);
                await WriteFrameAsyncSafe(endFrame);
                await ReadFrameAsyncSafe(500);
            }
            catch { }
        }

        /// <summary>
        /// Parse partition table data (Reference SPRDClientCore)
        /// Format: [PartitionName 72-byte Unicode] + [PartitionSize 4-byte LE] = 76 bytes per entry
        /// </summary>
        private List<SprdPartitionInfo> ParsePartitionTable(byte[] data)
        {
            var partitions = new List<SprdPartitionInfo>();
            
            if (data == null || data.Length == 0)
            {
                Log("[FDL] Partition table data is empty");
                return partitions;
            }
            
            // Partition table format (Reference SPRDClientCore):
            // Name: 72 bytes (Unicode, 36 characters)
            // Size: 4 bytes (Little Endian)
            // Each record is 76 bytes
            const int NameSize = 72;  // 36 chars * 2 bytes (Unicode)
            const int SizeFieldSize = 4;
            const int EntrySize = NameSize + SizeFieldSize;  // 76 bytes
            
            int count = data.Length / EntrySize;
            Log("[FDL] Partition table data size: {0} bytes, expected {1} partitions", data.Length, count);

            for (int i = 0; i < count; i++)
            {
                int offset = i * EntrySize;

                // Partition name: Unicode encoding
                string name = Encoding.Unicode.GetString(data, offset, NameSize).TrimEnd('\0');
                if (string.IsNullOrEmpty(name))
                    continue;

                // Partition size: Little Endian
                uint size = BitConverter.ToUInt32(data, offset + NameSize);

                var partition = new SprdPartitionInfo
                {
                    Name = name,
                    Offset = 0,  // READ_PARTITION response does not include offset
                    Size = size
                };

                partitions.Add(partition);
                Log("[FDL] Partition: {0}, Size: {1}", partition.Name, FormatSize(partition.Size));
            }

            Log("[FDL] Parse complete, total {0} partitions", partitions.Count);
            return partitions;
        }

        #endregion

        #region Security Functions

        /// <summary>
        /// Unlock/Lock device
        /// </summary>
        /// <param name="unlockData">Unlock data</param>
        /// <param name="relock">true=relock, false=unlock</param>
        public async Task<bool> UnlockAsync(byte[] unlockData = null, bool relock = false)
        {
            string action = relock ? "Lock" : "Unlock";
            Log($"[FDL] {action}ing device...");

            try
            {
            // Build payload: [1-byte operation type] + [unlock data]
            byte[] payload;
            if (unlockData != null && unlockData.Length > 0)
            {
                payload = new byte[1 + unlockData.Length];
                payload[0] = relock ? (byte)0x00 : (byte)0x01;  // 0=lock, 1=unlock
                Array.Copy(unlockData, 0, payload, 1, unlockData.Length);
            }
            else
            {
                payload = new byte[] { relock ? (byte)0x00 : (byte)0x01 };
            }

            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_UNLOCK, payload);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(10000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        Log($"[FDL] {action} successful");
                        return true;
                    }
                    Log("[FDL] {0} response: 0x{1:X2}", action, parsed.Type);
                }
                catch { }
            }

            Log($"[FDL] {action} failed");
            return false;
            }
            catch (Exception ex)
            {
                Log("[FDL] {0} exception: {1}", action, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Read public key
        /// </summary>
        public async Task<byte[]> ReadPublicKeyAsync()
        {
            Log("[FDL] Reading public key...");

            try
            {
            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_PUBKEY);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Payload != null && parsed.Payload.Length > 0)
                    {
                        Log("[FDL] Public key length: {0} bytes", parsed.Payload.Length);
                        return parsed.Payload;
                    }
                }
                catch { }
            }

            Log("[FDL] Read public key failed");
            return null;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read public key exception: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Send signature
        /// </summary>
        public async Task<bool> SendSignatureAsync(byte[] signature)
        {
            if (signature == null || signature.Length == 0)
            {
                Log("[FDL] Signature data is empty");
                return false;
            }

            Log("[FDL] Sending signature, length: {0} bytes", signature.Length);

            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_SEND_SIGNATURE, signature);
            await WriteFrameAsync(frame);

            if (await WaitAckAsync(10000))
            {
                Log("[FDL] Signature verification successful");
                return true;
            }

            Log("[FDL] Signature verification failed");
            return false;
        }

        /// <summary>
        /// Read eFuse
        /// </summary>
        public async Task<byte[]> ReadEfuseAsync(uint blockId = 0)
        {
            Log("[FDL] Reading eFuse, Block: {0}", blockId);

            try
            {
            var payload = BitConverter.GetBytes(blockId);
            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_EFUSE, payload);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Payload != null && parsed.Payload.Length > 0)
                    {
                        Log("[FDL] eFuse data: {0} bytes", parsed.Payload.Length);
                        return parsed.Payload;
                    }
                }
                catch { }
            }

            Log("[FDL] Read eFuse failed");
            return null;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read eFuse exception: {0}", ex.Message);
                return null;
            }
        }

        #endregion

        #region NV Operations

        /// <summary>
        /// Read NV item
        /// </summary>
        public async Task<byte[]> ReadNvItemAsync(ushort itemId)
        {
            Log("[FDL] Reading NV item: {0}", itemId);

            try
            {
            var payload = BitConverter.GetBytes(itemId);
            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_NVITEM, payload);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_DATA && parsed.Payload != null)
                    {
                        Log("[FDL] NV item {0} data: {1} bytes", itemId, parsed.Payload.Length);
                        return parsed.Payload;
                    }
                }
                catch { }
            }

            Log("[FDL] Read NV item {0} failed", itemId);
            return null;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read NV item exception: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Write NV item
        /// </summary>
        public async Task<bool> WriteNvItemAsync(ushort itemId, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                Log("[FDL] NV data is empty");
                return false;
            }

            Log("[FDL] Writing NV item: {0}, Length: {1} bytes", itemId, data.Length);

            // Payload: ItemId(2) + Data(N)
            var payload = new byte[2 + data.Length];
            BitConverter.GetBytes(itemId).CopyTo(payload, 0);
            Array.Copy(data, 0, payload, 2, data.Length);

            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_WRITE_NVITEM, payload);
            await WriteFrameAsync(frame);

            if (await WaitAckAsync(5000))
            {
                Log("[FDL] NV item {0} written successfully", itemId);
                return true;
            }

            Log("[FDL] Write NV item {0} failed", itemId);
            return false;
        }

        /// <summary>
        /// Read IMEI (NV Item 0)
        /// </summary>
        public async Task<string> ReadImeiAsync()
        {
            var data = await ReadNvItemAsync(0);
            if (data != null && data.Length >= 8)
            {
                // IMEI format conversion
                var imei = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    imei.AppendFormat("{0:X2}", data[i]);
                }
                string result = imei.ToString().TrimStart('0').Substring(0, 15);
                Log("[FDL] IMEI: {0}", result);
                return result;
            }
            return null;
        }

        #endregion

        #region Flash Information

        /// <summary>
        /// Read Flash information
        /// </summary>
        public async Task<SprdFlashInfo> ReadFlashInfoAsync()
        {
            Log("[FDL] Reading Flash information...");

            try
            {
            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_READ_FLASH_INFO);
            await WriteFrameAsync(frame);

                var response = await ReadFrameAsyncSafe(5000);
            if (response != null)
            {
                try
                {
                    var parsed = _hdlc.ParseFrame(response);
                    if (parsed.Type == (byte)BslCommand.BSL_REP_FLASH_INFO && parsed.Payload != null)
                    {
                        return ParseFlashInfo(parsed.Payload);
                    }
                }
                catch { }
            }

            Log("[FDL] Read Flash info failed");
            return null;
            }
            catch (Exception ex)
            {
                Log("[FDL] Read Flash info exception: {0}", ex.Message);
                return null;
            }
        }

        private SprdFlashInfo ParseFlashInfo(byte[] data)
        {
            if (data.Length < 16)
                return null;

            var info = new SprdFlashInfo
            {
                FlashType = data[0],
                ManufacturerId = data[1],
                DeviceId = BitConverter.ToUInt16(data, 2),
                BlockSize = BitConverter.ToUInt32(data, 4),
                BlockCount = BitConverter.ToUInt32(data, 8),
                TotalSize = BitConverter.ToUInt32(data, 12)
            };

            Log("[FDL] Flash: Type={0}, Manufacturer=0x{1:X2}, Device=0x{2:X4}, Size={3}",
                info.FlashTypeName, info.ManufacturerId, info.DeviceId, FormatSize(info.TotalSize));

            return info;
        }

        #endregion

        #region Partition Table Operations

        /// <summary>
        /// Repartition
        /// </summary>
        public async Task<bool> RepartitionAsync(byte[] partitionTableData)
        {
            if (_stage != FdlStage.FDL2)
            {
                Log("[FDL] FDL2 must be loaded first");
                return false;
            }

            if (partitionTableData == null || partitionTableData.Length == 0)
            {
                Log("[FDL] Partition table data is empty");
                return false;
            }

            Log("[FDL] Repartitioning, data length: {0} bytes", partitionTableData.Length);

            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_REPARTITION, partitionTableData);
            await WriteFrameAsync(frame);

            if (await WaitAckAsync(30000))
            {
                Log("[FDL] Repartition successful");
                return true;
            }

            Log("[FDL] Repartition failed");
            return false;
        }

        #endregion

        #region Baud Rate

        /// <summary>
        /// Set baud rate
        /// </summary>
        public async Task<bool> SetBaudRateAsync(int baudRate)
        {
            Log("[FDL] Setting baud rate: {0}", baudRate);

            var payload = BitConverter.GetBytes((uint)baudRate);
            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_SET_BAUD, payload);
            await WriteFrameAsync(frame);

            if (await WaitAckAsync(2000))
            {
                // Wait for device to switch
                await Task.Delay(100);
                
                // Update local baud rate
                if (_port != null && _port.IsOpen)
                {
                    _port.BaudRate = baudRate;
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }

                Log("[FDL] Baud rate switch successful");
                return true;
            }

            Log("[FDL] Baud rate switch failed");
            return false;
        }

        /// <summary>
        /// Check baud rate
        /// </summary>
        public async Task<bool> CheckBaudRateAsync()
        {
            Log("[FDL] Checking baud rate...");

            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_CHECK_BAUD);
            await WriteFrameAsync(frame);

            if (await WaitAckAsync(2000))
            {
                Log("[FDL] Baud rate check successful");
                return true;
            }

            Log("[FDL] Baud rate check failed");
            return false;
        }

        #endregion

        #region Force Download Mode

        /// <summary>
        /// Enter force download mode
        /// </summary>
        public async Task<bool> EnterForceDownloadAsync()
        {
            Log("[FDL] Entering force download mode...");

            try
            {
                // Send special command to enter force download mode
                // 1. Send sync frames
                byte[] syncFrame = new byte[] { 0x7E, 0x7E, 0x7E, 0x7E };
                await _port.BaseStream.WriteAsync(syncFrame, 0, syncFrame.Length);
                await Task.Delay(100);

                // 2. Send force download command (Use BSL_CMD_CONNECT with special flag)
                byte[] payload = new byte[] { 0x00, 0x00, 0x00, 0x01 };  // Force mode flag
                var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_CONNECT, payload);
                await WriteFrameAsync(frame);

                // 3. Wait for response
                if (await WaitAckAsync(5000))
                {
                    Log("[FDL] Force download mode activated successfully");
                    return true;
                }

                // 4. Try another way: Send reset command then reconnect immediately
                var resetFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_RESET);
                await WriteFrameAsync(resetFrame);
                
                await Task.Delay(1000);

                // Resync
                for (int i = 0; i < 10; i++)
                {
                    await _port.BaseStream.WriteAsync(syncFrame, 0, syncFrame.Length);
                    await Task.Delay(100);
                    
                    if (_port.BytesToRead > 0)
                    {
                        var response = await ReadFrameAsync(1000);
                        if (response != null)
                        {
                            Log("[FDL] Device responded, force download mode might be activated");
                            return true;
                        }
                    }
                }

                Log("[FDL] Force download mode activation failed");
                return false;
            }
            catch (Exception ex)
            {
                Log("[FDL] Force download mode exception: {0}", ex.Message);
                return false;
            }
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reset device
        /// </summary>
        public async Task<bool> ResetDeviceAsync()
        {
            Log("[FDL] Resetting device...");

            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_RESET);
            await WriteFrameAsync(frame);

            bool success = await WaitAckAsync(2000);
            if (success)
            {
                Log("[FDL] Reset command sent successfully");
                _stage = FdlStage.None;
                SetState(SprdDeviceState.Disconnected);
            }

            return success;
        }

        /// <summary>
        /// Power off
        /// </summary>
        public async Task<bool> PowerOffAsync()
        {
            Log("[FDL] Powering off...");

            var frame = _hdlc.BuildCommand((byte)BslCommand.BSL_CMD_POWER_OFF);
            await WriteFrameAsync(frame);

            bool success = await WaitAckAsync(2000);
            if (success)
            {
                Log("[FDL] Power off command sent successfully");
                _stage = FdlStage.None;
                SetState(SprdDeviceState.Disconnected);
            }

            return success;
        }

        /// <summary>
        /// Keep charging (Reference spreadtrum_flash: keep_charge)
        /// </summary>
        public async Task<bool> KeepChargeAsync(bool enable = true)
        {
            Log("[FDL] Set keep charge: {0}", enable ? "On" : "Off");

            byte[] payload = new byte[4];
            BitConverter.GetBytes(enable ? 1 : 0).CopyTo(payload, 0);

            var frame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_KEEP_CHARGE, payload);
            await WriteFrameAsync(frame);

            bool success = await WaitAckAsync(2000);
            if (success)
            {
                Log("[FDL] Keep charge setting successful");
            }
            return success;
        }

        /// <summary>
        /// Read raw Flash (using special partition names)
        /// Reference spreadtrum_flash: read_flash
        /// Special partition names:
        ///   - 0x80000001: boot0
        ///   - 0x80000002: boot1
        ///   - 0x80000003: kernel (NOR Flash)
        ///   - 0x80000004: user
        ///   - user_partition: Raw Flash access (ignore partitions)
        ///   - splloader: Bootloader (similar to FDL1)
        ///   - uboot: FDL2 alias
        /// </summary>
        public async Task<byte[]> ReadFlashAsync(uint flashId, uint offset, uint size, CancellationToken cancellationToken = default)
        {
            Log("[FDL] Reading Flash: ID=0x{0:X8}, Offset=0x{1:X}, Size={2}", flashId, offset, FormatSize(size));

            if (_stage != FdlStage.FDL2)
            {
                Log("[FDL] Error: FDL2 must be loaded first");
                return null;
            }

            // Check if using DHTB auto-size
            if (size == 0xFFFFFFFF)  // auto
            {
                var dhtbSize = await ReadDhtbSizeAsync(flashId);
                if (dhtbSize > 0)
                {
                    size = dhtbSize;
                    Log("[FDL] DHTB auto-detected size: {0}", FormatSize(size));
                }
                else
                {
                    Log("[FDL] DHTB parse failed, using default size 4MB");
                    size = 4 * 1024 * 1024;
                }
            }

            using (var ms = new MemoryStream())
            {
                // Build READ_FLASH command payload
                // [Flash ID (4)] [Offset (4)] [Size (4)]
                byte[] payload = new byte[12];
                BitConverter.GetBytes(flashId).CopyTo(payload, 0);
                BitConverter.GetBytes(offset).CopyTo(payload, 4);
                BitConverter.GetBytes(size).CopyTo(payload, 8);

                var startFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_FLASH, payload);
                if (!await WriteFrameAsyncSafe(startFrame))
                {
                    Log("[FDL] Failed to send READ_FLASH");
                    return null;
                }

                // Read data blocks
                uint received = 0;
                int consecutiveErrors = 0;

                while (received < size && !cancellationToken.IsCancellationRequested)
                {
                    var response = await ReadFrameAsyncSafe(30000);
                    if (response == null)
                    {
                        consecutiveErrors++;
                        if (consecutiveErrors > 5)
                        {
                            Log("[FDL] Read timeout");
                            break;
                        }
                        await Task.Delay(100);
                        continue;
                    }

                    try
                    {
                        var frame = _hdlc.ParseFrame(response);

                        if (frame.Type == (byte)BslCommand.BSL_REP_READ_FLASH && frame.Payload != null)
                        {
                            ms.Write(frame.Payload, 0, frame.Payload.Length);
                            received += (uint)frame.Payload.Length;
                            consecutiveErrors = 0;

                            // Send ACK
                            var ackFrame = _hdlc.BuildCommand((byte)BslCommand.BSL_REP_ACK);
                            await WriteFrameAsyncSafe(ackFrame);

                            int progress = (int)(received * 100 / size);
                            if (progress % 10 == 0)
                                Log("[FDL] Read progress: {0}%", progress);
                        }
                        else if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                        {
                            // Read complete
                            break;
                        }
                        else
                        {
                            Log("[FDL] Received unexpected response: 0x{0:X2}", frame.Type);
                            consecutiveErrors++;
                        }
                    }
                    catch
                    {
                        consecutiveErrors++;
                    }
                }

                Log("[FDL] Flash read complete, size: {0}", FormatSize((uint)ms.Length));
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Read DHTB header to get Flash size (Reference spreadtrum_flash)
        /// DHTB = Download Header Table Block
        /// </summary>
        private async Task<uint> ReadDhtbSizeAsync(uint flashId)
        {
            try
            {
                // Read DHTB header (usually at offset 0, size 512 bytes)
                byte[] payload = new byte[12];
                BitConverter.GetBytes(flashId).CopyTo(payload, 0);
                BitConverter.GetBytes((uint)0).CopyTo(payload, 4);  // offset = 0
                BitConverter.GetBytes((uint)512).CopyTo(payload, 8);  // size = 512

                var startFrame = _hdlc.BuildFrame((byte)BslCommand.BSL_CMD_READ_FLASH, payload);
                if (!await WriteFrameAsyncSafe(startFrame))
                    return 0;

                var response = await ReadFrameAsyncSafe(5000);
                if (response == null || response.Length < 16)
                    return 0;

                var frame = _hdlc.ParseFrame(response);
                if (frame.Type != (byte)BslCommand.BSL_REP_READ_FLASH || frame.Payload == null)
                    return 0;

                // Parse DHTB header
                // Format: [Magic "DHTB" (4)] [Size (4)] [...]
                if (frame.Payload.Length >= 8)
                {
                    string magic = Encoding.ASCII.GetString(frame.Payload, 0, 4);
                    if (magic == "DHTB")
                    {
                        uint size = BitConverter.ToUInt32(frame.Payload, 4);
                        return size;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Parse special partition name to Flash ID
        /// Reference spreadtrum_flash special partition handling
        /// </summary>
        public static uint ParseSpecialPartitionName(string name)
        {
            switch (name.ToLower())
            {
                case "boot0":
                    return 0x80000001;
                case "boot1":
                    return 0x80000002;
                case "kernel":
                case "nor":
                    return 0x80000003;
                case "user":
                case "user_partition":
                    return 0x80000004;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Determine if it is a special partition name
        /// </summary>
        public static bool IsSpecialPartition(string name)
        {
            string lower = name.ToLower();
            return lower == "boot0" || lower == "boot1" || lower == "kernel" ||
                   lower == "nor" || lower == "user" || lower == "user_partition" ||
                   lower == "splloader" || lower == "spl_loader_bak" || lower == "uboot";
        }

        /// <summary>
        /// Get partition list (XML format) - Reference spreadtrum_flash partition_list
        /// </summary>
        public async Task<string> GetPartitionListXmlAsync()
        {
            var partitions = await ReadPartitionTableAsync();
            if (partitions == null || partitions.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<partitions>");

            foreach (var p in partitions)
            {
                sb.AppendLine($"  <partition name=\"{p.Name}\" size=\"0x{p.Size:X}\" />");
            }

            sb.AppendLine("</partitions>");
            return sb.ToString();
        }

        #endregion

        #region Low-level Communication

        private async Task WriteFrameAsync(byte[] frame)
        {
            if (_isDisposed || _port == null || !_port.IsOpen)
                throw new InvalidOperationException("Port is not open");

            // Use timeout to prevent deadlock
            using (var cts = new CancellationTokenSource(MaxOperationTimeout))
            {
                try
                {
                    await _portLock.WaitAsync(cts.Token);
                    try
                    {
                        if (_port != null && _port.IsOpen)
                {
                    _port.Write(frame, 0, frame.Length);
                }
                    }
                    finally
                    {
                        _portLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Write operation timed out");
                }
            }
        }

        /// <summary>
        /// Safely write frame (catch exceptions, with timeout)
        /// </summary>
        private async Task<bool> WriteFrameAsyncSafe(byte[] frame, int timeout = 0)
        {
            if (_isDisposed || _port == null || !_port.IsOpen)
                return false;

            if (timeout <= 0)
                timeout = DefaultTimeout;

            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    if (!await _portLock.WaitAsync(timeout, cts.Token))
                        return false;

                    try
                    {
                        if (_port != null && _port.IsOpen)
                        {
                            _port.Write(frame, 0, frame.Length);
                            return true;
                        }
                        return false;
                    }
                    finally
                    {
                        _portLock.Release();
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely read frame (catch all exceptions)
        /// </summary>
        private async Task<byte[]> ReadFrameAsyncSafe(int timeout = 0)
        {
            try
            {
                return await ReadFrameAsync(timeout);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safely write to port (Reference SPRDClientCore) - Synchronous method, with lock
        /// </summary>
        private bool SafeWriteToPort(byte[] data)
        {
            if (_isDisposed)
            {
                Log("[FDL] SafeWriteToPort: Disposed");
                return false;
            }
                
            // Synchronously acquire lock (wait up to 3 seconds)
            bool lockAcquired = false;
            try
            {
                lockAcquired = _portLock.Wait(3000);
                if (!lockAcquired)
                {
                    Log("[FDL] SafeWriteToPort: Lock acquisition timed out");
                    return false;
                }
                
                if (_port == null)
                {
                    Log("[FDL] SafeWriteToPort: Port is null");
                    return false;
                }
                
                if (!_port.IsOpen)
                {
                    Log("[FDL] SafeWriteToPort: Port is closed, trying to reopen...");
                    try
                    {
                        _port.Open();
                        _port.DiscardInBuffer();
                        _port.DiscardOutBuffer();
                        Log("[FDL] SafeWriteToPort: Port reopened successfully");
                    }
                    catch (Exception ex)
                    {
                        Log("[FDL] SafeWriteToPort: Reopen failed - {0}", ex.Message);
                        return false;
                    }
                }
                    
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                _port.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log("[FDL] SafeWriteToPort exception: {0}", ex.Message);
                return false;
            }
            finally
            {
                if (lockAcquired)
                    _portLock.Release();
            }
        }

        /// <summary>
        /// Safely read from port (Using polling method to avoid semaphore timeout)
        /// </summary>
        private async Task<byte[]> SafeReadFromPortAsync(int timeout, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
                return null;

            // Create combined cancellation token (external cancellation + timeout)
            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
            return await Task.Run(() =>
            {
                var ms = new MemoryStream();
                bool inFrame = false;
                        int retryCount = 0;

                        while (!linkedCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                if (_isDisposed || _port == null || !_port.IsOpen)
                                    return null;

                                int available = 0;
                                try
                                {
                                    available = _port.BytesToRead;
                                }
                                catch
                                {
                                    Thread.Sleep(10);
                                    retryCount++;
                                    if (retryCount > 10) return null;
                                    continue;
                                }

                                if (available > 0)
                                {
                                    byte[] buffer = new byte[available];
                                    int read = 0;
                                    try
                                    {
                                        read = _port.Read(buffer, 0, available);
                                    }
                                    catch
                                    {
                                        Thread.Sleep(10);
                                        retryCount++;
                                        if (retryCount > 10) return null;
                                        continue;
                                    }

                                    for (int i = 0; i < read; i++)
                                    {
                                        byte b = buffer[i];

                                        if (b == HdlcProtocol.HDLC_FLAG)
                                        {
                                            if (inFrame && ms.Length > 0)
                                            {
                                                ms.WriteByte(b);
                                                return ms.ToArray();
                                            }
                                            inFrame = true;
                                            ms = new MemoryStream();
                                        }

                                        if (inFrame)
                                        {
                                            ms.WriteByte(b);
                                        }
                                    }
                                    
                                    retryCount = 0;  // Reset retry count
                                }
                                else
                                {
                                    Thread.Sleep(5);
                                }
                            }
                            catch
                            {
                                Thread.Sleep(10);
                                retryCount++;
                                if (retryCount > 10) return null;
                            }
                        }

                        // Timeout but partial data exists
                        if (ms.Length > 0)
                            return ms.ToArray();
                            
                        return null;
                    }, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Read frame using polling approach (Avoids semaphore timeout issues)
        /// </summary>
        private async Task<byte[]> ReadWithPollingAsync(int timeout)
        {
            return await Task.Run(() =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var ms = new MemoryStream();
                bool inFrame = false;

                while (stopwatch.ElapsedMilliseconds < timeout)
                {
                    try
                    {
                        if (_port == null || !_port.IsOpen)
                            return null;

                        // Check if data is available using BytesToRead to avoid blocking
                        int available = 0;
                        try
                        {
                            available = _port.BytesToRead;
                        }
                        catch
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        if (available > 0)
                        {
                            byte[] buffer = new byte[available];
                            int read = 0;
                            try
                            {
                                read = _port.Read(buffer, 0, available);
                            }
                            catch
                            {
                                Thread.Sleep(10);
                                continue;
                            }

                            for (int i = 0; i < read; i++)
                            {
                                byte b = buffer[i];

                            if (b == HdlcProtocol.HDLC_FLAG)
                            {
                                if (inFrame && ms.Length > 0)
                                {
                                        ms.WriteByte(b);
                                    return ms.ToArray();
                                }
                                inFrame = true;
                                ms = new MemoryStream();
                            }

                            if (inFrame)
                            {
                                    ms.WriteByte(b);
                                }
                            }
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }
                    catch
                    {
                        Thread.Sleep(10);
                }
                }

                return null;
            });
        }

        private async Task<byte[]> ReadFrameAsync(int timeout = 0, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
                return null;

            if (timeout == 0)
                timeout = DefaultTimeout;

            // Create combined cancellation token
            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    return await Task.Run(() =>
                    {
                        var ms = new MemoryStream();
                        bool inFrame = false;

                        try
                        {
                            while (!linkedCts.Token.IsCancellationRequested)
                            {
                                if (_isDisposed || _port == null || !_port.IsOpen)
                                    return null;

                                try
                                {
                                    int bytesToRead = _port.BytesToRead;
                                    if (bytesToRead > 0)
                                    {
                                        // Read all available bytes
                                        byte[] buffer = new byte[bytesToRead];
                                        int read = _port.Read(buffer, 0, bytesToRead);
                                        
                                        for (int i = 0; i < read; i++)
                                        {
                                            byte b = buffer[i];
                                            
                                            if (b == HdlcProtocol.HDLC_FLAG)
                                            {
                                                if (inFrame && ms.Length > 0)
                                                {
                                                    ms.WriteByte(b);
                                                    return ms.ToArray();
                                                }
                                                inFrame = true;
                                                ms = new MemoryStream();
                                            }

                                            if (inFrame)
                                            {
                                                ms.WriteByte(b);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Thread.Sleep(5);
                                    }
                                }
                                catch (TimeoutException)
                                {
                                    // Serial port timeout, continue waiting
                                    Thread.Sleep(10);
                                }
                                catch (InvalidOperationException)
                                {
                                    // Port might be closed
                                    return null;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Other exception, return received data (if any)
                            if (ms.Length > 0)
                                return ms.ToArray();
                        }

                        return null;
                    }, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        private async Task<bool> WaitAckAsync(int timeout = 0, bool verbose = false)
        {
            return await WaitAckWithDetailAsync(timeout, null, verbose);
        }
        
        /// <summary>
        /// Wait for ACK response (Safe version, catches all exceptions)
        /// </summary>
        private async Task<bool> WaitAckAsyncSafe(int timeout = 0, bool verbose = false)
        {
            try
            {
                return await WaitAckWithDetailAsync(timeout, null, verbose);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for ACK response (with detailed logging)
        /// </summary>
        private async Task<bool> WaitAckWithDetailAsync(int timeout = 0, string context = null, bool verbose = true)
        {
            if (timeout == 0)
                timeout = DefaultTimeout;

            var response = await ReadFrameAsync(timeout);
            if (response != null)
            {
                if (verbose)
                {
                    Log("[FDL] {0}Received response ({1} bytes): {2}", 
                        context != null ? context + " " : "",
                        response.Length, 
                        BitConverter.ToString(response).Replace("-", " "));
                }
                
                try
                {
                    var frame = _hdlc.ParseFrame(response);
                    if (frame.Type == (byte)BslCommand.BSL_REP_ACK)
                    {
                        return true;
                    }
                    else
                    {
                        // Log non-ACK response for debugging
                        string errorMsg = GetBslErrorMessage(frame.Type);
                        Log("[FDL] Received non-ACK response: 0x{0:X2} ({1})", frame.Type, errorMsg);
                        if (frame.Payload != null && frame.Payload.Length > 0)
                        {
                            Log("[FDL] Response data: {0}", BitConverter.ToString(frame.Payload).Replace("-", " "));
                        }
                        
                        // Provide specific error tips
                        switch (frame.Type)
                        {
                            case 0x8B: // BSL_REP_VERIFY_ERROR
                                Log("[FDL] Verification error: Possible causes:");
                                Log("[FDL]   1. FDL file mismatch with chip");
                                Log("[FDL]   2. Incorrect load address (Current: 0x{0:X8})", CustomFdl1Address > 0 ? CustomFdl1Address : SprdPlatform.GetFdl1Address(ChipId));
                                Log("[FDL]   3. FDL file corrupted or incorrect format");
                                break;
                            case 0x89: // BSL_REP_DOWN_DEST_ERROR
                                Log("[FDL] Destination address error");
                                break;
                            case 0x8A: // BSL_REP_DOWN_SIZE_ERROR
                                Log("[FDL] Data size error");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("[FDL] Parse response exception: {0}", ex.Message);
                    Log("[FDL] Raw response: {0}", BitConverter.ToString(response).Replace("-", " "));
                }
            }
            else
            {
                if (verbose)
                Log("[FDL] Wait for response timeout ({0}ms)", timeout);
            }
            return false;
        }

        /// <summary>
        /// Send command and wait for ACK (with retry)
        /// </summary>
        private async Task<bool> SendCommandWithRetryAsync(byte command, byte[] payload = null, int timeout = 0, int retries = -1)
        {
            if (retries < 0)
                retries = CommandRetries;
            if (timeout == 0)
                timeout = DefaultTimeout;

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                if (attempt > 0)
                {
                    Log("[FDL] Retry {0}/{1}...", attempt, retries);
                    await Task.Delay(RetryDelayMs);
                    
                    // Clear buffer before retry
                    try { _port?.DiscardInBuffer(); } catch { }
                }

                try
                {
                    var frame = _hdlc.BuildFrame(command, payload ?? new byte[0]);
                    
                    // Use safe write to avoid throwing exceptions
                    if (!await WriteFrameAsyncSafe(frame))
                    {
                        Log("[FDL] Frame write failed, retrying...");
                        continue;
                    }
                    
                    if (await WaitAckAsyncSafe(timeout))
                        return true;
                }
                catch (Exception ex)
                {
                    Log("[FDL] Command send exception: {0}", ex.Message);
                    // Do not throw exception, continue retrying
                }
            }
            return false;
        }

        /// <summary>
        /// Send data frame and wait for ACK (with retry, used for block transfer)
        /// </summary>
        private async Task<bool> SendDataWithRetryAsync(byte command, byte[] data, int timeout = 0, int maxRetries = 2)
        {
            if (timeout == 0)
                timeout = DefaultTimeout;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    Log("[FDL] Data retransmission {0}/{1}...", attempt, maxRetries);
                    await Task.Delay(RetryDelayMs / 2);  // Shorter interval for block retries
                }

                try
                {
                    var frame = _hdlc.BuildFrame(command, data);
                    await WriteFrameAsync(frame);
                    
                    if (await WaitAckAsync(timeout))
                        return true;
                }
                catch (TimeoutException)
                {
                    // Continue retrying on timeout
                }
                catch (IOException)
                {
                    // Continue retrying on IO error
                }
            }
            return false;
        }

        #endregion

        #region Helper Methods

        private void SetState(SprdDeviceState state)
        {
            if (_state != state)
            {
                _state = state;
                OnStateChanged?.Invoke(state);
            }
        }

        private void Log(string format, params object[] args)
        {
            string message = string.Format(format, args);
            OnLog?.Invoke(message);
        }

        /// <summary>
        /// Debug log (only output to debug window, not displayed in UI)
        /// </summary>
        private void LogDebug(string format, params object[] args)
        {
            string message = string.Format(format, args);
            System.Diagnostics.Debug.WriteLine(message);
        }

        /// <summary>
        /// Get BSL error code description
        /// </summary>
        private string GetBslErrorMessage(byte errorCode)
        {
            switch (errorCode)
            {
                case 0x80: return "Success";
                case 0x81: return "Version Information";
                case 0x82: return "Invalid Command";
                case 0x83: return "Data Error";
                case 0x84: return "Operation Failed";
                case 0x85: return "Unsupported Baud Rate";
                case 0x86: return "Download Not Started";
                case 0x87: return "Duplicate Download Start";
                case 0x88: return "Download Ended Early";
                case 0x89: return "Incorrect Download Target Address";
                case 0x8A: return "Incorrect Download Size";
                case 0x8B: return "Verification Error - Possible FDL Mismatch";
                case 0x8C: return "Not Verified";
                case 0x8D: return "Insufficient Memory";
                case 0x8E: return "Wait Input Timeout";
                case 0x8F: return "Operation Successful";
                case 0xA6: return "Signature Verification Failed";
                case 0xFE: return "Unsupported Command";
                default: return "Unknown Error";
            }
        }

        private static string FormatSize(uint size)
        {
            if (size >= 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
            if (size >= 1024 * 1024)
                return string.Format("{0:F2} MB", size / (1024.0 * 1024));
            if (size >= 1024)
                return string.Format("{0:F2} KB", size / 1024.0);
            return string.Format("{0} B", size);
        }

        /// <summary>
        /// Write 32-bit value (Big-Endian format)
        /// </summary>
        private static void WriteBigEndian32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Write 16-bit value (Big-Endian format)
        /// </summary>
        private static void WriteBigEndian16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Cancel all pending operations
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }
                catch (ObjectDisposedException) { /* Disposed, ignore */ }

                // Safely disconnect
                SafeDisconnect();
                
                // Release SemaphoreSlim
                try
                {
                    _portLock?.Dispose();
                }
                catch (ObjectDisposedException) { /* Disposed, ignore */ }
            }

            _disposed = true;
        }

        /// <summary>
        /// Safely disconnect (with timeout protection)
        /// </summary>
        private void SafeDisconnect()
        {
            try
            {
                if (_port != null)
                {
                    // Mark as disposed to block new operations
                    _isDisposed = true;

                    // Clear buffers first (ignore exceptions to ensure cleanup continues)
                    if (_port.IsOpen)
                    {
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FDL] Buffer clear exception: {ex.Message}"); }
                    }

                    // Asynchronously close port (with timeout to avoid deadlock)
                    try
                    {
                        using (var cts = new CancellationTokenSource(2000))
                        {
                            var closeTask = Task.Run(() =>
                            {
                                try
                                {
                                    if (_port != null && _port.IsOpen)
                                        _port.Close();
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FDL] Port close exception: {ex.Message}"); }
                            });

                            // Synchronous wait, at most 2 seconds
                            bool completed = closeTask.Wait(2000);
                            if (!completed)
                            {
                                Log("[FDL] Warning: Port close timed out");
                            }
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FDL] Port close task exception: {ex.Message}"); }

                    try
                    {
                        _port?.Dispose();
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FDL] Port disposal exception: {ex.Message}"); }
                    _port = null;
                }
            }
            catch (Exception ex)
            {
                Log("[FDL] Disconnect exception: {0}", ex.Message);
            }
            finally
            {
                _stage = FdlStage.None;
                SetState(SprdDeviceState.Disconnected);
            }
        }

        ~FdlClient()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// Spreadtrum partition information
    /// </summary>
    public class SprdPartitionInfo
    {
        public string Name { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }

        public override string ToString()
        {
            return string.Format("{0} (0x{1:X8}, {2} bytes)", Name, Offset, Size);
        }
    }

    /// <summary>
    /// Spreadtrum Flash information
    /// </summary>
    public class SprdFlashInfo
    {
        public byte FlashType { get; set; }
        public byte ManufacturerId { get; set; }
        public ushort DeviceId { get; set; }
        public uint BlockSize { get; set; }
        public uint BlockCount { get; set; }
        public uint TotalSize { get; set; }
        public string ChipModel { get; set; }

        public string FlashTypeName
        {
            get
            {
                switch (FlashType)
                {
                    case 0: return "Unknown";
                    case 1: return "NAND";
                    case 2: return "NOR";
                    case 3: return "eMMC";
                    case 4: return "UFS";
                    default: return string.Format("Type_{0}", FlashType);
                }
            }
        }

        public string ManufacturerName
        {
            get
            {
                switch (ManufacturerId)
                {
                    case 0x15: return "Samsung";
                    case 0x45: return "SanDisk";
                    case 0x90: return "Hynix";
                    case 0xFE: return "Micron";
                    case 0x13: return "Toshiba";
                    case 0x70: return "Kingston";
                    default: return string.Format("0x{0:X2}", ManufacturerId);
                }
            }
        }

        public override string ToString()
        {
            return string.Format("{0} {1} {2}", FlashTypeName, ManufacturerName, 
                TotalSize >= 1024 * 1024 * 1024 
                    ? string.Format("{0:F1} GB", TotalSize / (1024.0 * 1024 * 1024))
                    : string.Format("{0} MB", TotalSize / (1024 * 1024)));
        }
    }

    /// <summary>
    /// Spreadtrum security information
    /// </summary>
    public class SprdSecurityInfo
    {
        public bool IsLocked { get; set; }
        public bool RequiresSignature { get; set; }
        public byte[] PublicKey { get; set; }
        public uint SecurityVersion { get; set; }

        // Extended properties
        public bool IsSecureBootEnabled { get; set; }
        public bool IsEfuseLocked { get; set; }
        public bool IsAntiRollbackEnabled { get; set; }
        public string PublicKeyHash { get; set; }
        public byte[] RawEfuseData { get; set; }

        public override string ToString()
        {
            return string.Format("SecureBoot: {0}, eFuseLocked: {1}, AntiRollback: {2}, Version: {3}",
                IsSecureBootEnabled ? "Yes" : "No",
                IsEfuseLocked ? "Yes" : "No",
                IsAntiRollbackEnabled ? "Yes" : "No",
                SecurityVersion);
        }
    }

    /// <summary>
    /// NV item ID constants
    /// </summary>
    public static class SprdNvItems
    {
        public const ushort NV_IMEI = 0;
        public const ushort NV_IMEI2 = 1;
        public const ushort NV_BT_ADDR = 2;
        public const ushort NV_WIFI_ADDR = 3;
        public const ushort NV_SERIAL_NUMBER = 4;
        public const ushort NV_CALIBRATION = 100;
        public const ushort NV_RF_CALIBRATION = 101;
        public const ushort NV_GPS_CONFIG = 200;
        public const ushort NV_AUDIO_PARAM = 300;
    }
}
