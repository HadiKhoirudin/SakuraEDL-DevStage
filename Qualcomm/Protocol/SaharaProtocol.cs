// ============================================================================
// LoveAlways - Sahara 协议完整实现
// Sahara Protocol - 高通 EDL 模式第一阶段引导协议
// ============================================================================
// 模块: Qualcomm.Protocol
// 功能: 处理 Sahara 握手、芯片信息读取、Programmer 上传
// 支持: V1/V2/V3 协议版本
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;

namespace LoveAlways.Qualcomm.Protocol
{
    #region 协议枚举定义

    /// <summary>
    /// Sahara 命令 ID
    /// </summary>
    public enum SaharaCommand : uint
    {
        Hello = 0x01,
        HelloResponse = 0x02,
        ReadData = 0x03,            // 32位读取 (老设备)
        EndImageTransfer = 0x04,
        Done = 0x05,
        DoneResponse = 0x06,
        Reset = 0x07,               // 硬重置 (重启设备)
        ResetResponse = 0x08,
        MemoryDebug = 0x09,
        MemoryRead = 0x0A,
        CommandReady = 0x0B,        // 命令模式就绪
        SwitchMode = 0x0C,          // 切换模式
        Execute = 0x0D,             // 执行命令
        ExecuteData = 0x0E,         // 命令数据响应
        ExecuteResponse = 0x0F,     // 命令响应确认
        MemoryDebug64 = 0x10,
        MemoryRead64 = 0x11,
        ReadData64 = 0x12,          // 64位读取 (新设备)
        ResetStateMachine = 0x13    // 状态机重置 (软重置)
    }

    /// <summary>
    /// Sahara 模式
    /// </summary>
    public enum SaharaMode : uint
    {
        ImageTransferPending = 0x0,
        ImageTransferComplete = 0x1,
        MemoryDebug = 0x2,
        Command = 0x3               // 命令模式 (读取信息)
    }

    /// <summary>
    /// Sahara 执行命令 ID
    /// </summary>
    public enum SaharaExecCommand : uint
    {
        SerialNumRead = 0x01,       // 序列号
        MsmHwIdRead = 0x02,         // HWID (仅 V1/V2)
        OemPkHashRead = 0x03,       // PK Hash
        SblInfoRead = 0x06,         // SBL 信息 (V3)
        SblSwVersion = 0x07,        // SBL 版本 (V1/V2)
        PblSwVersion = 0x08,        // PBL 版本
        ChipIdV3Read = 0x0A,        // V3 芯片信息 (包含 HWID)
        SerialNumRead64 = 0x14      // 64位序列号
    }

    /// <summary>
    /// Sahara 状态码
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
        HashTableAuthFailure = 0x21,    // Loader 签名不匹配
        HashVerificationFailure = 0x22, // 镜像被篡改
        HashTableNotFound = 0x23,       // 镜像未签名
        MaxErrors = 0x29
    }

    #endregion

    #region 协议结构体

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
    /// Sahara 状态辅助类
    /// </summary>
    public static class SaharaStatusHelper
    {
        public static string GetErrorMessage(SaharaStatus status)
        {
            switch (status)
            {
                case SaharaStatus.Success: return "成功";
                case SaharaStatus.InvalidCommand: return "无效命令";
                case SaharaStatus.ProtocolMismatch: return "协议不匹配";
                case SaharaStatus.UnexpectedImageId: return "镜像 ID 不匹配";
                case SaharaStatus.ReceiveTimeout: return "接收超时";
                case SaharaStatus.TransmitTimeout: return "发送超时";
                case SaharaStatus.HashTableAuthFailure: return "签名验证失败: Loader 与设备不匹配";
                case SaharaStatus.HashVerificationFailure: return "完整性校验失败: 镜像可能被篡改";
                case SaharaStatus.HashTableNotFound: return "找不到签名数据: 镜像未签名";
                case SaharaStatus.CommandExecuteFailure: return "命令执行失败";
                case SaharaStatus.AccessDenied: return "命令不支持";
                default: return string.Format("未知错误 (0x{0:X2})", (uint)status);
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
    /// Sahara 协议客户端 - 完整版 (支持 V1/V2/V3)
    /// </summary>
    public class SaharaClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private bool _disposed;

        // 配置
        private const int MAX_BUFFER_SIZE = 4096;
        private const int READ_TIMEOUT_MS = 30000;
        private const int HELLO_TIMEOUT_MS = 30000;

        // 协议状态
        public uint ProtocolVersion { get; private set; }
        public uint ProtocolVersionSupported { get; private set; }
        public SaharaMode CurrentMode { get; private set; }
        public bool IsConnected { get; private set; }

        // 芯片信息
        public string ChipSerial { get; private set; }
        public string ChipHwId { get; private set; }
        public string ChipPkHash { get; private set; }
        public QualcommChipInfo ChipInfo { get; private set; }

        private bool _chipInfoRead = false;
        private bool _doneSent = false;
        private bool _skipCommandMode = false;

        // 预读取的 Hello 数据
        private byte[] _pendingHelloData = null;
        
        /// <summary>
        /// 是否跳过命令模式（某些设备不支持命令模式，强制跳过可避免 InvalidCommand 错误）
        /// </summary>
        public bool SkipCommandMode 
        { 
            get { return _skipCommandMode; } 
            set { _skipCommandMode = value; } 
        }

        // 传输进度
        private long _totalSent = 0;
        private Action<double> _progressCallback;

        public SaharaClient(SerialPortManager port, Action<string> log = null, Action<string> logDetail = null, Action<double> progressCallback = null)
        {
            _port = port;
            _log = log ?? delegate { };
            _logDetail = logDetail ?? _log;  // 如果没有指定，默认使用 _log
            _progressCallback = progressCallback;
            ProtocolVersion = 2;
            ProtocolVersionSupported = 1;
            CurrentMode = SaharaMode.ImageTransferPending;
            ChipSerial = "";
            ChipHwId = "";
            ChipPkHash = "";
            ChipInfo = new QualcommChipInfo();
        }

        /// <summary>
        /// 设置预读取的 Hello 数据
        /// </summary>
        public void SetPendingHelloData(byte[] data)
        {
            _pendingHelloData = data;
        }

        /// <summary>
        /// 握手并上传 Loader (失败时自动尝试重置) - 从内存数据
        /// </summary>
        /// <param name="loaderData">引导文件数据 (byte[])</param>
        /// <param name="loaderName">引导名称 (用于日志显示)</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="maxRetries">最大重试次数</param>
        public async Task<bool> HandshakeAndUploadAsync(byte[] loaderData, string loaderName, CancellationToken ct = default(CancellationToken), int maxRetries = 2)
        {
            if (loaderData == null || loaderData.Length == 0)
                throw new ArgumentException("引导数据为空");

            _log(string.Format("[Sahara] 加载内嵌引导: {0} ({1} KB)", loaderName, loaderData.Length / 1024));
            return await HandshakeAndUploadCoreAsync(loaderData, ct, maxRetries);
        }

        /// <summary>
        /// 握手并上传 Loader (失败时自动尝试重置) - 从文件路径
        /// </summary>
        /// <param name="loaderPath">引导文件路径</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="maxRetries">最大重试次数 (默认2次，即最多尝试3次)</param>
        public async Task<bool> HandshakeAndUploadAsync(string loaderPath, CancellationToken ct = default(CancellationToken), int maxRetries = 2)
        {
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("引导 文件不存在", loaderPath);

            byte[] fileBytes = File.ReadAllBytes(loaderPath);
            _log(string.Format("[Sahara] 加载引导: {0} ({1} KB)", Path.GetFileName(loaderPath), fileBytes.Length / 1024));
            return await HandshakeAndUploadCoreAsync(fileBytes, ct, maxRetries);
        }

        /// <summary>
        /// 握手上传核心逻辑
        /// </summary>
        private async Task<bool> HandshakeAndUploadCoreAsync(byte[] fileBytes, CancellationToken ct, int maxRetries)
        {
            // 尝试握手，失败时自动重置并重试
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) return false;
                
                if (attempt > 0)
                {
                    _log(string.Format("[Sahara] 握手失败，尝试重置 Sahara 状态 (第 {0} 次重试)...", attempt));
                    
                    // 尝试重置 Sahara 状态机
                    bool resetOk = await TryResetSaharaAsync(ct);
                    if (resetOk)
                    {
                        _log("[Sahara] ✓ 状态机重置成功，重新开始握手...");
                    }
                    else
                    {
                        _log("[Sahara] 状态机重置未确认，继续尝试握手...");
                    }
                    
                    // 重置内部状态
                    _chipInfoRead = false;
                    _pendingHelloData = null;
                    _doneSent = false;
                    _totalSent = 0;
                    IsConnected = false;
                    
                    await Task.Delay(300, ct);
                }
                
                bool success = await HandshakeAndLoadInternalAsync(fileBytes, ct);
                if (success)
                {
                    if (attempt > 0)
                        _log(string.Format("[Sahara] ✓ 重试成功 (第 {0} 次尝试)", attempt + 1));
                    return true;
                }
            }
            
            _log("[Sahara] ❌ 多次握手均失败，可能需要断电重启设备");
            return false;
        }

        /// <summary>
        /// 内部握手和加载
        /// </summary>
        private async Task<bool> HandshakeAndLoadInternalAsync(byte[] fileBytes, CancellationToken ct)
        {
            bool done = false;
            int loopGuard = 0;
            int endImageTxCount = 0;
            int timeoutCount = 0;
            _doneSent = false;
            _totalSent = 0;
            var sw = Stopwatch.StartNew();

            while (!done && loopGuard++ < 1000)
            {
                if (ct.IsCancellationRequested)
                    return false;

                byte[] header = null;

                // 检查是否有预读取的 Hello 数据
                if (loopGuard == 1 && _pendingHelloData != null && _pendingHelloData.Length >= 8)
                {
                    header = new byte[8];
                    Array.Copy(_pendingHelloData, 0, header, 0, 8);
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
                        _log("[Sahara] 设备无响应");
                        return false;
                    }

                    int available = _port.BytesToRead;
                    if (available > 0)
                        await ReadBytesAsync(available, 1000, ct);

                    await Task.Delay(500, ct);
                    continue;
                }

                timeoutCount = 0;
                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if (pktLen < 8 || pktLen > MAX_BUFFER_SIZE * 4)
                {
                    PurgeBuffer();
                    await Task.Delay(50, ct);
                    continue;
                }

                // 调试日志：显示收到的命令 (ReadData 除外，因为太频繁)
                if ((SaharaCommand)cmdId != SaharaCommand.ReadData && 
                    (SaharaCommand)cmdId != SaharaCommand.ReadData64)
                {
                    _logDetail(string.Format("[Sahara] 收到: Cmd=0x{0:X2} ({1}), Len={2}", 
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
                        _log("[Sahara] ✅ 引导加载成功");
                        done = true;
                        IsConnected = true;
                        break;

                    case SaharaCommand.CommandReady:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log("[Sahara] 收到 CommandReady，切换到传输模式");
                        SendSwitchMode(SaharaMode.ImageTransferPending);
                        break;

                    default:
                        _log(string.Format("[Sahara] 未知命令: 0x{0:X2}", cmdId));
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        break;
                }
            }

            return done;
        }

        private void HandleEndImageTransferResult(Tuple<bool, bool, int> result, out bool success, out bool isDone, out int newCount)
        {
            success = result.Item1;
            isDone = result.Item2;
            newCount = result.Item3;
        }

        /// <summary>
        /// 处理 Hello 包 (参考 tools 项目优化)
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
            
            // 详细日志 (与 tools 项目对齐)
            _logDetail(string.Format("[Sahara] 收到 HELLO (版本={0}, 模式={1})", ProtocolVersion, deviceMode));

            // 尝试读取芯片信息 (仅首次，且设备处于传输模式)
            if (!_chipInfoRead && deviceMode == (uint)SaharaMode.ImageTransferPending)
            {
                _chipInfoRead = true;
                bool enteredCommandMode = await TryReadChipInfoSafeAsync(ct);
                
                if (enteredCommandMode)
                {
                    // 成功进入命令模式并读取了信息，已发送 SwitchMode
                    // 设备会重新发送 Hello，不要在这里发送 HelloResponse
                    _logDetail("[Sahara] 等待设备重新发送 Hello...");
                    return;
                }
            }

            // 发送 HelloResponse 进入传输模式
            _logDetail("[Sahara] 发送 HelloResponse (传输模式)");
            SendHelloResponse(SaharaMode.ImageTransferPending);
        }

        /// <summary>
        /// 处理 32 位读取请求
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
            
            // 调用进度回调（进度条显示，不需要日志）
            if (_progressCallback != null)
                _progressCallback(percent);
        }

        /// <summary>
        /// 处理 64 位读取请求
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
            
            // 调用进度回调（进度条显示，不需要日志）
            if (_progressCallback != null)
                _progressCallback(percent);
        }

        /// <summary>
        /// 处理镜像传输结束 (参考 tools 项目优化)
        /// </summary>
        private async Task<Tuple<bool, bool, int>> HandleEndImageTransferAsync(uint pktLen, int endImageTxCount, CancellationToken ct)
        {
            endImageTxCount++;
            
            if (endImageTxCount > 10) 
            {
                _log("[Sahara] 收到过多 EndImageTransfer 命令");
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
                _log(string.Format("[Sahara] ❌ 传输失败: {0}", SaharaStatusHelper.GetErrorMessage(status)));
                
                // [关键] 如果是 InvalidCommand，可能是命令模式导致的状态不同步
                // 下次连接时尝试跳过命令模式
                if (status == SaharaStatus.InvalidCommand)
                {
                    _log("[Sahara] 提示: 此错误通常由设备状态残留导致，重试时将自动恢复");
                }
                
                return Tuple.Create(false, false, endImageTxCount);
            }

            if (!_doneSent)
            {
                _logDetail("[Sahara] 镜像传输完成，发送 Done");
                SendDone();
                _doneSent = true;
            }

            return Tuple.Create(true, false, endImageTxCount);
        }

        /// <summary>
        /// 安全读取芯片信息 - 支持 V1/V2/V3 (参考 tools 项目优化)
        /// </summary>
        private async Task<bool> TryReadChipInfoSafeAsync(CancellationToken ct)
        {
            if (_skipCommandMode) 
            {
                _logDetail("[Sahara] 跳过命令模式");
                return false;
            }

            try
            {
                // 发送 HelloResponse 请求进入命令模式
                _logDetail(string.Format("[Sahara] 尝试进入命令模式 (v{0})...", ProtocolVersion));
                SendHelloResponse(SaharaMode.Command);

                // 等待响应
                var header = await ReadBytesAsync(8, 2000, ct);
                if (header == null) 
                {
                    _logDetail("[Sahara] 命令模式无响应");
                    return false;
                }

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)cmdId == SaharaCommand.CommandReady)
                {
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _logDetail("[Sahara] 设备接受命令模式");
                    
                    await ReadChipInfoCommandsAsync(ct);
                    
                    // 切换回传输模式
                    _logDetail("[Sahara] 切换回传输模式...");
                    SendSwitchMode(SaharaMode.ImageTransferPending);
                    await Task.Delay(50, ct);
                    return true;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.ReadData ||
                         (SaharaCommand)cmdId == SaharaCommand.ReadData64)
                {
                    // 设备拒绝命令模式，直接开始数据传输
                    _logDetail(string.Format("[Sahara] 设备拒绝命令模式 (v{0})", ProtocolVersion));
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.EndImageTransfer)
                {
                    // [关键修复] 设备可能处于残留状态，直接发送了 EndImageTransfer
                    // 这种情况下需要重置状态机
                    _logDetail("[Sahara] 设备状态异常 (收到 EndImageTransfer)，需要重置");
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else
                {
                    _logDetail(string.Format("[Sahara] 命令模式未知响应: 0x{0:X2}", cmdId));
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Sahara] 芯片信息读取失败: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 读取芯片信息 - V1/V2/V3 版本区分
        /// [关键] 所有命令失败时静默跳过，不影响握手
        /// </summary>
        private async Task ReadChipInfoCommandsAsync(CancellationToken ct)
        {
            // 1. 首先显示协议版本
            _log(string.Format("- Sahara version  : {0}", ProtocolVersion));
            
            // 2. 读取序列号 (cmd=0x01)
            var serialData = await ExecuteCommandSafeAsync(SaharaExecCommand.SerialNumRead, ct);
            if (serialData != null && serialData.Length >= 4)
            {
                uint serial = BitConverter.ToUInt32(serialData, 0);
                ChipSerial = serial.ToString("x8");
                ChipInfo.SerialHex = "0x" + ChipSerial.ToUpperInvariant();
                ChipInfo.SerialDec = serial;
                _log(string.Format("- Chip Serial Number : {0}", ChipSerial));
            }

            // 3. 读取 PK Hash (cmd=0x03)
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

            // 4. 读取 HWID - V1/V2 和 V3 使用不同的命令
            if (ProtocolVersion < 3)
            {
                // V1/V2: 使用 cmd=0x02 (MsmHwIdRead)
                var hwidData = await ExecuteCommandSafeAsync(SaharaExecCommand.MsmHwIdRead, ct);
                if (hwidData != null && hwidData.Length >= 8)
                    ProcessHwIdData(hwidData);
                    
                // V1/V2: 读取 SBL 版本 (cmd=0x07)，失败跳过
                var sblVer = await ExecuteCommandSafeAsync(SaharaExecCommand.SblSwVersion, ct);
                if (sblVer != null && sblVer.Length >= 4)
                {
                    uint version = BitConverter.ToUInt32(sblVer, 0);
                    _log(string.Format("- SBL SW Version : 0x{0:X8}", version));
                }
            }
            else
            {
                // V3: 尝试 cmd=0x0A，失败跳过
                var extInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.ChipIdV3Read, ct);
                if (extInfo != null && extInfo.Length >= 44)
                {
                    ProcessV3ExtendedInfo(extInfo);
                }
                else
                {
                    // cmd=0x0A 不支持，尝试从 PK Hash 推断厂商
                    if (!string.IsNullOrEmpty(ChipPkHash))
                    {
                        ChipInfo.Vendor = QualcommDatabase.GetVendorByPkHash(ChipPkHash);
                        if (!string.IsNullOrEmpty(ChipInfo.Vendor) && ChipInfo.Vendor != "Unknown")
                        {
                            _log(string.Format("- Vendor (by PK Hash) : {0}", ChipInfo.Vendor));
                        }
                    }
                }
                
                // V3: 读取 SBL 信息 (cmd=0x06)，失败跳过
                var sblInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.SblInfoRead, ct);
                if (sblInfo != null && sblInfo.Length >= 4)
                {
                    ProcessSblInfo(sblInfo);
                }
            }
            // 注: PBL 版本读取 (cmd=0x08) 已移除，部分设备不支持会导致握手失败
        }
        
        /// <summary>
        /// 处理 SBL 信息 (V3 专用, cmd=0x06)
        /// </summary>
        private void ProcessSblInfo(byte[] sblInfo)
        {
            // SBL Info 返回格式:
            // 偏移 0: Serial Number (4字节)
            // 偏移 4: MSM HW ID (8字节) - V3 可能包含
            // 偏移 12+: 其他扩展信息
            
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
            
            // 如果有更多数据，尝试解析 OEM 信息
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
        /// 在上传引导前显示芯片信息摘要
        /// 注: 详细信息已在 ReadChipInfoCommandsAsync 中输出
        /// </summary>
        private void LogChipInfoBeforeUpload()
        {
            if (ChipInfo == null) return;
            
            // 只输出摘要，详细信息已在读取时输出
            _logDetail("[Sahara] 芯片信息读取完成");
        }

        /// <summary>
        /// 处理 V1/V2 HWID 数据 (参考 tools 项目)
        /// </summary>
        private void ProcessHwIdData(byte[] hwidData)
        {
            ulong hwid = BitConverter.ToUInt64(hwidData, 0);
            ChipHwId = hwid.ToString("x16");
            ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

            // V1/V2 HWID 格式:
            // Bits 0-23:  MSM_ID (芯片ID)
            // Bits 32-47: OEM_ID (厂商ID)
            // Bits 48-63: MODEL_ID (型号ID)
            uint msmId = (uint)(hwid & 0xFFFFFF);
            ushort oemId = (ushort)((hwid >> 32) & 0xFFFF);
            ushort modelId = (ushort)((hwid >> 48) & 0xFFFF);

            ChipInfo.MsmId = msmId;
            ChipInfo.OemId = oemId;
            ChipInfo.ModelId = modelId;
            ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
            ChipInfo.Vendor = QualcommDatabase.GetVendorName(oemId);

            // 日志输出 (与 tools 项目格式对齐)
            _log(string.Format("- MSM HWID : 0x{0:x} | model_id:0x{1:x4} | oem_id:{2:X4} {3}",
                msmId, modelId, oemId, ChipInfo.Vendor));

            if (ChipInfo.ChipName != "Unknown")
                _log(string.Format("- CHIP : {0}", ChipInfo.ChipName));

            _log(string.Format("- HW_ID : {0}", ChipHwId));
        }

        /// <summary>
        /// [关键] 处理 V3 扩展信息 (cmd=0x0A 返回)
        /// 参考: tools 项目的标准实现
        /// V3 返回 84 字节数据:
        /// - 偏移 0: Chip Identifier V3 (4字节)
        /// - 偏移 36: MSM_ID (4字节)
        /// - 偏移 40: OEM_ID (2字节)
        /// - 偏移 42: MODEL_ID (2字节)
        /// - 偏移 44: 备用 OEM_ID (如果偏移40为0)
        /// </summary>
        private void ProcessV3ExtendedInfo(byte[] extInfo)
        {
            // 读取 Chip Identifier V3
            uint chipIdV3 = BitConverter.ToUInt32(extInfo, 0);
            if (chipIdV3 != 0)
            {
                _log(string.Format("- Chip Identifier V3 : {0:x8}", chipIdV3));
            }

            // V3 标准格式: 偏移 36-44
            if (extInfo.Length >= 44)
            {
                uint rawMsm = BitConverter.ToUInt32(extInfo, 36);
                ushort rawOem = BitConverter.ToUInt16(extInfo, 40);
                ushort rawModel = BitConverter.ToUInt16(extInfo, 42);

                uint msmId = rawMsm & 0x00FFFFFF;

                // 检查备用 OEM_ID 位置 (偏移44)
                if (rawOem == 0 && extInfo.Length >= 46)
                {
                    ushort altOemId = BitConverter.ToUInt16(extInfo, 44);
                    if (altOemId > 0 && altOemId < 0x1000)
                        rawOem = altOemId;
                }

                if (msmId != 0 || rawOem != 0)
                {
                    // 保存到 ChipInfo
                    ChipInfo.MsmId = msmId;
                    ChipInfo.OemId = rawOem;
                    ChipInfo.ModelId = rawModel;
                    ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
                    ChipInfo.Vendor = QualcommDatabase.GetVendorName(rawOem);

                    ChipHwId = string.Format("00{0:x6}{1:x4}{2:x4}", msmId, rawOem, rawModel).ToLower();
                    ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

                    // 日志输出 (与 tools 项目格式对齐)
                    _log(string.Format("- MSM HWID : 0x{0:x} | model_id:0x{1:x4} | oem_id:{2:X4} {3}",
                        msmId, rawModel, rawOem, ChipInfo.Vendor));

                    if (ChipInfo.ChipName != "Unknown")
                        _log(string.Format("- CHIP : {0}", ChipInfo.ChipName));

                    _log(string.Format("- HW_ID : {0}", ChipHwId));
                }
            }
        }

        /// <summary>
        /// 安全执行命令
        /// </summary>
        private async Task<byte[]> ExecuteCommandSafeAsync(SaharaExecCommand cmd, CancellationToken ct)
        {
            try
            {
                int timeout = cmd == SaharaExecCommand.SblInfoRead ? 5000 : 2000;

                // 发送 Execute
                var execPacket = new byte[12];
                WriteUInt32(execPacket, 0, (uint)SaharaCommand.Execute);
                WriteUInt32(execPacket, 4, 12);
                WriteUInt32(execPacket, 8, (uint)cmd);
                _port.Write(execPacket);

                // 读取响应头
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

                // 发送确认
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

        #region 发送方法

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
        /// 发送软复位命令 (ResetStateMachine) - 重置状态机，设备会重新发送 Hello
        /// </summary>
        public void SendReset()
        {
            var packet = new byte[8];
            WriteUInt32(packet, 0, (uint)SaharaCommand.ResetStateMachine);
            WriteUInt32(packet, 4, 8);
            _port.Write(packet);
        }
        
        /// <summary>
        /// 发送硬复位命令 (Reset) - 完全重启设备
        /// </summary>
        public void SendHardReset()
        {
            var packet = new byte[8];
            WriteUInt32(packet, 0, (uint)SaharaCommand.Reset);
            WriteUInt32(packet, 4, 8);
            _port.Write(packet);
        }
        
        /// <summary>
        /// 尝试重置卡住的 Sahara 状态
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功收到新的 Hello 包</returns>
        public async Task<bool> TryResetSaharaAsync(CancellationToken ct = default(CancellationToken))
        {
            _logDetail("[Sahara] 尝试重置 Sahara 状态...");
            
            // 方法 1: 发送 ResetStateMachine 命令
            _logDetail("[Sahara] 方法1: 发送 ResetStateMachine...");
            PurgeBuffer();
            SendReset();
            await Task.Delay(500, ct);
            
            // 检查是否收到新的 Hello
            var hello = await TryReadHelloAsync(2000, ct);
            if (hello != null)
            {
                _logDetail("[Sahara] ✓ 收到新的 Hello 包，状态已重置");
                return true;
            }
            
            // 方法 2: 发送 Hello Response 尝试重新同步
            _logDetail("[Sahara] 方法2: 发送 Hello Response 尝试重新同步...");
            PurgeBuffer();
            await SendHelloResponseAsync(2, 1, SaharaMode.ImageTransferPending, ct);
            await Task.Delay(300, ct);
            
            hello = await TryReadHelloAsync(2000, ct);
            if (hello != null)
            {
                _logDetail("[Sahara] ✓ 收到新的 Hello 包，状态已重置");
                return true;
            }
            
            // 方法 3: 端口信号重置 (DTR/RTS)
            _logDetail("[Sahara] 方法3: 端口信号重置...");
            try
            {
                _port.Close();
                await Task.Delay(200, ct);
                
                // 重新打开端口并清空缓冲区
                string portName = _port.PortName;
                if (!string.IsNullOrEmpty(portName))
                {
                    await _port.OpenAsync(portName, 3, true, ct);
                    await Task.Delay(500, ct);
                    
                    hello = await TryReadHelloAsync(3000, ct);
                    if (hello != null)
                    {
                        _logDetail("[Sahara] ✓ 端口重置后收到 Hello 包");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail("[Sahara] 端口重置异常: " + ex.Message);
            }
            
            _log("[Sahara] ❌ 无法重置 Sahara 状态，设备可能需要断电重启");
            return false;
        }
        
        /// <summary>
        /// 尝试读取 Hello 包 (用于检测状态重置)
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
        /// 发送 Hello Response
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

        #region 工具方法

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
            }
        }
    }
}
