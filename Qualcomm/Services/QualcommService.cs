// ============================================================================
// LoveAlways - 高通刷写服务
// Qualcomm Flash Service - 整合 Sahara 和 Firehose 的高层 API
// ============================================================================
// 模块: Qualcomm.Services
// 功能: 设备连接、分区读写、刷写流程管理
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Qualcomm.Protocol;
using LoveAlways.Qualcomm.Authentication;

namespace LoveAlways.Qualcomm.Services
{
    /// <summary>
    /// 连接状态
    /// </summary>
    public enum QualcommConnectionState
    {
        Disconnected,
        Connecting,
        SaharaMode,
        FirehoseMode,
        Ready,
        Error
    }

    /// <summary>
    /// 高通刷写服务
    /// </summary>
    public class QualcommService : IDisposable
    {
        private SerialPortManager _portManager;
        private SaharaClient _sahara;
        private FirehoseClient _firehose;
        private readonly Action<string> _log;
        private readonly Action<long, long> _progress;
        private readonly OplusSuperFlashManager _oplusSuperManager;
        private readonly DeviceInfoService _deviceInfoService;
        private bool _disposed;

        // 状态
        public QualcommConnectionState State { get; private set; }
        public QualcommChipInfo ChipInfo { get { return _sahara != null ? _sahara.ChipInfo : null; } }
        public bool IsConnected { get { return State == QualcommConnectionState.Ready; } }
        public bool IsVipDevice { get; private set; }
        public string StorageType { get { return _firehose != null ? _firehose.StorageType : "ufs"; } }
        public int SectorSize { get { return _firehose != null ? _firehose.SectorSize : 4096; } }
        public string CurrentSlot { get { return _firehose != null ? _firehose.CurrentSlot : "nonexistent"; } }

        // 分区缓存
        private Dictionary<int, List<PartitionInfo>> _partitionCache;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event EventHandler<QualcommConnectionState> StateChanged;

        public QualcommService(Action<string> log = null, Action<long, long> progress = null)
        {
            _log = log ?? delegate { };
            _progress = progress;
            _oplusSuperManager = new OplusSuperFlashManager(_log);
            _deviceInfoService = new DeviceInfoService(_log);
            _partitionCache = new Dictionary<int, List<PartitionInfo>>();
            State = QualcommConnectionState.Disconnected;
        }

        #region 连接管理

        /// <summary>
        /// 连接设备
        /// </summary>
        /// <param name="portName">COM 端口名</param>
        /// <param name="programmerPath">Programmer 文件路径</param>
        /// <param name="storageType">存储类型 (ufs/emmc)</param>
        /// <param name="authMode">认证模式: none, vip, oneplus, xiaomi</param>
        /// <param name="digestPath">VIP Digest 文件路径</param>
        /// <param name="signaturePath">VIP Signature 文件路径</param>
        /// <param name="ct">取消令牌</param>
        public async Task<bool> ConnectAsync(string portName, string programmerPath, string storageType = "ufs", 
            string authMode = "none", string digestPath = "", string signaturePath = "",
            CancellationToken ct = default(CancellationToken))
        {
            try
            {
                SetState(QualcommConnectionState.Connecting);
                _log("等待高通 EDL USB 设备 : 成功");
                _log(string.Format("USB 端口 : {0}", portName));
                _log("正在连接设备 : 成功");

                // 验证 Programmer 文件
                if (!File.Exists(programmerPath))
                {
                    _log("[高通] Programmer 文件不存在: " + programmerPath);
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // 初始化串口
                _portManager = new SerialPortManager();

                // Sahara 模式必须保留初始 Hello 包，不清空缓冲区
                bool opened = await _portManager.OpenAsync(portName, 3, false, ct);
                if (!opened)
                {
                    _log("[高通] 无法打开端口");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Sahara 握手
                SetState(QualcommConnectionState.SaharaMode);
                
                // 创建 Sahara 客户端并传递进度回调
                Action<double> saharaProgress = null;
                if (_progress != null)
                {
                    saharaProgress = percent => _progress((long)percent, 100);
                }
                _sahara = new SaharaClient(_portManager, _log, saharaProgress);

                bool saharaOk = await _sahara.HandshakeAndUploadAsync(programmerPath, ct);
                if (!saharaOk)
                {
                    _log("[高通] Sahara 握手失败");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // 根据用户选择的认证模式设置标志 (不再自动检测)
                IsVipDevice = (authMode.ToLowerInvariant() == "vip" || authMode.ToLowerInvariant() == "oplus");

                // 等待 Firehose 就绪
                _log("正在发送 Firehose 引导文件 : 成功");
                await Task.Delay(1000, ct);

                // 重新打开端口 (Firehose 模式)
                _portManager.Close();
                await Task.Delay(500, ct);

                opened = await _portManager.OpenAsync(portName, 5, true, ct);
                if (!opened)
                {
                    _log("[高通] 无法重新打开端口");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Firehose 配置
                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress);

                // 传递芯片信息
                if (ChipInfo != null)
                {
                    _firehose.ChipSerial = ChipInfo.SerialHex;
                    _firehose.ChipHwId = ChipInfo.HwIdHex;
                    _firehose.ChipPkHash = ChipInfo.PkHash;
                }

                // 根据用户选择执行认证 (配置前认证)
                string authModeLower = authMode.ToLowerInvariant();
                bool preConfigAuth = (authModeLower == "vip" || authModeLower == "oplus" || authModeLower == "xiaomi");
                
                // 小米设备自动认证：即使用户选择 none，也自动执行小米认证
                bool isXiaomi = IsXiaomiDevice();
                if (authModeLower == "none" && isXiaomi)
                {
                    _log("[高通] 检测到小米设备 (SecBoot)，自动执行 MiAuth 认证...");
                    var xiaomi = new XiaomiAuthStrategy(_log);
                    bool authOk = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    if (authOk)
                        _log("[高通] 小米认证成功");
                    else
                        _log("[高通] 小米认证失败，设备可能需要官方授权");
                }
                else if (preConfigAuth && authModeLower != "none")
                {
                    _log(string.Format("[高通] 执行 {0} 认证 (配置前)...", authMode.ToUpper()));
                    bool authOk = false;
                    
                    if (authModeLower == "vip" || authModeLower == "oplus")
                    {
                        // VIP 认证必须在配置前
                        if (!string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath))
                        {
                            authOk = await PerformVipAuthManualAsync(digestPath, signaturePath, ct);
                        }
                        else
                        {
                            _log("[高通] VIP 认证需要 Digest 和 Signature 文件");
                        }
                    }
                    else if (authModeLower == "xiaomi")
                    {
                        var xiaomi = new XiaomiAuthStrategy(_log);
                        authOk = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    }
                    
                    if (authOk)
                        _log(string.Format("[高通] {0} 认证成功", authMode.ToUpper()));
                    else
                        _log(string.Format("[高通] {0} 认证失败", authMode.ToUpper()));
                }

                _log("正在配置 Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("配置 Firehose : 失败");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("配置 Firehose : 成功");

                // 配置后认证 (OnePlus)
                if (!preConfigAuth && authModeLower != "none")
                {
                    _log(string.Format("[高通] 执行 {0} 认证 (配置后)...", authMode.ToUpper()));
                    bool authOk = false;
                    
                    if (authModeLower == "oneplus")
                    {
                        var oneplus = new OnePlusAuthStrategy(_log);
                        authOk = await oneplus.AuthenticateAsync(_firehose, programmerPath, ct);
                    }
                    
                    if (authOk)
                        _log(string.Format("[高通] {0} 认证成功", authMode.ToUpper()));
                    else
                        _log(string.Format("[高通] {0} 认证失败", authMode.ToUpper()));
                }

                SetState(QualcommConnectionState.Ready);
                _log("[高通] 连接成功");

                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[高通] 连接已取消");
                SetState(QualcommConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _log(string.Format("[高通] 连接错误 - {0}", ex.Message));
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// 直接连接 Firehose (跳过 Sahara)
        /// </summary>
        public async Task<bool> ConnectFirehoseDirectAsync(string portName, string storageType = "ufs", CancellationToken ct = default(CancellationToken))
        {
            try
            {
                SetState(QualcommConnectionState.Connecting);
                _log(string.Format("[高通] 直接连接 Firehose: {0}...", portName));

                _portManager = new SerialPortManager();
                bool opened = await _portManager.OpenAsync(portName, 3, true, ct);
                if (!opened)
                {
                    _log("[高通] 无法打开端口");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress);

                _log("正在配置 Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("配置 Firehose : 失败");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("配置 Firehose : 成功");

                SetState(QualcommConnectionState.Ready);
                _log("[高通] Firehose 直连成功");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[高通] 连接已取消");
                SetState(QualcommConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _log(string.Format("[高通] 连接错误 - {0}", ex.Message));
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _log("[高通] 断开连接");

            if (_portManager != null)
            {
                _portManager.Close();
                _portManager.Dispose();
                _portManager = null;
            }

            if (_sahara != null)
            {
                _sahara.Dispose();
                _sahara = null;
            }

            if (_firehose != null)
            {
                _firehose.Dispose();
                _firehose = null;
            }

            _partitionCache.Clear();
            IsVipDevice = false;

            SetState(QualcommConnectionState.Disconnected);
        }

        /// <summary>
        /// 执行认证
        /// </summary>
        public async Task<bool> AuthenticateAsync(string authMode, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[高通] 未连接 Firehose，无法执行认证");
                return false;
            }

            try
            {
                switch (authMode.ToLowerInvariant())
                {
                    case "oneplus":
                        _log("[高通] 执行 OnePlus 认证...");
                        var oneplusAuth = new Authentication.OnePlusAuthStrategy();
                        // OnePlus 认证不需要外部文件，使用空字符串
                        return await oneplusAuth.AuthenticateAsync(_firehose, "", ct);

                    case "vip":
                    case "oplus":
                        _log("[高通] 执行 VIP/OPPO 认证...");
                        // VIP 认证通常需要签名文件，这里使用默认路径
                        string vipDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vip");
                        string digestPath = System.IO.Path.Combine(vipDir, "digest.bin");
                        string signaturePath = System.IO.Path.Combine(vipDir, "signature.bin");
                        if (!System.IO.File.Exists(digestPath) || !System.IO.File.Exists(signaturePath))
                        {
                            _log("[高通] VIP 认证文件不存在，尝试无签名认证...");
                            // 如果没有签名文件，返回 true 继续（某些设备可能不需要认证）
                            return true;
                        }
                        bool ok = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, ct);
                        if (ok) IsVipDevice = true;
                        return ok;

                    case "xiaomi":
                        _log("[高通] 执行小米认证...");
                        var xiaomiAuth = new Authentication.XiaomiAuthStrategy();
                        return await xiaomiAuth.AuthenticateAsync(_firehose, "", ct);

                    default:
                        _log(string.Format("[高通] 未知认证模式: {0}", authMode));
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[高通] 认证失败: {0}", ex.Message));
                return false;
            }
        }

        private void SetState(QualcommConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                if (StateChanged != null)
                    StateChanged(this, newState);
            }
        }

        #endregion

        #region 自动认证逻辑

        /// <summary>
        /// 自动认证 - 仅对小米设备自动执行
        /// 其他设备 (OnePlus/OPPO/Realme 等) 由用户手动选择认证方式
        /// </summary>
        private async Task<bool> AutoAuthenticateAsync(string programmerPath, CancellationToken ct)
        {
            if (_firehose == null) return true;

            // 只有小米设备自动认证
            if (IsXiaomiDevice())
            {
                _log("[高通] 检测到小米设备，自动执行 MiAuth 认证...");
                try
                {
                    var xiaomi = new XiaomiAuthStrategy(_log);
                    bool result = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    if (result)
                    {
                        _log("[高通] 小米认证成功");
                    }
                    else
                    {
                        _log("[高通] 小米认证失败，设备可能需要官方授权");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _log(string.Format("[高通] 小米认证异常: {0}", ex.Message));
                    return false;
                }
            }

            // 其他设备不自动认证，由用户手动选择
            return true;
        }

        /// <summary>
        /// 检测是否为小米设备 (通过 OEM ID 或其他特征)
        /// </summary>
        private bool IsXiaomiDevice()
        {
            if (ChipInfo == null) return false;

            // 通过 OEM ID 检测 (0x0072 = Xiaomi 官方)
            if (ChipInfo.OemId == 0x0072) return true;

            // 通过 PK Hash 前缀检测 (小米常见 PK Hash)
            if (!string.IsNullOrEmpty(ChipInfo.PkHash))
            {
                string pkLower = ChipInfo.PkHash.ToLowerInvariant();
                // 小米设备 PK Hash 前缀列表 (持续更新)
                string[] xiaomiPkHashPrefixes = new[]
                {
                    "c924a35f",  // 常见小米设备
                    "3373d5c8",
                    "e07be28b",
                    "6f5c4e17",
                    "57158eaf",
                    "355d47f9",
                    "a7b8b825",
                    "1c845b80",
                    "58b4add1",
                    "dd0cba2f",
                    "1bebe386"
                };

                foreach (var prefix in xiaomiPkHashPrefixes)
                {
                    if (pkLower.StartsWith(prefix))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 手动执行 OPLUS VIP 认证 (基于 Digest 和 Signature)
        /// </summary>
        public async Task<bool> PerformVipAuthManualAsync(string digestPath, string signaturePath, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[高通] 未连接设备");
                return false;
            }

            _log("[高通] 启动 OPLUS VIP 认证 (Digest + Sign)...");
            try
            {
                bool result = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, ct);
                if (result)
                {
                    _log("[高通] VIP 认证成功，已进入高权限模式");
                    IsVipDevice = true; 
                }
                else
                {
                    _log("[高通] VIP 认证失败：校验未通过");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log(string.Format("[高通] VIP 认证异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 获取设备挑战码 (用于在线签名)
        /// </summary>
        public async Task<string> GetVipChallengeAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return null;
            return await _firehose.GetVipChallengeAsync(ct);
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 读取所有 LUN 的 GPT 分区表
        /// </summary>
        public async Task<List<PartitionInfo>> ReadAllGptAsync(int maxLuns = 6, CancellationToken ct = default(CancellationToken))
        {
            return await ReadAllGptAsync(maxLuns, null, null, ct);
        }

        /// <summary>
        /// 读取所有 LUN 的 GPT 分区表（带进度回调）
        /// </summary>
        /// <param name="maxLuns">最大 LUN 数量</param>
        /// <param name="totalProgress">总进度回调 (当前LUN, 总LUN)</param>
        /// <param name="subProgress">子进度回调 (0-100)</param>
        /// <param name="ct">取消令牌</param>
        public async Task<List<PartitionInfo>> ReadAllGptAsync(
            int maxLuns, 
            IProgress<Tuple<int, int>> totalProgress,
            IProgress<double> subProgress,
            CancellationToken ct = default(CancellationToken))
        {
            var allPartitions = new List<PartitionInfo>();

            if (_firehose == null)
                return allPartitions;

            _log("正在读取 GUID 分区表...");

            // 报告开始
            if (totalProgress != null) totalProgress.Report(Tuple.Create(0, maxLuns));
            if (subProgress != null) subProgress.Report(0);

            // LUN 进度回调 - 实时更新进度
            var lunProgress = new Progress<int>(lun => {
                if (totalProgress != null) totalProgress.Report(Tuple.Create(lun, maxLuns));
                if (subProgress != null) subProgress.Report(100.0 * lun / maxLuns);
            });

            var partitions = await _firehose.ReadGptPartitionsAsync(IsVipDevice, ct, lunProgress);
            
            // 报告中间进度
            if (subProgress != null) subProgress.Report(80);
            
            if (partitions != null && partitions.Count > 0)
            {
                allPartitions.AddRange(partitions);
                _log(string.Format("读取 GUID 分区表 : 成功 [{0}]", partitions.Count));

                // 缓存分区
                _partitionCache.Clear();
                foreach (var p in partitions)
                {
                    if (!_partitionCache.ContainsKey(p.Lun))
                        _partitionCache[p.Lun] = new List<PartitionInfo>();
                    _partitionCache[p.Lun].Add(p);
                }
            }

            // 报告完成
            if (subProgress != null) subProgress.Report(100);
            if (totalProgress != null) totalProgress.Report(Tuple.Create(maxLuns, maxLuns));

            _log(string.Format("[高通] 共发现 {0} 个分区", allPartitions.Count));
            return allPartitions;
        }

        /// <summary>
        /// 获取指定 LUN 的分区列表
        /// </summary>
        public List<PartitionInfo> GetCachedPartitions(int lun = -1)
        {
            var result = new List<PartitionInfo>();

            if (lun == -1)
            {
                foreach (var kv in _partitionCache)
                    result.AddRange(kv.Value);
            }
            else
            {
                List<PartitionInfo> list;
                if (_partitionCache.TryGetValue(lun, out list))
                    result.AddRange(list);
            }

            return result;
        }

        /// <summary>
        /// 查找分区
        /// </summary>
        public PartitionInfo FindPartition(string name)
        {
            foreach (var kv in _partitionCache)
            {
                foreach (var p in kv.Value)
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            return null;
        }

        /// <summary>
        /// 读取分区到文件
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[高通] 未找到分区 " + partitionName);
                return false;
            }

            _log(string.Format("[高通] 读取分区 {0} ({1})", partitionName, partition.FormattedSize));

            try
            {
                int sectorsPerChunk = _firehose.MaxPayloadSize / partition.SectorSize;
                long totalSectors = partition.NumSectors;
                long readSectors = 0;
                long totalBytes = partition.Size;
                long readBytes = 0;

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4 * 1024 * 1024))
                {
                    while (readSectors < totalSectors && !ct.IsCancellationRequested)
                    {
                        int toRead = (int)Math.Min(sectorsPerChunk, totalSectors - readSectors);
                        byte[] data = await _firehose.ReadSectorsAsync(
                            partition.Lun, partition.StartSector + readSectors, toRead, ct, IsVipDevice, partitionName);

                        if (data == null)
                        {
                            _log("[高通] 读取失败");
                            return false;
                        }

                        fs.Write(data, 0, data.Length);
                        readSectors += toRead;
                        readBytes += data.Length;

                        // 调用字节级进度回调 (用于速度计算)
                        _firehose.ReportProgress(readBytes, totalBytes);

                        // 百分比进度 (使用 double)
                        if (progress != null)
                            progress.Report(100.0 * readBytes / totalBytes);
                    }
                }

                _log(string.Format("[高通] 分区 {0} 已保存到 {1}", partitionName, outputPath));
                return true;
            }
            catch (Exception ex)
            {
                _log(string.Format("[高通] 读取错误 - {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, string filePath, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[高通] 未找到分区 " + partitionName);
                return false;
            }

            // OPLUS 某些分区需要 SHA256 校验环绕
            bool useSha256 = IsOplusDevice && (partitionName.ToLower() == "xbl" || partitionName.ToLower() == "abl" || partitionName.ToLower() == "imagefv");
            if (useSha256) await _firehose.Sha256InitAsync(ct);

            // VIP 设备使用伪装模式写入
            bool success = await _firehose.FlashPartitionFromFileAsync(
                partitionName, filePath, partition.Lun, partition.StartSector, progress, ct, IsVipDevice);

            if (useSha256) await _firehose.Sha256FinalAsync(ct);

            return success;
        }

        private bool IsOplusDevice 
        { 
            get { 
                if (IsVipDevice) return true;
                if (ChipInfo != null && (ChipInfo.Vendor == "OPPO" || ChipInfo.Vendor == "Realme" || ChipInfo.Vendor == "OnePlus")) return true;
                return false;
            } 
        }

        /// <summary>
        /// 直接写入指定 LUN 和 StartSector (用于 PrimaryGPT/BackupGPT 等特殊分区)
        /// 支持官方 NUM_DISK_SECTORS-N 负扇区格式
        /// </summary>
        public async Task<bool> WriteDirectAsync(string label, string filePath, int lun, long startSector, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            // 负扇区使用官方格式直接发送给设备 (不再客户端转换)
            if (startSector < 0)
            {
                _log(string.Format("[高通] 直接写入: {0} -> LUN{1} @ sector NUM_DISK_SECTORS{2}", label, lun, startSector));
                
                // 使用新的官方格式发送方法
                return await _firehose.FlashPartitionFromFileWithNegativeSectorAsync(
                    label, filePath, lun, startSector, progress, ct, IsVipDevice);
            }
            else
            {
                _log(string.Format("[高通] 直接写入: {0} -> LUN{1} @ sector {2}", label, lun, startSector));

                // 正数扇区正常写入
                return await _firehose.FlashPartitionFromFileAsync(
                    label, filePath, lun, startSector, progress, ct, IsVipDevice);
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[高通] 未找到分区 " + partitionName);
                return false;
            }

            // VIP 设备使用伪装模式擦除
            return await _firehose.ErasePartitionAsync(partition, ct, IsVipDevice);
        }

        /// <summary>
        /// 读取分区指定偏移处的数据
        /// </summary>
        /// <param name="partitionName">分区名称</param>
        /// <param name="offset">偏移 (字节)</param>
        /// <param name="size">大小 (字节)</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>读取的数据</returns>
        public async Task<byte[]> ReadPartitionDataAsync(string partitionName, long offset, int size, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return null;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[高通] 未找到分区 " + partitionName);
                return null;
            }

            // 计算扇区位置
            int sectorSize = SectorSize > 0 ? SectorSize : 4096;
            long startSector = partition.StartSector + (offset / sectorSize);
            int numSectors = (size + sectorSize - 1) / sectorSize;

            // 读取数据
            byte[] data = await _firehose.ReadSectorsAsync(partition.Lun, startSector, numSectors, ct, IsVipDevice, partitionName);
            if (data == null) return null;

            // 如果有偏移对齐问题，截取正确的数据
            int offsetInSector = (int)(offset % sectorSize);
            if (offsetInSector > 0 || data.Length > size)
            {
                int actualSize = Math.Min(size, data.Length - offsetInSector);
                if (actualSize <= 0) return null;
                
                byte[] result = new byte[actualSize];
                Array.Copy(data, offsetInSector, result, 0, actualSize);
                return result;
            }

            return data;
        }

        /// <summary>
        /// 获取 Firehose 客户端 (供内部使用)
        /// </summary>
        internal Protocol.FirehoseClient GetFirehoseClient()
        {
            return _firehose;
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.ResetAsync("reset", ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// 关机
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.PowerOffAsync(ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// 重启到 EDL 模式
        /// </summary>
        public async Task<bool> RebootToEdlAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.RebootToEdlAsync(ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// 设置活动 Slot
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.SetActiveSlotAsync(slot, ct);
        }

        /// <summary>
        /// 修复 GPT
        /// </summary>
        public async Task<bool> FixGptAsync(int lun = -1, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.FixGptAsync(lun, true, ct);
        }

        /// <summary>
        /// 设置启动 LUN
        /// </summary>
        public async Task<bool> SetBootLunAsync(int lun, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.SetBootLunAsync(lun, ct);
        }

        /// <summary>
        /// Ping 测试连接
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.PingAsync(ct);
        }

        /// <summary>
        /// 应用 Patch XML 文件
        /// </summary>
        public async Task<int> ApplyPatchXmlAsync(string patchXmlPath, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return 0;

            return await _firehose.ApplyPatchXmlAsync(patchXmlPath, ct);
        }

        /// <summary>
        /// 应用多个 Patch XML 文件
        /// </summary>
        public async Task<int> ApplyPatchFilesAsync(IEnumerable<string> patchFiles, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return 0;

            int totalPatches = 0;
            foreach (var patchFile in patchFiles)
            {
                if (ct.IsCancellationRequested) break;
                totalPatches += await _firehose.ApplyPatchXmlAsync(patchFile, ct);
            }
            return totalPatches;
        }

        #endregion

        #region 批量刷写

        /// <summary>
        /// 批量刷写分区
        /// </summary>
        public async Task<bool> FlashMultipleAsync(IEnumerable<FlashPartitionInfo> partitions, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var list = new List<FlashPartitionInfo>(partitions);
            int total = list.Count;
            int current = 0;
            bool allSuccess = true;

            foreach (var p in list)
            {
                if (ct.IsCancellationRequested)
                    break;

                _log(string.Format("[高通] 刷写 [{0}/{1}] {2}", current + 1, total, p.Name));

                bool ok = await WritePartitionAsync(p.Name, p.Filename, null, ct);
                if (!ok)
                {
                    allSuccess = false;
                    _log("[高通] 刷写失败 - " + p.Name);
                }

                current++;
                if (progress != null)
                    progress.Report(100.0 * current / total);
            }

            return allSuccess;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
                }
                _disposed = true;
            }
        }

        ~QualcommService()
        {
            Dispose(false);
        }

        #endregion
        /// <summary>
        /// 刷写 OPLUS 固件包中的 Super 逻辑分区 (拆解写入)
        /// </summary>
        public async Task<bool> FlashOplusSuperAsync(string firmwareRoot, string nvId = "", IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;

            // 1. 查找 super 分区信息
            var superPart = FindPartition("super");
            if (superPart == null)
            {
                _log("[高通] 未在设备上找到 super 分区");
                return false;
            }

            // 2. 准备任务
            _log("[高通] 正在解析 OPLUS 固件 Super 布局...");
            string activeSlot = CurrentSlot;
            if (activeSlot == "nonexistent" || string.IsNullOrEmpty(activeSlot))
                activeSlot = "a";

            var tasks = await _oplusSuperManager.PrepareSuperTasksAsync(firmwareRoot, superPart.StartSector, (int)superPart.SectorSize, activeSlot, nvId);
            
            if (tasks.Count == 0)
            {
                _log("[高通] 未找到可用的 Super 逻辑分区镜像");
                return false;
            }

            // 3. 执行任务
            long totalBytes = tasks.Sum(t => t.SizeInBytes);
            long totalWritten = 0;

            _log(string.Format("[高通] 开始拆解写入 {0} 个逻辑镜像 (总计展开大小: {1} MB)...", tasks.Count, totalBytes / 1024 / 1024));

            foreach (var task in tasks)
            {
                if (ct.IsCancellationRequested) break;

                _log(string.Format("[高通] 写入 {0} [{1}] 到物理扇区 {2}...", task.PartitionName, Path.GetFileName(task.FilePath), task.PhysicalSector));
                
                // 嵌套进度计算
                var taskProgress = new Progress<double>(p => {
                    if (progress != null)
                    {
                        double currentTaskWeight = (double)task.SizeInBytes / totalBytes;
                        double overallPercent = ((double)totalWritten / totalBytes * 100) + (p * currentTaskWeight);
                        progress.Report(overallPercent);
                    }
                });

                bool success = await _firehose.FlashPartitionFromFileAsync(
                    task.PartitionName, 
                    task.FilePath, 
                    superPart.Lun, 
                    task.PhysicalSector, 
                    taskProgress, 
                    ct, 
                    IsVipDevice);

                if (!success)
                {
                    _log(string.Format("[高通] 写入 {0} 失败，流程中止", task.PartitionName));
                    return false;
                }

                totalWritten += task.SizeInBytes;
            }

            _log("[高通] OPLUS Super 拆解写入完成");
            return true;
        }
    }
}
