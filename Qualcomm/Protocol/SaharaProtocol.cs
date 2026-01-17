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

        // 传输进度
        private long _totalSent = 0;
        private long _lastProgressLog = 0;

        public SaharaClient(SerialPortManager port, Action<string> log = null)
        {
            _port = port;
            _log = log ?? delegate { };
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
        /// 握手并上传 Loader
        /// </summary>
        public async Task<bool> HandshakeAndUploadAsync(string loaderPath, CancellationToken ct = default(CancellationToken))
        {
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("引导 文件不存在", loaderPath);

            byte[] fileBytes = File.ReadAllBytes(loaderPath);
            _log(string.Format("[Sahara] 加载 引导: {0} ({1} KB)", Path.GetFileName(loaderPath), fileBytes.Length / 1024));

            _log("[Sahara] 等待设备 Hello 包...");

            return await HandshakeAndLoadInternalAsync(fileBytes, ct);
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
            _lastProgressLog = 0;
            var sw = Stopwatch.StartNew();

            while (!done && loopGuard++ < 1000)
            {
                if (ct.IsCancellationRequested)
                    return false;

                byte[] header = null;

                // 检查是否有预读取的 Hello 数据
                if (loopGuard == 1 && _pendingHelloData != null && _pendingHelloData.Length >= 8)
                {
                    _log(string.Format("[Sahara] 使用预读取的 Hello 数据 ({0} 字节)", _pendingHelloData.Length));
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
                    int available = _port.BytesToRead;
                    _log(string.Format("[Sahara] 读取超时 ({0}/5)，缓冲区: {1} 字节", timeoutCount, available));

                    if (timeoutCount >= 5)
                    {
                        _log("[Sahara] 多次读取超时，设备可能未响应");
                    
                        return false;
                    }

                    if (available > 0)
                    {
                        var partial = await ReadBytesAsync(available, 1000, ct);
                        if (partial != null)
                        {
                            _log(string.Format("[Sahara] 部分数据: {0}", BitConverter.ToString(partial, 0, Math.Min(16, partial.Length))));
                        }
                    }

                    await Task.Delay(500, ct);
                    continue;
                }

                timeoutCount = 0;
                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if (cmdId != (uint)SaharaCommand.ReadData && cmdId != (uint)SaharaCommand.ReadData64)
                {
                    _log(string.Format("[Sahara] 收到: Cmd=0x{0:X2} ({1}), Len={2}", cmdId, (SaharaCommand)cmdId, pktLen));
                }

                if (pktLen < 8 || pktLen > MAX_BUFFER_SIZE * 4)
                {
                    _log(string.Format("[Sahara] 异常包: CmdId=0x{0:X2}, Len={1}", cmdId, pktLen));
                    PurgeBuffer();
                    await Task.Delay(50, ct);
                    continue;
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
                        _log("[Sahara] 引导 加载成功");
                        done = true;
                        IsConnected = true;
                        break;

                    case SaharaCommand.CommandReady:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log("[Sahara] 收到 CmdReady，切换到传输模式");
                        SendSwitchMode(SaharaMode.ImageTransferPending);
                        break;

                    default:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log(string.Format("[Sahara] 未知命令: 0x{0:X2}", cmdId));
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
        /// 处理 Hello 包
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
            _log(string.Format("[Sahara] 收到 HELLO (版本={0}, 模式={1})", ProtocolVersion, deviceMode));

            // 尝试读取芯片信息
            if (!_chipInfoRead && deviceMode == (uint)SaharaMode.ImageTransferPending)
            {
                _chipInfoRead = true;
                bool enteredCommandMode = await TryReadChipInfoSafeAsync(ct);

                if (enteredCommandMode)
                {
                    _log("[Sahara] 等待设备重新发送 Hello...");
                    return;
                }
            }

            _log("[Sahara] 发送 HelloResponse (传输模式)");
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

            if (offset + length > fileBytes.Length)
            {
                _log(string.Format("[Sahara] 请求越界: offset={0}, length={1}", offset, length));
                return;
            }

            _port.Write(fileBytes, (int)offset, (int)length);

            _totalSent += length;
            if (_totalSent - _lastProgressLog > 100 * 1024)
            {
                int percent = (int)(_totalSent * 100 / fileBytes.Length);
                _log(string.Format("[Sahara] 传输进度: {0} KB / {1} KB ({2}%)", _totalSent / 1024, fileBytes.Length / 1024, percent));
                _lastProgressLog = _totalSent;
            }
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

            if ((long)offset + (long)length > fileBytes.Length)
            {
                _log(string.Format("[Sahara] 64位请求越界: offset={0}, length={1}", offset, length));
                return;
            }

            _port.Write(fileBytes, (int)offset, (int)length);

            _totalSent += (long)length;
            if (_totalSent - _lastProgressLog > 100 * 1024)
            {
                int percent = (int)(_totalSent * 100 / fileBytes.Length);
                _log(string.Format("[Sahara] 传输进度: {0} KB / {1} KB ({2}%)", _totalSent / 1024, fileBytes.Length / 1024, percent));
                _lastProgressLog = _totalSent;
            }
        }

        /// <summary>
        /// 处理镜像传输结束
        /// </summary>
        private async Task<Tuple<bool, bool, int>> HandleEndImageTransferAsync(uint pktLen, int endImageTxCount, CancellationToken ct)
        {
            endImageTxCount++;

            if (endImageTxCount > 10)
            {
                _log("[Sahara] 收到过多 EndImageTx 命令");
                return Tuple.Create(false, false, endImageTxCount);
            }

            uint endStatus = 0;
            if (pktLen >= 16)
            {
                var body = await ReadBytesAsync(8, 5000, ct);
                if (body != null)
                {
                    endStatus = BitConverter.ToUInt32(body, 4);
                }
            }

            if (endStatus != 0)
            {
                var status = (SaharaStatus)endStatus;
                _log(string.Format("[Sahara] 传输失败: {0}", SaharaStatusHelper.GetErrorMessage(status)));
                return Tuple.Create(false, false, endImageTxCount);
            }

            if (!_doneSent)
            {
                _log("[Sahara] 镜像传输完成，发送 Done");
                SendDone();
                _doneSent = true;
            }

            return Tuple.Create(true, false, endImageTxCount);
        }

        /// <summary>
        /// 安全读取芯片信息 - 支持 V1/V2/V3
        /// </summary>
        private async Task<bool> TryReadChipInfoSafeAsync(CancellationToken ct)
        {
            if (_skipCommandMode)
            {
                _log("[Sahara] 跳过命令模式");
                return false;
            }

            try
            {
                _log(string.Format("[Sahara] 尝试进入命令模式 (v{0})...", ProtocolVersion));
                SendHelloResponse(SaharaMode.Command);

                var header = await ReadBytesAsync(8, 2000, ct);
                if (header == null)
                {
                    _log("[Sahara] 命令模式无响应");
                    return false;
                }

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)cmdId == SaharaCommand.CommandReady)
                {
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _log("[Sahara] 设备接受命令模式");

                    await ReadChipInfoCommandsAsync(ct);

                    SendSwitchMode(SaharaMode.ImageTransferPending);
                    await Task.Delay(50, ct);

                    return true;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.ReadData ||
                         (SaharaCommand)cmdId == SaharaCommand.ReadData64)
                {
                    _log(string.Format("[Sahara] 设备拒绝命令模式 (v{0})", ProtocolVersion));
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else
                {
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
        /// </summary>
        private async Task ReadChipInfoCommandsAsync(CancellationToken ct)
        {
            _log(string.Format("- Sahara version  : {0}", ProtocolVersion));

            // 1. 读取序列号
            var serialData = await ExecuteCommandSafeAsync(SaharaExecCommand.SerialNumRead, ct);
            if (serialData != null && serialData.Length >= 4)
            {
                uint serial = BitConverter.ToUInt32(serialData, 0);
                ChipSerial = serial.ToString("x8");
                ChipInfo.SerialHex = "0x" + ChipSerial.ToUpperInvariant();
                ChipInfo.SerialDec = serial;
                _log(string.Format("- Chip Serial Number : {0}", ChipSerial));
            }

            // 2. 读取 HWID - V3 和 V1/V2 不同
            if (ProtocolVersion < 3)
            {
                var hwidData = await ExecuteCommandSafeAsync(SaharaExecCommand.MsmHwIdRead, ct);
                if (hwidData != null && hwidData.Length >= 8)
                {
                    ProcessHwIdData(hwidData);
                }
            }
            else
            {
                _log("[Sahara] V3 协议，使用 cmd=0x0A 读取芯片信息");
            }

            // 3. 读取 PK Hash
            var pkhash = await ExecuteCommandSafeAsync(SaharaExecCommand.OemPkHashRead, ct);
            if (pkhash != null && pkhash.Length > 0)
            {
                int hashLen = Math.Min(pkhash.Length, 48);
                ChipPkHash = BitConverter.ToString(pkhash, 0, hashLen).Replace("-", "").ToLower();
                ChipInfo.PkHash = ChipPkHash;
                ChipInfo.PkHashInfo = QualcommDatabase.GetPkHashInfo(ChipPkHash);
                _log(string.Format("- OEM PKHASH : {0}", ChipPkHash));

                var pkInfo = QualcommDatabase.GetPkHashInfo(ChipPkHash);
                if (pkInfo != "Unknown" && pkInfo != "Custom OEM")
                {
                    _log(string.Format("- SecBoot : {0}", pkInfo));
                }
            }

            // 4. V3 专用: 读取扩展信息
            if (string.IsNullOrEmpty(ChipHwId) || ProtocolVersion >= 3)
            {
                var extInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.ChipIdV3Read, ct);
                if (extInfo != null && extInfo.Length >= 44)
                {
                    ProcessV3ExtendedInfo(extInfo);
                }
            }
        }

        /// <summary>
        /// 处理 V1/V2 HWID 数据
        /// </summary>
        private void ProcessHwIdData(byte[] hwidData)
        {
            ulong hwid = BitConverter.ToUInt64(hwidData, 0);
            ChipHwId = hwid.ToString("x16");
            ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

            uint msmId = (uint)(hwid & 0xFFFFFF);
            ushort oemId = (ushort)((hwid >> 32) & 0xFFFF);
            ushort modelId = (ushort)((hwid >> 48) & 0xFFFF);

            ChipInfo.MsmId = msmId;
            ChipInfo.OemId = oemId;
            ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
            ChipInfo.Vendor = QualcommDatabase.GetVendorName(oemId);

            _log(string.Format("- MSM HWID : 0x{0:x} | model_id:0x{1:x4} | oem_id:{2:X4} {3}", 
                msmId, modelId, oemId, ChipInfo.Vendor));

            if (ChipInfo.ChipName != "Unknown")
                _log(string.Format("- CHIP : {0}", ChipInfo.ChipName));

            _log(string.Format("- HW_ID : {0}", ChipHwId));
        }

        /// <summary>
        /// 处理 V3 扩展信息
        /// </summary>
        private void ProcessV3ExtendedInfo(byte[] extInfo)
        {
            uint chipIdV3 = BitConverter.ToUInt32(extInfo, 0);
            if (chipIdV3 != 0)
                _log(string.Format("- Chip Identifier V3 : {0:x8}", chipIdV3));

            if (extInfo.Length >= 44)
            {
                uint rawMsm = BitConverter.ToUInt32(extInfo, 36);
                ushort rawOem = BitConverter.ToUInt16(extInfo, 40);
                ushort rawModel = BitConverter.ToUInt16(extInfo, 42);

                uint msmId = rawMsm & 0x00FFFFFF;

                if (rawOem == 0 && extInfo.Length >= 46)
                {
                    ushort altOemId = BitConverter.ToUInt16(extInfo, 44);
                    if (altOemId > 0 && altOemId < 0x1000)
                        rawOem = altOemId;
                }

                if (msmId != 0 || rawOem != 0)
                {
                    ChipInfo.MsmId = msmId;
                    ChipInfo.OemId = rawOem;
                    ChipInfo.ChipName = QualcommDatabase.GetChipName(msmId);
                    ChipInfo.Vendor = QualcommDatabase.GetVendorName(rawOem);

                    ChipHwId = string.Format("00{0:x6}{1:x4}{2:x4}", msmId, rawOem, rawModel).ToLower();
                    ChipInfo.HwIdHex = "0x" + ChipHwId.ToUpperInvariant();

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
        /// 发送复位命令
        /// </summary>
        public void SendReset()
        {
            var packet = new byte[8];
            WriteUInt32(packet, 0, (uint)SaharaCommand.ResetStateMachine);
            WriteUInt32(packet, 4, 8);
            _port.Write(packet);
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
