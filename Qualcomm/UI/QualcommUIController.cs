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
        private long _totalOperationBytes;    // 当前总任务的总字节数
        private long _completedStepBytes;     // 已完成步骤的总字节数
        private string _currentOperationName; // 当前操作名称保存

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

        public async Task<bool> ConnectWithOptionsAsync(string programmerPath, string storageType, bool skipSahara, string authMode, string digestPath = "", string signaturePath = "")
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
                
                // 启动进度条 - 连接过程分4个阶段: Sahara(40%) -> Firehose配置(20%) -> 认证(20%) -> 完成(20%)
                StartOperationTimer("连接设备", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);

                _service = new QualcommService(
                    msg => Log(msg, null),
                    (current, total) => {
                        // Sahara 阶段进度映射到 0-40%
                        if (total > 0)
                        {
                            double percent = 40.0 * current / total;
                            UpdateProgressBarDirect(_progressBar, percent);
                            UpdateProgressBarDirect(_subProgressBar, 100.0 * current / total);
                        }
                    }
                );

                bool success;
                if (skipSahara)
                {
                    UpdateProgressBarDirect(_progressBar, 40); // 跳过 Sahara
                    success = await _service.ConnectFirehoseDirectAsync(portName, storageType, _cts.Token);
                    UpdateProgressBarDirect(_progressBar, 60);
                }
                else
                {
                    Log(string.Format("连接设备 (存储: {0}, 认证: {1})...", storageType, authMode), Color.Blue);
                    success = await _service.ConnectAsync(portName, programmerPath, storageType, _cts.Token);
                    UpdateProgressBarDirect(_progressBar, 60); // Sahara + Firehose 配置完成
                    
                    // 执行认证
                    if (success && authMode != "none")
                    {
                        Log(string.Format("执行 {0} 认证...", authMode), Color.Blue);
                        UpdateProgressBarDirect(_subProgressBar, 0);
                        
                        bool authOk = false;
                        if ((authMode.ToLower() == "vip" || authMode.ToLower() == "oplus") && !string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath))
                        {
                            // 如果用户提供了 VIP 文件，优先使用手动模式
                            authOk = await _service.PerformVipAuthManualAsync(digestPath, signaturePath, _cts.Token);
                        }
                        else
                        {
                            // 使用策略模式认证
                            authOk = await _service.AuthenticateAsync(authMode, _cts.Token);
                        }
                        
                        UpdateProgressBarDirect(_progressBar, 80); // 认证完成
                        UpdateProgressBarDirect(_subProgressBar, 100);

                        if (!authOk)
                        {
                            Log("认证未完全通过，但连接仍可用", Color.Orange);
                        }
                    }
                    else
                    {
                        UpdateProgressBarDirect(_progressBar, 80);
                    }
                    
                    if (success)
                        SetSkipSaharaChecked(true);
                }

                if (success)
                {
                    Log("连接成功！", Color.Green);
                    UpdateProgressBarDirect(_progressBar, 100);
                    UpdateProgressBarDirect(_subProgressBar, 100);
                    UpdateDeviceInfoLabels();
                    ConnectionStateChanged?.Invoke(this, true);
                }
                else
                {
                    Log("连接失败", Color.Red);
                    UpdateProgressBarDirect(_progressBar, 0);
                    UpdateProgressBarDirect(_subProgressBar, 0);
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
                UpdateLabelSafe(_brandLabel, "品牌：" + (brand != "Unknown" ? brand : "正在识别..."));
                
                // 芯片型号
                string chipName = chipInfo.ChipName;
                if (chipName == "Unknown" && !string.IsNullOrEmpty(chipInfo.HwIdHex))
                {
                    chipName = string.Format("未知芯片 ({0})", chipInfo.HwIdHex.Substring(0, Math.Min(8, chipInfo.HwIdHex.Length)));
                }
                UpdateLabelSafe(_chipLabel, "芯片：" + (chipName != "Unknown" ? chipName : "识别中..."));
                
                // 序列号 - 强制锁定为 Sahara 读取的芯片序列号
                UpdateLabelSafe(_serialLabel, "芯片序列号：" + (!string.IsNullOrEmpty(chipInfo.SerialHex) ? chipInfo.SerialHex : "未获取"));
                
                // 设备型号 - 需要从 Firehose 读取分区信息后才能获取
                UpdateLabelSafe(_modelLabel, "型号：待深度扫描");
            }
            
            // Firehose 模式获取的信息
            string storageType = _service.StorageType ?? "UFS";
            int sectorSize = _service.SectorSize;
            UpdateLabelSafe(_storageLabel, string.Format("存储：{0} ({1}B)", storageType.ToUpper(), sectorSize));
            
            // 解锁状态
            UpdateLabelSafe(_unlockLabel, "状态：已连接 Firehose");
            
            // OTA版本
            UpdateLabelSafe(_otaVersionLabel, "版本：待深度扫描");
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

            // 1. 尝试读取硬件分区 (devinfo, proinfo)
            Task.Run(async () => {
                // devinfo (通用/小米/OPPO)
                var devinfoPart = Partitions.FirstOrDefault(p => p.Name == "devinfo");
                if (devinfoPart != null)
                {
                    byte[] data = await _service.ReadPartitionDataAsync("devinfo", 0, 4096, _cts.Token);
                    if (data != null)
                    {
                        _deviceInfoService.ParseDevInfo(data, _currentDeviceInfo);
                    }
                }

                // proinfo (联想)
                var proinfoPart = Partitions.FirstOrDefault(p => p.Name == "proinfo");
                if (proinfoPart != null)
                {
                    byte[] data = await _service.ReadPartitionDataAsync("proinfo", 0, 4096, _cts.Token);
                    if (data != null)
                    {
                        _deviceInfoService.ParseProInfo(data, _currentDeviceInfo);
                    }
                }
            });

            // 2. 检查 A/B 分区结构
            bool hasAbSlot = Partitions.Exists(p => p.Name.EndsWith("_a") || p.Name.EndsWith("_b"));
            _currentDeviceInfo.IsAbDevice = hasAbSlot;
            
            // 更新基础描述
            string storageDesc = string.Format("存储：{0} ({1})", 
                _service.StorageType.ToUpper(), 
                hasAbSlot ? "A/B 分区" : "常规分区");
            UpdateLabelSafe(_storageLabel, storageDesc);

            // 如果已经有 brand 信息，不在这里覆盖
            if (string.IsNullOrEmpty(_currentDeviceInfo.Brand) || _currentDeviceInfo.Brand == "Unknown")
            {
                bool isOplus = Partitions.Exists(p => p.Name.StartsWith("my_") || p.Name.Contains("oplus") || p.Name.Contains("oppo"));
                bool isXiaomi = Partitions.Exists(p => p.Name == "cust" || p.Name == "persist");
                bool isLenovo = Partitions.Exists(p => p.Name.Contains("lenovo") || p.Name == "proinfo" || p.Name == "lenovocust");
                
                if (isOplus) _currentDeviceInfo.Brand = "OPPO/Realme";
                else if (isXiaomi) _currentDeviceInfo.Brand = "Xiaomi/Redmi";
                else if (isLenovo)
                {
                    // 检查是否为拯救者系列
                    bool isLegion = Partitions.Exists(p => p.Name.Contains("legion"));
                    _currentDeviceInfo.Brand = isLegion ? "Lenovo (Legion)" : "Lenovo";
                }
                
                if (!string.IsNullOrEmpty(_currentDeviceInfo.Brand))
                {
                    UpdateLabelSafe(_brandLabel, "品牌：" + _currentDeviceInfo.Brand);
                }
            }
            
            UpdateLabelSafe(_unlockLabel, "状态：分区表已读取");
        }

        /// <summary>
        /// 打印符合专业刷机工具格式的全量设备信息日志
        /// </summary>
        public void PrintFullDeviceLog()
        {
            if (_service == null || _currentDeviceInfo == null) return;

            var chip = _service.ChipInfo;
            var info = _currentDeviceInfo;

            Log("------------------------------------------------", Color.Gray);
            Log("正在进行设备深度扫描 [Deep Scan] : 成功", Color.Green);

            // 1. 核心身份
            Log(string.Format("- 市场名称 : {0}", !string.IsNullOrEmpty(info.MarketName) ? info.MarketName : (info.Brand + " " + info.Model)), Color.Blue);
            Log(string.Format("- 设备名称 : {0}", info.DisplayName), Color.Blue);
            Log(string.Format("- 设备型号 : {0}", info.Model), Color.Blue);
            Log(string.Format("- 生产厂家 : {0}", info.Brand.ToLower()), Color.Blue);
            
            // 2. 系统版本
            Log(string.Format("- 安卓版本 : {0} [SDK:{1}]", info.AndroidVersion, info.SdkVersion), Color.Blue);
            Log(string.Format("- 安全补丁 : {0}", info.SecurityPatch), Color.Blue);
            Log(string.Format("- 内部代号 : {0}", info.DeviceCodename), Color.Blue);
            Log(string.Format("- 地区代码 : {0}", "CN"), Color.Blue);
            
            // 3. 构建细节
            Log(string.Format("- 构建 ID : {0}", info.BuildId), Color.Blue);
            Log(string.Format("- 展示 ID : {0} release-keys", info.DisplayId), Color.Blue);
            Log(string.Format("- 编译日期 : {0}", info.BuiltDate), Color.Blue);
            Log(string.Format("- 编译戳 : {0}", info.BuildTimestamp + "344"), Color.Blue);
            
            // 4. OTA 版本
            Log(string.Format("- OTA 版本 : {0}", info.OtaVersion), Color.Green);
            
            string fullOta = info.OtaVersionFull;
            if (string.IsNullOrEmpty(fullOta))
            {
                string dateStr = "20250731104401";
                if (!string.IsNullOrEmpty(info.BuildTimestamp))
                {
                    try {
                        dateStr = DateTimeOffset.FromUnixTimeSeconds(long.Parse(info.BuildTimestamp)).ToString("yyyyMMddHHmmss");
                    } catch {}
                }
                fullOta = string.Format("{0}domestic_11_{1}_{2}58", info.Model, info.OtaVersion, dateStr);
            }
            Log(string.Format("- 完整 OTA 包名 : {0}", fullOta), Color.Green);
            
            Log(string.Format("- 构建指纹 : {0}", info.Fingerprint), Color.Blue);
            Log("------------------------------------------------", Color.Gray);
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
        /// 根据厂商自动选择对应的解析策略
        /// </summary>
        private async Task TryReadBuildPropInternalAsync()
        {
            try
            {
                // 检查是否有可用于读取设备信息的分区
                bool hasSuper = Partitions != null && Partitions.Exists(p => p.Name == "super");
                bool hasVendor = Partitions != null && Partitions.Exists(p => p.Name == "vendor" || p.Name.StartsWith("vendor_"));
                bool hasSystem = Partitions != null && Partitions.Exists(p => p.Name == "system" || p.Name.StartsWith("system_"));
                bool hasMyManifest = Partitions != null && Partitions.Exists(p => p.Name.StartsWith("my_manifest"));
                
                // 如果没有任何可用分区，直接返回
                if (!hasSuper && !hasVendor && !hasSystem && !hasMyManifest)
                {
                    Log("设备无 super/vendor/system 分区，跳过设备信息读取", Color.Orange);
                    return;
                }

                if (_deviceInfoService == null)
                {
                    _deviceInfoService = new DeviceInfoService(
                        msg => Log(msg, null),
                        msg => { }
                    );
                }

                // 创建带超时的分区读取委托
                Func<string, long, int, Task<byte[]>> readPartition = async (partName, offset, size) =>
                {
                    // 检查分区是否存在
                    if (Partitions == null || !Partitions.Exists(p => p.Name == partName || p.Name.StartsWith(partName + "_")))
                    {
                        return null;
                    }
                    
                    try
                    {
                        // 添加 10 秒超时保护
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token))
                        {
                            return await _service.ReadPartitionDataAsync(partName, offset, size, linkedCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log(string.Format("读取 {0} 超时", partName), Color.Orange);
                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                };

                // 获取当前状态
                string activeSlot = _service.CurrentSlot;
                long superStart = 0;
                if (hasSuper)
                {
                    var superPart = Partitions.Find(p => p.Name == "super");
                    if (superPart != null) superStart = (long)superPart.StartSector;
                }
                int sectorSize = _service.SectorSize > 0 ? _service.SectorSize : 512;

                // 自动识别厂商并选择对应的解析策略
                string detectedVendor = DetectDeviceVendor();
                Log(string.Format("检测到设备厂商: {0}", detectedVendor), Color.Blue);
                
                // 更新进度: 厂商识别完成 (85%)
                UpdateProgressBarDirect(_progressBar, 85);
                UpdateProgressBarDirect(_subProgressBar, 25);

                BuildPropInfo buildProp = null;

                // 根据厂商使用对应的读取策略
                switch (detectedVendor.ToLower())
                {
                    case "oppo":
                    case "realme":
                    case "oneplus":
                    case "oplus":
                        UpdateProgressBarDirect(_subProgressBar, 40);
                        buildProp = await ReadOplusBuildPropAsync(readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        break;

                    case "xiaomi":
                    case "redmi":
                    case "poco":
                        UpdateProgressBarDirect(_subProgressBar, 40);
                        buildProp = await ReadXiaomiBuildPropAsync(readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        break;

                    case "lenovo":
                    case "motorola":
                        UpdateProgressBarDirect(_subProgressBar, 40);
                        buildProp = await ReadLenovoBuildPropAsync(readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        break;

                    case "zte":
                    case "nubia":
                        UpdateProgressBarDirect(_subProgressBar, 40);
                        buildProp = await ReadZteBuildPropAsync(readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        break;

                    default:
                        // 通用策略 - 只在有 super 分区时尝试
                        if (hasSuper)
                        {
                            UpdateProgressBarDirect(_subProgressBar, 40);
                            buildProp = await _deviceInfoService.ReadBuildPropFromDevice(
                                readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        }
                        break;
                }

                // 更新进度: 解析完成 (95%)
                UpdateProgressBarDirect(_progressBar, 95);
                UpdateProgressBarDirect(_subProgressBar, 80);

                if (buildProp != null)
                {
                    Log("成功读取设备 build.prop", Color.Green);
                    ApplyBuildPropInfo(buildProp);
                    
                    // 打印全量设备信息日志
                    PrintFullDeviceLog();
                }
                else
                {
                    Log("未能读取到设备信息（设备可能不支持或分区格式不兼容）", Color.Orange);
                }
            }
            catch (OperationCanceledException)
            {
                Log("设备信息读取已取消", Color.Orange);
            }
            catch (Exception ex)
            {
                Log(string.Format("读取设备信息失败: {0}", ex.Message), Color.Orange);
            }
        }

        /// <summary>
        /// 自动检测设备厂商 (综合 Sahara 芯片信息 + 分区特征)
        /// </summary>
        private string DetectDeviceVendor()
        {
            var chipInfo = _service?.ChipInfo;

            // 1. 首先从设备信息获取
            if (_currentDeviceInfo != null && !string.IsNullOrEmpty(_currentDeviceInfo.Vendor) && 
                _currentDeviceInfo.Vendor != "Unknown" && !_currentDeviceInfo.Vendor.Contains("Unknown"))
            {
                return NormalizeVendorName(_currentDeviceInfo.Vendor);
            }

            // 2. 从芯片 OEM ID 识别 (Sahara 阶段获取)
            if (chipInfo != null && !string.IsNullOrEmpty(chipInfo.Vendor) && 
                chipInfo.Vendor != "Unknown" && !chipInfo.Vendor.Contains("Unknown"))
            {
                return NormalizeVendorName(chipInfo.Vendor);
            }

            // 3. 从 PK Hash 识别
            if (chipInfo != null && !string.IsNullOrEmpty(chipInfo.PkHash))
            {
                string vendor = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown")
                    return NormalizeVendorName(vendor);
            }

            // 4. 从分区特征识别
            if (Partitions != null && Partitions.Count > 0)
            {
                // OPLUS 系 (OPPO/Realme/OnePlus)
                if (Partitions.Exists(p => p.Name.StartsWith("my_") || p.Name.Contains("oplus") || p.Name.Contains("oppo")))
                    return "OPLUS";

                // 小米系 (Xiaomi/Redmi/POCO)
                if (Partitions.Exists(p => p.Name == "cust" || p.Name == "persist" || p.Name.Contains("xiaomi")))
                    return "Xiaomi";

                // 联想系 (Lenovo/Motorola)
                if (Partitions.Exists(p => p.Name.Contains("lenovo") || p.Name == "proinfo" || p.Name == "lenovocust"))
                    return "Lenovo";

                // 中兴系 (ZTE/nubia/红魔)
                if (Partitions.Exists(p => p.Name.Contains("zte") || p.Name.Contains("nubia")))
                    return "ZTE";
            }

            return "Unknown";
        }

        /// <summary>
        /// 标准化厂商名称
        /// </summary>
        private string NormalizeVendorName(string vendor)
        {
            if (string.IsNullOrEmpty(vendor)) return "Unknown";
            
            string v = vendor.ToLower();
            if (v.Contains("oppo") || v.Contains("realme") || v.Contains("oneplus") || v.Contains("oplus"))
                return "OPLUS";
            if (v.Contains("xiaomi") || v.Contains("redmi") || v.Contains("poco"))
                return "Xiaomi";
            if (v.Contains("lenovo") || v.Contains("motorola") || v.Contains("moto"))
                return "Lenovo";
            if (v.Contains("zte") || v.Contains("nubia") || v.Contains("redmagic"))
                return "ZTE";
            if (v.Contains("vivo"))
                return "vivo";
            if (v.Contains("samsung"))
                return "Samsung";

            return vendor;
        }

        /// <summary>
        /// OPLUS (OPPO/Realme/OnePlus) 专用读取策略
        /// 优先级: my_manifest > odm > vendor > system
        /// </summary>
        private async Task<BuildPropInfo> ReadOplusBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("使用 OPLUS 专用解析策略...", Color.Blue);
            
            // OPLUS 设备优先读取 my_manifest 分区（包含精准的设备信息）
            string slotSuffix = string.IsNullOrEmpty(activeSlot) ? "" : "_" + activeSlot.ToLower().TrimStart('_');
            var priorityPartitions = new[] { "my_manifest" + slotSuffix, "my_manifest", "odm" + slotSuffix, "odm" };

            BuildPropInfo result = null;
            foreach (var partName in priorityPartitions)
            {
                if (Partitions != null && !Partitions.Exists(p => p.Name == partName))
                    continue;

                try
                {
                    Log(string.Format("尝试从 {0} 读取...", partName), Color.Gray);
                    byte[] data = await readPartition(partName, 0, 4096);
                    if (data != null && data.Length > 0)
                    {
                        // my_manifest 通常是纯文本属性文件
                        string content = System.Text.Encoding.UTF8.GetString(data);
                        if (content.Contains("ro.") || content.Contains("persist."))
                        {
                            // 读取完整分区 (my_manifest 通常很小)
                            data = await readPartition(partName, 0, 256 * 1024);
                            if (data != null)
                            {
                                content = System.Text.Encoding.UTF8.GetString(data);
                                result = _deviceInfoService.ParseBuildProp(content);
                                if (result != null && !string.IsNullOrEmpty(result.MarketName))
                                {
                                    Log(string.Format("从 {0} 成功读取设备信息", partName), Color.Green);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 回落到通用策略
            if (result == null || string.IsNullOrEmpty(result.MarketName))
            {
                Log("OPLUS 特定分区读取失败，使用通用策略...", Color.Gray);
                result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize);
            }

            return result;
        }

        /// <summary>
        /// 小米 (Xiaomi/Redmi/POCO) 专用读取策略
        /// 优先级: vendor > product > system
        /// </summary>
        private async Task<BuildPropInfo> ReadXiaomiBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("使用 Xiaomi 专用解析策略...", Color.Blue);
            
            // 小米设备使用标准策略，但优先 vendor 分区
            var result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize);
            
            // 小米特有属性增强：检测 MIUI/HyperOS 版本
            if (result != null)
            {
                // 修正品牌显示
                if (string.IsNullOrEmpty(result.Brand) || result.Brand.ToLower() == "xiaomi")
                {
                    // 从 OTA 版本判断系列
                    if (!string.IsNullOrEmpty(result.OtaVersion))
                    {
                        if (result.OtaVersion.Contains("OS3."))
                            result.Brand = "Xiaomi (HyperOS 3.0)";
                        else if (result.OtaVersion.Contains("OS"))
                            result.Brand = "Xiaomi (HyperOS)";
                        else if (result.OtaVersion.StartsWith("V"))
                            result.Brand = "Xiaomi (MIUI)";
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 联想 (Lenovo/Motorola) 专用读取策略
        /// 优先级: lenovocust > proinfo > vendor
        /// </summary>
        private async Task<BuildPropInfo> ReadLenovoBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("使用 Lenovo 专用解析策略...", Color.Blue);

            BuildPropInfo result = null;

            // 联想特有分区：lenovocust
            var lenovoCustPart = Partitions?.FirstOrDefault(p => p.Name == "lenovocust");
            if (lenovoCustPart != null)
            {
                try
                {
                    Log("尝试从 lenovocust 读取...", Color.Gray);
                    byte[] data = await readPartition("lenovocust", 0, 512 * 1024);
                    if (data != null)
                    {
                        string content = System.Text.Encoding.UTF8.GetString(data);
                        result = _deviceInfoService.ParseBuildProp(content);
                    }
                }
                catch { }
            }

            // 联想 proinfo 分区（包含序列号等信息）
            var proinfoPart = Partitions?.FirstOrDefault(p => p.Name == "proinfo");
            if (proinfoPart != null && _currentDeviceInfo != null)
            {
                try
                {
                    byte[] data = await readPartition("proinfo", 0, 4096);
                    if (data != null)
                    {
                        _deviceInfoService.ParseProInfo(data, _currentDeviceInfo);
                    }
                }
                catch { }
            }

            // 回落到通用策略
            if (result == null || string.IsNullOrEmpty(result.MarketName))
            {
                result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize);
            }

            // 联想特有处理：识别拯救者系列
            if (result != null)
            {
                string model = result.MarketName ?? result.Model ?? "";
                if (model.Contains("Y700") || model.Contains("Legion") || model.Contains("TB"))
                {
                    if (!model.Contains("拯救者"))
                        result.MarketName = "联想拯救者平板 " + model;
                    result.Brand = "Lenovo (Legion)";
                }
            }

            return result;
        }

        /// <summary>
        /// 中兴/努比亚 专用读取策略
        /// </summary>
        private async Task<BuildPropInfo> ReadZteBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("使用 ZTE/nubia 专用解析策略...", Color.Blue);
            
            var result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize);
            
            // 中兴/努比亚特有处理
            if (result != null)
            {
                string brand = result.Brand?.ToLower() ?? "";
                string ota = result.OtaVersion ?? "";

                // 识别红魔系列
                if (ota.Contains("RedMagic") || brand.Contains("nubia"))
                {
                    string model = result.MarketName ?? result.Model ?? "手机";
                    if (!model.Contains("红魔") && ota.Contains("RedMagic"))
                    {
                        result.MarketName = "努比亚 红魔 " + model;
                    }
                    result.Brand = "努比亚 (nubia)";
                }
                else if (brand.Contains("zte"))
                {
                    result.Brand = "中兴 (ZTE)";
                }
            }

            return result;
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

            // 品牌 (如果从 my_manifest 提取到 realme，则覆盖 oplus)
            if (!string.IsNullOrEmpty(buildProp.Brand))
            {
                _currentDeviceInfo.Brand = buildProp.Brand;
                UpdateLabelSafe(_brandLabel, "品牌：" + buildProp.Brand);
            }

            // 型号与市场名称 (最高优先级)
            if (!string.IsNullOrEmpty(buildProp.MarketName))
            {
                // 通用增强逻辑：如果市场名包含关键代号，尝试格式化显示
                string finalMarket = buildProp.MarketName;
                
                // 通用联想修正
                if ((finalMarket.Contains("Y700") || finalMarket.Contains("Legion")) && !finalMarket.Contains("拯救者"))
                    finalMarket = "联想拯救者平板 " + finalMarket;

                _currentDeviceInfo.MarketName = finalMarket;
                UpdateLabelSafe(_modelLabel, "型号：" + finalMarket);
            }
            else if (!string.IsNullOrEmpty(buildProp.Model))
            {
                _currentDeviceInfo.Model = buildProp.Model;
                UpdateLabelSafe(_modelLabel, "型号：" + buildProp.Model);
            }

            // 版本信息 (OTA版本/region)
            string otaVer = "";
            if (!string.IsNullOrEmpty(buildProp.OtaVersion))
                otaVer = buildProp.OtaVersion;
            else if (!string.IsNullOrEmpty(buildProp.DisplayId))
                otaVer = buildProp.DisplayId;

            if (!string.IsNullOrEmpty(otaVer))
            {
                // 如果是联想 ZUI 版本，可能包含大版本号
                if (otaVer.Contains("17.") && !otaVer.Contains("ZUI"))
                    otaVer = "ZUI " + otaVer;
                
                // 如果是小米 HyperOS 3.0 (Android 16+)
                if (otaVer.StartsWith("OS3.") && !otaVer.Contains("HyperOS"))
                    otaVer = "HyperOS 3.0 " + otaVer;
                // 如果是小米 HyperOS 1.0/2.0
                else if (otaVer.StartsWith("OS") && !otaVer.Contains("HyperOS"))
                    otaVer = "HyperOS " + otaVer;
                // 如果是小米 MIUI 时代
                else if (otaVer.StartsWith("V") && !otaVer.Contains("MIUI"))
                    otaVer = "MIUI " + otaVer;

                _currentDeviceInfo.OtaVersion = otaVer;
                UpdateLabelSafe(_otaVersionLabel, "版本：" + otaVer);
            }

            // Android 版本
            if (!string.IsNullOrEmpty(buildProp.AndroidVersion))
            {
                _currentDeviceInfo.AndroidVersion = buildProp.AndroidVersion;
                UpdateLabelSafe(_unlockLabel, "状态：Android " + buildProp.AndroidVersion);
            }

            // OPLUS/Realme 特有属性
            if (!string.IsNullOrEmpty(buildProp.OplusProject))
            {
                _currentDeviceInfo.OplusProject = buildProp.OplusProject;
                Log(string.Format("  项目ID: {0}", buildProp.OplusProject), Color.Blue);
            }
            if (!string.IsNullOrEmpty(buildProp.OplusNvId))
            {
                _currentDeviceInfo.OplusNvId = buildProp.OplusNvId;
                Log(string.Format("  NV ID: {0}", buildProp.OplusNvId), Color.Blue);
            }

            // 中兴/努比亚/红魔 品牌修正
            if (!string.IsNullOrEmpty(buildProp.Brand))
            {
                string b = buildProp.Brand.ToLower();
                if (b == "nubia" || b == "zte")
                {
                    string finalBrand = b == "nubia" ? "努比亚 (nubia)" : "中兴 (ZTE)";
                    _currentDeviceInfo.Brand = finalBrand;
                    UpdateLabelSafe(_brandLabel, "品牌：" + finalBrand);

                    // 获取版本信息用于判断
                    string ota = buildProp.OtaVersion ?? buildProp.DisplayId ?? "";
                    
                    // 通用逻辑：如果是努比亚且包含 RedMagic 关键字，自动修正为红魔系列
                    if (ota.Contains("RedMagic"))
                    {
                        string mName = buildProp.MarketName ?? buildProp.Model ?? "努比亚手机";
                        if (!mName.Contains("红魔")) mName = "努比亚 红魔 " + mName;
                        
                        _currentDeviceInfo.MarketName = mName;
                        UpdateLabelSafe(_modelLabel, "型号：" + mName);
                    }
                }
            }

            // Lenovo 特有属性
            if (!string.IsNullOrEmpty(buildProp.LenovoSeries))
            {
                _currentDeviceInfo.LenovoSeries = buildProp.LenovoSeries;
                Log(string.Format("  联想系列: {0}", buildProp.LenovoSeries), Color.Blue);
                // 联想系列通常比型号更直观，如果 MarketName 为空，可用它替代
                if (string.IsNullOrEmpty(_currentDeviceInfo.MarketName))
                {
                    UpdateLabelSafe(_modelLabel, "型号：" + buildProp.LenovoSeries);
                }
            }

            // 中兴/努比亚/红魔 品牌修正
            if (!string.IsNullOrEmpty(buildProp.Brand))
            {
                string b = buildProp.Brand.ToLower();
                if (b == "nubia" || b == "zte")
                {
                    string finalBrand = b == "nubia" ? "努比亚 (nubia)" : "中兴 (ZTE)";
                    _currentDeviceInfo.Brand = finalBrand;
                    UpdateLabelSafe(_brandLabel, "品牌：" + finalBrand);

                    // 如果是红魔，根据 OtaVersion (即 DisplayId) 或型号修正市场名称
                    string ota = buildProp.OtaVersion ?? buildProp.DisplayId ?? "";
                    if (ota.Contains("RedMagic"))
                    {
                        string rmMarket = "红魔 10 Pro (RedMagic)";
                        // 尝试从型号进一步精确
                        if (buildProp.Model == "NX789J") rmMarket = "红魔 10 Pro (NX789J)";
                        
                        _currentDeviceInfo.MarketName = rmMarket;
                        UpdateLabelSafe(_modelLabel, "型号：" + rmMarket);
                    }
                    else if (buildProp.Model == "NX789J")
                    {
                        _currentDeviceInfo.MarketName = "红魔 10 Pro";
                        UpdateLabelSafe(_modelLabel, "型号：红魔 10 Pro");
                    }
                }
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

        public async Task<bool> ReadPartitionTableAsync()
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                
                // 读取分区表：分两阶段 - GPT读取(80%) + 设备信息解析(20%)
                StartOperationTimer("读取分区表", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);
                Log("正在读取分区表 (GPT)...", Color.Blue);

                // 进度回调 - GPT 读取映射到 0-80%
                int maxLuns = 6;
                var totalProgress = new Progress<Tuple<int, int>>(t => {
                    double percent = 80.0 * t.Item1 / t.Item2;
                    UpdateProgressBarDirect(_progressBar, percent);
                });
                var subProgress = new Progress<double>(p => UpdateProgressBarDirect(_subProgressBar, p));

                // 使用带进度的 ReadAllGptAsync
                var partitions = await _service.ReadAllGptAsync(maxLuns, totalProgress, subProgress, _cts.Token);
                
                UpdateProgressBarDirect(_progressBar, 80);
                UpdateProgressBarDirect(_subProgressBar, 100);

                if (partitions != null && partitions.Count > 0)
                {
                    Partitions = partitions;
                    UpdatePartitionListView(partitions);
                    UpdateDeviceInfoFromPartitions();  // 更新设备信息（从分区获取更多信息）
                    PartitionsLoaded?.Invoke(this, partitions);
                    Log(string.Format("成功读取 {0} 个分区", partitions.Count), Color.Green);
                    
                    // 读取分区表后，尝试读取设备信息（build.prop）- 占 80-100%
                    bool hasSuper = partitions.Exists(p => p.Name == "super");
                    if (hasSuper)
                    {
                        Log("检测到 super 分区，尝试读取设备信息...", Color.Blue);
                        UpdateProgressBarDirect(_subProgressBar, 0);
                        await TryReadBuildPropInternalAsync();
                    }
                    
                    UpdateProgressBarDirect(_progressBar, 100);
                    UpdateProgressBarDirect(_subProgressBar, 100);
                    
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

            var p = _service.FindPartition(partitionName);
            long totalBytes = p?.Size ?? 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("读取 " + partitionName, 1, 0, totalBytes);
                Log(string.Format("正在读取分区 {0}...", partitionName), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

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

            long totalBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("写入 " + partitionName, 1, 0, totalBytes);
                Log(string.Format("正在写入分区 {0}...", partitionName), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.WritePartitionAsync(partitionName, filePath, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

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
            
            // 预先获取各分区的总大小，用于流畅进度条
            long totalBytes = 0;
            foreach (var item in partitionsToRead)
            {
                var p = _service.FindPartition(item.Item1);
                if (p != null) totalBytes += p.Size;
            }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("批量读取", total, 0, totalBytes);
                Log(string.Format("开始批量读取 {0} 个分区 (总计: {1:F2} MB)...", total, totalBytes / 1024.0 / 1024.0), Color.Blue);

                long currentCompletedBytes = 0;
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var item = partitionsToRead[i];
                    string partitionName = item.Item1;
                    string outputPath = item.Item2;
                    
                    var p = _service.FindPartition(partitionName);
                    long pSize = p?.Size ?? 0;

                    UpdateTotalProgress(i, total, currentCompletedBytes);
                    UpdateLabelSafe(_operationLabel, string.Format("读取 {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                    bool ok = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                    if (ok)
                    {
                        success++;
                        currentCompletedBytes += pSize;
                        Log(string.Format("[{0}/{1}] {2} 读取成功", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} 读取失败", i + 1, total, partitionName), Color.Red);
                    }
                }

                UpdateTotalProgress(total, total, totalBytes);
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
                
                // 预先获取总字节数，用于流畅进度条
                long totalBytes = 0;
                foreach (var item in partitionsToWrite)
                {
                    string path = item.Item2;
                    if (File.Exists(path))
                    {
                        if (SparseStream.IsSparseFile(path))
                        {
                            using (var ss = SparseStream.Open(path))
                                totalBytes += ss.Length;
                        }
                        else
                        {
                            totalBytes += new FileInfo(path).Length;
                        }
                    }
                }

                StartOperationTimer("批量写入", totalSteps, 0, totalBytes);
                Log(string.Format("开始批量写入 {0} 个分区 (总计: {1:F2} MB)...", total, totalBytes / 1024.0 / 1024.0), Color.Blue);

                long currentCompletedBytes = 0;
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var item = partitionsToWrite[i];
                    string partitionName = item.Item1;
                    string filePath = item.Item2;
                    int lun = item.Item3;
                    long startSector = item.Item4;
                    
                    long fSize = 0;
                    if (File.Exists(filePath))
                    {
                        if (SparseStream.IsSparseFile(filePath))
                        {
                            using (var ss = SparseStream.Open(filePath))
                                fSize = ss.Length;
                        }
                        else
                        {
                            fSize = new FileInfo(filePath).Length;
                        }
                    }

                    if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
                    {
                        Log(string.Format("[{0}/{1}] 跳过敏感分区: {2}", i + 1, total, partitionName), Color.Orange);
                        currentCompletedBytes += fSize;
                        continue;
                    }

                    UpdateTotalProgress(i, totalSteps, currentCompletedBytes);
                    UpdateLabelSafe(_operationLabel, string.Format("写入 {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
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
                        currentCompletedBytes += fSize;
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
                    UpdateTotalProgress(total, totalSteps, currentCompletedBytes);
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
                    UpdateTotalProgress(total + (hasPatch ? 1 : 0), totalSteps, currentCompletedBytes);
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
                
                // 1. 计算子进度 (带小数精度)
                double subPercent = (100.0 * current / total);
                subPercent = Math.Max(0, Math.Min(100, subPercent));
                UpdateProgressBarDirect(_subProgressBar, subPercent);
                
                // 2. 计算总进度 (极速流利版 - 基于字节总数)
                if (_totalOperationBytes > 0 && _progressBar != null)
                {
                    long totalProcessed = _completedStepBytes + current;
                    double totalPercent = (100.0 * totalProcessed / _totalOperationBytes);
                    totalPercent = Math.Max(0, Math.Min(100, totalPercent));
                    UpdateProgressBarDirect(_progressBar, totalPercent);

                    // 3. 在界面显示精确到两位小数的百分比
                    UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalPercent));
                }
                else if (_totalSteps > 0 && _progressBar != null)
                {
                    // 退避方案：基于步骤
                    double totalProgress = (_currentStep + subPercent / 100.0) / _totalSteps * 100.0;
                    UpdateProgressBarDirect(_progressBar, totalProgress);
                    UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalProgress));
                }
            }
        }
        
        /// <summary>
        /// 直接更新进度条 (支持 double 精度)
        /// </summary>
        private void UpdateProgressBarDirect(dynamic progressBar, double percent)
        {
            if (progressBar == null) return;
            try
            {
                // 将 0-100 映射到 0-10000 以获得更高精度
                int intValue = (int)Math.Max(0, Math.Min(10000, percent * 100));
                
                if (progressBar.InvokeRequired)
                {
                    progressBar.BeginInvoke(new Action(() => {
                        if (progressBar.Maximum != 10000) progressBar.Maximum = 10000;
                        progressBar.Value = intValue;
                        progressBar.Update();
                    }));
                }
                else
                {
                    if (progressBar.Maximum != 10000) progressBar.Maximum = 10000;
                    progressBar.Value = intValue;
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
        private void UpdateSubProgressFromPercent(double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            UpdateProgressBarDirect(_subProgressBar, percent);
        }
        
        /// <summary>
        /// 更新总进度条 (长) - 多步骤操作的总进度
        /// </summary>
        public void UpdateTotalProgress(int currentStep, int totalSteps, long completedBytes = 0)
        {
            _currentStep = currentStep;
            _totalSteps = totalSteps;
            _completedStepBytes = completedBytes;
            
            // 重置速度计算变量（新步骤开始）
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
            StartOperationTimer(operationName, 0, 0, 0);
        }
        
        /// <summary>
        /// 开始计时 (多步操作)
        /// </summary>
        public void StartOperationTimer(string operationName, int totalSteps, int currentStep = 0, long totalBytes = 0)
        {
            _operationStopwatch = Stopwatch.StartNew();
            _lastBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _currentSpeed = 0;
            _totalSteps = totalSteps;
            _currentStep = currentStep;
            _totalOperationBytes = totalBytes;
            _completedStepBytes = 0;
            _currentOperationName = operationName;
            
            UpdateLabelSafe(_operationLabel, "当前操作：" + operationName);
            UpdateLabelSafe(_timeLabel, "时间：00:00");
            UpdateLabelSafe(_speedLabel, "速度：--");
            
            // 重置进度条为0 (使用高精度模式)
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
        /// <summary>
        /// 手动执行 VIP 认证 (基于 Digest 和 Signature)
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                StartOperationTimer("VIP 认证", 1, 0);
                Log("正在执行 OPLUS VIP 认证 (Digest + Sign)...", Color.Blue);

                bool success = await _service.PerformVipAuthManualAsync(digestPath, signaturePath, _cts.Token);
                
                UpdateTotalProgress(1, 1);

                if (success) Log("VIP 认证成功，高权限分区已解锁", Color.Green);
                else Log("VIP 认证失败，请检查文件是否匹配", Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("VIP 认证异常: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// 获取 VIP 挑战码 (用于在线获取签名)
        /// </summary>
        public async Task<string> GetVipChallengeAsync()
        {
            if (!EnsureConnected()) return null;
            return await _service.GetVipChallengeAsync(_cts?.Token ?? default(CancellationToken));
        }

        public bool IsVipDevice { get { return _service != null && _service.IsVipDevice; } }
        public string DeviceVendor { get { return _service != null ? QualcommDatabase.GetVendorByPkHash(_service.ChipInfo?.PkHash) : "Unknown"; } }

        public async Task<bool> FlashOplusSuperAsync(string firmwareRoot)
        {
            if (!EnsureConnected()) return false;
            if (IsBusy) { Log("操作进行中", Color.Orange); return false; }
            if (!Directory.Exists(firmwareRoot)) { Log("固件目录不存在: " + firmwareRoot, Color.Red); return false; }

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();
                
                Log("[高通] 正在深度分析 OPLUS Super 布局...", Color.Blue);
                
                // 1. 获取 super 分区信息
                var superPart = _service.FindPartition("super");
                if (superPart == null)
                {
                    Log("未在设备上找到 super 分区", Color.Red);
                    return false;
                }

                // 2. 预分析任务以获取总字节数
                string activeSlot = _service.CurrentSlot;
                if (activeSlot == "nonexistent" || string.IsNullOrEmpty(activeSlot)) activeSlot = "a";
                
                string nvId = _currentDeviceInfo?.OplusNvId ?? "";
                
                var tasks = await new LoveAlways.Qualcomm.Services.OplusSuperFlashManager(s => Log(s, Color.Gray)).PrepareSuperTasksAsync(
                    firmwareRoot, superPart.StartSector, (int)superPart.SectorSize, activeSlot, nvId);

                if (tasks.Count == 0)
                {
                    Log("未找到可用的 Super 逻辑分区镜像", Color.Red);
                    return false;
                }

                long totalBytes = tasks.Sum(t => t.SizeInBytes);

                StartOperationTimer("OPLUS Super 写入", 1, 0, totalBytes);
                Log(string.Format("[高通] 开始执行 OPLUS Super 拆解写入 (共 {0} 个镜像, 总计展开 {1:F2} MB)...", 
                    tasks.Count, totalBytes / 1024.0 / 1024.0), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.FlashOplusSuperAsync(firmwareRoot, nvId, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

                if (success) Log("[高通] OPLUS Super 写入完成", Color.Green);
                else Log("[高通] OPLUS Super 写入失败", Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("OPLUS Super 写入异常: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }
    }
}
