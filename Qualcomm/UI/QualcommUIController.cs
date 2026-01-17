// ============================================================================
// LoveAlways - 高通 UI 控制器
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Qualcomm.Services;

namespace LoveAlways.Qualcomm.UI
{
    public class QualcommUIController : IDisposable
    {
        private QualcommService _service;
        private CancellationTokenSource _cts;
        private readonly Action<string, Color?> _log;
        private bool _disposed;

        // UI 控件引用 - 使用 dynamic 或反射来处理不同类型的控件
        private dynamic _portComboBox;
        private ListView _partitionListView;
        private dynamic _progressBar;        // 总进度条 (长)
        private dynamic _subProgressBar;     // 子进度条 (短)
        private dynamic _statusLabel;
        private dynamic _skipSaharaCheckbox;
        private dynamic _protectPartitionsCheckbox;
        private dynamic _programmerPathTextbox;
        private dynamic _outputPathTextbox;
        
        // 时间/速度/操作状态标签
        private dynamic _timeLabel;
        private dynamic _speedLabel;
        private dynamic _operationLabel;
        
        // 设备信息标签
        private dynamic _brandLabel;         // 品牌
        private dynamic _chipLabel;          // 芯片
        private dynamic _modelLabel;         // 设备型号
        private dynamic _serialLabel;        // 序列号
        private dynamic _storageLabel;       // 存储类型
        private dynamic _unlockLabel;        // 解锁状态
        private dynamic _otaVersionLabel;    // OTA版本
        
        // 计时器和速度计算
        private Stopwatch _operationStopwatch;
        private long _lastBytes;
        private DateTime _lastSpeedUpdate;
        private double _currentSpeed; // 当前速度 (bytes/s)
        
        // 总进度追踪
        private int _totalSteps;
        private int _currentStep;

        public bool IsConnected { get { return _service != null && _service.IsConnected; } }
        public bool IsBusy { get; private set; }
        public List<PartitionInfo> Partitions { get; private set; }

        /// <summary>
        /// 获取当前槽位 ("a", "b", "undefined", "nonexistent")
        /// </summary>
        public string GetCurrentSlot()
        {
            if (_service == null) return "nonexistent";
            return _service.CurrentSlot ?? "nonexistent";
        }

        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<List<PartitionInfo>> PartitionsLoaded;

        public QualcommUIController(Action<string, Color?> log = null)
        {
            _log = log ?? delegate { };
            Partitions = new List<PartitionInfo>();
        }

        public void BindControls(
            object portComboBox = null,
            ListView partitionListView = null,
            object progressBar = null,
            object statusLabel = null,
            object skipSaharaCheckbox = null,
            object protectPartitionsCheckbox = null,
            object programmerPathTextbox = null,
            object outputPathTextbox = null,
            object timeLabel = null,
            object speedLabel = null,
            object operationLabel = null,
            object subProgressBar = null,
            // 设备信息标签
            object brandLabel = null,
            object chipLabel = null,
            object modelLabel = null,
            object serialLabel = null,
            object storageLabel = null,
            object unlockLabel = null,
            object otaVersionLabel = null)
        {
            _portComboBox = portComboBox;
            _partitionListView = partitionListView;
            _progressBar = progressBar;
            _subProgressBar = subProgressBar;
            _statusLabel = statusLabel;
            _skipSaharaCheckbox = skipSaharaCheckbox;
            _protectPartitionsCheckbox = protectPartitionsCheckbox;
            _programmerPathTextbox = programmerPathTextbox;
            _outputPathTextbox = outputPathTextbox;
            _timeLabel = timeLabel;
            _speedLabel = speedLabel;
            _operationLabel = operationLabel;
            
            // 设备信息标签绑定
            _brandLabel = brandLabel;
            _chipLabel = chipLabel;
            _modelLabel = modelLabel;
            _serialLabel = serialLabel;
            _storageLabel = storageLabel;
            _unlockLabel = unlockLabel;
            _otaVersionLabel = otaVersionLabel;
        }

        /// <summary>
        /// 刷新端口列表
        /// </summary>
        /// <param name="silent">静默模式，不输出日志</param>
        /// <returns>检测到的EDL端口数量</returns>
        public int RefreshPorts(bool silent = false)
        {
            if (_portComboBox == null) return 0;

            try
            {
                var ports = PortDetector.DetectAllPorts();
                var edlPorts = PortDetector.DetectEdlPorts();
                
                _portComboBox.Items.Clear();

                if (ports.Count == 0)
                {
                    // 没有设备时显示默认文本
                    _portComboBox.Text = "设备状态：未连接任何设备";
                }
                else
                {
                foreach (var port in ports)
                {
                    string display = port.IsEdl
                        ? string.Format("{0} - {1} [EDL]", port.PortName, port.Description)
                        : string.Format("{0} - {1}", port.PortName, port.Description);
                    _portComboBox.Items.Add(display);
                }

                    // 优先选择EDL端口
                if (edlPorts.Count > 0)
                {
                    for (int i = 0; i < _portComboBox.Items.Count; i++)
                    {
                        if (_portComboBox.Items[i].ToString().Contains(edlPorts[0].PortName))
                        {
                            _portComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }
                else if (_portComboBox.Items.Count > 0)
                {
                    _portComboBox.SelectedIndex = 0;
                    }
                }

                return edlPorts.Count;
            }
            catch (Exception ex)
            {
                if (!silent)
            {
                Log(string.Format("刷新端口失败: {0}", ex.Message), Color.Red);
                }
                return 0;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            return await ConnectWithOptionsAsync("", "ufs", IsSkipSaharaEnabled(), "none");
        }

        public async Task<bool> ConnectWithOptionsAsync(string programmerPath, string storageType, bool skipSahara, string authMode)
        {
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName)) { Log("请选择端口", Color.Red); return false; }

            if (!skipSahara && string.IsNullOrEmpty(programmerPath))
            {
                Log("请选择引导文件", Color.Red);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                _service = new QualcommService(
                    msg => Log(msg, null),
                    (current, total) => UpdateProgress(current, total)
                );

                bool success;
                if (skipSahara)
                {
                    Log("跳过 Sahara，直接连接 Firehose...", Color.Blue);
                    success = await _service.ConnectFirehoseDirectAsync(portName, storageType, _cts.Token);
                }
                else
                {
                    Log(string.Format("连接设备 (存储: {0}, 认证: {1})...", storageType, authMode), Color.Blue);
                    success = await _service.ConnectAsync(portName, programmerPath, storageType, _cts.Token);
                    
                    // 执行认证
                    if (success && authMode != "none")
                    {
                        Log(string.Format("执行 {0} 认证...", authMode), Color.Blue);
                        bool authOk = await _service.AuthenticateAsync(authMode, _cts.Token);
                        if (!authOk)
                        {
                            Log("认证失败，但连接仍可用", Color.Orange);
                        }
                    }
                    
                    if (success)
                    {
                        SetSkipSaharaChecked(true);
                        Log("已自动勾选「跳过引导」", Color.Green);
                    }
                }

                if (success)
                {
                    Log("连接成功！", Color.Green);
                    UpdateDeviceInfoLabels();
                    ConnectionStateChanged?.Invoke(this, true);
                }
                else
                {
                    Log("连接失败", Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log("连接异常: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Disconnect()
        {
            if (_service != null)
            {
                _service.Disconnect();
                _service.Dispose();
                _service = null;
            }
            CancelOperation();
            ConnectionStateChanged?.Invoke(this, false);
            ClearDeviceInfoLabels();
            Log("已断开连接", Color.Gray);
        }

        #region 设备信息显示

        private DeviceInfoService _deviceInfoService;
        private DeviceFullInfo _currentDeviceInfo;

        /// <summary>
        /// 获取当前芯片信息
        /// </summary>
        public QualcommChipInfo ChipInfo
        {
            get { return _service != null ? _service.ChipInfo : null; }
        }

        /// <summary>
        /// 获取当前完整设备信息
        /// </summary>
        public DeviceFullInfo CurrentDeviceInfo
        {
            get { return _currentDeviceInfo; }
        }

        /// <summary>
        /// 更新设备信息标签 (Sahara + Firehose 模式获取的信息)
        /// </summary>
        public void UpdateDeviceInfoLabels()
        {
            if (_service == null) return;

            // 初始化设备信息服务
            if (_deviceInfoService == null)
            {
                _deviceInfoService = new DeviceInfoService(
                    msg => Log(msg, null),
                    msg => { } // 详细日志可选
                );
            }

            // 从 Qualcomm 服务获取设备信息
            _currentDeviceInfo = _deviceInfoService.GetInfoFromQualcommService(_service);

            var chipInfo = _service.ChipInfo;
            
            // Sahara 模式获取的信息
            if (chipInfo != null)
            {
                // 品牌 (从 PK Hash 或 OEM ID 识别)
                string brand = _currentDeviceInfo.Vendor;
                if (brand == "Unknown" && !string.IsNullOrEmpty(chipInfo.PkHash))
                {
                    brand = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                    _currentDeviceInfo.Vendor = brand;
                }
                UpdateLabelSafe(_brandLabel, "品牌：" + (brand != "Unknown" ? brand : "未识别"));
                
                // 芯片型号
                UpdateLabelSafe(_chipLabel, "芯片：" + (chipInfo.ChipName != "Unknown" ? chipInfo.ChipName : "未识别"));
                
                // 序列号
                UpdateLabelSafe(_serialLabel, "序列号：" + (!string.IsNullOrEmpty(chipInfo.SerialHex) ? chipInfo.SerialHex : "未获取"));
                
                // 设备型号 - 需要从 Firehose 读取分区信息后才能获取
                UpdateLabelSafe(_modelLabel, "设备型号：读取分区后获取");
            }
            
            // Firehose 模式获取的信息
            string storageType = _service.StorageType ?? "ufs";
            int sectorSize = _service.SectorSize;
            UpdateLabelSafe(_storageLabel, string.Format("存储：{0} ({1}B)", storageType.ToUpper(), sectorSize));
            
            // 解锁状态 - 需要读取特定分区判断
            UpdateLabelSafe(_unlockLabel, "解锁状态：检测中...");
            
            // OTA版本 - 需要读取 GPT 后从分区内容获取
            UpdateLabelSafe(_otaVersionLabel, "OTA版本：读取分区后获取");
        }

        /// <summary>
        /// 读取分区表后更新更多设备信息
        /// </summary>
        public void UpdateDeviceInfoFromPartitions()
        {
            if (_service == null || Partitions == null || Partitions.Count == 0) return;

            if (_currentDeviceInfo == null)
            {
                _currentDeviceInfo = new DeviceFullInfo();
            }

            // 尝试从分区推断设备信息
            string model = GetDeviceModelFromPartitions();
            if (!string.IsNullOrEmpty(model))
            {
                _currentDeviceInfo.Model = model;
                UpdateLabelSafe(_modelLabel, "设备型号：" + model);
            }
            
            // 检查 A/B 分区结构 (通常有 _a 和 _b 后缀)
            bool hasAbSlot = Partitions.Exists(p => p.Name.EndsWith("_a") || p.Name.EndsWith("_b"));
            _currentDeviceInfo.IsAbDevice = hasAbSlot;
            
            // 检查 OPLUS 特有分区
            bool isOplus = Partitions.Exists(p => 
                p.Name.StartsWith("my_") || p.Name.Contains("oplus") || p.Name.Contains("oppo"));
            
            // 检查 Xiaomi 特有分区
            bool isXiaomi = Partitions.Exists(p => 
                p.Name == "cust" || p.Name == "persist" || p.Name.Contains("xiaomi"));
            
            // 检查 OnePlus 特有分区
            bool isOnePlus = Partitions.Exists(p => 
                p.Name.Contains("op") && p.Name.Contains("oem"));
            
            // 更新品牌信息
            if (string.IsNullOrEmpty(_currentDeviceInfo.Brand) || _currentDeviceInfo.Brand == "Unknown")
            {
                if (isOplus) _currentDeviceInfo.Brand = "OPPO/Realme";
                else if (isXiaomi) _currentDeviceInfo.Brand = "Xiaomi";
                else if (isOnePlus) _currentDeviceInfo.Brand = "OnePlus";
                
                if (!string.IsNullOrEmpty(_currentDeviceInfo.Brand))
                {
                    UpdateLabelSafe(_brandLabel, "品牌：" + _currentDeviceInfo.Brand);
                }
            }
            
            // 构建 OTA 版本信息
            string otaInfo = hasAbSlot ? "A/B 分区" : "传统分区";
            bool hasSuper = Partitions.Exists(p => p.Name == "super");
            if (hasSuper)
            {
                otaInfo += " (Dynamic)";
            }
            _currentDeviceInfo.OtaVersion = otaInfo;
            UpdateLabelSafe(_otaVersionLabel, "OTA版本：" + otaInfo);
            
            // 检查解锁状态 - 基于关键分区是否可访问
            bool hasSecureBoot = Partitions.Exists(p => 
                p.Name == "sbl1" || p.Name == "xbl" || p.Name == "abl" || 
                p.Name == "xbl_a" || p.Name == "abl_a");
            UpdateLabelSafe(_unlockLabel, "解锁状态：" + (hasSecureBoot ? "EDL 模式" : "未知"));
        }

        /// <summary>
        /// 从分区列表推断设备型号
        /// </summary>
        private string GetDeviceModelFromPartitions()
        {
            if (Partitions == null || Partitions.Count == 0) return null;

            // 基于芯片信息
            var chipInfo = ChipInfo;
            if (chipInfo != null)
            {
                string vendor = chipInfo.Vendor;
                if (vendor == "Unknown")
                    vendor = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                
                if (vendor != "Unknown" && chipInfo.ChipName != "Unknown")
                {
                    return string.Format("{0} ({1})", vendor, chipInfo.ChipName);
                }
            }

            // 基于特征分区名推断设备类型
            bool isOnePlus = Partitions.Exists(p => p.Name.Contains("oem") && p.Name.Contains("op"));
            bool isXiaomi = Partitions.Exists(p => p.Name.Contains("cust") || p.Name == "persist");
            bool isOppo = Partitions.Exists(p => p.Name.Contains("oplus") || p.Name.Contains("my_"));

            if (isOnePlus) return "OnePlus";
            if (isXiaomi) return "Xiaomi";
            if (isOppo) return "OPPO/Realme";
            
            return null;
        }

        /// <summary>
        /// 内部方法：尝试读取 build.prop（不检查 IsBusy）
        /// </summary>
        private async Task TryReadBuildPropInternalAsync()
        {
            try
            {
                if (_deviceInfoService == null)
                {
                    _deviceInfoService = new DeviceInfoService(
                        msg => Log(msg, null),
                        msg => { }
                    );
                }

                // 创建从 super 分区读取数据的委托
                DeviceInfoService.DeviceReadDelegate readFromSuper = (offset, size) =>
                {
                    try
                    {
                        var task = _service.ReadPartitionDataAsync("super", offset, size, _cts.Token);
                        task.Wait(_cts.Token);
                        return task.Result;
                    }
                    catch
                    {
                        return null;
                    }
                };

                // 读取并解析 build.prop
                var buildProp = await Task.Run(() => _deviceInfoService.ReadBuildPropFromDevice(readFromSuper));

                if (buildProp != null)
                {
                    Log("成功读取设备 build.prop", Color.Green);
                    ApplyBuildPropInfo(buildProp);
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("读取设备信息失败: {0}", ex.Message), Color.Orange);
            }
        }

        /// <summary>
        /// 应用 build.prop 信息到界面
        /// </summary>
        private void ApplyBuildPropInfo(BuildPropInfo buildProp)
        {
            if (buildProp == null) return;

            if (_currentDeviceInfo == null)
            {
                _currentDeviceInfo = new DeviceFullInfo();
            }

            // 市场名称 (最高优先级)
            if (!string.IsNullOrEmpty(buildProp.MarketName))
            {
                _currentDeviceInfo.MarketName = buildProp.MarketName;
                UpdateLabelSafe(_modelLabel, "设备型号：" + buildProp.MarketName);
                Log(string.Format("  设备名称: {0}", buildProp.MarketName), Color.Green);
            }
            else if (!string.IsNullOrEmpty(buildProp.Model))
            {
                _currentDeviceInfo.Model = buildProp.Model;
                UpdateLabelSafe(_modelLabel, "设备型号：" + buildProp.Model);
                Log(string.Format("  型号: {0}", buildProp.Model), Color.Green);
            }

            // 品牌
            if (!string.IsNullOrEmpty(buildProp.Brand) && buildProp.Brand != "oplus")
            {
                _currentDeviceInfo.Brand = buildProp.Brand;
                UpdateLabelSafe(_brandLabel, "品牌：" + buildProp.Brand);
            }

            // Android 版本
            if (!string.IsNullOrEmpty(buildProp.AndroidVersion))
            {
                _currentDeviceInfo.AndroidVersion = buildProp.AndroidVersion;
            }

            // OTA 版本
            if (!string.IsNullOrEmpty(buildProp.DisplayId))
            {
                _currentDeviceInfo.OtaVersion = buildProp.DisplayId;
                UpdateLabelSafe(_otaVersionLabel, "OTA版本：" + buildProp.DisplayId);
            }
            else if (!string.IsNullOrEmpty(buildProp.OtaVersion))
            {
                _currentDeviceInfo.OtaVersion = buildProp.OtaVersion;
                UpdateLabelSafe(_otaVersionLabel, "OTA版本：" + buildProp.OtaVersion);
            }

            // 安全补丁
            if (!string.IsNullOrEmpty(buildProp.SecurityPatch))
            {
                _currentDeviceInfo.SecurityPatch = buildProp.SecurityPatch;
            }
        }

        /// <summary>
        /// 从设备 Super 分区在线读取 build.prop 并更新设备信息（公开方法，可单独调用）
        /// </summary>
        public async Task<bool> ReadBuildPropFromDeviceAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            // 检查是否有 super 分区
            bool hasSuper = Partitions != null && Partitions.Exists(p => p.Name == "super");
            if (!hasSuper)
            {
                Log("未找到 super 分区，无法读取 build.prop", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("读取设备信息", 1, 0);
                Log("正在从设备读取 build.prop...", Color.Blue);

                await TryReadBuildPropInternalAsync();
                
                UpdateTotalProgress(1, 1);
                return _currentDeviceInfo != null && !string.IsNullOrEmpty(_currentDeviceInfo.MarketName);
            }
            catch (Exception ex)
            {
                Log("读取 build.prop 失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 清空设备信息标签
        /// </summary>
        public void ClearDeviceInfoLabels()
        {
            _currentDeviceInfo = null;
            UpdateLabelSafe(_brandLabel, "品牌：无法获取");
            UpdateLabelSafe(_chipLabel, "芯片：无法获取");
            UpdateLabelSafe(_modelLabel, "设备型号：无法获取");
            UpdateLabelSafe(_serialLabel, "序列号：无法获取");
            UpdateLabelSafe(_storageLabel, "存储：无法获取");
            UpdateLabelSafe(_unlockLabel, "解锁状态：无法获取");
            UpdateLabelSafe(_otaVersionLabel, "OTA版本：无法获取");
        }

        #endregion

        #region VIP 认证

        /// <summary>
        /// 手动执行 VIP 认证 (OPPO/Realme 等设备)
        /// </summary>
        /// <param name="digestPath">Digest 文件路径 (elf/bin/mbn)</param>
        /// <param name="signaturePath">Signature 文件路径</param>
        /// <returns>认证是否成功</returns>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("VIP 认证", 1, 0);
                Log("正在执行 VIP 认证...", Color.Blue);

                bool success = await _service.PerformVipAuthManualAsync(digestPath, signaturePath, _cts.Token);
                
                UpdateTotalProgress(1, 1);

                if (success)
                {
                    Log("VIP 认证成功", Color.Green);
                }
                else
                {
                    Log("VIP 认证失败", Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log("VIP 认证失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 检查当前设备是否为 VIP 设备
        /// </summary>
        public bool IsVipDevice
        {
            get { return _service != null && _service.IsVipDevice; }
        }

        /// <summary>
        /// 获取设备厂商信息
        /// </summary>
        public string DeviceVendor
        {
            get
            {
                if (_service == null || _service.ChipInfo == null) return "Unknown";
                return QualcommDatabase.GetVendorByPkHash(_service.ChipInfo.PkHash);
            }
        }

        #endregion

        public async Task<bool> ReadPartitionTableAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                
                // 读取分区表：6个LUN
                int maxLuns = 6;
                StartOperationTimer("读取分区表", maxLuns, 0);
                Log("正在读取分区表 (GPT)...", Color.Blue);

                // 进度回调
                var totalProgress = new Progress<Tuple<int, int>>(t => UpdateTotalProgress(t.Item1, t.Item2));
                var subProgress = new Progress<int>(p => UpdateSubProgressFromPercent(p));

                // 使用带进度的 ReadAllGptAsync
                var partitions = await _service.ReadAllGptAsync(maxLuns, totalProgress, subProgress, _cts.Token);
                
                // 更新总进度到100%
                UpdateTotalProgress(maxLuns, maxLuns);

                if (partitions != null && partitions.Count > 0)
                {
                    Partitions = partitions;
                    UpdatePartitionListView(partitions);
                    UpdateDeviceInfoFromPartitions();  // 更新设备信息（从分区获取更多信息）
                    PartitionsLoaded?.Invoke(this, partitions);
                    Log(string.Format("成功读取 {0} 个分区", partitions.Count), Color.Green);
                    
                    // 如果有 super 分区，尝试自动读取 build.prop
                    bool hasSuper = partitions.Exists(p => p.Name == "super");
                    if (hasSuper)
                    {
                        Log("检测到 super 分区，尝试读取设备信息...", Color.Blue);
                        await TryReadBuildPropInternalAsync();
                    }
                    
                    return true;
                }
                else
                {
                    Log("未读取到分区", Color.Orange);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("读取分区表失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("读取 " + partitionName, 1, 0);
                Log(string.Format("正在读取分区 {0}...", partitionName), Color.Blue);

                var progress = new Progress<int>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                UpdateTotalProgress(1, 1);

                if (success) Log(string.Format("分区 {0} 已保存到 {1}", partitionName, outputPath), Color.Green);
                else Log(string.Format("读取 {0} 失败", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("读取分区失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        public async Task<bool> WritePartitionAsync(string partitionName, string filePath)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }
            if (!File.Exists(filePath)) { Log("文件不存在: " + filePath, Color.Red); return false; }

            if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
            {
                Log(string.Format("跳过敏感分区: {0}", partitionName), Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("写入 " + partitionName, 1, 0);
                Log(string.Format("正在写入分区 {0}...", partitionName), Color.Blue);

                var progress = new Progress<int>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.WritePartitionAsync(partitionName, filePath, progress, _cts.Token);

                UpdateTotalProgress(1, 1);

                if (success) Log(string.Format("分区 {0} 写入成功", partitionName), Color.Green);
                else Log(string.Format("写入 {0} 失败", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("写入分区失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
            {
                Log(string.Format("跳过敏感分区: {0}", partitionName), Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("擦除 " + partitionName, 1, 0);
                Log(string.Format("正在擦除分区 {0}...", partitionName), Color.Blue);

                // 擦除没有细粒度进度，模拟进度
                UpdateProgressBarDirect(_subProgressBar, 50);

                bool success = await _service.ErasePartitionAsync(partitionName, _cts.Token);

                UpdateProgressBarDirect(_subProgressBar, 100);
                UpdateTotalProgress(1, 1);

                if (success) Log(string.Format("分区 {0} 已擦除", partitionName), Color.Green);
                else Log(string.Format("擦除 {0} 失败", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("擦除分区失败: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        #region 批量操作 (支持双进度条)

        /// <summary>
        /// 批量读取分区
        /// </summary>
        public async Task<int> ReadPartitionsBatchAsync(List<Tuple<string, string>> partitionsToRead)
        {
            if (!EnsureConnected()) return 0;
            if (IsBusy) { Log("操作进行中", Color.Orange); return 0; }

            int total = partitionsToRead.Count;
            int success = 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("批量读取", total, 0);
                Log(string.Format("开始批量读取 {0} 个分区...", total), Color.Blue);

                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var item = partitionsToRead[i];
                    string partitionName = item.Item1;
                    string outputPath = item.Item2;

                    UpdateTotalProgress(i, total);
                    UpdateLabelSafe(_operationLabel, string.Format("读取 {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<int>(p => UpdateSubProgressFromPercent(p));
                    bool ok = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                    if (ok)
                    {
                        success++;
                        Log(string.Format("[{0}/{1}] {2} 读取成功", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} 读取失败", i + 1, total, partitionName), Color.Red);
                    }
                }

                UpdateTotalProgress(total, total);
                Log(string.Format("批量读取完成: {0}/{1} 成功", success, total), success == total ? Color.Green : Color.Orange);
                return success;
            }
            catch (Exception ex)
            {
                Log("批量读取失败: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 批量写入分区 (简单版本)
        /// </summary>
        public async Task<int> WritePartitionsBatchAsync(List<Tuple<string, string>> partitionsToWrite)
        {
            // 转换为新格式 (使用 LUN=0, StartSector=0 作为占位)
            var converted = partitionsToWrite.Select(t => Tuple.Create(t.Item1, t.Item2, 0, 0L)).ToList();
            return await WritePartitionsBatchAsync(converted, null, false);
        }

        /// <summary>
        /// 批量写入分区 (支持 Patch 和激活启动分区)
        /// </summary>
        /// <param name="partitionsToWrite">分区信息列表 (名称, 文件路径, LUN, StartSector)</param>
        /// <param name="patchFiles">Patch XML 文件列表 (可选)</param>
        /// <param name="activateBootLun">是否激活启动 LUN (UFS)</param>
        public async Task<int> WritePartitionsBatchAsync(List<Tuple<string, string, int, long>> partitionsToWrite, List<string> patchFiles, bool activateBootLun)
        {
            if (!EnsureConnected()) return 0;
            if (IsBusy) { Log("操作进行中", Color.Orange); return 0; }

            int total = partitionsToWrite.Count;
            int success = 0;
            bool hasPatch = patchFiles != null && patchFiles.Count > 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                
                // 计算总步骤: 分区写入 + Patch + 激活
                int totalSteps = total + (hasPatch ? 1 : 0) + (activateBootLun ? 1 : 0);
                StartOperationTimer("批量写入", totalSteps, 0);
                Log(string.Format("开始批量写入 {0} 个分区...", total), Color.Blue);

                // 1. 写入所有分区
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var item = partitionsToWrite[i];
                    string partitionName = item.Item1;
                    string filePath = item.Item2;
                    int lun = item.Item3;
                    long startSector = item.Item4;

                    if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
                    {
                        Log(string.Format("[{0}/{1}] 跳过敏感分区: {2}", i + 1, total, partitionName), Color.Orange);
                        continue;
                    }

                    UpdateTotalProgress(i, totalSteps);
                    UpdateLabelSafe(_operationLabel, string.Format("写入 {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<int>(p => UpdateSubProgressFromPercent(p));
                    bool ok;

                    // PrimaryGPT/BackupGPT 等特殊分区使用直接写入
                    if (partitionName == "PrimaryGPT" || partitionName == "BackupGPT" || 
                        partitionName.StartsWith("gpt_main") || partitionName.StartsWith("gpt_backup"))
                    {
                        ok = await _service.WriteDirectAsync(partitionName, filePath, lun, startSector, progress, _cts.Token);
                    }
                    else
                    {
                        ok = await _service.WritePartitionAsync(partitionName, filePath, progress, _cts.Token);
                    }

                    if (ok)
                    {
                        success++;
                        Log(string.Format("[{0}/{1}] {2} 写入成功", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} 写入失败", i + 1, total, partitionName), Color.Red);
                    }
                }

                Log(string.Format("分区写入完成: {0}/{1} 成功", success, total), success == total ? Color.Green : Color.Orange);

                // 2. 应用 Patch (如果有)
                Log(string.Format("[调试] hasPatch={0}, 取消={1}, patchFiles数量={2}", 
                    hasPatch, _cts.Token.IsCancellationRequested, patchFiles != null ? patchFiles.Count : 0), Color.Gray);
                    
                if (hasPatch && !_cts.Token.IsCancellationRequested)
                {
                    UpdateTotalProgress(total, totalSteps);
                    UpdateLabelSafe(_operationLabel, "应用补丁...");
                    Log(string.Format("开始应用 {0} 个 Patch 文件...", patchFiles.Count), Color.Blue);

                    int patchCount = await _service.ApplyPatchFilesAsync(patchFiles, _cts.Token);
                    Log(string.Format("成功应用 {0} 个补丁", patchCount), patchCount > 0 ? Color.Green : Color.Orange);
                }
                else if (!hasPatch)
                {
                    Log("无 Patch 文件，跳过补丁步骤", Color.Gray);
                }

                // 3. 修复 GPT (关键步骤！修复主备 GPT 和 CRC)
                if (!_cts.Token.IsCancellationRequested)
                {
                    UpdateLabelSafe(_operationLabel, "修复 GPT...");
                    Log("修复 GPT 分区表 (主备同步 + CRC)...", Color.Blue);
                    
                    // 修复所有 LUN 的 GPT (-1 表示所有 LUN)
                    bool fixOk = await _service.FixGptAsync(-1, _cts.Token);
                    if (fixOk)
                        Log("GPT 修复成功", Color.Green);
                    else
                        Log("GPT 修复失败 (可能导致无法启动)", Color.Orange);
                }

                // 4. 激活启动分区 (UFS 设备需要激活，eMMC 只有 LUN0)
                if (activateBootLun && !_cts.Token.IsCancellationRequested)
                {
                    UpdateTotalProgress(total + (hasPatch ? 1 : 0), totalSteps);
                    UpdateLabelSafe(_operationLabel, "回读分区表检测槽位...");
                    
                    // 回读 GPT 检测当前槽位
                    Log("回读 GPT 检测当前槽位...", Color.Blue);
                    var partitions = await _service.ReadAllGptAsync(6, _cts.Token);
                    
                    string currentSlot = _service.CurrentSlot;
                    Log(string.Format("检测到当前槽位: {0}", currentSlot), Color.Blue);

                    // 根据槽位确定启动 LUN - 严格按照 A/B 分区状态
                    int bootLun = -1;
                    string bootSlotName = "";
                    
                    if (currentSlot == "a")
                    {
                        bootLun = 1;  // slot_a -> LUN1
                        bootSlotName = "boot_a";
                    }
                    else if (currentSlot == "b")
                    {
                        bootLun = 2;  // slot_b -> LUN2
                        bootSlotName = "boot_b";
                    }
                    else if (currentSlot == "undefined" || currentSlot == "unknown")
                    {
                        // A/B 分区存在但未设置激活状态，尝试从写入的分区推断
                        // 检查是否写入了 _a 或 _b 后缀的分区
                        int slotACount = partitionsToWrite.Count(p => p.Item1.EndsWith("_a"));
                        int slotBCount = partitionsToWrite.Count(p => p.Item1.EndsWith("_b"));
                        
                        if (slotACount > slotBCount)
                        {
                            bootLun = 1;
                            bootSlotName = "boot_a (根据写入分区推断)";
                            Log("槽位未激活，根据写入的 _a 分区推断使用 LUN1", Color.Blue);
                        }
                        else if (slotBCount > slotACount)
                        {
                            bootLun = 2;
                            bootSlotName = "boot_b (根据写入分区推断)";
                            Log("槽位未激活，根据写入的 _b 分区推断使用 LUN2", Color.Blue);
                        }
                        else
                        {
                            // 无法推断，跳过激活
                            Log("无法确定槽位，跳过启动分区激活 (建议手动设置)", Color.Orange);
                        }
                    }
                    else if (currentSlot == "nonexistent")
                    {
                        // 设备不支持 A/B 分区，跳过激活
                        Log("设备不支持 A/B 分区，跳过启动分区激活", Color.Gray);
                    }

                    // 只有在确定了 bootLun 后才执行激活
                    if (bootLun > 0)
                    {
                        UpdateLabelSafe(_operationLabel, string.Format("激活启动分区 LUN{0}...", bootLun));
                        Log(string.Format("激活 LUN{0} ({1})...", bootLun, bootSlotName), Color.Blue);

                        bool bootOk = await _service.SetBootLunAsync(bootLun, _cts.Token);
                        if (bootOk)
                            Log(string.Format("LUN{0} 激活成功", bootLun), Color.Green);
                        else
                            Log(string.Format("LUN{0} 激活失败 (部分设备可能不支持)", bootLun), Color.Orange);
                    }
                }

                UpdateTotalProgress(totalSteps, totalSteps);
                return success;
            }
            catch (Exception ex)
            {
                Log("批量写入失败: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 应用 Patch 文件
        /// </summary>
        public async Task<int> ApplyPatchFilesAsync(List<string> patchFiles)
        {
            if (!EnsureConnected()) return 0;
            if (IsBusy) { Log("操作进行中", Color.Orange); return 0; }
            if (patchFiles == null || patchFiles.Count == 0) return 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("应用补丁", patchFiles.Count, 0);
                Log(string.Format("开始应用 {0} 个 Patch 文件...", patchFiles.Count), Color.Blue);

                int totalPatches = 0;
                for (int i = 0; i < patchFiles.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    UpdateTotalProgress(i, patchFiles.Count);
                    UpdateLabelSafe(_operationLabel, string.Format("Patch {0}/{1}", i + 1, patchFiles.Count));

                    int count = await _service.ApplyPatchXmlAsync(patchFiles[i], _cts.Token);
                    totalPatches += count;
                    Log(string.Format("[{0}/{1}] {2}: {3} 个补丁", i + 1, patchFiles.Count, 
                        Path.GetFileName(patchFiles[i]), count), Color.Green);
                }

                UpdateTotalProgress(patchFiles.Count, patchFiles.Count);
                Log(string.Format("Patch 完成: 共 {0} 个补丁", totalPatches), Color.Green);
                return totalPatches;
            }
            catch (Exception ex)
            {
                Log("应用 Patch 失败: " + ex.Message, Color.Red);
                return 0;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 批量擦除分区
        /// </summary>
        public async Task<int> ErasePartitionsBatchAsync(List<string> partitionNames)
        {
            if (!EnsureConnected()) return 0;
            if (IsBusy) { Log("操作进行中", Color.Orange); return 0; }

            int total = partitionNames.Count;
            int success = 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("批量擦除", total, 0);
                Log(string.Format("开始批量擦除 {0} 个分区...", total), Color.Blue);

                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    string partitionName = partitionNames[i];

                    if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
                    {
                        Log(string.Format("[{0}/{1}] 跳过敏感分区: {2}", i + 1, total, partitionName), Color.Orange);
                        continue;
                    }

                    UpdateTotalProgress(i, total);
                    UpdateLabelSafe(_operationLabel, string.Format("擦除 {0} ({1}/{2})", partitionName, i + 1, total));

                    // 擦除没有细粒度进度，直接更新子进度
                    UpdateProgressBarDirect(_subProgressBar, 50);
                    
                    bool ok = await _service.ErasePartitionAsync(partitionName, _cts.Token);

                    UpdateProgressBarDirect(_subProgressBar, 100);

                    if (ok)
                    {
                        success++;
                        Log(string.Format("[{0}/{1}] {2} 擦除成功", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} 擦除失败", i + 1, total, partitionName), Color.Red);
                    }
                }

                UpdateTotalProgress(total, total);
                Log(string.Format("批量擦除完成: {0}/{1} 成功", success, total), success == total ? Color.Green : Color.Orange);
                return success;
            }
            catch (Exception ex)
            {
                Log("批量擦除失败: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        #endregion

        public async Task<bool> RebootToEdlAsync()
        {
            if (!EnsureConnected()) return false;
            try
            {
                bool success = await _service.RebootToEdlAsync(_cts?.Token ?? CancellationToken.None);
                if (success) Log("已发送重启到 EDL 命令", Color.Green);
                return success;
            }
            catch (Exception ex) { Log("重启到 EDL 失败: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> RebootToSystemAsync()
        {
            if (!EnsureConnected()) return false;
            try
            {
                bool success = await _service.RebootAsync(_cts?.Token ?? CancellationToken.None);
                if (success) { Log("设备正在重启到系统", Color.Green); Disconnect(); }
                return success;
            }
            catch (Exception ex) { Log("重启失败: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> SwitchSlotAsync(string slot)
        {
            if (!EnsureConnected()) return false;
            try
            {
                bool success = await _service.SetActiveSlotAsync(slot, _cts?.Token ?? CancellationToken.None);
                if (success) Log(string.Format("已切换到槽位 {0}", slot), Color.Green);
                else Log("切换槽位失败", Color.Red);
                return success;
            }
            catch (Exception ex) { Log("切换槽位失败: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> SetBootLunAsync(int lun)
        {
            if (!EnsureConnected()) return false;
            try
            {
                bool success = await _service.SetBootLunAsync(lun, _cts?.Token ?? CancellationToken.None);
                if (success) Log(string.Format("LUN {0} 已激活", lun), Color.Green);
                else Log("激活 LUN 失败", Color.Red);
                return success;
            }
            catch (Exception ex) { Log("激活 LUN 失败: " + ex.Message, Color.Red); return false; }
        }

        public PartitionInfo FindPartition(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;
            foreach (var p in Partitions)
            {
                if (p.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
            }
            return null;
        }

        private string GetSelectedPortName()
        {
            try
            {
                if (_portComboBox == null) return "";
                object selectedItem = _portComboBox.SelectedItem;
                if (selectedItem == null) return "";
                string item = selectedItem.ToString();
                int idx = item.IndexOf(" - ");
                return idx > 0 ? item.Substring(0, idx) : item;
            }
            catch { return ""; }
        }

        private bool IsProtectPartitionsEnabled()
        {
            try 
            { 
                if (_protectPartitionsCheckbox == null) return false;
                bool isChecked = _protectPartitionsCheckbox.Checked;
                return isChecked; 
            }
            catch { return false; }
        }

        private bool IsSkipSaharaEnabled()
        {
            try { return _skipSaharaCheckbox != null && (bool)_skipSaharaCheckbox.Checked; }
            catch { return false; }
        }

        private string GetProgrammerPath()
        {
            try { return _programmerPathTextbox != null ? (string)_programmerPathTextbox.Text : ""; }
            catch { return ""; }
        }

        private void SetSkipSaharaChecked(bool value)
        {
            try { if (_skipSaharaCheckbox != null) _skipSaharaCheckbox.Checked = value; }
            catch { }
        }

        private bool EnsureConnected()
        {
            if (!IsConnected) { Log("未连接设备", Color.Red); return false; }
            return true;
        }

        /// <summary>
        /// 取消当前操作
        /// </summary>
        public void CancelOperation()
        {
            if (_cts != null) 
            { 
                Log("正在取消操作...", Color.Orange);
                _cts.Cancel(); 
                _cts.Dispose(); 
                _cts = null; 
            }
        }

        /// <summary>
        /// 是否有操作正在进行
        /// </summary>
        public bool HasPendingOperation
        {
            get { return _cts != null && !_cts.IsCancellationRequested; }
        }

        private void Log(string message, Color? color)
        {
            _log(message, color);
        }

        private void UpdateProgress(long current, long total)
        {
            // 实时计算速度 (current=已传输字节, total=总字节)
            if (total > 0 && _operationStopwatch != null)
            {
                // 计算实时速度
                long bytesDelta = current - _lastBytes;
                double timeDelta = (DateTime.Now - _lastSpeedUpdate).TotalSeconds;
                
                if (timeDelta >= 0.15 && bytesDelta > 0) // 每150ms更新一次
                {
                    double instantSpeed = bytesDelta / timeDelta;
                    // 指数移动平均平滑速度
                    _currentSpeed = (_currentSpeed > 0) ? (_currentSpeed * 0.6 + instantSpeed * 0.4) : instantSpeed;
                    _lastBytes = current;
                    _lastSpeedUpdate = DateTime.Now;
                    
                    // 更新速度显示
                    UpdateSpeedDisplayInternal();
                    
                    // 更新时间
                    var elapsed = _operationStopwatch.Elapsed;
                    string timeText = string.Format("时间：{0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
                    UpdateLabelSafe(_timeLabel, timeText);
                }
                
                // 更新子进度条 (单个操作实时进度)
                int subPercent = (int)(100.0 * current / total);
                subPercent = Math.Max(0, Math.Min(100, subPercent));
                UpdateProgressBarDirect(_subProgressBar, subPercent);
                
                // 更新总进度条 (实时) - 计算当前步骤在总进度中的位置
                if (_totalSteps > 0 && _progressBar != null)
                {
                    // 总进度 = (已完成步骤 + 当前步骤进度) / 总步骤
                    double totalProgress = (_currentStep + subPercent / 100.0) / _totalSteps * 100.0;
                    int totalPercent = (int)Math.Max(0, Math.Min(100, totalProgress));
                    UpdateProgressBarDirect(_progressBar, totalPercent);
                }
            }
        }
        
        /// <summary>
        /// 直接更新进度条 (从服务进度回调)
        /// </summary>
        private void UpdateProgressBarDirect(dynamic progressBar, int percent)
        {
            if (progressBar == null) return;
            try
            {
                if (progressBar.InvokeRequired)
                {
                    progressBar.BeginInvoke(new Action(() => {
                        progressBar.Value = percent;
                        progressBar.Update();
                    }));
                }
                else
                {
                    progressBar.Value = percent;
                    progressBar.Update();
                }
            }
            catch { }
        }
        
        private void UpdateSpeedDisplayInternal()
        {
            if (_speedLabel == null) return;
            
            string speedText;
            if (_currentSpeed >= 1024 * 1024)
                speedText = string.Format("速度：{0:F1} MB/s", _currentSpeed / (1024 * 1024));
            else if (_currentSpeed >= 1024)
                speedText = string.Format("速度：{0:F1} KB/s", _currentSpeed / 1024);
            else if (_currentSpeed > 0)
                speedText = string.Format("速度：{0:F0} B/s", _currentSpeed);
            else
                speedText = "速度：--";
            
            UpdateLabelSafe(_speedLabel, speedText);
        }
        
        /// <summary>
        /// 更新子进度条 (短) - 从百分比
        /// </summary>
        private void UpdateSubProgressFromPercent(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            UpdateProgressBarDirect(_subProgressBar, percent);
        }
        
        /// <summary>
        /// 更新总进度条 (长) - 多步骤操作的总进度
        /// </summary>
        public void UpdateTotalProgress(int currentStep, int totalSteps)
        {
            _currentStep = currentStep;
            _totalSteps = totalSteps;
            
            // 重置子进度条和速度计算变量（新步骤开始）
            _lastBytes = 0;
            _currentSpeed = 0;
            _lastSpeedUpdate = DateTime.Now;
            UpdateProgressBarDirect(_subProgressBar, 0);
        }
        
        private void UpdateLabelSafe(dynamic label, string text)
        {
            if (label == null) return;
            try
            {
                if (label.InvokeRequired)
                    label.BeginInvoke(new Action(() => label.Text = text));
                else
                    label.Text = text;
            }
            catch { }
        }
        
        /// <summary>
        /// 开始计时 (单步操作)
        /// </summary>
        public void StartOperationTimer(string operationName)
        {
            StartOperationTimer(operationName, 0, 0);
        }
        
        /// <summary>
        /// 开始计时 (多步操作)
        /// </summary>
        public void StartOperationTimer(string operationName, int totalSteps, int currentStep = 0)
        {
            _operationStopwatch = Stopwatch.StartNew();
            _lastBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _currentSpeed = 0;
            _totalSteps = totalSteps;
            _currentStep = currentStep;
            
            UpdateLabelSafe(_operationLabel, "当前操作：" + operationName);
            UpdateLabelSafe(_timeLabel, "时间：00:00");
            UpdateLabelSafe(_speedLabel, "速度：--");
            
            // 重置进度条为0
            UpdateProgressBarDirect(_progressBar, 0);
            UpdateProgressBarDirect(_subProgressBar, 0);
        }
        
        /// <summary>
        /// 重置子进度条 (单个操作开始前调用)
        /// </summary>
        public void ResetSubProgress()
        {
            _lastBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _currentSpeed = 0;
            UpdateProgressBarDirect(_subProgressBar, 0);
        }
        
        /// <summary>
        /// 停止计时
        /// </summary>
        public void StopOperationTimer()
        {
            if (_operationStopwatch != null)
            {
                _operationStopwatch.Stop();
                _operationStopwatch = null;
            }
            _totalSteps = 0;
            _currentStep = 0;
            _currentSpeed = 0;
            UpdateLabelSafe(_operationLabel, "当前操作：完成");
            UpdateProgressBarDirect(_progressBar, 100);
            UpdateProgressBarDirect(_subProgressBar, 100);
        }
        
        /// <summary>
        /// 重置所有进度显示
        /// </summary>
        public void ResetProgress()
        {
            _totalSteps = 0;
            _currentStep = 0;
            _lastBytes = 0;
            _currentSpeed = 0;
            UpdateProgressBarDirect(_progressBar, 0);
            UpdateProgressBarDirect(_subProgressBar, 0);
            UpdateLabelSafe(_timeLabel, "时间：00:00");
            UpdateLabelSafe(_speedLabel, "速度：--");
            UpdateLabelSafe(_operationLabel, "当前操作：待命");
        }

        private void UpdatePartitionListView(List<PartitionInfo> partitions)
        {
            if (_partitionListView == null) return;
            if (_partitionListView.InvokeRequired)
            {
                _partitionListView.BeginInvoke(new Action(() => UpdatePartitionListView(partitions)));
                return;
            }

            _partitionListView.BeginUpdate();
            _partitionListView.Items.Clear();

            foreach (var p in partitions)
            {
                // 计算地址
                long startAddress = p.StartSector * p.SectorSize;
                long endSector = p.StartSector + p.NumSectors - 1;
                long endAddress = (endSector + 1) * p.SectorSize;

                // 列顺序: 分区, LUN, 大小, 起始扇区, 结束扇区, 扇区数, 起始地址, 结束地址, 文件路径
                var item = new ListViewItem(p.Name);                           // 分区
                item.SubItems.Add(p.Lun.ToString());                           // LUN
                item.SubItems.Add(p.FormattedSize);                            // 大小
                item.SubItems.Add(p.StartSector.ToString());                   // 起始扇区
                item.SubItems.Add(endSector.ToString());                       // 结束扇区
                item.SubItems.Add(p.NumSectors.ToString());                    // 扇区数
                item.SubItems.Add(string.Format("0x{0:X}", startAddress));     // 起始地址
                item.SubItems.Add(string.Format("0x{0:X}", endAddress));       // 结束地址
                item.SubItems.Add("");                                         // 文件路径 (GPT 读取时无文件)
                item.Tag = p;

                // 只有勾选"保护分区"时，敏感分区才显示灰色
                if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(p.Name))
                    item.ForeColor = Color.Gray;

                _partitionListView.Items.Add(item);
            }

            _partitionListView.EndUpdate();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CancelOperation();
                Disconnect();
                _disposed = true;
            }
        }
    }
}
