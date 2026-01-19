using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.Fastboot.Common;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Payload;
using LoveAlways.Fastboot.Services;

namespace LoveAlways.Fastboot.UI
{
    /// <summary>
    /// Fastboot UI 控制器
    /// 负责连接 UI 控件与 Fastboot 服务
    /// </summary>
    public class FastbootUIController : IDisposable
    {
        private readonly Action<string, Color?> _log;
        private readonly Action<string> _logDetail;

        private FastbootService _service;
        private CancellationTokenSource _cts;
        private System.Windows.Forms.Timer _deviceRefreshTimer;
        private bool _disposed;

        // UI 控件绑定
        private dynamic _deviceComboBox;      // 设备选择下拉框（独立）
        private dynamic _partitionListView;   // 分区列表
        private dynamic _progressBar;         // 总进度条
        private dynamic _subProgressBar;      // 子进度条
        private dynamic _commandComboBox;     // 快捷命令下拉框
        private dynamic _payloadTextBox;      // Payload 路径
        private dynamic _outputPathTextBox;   // 输出路径

        // 设备信息标签 (右上角信息区域)
        private dynamic _brandLabel;          // 品牌
        private dynamic _chipLabel;           // 芯片/平台
        private dynamic _modelLabel;          // 设备型号
        private dynamic _serialLabel;         // 序列号
        private dynamic _storageLabel;        // 存储类型
        private dynamic _unlockLabel;         // 解锁状态
        private dynamic _slotLabel;           // 当前槽位

        // 时间/速度/操作状态标签
        private dynamic _timeLabel;           // 时间标签
        private dynamic _speedLabel;          // 速度标签
        private dynamic _operationLabel;      // 当前操作标签
        private dynamic _deviceCountLabel;    // 设备数量标签

        // Checkbox 控件
        private dynamic _autoRebootCheckbox;      // 自动重启
        private dynamic _switchSlotCheckbox;      // 切换A槽
        private dynamic _eraseGoogleLockCheckbox; // 擦除谷歌锁
        private dynamic _keepDataCheckbox;        // 保留数据
        private dynamic _fbdFlashCheckbox;        // FBD刷写
        private dynamic _unlockBlCheckbox;        // 解锁BL
        private dynamic _lockBlCheckbox;          // 锁定BL

        // 计时器和速度计算
        private Stopwatch _operationStopwatch;
        private long _lastBytes;
        private DateTime _lastSpeedUpdate;
        private double _currentSpeed; // 当前速度 (bytes/s)
        private long _totalOperationBytes;
        private long _completedBytes;
        private string _currentOperationName;

        // 设备列表缓存
        private List<FastbootDeviceListItem> _cachedDevices = new List<FastbootDeviceListItem>();

        // Payload 服务
        private PayloadService _payloadService;
        private RemotePayloadService _remotePayloadService;

        // 状态
        public bool IsBusy { get; private set; }
        public bool IsConnected => _service?.IsConnected ?? false;
        public FastbootDeviceInfo DeviceInfo => _service?.DeviceInfo;
        public List<FastbootPartitionInfo> Partitions => _service?.DeviceInfo?.GetPartitions();
        public int DeviceCount => _cachedDevices?.Count ?? 0;
        
        // Payload 状态
        public bool IsPayloadLoaded => (_payloadService?.IsLoaded ?? false) || (_remotePayloadService?.IsLoaded ?? false);
        public IReadOnlyList<PayloadPartition> PayloadPartitions => _payloadService?.Partitions;
        public PayloadSummary PayloadSummary => _payloadService?.GetSummary();
        
        // 远程 Payload 状态
        public bool IsRemotePayloadLoaded => _remotePayloadService?.IsLoaded ?? false;
        public IReadOnlyList<RemotePayloadPartition> RemotePayloadPartitions => _remotePayloadService?.Partitions;
        public RemotePayloadSummary RemotePayloadSummary => _remotePayloadService?.GetSummary();

        // 事件
        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<List<FastbootPartitionInfo>> PartitionsLoaded;
        public event EventHandler<List<FastbootDeviceListItem>> DevicesRefreshed;
        public event EventHandler<PayloadSummary> PayloadLoaded;
        public event EventHandler<PayloadExtractProgress> PayloadExtractProgress;

        public FastbootUIController(Action<string, Color?> log, Action<string> logDetail = null)
        {
            _log = log ?? ((msg, color) => { });
            _logDetail = logDetail ?? (msg => { });

            // 初始化计时器
            _operationStopwatch = new Stopwatch();
            _lastSpeedUpdate = DateTime.Now;

            // 初始化设备刷新定时器
            _deviceRefreshTimer = new System.Windows.Forms.Timer();
            _deviceRefreshTimer.Interval = 2000; // 每 2 秒刷新一次
            _deviceRefreshTimer.Tick += async (s, e) => await RefreshDeviceListAsync();
        }

        #region 日志方法

        private void Log(string message, Color? color = null)
        {
            _log(message, color);
        }

        #endregion

        #region 控件绑定

        /// <summary>
        /// 绑定 UI 控件
        /// </summary>
        public void BindControls(
            object deviceComboBox = null,
            object partitionListView = null,
            object progressBar = null,
            object subProgressBar = null,
            object commandComboBox = null,
            object payloadTextBox = null,
            object outputPathTextBox = null,
            // 设备信息标签
            object brandLabel = null,
            object chipLabel = null,
            object modelLabel = null,
            object serialLabel = null,
            object storageLabel = null,
            object unlockLabel = null,
            object slotLabel = null,
            // 时间/速度/操作标签
            object timeLabel = null,
            object speedLabel = null,
            object operationLabel = null,
            object deviceCountLabel = null,
            // Checkbox 控件
            object autoRebootCheckbox = null,
            object switchSlotCheckbox = null,
            object eraseGoogleLockCheckbox = null,
            object keepDataCheckbox = null,
            object fbdFlashCheckbox = null,
            object unlockBlCheckbox = null,
            object lockBlCheckbox = null)
        {
            _deviceComboBox = deviceComboBox;
            _partitionListView = partitionListView;
            _progressBar = progressBar;
            _subProgressBar = subProgressBar;
            _commandComboBox = commandComboBox;
            _payloadTextBox = payloadTextBox;
            _outputPathTextBox = outputPathTextBox;

            // 设备信息标签
            _brandLabel = brandLabel;
            _chipLabel = chipLabel;
            _modelLabel = modelLabel;
            _serialLabel = serialLabel;
            _storageLabel = storageLabel;
            _unlockLabel = unlockLabel;
            _slotLabel = slotLabel;

            // 时间/速度/操作标签
            _timeLabel = timeLabel;
            _speedLabel = speedLabel;
            _operationLabel = operationLabel;
            _deviceCountLabel = deviceCountLabel;

            // Checkbox
            _autoRebootCheckbox = autoRebootCheckbox;
            _switchSlotCheckbox = switchSlotCheckbox;
            _eraseGoogleLockCheckbox = eraseGoogleLockCheckbox;
            _keepDataCheckbox = keepDataCheckbox;
            _fbdFlashCheckbox = fbdFlashCheckbox;
            _unlockBlCheckbox = unlockBlCheckbox;
            _lockBlCheckbox = lockBlCheckbox;

            // 初始化分区列表
            if (_partitionListView != null)
            {
                try
                {
                    _partitionListView.CheckBoxes = true;
                    _partitionListView.FullRowSelect = true;
                    _partitionListView.MultiSelect = true;
                }
                catch { }
            }

            // 初始化设备信息显示
            ResetDeviceInfoLabels();
        }

        /// <summary>
        /// 重置设备信息标签为默认值
        /// </summary>
        public void ResetDeviceInfoLabels()
        {
            UpdateLabelSafe(_brandLabel, "品牌：等待连接");
            UpdateLabelSafe(_chipLabel, "芯片：等待连接");
            UpdateLabelSafe(_modelLabel, "型号：等待连接");
            UpdateLabelSafe(_serialLabel, "序列号：等待连接");
            UpdateLabelSafe(_storageLabel, "存储：等待连接");
            UpdateLabelSafe(_unlockLabel, "解锁：等待连接");
            UpdateLabelSafe(_slotLabel, "槽位：等待连接");
            UpdateLabelSafe(_timeLabel, "时间：00:00");
            UpdateLabelSafe(_speedLabel, "速度：0 KB/s");
            UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            UpdateLabelSafe(_deviceCountLabel, "FB设备：0");
        }

        /// <summary>
        /// 更新设备信息标签
        /// </summary>
        public void UpdateDeviceInfoLabels()
        {
            if (DeviceInfo == null)
            {
                ResetDeviceInfoLabels();
                return;
            }

            // 品牌/厂商
            string brand = DeviceInfo.GetVariable("ro.product.brand") 
                ?? DeviceInfo.GetVariable("manufacturer") 
                ?? "未知";
            UpdateLabelSafe(_brandLabel, $"品牌：{brand}");

            // 芯片/平台
            string chip = DeviceInfo.GetVariable("ro.boot.hardware") 
                ?? DeviceInfo.GetVariable("hw-revision") 
                ?? DeviceInfo.GetVariable("variant") 
                ?? "未知";
            UpdateLabelSafe(_chipLabel, $"芯片：{chip}");

            // 型号
            string model = DeviceInfo.GetVariable("product") 
                ?? DeviceInfo.GetVariable("ro.product.model") 
                ?? "未知";
            UpdateLabelSafe(_modelLabel, $"型号：{model}");

            // 序列号
            string serial = DeviceInfo.Serial ?? "未知";
            UpdateLabelSafe(_serialLabel, $"序列号：{serial}");

            // 存储类型
            string storage = DeviceInfo.GetVariable("partition-type:userdata") ?? "未知";
            if (storage.Contains("ext4") || storage.Contains("f2fs"))
                storage = "eMMC/UFS";
            UpdateLabelSafe(_storageLabel, $"存储：{storage}");

            // 解锁状态
            string unlocked = DeviceInfo.GetVariable("unlocked");
            string secureState = DeviceInfo.GetVariable("secure");
            string unlockStatus = "未知";
            if (!string.IsNullOrEmpty(unlocked))
            {
                unlockStatus = unlocked.ToLower() == "yes" || unlocked == "1" ? "已解锁" : "已锁定";
            }
            else if (!string.IsNullOrEmpty(secureState))
            {
                unlockStatus = secureState.ToLower() == "no" || secureState == "0" ? "已解锁" : "已锁定";
            }
            UpdateLabelSafe(_unlockLabel, $"解锁：{unlockStatus}");

            // 当前槽位
            string slot = DeviceInfo.GetVariable("current-slot") ?? "N/A";
            if (slot != "N/A" && !slot.StartsWith("_"))
                slot = "_" + slot;
            UpdateLabelSafe(_slotLabel, $"槽位：{slot}");
        }

        /// <summary>
        /// 安全更新 Label 文本
        /// </summary>
        private void UpdateLabelSafe(dynamic label, string text)
        {
            if (label == null) return;

            try
            {
                if (label.InvokeRequired)
                {
                    label.BeginInvoke(new Action(() =>
                    {
                        try { label.Text = text; } catch { }
                    }));
                }
                else
                {
                    label.Text = text;
                }
            }
            catch { }
        }

        /// <summary>
        /// 启动设备监控
        /// </summary>
        public void StartDeviceMonitoring()
        {
            _deviceRefreshTimer.Start();
            Task.Run(() => RefreshDeviceListAsync());
        }

        /// <summary>
        /// 停止设备监控
        /// </summary>
        public void StopDeviceMonitoring()
        {
            _deviceRefreshTimer.Stop();
        }

        #endregion

        #region 设备操作

        /// <summary>
        /// 刷新设备列表
        /// </summary>
        public async Task RefreshDeviceListAsync()
        {
            try
            {
                using (var tempService = new FastbootService(msg => { }))
                {
                    var devices = await tempService.GetDevicesAsync();
                    _cachedDevices = devices ?? new List<FastbootDeviceListItem>();
                    
                    // 在 UI 线程更新
                    if (_deviceComboBox != null)
                    {
                        try
                        {
                            if (_deviceComboBox.InvokeRequired)
                            {
                                _deviceComboBox.BeginInvoke(new Action(() => UpdateDeviceComboBox(devices)));
                            }
                            else
                            {
                                UpdateDeviceComboBox(devices);
                            }
                        }
                        catch { }
                    }

                    // 更新设备数量显示
                    UpdateDeviceCountLabel();

                    DevicesRefreshed?.Invoke(this, devices);
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] 刷新设备列表异常: {ex.Message}");
            }
        }

        private void UpdateDeviceComboBox(List<FastbootDeviceListItem> devices)
        {
            if (_deviceComboBox == null) return;

            try
            {
                string currentSelection = null;
                try { currentSelection = _deviceComboBox.SelectedItem?.ToString(); } catch { }

                _deviceComboBox.Items.Clear();
                foreach (var device in devices)
                {
                    _deviceComboBox.Items.Add(device.ToString());
                }

                // 尝试恢复之前的选择
                if (!string.IsNullOrEmpty(currentSelection) && _deviceComboBox.Items.Contains(currentSelection))
                {
                    _deviceComboBox.SelectedItem = currentSelection;
                }
                else if (_deviceComboBox.Items.Count > 0)
                {
                    _deviceComboBox.SelectedIndex = 0;
                }
            }
            catch { }
        }

        /// <summary>
        /// 更新设备数量显示标签
        /// </summary>
        private void UpdateDeviceCountLabel()
        {
            int count = _cachedDevices?.Count ?? 0;
            string text = count == 0 ? "FB设备：0" 
                : count == 1 ? $"FB设备：{_cachedDevices[0].Serial}" 
                : $"FB设备：{count}个";
            
            UpdateLabelSafe(_deviceCountLabel, text);
        }

        /// <summary>
        /// 连接选中的设备
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (IsBusy)
            {
                Log("操作进行中", Color.Orange);
                return false;
            }

            string selectedDevice = GetSelectedDevice();
            if (string.IsNullOrEmpty(selectedDevice))
            {
                Log("请选择 Fastboot 设备", Color.Red);
                return false;
            }

            // 从 "serial (status)" 格式提取序列号
            string serial = selectedDevice.Split(' ')[0];

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("连接设备");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "当前操作：连接设备");

                _service = new FastbootService(
                    msg => Log(msg, null),
                    (current, total) => UpdateProgressWithSpeed(current, total),
                    _logDetail
                );

                UpdateProgressBar(30);
                bool success = await _service.SelectDeviceAsync(serial, _cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);
                    Log("Fastboot 设备连接成功", Color.Green);
                    
                    // 更新设备信息标签
                    UpdateDeviceInfoLabels();
                    
                    // 更新分区列表
                    UpdatePartitionListView();
                    
                    UpdateProgressBar(100);
                    ConnectionStateChanged?.Invoke(this, true);
                    PartitionsLoaded?.Invoke(this, Partitions);
                }
                else
                {
                    Log("Fastboot 设备连接失败", Color.Red);
                    ResetDeviceInfoLabels();
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"连接异常: {ex.Message}", Color.Red);
                ResetDeviceInfoLabels();
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _service?.Disconnect();
            ResetDeviceInfoLabels();
            ConnectionStateChanged?.Invoke(this, false);
        }

        private string GetSelectedDevice()
        {
            try
            {
                if (_deviceComboBox == null) return null;
                return _deviceComboBox.SelectedItem?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 读取分区表（刷新设备信息）
        /// </summary>
        public async Task<bool> ReadPartitionTableAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("读取分区表");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "当前操作：读取分区表");
                UpdateLabelSafe(_speedLabel, "速度：读取中...");

                Log("正在读取 Fastboot 分区表...", Color.Blue);

                UpdateProgressBar(30);
                bool success = await _service.RefreshDeviceInfoAsync(_cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);
                    
                    // 更新设备信息标签
                    UpdateDeviceInfoLabels();
                    
                    UpdatePartitionListView();
                    UpdateProgressBar(100);
                    
                    Log($"成功读取 {Partitions?.Count ?? 0} 个分区", Color.Green);
                    PartitionsLoaded?.Invoke(this, Partitions);
                }
                else
                {
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"读取分区表失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 更新分区列表视图
        /// </summary>
        private void UpdatePartitionListView()
        {
            if (_partitionListView == null || Partitions == null) return;

            try
            {
                if (_partitionListView.InvokeRequired)
                {
                    _partitionListView.BeginInvoke(new Action(UpdatePartitionListViewInternal));
                }
                else
                {
                    UpdatePartitionListViewInternal();
                }
            }
            catch { }
        }

        private void UpdatePartitionListViewInternal()
        {
            try
            {
                _partitionListView.Items.Clear();

                foreach (var part in Partitions)
                {
                    var item = new ListViewItem(new string[]
                    {
                        part.Name,
                        "-",  // 操作列
                        part.SizeFormatted,
                        part.IsLogicalText
                    });
                    item.Tag = part;
                    _partitionListView.Items.Add(item);
                }
            }
            catch { }
        }

        /// <summary>
        /// 刷写选中的分区
        /// </summary>
        public async Task<bool> FlashSelectedPartitionsAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            var selectedItems = GetSelectedPartitionItems();
            if (selectedItems.Count == 0)
            {
                Log("请选择要刷写的分区", Color.Orange);
                return false;
            }

            // 检查是否有镜像文件
            var partitionsWithFiles = new List<Tuple<string, string>>();
            foreach (ListViewItem item in selectedItems)
            {
                string partName = item.SubItems[0].Text;
                string filePath = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
                
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Log($"分区 {partName} 没有选择镜像文件", Color.Orange);
                    continue;
                }

                partitionsWithFiles.Add(Tuple.Create(partName, filePath));
            }

            if (partitionsWithFiles.Count == 0)
            {
                Log("没有可刷写的分区（请双击分区选择镜像文件）", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("刷写分区");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_operationLabel, $"当前操作：刷写 {partitionsWithFiles.Count} 个分区");
                UpdateLabelSafe(_speedLabel, "速度：计算中...");

                Log($"开始刷写 {partitionsWithFiles.Count} 个分区...", Color.Blue);

                int successCount = 0;
                int total = partitionsWithFiles.Count;

                for (int i = 0; i < total; i++)
                {
                    var part = partitionsWithFiles[i];
                    UpdateLabelSafe(_operationLabel, $"当前操作：刷写 {part.Item1} ({i + 1}/{total})");
                    // 总进度：基于已完成的分区数
                    UpdateProgressBar((i * 100.0) / total);
                    // 子进度：当前分区刷写开始
                    UpdateSubProgressBar(0);

                    var flashStart = DateTime.Now;
                    var fileSize = new FileInfo(part.Item2).Length;
                    
                    bool result = await _service.FlashPartitionAsync(part.Item1, part.Item2, false, _cts.Token);
                    
                    // 子进度：当前分区刷写完成
                    UpdateSubProgressBar(100);
                    
                    // 计算并显示速度
                    var elapsed = (DateTime.Now - flashStart).TotalSeconds;
                    if (elapsed > 0)
                    {
                        double speed = fileSize / elapsed;
                        UpdateSpeedLabel(FormatSpeed(speed));
                    }
                    
                    if (result)
                        successCount++;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"刷写完成: {successCount}/{total} 成功", 
                    successCount == total ? Color.Green : Color.Orange);

                // 自动重启
                if (IsAutoRebootEnabled() && successCount > 0)
                {
                    await _service.RebootAsync(_cts.Token);
                }

                return successCount == total;
            }
            catch (Exception ex)
            {
                Log($"刷写失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 擦除选中的分区
        /// </summary>
        public async Task<bool> EraseSelectedPartitionsAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            var selectedItems = GetSelectedPartitionItems();
            if (selectedItems.Count == 0)
            {
                Log("请选择要擦除的分区", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("擦除分区");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：擦除中...");

                int success = 0;
                int total = selectedItems.Count;
                int current = 0;

                Log($"开始擦除 {total} 个分区...", Color.Blue);

                foreach (ListViewItem item in selectedItems)
                {
                    string partName = item.SubItems[0].Text;
                    UpdateLabelSafe(_operationLabel, $"当前操作：擦除 {partName} ({current + 1}/{total})");
                    // 总进度：基于已完成的分区数
                    UpdateProgressBar((current * 100.0) / total);
                    // 子进度：开始擦除
                    UpdateSubProgressBar(0);
                    
                    if (await _service.ErasePartitionAsync(partName, _cts.Token))
                    {
                        success++;
                    }
                    
                    // 子进度：当前分区擦除完成
                    UpdateSubProgressBar(100);
                    current++;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"擦除完成: {success}/{total} 成功", 
                    success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (Exception ex)
            {
                Log($"擦除失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        private List<ListViewItem> GetSelectedPartitionItems()
        {
            var items = new List<ListViewItem>();
            if (_partitionListView == null) return items;

            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    items.Add(item);
                }
            }
            catch { }

            return items;
        }

        /// <summary>
        /// 为分区选择镜像文件
        /// </summary>
        public void SelectImageForPartition(ListViewItem item)
        {
            if (item == null) return;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"选择 {item.SubItems[0].Text} 分区镜像";
                ofd.Filter = "镜像文件|*.img;*.bin;*.mbn|所有文件|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // 更新操作列和文件路径列
                    if (item.SubItems.Count > 1)
                        item.SubItems[1].Text = "写入";
                    if (item.SubItems.Count > 3)
                        item.SubItems[3].Text = ofd.FileName;
                    else
                    {
                        while (item.SubItems.Count < 4)
                            item.SubItems.Add("");
                        item.SubItems[3].Text = ofd.FileName;
                    }

                    item.Checked = true;
                    Log($"已选择镜像: {Path.GetFileName(ofd.FileName)} -> {item.SubItems[0].Text}", Color.Blue);
                }
            }
        }

        #endregion

        #region 重启操作

        /// <summary>
        /// 重启到系统
        /// </summary>
        public async Task<bool> RebootToSystemAsync()
        {
            if (!EnsureConnected()) return false;
            return await _service.RebootAsync();
        }

        /// <summary>
        /// 重启到 Bootloader
        /// </summary>
        public async Task<bool> RebootToBootloaderAsync()
        {
            if (!EnsureConnected()) return false;
            return await _service.RebootBootloaderAsync();
        }

        /// <summary>
        /// 重启到 Fastbootd
        /// </summary>
        public async Task<bool> RebootToFastbootdAsync()
        {
            if (!EnsureConnected()) return false;
            return await _service.RebootFastbootdAsync();
        }

        /// <summary>
        /// 重启到 Recovery
        /// </summary>
        public async Task<bool> RebootToRecoveryAsync()
        {
            if (!EnsureConnected()) return false;
            return await _service.RebootRecoveryAsync();
        }

        #endregion

        #region 解锁/锁定

        /// <summary>
        /// 执行解锁操作
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync()
        {
            if (!EnsureConnected()) return false;

            string method = "flashing unlock";
            
            // 根据 checkbox 状态选择解锁方法
            // 可以从 _commandComboBox 获取选择的命令
            string selectedCmd = GetSelectedCommand();
            if (!string.IsNullOrEmpty(selectedCmd) && selectedCmd.Contains("unlock"))
            {
                method = selectedCmd;
            }

            return await _service.UnlockBootloaderAsync(method);
        }

        /// <summary>
        /// 执行锁定操作
        /// </summary>
        public async Task<bool> LockBootloaderAsync()
        {
            if (!EnsureConnected()) return false;

            string method = "flashing lock";
            
            string selectedCmd = GetSelectedCommand();
            if (!string.IsNullOrEmpty(selectedCmd) && selectedCmd.Contains("lock"))
            {
                method = selectedCmd;
            }

            return await _service.LockBootloaderAsync(method);
        }

        #endregion

        #region A/B 槽位

        /// <summary>
        /// 切换 A/B 槽位
        /// </summary>
        public async Task<bool> SwitchSlotAsync()
        {
            if (!EnsureConnected()) return false;
            
            bool success = await _service.SwitchSlotAsync();
            
            if (success)
            {
                await ReadPartitionTableAsync();
            }

            return success;
        }

        #endregion

        #region 快捷命令

        /// <summary>
        /// 执行选中的快捷命令
        /// </summary>
        public async Task<bool> ExecuteSelectedCommandAsync()
        {
            if (!EnsureConnected()) return false;

            string command = GetSelectedCommand();
            if (string.IsNullOrEmpty(command))
            {
                Log("请选择要执行的命令", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                var result = await _service.ExecuteCommandAsync(command, _cts.Token);
                
                return result.Success;
            }
            catch (Exception ex)
            {
                Log($"命令执行失败: {ex.Message}", Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string GetSelectedCommand()
        {
            try
            {
                if (_commandComboBox == null) return null;
                return _commandComboBox.SelectedItem?.ToString() ?? _commandComboBox.Text;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 辅助方法

        private bool EnsureConnected()
        {
            if (_service == null || !_service.IsConnected)
            {
                Log("请先连接 Fastboot 设备", Color.Red);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 启动操作计时器
        /// </summary>
        private void StartOperationTimer(string operationName)
        {
            _currentOperationName = operationName;
            _operationStopwatch.Restart();
            _lastBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _currentSpeed = 0;
            _completedBytes = 0;
            _totalOperationBytes = 0;
        }

        /// <summary>
        /// 停止操作计时器
        /// </summary>
        private void StopOperationTimer()
        {
            _operationStopwatch.Stop();
            UpdateTimeLabel();
        }

        /// <summary>
        /// 更新时间标签
        /// </summary>
        private void UpdateTimeLabel()
        {
            if (_timeLabel == null) return;

            var elapsed = _operationStopwatch.Elapsed;
            string timeText = elapsed.Hours > 0
                ? $"时间：{elapsed:hh\\:mm\\:ss}"
                : $"时间：{elapsed:mm\\:ss}";
            
            UpdateLabelSafe(_timeLabel, timeText);
        }

        /// <summary>
        /// 更新速度标签
        /// </summary>
        private void UpdateSpeedLabel()
        {
            if (_speedLabel == null) return;

            string speedText;
            if (_currentSpeed >= 1024 * 1024)
                speedText = $"速度：{_currentSpeed / (1024 * 1024):F1} MB/s";
            else if (_currentSpeed >= 1024)
                speedText = $"速度：{_currentSpeed / 1024:F1} KB/s";
            else
                speedText = $"速度：{_currentSpeed:F0} B/s";
            
            UpdateLabelSafe(_speedLabel, speedText);
        }
        
        /// <summary>
        /// 更新速度标签 (使用格式化的速度字符串)
        /// </summary>
        private void UpdateSpeedLabel(string formattedSpeed)
        {
            if (_speedLabel == null) return;
            UpdateLabelSafe(_speedLabel, $"速度：{formattedSpeed}");
        }
        
        /// <summary>
        /// 格式化速度显示
        /// </summary>
        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "计算中...";
            
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            double speed = bytesPerSecond;
            int unitIndex = 0;
            while (speed >= 1024 && unitIndex < units.Length - 1)
            {
                speed /= 1024;
                unitIndex++;
            }
            return $"{speed:F2} {units[unitIndex]}";
        }

        /// <summary>
        /// 更新进度条 (百分比)
        /// </summary>
        private void UpdateProgressBar(double percent)
        {
            if (_progressBar == null) return;

            try
            {
                int value = Math.Min(100, Math.Max(0, (int)percent));
                
                if (_progressBar.InvokeRequired)
                {
                    _progressBar.BeginInvoke(new Action(() =>
                    {
                        try { _progressBar.Value = value; } catch { }
                    }));
                }
                else
                {
                    _progressBar.Value = value;
                }
            }
            catch { }
        }

        /// <summary>
        /// 更新子进度条
        /// </summary>
        private void UpdateSubProgressBar(double percent)
        {
            if (_subProgressBar == null) return;

            try
            {
                int value = Math.Min(100, Math.Max(0, (int)percent));
                
                if (_subProgressBar.InvokeRequired)
                {
                    _subProgressBar.BeginInvoke(new Action(() =>
                    {
                        try { _subProgressBar.Value = value; } catch { }
                    }));
                }
                else
                {
                    _subProgressBar.Value = value;
                }
            }
            catch { }
        }

        /// <summary>
        /// 带速度计算的进度更新 (用于文件传输)
        /// </summary>
        private void UpdateProgressWithSpeed(long current, long total)
        {
            // 计算进度
            if (total > 0)
            {
                double percent = 100.0 * current / total;
                UpdateSubProgressBar(percent);
            }

            // 计算速度
            long bytesDelta = current - _lastBytes;
            double timeDelta = (DateTime.Now - _lastSpeedUpdate).TotalSeconds;
            
            if (timeDelta >= 0.2 && bytesDelta > 0) // 每200ms更新一次
            {
                double instantSpeed = bytesDelta / timeDelta;
                // 指数移动平均平滑速度
                _currentSpeed = (_currentSpeed > 0) ? (_currentSpeed * 0.6 + instantSpeed * 0.4) : instantSpeed;
                _lastBytes = current;
                _lastSpeedUpdate = DateTime.Now;
                
                // 更新速度和时间显示
                UpdateSpeedLabel();
                UpdateTimeLabel();
            }
        }

        private void UpdateProgress(int current, int total)
        {
            if (_progressBar == null) return;

            try
            {
                int percent = total > 0 ? (current * 100 / total) : 0;
                UpdateProgressBar(percent);
            }
            catch { }
        }

        private bool IsAutoRebootEnabled()
        {
            try
            {
                return _autoRebootCheckbox?.Checked ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取消当前操作
        /// </summary>
        public void CancelOperation()
        {
            _cts?.Cancel();
            StopOperationTimer();
            UpdateLabelSafe(_operationLabel, "当前操作：已取消");
        }

        #endregion

        #region Bat 脚本解析

        // 存储解析的刷机任务
        private List<BatScriptParser.FlashTask> _flashTasks;
        
        /// <summary>
        /// 获取当前加载的刷机任务
        /// </summary>
        public List<BatScriptParser.FlashTask> FlashTasks => _flashTasks;

        /// <summary>
        /// 加载 bat/sh 刷机脚本
        /// </summary>
        public bool LoadFlashScript(string scriptPath)
        {
            if (!File.Exists(scriptPath))
            {
                Log($"脚本文件不存在: {scriptPath}", Color.Red);
                return false;
            }

            try
            {
                Log($"正在解析脚本: {Path.GetFileName(scriptPath)}...", Color.Blue);

                string baseDir = Path.GetDirectoryName(scriptPath);
                var parser = new BatScriptParser(baseDir, msg => _logDetail(msg));

                _flashTasks = parser.ParseScript(scriptPath);

                if (_flashTasks.Count == 0)
                {
                    Log("脚本中未找到有效的刷机命令", Color.Orange);
                    return false;
                }

                // 统计信息
                int flashCount = _flashTasks.Count(t => t.Operation == "flash");
                int eraseCount = _flashTasks.Count(t => t.Operation == "erase");
                int existCount = _flashTasks.Count(t => t.ImageExists);
                long totalSize = _flashTasks.Where(t => t.ImageExists).Sum(t => t.FileSize);

                Log($"解析完成: {flashCount} 个刷写, {eraseCount} 个擦除", Color.Green);
                Log($"镜像文件: {existCount} 个存在, 总大小 {FormatSize(totalSize)}", Color.Blue);

                // 更新分区列表显示
                UpdatePartitionListFromScript();

                return true;
            }
            catch (Exception ex)
            {
                Log($"解析脚本失败: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// 从脚本任务更新分区列表
        /// </summary>
        private void UpdatePartitionListFromScript()
        {
            if (_partitionListView == null || _flashTasks == null) return;

            try
            {
                if (_partitionListView.InvokeRequired)
                {
                    _partitionListView.BeginInvoke(new Action(UpdatePartitionListFromScriptInternal));
                }
                else
                {
                    UpdatePartitionListFromScriptInternal();
                }
            }
            catch { }
        }

        private void UpdatePartitionListFromScriptInternal()
        {
            try
            {
                _partitionListView.Items.Clear();

                foreach (var task in _flashTasks)
                {
                    // 根据操作类型设置不同显示
                    string operationText = task.Operation;
                    string sizeText = "-";
                    string filePathText = "";

                    if (task.Operation == "flash")
                    {
                        operationText = task.ImageExists ? "写入" : "写入 (缺失)";
                        sizeText = task.FileSizeFormatted;
                        filePathText = task.ImagePath;
                    }
                    else if (task.Operation == "erase")
                    {
                        operationText = "擦除";
                    }
                    else if (task.Operation == "set_active")
                    {
                        operationText = "激活槽位";
                    }
                    else if (task.Operation == "reboot")
                    {
                        operationText = "重启";
                    }

                    var item = new ListViewItem(new string[]
                    {
                        task.PartitionName,
                        operationText,
                        sizeText,
                        filePathText
                    });

                    item.Tag = task;

                    // 根据状态设置颜色
                    if (task.Operation == "flash" && !task.ImageExists)
                    {
                        item.ForeColor = Color.Red;
                    }
                    else if (task.Operation == "erase")
                    {
                        item.ForeColor = Color.Orange;
                    }
                    else if (task.Operation == "set_active" || task.Operation == "reboot")
                    {
                        item.ForeColor = Color.Gray;
                    }

                    // 默认勾选所有 flash 操作
                    if (task.Operation == "flash" && task.ImageExists)
                    {
                        item.Checked = true;
                    }

                    _partitionListView.Items.Add(item);
                }
            }
            catch { }
        }

        /// <summary>
        /// 执行加载的刷机脚本
        /// </summary>
        /// <param name="keepData">是否保留数据（跳过 userdata 刷写）</param>
        /// <param name="lockBl">是否在刷机后锁定BL</param>
        public async Task<bool> ExecuteFlashScriptAsync(bool keepData = false, bool lockBl = false)
        {
            if (_flashTasks == null || _flashTasks.Count == 0)
            {
                Log("请先加载刷机脚本", Color.Orange);
                return false;
            }

            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            // 获取选中的任务
            var selectedTasks = new List<BatScriptParser.FlashTask>();
            
            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    if (item.Tag is BatScriptParser.FlashTask task)
                    {
                        selectedTasks.Add(task);
                    }
                }
            }
            catch { }

            if (selectedTasks.Count == 0)
            {
                Log("请选择要执行的刷机任务", Color.Orange);
                return false;
            }

            // 根据选项过滤任务
            if (keepData)
            {
                // 保留数据：跳过 userdata 相关分区
                int beforeCount = selectedTasks.Count;
                selectedTasks = selectedTasks.Where(t => 
                    !t.PartitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase) &&
                    !t.PartitionName.Equals("userdata_ab", StringComparison.OrdinalIgnoreCase) &&
                    !t.PartitionName.Equals("metadata", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                if (selectedTasks.Count < beforeCount)
                {
                    Log("保留数据模式：跳过 userdata/metadata 分区", Color.Blue);
                }
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("执行刷机脚本");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：准备中...");

                int total = selectedTasks.Count;
                int success = 0;
                int failed = 0;

                Log($"开始执行 {total} 个刷机任务...", Color.Blue);
                if (keepData) Log("模式: 保留数据", Color.Blue);
                if (lockBl) Log("模式: 刷机后锁定BL", Color.Blue);

                for (int i = 0; i < total; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var task = selectedTasks[i];
                    // 总进度：基于任务数
                    UpdateProgressBar((i * 100.0) / total);
                    // 子进度：当前任务开始
                    UpdateSubProgressBar(0);
                    UpdateLabelSafe(_operationLabel, $"当前操作：{task.Operation} {task.PartitionName} ({i + 1}/{total})");

                    bool taskSuccess = false;

                    switch (task.Operation)
                    {
                        case "flash":
                            if (task.ImageExists)
                            {
                                taskSuccess = await _service.FlashPartitionAsync(
                                    task.PartitionName, task.ImagePath, false, _cts.Token);
                            }
                            else
                            {
                                Log($"跳过 {task.PartitionName}: 镜像文件不存在", Color.Orange);
                            }
                            break;

                        case "erase":
                            // 保留数据模式下跳过 userdata 擦除
                            if (keepData && (task.PartitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase) ||
                                             task.PartitionName.Equals("metadata", StringComparison.OrdinalIgnoreCase)))
                            {
                                Log($"跳过擦除 {task.PartitionName} (保留数据)", Color.Gray);
                                taskSuccess = true;
                            }
                            else
                            {
                                taskSuccess = await _service.ErasePartitionAsync(task.PartitionName, _cts.Token);
                            }
                            break;

                        case "set_active":
                            string slot = task.PartitionName.Replace("slot_", "");
                            taskSuccess = await _service.SetActiveSlotAsync(slot, _cts.Token);
                            break;

                        case "reboot":
                            // 重启操作放在最后执行
                            if (i == total - 1)
                            {
                                // 如果需要锁定BL，在重启前执行
                                if (lockBl)
                                {
                                    Log("正在锁定 Bootloader...", Color.Blue);
                                    await _service.LockBootloaderAsync("flashing lock", _cts.Token);
                                }

                                string target = task.PartitionName.Replace("reboot_", "");
                                if (target == "system" || string.IsNullOrEmpty(target))
                                {
                                    taskSuccess = await _service.RebootAsync(_cts.Token);
                                }
                                else if (target == "bootloader")
                                {
                                    taskSuccess = await _service.RebootBootloaderAsync(_cts.Token);
                                }
                                else if (target == "recovery")
                                {
                                    taskSuccess = await _service.RebootRecoveryAsync(_cts.Token);
                                }
                            }
                            else
                            {
                                Log("跳过中间的重启命令", Color.Gray);
                                taskSuccess = true;
                            }
                            break;
                    }

                    // 子进度：当前任务完成
                    UpdateSubProgressBar(100);
                    
                    if (taskSuccess)
                        success++;
                    else
                        failed++;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                // 如果没有重启命令但需要锁定BL，在这里执行
                bool hasReboot = selectedTasks.Any(t => t.Operation == "reboot");
                if (lockBl && !hasReboot)
                {
                    Log("正在锁定 Bootloader...", Color.Blue);
                    await _service.LockBootloaderAsync("flashing lock", _cts.Token);
                }

                Log($"刷机完成: {success} 成功, {failed} 失败", 
                    failed == 0 ? Color.Green : Color.Orange);

                return failed == 0;
            }
            catch (OperationCanceledException)
            {
                Log("刷机操作已取消", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"刷机失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 扫描目录中的刷机脚本
        /// </summary>
        public List<string> ScanFlashScripts(string directory)
        {
            return BatScriptParser.FindFlashScripts(directory);
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatSize(long size)
        {
            if (size >= 1024L * 1024 * 1024)
                return $"{size / (1024.0 * 1024 * 1024):F2} GB";
            if (size >= 1024 * 1024)
                return $"{size / (1024.0 * 1024):F2} MB";
            if (size >= 1024)
                return $"{size / 1024.0:F2} KB";
            return $"{size} B";
        }

        #endregion

        #region Payload 解析

        /// <summary>
        /// 从 URL 加载远程 Payload (云端解析)
        /// </summary>
        public async Task<bool> LoadPayloadFromUrlAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Log("请输入 URL", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("操作进行中", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("解析云端 Payload");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "当前操作：解析云端 Payload");
                UpdateLabelSafe(_speedLabel, "速度：连接中...");

                Log($"正在解析云端 Payload...", Color.Blue);

                // 创建或重用 RemotePayloadService
                if (_remotePayloadService == null)
                {
                    _remotePayloadService = new RemotePayloadService(
                        msg => Log(msg, null),
                        (current, total) => UpdateProgressWithSpeed(current, total),
                        _logDetail
                    );

                    _remotePayloadService.ExtractProgressChanged += (s, e) =>
                    {
                        UpdateSubProgressBar(e.Percent);
                        // 更新速度显示
                        if (e.SpeedBytesPerSecond > 0)
                        {
                            UpdateSpeedLabel(e.SpeedFormatted);
                        }
                    };
                }

                // 先获取真实 URL (处理重定向)
                UpdateProgressBar(10);
                var (realUrl, expiresTime) = await _remotePayloadService.GetRedirectUrlAsync(url, _cts.Token);
                
                if (string.IsNullOrEmpty(realUrl))
                {
                    Log("无法获取下载链接", Color.Red);
                    UpdateProgressBar(0);
                    return false;
                }

                if (realUrl != url)
                {
                    Log("已获取真实下载链接", Color.Green);
                    if (expiresTime.HasValue)
                    {
                        Log($"链接过期时间: {expiresTime.Value:yyyy-MM-dd HH:mm:ss}", Color.Blue);
                    }
                }

                UpdateProgressBar(30);
                bool success = await _remotePayloadService.LoadFromUrlAsync(realUrl, _cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);

                    var summary = _remotePayloadService.GetSummary();
                    Log($"云端 Payload 解析成功: {summary.PartitionCount} 个分区", Color.Green);
                    Log($"文件大小: {summary.TotalSizeFormatted}", Color.Blue);

                    // 更新分区列表显示
                    UpdatePartitionListFromRemotePayload();

                    UpdateProgressBar(100);
                }
                else
                {
                    Log("云端 Payload 解析失败", Color.Red);
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"云端 Payload 加载失败: {ex.Message}", Color.Red);
                _logDetail($"云端 Payload 加载错误: {ex}");
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 从远程 Payload 更新分区列表
        /// </summary>
        private void UpdatePartitionListFromRemotePayload()
        {
            if (_partitionListView == null || _remotePayloadService == null || !_remotePayloadService.IsLoaded) return;

            try
            {
                if (_partitionListView.InvokeRequired)
                {
                    _partitionListView.BeginInvoke(new Action(UpdatePartitionListFromRemotePayloadInternal));
                }
                else
                {
                    UpdatePartitionListFromRemotePayloadInternal();
                }
            }
            catch { }
        }

        private void UpdatePartitionListFromRemotePayloadInternal()
        {
            try
            {
                _partitionListView.Items.Clear();

                foreach (var partition in _remotePayloadService.Partitions)
                {
                    var item = new ListViewItem(new string[]
                    {
                        partition.Name,
                        "云端提取",  // 操作列
                        partition.SizeFormatted,
                        $"{partition.Operations.Count} ops"  // 操作数
                    });

                    item.Tag = partition;
                    item.Checked = true;  // 默认勾选

                    // 标记常用分区
                    string name = partition.Name.ToLowerInvariant();
                    if (name.Contains("system") || name.Contains("vendor") || name.Contains("product"))
                    {
                        item.ForeColor = Color.Blue;
                    }
                    else if (name.Contains("boot") || name.Contains("dtbo") || name.Contains("vbmeta"))
                    {
                        item.ForeColor = Color.DarkGreen;
                    }

                    _partitionListView.Items.Add(item);
                }
            }
            catch { }
        }

        /// <summary>
        /// 从云端提取选中的分区
        /// </summary>
        public async Task<bool> ExtractSelectedRemotePartitionsAsync(string outputDir)
        {
            if (_remotePayloadService == null || !_remotePayloadService.IsLoaded)
            {
                Log("请先加载云端 Payload", Color.Orange);
                return false;
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                Log("请指定输出目录", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("操作进行中", Color.Orange);
                return false;
            }

            // 获取选中的分区名称
            var selectedNames = new List<string>();
            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    if (item.Tag is RemotePayloadPartition partition)
                    {
                        selectedNames.Add(partition.Name);
                    }
                }
            }
            catch { }

            if (selectedNames.Count == 0)
            {
                Log("请选择要提取的分区", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("云端提取分区");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：准备中...");

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                Log($"开始从云端提取 {selectedNames.Count} 个分区到: {outputDir}", Color.Blue);

                int success = 0;
                int total = selectedNames.Count;
                int currentIndex = 0;

                // 注册进度事件处理器
                EventHandler<RemoteExtractProgress> progressHandler = (s, e) =>
                {
                    // 子进度条：当前分区的提取进度
                    UpdateSubProgressBar(e.Percent);
                    // 更新速度显示
                    if (e.SpeedBytesPerSecond > 0)
                    {
                        UpdateSpeedLabel(e.SpeedFormatted);
                    }
                };

                _remotePayloadService.ExtractProgressChanged += progressHandler;

                try
                {
                    for (int i = 0; i < total; i++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        currentIndex = i;

                        string name = selectedNames[i];
                        string outputPath = Path.Combine(outputDir, $"{name}.img");

                        UpdateLabelSafe(_operationLabel, $"当前操作：云端提取 {name} ({i + 1}/{total})");
                        // 总进度：基于已完成的分区数
                        UpdateProgressBar((i * 100.0) / total);
                        // 子进度：开始提取
                        UpdateSubProgressBar(0);

                        if (await _remotePayloadService.ExtractPartitionAsync(name, outputPath, _cts.Token))
                        {
                            success++;
                            Log($"提取成功: {name}.img", Color.Green);
                        }
                        else
                        {
                            Log($"提取失败: {name}", Color.Red);
                        }
                        
                        // 子进度：当前分区提取完成
                        UpdateSubProgressBar(100);
                    }
                }
                finally
                {
                    _remotePayloadService.ExtractProgressChanged -= progressHandler;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"云端提取完成: {success}/{total} 成功", success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("提取操作已取消", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"提取失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 加载 Payload 文件 (支持 .bin 和 .zip)
        /// </summary>
        public async Task<bool> LoadPayloadAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Log("请选择 Payload 文件", Color.Orange);
                return false;
            }

            if (!File.Exists(filePath))
            {
                Log($"文件不存在: {filePath}", Color.Red);
                return false;
            }

            if (IsBusy)
            {
                Log("操作进行中", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("解析 Payload");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "当前操作：解析 Payload");
                UpdateLabelSafe(_speedLabel, "速度：解析中...");

                Log($"正在加载 Payload: {Path.GetFileName(filePath)}...", Color.Blue);

                // 创建或重用 PayloadService
                if (_payloadService == null)
                {
                    _payloadService = new PayloadService(
                        msg => Log(msg, null),
                        (current, total) => UpdateProgressWithSpeed(current, total),
                        _logDetail
                    );

                    _payloadService.ExtractProgressChanged += (s, e) =>
                    {
                        PayloadExtractProgress?.Invoke(this, e);
                        UpdateSubProgressBar(e.Percent);
                    };
                }

                UpdateProgressBar(30);
                bool success = await _payloadService.LoadPayloadAsync(filePath, _cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);

                    var summary = _payloadService.GetSummary();
                    Log($"Payload 解析成功: {summary.PartitionCount} 个分区", Color.Green);
                    Log($"总大小: {summary.TotalSizeFormatted}, 压缩后: {summary.TotalCompressedSizeFormatted}", Color.Blue);

                    // 更新分区列表显示
                    UpdatePartitionListFromPayload();

                    UpdateProgressBar(100);
                    PayloadLoaded?.Invoke(this, summary);
                }
                else
                {
                    Log("Payload 解析失败", Color.Red);
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"Payload 加载失败: {ex.Message}", Color.Red);
                _logDetail($"Payload 加载错误: {ex}");
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 从 Payload 更新分区列表
        /// </summary>
        private void UpdatePartitionListFromPayload()
        {
            if (_partitionListView == null || _payloadService == null || !_payloadService.IsLoaded) return;

            try
            {
                if (_partitionListView.InvokeRequired)
                {
                    _partitionListView.BeginInvoke(new Action(UpdatePartitionListFromPayloadInternal));
                }
                else
                {
                    UpdatePartitionListFromPayloadInternal();
                }
            }
            catch { }
        }

        private void UpdatePartitionListFromPayloadInternal()
        {
            try
            {
                _partitionListView.Items.Clear();

                foreach (var partition in _payloadService.Partitions)
                {
                    var item = new ListViewItem(new string[]
                    {
                        partition.Name,
                        "提取",  // 操作列
                        partition.SizeFormatted,
                        partition.CompressedSizeFormatted  // 压缩大小
                    });

                    item.Tag = partition;
                    item.Checked = true;  // 默认勾选

                    // 标记常用分区
                    string name = partition.Name.ToLowerInvariant();
                    if (name.Contains("system") || name.Contains("vendor") || name.Contains("product"))
                    {
                        item.ForeColor = Color.Blue;
                    }
                    else if (name.Contains("boot") || name.Contains("dtbo") || name.Contains("vbmeta"))
                    {
                        item.ForeColor = Color.DarkGreen;
                    }

                    _partitionListView.Items.Add(item);
                }
            }
            catch { }
        }

        /// <summary>
        /// 提取选中的 Payload 分区
        /// </summary>
        public async Task<bool> ExtractSelectedPayloadPartitionsAsync(string outputDir)
        {
            if (_payloadService == null || !_payloadService.IsLoaded)
            {
                Log("请先加载 Payload 文件", Color.Orange);
                return false;
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                Log("请指定输出目录", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("操作进行中", Color.Orange);
                return false;
            }

            // 获取选中的分区名称
            var selectedNames = new List<string>();
            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    if (item.Tag is PayloadPartition partition)
                    {
                        selectedNames.Add(partition.Name);
                    }
                }
            }
            catch { }

            if (selectedNames.Count == 0)
            {
                Log("请选择要提取的分区", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("提取 Payload 分区");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：准备中...");

                Log($"开始提取 {selectedNames.Count} 个分区到: {outputDir}", Color.Blue);

                int success = 0;
                int total = selectedNames.Count;

                for (int i = 0; i < total; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    string name = selectedNames[i];
                    string outputPath = Path.Combine(outputDir, $"{name}.img");

                    UpdateLabelSafe(_operationLabel, $"当前操作：提取 {name} ({i + 1}/{total})");
                    // 总进度：基于已完成的分区数
                    UpdateProgressBar((i * 100.0) / total);
                    // 子进度：开始提取
                    UpdateSubProgressBar(0);

                    if (await _payloadService.ExtractPartitionAsync(name, outputPath, _cts.Token))
                    {
                        success++;
                        Log($"提取成功: {name}.img", Color.Green);
                    }
                    else
                    {
                        Log($"提取失败: {name}", Color.Red);
                    }
                    
                    // 子进度：当前分区提取完成
                    UpdateSubProgressBar(100);
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"提取完成: {success}/{total} 成功", success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("提取操作已取消", Color.Orange);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"提取失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 提取 Payload 分区并直接刷写到设备
        /// </summary>
        public async Task<bool> FlashFromPayloadAsync()
        {
            if (_payloadService == null || !_payloadService.IsLoaded)
            {
                Log("请先加载 Payload 文件", Color.Orange);
                return false;
            }

            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            // 获取选中的分区
            var selectedPartitions = new List<PayloadPartition>();
            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    if (item.Tag is PayloadPartition partition)
                    {
                        selectedPartitions.Add(partition);
                    }
                }
            }
            catch { }

            if (selectedPartitions.Count == 0)
            {
                Log("请选择要刷写的分区", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("Payload 刷写");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：准备中...");

                Log($"开始从 Payload 刷写 {selectedPartitions.Count} 个分区...", Color.Blue);

                int success = 0;
                int total = selectedPartitions.Count;
                string tempDir = Path.Combine(Path.GetTempPath(), $"payload_flash_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    for (int i = 0; i < total; i++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();

                        var partition = selectedPartitions[i];
                        string tempPath = Path.Combine(tempDir, $"{partition.Name}.img");

                        UpdateLabelSafe(_operationLabel, $"当前操作：提取+刷写 {partition.Name} ({i + 1}/{total})");
                        // 总进度：基于已完成的分区数
                        UpdateProgressBar((i * 100.0) / total);
                        // 子进度：开始提取
                        UpdateSubProgressBar(0);

                        // 1. 提取分区
                        Log($"提取 {partition.Name}...", Color.Blue);
                        // 子进度：提取阶段 (0-50%)
                        UpdateSubProgressBar(10);
                        if (!await _payloadService.ExtractPartitionAsync(partition.Name, tempPath, _cts.Token))
                        {
                            Log($"提取 {partition.Name} 失败，跳过刷写", Color.Red);
                            continue;
                        }
                        
                        // 子进度：提取完成 (50%)
                        UpdateSubProgressBar(50);

                        // 2. 刷写分区
                        Log($"刷写 {partition.Name}...", Color.Blue);
                        var flashStart = DateTime.Now;
                        var fileSize = new FileInfo(tempPath).Length;
                        
                        if (await _service.FlashPartitionAsync(partition.Name, tempPath, false, _cts.Token))
                        {
                            success++;
                            Log($"刷写成功: {partition.Name}", Color.Green);
                            
                            // 计算并显示刷写速度
                            var elapsed = (DateTime.Now - flashStart).TotalSeconds;
                            if (elapsed > 0)
                            {
                                double speed = fileSize / elapsed;
                                UpdateSpeedLabel(FormatSpeed(speed));
                            }
                        }
                        else
                        {
                            Log($"刷写失败: {partition.Name}", Color.Red);
                        }
                        
                        // 子进度：刷写完成 (100%)
                        UpdateSubProgressBar(100);

                        // 3. 删除临时文件
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                finally
                {
                    // 清理临时目录
                    try { Directory.Delete(tempDir, true); } catch { }
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"Payload 刷写完成: {success}/{total} 成功", success == total ? Color.Green : Color.Orange);

                // 自动重启
                if (IsAutoRebootEnabled() && success > 0)
                {
                    await _service.RebootAsync(_cts.Token);
                }

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("刷写操作已取消", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"刷写失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 从云端 Payload 直接刷写分区到设备
        /// </summary>
        public async Task<bool> FlashFromRemotePayloadAsync()
        {
            if (_remotePayloadService == null || !_remotePayloadService.IsLoaded)
            {
                Log("请先解析云端 Payload", Color.Orange);
                return false;
            }

            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            // 获取选中的分区
            var selectedPartitions = new List<RemotePayloadPartition>();
            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    if (item.Tag is RemotePayloadPartition partition)
                    {
                        selectedPartitions.Add(partition);
                    }
                }
            }
            catch { }

            if (selectedPartitions.Count == 0)
            {
                Log("请选择要刷写的分区", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                StartOperationTimer("云端 Payload 刷写");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "速度：准备中...");

                Log($"开始从云端刷写 {selectedPartitions.Count} 个分区...", Color.Blue);

                int success = 0;
                int total = selectedPartitions.Count;

                // 注册流式刷写进度事件
                EventHandler<RemotePayloadService.StreamFlashProgressEventArgs> progressHandler = (s, e) =>
                {
                    // 总进度：基于已完成的分区数 + 当前分区的进度
                    double overallPercent = ((success * 100.0) + e.Percent) / total;
                    UpdateProgressBar(overallPercent);
                    // 子进度：当前分区的操作进度
                    UpdateSubProgressBar(e.Percent);
                    
                    // 根据阶段显示不同的速度
                    if (e.Phase == RemotePayloadService.StreamFlashPhase.Downloading)
                    {
                        UpdateSpeedLabel($"{e.DownloadSpeedFormatted} (下载)");
                    }
                    else if (e.Phase == RemotePayloadService.StreamFlashPhase.Flashing)
                    {
                        UpdateSpeedLabel($"{e.FlashSpeedFormatted} (刷写)");
                    }
                    else if (e.Phase == RemotePayloadService.StreamFlashPhase.Completed && e.FlashSpeedBytesPerSecond > 0)
                    {
                        UpdateSpeedLabel($"{e.FlashSpeedFormatted} (Fastboot)");
                    }
                };

                _remotePayloadService.StreamFlashProgressChanged += progressHandler;

                try
                {
                    for (int i = 0; i < total; i++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();

                        var partition = selectedPartitions[i];
                        
                        UpdateLabelSafe(_operationLabel, $"当前操作：下载+刷写 {partition.Name} ({i + 1}/{total})");

                        // 使用流式刷写
                        bool flashResult = await _remotePayloadService.ExtractAndFlashPartitionAsync(
                            partition.Name,
                            async (tempPath) =>
                            {
                                // 刷写回调 - 测量 Fastboot 通讯速度
                                var flashStartTime = DateTime.Now;
                                var fileInfo = new FileInfo(tempPath);
                                long fileSize = fileInfo.Length;
                                
                                bool flashSuccess = await _service.FlashPartitionAsync(
                                    partition.Name, tempPath, false, _cts.Token);
                                
                                var flashElapsed = (DateTime.Now - flashStartTime).TotalSeconds;
                                
                                return (flashSuccess, fileSize, flashElapsed);
                            },
                            _cts.Token
                        );

                        if (flashResult)
                        {
                            success++;
                            Log($"刷写成功: {partition.Name}", Color.Green);
                        }
                        else
                        {
                            Log($"刷写失败: {partition.Name}", Color.Red);
                        }
                    }
                }
                finally
                {
                    _remotePayloadService.StreamFlashProgressChanged -= progressHandler;
                }

                UpdateProgressBar(100);
                StopOperationTimer();

                if (success == total)
                {
                    Log($"✓ 全部 {total} 个分区刷写成功", Color.Green);
                }
                else
                {
                    Log($"刷写完成: {success}/{total} 成功", success > 0 ? Color.Orange : Color.Red);
                }

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("刷写操作已取消", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"刷写失败: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "当前操作：空闲");
            }
        }

        /// <summary>
        /// 关闭 Payload
        /// </summary>
        public void ClosePayload()
        {
            _payloadService?.Close();
            Log("Payload 已关闭", Color.Gray);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                StopDeviceMonitoring();
                _deviceRefreshTimer?.Dispose();
                _service?.Dispose();
                _payloadService?.Dispose();
                _remotePayloadService?.Dispose();
                _cts?.Dispose();
                _disposed = true;
            }
        }
    }
}
