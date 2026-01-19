using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Fastboot.Common;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Protocol;
using LoveAlways.Fastboot.Transport;

namespace LoveAlways.Fastboot.Services
{
    /// <summary>
    /// Fastboot 服务层
    /// 使用原生 C# 协议实现，不依赖外部 fastboot.exe
    /// </summary>
    public class FastbootService : IDisposable
    {
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<int, int> _progress;

        private FastbootNativeService _nativeService;
        private bool _disposed;

        /// <summary>
        /// 当前连接的设备序列号
        /// </summary>
        public string CurrentSerial => _nativeService?.CurrentSerial;

        /// <summary>
        /// 当前设备信息
        /// </summary>
        public FastbootDeviceInfo DeviceInfo => _nativeService?.DeviceInfo;

        /// <summary>
        /// 是否已连接设备
        /// </summary>
        public bool IsConnected => _nativeService?.IsConnected ?? false;
        
        /// <summary>
        /// 刷写进度事件
        /// </summary>
        public event Action<FlashProgress> FlashProgressChanged;

        public FastbootService(Action<string> log, Action<int, int> progress = null, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _progress = progress;
            _logDetail = logDetail ?? (msg => { });
        }

        #region 设备检测

        /// <summary>
        /// 获取 Fastboot 设备列表（使用原生协议）
        /// </summary>
        public Task<List<FastbootDeviceListItem>> GetDevicesAsync(CancellationToken ct = default)
        {
            var devices = new List<FastbootDeviceListItem>();

            try
            {
                // 使用原生 USB 枚举
                var nativeDevices = FastbootClient.GetDevices();
                
                foreach (var device in nativeDevices)
                {
                    devices.Add(new FastbootDeviceListItem
                    {
                        Serial = device.Serial ?? $"{device.VendorId:X4}:{device.ProductId:X4}",
                        Status = "fastboot"
                    });
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] 获取设备列表失败: {ex.Message}");
            }

            return Task.FromResult(devices);
        }

        /// <summary>
        /// 选择设备并获取设备信息
        /// </summary>
        public async Task<bool> SelectDeviceAsync(string serial, CancellationToken ct = default)
        {
            _log($"[Fastboot] 选择设备: {serial}");
            
            // 断开旧连接
            Disconnect();
            
            // 创建新的原生服务
            _nativeService = new FastbootNativeService(_log, _logDetail);
            _nativeService.ProgressChanged += OnNativeProgressChanged;
            
            // 连接设备
            bool success = await _nativeService.ConnectAsync(serial, ct);
            
            if (success)
            {
                _log($"[Fastboot] 设备: {DeviceInfo?.Product ?? "未知"}");
                _log($"[Fastboot] 安全启动: {(DeviceInfo?.SecureBoot == true ? "启用" : "禁用")}");
                
                if (DeviceInfo?.HasABPartition == true)
                {
                    _log($"[Fastboot] 当前槽位: {DeviceInfo.CurrentSlot}");
                }
                
                _log($"[Fastboot] Fastbootd 模式: {(DeviceInfo?.IsFastbootd == true ? "是" : "否")}");
                _log($"[Fastboot] 分区数量: {DeviceInfo?.PartitionSizes?.Count ?? 0}");
            }
            
            return success;
        }
        
        /// <summary>
        /// 原生进度回调
        /// </summary>
        private void OnNativeProgressChanged(object sender, FastbootNativeProgressEventArgs e)
        {
            // 转换为 FlashProgress 并触发事件
            var progress = new FlashProgress
            {
                PartitionName = e.Partition,
                Phase = e.Stage,
                CurrentChunk = e.CurrentChunk,
                TotalChunks = e.TotalChunks,
                SizeKB = e.TotalBytes / 1024,
                SpeedKBps = e.SpeedBps / 1024.0,
                Percent = e.Percent  // 传递实际进度值
            };
            
            FlashProgressChanged?.Invoke(progress);
        }

        /// <summary>
        /// 刷新设备信息
        /// </summary>
        public async Task<bool> RefreshDeviceInfoAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在读取设备信息...");
                bool result = await _nativeService.RefreshDeviceInfoAsync(ct);
                
                if (result && DeviceInfo != null)
                {
                    _log($"[Fastboot] 设备: {DeviceInfo.Product ?? "未知"}");
                    _log($"[Fastboot] 解锁状态: {(DeviceInfo.Unlocked == true ? "已解锁" : DeviceInfo.Unlocked == false ? "已锁定" : "未知")}");
                    _log($"[Fastboot] Fastbootd: {(DeviceInfo.IsFastbootd ? "是" : "否")}");
                    if (!string.IsNullOrEmpty(DeviceInfo.CurrentSlot))
                        _log($"[Fastboot] 当前槽位: {DeviceInfo.CurrentSlot}");
                    _log($"[Fastboot] 变量数量: {DeviceInfo.RawVariables?.Count ?? 0}");
                    _log($"[Fastboot] 分区数量: {DeviceInfo.PartitionSizes?.Count ?? 0}");
                    
                    // 提示 bootloader 模式限制
                    if (!DeviceInfo.IsFastbootd && DeviceInfo.PartitionSizes?.Count == 0)
                    {
                        _log("[Fastboot] 提示: Bootloader 模式不支持读取分区列表，如需查看请进入 Fastbootd 模式");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 读取设备信息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开设备
        /// </summary>
        public void Disconnect()
        {
            if (_nativeService != null)
            {
                _nativeService.ProgressChanged -= OnNativeProgressChanged;
                _nativeService.Disconnect();
                _nativeService.Dispose();
                _nativeService = null;
            }
        }

        #endregion

        #region 分区操作
        
        /// <summary>
        /// 刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partitionName, string imagePath, 
            bool disableVerity = false, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            if (!File.Exists(imagePath))
            {
                _log($"[Fastboot] 镜像文件不存在: {imagePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(imagePath);
                _log($"[Fastboot] 正在刷写 {partitionName} ({FormatSize(fileInfo.Length)})...");

                bool result = await _nativeService.FlashPartitionAsync(partitionName, imagePath, disableVerity, ct);
                
                if (result)
                {
                    _log($"[Fastboot] {partitionName} 刷写成功");
                }
                else
                {
                    _log($"[Fastboot] {partitionName} 刷写失败");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 刷写异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:F2} {units[unitIndex]}";
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log($"[Fastboot] 正在擦除 {partitionName}...");

                bool result = await _nativeService.ErasePartitionAsync(partitionName, ct);

                if (result)
                {
                    _log($"[Fastboot] {partitionName} 擦除成功");
                }
                else
                {
                    _log($"[Fastboot] {partitionName} 擦除失败");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 擦除异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 批量刷写分区
        /// </summary>
        public async Task<int> FlashPartitionsAsync(List<Tuple<string, string>> partitions, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return 0;
            }

            int success = 0;
            int total = partitions.Count;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (partName, imagePath) = partitions[i];
                
                _progress?.Invoke(i, total);
                
                if (await FlashPartitionAsync(partName, imagePath, false, ct))
                {
                    success++;
                }
            }

            _progress?.Invoke(total, total);
            return success;
        }

        #endregion

        #region 重启操作

        /// <summary>
        /// 重启到系统
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在重启...");
                return await _nativeService.RebootAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 重启失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重启到 Bootloader
        /// </summary>
        public async Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在重启到 Bootloader...");
                return await _nativeService.RebootBootloaderAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 重启失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重启到 Recovery
        /// </summary>
        public async Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在重启到 Recovery...");
                return await _nativeService.RebootRecoveryAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 重启失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重启到 Fastbootd
        /// </summary>
        public async Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在重启到 Fastbootd...");
                return await _nativeService.RebootFastbootdAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 重启失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Bootloader 解锁/锁定

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在解锁 Bootloader...");
                return await _nativeService.UnlockBootloaderAsync(method, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 解锁失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public async Task<bool> LockBootloaderAsync(string method = null, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log("[Fastboot] 正在锁定 Bootloader...");
                return await _nativeService.LockBootloaderAsync(method, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 锁定失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region A/B 槽位

        /// <summary>
        /// 设置活动槽位
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                _log($"[Fastboot] 正在设置活动槽位: {slot}...");
                return await _nativeService.SetActiveSlotAsync(slot, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 设置槽位失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 切换 A/B 槽位
        /// </summary>
        public async Task<bool> SwitchSlotAsync(CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return false;
            }

            try
            {
                return await _nativeService.SwitchSlotAsync(ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 切换槽位失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region OEM 命令

        /// <summary>
        /// 执行 OEM 命令
        /// </summary>
        public async Task<string> ExecuteOemCommandAsync(string command, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return null;
            }

            try
            {
                _log($"[Fastboot] 执行 OEM: {command}");
                return await _nativeService.ExecuteOemCommandAsync(command, ct);
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] OEM 命令失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取变量值
        /// </summary>
        public async Task<string> GetVariableAsync(string name, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                return null;
            }

            try
            {
                return await _nativeService.GetVariableAsync(name, ct);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 执行任意命令（用于快捷命令功能）
        /// </summary>
        public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
        {
            if (_nativeService == null || !_nativeService.IsConnected)
            {
                _log("[Fastboot] 未连接设备");
                return null;
            }

            try
            {
                _log($"[Fastboot] 执行: {command}");
                
                // 解析命令
                if (command.StartsWith("getvar ", StringComparison.OrdinalIgnoreCase))
                {
                    string varName = command.Substring(7).Trim();
                    return await _nativeService.GetVariableAsync(varName, ct);
                }
                else if (command.StartsWith("oem ", StringComparison.OrdinalIgnoreCase))
                {
                    string oemCmd = command.Substring(4).Trim();
                    return await _nativeService.ExecuteOemCommandAsync(oemCmd, ct);
                }
                else if (command == "reboot")
                {
                    await _nativeService.RebootAsync(ct);
                    return "OKAY";
                }
                else if (command == "reboot-bootloader" || command == "reboot bootloader")
                {
                    await _nativeService.RebootBootloaderAsync(ct);
                    return "OKAY";
                }
                else if (command == "reboot-recovery" || command == "reboot recovery")
                {
                    await _nativeService.RebootRecoveryAsync(ct);
                    return "OKAY";
                }
                else if (command == "reboot-fastboot" || command == "reboot fastboot")
                {
                    await _nativeService.RebootFastbootdAsync(ct);
                    return "OKAY";
                }
                else
                {
                    // 其他命令当作 OEM 命令执行
                    return await _nativeService.ExecuteOemCommandAsync(command, ct);
                }
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 命令执行失败: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region IDisposable

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
}
