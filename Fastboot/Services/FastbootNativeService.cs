using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Fastboot.Image;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Protocol;
using LoveAlways.Fastboot.Transport;

namespace LoveAlways.Fastboot.Services
{
    /// <summary>
    /// Fastboot 原生服务
    /// 使用纯 C# 实现的 Fastboot 协议，不依赖外部 fastboot.exe
    /// 
    /// 优势：
    /// - 实时进度百分比回调
    /// - 完全控制传输过程
    /// - 无需外部依赖
    /// - 更好的错误处理
    /// </summary>
    public class FastbootNativeService : IDisposable
    {
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        
        private FastbootClient _client;
        private bool _disposed;
        
        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;
        
        /// <summary>
        /// 当前设备序列号
        /// </summary>
        public string CurrentSerial => _client?.Serial;
        
        /// <summary>
        /// 设备信息
        /// </summary>
        public FastbootDeviceInfo DeviceInfo { get; private set; }
        
        /// <summary>
        /// 进度更新事件
        /// </summary>
        public event EventHandler<FastbootNativeProgressEventArgs> ProgressChanged;
        
        public FastbootNativeService(Action<string> log, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _logDetail = logDetail ?? (msg => { });
        }
        
        #region 设备操作
        
        /// <summary>
        /// 获取所有 Fastboot 设备
        /// </summary>
        public List<FastbootDeviceListItem> GetDevices()
        {
            var nativeDevices = FastbootClient.GetDevices();
            
            return nativeDevices.Select(d => new FastbootDeviceListItem
            {
                Serial = d.Serial ?? $"{d.VendorId:X4}:{d.ProductId:X4}",
                Status = "fastboot"
            }).ToList();
        }
        
        /// <summary>
        /// 连接到设备
        /// </summary>
        public async Task<bool> ConnectAsync(string serial, CancellationToken ct = default)
        {
            Disconnect();
            
            _client = new FastbootClient(_log, _logDetail);
            _client.ProgressChanged += OnClientProgressChanged;
            
            // 查找设备
            var devices = FastbootClient.GetDevices();
            var device = devices.FirstOrDefault(d => 
                d.Serial == serial || 
                $"{d.VendorId:X4}:{d.ProductId:X4}" == serial);
            
            if (device == null)
            {
                _log($"未找到设备: {serial}");
                return false;
            }
            
            bool success = await _client.ConnectAsync(device, ct);
            
            if (success)
            {
                // 构建设备信息
                DeviceInfo = BuildDeviceInfo();
            }
            
            return success;
        }
        
        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (_client != null)
            {
                _client.ProgressChanged -= OnClientProgressChanged;
                _client.Disconnect();
                _client.Dispose();
                _client = null;
            }
            DeviceInfo = null;
        }
        
        /// <summary>
        /// 刷新设备信息
        /// </summary>
        public async Task<bool> RefreshDeviceInfoAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            
            await _client.RefreshDeviceInfoAsync(ct);
            DeviceInfo = BuildDeviceInfo();
            
            return true;
        }
        
        private FastbootDeviceInfo BuildDeviceInfo()
        {
            if (_client?.Variables == null) return null;
            
            var info = new FastbootDeviceInfo();
            
            // 复制所有变量到 RawVariables，并解析分区信息
            foreach (var kv in _client.Variables)
            {
                string key = kv.Key.ToLowerInvariant();
                string value = kv.Value;
                
                info.RawVariables[key] = value;
                
                // 解析分区大小: partition-size:boot_a: 0x4000000
                if (key.StartsWith("partition-size:"))
                {
                    string partName = key.Substring("partition-size:".Length);
                    if (TryParseHexOrDecimal(value, out long size))
                    {
                        info.PartitionSizes[partName] = size;
                    }
                }
                // 解析逻辑分区: is-logical:system_a: yes
                else if (key.StartsWith("is-logical:"))
                {
                    string partName = key.Substring("is-logical:".Length);
                    info.PartitionIsLogical[partName] = value.ToLower() == "yes";
                }
            }
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_PRODUCT, out string product))
                info.Product = product;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_SERIALNO, out string serial))
                info.Serial = serial;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_SECURE, out string secure))
                info.SecureBoot = secure.ToLower() == "yes";
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_UNLOCKED, out string unlocked))
                info.Unlocked = unlocked.ToLower() == "yes";
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_CURRENT_SLOT, out string slot))
                info.CurrentSlot = slot;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_IS_USERSPACE, out string userspace))
                info.IsFastbootd = userspace.ToLower() == "yes";
            
            // 解析其他设备信息
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VERSION_BOOTLOADER, out string blVersion))
                info.BootloaderVersion = blVersion;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VERSION_BASEBAND, out string bbVersion))
                info.BasebandVersion = bbVersion;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_HW_REVISION, out string hwRev))
                info.HardwareVersion = hwRev;
            
            if (_client.Variables.TryGetValue(FastbootProtocol.VAR_VARIANT, out string variant))
                info.Variant = variant;
            
            info.MaxDownloadSize = _client.MaxDownloadSize;
            
            return info;
        }
        
        /// <summary>
        /// 尝试解析十六进制或十进制数字
        /// </summary>
        private static bool TryParseHexOrDecimal(string value, out long result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;

            value = value.Trim();
            
            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    result = Convert.ToInt64(value.Substring(2), 16);
                    return true;
                }
                else
                {
                    return long.TryParse(value, out result);
                }
            }
            catch
            {
                return false;
            }
        }
        
        #endregion
        
        #region 刷写操作
        
        /// <summary>
        /// 刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partition, string imagePath, 
            bool disableVerity = false, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                _log("未连接设备");
                return false;
            }
            
            if (!File.Exists(imagePath))
            {
                _log($"文件不存在: {imagePath}");
                return false;
            }
            
            var progress = new Progress<FastbootProgressEventArgs>(args =>
            {
                ReportProgress(new FastbootNativeProgressEventArgs
                {
                    Partition = args.Partition,
                    Stage = args.Stage.ToString(),
                    CurrentChunk = args.CurrentChunk,
                    TotalChunks = args.TotalChunks,
                    BytesSent = args.BytesSent,
                    TotalBytes = args.TotalBytes,
                    Percent = args.Percent,
                    SpeedBps = args.SpeedBps
                });
            });
            
            try
            {
                // 强制 GC 释放之前可能残留的大内存块
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                return await _client.FlashAsync(partition, imagePath, progress, ct);
            }
            catch (OutOfMemoryException ex)
            {
                _log($"内存不足，无法刷写 {partition}：文件太大，请尝试关闭其他程序后重试");
                _logDetail($"OutOfMemoryException: {ex.Message}");
                
                // 尝试释放内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                return false;
            }
        }
        
        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partition, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                _log("未连接设备");
                return false;
            }
            
            return await _client.EraseAsync(partition, ct);
        }
        
        /// <summary>
        /// 批量刷写分区
        /// </summary>
        public async Task<int> FlashPartitionsBatchAsync(
            List<Tuple<string, string>> partitions, 
            CancellationToken ct = default)
        {
            int success = 0;
            int total = partitions.Count;
            
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var (partName, imagePath) = partitions[i];
                
                // 报告整体进度
                ReportProgress(new FastbootNativeProgressEventArgs
                {
                    Partition = partName,
                    Stage = "Preparing",
                    CurrentChunk = i + 1,
                    TotalChunks = total,
                    Percent = i * 100.0 / total
                });
                
                if (await FlashPartitionAsync(partName, imagePath, false, ct))
                {
                    success++;
                }
            }
            
            return success;
        }
        
        #endregion
        
        #region 重启操作
        
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootAsync(ct);
        }
        
        public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootBootloaderAsync(ct);
        }
        
        public async Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootFastbootdAsync(ct);
        }
        
        public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.RebootRecoveryAsync(ct);
        }
        
        #endregion
        
        #region 解锁/锁定
        
        public async Task<bool> UnlockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.UnlockAsync(ct);
        }
        
        public async Task<bool> LockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.LockAsync(ct);
        }
        
        #endregion
        
        #region A/B 槽位
        
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            return await _client.SetActiveSlotAsync(slot, ct);
        }
        
        public async Task<bool> SwitchSlotAsync(CancellationToken ct = default)
        {
            if (!IsConnected || DeviceInfo == null) return false;
            
            string currentSlot = DeviceInfo.CurrentSlot;
            string newSlot = currentSlot == "a" ? "b" : "a";
            
            return await SetActiveSlotAsync(newSlot, ct);
        }
        
        #endregion
        
        #region 变量操作
        
        public async Task<string> GetVariableAsync(string name, CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            return await _client.GetVariableAsync(name, ct);
        }
        
        #endregion
        
        #region OEM 命令
        
        public async Task<string> ExecuteOemCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            var response = await _client.OemCommandAsync(command, ct);
            return response?.Message;
        }
        
        #endregion
        
        #region 辅助方法
        
        private void OnClientProgressChanged(object sender, FastbootProgressEventArgs e)
        {
            ReportProgress(new FastbootNativeProgressEventArgs
            {
                Partition = e.Partition,
                Stage = e.Stage.ToString(),
                CurrentChunk = e.CurrentChunk,
                TotalChunks = e.TotalChunks,
                BytesSent = e.BytesSent,
                TotalBytes = e.TotalBytes,
                Percent = e.Percent,
                SpeedBps = e.SpeedBps
            });
        }
        
        private void ReportProgress(FastbootNativeProgressEventArgs args)
        {
            ProgressChanged?.Invoke(this, args);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// 原生 Fastboot 进度事件参数
    /// </summary>
    public class FastbootNativeProgressEventArgs : EventArgs
    {
        public string Partition { get; set; }
        public string Stage { get; set; }
        public int CurrentChunk { get; set; }
        public int TotalChunks { get; set; }
        public long BytesSent { get; set; }
        public long TotalBytes { get; set; }
        public double Percent { get; set; }
        public double SpeedBps { get; set; }
        
        public string PercentFormatted => $"{Percent:F1}%";
        
        public string SpeedFormatted
        {
            get
            {
                if (SpeedBps >= 1024 * 1024)
                    return $"{SpeedBps / 1024 / 1024:F2} MB/s";
                if (SpeedBps >= 1024)
                    return $"{SpeedBps / 1024:F2} KB/s";
                return $"{SpeedBps:F0} B/s";
            }
        }
        
        public string StatusText
        {
            get
            {
                if (TotalChunks > 1)
                {
                    return $"{Stage} '{Partition}' ({CurrentChunk}/{TotalChunks}) {PercentFormatted}";
                }
                return $"{Stage} '{Partition}' {PercentFormatted}";
            }
        }
    }
}
