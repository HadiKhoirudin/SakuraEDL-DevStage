using OPFlashTool.Services;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.Qualcomm.UI;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Fastboot.UI;
using LoveAlways.Fastboot.Common;

namespace LoveAlways
{
    public partial class Form1 : AntdUI.Window
    {
        private string logFilePath;
        private string selectedLocalImagePath = "";
        private string input8OriginalText = "";
        private bool isEnglish = false;
 
        // 图片URL历史记录
        private List<string> urlHistory = new List<string>();

        // 图片预览缓存
        private List<Image> previewImages = new List<Image>();
        private const int MAX_PREVIEW_IMAGES = 5; // 最多保存5个预览

        // 原始控件位置和大小
        private Point originalinput6Location;
        private Point originalbutton4Location;
        private Point originalcheckbox13Location;
        private Point originalinput7Location;
        private Point originalinput9Location;
        private Point originallistView2Location;
        private Size originallistView2Size;
        private Point originaluiGroupBox4Location;
        private Size originaluiGroupBox4Size;

        // 高通 UI 控制器
        private QualcommUIController _qualcommController;
        private System.Windows.Forms.Timer _portRefreshTimer;
        private string _lastPortList = "";
        private int _lastEdlCount = 0;

        // Fastboot UI 控制器
        private FastbootUIController _fastbootController;

        public Form1()
        {
            InitializeComponent();
            
            // 初始化日志系统
            InitializeLogSystem();
            
            checkbox14.Checked = true;
            radio3.Checked = true;
            // 加载系统信息
            this.Load += async (sender, e) =>
            {
                try
                {
                    string sysInfo = await WindowsInfo.GetSystemInfoAsync();
                    uiLabel4.Text = $"计算机：{sysInfo}";
                    
                    // 写入系统信息到日志头部
                    WriteLogHeader(sysInfo);
                    AppendLog("加载中...OK", Color.Green);
                }
                catch (Exception ex)
                {
                    uiLabel4.Text = $"系统信息错误: {ex.Message}";
                    AppendLog($"初始化失败: {ex.Message}", Color.Red);
                }
            };

            // 绑定按钮事件
            button2.Click += Button2_Click;
            button3.Click += Button3_Click;
            slider1.ValueChanged += Slider1_ValueChanged;
            uiComboBox4.SelectedIndexChanged += UiComboBox4_SelectedIndexChanged;
            
            // 添加 select3 事件绑定
            select3.SelectedIndexChanged += Select3_SelectedIndexChanged;
            
            // 保存原始控件位置和大小
            SaveOriginalPositions();
            
            // 添加 checkbox17 和checkbox19 事件绑定
            checkbox17.CheckedChanged += Checkbox17_CheckedChanged;
            checkbox19.CheckedChanged += Checkbox19_CheckedChanged;

            // 初始化URL下拉框
            InitializeUrlComboBox();

            // 初始化图片预览控件
            InitializeImagePreview();

            // 默认调整控件布局
            ApplyCompactLayout();

            // 初始化高通模块
            InitializeQualcommModule();

            // 初始化 Fastboot 模块
            InitializeFastbootModule();
        }

        #region 高通模块

        private void InitializeQualcommModule()
        {
            try
            {
                // 创建高通 UI 控制器 (传入两个日志委托：UI日志 + 详细调试日志)
                _qualcommController = new QualcommUIController(
                    (msg, color) => AppendLog(msg, color),
                    msg => AppendLogDetail(msg));

                // 设置 listView2 支持多选和复选框
                listView2.MultiSelect = true;
                listView2.CheckBoxes = true;
                listView2.FullRowSelect = true;

                // 绑定控件 - tabPage2 上的高通控件
                // checkbox12 = 跳过引导, checkbox16 = 保护分区, input6 = 引导文件路径
                _qualcommController.BindControls(
                    portComboBox: uiComboBox1,           // 全局端口选择
                    partitionListView: listView2,        // 分区列表
                    progressBar: uiProcessBar1,          // 总进度条 (长) - 显示整体操作进度
                    statusLabel: null,
                    skipSaharaCheckbox: checkbox12,      // 跳过引导
                    protectPartitionsCheckbox: checkbox16, // 保护分区
                    programmerPathTextbox: null,         // input6 是 AntdUI.Input 类型，需要特殊处理
                    outputPathTextbox: null,
                    timeLabel: uiLabel6,                 // 时间标签
                    speedLabel: uiLabel7,                // 速度标签
                    operationLabel: uiLabel8,            // 当前操作标签
                    subProgressBar: uiProcessBar2,       // 子进度条 (短) - 显示单个操作实时进度
                    // 设备信息标签 (uiGroupBox3)
                    brandLabel: uiLabel9,                // 品牌
                    chipLabel: uiLabel11,                // 芯片
                    modelLabel: uiLabel3,                // 设备型号
                    serialLabel: uiLabel10,              // 序列号
                    storageLabel: uiLabel13,             // 存储类型
                    unlockLabel: uiLabel14,              // 设备型号2
                    otaVersionLabel: uiLabel12           // OTA版本
                );

                // ========== tabPage2 高通页面按钮事件 ==========
                // uiButton6 = 读取分区表, uiButton7 = 读取分区
                // uiButton8 = 写入分区, uiButton9 = 擦除分区
                uiButton6.Click += async (s, e) => await QualcommReadPartitionTableAsync();
                uiButton7.Click += async (s, e) => await QualcommReadPartitionAsync();
                uiButton8.Click += async (s, e) => await QualcommWritePartitionAsync();
                uiButton9.Click += async (s, e) => await QualcommErasePartitionAsync();

                // ========== 文件选择 ==========
                // input8 = 双击选择引导文件 (Programmer/Firehose)
                input8.DoubleClick += (s, e) => QualcommSelectProgrammer();
                
                // input9 = 双击选择 Digest 文件 (VIP认证用)
                input9.DoubleClick += (s, e) => QualcommSelectDigest();
                
                // input7 = 双击选择 Signature 文件 (VIP认证用)
                input7.DoubleClick += (s, e) => QualcommSelectSignature();
                
                // input6 = 双击选择 rawprogram.xml
                input6.DoubleClick += (s, e) => QualcommSelectRawprogramXml();
                
                // button4 = input6 右边的浏览按钮 (选择 Raw XML)
                button4.Click += (s, e) => QualcommSelectRawprogramXml();

                // 分区搜索 (select4 = 查找分区)
                select4.TextChanged += (s, e) => QualcommSearchPartition();
                select4.SelectedIndexChanged += (s, e) => { _isSelectingFromDropdown = true; };

                // 存储类型选择 (radio3 = UFS, radio4 = eMMC)
                radio3.CheckedChanged += (s, e) => { if (radio3.Checked) _storageType = "ufs"; };
                radio4.CheckedChanged += (s, e) => { if (radio4.Checked) _storageType = "emmc"; };

                // 注意: checkbox17/checkbox19 的事件已在构造函数中绑定 (Checkbox17_CheckedChanged / Checkbox19_CheckedChanged)
                // 那里会调用 UpdateAuthMode()，这里不再重复绑定

                // ========== checkbox13 全选/取消全选 ==========
                checkbox13.CheckedChanged += (s, e) => QualcommSelectAllPartitions(checkbox13.Checked);

                // ========== listView2 双击选择镜像文件 ==========
                listView2.DoubleClick += (s, e) => QualcommPartitionDoubleClick();

                // ========== checkbox11 生成XML 选项 ==========
                // 这只是一个开关，表示回读分区时是否同时生成 XML
                // 实际生成在回读完成后执行

                // ========== checkbox15 自动重启 (刷写完成后) ==========
                // 状态读取已在 QualcommErasePartitionAsync 等操作中检查

                // ========== EDL 操作菜单事件 ==========
                toolStripMenuItem4.Click += async (s, e) => await _qualcommController.RebootToEdlAsync();
                toolStripMenuItem5.Click += async (s, e) => await _qualcommController.RebootToSystemAsync();
                eDL切换槽位ToolStripMenuItem.Click += async (s, e) => await QualcommSwitchSlotAsync();
                激活LUNToolStripMenuItem.Click += async (s, e) => await QualcommSetBootLunAsync();

                // ========== 停止按钮 ==========
                uiButton1.Click += (s, e) => StopCurrentOperation();

                // ========== 刷新端口 ==========
                // 初始化时刷新端口列表（静默模式）
                _lastEdlCount = _qualcommController.RefreshPorts(silent: true);
                
                // 端口下拉框点击时刷新（静默模式）
                uiComboBox1.DropDown += (s, e) => _qualcommController.RefreshPorts(silent: true);
                
                // 启动端口自动检测定时器 (每2秒检测一次)
                _portRefreshTimer = new System.Windows.Forms.Timer();
                _portRefreshTimer.Interval = 2000;
                _portRefreshTimer.Tick += (s, e) => RefreshPortsIfIdle();
                _portRefreshTimer.Start();

                AppendLog("高通模块初始化完成", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"高通模块初始化失败: {ex.Message}", Color.Red);
            }
        }

        private string _storageType = "ufs";
        private string _authMode = "none";

        /// <summary>
        /// 空闲时刷新端口（检测设备连接/断开）
        /// </summary>
        private void RefreshPortsIfIdle()
        {
            try
            {
                // 如果有正在进行的操作，不刷新
                if (_qualcommController != null && _qualcommController.HasPendingOperation)
                    return;

                // 获取当前端口列表用于变化检测
                var ports = LoveAlways.Qualcomm.Common.PortDetector.DetectAllPorts();
                string currentPortList = string.Join(",", ports.ConvertAll(p => p.PortName));
                
                // 只有端口列表变化时才刷新
                if (currentPortList != _lastPortList)
                {
                    bool hadEdl = _lastEdlCount > 0;
                    _lastPortList = currentPortList;
                    
                    // 静默刷新，返回EDL端口数量
                    int edlCount = _qualcommController?.RefreshPorts(silent: true) ?? 0;
                    
                    // 新检测到EDL设备时提示
                    if (edlCount > 0 && !hadEdl)
                    {
                        var edlPorts = LoveAlways.Qualcomm.Common.PortDetector.DetectEdlPorts();
                        if (edlPorts.Count > 0)
                        {
                            AppendLog($"检测到 EDL 设备: {edlPorts[0].PortName} - {edlPorts[0].Description}", Color.LimeGreen);
                        }
                    }
                    
                    _lastEdlCount = edlCount;
                }
            }
            catch { }
        }

        private void UpdateAuthMode()
        {
            if (checkbox17.Checked && checkbox19.Checked)
            {
                checkbox19.Checked = false; // 互斥，优先 OnePlus
            }
            
            if (checkbox17.Checked)
                _authMode = "oneplus";
            else if (checkbox19.Checked)
                _authMode = "vip";
            else
                _authMode = "none";
        }

        private void QualcommSelectProgrammer()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择引导文件 (Programmer/Firehose)";
                ofd.Filter = "引导文件|*.mbn;*.elf|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input8.Text = ofd.FileName;
                    AppendLog($"已选择引导文件: {Path.GetFileName(ofd.FileName)}", Color.Green);
                }
            }
        }

        private async void QualcommSelectDigest()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择 Digest 文件 (VIP认证)";
                ofd.Filter = "Digest文件|*.elf;*.bin;*.mbn|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input9.Text = ofd.FileName;
                    AppendLog($"已选择 Digest: {Path.GetFileName(ofd.FileName)}", Color.Green);

                    // 如果已连接设备且已选择 Signature，自动执行 VIP 认证
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        string signaturePath = input7.Text;
                        if (!string.IsNullOrEmpty(signaturePath) && File.Exists(signaturePath))
                        {
                            AppendLog("已选择完整 VIP 认证文件，开始认证...", Color.Blue);
                            await QualcommPerformVipAuthAsync();
                        }
                    }
                }
            }
        }

        private async void QualcommSelectSignature()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择 Signature 文件 (VIP认证)";
                ofd.Filter = "Signature文件|*.bin;signature*|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input7.Text = ofd.FileName;
                    AppendLog($"已选择 Signature: {Path.GetFileName(ofd.FileName)}", Color.Green);
                    
                    // 如果已连接设备且已选择 Digest，自动执行 VIP 认证
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        string digestPath = input9.Text;
                        if (!string.IsNullOrEmpty(digestPath) && File.Exists(digestPath))
                        {
                            AppendLog("已选择完整 VIP 认证文件，开始认证...", Color.Blue);
                            await QualcommPerformVipAuthAsync();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 手动执行 VIP 认证 (OPPO/Realme)
        /// </summary>
        private async Task QualcommPerformVipAuthAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备", Color.Orange);
                return;
            }

            string digestPath = input9.Text;
            string signaturePath = input7.Text;

            if (string.IsNullOrEmpty(digestPath) || !File.Exists(digestPath))
            {
                AppendLog("请先选择 Digest 文件 (双击输入框选择)", Color.Orange);
                return;
            }

            if (string.IsNullOrEmpty(signaturePath) || !File.Exists(signaturePath))
            {
                AppendLog("请先选择 Signature 文件 (双击输入框选择)", Color.Orange);
                return;
            }

            bool success = await _qualcommController.PerformVipAuthAsync(digestPath, signaturePath);
            if (success)
            {
                AppendLog("VIP 认证成功，现在可以操作敏感分区", Color.Green);
            }
        }

        private void QualcommSelectRawprogramXml()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择 Rawprogram XML 文件 (可多选)";
                ofd.Filter = "XML文件|rawprogram*.xml;*.xml|所有文件|*.*";
                ofd.Multiselect = true;  // 支持多选
                
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (ofd.FileNames.Length == 1)
                {
                    input6.Text = ofd.FileName;
                    AppendLog($"已选择 XML: {Path.GetFileName(ofd.FileName)}", Color.Green);
                    }
                    else
                    {
                        input6.Text = $"已选择 {ofd.FileNames.Length} 个文件";
                        foreach (var file in ofd.FileNames)
                        {
                            AppendLog($"已选择 XML: {Path.GetFileName(file)}", Color.Green);
                        }
                    }
                    
                    // 解析所有选中的 XML 文件
                    LoadMultipleRawprogramXml(ofd.FileNames);
                }
            }
        }

        private void LoadMultipleRawprogramXml(string[] xmlPaths)
        {
            var allTasks = new List<Qualcomm.Common.FlashTask>();
            string programmerPath = "";
            string[] filesToLoad = xmlPaths;

            // 如果用户只选择了一个文件，且文件名包含 rawprogram，则自动搜索同目录下的其他 LUN
            if (xmlPaths.Length == 1 && Path.GetFileName(xmlPaths[0]).Contains("rawprogram"))
            {
                string dir = Path.GetDirectoryName(xmlPaths[0]);
                var siblingFiles = Directory.GetFiles(dir, "rawprogram*.xml")
                    .OrderBy(f => f).ToArray();
                
                if (siblingFiles.Length > 1)
                {
                    filesToLoad = siblingFiles;
                    AppendLog($"检测到多个 LUN，已自动加载同目录下的 {siblingFiles.Length} 个 XML 文件", Color.Blue);
                }
            }
            
            foreach (var xmlPath in filesToLoad)
            {
                try
                {
                    string dir = Path.GetDirectoryName(xmlPath);
                    var parser = new Qualcomm.Common.RawprogramParser(dir, msg => { /* 避免过多冗余日志 */ });
                    
                    // 解析当前 XML 文件
                    var tasks = parser.ParseRawprogramXml(xmlPath);
                    
                    // 仅添加尚未存在的任务 (按 LUN + StartSector + Label 判定)
                    foreach (var task in tasks)
                    {
                        if (!allTasks.Any(t => t.Lun == task.Lun && t.StartSector == task.StartSector && t.Label == task.Label))
                        {
                            allTasks.Add(task);
                        }
                    }

                    AppendLog($"解析 {Path.GetFileName(xmlPath)}: {tasks.Count} 个任务 (当前累计: {allTasks.Count})", Color.Blue);
                    
                    // 自动识别对应的 patch 文件
                    string patchPath = FindMatchingPatchFile(xmlPath);
                    if (!string.IsNullOrEmpty(patchPath))
                    {
                        // 记录到全局变量或后续处理中
                    }
                    
                    // 自动识别 programmer 文件（只识别一次）
                    if (string.IsNullOrEmpty(programmerPath))
                    {
                        programmerPath = parser.FindProgrammer();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"解析 {Path.GetFileName(xmlPath)} 失败: {ex.Message}", Color.Red);
                }
            }
            
            if (allTasks.Count > 0)
            {
                AppendLog($"共加载 {allTasks.Count} 个刷机任务", Color.Green);
                
                if (!string.IsNullOrEmpty(programmerPath))
                {
                    input8.Text = programmerPath;
                    AppendLog($"自动识别引导文件: {Path.GetFileName(programmerPath)}", Color.Green);
                }
                
                // 将所有任务填充到分区列表
                FillPartitionListFromTasks(allTasks);
            }
            else
            {
                AppendLog("未在 XML 中找到有效的刷机任务", Color.Orange);
            }
        }

        private string FindMatchingPatchFile(string rawprogramPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(rawprogramPath);
                string fileName = Path.GetFileName(rawprogramPath);
                
                // rawprogram0.xml -> patch0.xml, rawprogram_unsparse.xml -> patch_unsparse.xml
                string patchName = fileName.Replace("rawprogram", "patch");
                string patchPath = Path.Combine(dir, patchName);
                
                if (File.Exists(patchPath))
                    return patchPath;
                
                // 尝试其他 patch 文件
                var patchFiles = Directory.GetFiles(dir, "patch*.xml");
                if (patchFiles.Length > 0)
                    return patchFiles[0];
                
                return "";
            }
            catch
            {
                return "";
            }
        }

        private void FillPartitionListFromTasks(List<Qualcomm.Common.FlashTask> tasks)
        {
            listView2.BeginUpdate();
            listView2.Items.Clear();

            int checkedCount = 0;
            
            foreach (var task in tasks)
            {
                // 转换为 PartitionInfo 用于统一处理
                var partition = new PartitionInfo
                {
                    Name = task.Label,
                    Lun = task.Lun,
                    StartSector = task.StartSector,
                    NumSectors = task.NumSectors,
                    SectorSize = task.SectorSize
                };

                // 计算地址
                long startAddress = task.StartSector * task.SectorSize;
                long endSector = task.StartSector + task.NumSectors - 1;
                long endAddress = (endSector + 1) * task.SectorSize;

                // 列顺序: 分区, LUN, 大小, 起始扇区, 结束扇区, 扇区数, 起始地址, 结束地址, 文件路径
                var item = new ListViewItem(task.Label);                           // 分区
                item.SubItems.Add(task.Lun.ToString());                             // LUN
                item.SubItems.Add(task.FormattedSize);                              // 大小
                item.SubItems.Add(task.StartSector.ToString());                     // 起始扇区
                item.SubItems.Add(endSector.ToString());                            // 结束扇区
                item.SubItems.Add(task.NumSectors.ToString());                      // 扇区数
                item.SubItems.Add($"0x{startAddress:X}");                           // 起始地址
                item.SubItems.Add($"0x{endAddress:X}");                             // 结束地址
                item.SubItems.Add(string.IsNullOrEmpty(task.FilePath) ? task.Filename : task.FilePath);  // 文件路径
                item.Tag = partition;

                // 检查镜像文件是否存在
                bool fileExists = !string.IsNullOrEmpty(task.FilePath) && File.Exists(task.FilePath);
                
                // 文件存在则自动勾选（排除敏感分区）
                if (fileExists && !Qualcomm.Common.RawprogramParser.IsSensitivePartition(task.Label))
                {
                    item.Checked = true;
                    checkedCount++;
                }

                // 敏感分区标记
                if (Qualcomm.Common.RawprogramParser.IsSensitivePartition(task.Label))
                    item.ForeColor = Color.Gray;

                // 文件不存在标记
                if (!fileExists)
                    item.ForeColor = Color.Red;

                listView2.Items.Add(item);
            }

            listView2.EndUpdate();
            AppendLog($"分区列表已更新: {tasks.Count} 个分区, 自动选中 {checkedCount} 个有效分区", Color.Green);
        }

        private async Task QualcommReadPartitionTableAsync()
        {
            if (_qualcommController == null) return;

            if (!_qualcommController.IsConnected)
            {
                // 先连接
                bool connected = await QualcommConnectAsync();
                if (!connected) return;
            }

            await _qualcommController.ReadPartitionTableAsync();
        }

        private async Task<bool> QualcommConnectAsync()
        {
            if (_qualcommController == null) return false;

            // input8 = 引导文件路径
            string programmerPath = input8.Text?.Trim() ?? "";
            bool skipSahara = checkbox12.Checked;

            if (!skipSahara && string.IsNullOrEmpty(programmerPath))
            {
                AppendLog("请选择引导文件或勾选「跳过引导」", Color.Orange);
                return false;
            }

            // 使用自定义连接逻辑
            return await _qualcommController.ConnectWithOptionsAsync(
                programmerPath, 
                _storageType, 
                skipSahara,
                _authMode,
                input9.Text?.Trim() ?? "",
                input7.Text?.Trim() ?? ""
            );
        }

        private void GeneratePartitionXml()
        {
            try
            {
                if (_qualcommController.Partitions == null || _qualcommController.Partitions.Count == 0)
                {
                    AppendLog("请先读取分区表", Color.Orange);
                    return;
                }

                // 选择保存目录，因为我们要生成多个文件 (rawprogram0.xml, patch0.xml 等)
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "选择 XML 保存目录 (将根据 LUN 生成多个 rawprogram 和 patch 文件)";
                    
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        string saveDir = fbd.SelectedPath;
                        var parser = new LoveAlways.Qualcomm.Common.GptParser();
                        int sectorSize = _qualcommController.Partitions.Count > 0 
                            ? _qualcommController.Partitions[0].SectorSize 
                            : 4096;

                        // 1. 生成 rawprogramX.xml
                        var rawprogramDict = parser.GenerateRawprogramXmls(_qualcommController.Partitions, sectorSize);
                        foreach (var kv in rawprogramDict)
                        {
                            string fileName = Path.Combine(saveDir, $"rawprogram{kv.Key}.xml");
                            File.WriteAllText(fileName, kv.Value);
                            AppendLog($"已生成: {Path.GetFileName(fileName)}", Color.Blue);
                        }

                        // 2. 生成 patchX.xml
                        var patchDict = parser.GeneratePatchXmls(_qualcommController.Partitions, sectorSize);
                        foreach (var kv in patchDict)
                        {
                            string fileName = Path.Combine(saveDir, $"patch{kv.Key}.xml");
                            File.WriteAllText(fileName, kv.Value);
                            AppendLog($"已生成: {Path.GetFileName(fileName)}", Color.Blue);
                        }

                        // 3. 生成单个合并的 partition.xml (可选)
                        string partitionXml = parser.GeneratePartitionXml(_qualcommController.Partitions, sectorSize);
                        string pFileName = Path.Combine(saveDir, "partition.xml");
                        File.WriteAllText(pFileName, partitionXml);
                        
                        AppendLog($"XML 集合已成功保存到: {saveDir}", Color.Green);
                        
                        // 显示槽位信息
                        string currentSlot = _qualcommController.GetCurrentSlot();
                        if (currentSlot != "nonexistent")
                        {
                            AppendLog($"当前槽位: {currentSlot}", Color.Blue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"生成 XML 失败: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// 为指定分区生成 XML 文件到指定目录 (回读时调用)
        /// </summary>
        private void GenerateXmlForPartitions(List<PartitionInfo> partitions, string saveDir)
        {
            try
            {
                if (partitions == null || partitions.Count == 0)
                {
                    return;
                }

                var parser = new LoveAlways.Qualcomm.Common.GptParser();
                int sectorSize = partitions[0].SectorSize > 0 ? partitions[0].SectorSize : 4096;

                // 按 LUN 分组生成 rawprogram XML
                var byLun = partitions.GroupBy(p => p.Lun).ToDictionary(g => g.Key, g => g.ToList());
                
                foreach (var kv in byLun)
                {
                    int lun = kv.Key;
                    var lunPartitions = kv.Value;
                    
                    // 生成该 LUN 的 rawprogram XML
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" ?>");
                    sb.AppendLine("<data>");
                    sb.AppendLine("  <!-- 由 LoveAlways EDL Tool 生成 - 回读分区 -->");
                    
                    foreach (var p in lunPartitions)
                    {
                        // 生成 program 条目 (用于刷写回读的分区)
                        sb.AppendFormat("  <program SECTOR_SIZE_IN_BYTES=\"{0}\" file_sector_offset=\"0\" " +
                            "filename=\"{1}.img\" label=\"{1}\" num_partition_sectors=\"{2}\" " +
                            "physical_partition_number=\"{3}\" start_sector=\"{4}\" />\n",
                            sectorSize, p.Name, p.NumSectors, lun, p.StartSector);
                    }
                    
                    sb.AppendLine("</data>");
                    
                    string fileName = Path.Combine(saveDir, $"rawprogram{lun}.xml");
                    File.WriteAllText(fileName, sb.ToString());
                    AppendLog($"已生成回读分区 XML: {Path.GetFileName(fileName)}", Color.Blue);
                }
                
                AppendLog($"回读分区 XML 已保存到: {saveDir}", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"生成回读 XML 失败: {ex.Message}", Color.Orange);
            }
        }

        private async Task QualcommReadPartitionAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备并读取分区表", Color.Orange);
                return;
            }

            // 获取勾选的分区或选中的分区
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("请选择或勾选要读取的分区", Color.Orange);
                return;
            }

            // 选择保存目录
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = checkedItems.Count == 1 ? "选择保存位置" : $"选择保存目录 (将读取 {checkedItems.Count} 个分区)";
                
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string saveDir = fbd.SelectedPath;
                    
                    if (checkedItems.Count == 1)
                    {
                        // 单个分区
                        var partition = checkedItems[0];
                        string savePath = Path.Combine(saveDir, partition.Name + ".img");
                        await _qualcommController.ReadPartitionAsync(partition.Name, savePath);
                    }
                    else
                    {
                        // 批量读取
                        var partitionsToRead = new List<Tuple<string, string>>();
                        foreach (var p in checkedItems)
                        {
                            string savePath = Path.Combine(saveDir, p.Name + ".img");
                            partitionsToRead.Add(Tuple.Create(p.Name, savePath));
                        }
                        await _qualcommController.ReadPartitionsBatchAsync(partitionsToRead);
                    }
                    
                    // 回读完成后，如果勾选了生成XML，则为回读的分区生成 XML
                    if (checkbox11.Checked && checkedItems.Count > 0)
                    {
                        GenerateXmlForPartitions(checkedItems, saveDir);
                    }
                }
            }
        }

        private List<PartitionInfo> GetCheckedOrSelectedPartitions()
        {
            var result = new List<PartitionInfo>();
            
            // 优先使用勾选的项
            foreach (ListViewItem item in listView2.CheckedItems)
            {
                var p = item.Tag as PartitionInfo;
                if (p != null) result.Add(p);
            }
            
            // 如果没有勾选，使用选中的项
            if (result.Count == 0)
            {
                foreach (ListViewItem item in listView2.SelectedItems)
                {
                    var p = item.Tag as PartitionInfo;
                    if (p != null) result.Add(p);
                }
            }
            
            return result;
        }

        private async Task QualcommWritePartitionAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备并读取分区表", Color.Orange);
                return;
            }

            // 获取勾选的分区或选中的分区
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("请选择或勾选要写入的分区", Color.Orange);
                return;
            }

            if (checkedItems.Count == 1)
            {
                // 单个分区写入
                var partition = checkedItems[0];
                string filePath = "";

                // 先检查是否已有文件路径（双击选择的或从XML解析的）
                foreach (ListViewItem item in listView2.Items)
                {
                    var p = item.Tag as PartitionInfo;
                    if (p != null && p.Name == partition.Name)
                    {
                        filePath = item.SubItems.Count > 8 ? item.SubItems[8].Text : "";
                        break;
                    }
                }

                // 如果没有文件路径或文件不存在，弹出选择对话框
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    // 如果勾选了 MetaSuper 且是 super 分区，引导用户选择固件目录
                    if (checkbox18.Checked && partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var fbd = new FolderBrowserDialog())
                        {
                            fbd.Description = "已开启 MetaSuper！请选择 OPLUS 固件根目录 (包含 IMAGES 和 META)";
                            if (fbd.ShowDialog() == DialogResult.OK)
                            {
                                await _qualcommController.FlashOplusSuperAsync(fbd.SelectedPath);
                                return;
                            }
                        }
                    }

                    using (var ofd = new OpenFileDialog())
                    {
                        ofd.Title = $"选择要写入 {partition.Name} 的镜像文件";
                        ofd.Filter = "镜像文件|*.img;*.bin|所有文件|*.*";

                        if (ofd.ShowDialog() != DialogResult.OK)
                            return;

                        filePath = ofd.FileName;
                    }
                }
                else
                {
                    // 即使路径存在，如果开启了 MetaSuper 且是 super 分区，也执行拆解逻辑
                    if (checkbox18.Checked && partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase))
                    {
                        // 尝试从文件路径推断固件根目录 (通常镜像在 IMAGES 文件夹下)
                        string firmwareRoot = Path.GetDirectoryName(Path.GetDirectoryName(filePath));
                        if (Directory.Exists(Path.Combine(firmwareRoot, "META")))
                        {
                            await _qualcommController.FlashOplusSuperAsync(firmwareRoot);
                            return;
                        }
                        else
                        {
                            // 如果推断失败，手动选择
                            using (var fbd = new FolderBrowserDialog())
                            {
                                fbd.Description = "已开启 MetaSuper！请选择 OPLUS 固件根目录 (包含 IMAGES 和 META)";
                                if (fbd.ShowDialog() == DialogResult.OK)
                                {
                                    await _qualcommController.FlashOplusSuperAsync(fbd.SelectedPath);
                                    return;
                                }
                            }
                        }
                    }
                }

                // 执行写入
                AppendLog($"开始写入 {Path.GetFileName(filePath)} -> {partition.Name}", Color.Blue);
                bool success = await _qualcommController.WritePartitionAsync(partition.Name, filePath);
                
                if (success && checkbox15.Checked)
                {
                    AppendLog("写入完成，自动重启设备...", Color.Blue);
                    await _qualcommController.RebootToSystemAsync();
                }
            }
            else
            {
                // 批量写入 - 从 XML 解析的任务中获取文件路径
                // 使用包含 LUN 和 StartSector 的元组，便于处理 PrimaryGPT/BackupGPT
                var partitionsToWrite = new List<Tuple<string, string, int, long>>();
                var missingFiles = new List<string>();

                foreach (ListViewItem item in listView2.CheckedItems)
                {
                    var partition = item.Tag as PartitionInfo;
                    if (partition == null) continue;

                    // 获取文件路径（从 SubItems 中）
                    string filePath = item.SubItems.Count > 8 ? item.SubItems[8].Text : "";
                    
                    // 尝试从当前目录或 XML 目录查找文件
                    if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
                    {
                        // 尝试从 input6 (XML路径) 的目录查找
                        try
                        {
                            string xmlPath = input6.Text?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(xmlPath))
                            {
                                string xmlDir = Path.GetDirectoryName(xmlPath) ?? "";
                                if (!string.IsNullOrEmpty(xmlDir))
                                {
                                    string altPath = Path.Combine(xmlDir, Path.GetFileName(filePath));
                                    if (File.Exists(altPath))
                                        filePath = altPath;
                                }
                            }
                        }
                        catch (ArgumentException)
                        {
                            // 路径包含无效字符，忽略
                        }
                    }

                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        // 传递 (分区名, 文件路径, LUN, StartSector)
                        partitionsToWrite.Add(Tuple.Create(partition.Name, filePath, partition.Lun, partition.StartSector));
                    }
                    else
                    {
                        missingFiles.Add(partition.Name);
                    }
                }

                if (missingFiles.Count > 0)
                {
                    AppendLog($"以下分区缺少镜像文件: {string.Join(", ", missingFiles)}", Color.Orange);
                }

                if (partitionsToWrite.Count > 0)
                {
                    // 收集 Patch 文件
                    List<string> patchFiles = new List<string>();
                    string xmlDir = "";
                    try
                    {
                        // 安全获取目录路径，处理空字符串或无效路径
                        string xmlPath = input6.Text?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(xmlPath) && (File.Exists(xmlPath) || Directory.Exists(Path.GetDirectoryName(xmlPath))))
                        {
                            xmlDir = Path.GetDirectoryName(xmlPath) ?? "";
                        }
                    }
                    catch (ArgumentException)
                    {
                        // 路径包含无效字符，忽略
                        xmlDir = "";
                    }
                    if (!string.IsNullOrEmpty(xmlDir) && Directory.Exists(xmlDir))
                    {
                        // 搜索所有 patch XML 文件
                        var foundPatches = Directory.GetFiles(xmlDir, "patch*.xml", SearchOption.TopDirectoryOnly)
                            .Where(f => !Path.GetFileName(f).Contains("BLANK") && !Path.GetFileName(f).Contains("WIPE"))
                            .OrderBy(f => f)
                            .ToList();
                        patchFiles.AddRange(foundPatches);

                        if (patchFiles.Count > 0)
                        {
                            AppendLog($"检测到 {patchFiles.Count} 个 Patch 文件:", Color.Blue);
                            foreach (var pf in patchFiles)
                            {
                                AppendLog($"  - {Path.GetFileName(pf)}", Color.Gray);
                            }
                        }
                        else
                        {
                            AppendLog("未检测到 Patch 文件，跳过补丁步骤", Color.Gray);
                        }
                    }

                    // UFS 设备需要激活启动 LUN，eMMC 只有 LUN0 不需要
                    bool activateBootLun = _storageType == "ufs";
                    if (activateBootLun)
                    {
                        AppendLog("UFS 设备: 写入完成后将回读 GPT 并激活对应启动 LUN", Color.Blue);
                    }
                    else
                    {
                        AppendLog("eMMC 设备: 仅 LUN0，无需激活启动分区", Color.Gray);
                    }

                    int success = await _qualcommController.WritePartitionsBatchAsync(partitionsToWrite, patchFiles, activateBootLun);
                    
                    if (success > 0 && checkbox15.Checked)
                    {
                        AppendLog("批量写入完成，自动重启设备...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
                else
                {
                    AppendLog("没有找到有效的镜像文件，请确保 XML 解析正确或手动选择文件", Color.Orange);
                }
            }
        }

        private async Task QualcommErasePartitionAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备并读取分区表", Color.Orange);
                return;
            }

            // 获取勾选的分区或选中的分区
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("请选择或勾选要擦除的分区", Color.Orange);
                return;
            }

            // 擦除确认
            string message = checkedItems.Count == 1
                ? $"确定要擦除分区 {checkedItems[0].Name} 吗？\n\n此操作不可逆！"
                : $"确定要擦除 {checkedItems.Count} 个分区吗？\n\n分区: {string.Join(", ", checkedItems.ConvertAll(p => p.Name))}\n\n此操作不可逆！";

            var result = MessageBox.Show(
                message,
                "确认擦除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                if (checkedItems.Count == 1)
                {
                    // 单个擦除
                    bool success = await _qualcommController.ErasePartitionAsync(checkedItems[0].Name);
                    
                    if (success && checkbox15.Checked)
                    {
                        AppendLog("擦除完成，自动重启设备...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
                else
                {
                    // 批量擦除
                    var partitionNames = checkedItems.ConvertAll(p => p.Name);
                    int success = await _qualcommController.ErasePartitionsBatchAsync(partitionNames);
                    
                    if (success > 0 && checkbox15.Checked)
                    {
                        AppendLog("批量擦除完成，自动重启设备...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
            }
        }

        private async Task QualcommSwitchSlotAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备", Color.Orange);
                return;
            }

            // 询问槽位
            var result = MessageBox.Show("切换到槽位 A？\n\n选择 是 切换到 A\n选择 否 切换到 B",
                "切换槽位", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
                await _qualcommController.SwitchSlotAsync("a");
            else if (result == DialogResult.No)
                await _qualcommController.SwitchSlotAsync("b");
        }

        private async Task QualcommSetBootLunAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("请先连接设备", Color.Orange);
                return;
            }

            // UFS: 0, 1, 2, 4(Boot A), 5(Boot B)
            // eMMC: 0
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "输入 LUN 编号:\n\nUFS 支持: 0, 1, 2, 4(Boot A), 5(Boot B)\neMMC 仅支持: 0",
                "激活 LUN", "0");

            int lun;
            if (int.TryParse(input, out lun))
            {
                await _qualcommController.SetBootLunAsync(lun);
            }
        }

        private void StopCurrentOperation()
        {
            if (_qualcommController == null)
            {
                AppendLog("没有进行中的操作", Color.Gray);
                return;
            }

            if (_qualcommController.HasPendingOperation)
            {
                _qualcommController.CancelOperation();
                AppendLog("操作已取消", Color.Orange);
                
                // 重置进度条
                uiProcessBar1.Value = 0;
                uiProcessBar2.Value = 0;
            }
            else
            {
                AppendLog("当前没有进行中的操作", Color.Gray);
            }
        }

        private void QualcommSelectAllPartitions(bool selectAll)
        {
            if (listView2.Items.Count == 0) return;

            listView2.BeginUpdate();
            foreach (ListViewItem item in listView2.Items)
            {
                item.Checked = selectAll;
            }
            listView2.EndUpdate();

            AppendLog(selectAll ? "已全选分区" : "已取消全选", Color.Blue);
        }

        /// <summary>
        /// 双击分区列表项，选择对应的镜像文件
        /// </summary>
        private void QualcommPartitionDoubleClick()
        {
            if (listView2.SelectedItems.Count == 0) return;

            var item = listView2.SelectedItems[0];
            var partition = item.Tag as PartitionInfo;
            if (partition == null)
            {
                // 如果没有 Tag，尝试从名称获取
                string partitionName = item.Text;
                if (string.IsNullOrEmpty(partitionName)) return;

                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = $"选择 {partitionName} 分区的镜像文件";
                    ofd.Filter = $"镜像文件|{partitionName}.img;{partitionName}.bin;*.img;*.bin|所有文件|*.*";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        // 更新文件路径列 (最后一列)
                        int lastCol = item.SubItems.Count - 1;
                        if (lastCol >= 0)
                        {
                            item.SubItems[lastCol].Text = ofd.FileName;
                            item.Checked = true; // 自动勾选
                            AppendLog($"已为分区 {partitionName} 选择文件: {Path.GetFileName(ofd.FileName)}", Color.Blue);
                        }
                    }
                }
                return;
            }

            // 有 PartitionInfo 的情况
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"选择 {partition.Name} 分区的镜像文件";
                ofd.Filter = $"镜像文件|{partition.Name}.img;{partition.Name}.bin;*.img;*.bin|所有文件|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // 更新文件路径列 (最后一列)
                    int lastCol = item.SubItems.Count - 1;
                    if (lastCol >= 0)
                    {
                        item.SubItems[lastCol].Text = ofd.FileName;
                        item.Checked = true; // 自动勾选
                        AppendLog($"已为分区 {partition.Name} 选择文件: {Path.GetFileName(ofd.FileName)}", Color.Blue);
                    }
                }
            }
        }

        private string _lastSearchKeyword = "";
        private List<ListViewItem> _searchMatches = new List<ListViewItem>();
        private int _currentMatchIndex = 0;
        private bool _isSelectingFromDropdown = false;

        private void QualcommSearchPartition()
        {
            // 如果是从下拉选择触发的，直接定位不更新下拉
            if (_isSelectingFromDropdown)
            {
                _isSelectingFromDropdown = false;
                string selectedName = select4.Text?.Trim()?.ToLower();
                if (!string.IsNullOrEmpty(selectedName))
                {
                    LocatePartitionByName(selectedName);
                }
                return;
            }

            string keyword = select4.Text?.Trim()?.ToLower();
            
            // 如果搜索框为空，重置所有高亮
            if (string.IsNullOrEmpty(keyword))
            {
                ResetPartitionHighlights();
                _lastSearchKeyword = "";
                _searchMatches.Clear();
                _currentMatchIndex = 0;
                return;
            }
            
            // 如果关键词相同，跳转到下一个匹配项
            if (keyword == _lastSearchKeyword && _searchMatches.Count > 1)
            {
                JumpToNextMatch();
                return;
            }
            
            _lastSearchKeyword = keyword;
            _searchMatches.Clear();
            _currentMatchIndex = 0;
            
            // 收集匹配的分区名称用于下拉建议
            var suggestions = new List<string>();
            
            listView2.BeginUpdate();
            
                foreach (ListViewItem item in listView2.Items)
                {
                string partitionName = item.Text?.ToLower() ?? "";
                string originalName = item.Text ?? "";
                bool isMatch = partitionName.Contains(keyword);
                
                if (isMatch)
                {
                    // 精确匹配用深色，模糊匹配用浅色
                    item.BackColor = (partitionName == keyword) ? Color.Gold : Color.LightYellow;
                    _searchMatches.Add(item);
                    
                    // 添加到下拉建议（最多显示10个）
                    if (suggestions.Count < 10)
                    {
                        suggestions.Add(originalName);
                    }
                }
                else
                {
                    item.BackColor = Color.Transparent;
                }
            }
            
            listView2.EndUpdate();
            
            // 更新下拉建议列表
            UpdateSearchSuggestions(suggestions);
            
            // 滚动到第一个匹配项
            if (_searchMatches.Count > 0)
            {
                _searchMatches[0].Selected = true;
                _searchMatches[0].EnsureVisible();
                
                // 显示匹配数量（不重复打日志）
                if (_searchMatches.Count > 1)
                {
                    // 在状态栏或其他地方显示，避免刷屏
                }
            }
            else if (keyword.Length >= 2)
            {
                // 只有输入2个以上字符才提示未找到
                AppendLog($"未找到分区: {keyword}", Color.Orange);
            }
        }

        private void JumpToNextMatch()
        {
            if (_searchMatches.Count == 0) return;
            
            // 取消当前选中
            if (_currentMatchIndex < _searchMatches.Count)
            {
                _searchMatches[_currentMatchIndex].Selected = false;
            }
            
            // 跳转到下一个
            _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
            _searchMatches[_currentMatchIndex].Selected = true;
            _searchMatches[_currentMatchIndex].EnsureVisible();
        }

        private void ResetPartitionHighlights()
        {
            listView2.BeginUpdate();
            foreach (ListViewItem item in listView2.Items)
            {
                item.BackColor = Color.Transparent;
            }
            listView2.EndUpdate();
        }

        private void UpdateSearchSuggestions(List<string> suggestions)
        {
            // 保存当前输入的文本
            string currentText = select4.Text;
            
            // 更新下拉项
            select4.Items.Clear();
            foreach (var name in suggestions)
            {
                select4.Items.Add(name);
            }
            
            // 恢复输入的文本（防止被清空）
            select4.Text = currentText;
        }

        private void LocatePartitionByName(string partitionName)
        {
            ResetPartitionHighlights();
            
            foreach (ListViewItem item in listView2.Items)
            {
                if (item.Text?.ToLower() == partitionName)
                    {
                    item.BackColor = Color.Gold;
                        item.Selected = true;
                        item.EnsureVisible();
                        listView2.Focus();
                        break;
                }
            }
        }

        #endregion
        // 窗体关闭时清理资源
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // 释放高通控制器
            if (_qualcommController != null)
            {
                _qualcommController.Dispose();
                _qualcommController = null;
            }

            // 释放背景图片
            if (this.BackgroundImage != null)
            {
                this.BackgroundImage.Dispose();
                this.BackgroundImage = null;
            }

            // 清空预览
            ClearImagePreview();

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void InitializeUrlComboBox()
        {
            // 只保留已验证可用的API
            string[] defaultUrls = new[]
            {
                "https://img.xjh.me/random_img.php?return=302",
                "https://www.dmoe.cc/random.php",
                "https://www.loliapi.com/acg/",
                "https://t.alcy.cc/moe"
            };

            uiComboBox3.Items.Clear();
            foreach (string url in defaultUrls)
            {
                uiComboBox3.Items.Add(url);
            }

            if (uiComboBox3.Items.Count > 0)
            {
                uiComboBox3.SelectedIndex = 0;
            }
        }

        private void InitializeImagePreview()
        {
            // 清空预览控件
            ClearImagePreview();

            // 设置预览控件属性
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.BackColor = Color.Black;

        }

        private void SaveOriginalPositions()
        {
            try
            {
                // 保存原始位置和大小
                originalinput6Location = input6.Location;
                originalbutton4Location = button4.Location;
                originalcheckbox13Location = checkbox13.Location;
                originalinput7Location = input7.Location;
                originalinput9Location = input9.Location;
                originallistView2Location = listView2.Location;
                originallistView2Size = listView2.Size;
                originaluiGroupBox4Location =uiGroupBox4.Location;
                originaluiGroupBox4Size =uiGroupBox4.Size;

            }
            catch (Exception ex)
            {
                AppendLog($"保存原始位置失败: {ex.Message}", Color.Red);
            }
        }

        private void AppendLog(string message, Color? color = null)
        {
            if (uiRichTextBox1.InvokeRequired)
            {
                uiRichTextBox1.BeginInvoke(new Action<string, Color?>(AppendLog), message, color);
                return;
            }

            Color logColor = color ?? Color.Black;

            // 写入文件
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] {message}" + Environment.NewLine);
            }
            catch { }

            // 显示到 UI
            uiRichTextBox1.SelectionColor = logColor;
            uiRichTextBox1.AppendText(message + "\n");
            uiRichTextBox1.SelectionStart = uiRichTextBox1.Text.Length;
            uiRichTextBox1.ScrollToCaret();
        }

        /// <summary>
        /// 详细调试日志 - 只写入文件，不显示在 UI
        /// </summary>
        private void AppendLogDetail(string message)
        {
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] [DEBUG] {message}" + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        private void InitializeLogSystem()
        {
            try
            {
                string logFolderPath = "C:\\Tool_Log";
                if (!Directory.Exists(logFolderPath))
                {
                    Directory.CreateDirectory(logFolderPath);
                }

                // 清理 7 天前的旧日志
                CleanOldLogs(logFolderPath, 7);

                string logFileName = $"{DateTime.Now:yyyy-MM-dd_HH.mm.ss}_log.txt";
                logFilePath = Path.Combine(logFolderPath, logFileName);
            }
            catch
            {
                // 日志初始化失败时使用临时目录
                logFilePath = Path.Combine(Path.GetTempPath(), $"LoveAlways_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
        }

        /// <summary>
        /// 清理指定天数之前的旧日志
        /// </summary>
        private void CleanOldLogs(string logFolder, int daysToKeep)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                var oldFiles = Directory.GetFiles(logFolder, "*_log.txt")
                    .Where(f => File.GetCreationTime(f) < cutoff)
                    .ToArray();

                foreach (var file in oldFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 写入日志文件头部信息
        /// </summary>
        private void WriteLogHeader(string sysInfo)
        {
            try
            {
                var header = new StringBuilder();
                header.AppendLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine($"系统: {sysInfo}");
                header.AppendLine($"版本: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                header.AppendLine();

                File.WriteAllText(logFilePath, header.ToString());
            }
            catch { }
        }

        /// <summary>
        /// 查看日志菜单点击事件 - 打开日志文件夹并选中当前日志
        /// </summary>
        private void 查看日志ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string logFolder = Path.GetDirectoryName(logFilePath);
                
                if (!Directory.Exists(logFolder))
                {
                    Directory.CreateDirectory(logFolder);
                }

                // 如果当前日志文件存在，使用 Explorer 打开并选中它
                if (File.Exists(logFilePath))
                {
                    // 使用 /select 参数打开资源管理器并选中文件
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logFilePath}\"");
                    AppendLog($"已打开日志文件夹: {logFolder}", Color.Blue);
                }
                else
                {
                    // 文件不存在，直接打开文件夹
                    System.Diagnostics.Process.Start("explorer.exe", logFolder);
                    AppendLog($"已打开日志文件夹: {logFolder}", Color.Blue);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"打开日志失败: {ex.Message}", Color.Red);
                MessageBox.Show($"无法打开日志文件夹: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "选择本地图片";
            openFileDialog.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*";
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                selectedLocalImagePath = openFileDialog.FileName;
                AppendLog($"已选择本地文件：{selectedLocalImagePath}", Color.Green);

                // 强制垃圾回收释放内存
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 使用异步加载避免UI卡死
                Task.Run(() => LoadLocalImage(selectedLocalImagePath));
            }
        }

        private void LoadLocalImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    this.Invoke(new Action(() => AppendLog("文件不存在", Color.Red)));
                    return;
                }

                // 检查文件大小
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length > 50 * 1024 * 1024) // 50MB限制
                {
                    this.Invoke(new Action(() => AppendLog($"文件过大（{fi.Length / 1024 / 1024}MB），请选择小于50MB的图片", Color.Red)));
                    return;
                }

                // 方法1：使用超低质量加载
                using (Bitmap original = LoadImageWithLowQuality(filePath))
                {
                    if (original != null)
                    {
                        // 创建适合窗体大小的缩略图
                        Size targetSize = this.Invoke(new Func<Size>(() => this.ClientSize));
                        using (Bitmap resized = ResizeImageToFitWithLowMemory(original, targetSize))
                        {
                            if (resized != null)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    // 释放旧图片
                                    if (this.BackgroundImage != null)
                                    {
                                        this.BackgroundImage.Dispose();
                                        this.BackgroundImage = null;
                                    }

                                    // 设置新图片
                                    this.BackgroundImage = resized.Clone() as Bitmap;
                                    this.BackgroundImageLayout = ImageLayout.Stretch;

                                    // 添加到预览
                                    AddImageToPreview(resized.Clone() as Image, Path.GetFileName(filePath));

                                    AppendLog($"本地图片设置成功（{resized.Width}x{resized.Height}）", Color.Green);
                                }));
                            }
                        }
                    }
                    else
                    {
                        this.Invoke(new Action(() => AppendLog("无法加载图片，文件可能已损坏", Color.Red)));
                    }
                }

                // 再次垃圾回收
                GC.Collect();
            }
            catch (OutOfMemoryException)
            {
                this.Invoke(new Action(() =>
                {
                    AppendLog("内存严重不足，请尝试重启应用", Color.Red);
                    AppendLog("建议：关闭其他程序，释放内存", Color.Yellow);
                }));
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() => AppendLog($"图片加载失败：{ex.Message}", Color.Red)));
            }
        }

        private Bitmap LoadImageWithLowQuality(string filePath)
        {
            try
            {
                // 使用最小内存的方式加载图片
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // 读取图片信息但不加载全部数据
                    using (Image img = Image.FromStream(fs, false, false))
                    {
                        // 如果图片很大，先创建缩略图
                        if (img.Width > 2000 || img.Height > 2000)
                        {
                            int newWidth = Math.Min(img.Width / 4, 800);
                            int newHeight = Math.Min(img.Height / 4, 600);

                            Bitmap thumbnail = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
                            using (Graphics g = Graphics.FromImage(thumbnail))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                g.DrawImage(img, 0, 0, newWidth, newHeight);
                            }
                            return thumbnail;
                        }
                        else
                        {
                            // 直接返回新Bitmap
                            return new Bitmap(img);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"加载图片失败：{ex.Message}", Color.Red);
                return null;
            }
        }

        private Bitmap ResizeImageToFitWithLowMemory(Image original, Size targetSize)
        {
            try
            {
                // 限制预览图片尺寸
                int maxWidth = Math.Min(800, targetSize.Width);
                int maxHeight = Math.Min(600, targetSize.Height);

                int newWidth, newHeight;

                // 计算新尺寸
                double ratioX = (double)maxWidth / original.Width;
                double ratioY = (double)maxHeight / original.Height;
                double ratio = Math.Min(ratioX, ratioY);

                newWidth = (int)(original.Width * ratio);
                newHeight = (int)(original.Height * ratio);

                // 确保最小尺寸
                newWidth = Math.Max(100, newWidth);
                newHeight = Math.Max(100, newHeight);

                // 创建新Bitmap
                Bitmap result = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);

                using (Graphics g = Graphics.FromImage(result))
                {
                    // 使用最低质量设置节省内存
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                    g.DrawImage(original, 0, 0, newWidth, newHeight);
                }

                return result;
            }
            catch (Exception ex)
            {
                AppendLog($"调整图片大小失败：{ex.Message}", Color.Red);
                return null;
            }
        }

        private async void Button3_Click(object sender, EventArgs e)
        {
            string url = uiComboBox3.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                AppendLog("请输入或选择壁纸URL", Color.Red);
                return;
            }

            // 清理URL
            url = url.Trim('`', '\'');
            AppendLog($"正在从URL获取壁纸：{url}", Color.Blue);

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();

            try
            {
                // 使用最简单的方式
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15); // 增加超时时间
                    client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/*"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));

                    // 显示加载提示
                    AppendLog("正在下载图片...", Color.Blue);

                    byte[] imageData = null;

                    // 特殊处理某些API
                    if (url.Contains("picsum.photos"))
                    {
                        // 添加随机参数避免缓存
                        url += $"?random={DateTime.Now.Ticks}";
                    }
                    else if (url.Contains("loliapi.com"))
            {
                // 特殊处理loliapi.com API响应...
                AppendLog("正在处理loliapi.com API响应...", Color.Blue);
                // 注意：loliapi.com 直接返回图片二进制数据，不需要JSON参数
            }

                    // 发送请求并获取响应
                    using (HttpResponseMessage response = await client.GetAsync(url))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        // 检查响应内容类型
                        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                        AppendLog($"响应内容类型: {contentType}", Color.Blue);
                        
                        // 检查是否是图片
                        if (contentType.StartsWith("image/"))
                        {
                            imageData = await response.Content.ReadAsByteArrayAsync();
                            AppendLog($"下载的图片大小: {imageData.Length} 字节", Color.Blue);
                        }
                        else if (contentType.Contains("json"))
                        {
                            // 处理JSON响应
                            string jsonContent = await response.Content.ReadAsStringAsync();
                            AppendLog($"JSON响应长度: {jsonContent.Length}", Color.Blue);
                            
                            // 尝试从JSON中提取图片URL
                            string imageUrl = ExtractImageUrlFromJson(jsonContent);
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                AppendLog($"从JSON中提取到图片URL: {imageUrl}", Color.Blue);
                                // 下载提取到的图片
                                using (HttpResponseMessage imageResponse = await client.GetAsync(imageUrl))
                                {
                                    imageResponse.EnsureSuccessStatusCode();
                                    imageData = await imageResponse.Content.ReadAsByteArrayAsync();
                                    AppendLog($"下载的图片大小: {imageData.Length} 字节", Color.Blue);
                                }
                            }
                            else
                            {
                                AppendLog("无法从JSON响应中提取图片URL", Color.Red);
                                AppendLog($"JSON内容: {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}...", Color.Yellow);
                                return;
                            }
                        }
                        else
                        {
                            // 可能是重定向或HTML响应
                            string content = await response.Content.ReadAsStringAsync();
                            AppendLog($"响应不是图片，内容长度: {content.Length}", Color.Yellow);
                            
                            // 尝试从HTML中提取图片URL
                            string imageUrl = ExtractImageUrlFromHtml(content);
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                AppendLog($"从HTML中提取到图片URL: {imageUrl}", Color.Blue);
                                // 下载提取到的图片
                                using (HttpResponseMessage imageResponse = await client.GetAsync(imageUrl))
                                {
                                    imageResponse.EnsureSuccessStatusCode();
                                    imageData = await imageResponse.Content.ReadAsByteArrayAsync();
                                    AppendLog($"下载的图片大小: {imageData.Length} 字节", Color.Blue);
                                }
                            }
                            else
                            {
                                AppendLog("无法从响应中提取图片URL", Color.Red);
                                // 显示部分响应内容用于调试
                                if (content.Length > 0)
                                {
                                    AppendLog($"响应内容预览: {content.Substring(0, Math.Min(500, content.Length))}...", Color.Yellow);
                                }
                                return;
                            }
                        }
                    }

                    if (imageData == null || imageData.Length < 1000)
                    {
                        AppendLog("下载的数据无效", Color.Red);
                        return;
                    }

                    // 直接从内存加载图片，避免文件扩展名问题
                    LoadAndSetBackgroundFromMemory(imageData, url);
                }
            }
            catch (HttpRequestException ex)
            {
                AppendLog($"网络请求失败：{ex.Message}", Color.Red);
                AppendLog("请检查网络连接或尝试其他网址", Color.Yellow);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("参数无效") || ex.Message.Contains("Invalid parameter"))
                {
                    AppendLog("图片格式可能不受完全支持，尝试使用其他图片URL", Color.Yellow);
                   // AppendLog($"错误详情：{ex.Message}", Color.Red);
                }
                else
                {
                    AppendLog($"壁纸获取失败：{ex.Message}", Color.Red);
                //    AppendLog($"错误详情：{ex.ToString()}", Color.Yellow);
                }
            }
        }

        private string ExtractImageUrlFromJson(string jsonContent)
        {
            try
            {
                // 尝试简单的JSON解析
                jsonContent = jsonContent.Trim();
                
                // 处理常见的JSON格式
                if (jsonContent.StartsWith("{") && jsonContent.EndsWith("}"))
                {
                    // 尝试提取url字段
                    int urlIndex = jsonContent.IndexOf("\"url\"", StringComparison.OrdinalIgnoreCase);
                    if (urlIndex >= 0)
                    {
                        int startIndex = jsonContent.IndexOf(":", urlIndex) + 1;
                        int endIndex = jsonContent.IndexOf("\"", startIndex + 1);
                        if (startIndex > 0 && endIndex > startIndex)
                        {
                            string url = jsonContent.Substring(startIndex, endIndex - startIndex).Trim('"', ' ', '\t', ',');
                            if (url.StartsWith("http"))
                            {
                                return url;
                            }
                        }
                    }
                    
                    // 尝试提取data字段
                    int dataIndex = jsonContent.IndexOf("\"data\"", StringComparison.OrdinalIgnoreCase);
                    if (dataIndex >= 0)
                    {
                        int startIndex = jsonContent.IndexOf(":", dataIndex) + 1;
                        int endIndex = jsonContent.IndexOf("\"", startIndex + 1);
                        if (startIndex > 0 && endIndex > startIndex)
                        {
                            string url = jsonContent.Substring(startIndex, endIndex - startIndex).Trim('"', ' ', '\t', ',');
                            if (url.StartsWith("http"))
                            {
                                return url;
                            }
                        }
                    }
                }
                else if (jsonContent.StartsWith("[") && jsonContent.EndsWith("]"))
                {
                    // 处理数组格式
                    int urlIndex = jsonContent.IndexOf("\"url\"", StringComparison.OrdinalIgnoreCase);
                    if (urlIndex >= 0)
                    {
                        int startIndex = jsonContent.IndexOf(":", urlIndex) + 1;
                        int endIndex = jsonContent.IndexOf("\"", startIndex + 1);
                        if (startIndex > 0 && endIndex > startIndex)
                        {
                            string url = jsonContent.Substring(startIndex, endIndex - startIndex).Trim('"', ' ', '\t', ',');
                            if (url.StartsWith("http"))
                            {
                                return url;
                            }
                        }
                    }
                }
                
                // 尝试使用正则表达式提取URL
                System.Text.RegularExpressions.Regex urlRegex = new System.Text.RegularExpressions.Regex(
                    @"https?://[^\s""'<>]+?\.(?:jpg|jpeg|png|gif|webp)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                System.Text.RegularExpressions.Match match = urlRegex.Match(jsonContent);
                if (match.Success)
                {
                    return match.Value;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"解析JSON失败：{ex.Message}", Color.Red);
                return null;
            }
        }

        private string ExtractImageUrlFromHtml(string html)
        {
            try
            {
                // 简单的正则表达式提取图片URL
                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                    @"https?://[^\s""'<>]+?\.(?:jpg|jpeg|png|gif|webp)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                System.Text.RegularExpressions.Match match = regex.Match(html);
                if (match.Success)
                {
                    return match.Value;
                }
                
                // 尝试提取所有可能的URL
                System.Text.RegularExpressions.Regex urlRegex = new System.Text.RegularExpressions.Regex(
                    @"https?://[^\s""'<>]+", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                System.Text.RegularExpressions.MatchCollection matches = urlRegex.Matches(html);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string url = m.Value;
                    if (url.Contains(".jpg") || url.Contains(".jpeg") || url.Contains(".png") || 
                        url.Contains(".gif") || url.Contains(".webp"))
                    {
                        return url;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"提取图片URL失败：{ex.Message}", Color.Red);
                return null;
            }
        }

        private void LoadAndSetBackgroundFromMemory(byte[] imageData, string sourceUrl)
        {
            try
            {
                // 检查数据是否有效
                if (imageData == null || imageData.Length < 100)
                {
                    AppendLog("图片数据无效或过小", Color.Red);
                    return;
                }

                // 检查是否是有效的图片数据（通过文件头）
                string fileHeader = BitConverter.ToString(imageData, 0, Math.Min(8, imageData.Length)).ToLower();
                bool isImage = false;
                
                // 检查常见图片格式的文件头
                if (fileHeader.StartsWith("89-50-4e-47") || // PNG
                    fileHeader.StartsWith("ff-d8") || // JPEG
                    fileHeader.StartsWith("42-4d") || // BMP
                    fileHeader.StartsWith("47-49-46") || // GIF
                    fileHeader.StartsWith("52-49-46-46") || // WebP
                    fileHeader.StartsWith("00-00-00-1c") || // MP4
                    fileHeader.StartsWith("00-00-00-18")) // MP4
                {
                    isImage = true;
                }

                if (!isImage)
                {
                    AppendLog("文件不是有效的图片格式", Color.Red);
                    AppendLog($"文件头: {fileHeader}", Color.Yellow);
                    return;
                }

                // 特殊处理WebP格式
                bool isWebP = fileHeader.StartsWith("52-49-46-46");
                if (isWebP)
                {
                    AppendLog("检测到WebP格式图片，使用特殊处理...", Color.Blue);
                }

                // 创建内存流
                using (MemoryStream ms = new MemoryStream(imageData))
                {
                    ms.Position = 0; // 确保流位置在开始
                    
                    try
                    {
                        using (Image original = Image.FromStream(ms, false, false))
                        {
                            if (original != null)
                            {
                                AppendLog($"成功加载图片，尺寸: {original.Width}x{original.Height}", Color.Blue);
                                
                                Size targetSize = this.ClientSize;
                                using (Bitmap resized = ResizeImageToFitWithLowMemory(original, targetSize))
                                {
                                    if (resized != null)
                                    {
                                        // 释放旧图片
                                        if (this.BackgroundImage != null)
                                        {
                                            this.BackgroundImage.Dispose();
                                            this.BackgroundImage = null;
                                        }

                                        // 设置新图片
                                        this.BackgroundImage = resized.Clone() as Bitmap;
                                        this.BackgroundImageLayout = ImageLayout.Stretch;

                                        // 添加到预览
                                      //  AddImageToPreview(resized.Clone() as Image, "网络图片");

                                    //    AppendLog($"网络图片设置成功（{resized.Width}x{resized.Height}）", Color.Green);

                                        // 添加到历史记录
                                        if (!urlHistory.Contains(sourceUrl))
                                        {
                                            urlHistory.Add(sourceUrl);
                                        }

                                        // 更新下拉框
                                        UpdateUrlComboBox(sourceUrl);
                                    }
                                }
                            }
                            else
                            {
                                AppendLog("下载的文件不是有效图片", Color.Red);
                            }
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("参数无效") || ex.Message.Contains("Invalid parameter"))
                    {
                        // 处理"参数无效"错误，这通常发生在WebP格式不被完全支持时
                        AppendLog("图片格式可能不受完全支持，尝试转换...", Color.Yellow);
                        
                        // 尝试保存为临时文件然后重新加载
                        string tempFile = Path.GetTempFileName() + (isWebP ? ".webp" : ".jpg");
                        try
                        {
                            File.WriteAllBytes(tempFile, imageData);
                         //   AppendLog($"已保存临时文件: {tempFile}", Color.Blue);
                            
                            // 尝试使用不同的方式加载
                            using (Image original = Image.FromFile(tempFile))
                            {
                                if (original != null)
                                {
                                  //  AppendLog($"成功从文件加载图片，尺寸: {original.Width}x{original.Height}", Color.Blue);
                                    
                                    Size targetSize = this.ClientSize;
                                    using (Bitmap resized = ResizeImageToFitWithLowMemory(original, targetSize))
                                    {
                                        if (resized != null)
                                        {
                                            // 释放旧图片
                                            if (this.BackgroundImage != null)
                                            {
                                                this.BackgroundImage.Dispose();
                                                this.BackgroundImage = null;
                                            }

                                            // 设置新图片
                                            this.BackgroundImage = resized.Clone() as Bitmap;
                                            this.BackgroundImageLayout = ImageLayout.Stretch;

                                            // 添加到预览
                                            AddImageToPreview(resized.Clone() as Image, "网络图片");

                                         //   AppendLog($"网络图片设置成功（{resized.Width}x{resized.Height}）", Color.Green);

                                            // 添加到历史记录
                                            if (!urlHistory.Contains(sourceUrl))
                                            {
                                                urlHistory.Add(sourceUrl);
                                            }

                                            // 更新下拉框
                                            UpdateUrlComboBox(sourceUrl);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // 尝试使用GDI+的其他方法
                            try
                            {
                            //    AppendLog("尝试使用GDI+直接绘制...", Color.Yellow);
                                
                                // 创建一个新的Bitmap并手动绘制
                                using (Bitmap tempBmp = new Bitmap(800, 600))
                                using (Graphics g = Graphics.FromImage(tempBmp))
                                {
                                    g.Clear(Color.White);
                                    
                                    // 尝试使用WebClient下载并绘制
                                    AppendLog("图片加载失败", Color.Yellow);
                                    AppendLog("请尝试使用其他图片URL", Color.Yellow);
                                }
                            }
                            catch (Exception)
                            {
                                AppendLog("无法处理此图片格式", Color.Red);
                            }
                        }
                        finally
                        {
                            // 清理临时文件
                            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                        }
                    }
                }

                // 垃圾回收
                GC.Collect();
            }
            catch (OutOfMemoryException)
            {
                AppendLog("内存不足，无法处理图片", Color.Red);
            }
            catch (Exception ex)
            {
                AppendLog($"图片处理失败：{ex.Message}", Color.Red);
                // 输出更详细的错误信息
             //   AppendLog($"错误详情：{ex.ToString()}", Color.Yellow);
            }
        }

        private void AddImageToPreview(Image image, string description)
        {
            if (image == null) return;

            try
            {
                // 限制预览图片数量
                if (previewImages.Count >= MAX_PREVIEW_IMAGES)
                {
                    // 移除最旧的预览
                    Image oldImage = previewImages[0];
                    previewImages.RemoveAt(0);
                    oldImage.Dispose();
                }

                // 添加新预览
                previewImages.Add(image);

                // 更新预览控件
                UpdateImagePreview();

          //      AppendLog($"已添加到预览：{description}", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"更新预览失败：{ex.Message}", Color.Red);
            }
        }

        private void UpdateImagePreview()
        {
            if (previewImages.Count == 0)
            {
                // 显示默认图片或清空
                pictureBox1.Image = null;
                pictureBox1.Invalidate();
                return;
            }

            try
            {
                // 显示最新的预览图片
                Image latestImage = previewImages[previewImages.Count - 1];
                pictureBox1.Image = latestImage;

                // 更新预览标签
                UpdatePreviewLabel();
            }
            catch (Exception ex)
            {
                AppendLog($"显示预览失败：{ex.Message}", Color.Red);
            }
        }

        private void UpdatePreviewLabel()
        {
            if (previewImages.Count > 0 && label3 != null)
            {
                Image currentImage = pictureBox1.Image;
                if (currentImage != null)
                {
                    string language = uiComboBox4.SelectedItem?.ToString() ?? "中文";
                    bool isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);

                    if (isEnglish)
                    {
                        label3.Text = $"Preview: {currentImage.Width}×{currentImage.Height} ({previewImages.Count} images)";
                    }
                    else
                    {
                        label3.Text = $"预览: {currentImage.Width}×{currentImage.Height} ({previewImages.Count}张图片)";
                    }
                }
            }
        }

        private void ClearImagePreview()
        {
            try
            {
                // 清空预览控件
                pictureBox1.Image = null;

                // 释放所有预览图片
                foreach (Image img in previewImages)
                {
                    img?.Dispose();
                }
                previewImages.Clear();

                // 重置标签
                label3.Text = "预览";
            }
            catch (Exception ex)
            {
                AppendLog($"清空预览失败：{ex.Message}", Color.Red);
            }
        }

        private void UpdateUrlComboBox(string newUrl)
        {
            if (!uiComboBox3.Items.Contains(newUrl))
            {
                uiComboBox3.Items.Add(newUrl);
            }
        }

        private void Slider1_ValueChanged(object sender, EventArgs e)
        {
            int value = (int)slider1.Value;
            float opacity = Math.Max(0.2f, value / 100.0f);
            this.Opacity = opacity;
        }

        private void UiComboBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedLanguage = uiComboBox4.SelectedItem?.ToString() ?? "中文";
            SwitchLanguage(selectedLanguage);
        }

        private void SwitchLanguage(string language)
        {
            isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);

            if (isEnglish)
            {
                // 英文界面
                tabPage6.Text = "Settings";
                label1.Text = "Background Blur";
                label2.Text = "Wallpaper";
                label3.Text = "Preview";
                label4.Text = "Language";
                button2.Text = "Local Wallpaper";
                button3.Text = "Apply";
                uiComboBox3.Watermark = "URL";
                
                // 更新其他标签页
                tabPage2.Text = "Home";
                tabPage2.Text = "Qualcomm";
                tabPage4.Text = "MTK";
                tabPage5.Text = "Spreadtrum";
                
                // 更新菜单
                快捷重启ToolStripMenuItem.Text = "Quick Restart";
                toolStripMenuItem1.Text = "EDL Operations";
                其他ToolStripMenuItem.Text = "Others";
                
                // 更新按钮
                uiButton2.Text = "Erase Partition";
                uiButton3.Text = "Write Partition";
                uiButton4.Text = "Read Partition";
                uiButton5.Text = "Read Partition Table";
                select4.PlaceholderText = "Find Partition";
            }
            else
            {
                // 中文界面
                tabPage6.Text = "设置";
                label1.Text = "背景模糊度";
                label2.Text = "壁纸";
                label3.Text = "预览";
                label4.Text = "语言";
                button2.Text = "本地壁纸";
                button3.Text = "应用";
                uiComboBox3.Watermark = "Url";
                
                // 更新其他标签页
                tabPage2.Text = "主页";
                tabPage2.Text = "高通平台";
                tabPage4.Text = "MTK平台";
                tabPage5.Text = "展讯平台";
                
                // 更新菜单
                快捷重启ToolStripMenuItem.Text = "快捷重启";
                toolStripMenuItem1.Text = "EDL操作";
                其他ToolStripMenuItem.Text = "其他";
                
                // 更新按钮
                uiButton2.Text = "擦除分区";
                uiButton3.Text = "写入分区";
                uiButton4.Text = "读取分区";
                uiButton5.Text = "读取分区表";
                select4.PlaceholderText = "查找分区";
            }

            // 更新预览标签
            UpdatePreviewLabel();

            AppendLog($"界面语言已切换为：{language}", Color.Green);
        }

        private void Checkbox17_CheckedChanged(object sender, EventArgs e)
        {
            if (checkbox17.Checked)
            {
                // 自动取消勾选checkbox19
               checkbox19.Checked = false;
                
                // 检查当前是否已经是紧凑布局（通过 input7 的可见性判断）
                if (input7.Visible)
                {
                    // 如果 input7 可见，说明当前是默认布局，需要改为紧凑布局
                    ApplyCompactLayout();
                }
                // 如果 input7 不可见，说明已经是紧凑布局，不做改变
            }
            
            // 更新认证模式
            UpdateAuthMode();
        }

        private void Checkbox19_CheckedChanged(object sender, EventArgs e)
        {
            if (checkbox19.Checked)
            {
                // 自动取消勾选 checkbox17
                checkbox17.Checked = false;
                
                RestoreOriginalLayout();
            }
            else
            {
                ApplyCompactLayout();
            }
            
            // 更新认证模式
            UpdateAuthMode();
        }

        private void ApplyCompactLayout()
        {
            try
            {
                // 挂起布局更新，减少闪烁
                this.SuspendLayout();
               uiGroupBox4.SuspendLayout();
                listView2.SuspendLayout();

                // 移除 input9, input7
                input9.Visible = false;
                input7.Visible = false;

                // 上移 input6, button4 到 input7 和 input9 的位置
                input6.Location = new Point(input6.Location.X, input7.Location.Y);
                button4.Location = new Point(button4.Location.X, input9.Location.Y);

                // 上移 checkbox13 到固定位置
                checkbox13.Location = new Point(6, 25);

                // 向上调整uiGroupBox4 和 listView2 的位置并变长
                const int VERTICAL_ADJUSTMENT = 38; // 使用固定数值调整
               uiGroupBox4.Location = new Point(uiGroupBox4.Location.X,uiGroupBox4.Location.Y - VERTICAL_ADJUSTMENT);
               uiGroupBox4.Size = new Size(uiGroupBox4.Size.Width,uiGroupBox4.Size.Height + VERTICAL_ADJUSTMENT);
                listView2.Size = new Size(listView2.Size.Width, listView2.Size.Height + VERTICAL_ADJUSTMENT);

                // 恢复布局更新
                listView2.ResumeLayout(false);
               uiGroupBox4.ResumeLayout(false);
                this.ResumeLayout(false);
                this.PerformLayout();

            }
            catch (Exception ex)
            {
                AppendLog($"应用布局失败: {ex.Message}", Color.Red);
            }
        }

        private void RestoreOriginalLayout()
        {
            try
            {
                // 挂起布局更新，减少闪烁
                this.SuspendLayout();
               uiGroupBox4.SuspendLayout();
                listView2.SuspendLayout();

                // 恢复 input9, input7 的显示
                input9.Visible = true;
                input7.Visible = true;

                // 恢复原始位置
                input6.Location = originalinput6Location;
                button4.Location = originalbutton4Location;
                // 恢复 checkbox13 到固定位置 (6, 25)
                checkbox13.Location = new Point(6, 25);

                // 恢复原始大小和位置
               uiGroupBox4.Location = originaluiGroupBox4Location;
               uiGroupBox4.Size = originaluiGroupBox4Size;
                listView2.Size = originallistView2Size;

                // 恢复布局更新
                listView2.ResumeLayout(false);
               uiGroupBox4.ResumeLayout(false);
                this.ResumeLayout(false);
                this.PerformLayout();

            }
            catch (Exception ex)
            {
                AppendLog($"恢复布局失败: {ex.Message}", Color.Red);
            }
        }

        private void uiGroupBox1_Click(object sender, EventArgs e)
        {

        }

        private void toolStripMenuItem6_Click(object sender, EventArgs e)
        {

        }

        private void 重启恢复ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void Select3_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedItem = select3.Text;
            bool isAutoOrCustom = selectedItem == "自动识别或自选引导";
            
            // 禁用或启用输入字段
            input9.Enabled = isAutoOrCustom;
            input8.Enabled = isAutoOrCustom;
            input7.Enabled = isAutoOrCustom;
            
            // 处理 input8 的显示文本
            if (!isAutoOrCustom)
            {
                // 只在首次禁用时保存原始文本
                if (string.IsNullOrEmpty(input8OriginalText))
                {
                    input8OriginalText = input8.Text;
                }
                // 设置禁用状态的提示文本
                input8.Text = "当前模式禁止选择文件";
            }
            else
            {
                // 强制清空以显示占位符
                input8.Text = "";
                // 重置原始文本存储
                input8OriginalText = "";
            }
        }

        #region Fastboot 模块

        private void InitializeFastbootModule()
        {
            try
            {
                // 设置 fastboot.exe 路径
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastboot.exe");
                FastbootCommand.SetFastbootPath(fastbootPath);

                // 创建 Fastboot UI 控制器
                _fastbootController = new FastbootUIController(
                    (msg, color) => AppendLog(msg, color),
                    msg => AppendLogDetail(msg));

                // 设置 listView5 支持复选框
                listView5.MultiSelect = true;
                listView5.CheckBoxes = true;
                listView5.FullRowSelect = true;

                // 绑定控件 - tabPage3 上的 Fastboot 控件
                _fastbootController.BindControls(
                    portComboBox: uiComboBox2,           // Fastboot 设备选择（使用现有的 uiComboBox2）
                    partitionListView: listView5,        // 分区列表
                    progressBar: null,                   // 可以后续添加进度条
                    commandComboBox: uiComboBox2,        // 快捷命令下拉框 (device, unlock 等)
                    payloadTextBox: uiTextBox1,          // Payload 路径
                    outputPathTextBox: input1,           // 输出路径
                    autoRebootCheckbox: checkbox44,      // 自动重启
                    switchSlotCheckbox: checkbox41,      // 切换A槽
                    eraseGoogleLockCheckbox: checkbox43, // 擦除谷歌锁
                    keepDataCheckbox: checkbox50,        // 保留数据
                    fbdFlashCheckbox: checkbox45,        // FBD刷写
                    unlockBlCheckbox: checkbox22,        // 解锁BL
                    lockBlCheckbox: checkbox21           // 锁定BL
                );

                // ========== tabPage3 Fastboot 页面按钮事件 ==========
                
                // uiButton11 = 读取信息
                uiButton11.Click += async (s, e) => await FastbootReadInfoAsync();

                // uiButton18 = 读取分区表
                uiButton18.Click += async (s, e) => await FastbootReadPartitionTableAsync();

                // uiButton19 = 提取镜像 (Fastboot 模式下一般不支持读取分区)
                uiButton19.Click += (s, e) => AppendLog("Fastboot 模式不支持直接读取分区内容", Color.Orange);

                // uiButton20 = 写入分区
                uiButton20.Click += async (s, e) => await FastbootFlashPartitionsAsync();

                // uiButton21 = 擦除分区
                uiButton21.Click += async (s, e) => await FastbootErasePartitionsAsync();

                // uiButton22 = 修复FBD (后续实现)
                uiButton22.Click += (s, e) => AppendLog("FBD 修复功能开发中...", Color.Orange);

                // uiButton10 = 执行 (执行刷机脚本或快捷命令)
                uiButton10.Click += async (s, e) => await FastbootExecuteAsync();

                // button8 = 浏览 (选择输出路径/刷机目录)
                button8.Click += (s, e) => FastbootSelectFlashDirectory();

                // button9 = 浏览 (选择 Payload/刷机脚本)
                button9.Click += (s, e) => FastbootSelectPayload();

                // checkbox22 = 解锁BL
                checkbox22.CheckedChanged += async (s, e) =>
                {
                    if (checkbox22.Checked && _fastbootController.IsConnected)
                    {
                        await _fastbootController.UnlockBootloaderAsync();
                    }
                };

                // checkbox21 = 锁定BL
                checkbox21.CheckedChanged += async (s, e) =>
                {
                    if (checkbox21.Checked && _fastbootController.IsConnected)
                    {
                        await _fastbootController.LockBootloaderAsync();
                    }
                };

                // checkbox41 = 切换A槽
                checkbox41.CheckedChanged += async (s, e) =>
                {
                    if (checkbox41.Checked && _fastbootController.IsConnected)
                    {
                        await _fastbootController.SwitchSlotAsync();
                        checkbox41.Checked = false; // 执行后取消勾选
                    }
                };

                // checkbox42 = 分区全选
                checkbox42.CheckedChanged += (s, e) => FastbootSelectAllPartitions(checkbox42.Checked);

                // listView5 双击选择镜像文件
                listView5.DoubleClick += (s, e) => FastbootPartitionDoubleClick();

                // select5 = 分区搜索
                select5.TextChanged += (s, e) => FastbootSearchPartition();

                // 启动设备监控
                _fastbootController.StartDeviceMonitoring();

                AppendLog("Fastboot 模块初始化完成", Color.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"Fastboot 模块初始化失败: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Fastboot 读取设备信息
        /// </summary>
        private async Task FastbootReadInfoAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                // 先连接
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected) return;
            }

            await _fastbootController.ReadPartitionTableAsync();
        }

        /// <summary>
        /// Fastboot 读取分区表
        /// </summary>
        private async Task FastbootReadPartitionTableAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected) return;
            }

            await _fastbootController.ReadPartitionTableAsync();
        }

        /// <summary>
        /// Fastboot 刷写分区
        /// </summary>
        private async Task FastbootFlashPartitionsAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("请先连接 Fastboot 设备", Color.Orange);
                return;
            }

            await _fastbootController.FlashSelectedPartitionsAsync();
        }

        /// <summary>
        /// Fastboot 擦除分区
        /// </summary>
        private async Task FastbootErasePartitionsAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("请先连接 Fastboot 设备", Color.Orange);
                return;
            }

            await _fastbootController.EraseSelectedPartitionsAsync();
        }

        /// <summary>
        /// Fastboot 执行刷机脚本或快捷命令
        /// </summary>
        private async Task FastbootExecuteAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected) return;
            }

            // 如果有加载的刷机任务，执行刷机脚本
            if (_fastbootController.FlashTasks != null && _fastbootController.FlashTasks.Count > 0)
            {
                await _fastbootController.ExecuteFlashScriptAsync();
            }
            else
            {
                // 否则执行快捷命令
                await _fastbootController.ExecuteSelectedCommandAsync();
            }
        }

        /// <summary>
        /// Fastboot 执行快捷命令
        /// </summary>
        private async Task FastbootExecuteCommandAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected) return;
            }

            await _fastbootController.ExecuteSelectedCommandAsync();
        }

        /// <summary>
        /// Fastboot 选择输出路径
        /// </summary>
        private void FastbootSelectOutputPath()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择输出目录";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    input1.Text = fbd.SelectedPath;
                    AppendLog($"输出路径: {fbd.SelectedPath}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot 选择 Payload 或刷机脚本
        /// </summary>
        private void FastbootSelectPayload()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择 Payload 或刷机脚本";
                ofd.Filter = "刷机脚本|*.bat;*.sh|Payload|*.bin;*.zip|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    uiTextBox1.Text = ofd.FileName;
                    AppendLog($"已选择: {Path.GetFileName(ofd.FileName)}", Color.Blue);

                    // 自动解析 bat/sh 脚本
                    string ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
                    if (ext == ".bat" || ext == ".sh" || ext == ".cmd")
                    {
                        FastbootLoadScript(ofd.FileName);
                    }
                }
            }
        }

        /// <summary>
        /// Fastboot 选择刷机目录 (自动扫描脚本)
        /// </summary>
        private void FastbootSelectFlashDirectory()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择包含刷机脚本的目录";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    // 扫描目录中的刷机脚本
                    var scripts = _fastbootController.ScanFlashScripts(fbd.SelectedPath);
                    
                    if (scripts.Count == 0)
                    {
                        AppendLog("目录中未找到刷机脚本 (flash_all.bat 等)", Color.Orange);
                        return;
                    }

                    // 如果只有一个脚本，直接加载
                    if (scripts.Count == 1)
                    {
                        uiTextBox1.Text = scripts[0];
                        FastbootLoadScript(scripts[0]);
                    }
                    else
                    {
                        // 多个脚本，让用户选择
                        var items = scripts.Select(s => 
                            $"{Path.GetFileName(s)} - {LoveAlways.Fastboot.Common.BatScriptParser.GetScriptDescription(s)}").ToArray();
                        
                        // 简单选择：默认使用 flash_all.bat
                        var flashAll = scripts.FirstOrDefault(s => 
                            Path.GetFileName(s).Equals("flash_all.bat", StringComparison.OrdinalIgnoreCase));
                        
                        if (flashAll != null)
                        {
                            uiTextBox1.Text = flashAll;
                            FastbootLoadScript(flashAll);
                        }
                        else
                        {
                            uiTextBox1.Text = scripts[0];
                            FastbootLoadScript(scripts[0]);
                        }

                        AppendLog($"发现 {scripts.Count} 个刷机脚本，已加载: {Path.GetFileName(uiTextBox1.Text)}", Color.Blue);
                    }
                }
            }
        }

        /// <summary>
        /// Fastboot 加载刷机脚本
        /// </summary>
        private void FastbootLoadScript(string scriptPath)
        {
            if (_fastbootController == null) return;

            bool success = _fastbootController.LoadFlashScript(scriptPath);
            
            if (success)
            {
                // 更新输出路径为脚本所在目录
                input1.Text = Path.GetDirectoryName(scriptPath);
            }
        }

        /// <summary>
        /// Fastboot 分区全选/取消全选
        /// </summary>
        private void FastbootSelectAllPartitions(bool selectAll)
        {
            foreach (ListViewItem item in listView5.Items)
            {
                item.Checked = selectAll;
            }
        }

        /// <summary>
        /// Fastboot 分区双击选择镜像
        /// </summary>
        private void FastbootPartitionDoubleClick()
        {
            if (listView5.SelectedItems.Count == 0) return;
            
            var selectedItem = listView5.SelectedItems[0];
            _fastbootController?.SelectImageForPartition(selectedItem);
        }

        /// <summary>
        /// Fastboot 分区搜索
        /// </summary>
        private void FastbootSearchPartition()
        {
            string keyword = select5.Text?.Trim().ToLower() ?? "";
            
            foreach (ListViewItem item in listView5.Items)
            {
                string partName = item.SubItems[0].Text.ToLower();
                // 简单的显示/隐藏筛选
                // 注：ListView 不直接支持隐藏行，可以通过 Tag 标记后重建列表
            }
        }

        #endregion
    }
}