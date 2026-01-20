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
using Microsoft.VisualBasic;
using LoveAlways.Qualcomm.UI;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Fastboot.UI;
using LoveAlways.Fastboot.Common;
using LoveAlways.Qualcomm.Database;

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
        private bool _isOnFastbootTab = false;  // 当前是否在 Fastboot 标签页
        private string _selectedXmlDirectory = "";  // 存储选择的 XML 文件所在目录

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
            
            // 初始化 EDL Loader 选择列表
            InitializeEdlLoaderList();
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
                
                // ========== 快捷操作菜单事件 (设备管理器) ==========
                // 重启系统 (ADB/Fastboot)
                toolStripMenuItem2.Click += async (s, e) => await QuickRebootSystemAsync();
                // 重启到 Fastboot (ADB/Fastboot)
                toolStripMenuItem6.Click += async (s, e) => await QuickRebootBootloaderAsync();
                // 重启到 Fastbootd (ADB/Fastboot)
                toolStripMenuItem7.Click += async (s, e) => await QuickRebootFastbootdAsync();
                // 重启到 Recovery (ADB/Fastboot)
                重启恢复ToolStripMenuItem.Click += async (s, e) => await QuickRebootRecoveryAsync();
                // MI踢EDL (Fastboot only)
                mIToolStripMenuItem.Click += async (s, e) => await QuickMiRebootEdlAsync();
                // 联想或安卓踢EDL (ADB only)
                联想或安卓踢EDLToolStripMenuItem.Click += async (s, e) => await QuickAdbRebootEdlAsync();
                // 擦除谷歌锁 (Fastboot only)
                擦除谷歌锁ToolStripMenuItem.Click += async (s, e) => await QuickEraseFrpAsync();
                // 切换槽位 (Fastboot only)
                切换槽位ToolStripMenuItem.Click += async (s, e) => await QuickSwitchSlotAsync();
                
                // ========== 其他菜单事件 ==========
                // 设备管理器
                设备管理器ToolStripMenuItem.Click += (s, e) => OpenDeviceManager();
                // CMD命令行
                cMD命令行ToolStripMenuItem.Click += (s, e) => OpenCommandPrompt();
                // 安卓驱动
                安卓驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("android");
                // MTK驱动
                mTK驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("mtk");
                // 高通驱动
                高通驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("qualcomm");

                // ========== 停止按钮 ==========
                uiButton1.Click += (s, e) => StopCurrentOperation();

                // ========== 刷新端口 ==========
                // 初始化时刷新端口列表（静默模式）
                _lastEdlCount = _qualcommController.RefreshPorts(silent: true);
                
                // 启动端口自动检测定时器 (每2秒检测一次，只在端口列表变化时才刷新)
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
                // 如果当前在 Fastboot 标签页，不刷新高通端口
                if (_isOnFastbootTab)
                    return;
                    
                // 如果有正在进行的操作，不刷新
                if (_qualcommController != null && _qualcommController.HasPendingOperation)
                    return;

                // 获取当前端口列表用于变化检测
                var ports = LoveAlways.Qualcomm.Common.PortDetector.DetectAllPorts();
                var edlPorts = LoveAlways.Qualcomm.Common.PortDetector.DetectEdlPorts();
                string currentPortList = string.Join(",", ports.ConvertAll(p => p.PortName));
                
                // 只有端口列表变化时才刷新
                if (currentPortList != _lastPortList)
                {
                    bool hadEdl = _lastEdlCount > 0;
                    bool newEdlDetected = edlPorts.Count > 0 && !hadEdl;
                    _lastPortList = currentPortList;
                    
                    // 静默刷新，返回EDL端口数量
                    int edlCount = _qualcommController?.RefreshPorts(silent: true) ?? 0;
                    
                    // 新检测到EDL设备时提示
                    if (newEdlDetected && edlPorts.Count > 0)
                    {
                        AppendLog($"检测到 EDL 设备: {edlPorts[0].PortName} - {edlPorts[0].Description}", Color.LimeGreen);
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
                    // 保存 XML 文件所在目录（用于后续搜索 patch 文件）
                    _selectedXmlDirectory = Path.GetDirectoryName(ofd.FileNames[0]) ?? "";
                    
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
                // 只匹配 rawprogram0.xml, rawprogram1.xml 等数字后缀的文件
                // 排除 _BLANK_GPT, _WIPE_PARTITIONS, _ERASE 等特殊文件
                var siblingFiles = Directory.GetFiles(dir, "rawprogram*.xml")
                    .Where(f => {
                        string fileName = Path.GetFileNameWithoutExtension(f).ToLower();
                        // 只接受 rawprogram + 数字 格式 (如 rawprogram0, rawprogram1, rawprogram_unsparse)
                        // 排除包含 blank, wipe, erase 的文件
                        if (fileName.Contains("blank") || fileName.Contains("wipe") || fileName.Contains("erase"))
                            return false;
                        return true;
                    })
                    .OrderBy(f => {
                        // 按数字排序
                        string name = Path.GetFileNameWithoutExtension(f);
                        var numStr = new string(name.Where(char.IsDigit).ToArray());
                        int num;
                        return int.TryParse(numStr, out num) ? num : 999;
                    })
                    .ToArray();
                
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
                
                // 预先检查 patch 文件
                if (!string.IsNullOrEmpty(_selectedXmlDirectory) && Directory.Exists(_selectedXmlDirectory))
                {
                    var patchFiles = Directory.GetFiles(_selectedXmlDirectory, "patch*.xml", SearchOption.TopDirectoryOnly)
                        .Where(f => {
                            string fn = Path.GetFileName(f).ToLower();
                            return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                        })
                        .OrderBy(f => {
                            string name = Path.GetFileNameWithoutExtension(f);
                            var numStr = new string(name.Where(char.IsDigit).ToArray());
                            int num;
                            return int.TryParse(numStr, out num) ? num : 999;
                        })
                        .ToList();
                    
                    if (patchFiles.Count > 0)
                    {
                        AppendLog($"检测到 {patchFiles.Count} 个 Patch 文件: {string.Join(", ", patchFiles.Select(f => Path.GetFileName(f)))}", Color.Blue);
                    }
                    else
                    {
                        AppendLog("未检测到 Patch 文件", Color.Gray);
                    }
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

                // 敏感分区标记为灰色
                if (Qualcomm.Common.RawprogramParser.IsSensitivePartition(task.Label))
                    item.ForeColor = Color.Gray;
                // 文件不存在的分区不标红，只是不自动勾选（已在上面处理）

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

            string selectedLoader = select3.Text;
            bool isVipLoader = selectedLoader.StartsWith("[VIP]");
            bool isEdlLoader = selectedLoader.StartsWith("[") && !isVipLoader && !selectedLoader.StartsWith("───");
            bool skipSahara = checkbox12.Checked;

            // VIP 内嵌模式 (OPLUS 签名)
            if (isVipLoader)
            {
                string platform = ExtractPlatformFromVipSelection(selectedLoader);
                
                if (!ChimeraSignDatabase.TryGet(platform, out var signData))
                {
                    AppendLog($"未找到平台 {platform} 的签名数据", Color.Red);
                    return false;
                }

                // 检查资源包是否存在
                if (!ChimeraSignDatabase.IsLoaderPackAvailable())
                {
                    AppendLog("错误: 找不到 firehose.pak 资源包", Color.Red);
                    AppendLog("请将资源包放到程序目录下", Color.Orange);
                    return false;
                }

                // 从资源包加载 Loader
                byte[] loaderData = ChimeraSignDatabase.LoadLoader(platform);
                if (loaderData == null)
                {
                    AppendLog($"无法从资源包加载 {platform} 的 Loader", Color.Red);
                    return false;
                }

                AppendLog($"[VIP] {signData.Name} - Loader:{loaderData.Length/1024}KB Digest:{signData.DigestSize/1024}KB", Color.Blue);
                
                // 使用 VIP 连接 (Loader 从资源包, Digest+Sign 内嵌)
                return await _qualcommController.ConnectWithVipDataAsync(
                    _storageType,
                    platform,
                    loaderData,
                    signData.Digest,
                    signData.Signature
                );
            }
            
            // EDL Loader 模式 (通用/无签名)
            if (isEdlLoader)
            {
                string edlId = ExtractEdlLoaderIdFromSelection(selectedLoader);
                
                // 检查资源包是否存在
                if (!EdlLoaderDatabase.IsPakAvailable())
                {
                    AppendLog("错误: 找不到 edl_loaders.pak 资源包", Color.Red);
                    AppendLog("请将资源包放到程序目录下", Color.Orange);
                    return false;
                }
                
                // 从资源包加载 Loader
                byte[] loaderData = EdlLoaderDatabase.LoadLoader(edlId);
                if (loaderData == null)
                {
                    AppendLog($"无法从资源包加载 {edlId}", Color.Red);
                    return false;
                }
                
                // 获取 Loader 信息用于显示
                string displayName = edlId;
                if (EdlLoaderDatabase.Database.TryGetValue(edlId, out var loaderInfo))
                {
                    displayName = loaderInfo.Name;
                }
                
                // 获取认证模式
                string edlAuthMode = "none";
                if (EdlLoaderDatabase.Database.TryGetValue(edlId, out var edlInfo))
                {
                    edlAuthMode = edlInfo.AuthMode ?? "none";
                }
                
                string authInfo = edlAuthMode != "none" ? $" [认证: {edlAuthMode}]" : "";
                AppendLog($"[EDL] {displayName} - {loaderData.Length/1024}KB{authInfo}", Color.Cyan);
                
                // 使用 EDL 连接 (传递认证模式)
                return await _qualcommController.ConnectWithLoaderDataAsync(
                    _storageType,
                    loaderData,
                    edlId,
                    edlAuthMode
                );
            }

            // 普通模式
            // input8 = 引导文件路径
            string programmerPath = input8.Text?.Trim() ?? "";

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
                    
                    // 优先使用存储的 XML 目录，如果为空则尝试从 input6.Text 解析
                    string xmlDir = _selectedXmlDirectory;
                    if (string.IsNullOrEmpty(xmlDir))
                    {
                        try
                        {
                            string xmlPath = input6.Text?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(xmlPath) && File.Exists(xmlPath))
                            {
                                xmlDir = Path.GetDirectoryName(xmlPath) ?? "";
                            }
                        }
                        catch (ArgumentException)
                        {
                            // 路径包含无效字符，忽略
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(xmlDir) && Directory.Exists(xmlDir))
                    {
                        AppendLog($"正在目录中搜索 Patch 文件: {xmlDir}", Color.Gray);
                        
                        // 1. 先搜索当前目录（同级目录）
                        try
                        {
                            var sameDir = Directory.GetFiles(xmlDir, "patch*.xml", SearchOption.TopDirectoryOnly)
                                .Where(f => {
                                    string fn = Path.GetFileName(f).ToLower();
                                    return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                })
                                .ToList();
                            patchFiles.AddRange(sameDir);
                        }
                        catch { }
                        
                        // 2. 如果当前目录没找到，搜索子目录（如 images 文件夹）
                        if (patchFiles.Count == 0)
                        {
                            try
                            {
                                var subDirs = Directory.GetFiles(xmlDir, "patch*.xml", SearchOption.AllDirectories)
                                    .Where(f => {
                                        string fn = Path.GetFileName(f).ToLower();
                                        return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                    })
                                    .ToList();
                                patchFiles.AddRange(subDirs);
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"搜索子目录 Patch 文件时出错: {ex.Message}", Color.Orange);
                            }
                        }

                        // 3. 如果都没找到，尝试在父目录搜索
                        if (patchFiles.Count == 0)
                        {
                            try
                            {
                                string parentDir = Path.GetDirectoryName(xmlDir);
                                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                                {
                                    AppendLog($"当前目录未找到，搜索父目录: {parentDir}", Color.Gray);
                                    var parentPatches = Directory.GetFiles(parentDir, "patch*.xml", SearchOption.AllDirectories)
                                        .Where(f => {
                                            string fn = Path.GetFileName(f).ToLower();
                                            return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                        })
                                        .ToList();
                                    patchFiles.AddRange(parentPatches);
                                }
                            }
                            catch { }
                        }
                        
                        // 排序 patch 文件
                        patchFiles = patchFiles.Distinct().OrderBy(f => {
                            string name = Path.GetFileNameWithoutExtension(f);
                            var numStr = new string(name.Where(char.IsDigit).ToArray());
                            int num;
                            return int.TryParse(numStr, out num) ? num : 999;
                        }).ToList();

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
                    else
                    {
                        AppendLog($"无法获取 XML 目录路径 (xmlDir={xmlDir ?? "null"})", Color.Orange);
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
            bool isVipLoader = selectedItem.StartsWith("[VIP]");
            bool isEdlLoader = selectedItem.StartsWith("[") && !isVipLoader && !selectedItem.StartsWith("───");
            bool isSeparator = selectedItem.StartsWith("───");
            
            // 跳过分隔符选择
            if (isSeparator)
            {
                select3.Text = "自动识别或自选引导";
                return;
            }
            
            // 禁用或启用输入字段
            input9.Enabled = isAutoOrCustom;
            input8.Enabled = isAutoOrCustom;
            input7.Enabled = isAutoOrCustom;
            
            // 处理 VIP Loader
            if (isVipLoader)
            {
                // 提取平台名称 (例如: "[VIP] SM8550 - Snapdragon 8Gen2/8+Gen2" -> "SM8550")
                string platform = ExtractPlatformFromVipSelection(selectedItem);
                
                // 只在首次禁用时保存原始文本
                if (string.IsNullOrEmpty(input8OriginalText))
                {
                    input8OriginalText = input8.Text;
                }
                
                // 显示 VIP 模式信息
                input8.Text = $"[VIP] {platform} (资源包)";
                input9.Text = $"[VIP] Digest (内嵌)";
                input7.Text = $"[VIP] Signature (内嵌)";
                
                // 显示平台信息 (简洁模式)
                if (ChimeraSignDatabase.TryGet(platform, out var signData))
                {
                    bool pakAvailable = ChimeraSignDatabase.IsLoaderPackAvailable();
                    if (!pakAvailable)
                    {
                        AppendLog($"[VIP] {signData.Name} - 资源包未找到!", Color.Orange);
                    }
                }
                
                // 自动启用 VIP 验证
                checkbox19.Checked = true;
                checkbox17.Checked = false;  // 取消 oneplus
                _authMode = "vip";
            }
            // 处理 EDL Loader
            else if (isEdlLoader)
            {
                string edlId = ExtractEdlLoaderIdFromSelection(selectedItem);
                
                if (string.IsNullOrEmpty(input8OriginalText))
                {
                    input8OriginalText = input8.Text;
                }
                
                // 显示 EDL 模式信息
                input8.Text = $"[EDL] {edlId} (资源包)";
                input9.Text = "";
                input7.Text = "";
                
                // 根据 AuthMode 决定认证模式
                string authMode = "none";
                if (EdlLoaderDatabase.Database.TryGetValue(edlId, out var loaderInfo))
                {
                    authMode = loaderInfo.AuthMode ?? "none";
                }
                
                if (authMode == "oneplus")
                {
                    // OnePlus Loader: 自动启用 oneplus 认证
                    checkbox17.Checked = true;   // oldoneplus
                    checkbox19.Checked = false;  // oplus
                    _authMode = "oneplus";
                }
                else
                {
                    // OPPO/Realme 或其他: 不需要验证
                    checkbox17.Checked = false;
                    checkbox19.Checked = false;
                    _authMode = "none";
                }
            }
            else if (!isAutoOrCustom)
            {
                // 其他非自动模式
                if (string.IsNullOrEmpty(input8OriginalText))
                {
                    input8OriginalText = input8.Text;
                }
                input8.Text = "当前模式禁止选择文件";
            }
            else
            {
                // 强制清空以显示占位符
                input8.Text = "";
                input9.Text = "";
                input7.Text = "";
                // 重置原始文本存储
                input8OriginalText = "";
            }
        }
        
        /// <summary>
        /// 从 EDL 选择项中提取 Loader ID
        /// </summary>
        private string ExtractEdlLoaderIdFromSelection(string selection)
        {
            // "[Huawei] 888 (通用)" -> "Huawei_888"
            // "[Meizu] Meizu21Pro" -> "Meizu_Meizu21Pro"
            if (string.IsNullOrEmpty(selection)) return "";
            
            // 提取品牌和名称
            int bracketEnd = selection.IndexOf(']');
            if (bracketEnd < 0) return "";
            
            string brand = selection.Substring(1, bracketEnd - 1);
            string rest = selection.Substring(bracketEnd + 1).Trim();
            
            // 处理通用 loader
            if (rest.EndsWith("(通用)"))
            {
                string chip = rest.Replace("(通用)", "").Trim();
                return $"{brand}_{chip}";
            }
            
            // 处理专用 loader
            // 从 rest 中提取型号名
            string model = rest.Replace($"{brand} ", "").Trim();
            // 移除芯片信息 (括号部分)
            int parenIndex = model.IndexOf('(');
            if (parenIndex > 0)
            {
                model = model.Substring(0, parenIndex).Trim();
            }
            
            return $"{brand}_{model}";
        }

        /// <summary>
        /// 从 VIP 选择项中提取平台名称
        /// </summary>
        private string ExtractPlatformFromVipSelection(string selection)
        {
            // "[VIP] SM8550 - Snapdragon 8Gen2/8+Gen2" -> "SM8550"
            if (string.IsNullOrEmpty(selection)) return "";
            
            // 移除 "[VIP] " 前缀
            string trimmed = selection.Replace("[VIP] ", "");
            
            // 取 " - " 之前的部分
            int dashIndex = trimmed.IndexOf(" - ");
            if (dashIndex > 0)
            {
                return trimmed.Substring(0, dashIndex).Trim();
            }
            
            return trimmed.Trim();
        }

        #region EDL Loader 初始化
        
        // 缓存 EDL Loader 列表项
        private List<string> _edlLoaderItems = null;
        
        /// <summary>
        /// 初始化 EDL Loader 选择列表 (按品牌分组) - 异步优化
        /// </summary>
        private void InitializeEdlLoaderList()
        {
            // 异步加载，避免阻塞 UI
            Task.Run(() => BuildEdlLoaderItems()).ContinueWith(t =>
            {
                if (t.Result != null && t.Result.Count > 0)
                {
                    // 回到 UI 线程批量添加
                    this.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // 批量添加所有项目
                            select3.Items.AddRange(t.Result.ToArray());
                        }
                        catch { }
                    }));
                }
            });
        }
        
        /// <summary>
        /// 构建 EDL Loader 列表项 (后台线程)
        /// </summary>
        private List<string> BuildEdlLoaderItems()
        {
            var items = new List<string>(300);
            
            try
            {
                // 检查 EDL PAK 是否可用
                if (!EdlLoaderDatabase.IsPakAvailable())
                    return items;
                
                // 获取所有品牌
                var brands = EdlLoaderDatabase.GetBrands();
                if (brands.Length == 0)
                    return items;
                
                // 按品牌分组构建
                foreach (var brand in brands)
                {
                    var loaders = EdlLoaderDatabase.GetByBrand(brand);
                    if (loaders.Length == 0) continue;
                    
                    // 添加品牌分隔符
                    string brandName = GetBrandDisplayName(brand);
                    items.Add($"─── {brandName} ({loaders.Length}) ───");
                    
                    // 先添加通用 loader
                    foreach (var loader in loaders)
                    {
                        if (loader.IsCommon)
                        {
                            string chip = string.IsNullOrEmpty(loader.Chip) ? "" : $" {loader.Chip}";
                            items.Add($"[{brand}]{chip} (通用)");
                        }
                    }
                    
                    // 再添加专用 loader
                    foreach (var loader in loaders)
                    {
                        if (!loader.IsCommon)
                        {
                            string shortName = loader.Name.Replace($"{brand} ", "");
                            items.Add($"[{brand}] {shortName}");
                        }
                    }
                }
                
                _edlLoaderItems = items;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EDL Loader 构建失败: {ex.Message}");
            }
            
            return items;
        }
        
        /// <summary>
        /// 获取品牌中文显示名
        /// </summary>
        private string GetBrandDisplayName(string brand)
        {
            switch (brand.ToLower())
            {
                case "huawei": return "华为/荣耀";
                case "zte": return "中兴/努比亚/红魔";
                case "xiaomi": return "小米/Redmi";
                case "blackshark": return "黑鲨";
                case "vivo": return "vivo/iQOO";
                case "meizu": return "魅族";
                case "lenovo": return "联想/摩托罗拉";
                case "samsung": return "三星";
                case "nothing": return "Nothing";
                case "rog": return "华硕ROG";
                case "lg": return "LG";
                case "smartisan": return "锤子";
                case "xtc": return "小天才";
                case "360": return "360";
                case "bbk": return "BBK";
                case "royole": return "柔宇";
                case "oplus": return "OPPO/OnePlus/Realme";
                default: return brand;
            }
        }
        
        #endregion

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
                // 注意: 设备信息标签(uiGroupBox3内)与高通模块共用，通过标签页切换来更新
                _fastbootController.BindControls(
                    deviceComboBox: uiComboBox1,          // 使用全局端口选择下拉框 (共用)
                    partitionListView: listView5,         // 分区列表
                    progressBar: uiProcessBar1,           // 总进度条 (共用)
                    subProgressBar: uiProcessBar2,        // 子进度条 (共用)
                    commandComboBox: uiComboBox2,         // 快捷命令下拉框 (device, unlock 等)
                    payloadTextBox: uiTextBox1,           // Payload 路径
                    outputPathTextBox: input1,            // 输出路径
                    // 设备信息标签 (uiGroupBox3 - 共用)
                    brandLabel: uiLabel9,                 // 品牌
                    chipLabel: uiLabel11,                 // 芯片
                    modelLabel: uiLabel3,                 // 型号
                    serialLabel: uiLabel10,               // 序列号
                    storageLabel: uiLabel13,              // 存储
                    unlockLabel: uiLabel14,               // 解锁状态
                    slotLabel: uiLabel12,                 // 槽位 (复用OTA版本标签)
                    // 时间/速度/操作标签 (共用)
                    timeLabel: uiLabel6,                  // 时间
                    speedLabel: uiLabel7,                 // 速度
                    operationLabel: uiLabel8,             // 当前操作
                    deviceCountLabel: uiLabel4,           // 设备数量 (复用)
                    // Checkbox 控件
                    autoRebootCheckbox: checkbox44,       // 自动重启
                    switchSlotCheckbox: checkbox41,       // 切换A槽
                    eraseGoogleLockCheckbox: checkbox43,  // 擦除谷歌锁
                    keepDataCheckbox: checkbox50,         // 保留数据
                    fbdFlashCheckbox: checkbox45,         // FBD刷写
                    unlockBlCheckbox: checkbox22,         // 解锁BL
                    lockBlCheckbox: checkbox21            // 锁定BL
                );

                // ========== tabPage3 Fastboot 页面按钮事件 ==========
                
                // uiButton11 = 解析Payload (本地文件或云端URL)
                uiButton11.Click += (s, e) => FastbootOpenPayloadDialog();

                // uiButton18 = 读取分区表 (同时读取设备信息)
                uiButton18.Click += async (s, e) => await FastbootReadPartitionTableWithInfoAsync();

                // uiButton19 = 提取镜像 (支持从 Payload 提取，自定义或全部)
                uiButton19.Click += async (s, e) => await FastbootExtractPartitionsWithOptionsAsync();

                // uiButton20 = 写入分区
                uiButton20.Click += async (s, e) => await FastbootFlashPartitionsAsync();

                // uiButton21 = 擦除分区
                uiButton21.Click += async (s, e) => await FastbootErasePartitionsAsync();

                // uiButton22 = 修复FBD (后续实现)
                uiButton22.Click += (s, e) => AppendLog("FBD 修复功能开发中...", Color.Orange);

                // uiButton10 = 执行 (执行刷机脚本或快捷命令)
                uiButton10.Click += async (s, e) => await FastbootExecuteAsync();

                // button8 = 浏览 (选择刷机脚本)
                button8.Click += (s, e) => FastbootSelectScript();

                // button9 = 浏览 (选择 Payload/刷机脚本)
                button9.Click += (s, e) => FastbootSelectPayload();

                // uiTextBox1 = Payload/URL 输入框，支持回车键触发解析
                uiTextBox1.Watermark = "请选择本地Payload或输入云端链接";
                uiTextBox1.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        FastbootParsePayloadInput(uiTextBox1.Text);
                    }
                };

                // 修改按钮文字
                uiButton11.Text = "云端解析";
                uiButton18.Text = "读取分区表";
                uiButton19.Text = "提取分区";

                // checkbox22 = 解锁BL (手动操作时执行，脚本执行时作为标志)
                // checkbox21 = 锁定BL (手动操作时执行，脚本执行时作为标志)
                // 注意：不再自动执行，而是在刷机完成后根据选项执行

                // checkbox41 = 切换A槽 (刷写完成后执行)
                // checkbox43 = 擦除谷歌锁 (刷写完成后执行)
                // 这些复选框只作为标记，不立即执行操作

                // checkbox42 = 分区全选
                checkbox42.CheckedChanged += (s, e) => FastbootSelectAllPartitions(checkbox42.Checked);

                // listView5 双击选择镜像文件
                listView5.DoubleClick += (s, e) => FastbootPartitionDoubleClick();

                // select5 = 分区搜索
                select5.TextChanged += (s, e) => FastbootSearchPartition();
                select5.SelectedIndexChanged += (s, e) => { _fbIsSelectingFromDropdown = true; };

                // 注意：不在初始化时启动 Fastboot 设备监控
                // 只有当用户切换到 Fastboot 标签页时才启动监控
                // 避免覆盖高通端口列表
                // _fastbootController.StartDeviceMonitoring();

                // 绑定标签页切换事件 - 更新右侧设备信息显示
                tabs1.SelectedIndexChanged += OnTabPageChanged;

                AppendLog("Fastboot 模块初始化完成", Color.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"Fastboot 模块初始化失败: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// 标签页切换事件 - 切换设备信息显示和设备列表
        /// </summary>
        private void OnTabPageChanged(object sender, EventArgs e)
        {
            try
            {
                // 获取当前选中的标签页
                int selectedIndex = tabs1.SelectedIndex;
                var selectedTab = tabs1.Pages[selectedIndex];

                // tabPage3 是引导模式 (Fastboot)
                if (selectedTab == tabPage3)
                {
                    // 切换到 Fastboot 标签页
                    _isOnFastbootTab = true;
                    
                    // 停止高通端口刷新定时器
                    _portRefreshTimer?.Stop();
                    
                    // 更新 Fastboot 设备信息
                    if (_fastbootController != null)
                    {
                        // 启动 Fastboot 设备监控
                        _fastbootController.StartDeviceMonitoring();
                        _fastbootController.UpdateDeviceInfoLabels();
                        
                        // 更新设备计数标签
                        int deviceCount = _fastbootController.DeviceCount;
                        if (deviceCount == 0)
                            uiLabel4.Text = "FB设备：0";
                        else if (deviceCount == 1)
                            uiLabel4.Text = $"FB设备：已连接";
                        else
                            uiLabel4.Text = $"FB设备：{deviceCount}个";
                    }
                }
                // tabPage2 是高通平台 (EDL)
                else if (selectedTab == tabPage2)
                {
                    // 切换到高通标签页
                    _isOnFastbootTab = false;
                    
                    // 停止 Fastboot 设备监控
                    _fastbootController?.StopDeviceMonitoring();
                    
                    // 启动高通端口刷新定时器
                    _portRefreshTimer?.Start();
                    
                    // 刷新高通端口列表到下拉框
                    _qualcommController?.RefreshPorts(silent: true);
                    
                    // 恢复高通设备信息
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        // 高通控制器会自动更新，这里不需要额外操作
                    }
                    else
                    {
                        // 重置为等待连接状态
                        uiLabel9.Text = "品牌：等待连接";
                        uiLabel11.Text = "芯片：等待连接";
                        uiLabel3.Text = "型号：等待连接";
                        uiLabel10.Text = "序列号：等待连接";
                        uiLabel13.Text = "存储：等待连接";
                        uiLabel14.Text = "型号：等待连接";
                        uiLabel12.Text = "OTA：等待连接";
                    }
                }
                else
                {
                    // 其他标签页
                    _isOnFastbootTab = false;
                    // 停止 Fastboot 设备监控
                    _fastbootController?.StopDeviceMonitoring();
                }
            }
            catch { }
        }

        /// <summary>
        /// Fastboot 云端链接解析
        /// </summary>
        private void FastbootOpenPayloadDialog()
        {
            // 如果文本框中已有内容，直接解析
            if (!string.IsNullOrWhiteSpace(uiTextBox1.Text))
            {
                FastbootParsePayloadInput(uiTextBox1.Text.Trim());
                return;
            }

            // 文本框为空时，弹出输入框
            string url = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入 OTA 下载链接:\n\n支持 OPPO/OnePlus/Realme 官方链接\n或直接的 ZIP/Payload 链接",
                "云端链接解析",
                "");

            if (!string.IsNullOrWhiteSpace(url))
            {
                uiTextBox1.Text = url.Trim();
                FastbootParsePayloadInput(url.Trim());
            }
        }

        /// <summary>
        /// Fastboot 读取分区表 (同时读取设备信息)
        /// </summary>
        private async Task FastbootReadPartitionTableWithInfoAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("正在连接 Fastboot 设备...", Color.Blue);
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected)
                {
                    AppendLog("连接失败，请检查设备是否处于 Fastboot 模式", Color.Red);
                    return;
                }
            }

            // 读取分区表和设备信息
            await _fastbootController.ReadPartitionTableAsync();
        }

        /// <summary>
        /// Fastboot 提取分区 (提取已勾选的分区)
        /// </summary>
        private async Task FastbootExtractPartitionsWithOptionsAsync()
        {
            if (_fastbootController == null) return;

            // 检查是否已加载 Payload (本地或云端)
            bool hasLocalPayload = _fastbootController.PayloadSummary != null;
            bool hasRemotePayload = _fastbootController.IsRemotePayloadLoaded;

            if (!hasLocalPayload && !hasRemotePayload)
            {
                AppendLog("请先解析 Payload (本地文件或云端链接)", Color.Orange);
                return;
            }

            // 让用户选择保存目录
            string outputDir;
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择分区提取保存目录";
                // 如果之前有选过目录，作为默认路径
                if (!string.IsNullOrEmpty(input1.Text) && Directory.Exists(input1.Text))
                {
                    fbd.SelectedPath = input1.Text;
                }
                
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    outputDir = fbd.SelectedPath;
                    input1.Text = outputDir;
                }
                else
                {
                    return;
                }
            }

            // 根据加载的类型选择提取方法
            if (hasRemotePayload)
            {
                await _fastbootController.ExtractSelectedRemotePartitionsAsync(outputDir);
            }
            else
            {
                await _fastbootController.ExtractSelectedPayloadPartitionsAsync(outputDir);
            }
        }

        /// <summary>
        /// Fastboot 读取设备信息 (保留兼容)
        /// </summary>
        private async Task FastbootReadInfoAsync()
        {
            await FastbootReadPartitionTableWithInfoAsync();
        }

        /// <summary>
        /// Fastboot 读取分区表 (保留兼容)
        /// </summary>
        private async Task FastbootReadPartitionTableAsync()
        {
            await FastbootReadPartitionTableWithInfoAsync();
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

            // 检查是否有 Payload 加载 (本地或云端)
            bool hasLocalPayload = _fastbootController.PayloadSummary != null;
            bool hasRemotePayload = _fastbootController.IsRemotePayloadLoaded;

            if (hasRemotePayload)
            {
                // 使用云端 Payload 刷写 (边下载边刷写)
                AppendLog("使用云端 Payload 刷写模式", Color.Blue);
                await _fastbootController.FlashFromRemotePayloadAsync();
            }
            else if (hasLocalPayload)
            {
                // 使用本地 Payload 刷写
                AppendLog("使用本地 Payload 刷写模式", Color.Blue);
                await _fastbootController.FlashFromPayloadAsync();
            }
            else
            {
                // 普通刷写 (需要选择镜像文件)
                await _fastbootController.FlashSelectedPartitionsAsync();
            }
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

            // 优先级 1: 如果选中了快捷命令，直接执行命令
            if (_fastbootController.HasSelectedCommand())
            {
                await _fastbootController.ExecuteSelectedCommandAsync();
            }
            // 优先级 2: 如果有加载的 Payload，执行 Payload 刷写
            else if (_fastbootController.IsPayloadLoaded)
            {
                await _fastbootController.FlashFromPayloadAsync();
            }
            // 优先级 3: 如果有加载的刷机任务，执行刷机脚本
            else if (_fastbootController.FlashTasks != null && _fastbootController.FlashTasks.Count > 0)
            {
                // 读取用户选项
                bool keepData = checkbox50.Checked;   // 保留数据
                bool lockBl = checkbox21.Checked;     // 锁定BL

                await _fastbootController.ExecuteFlashScriptAsync(keepData, lockBl);
            }
            // 优先级 4: 如果勾选了分区且有镜像文件，直接写入分区
            else if (_fastbootController.HasSelectedPartitionsWithFiles())
            {
                await _fastbootController.FlashSelectedPartitionsAsync();
            }
            else
            {
                // 什么都没选，提示用户
                AppendLog("请选择快捷命令、加载刷机脚本或勾选分区后再执行", Color.Orange);
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
        /// Fastboot 提取分区 (从 Payload 提取，支持本地和云端)
        /// </summary>
        private async Task FastbootExtractPartitionsAsync()
        {
            // 直接调用带选项的方法
            await FastbootExtractPartitionsWithOptionsAsync();
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
                ofd.Filter = "Payload|*.bin;*.zip|刷机脚本|*.bat;*.sh;*.cmd|所有文件|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    uiTextBox1.Text = ofd.FileName;
                    FastbootParsePayloadInput(ofd.FileName);
                }
            }
        }

        /// <summary>
        /// 解析 Payload 输入 (支持本地文件和 URL)
        /// </summary>
        private void FastbootParsePayloadInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            input = input.Trim();

            // 判断是 URL 还是本地文件
            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // URL - 云端解析
                AppendLog($"检测到云端 URL，开始解析...", Color.Blue);
                _ = FastbootLoadPayloadFromUrlAsync(input);
            }
            else if (File.Exists(input))
            {
                // 本地文件
                AppendLog($"已选择: {Path.GetFileName(input)}", Color.Blue);

                string ext = Path.GetExtension(input).ToLowerInvariant();
                string fileName = Path.GetFileName(input).ToLowerInvariant();

                if (ext == ".bat" || ext == ".sh" || ext == ".cmd")
                {
                    // 刷机脚本
                    FastbootLoadScript(input);
                }
                else if (ext == ".bin" || ext == ".zip" || fileName == "payload.bin")
                {
                    // Payload 文件
                    _ = FastbootLoadPayloadAsync(input);
                }
            }
            else
            {
                AppendLog($"无效的输入: 文件不存在或 URL 格式错误", Color.Red);
            }
        }

        /// <summary>
        /// Fastboot 加载 Payload 文件
        /// </summary>
        private async Task FastbootLoadPayloadAsync(string payloadPath)
        {
            if (_fastbootController == null) return;

            bool success = await _fastbootController.LoadPayloadAsync(payloadPath);
            
            if (success)
            {
                // 更新输出路径为 Payload 所在目录
                input1.Text = Path.GetDirectoryName(payloadPath);
                
                // 显示 Payload 摘要信息
                var summary = _fastbootController.PayloadSummary;
                if (summary != null)
                {
                    AppendLog($"[Payload] 分区数: {summary.PartitionCount}, 总大小: {summary.TotalSizeFormatted}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot 从 URL 加载云端 Payload
        /// </summary>
        private async Task FastbootLoadPayloadFromUrlAsync(string url)
        {
            if (_fastbootController == null) return;

            bool success = await _fastbootController.LoadPayloadFromUrlAsync(url);
            
            if (success)
            {
                // 显示远程 Payload 摘要信息
                var summary = _fastbootController.RemotePayloadSummary;
                if (summary != null)
                {
                    AppendLog($"[云端Payload] 分区数: {summary.PartitionCount}, 文件大小: {summary.TotalSizeFormatted}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot 选择刷机脚本文件
        /// </summary>
        private void FastbootSelectScript()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择刷机脚本 (flash_all.bat)";
                ofd.Filter = "刷机脚本|*.bat;*.sh;*.cmd|所有文件|*.*";
                
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input1.Text = ofd.FileName;
                    AppendLog($"已选择脚本: {Path.GetFileName(ofd.FileName)}", Color.Blue);
                    
                    // 加载脚本
                    FastbootLoadScript(ofd.FileName);
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

                // 根据脚本类型自动勾选对应选项
                AutoSelectOptionsFromScript(scriptPath);
            }
        }

        /// <summary>
        /// 根据脚本类型自动勾选 UI 选项
        /// </summary>
        private void AutoSelectOptionsFromScript(string scriptPath)
        {
            string fileName = Path.GetFileName(scriptPath).ToLowerInvariant();

            // 重置所有相关选项
            checkbox50.Checked = false;  // 保留数据
            checkbox21.Checked = false;  // 锁定BL

            // 根据脚本名称判断类型
            if (fileName.Contains("except_storage") || fileName.Contains("except-storage") || 
                fileName.Contains("keep_data") || fileName.Contains("keepdata"))
            {
                // 保留数据刷机脚本
                checkbox50.Checked = true;
                AppendLog("检测到保留数据脚本，已勾选「保留数据」", Color.Blue);
            }
            else if (fileName.Contains("_lock") || fileName.Contains("-lock") || 
                     fileName.EndsWith("lock.bat") || fileName.EndsWith("lock.sh"))
            {
                // 锁定BL刷机脚本
                checkbox21.Checked = true;
                AppendLog("检测到锁定BL脚本，已勾选「锁定BL」", Color.Blue);
            }
            else
            {
                // 普通刷机脚本 (flash_all.bat)
                AppendLog("普通刷机脚本，将清除所有数据", Color.Orange);
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

        // Fastboot 分区搜索相关变量
        private string _fbLastSearchKeyword = "";
        private List<ListViewItem> _fbSearchMatches = new List<ListViewItem>();
        private int _fbCurrentMatchIndex = 0;
        private bool _fbIsSelectingFromDropdown = false;

        /// <summary>
        /// Fastboot 分区搜索
        /// </summary>
        private void FastbootSearchPartition()
        {
            // 如果是从下拉选择触发的，直接定位
            if (_fbIsSelectingFromDropdown)
            {
                _fbIsSelectingFromDropdown = false;
                string selectedName = select5.Text?.Trim()?.ToLower();
                if (!string.IsNullOrEmpty(selectedName))
                {
                    FastbootLocatePartitionByName(selectedName);
                }
                return;
            }

            string keyword = select5.Text?.Trim()?.ToLower() ?? "";
            
            // 如果搜索框为空，重置所有高亮
            if (string.IsNullOrEmpty(keyword))
            {
                FastbootResetPartitionHighlights();
                _fbLastSearchKeyword = "";
                _fbSearchMatches.Clear();
                _fbCurrentMatchIndex = 0;
                return;
            }
            
            // 如果关键词相同，跳转到下一个匹配项
            if (keyword == _fbLastSearchKeyword && _fbSearchMatches.Count > 1)
            {
                FastbootJumpToNextMatch();
                return;
            }
            
            _fbLastSearchKeyword = keyword;
            _fbSearchMatches.Clear();
            _fbCurrentMatchIndex = 0;
            
            // 收集匹配的分区名称用于下拉建议
            var suggestions = new List<string>();
            
            listView5.BeginUpdate();
            
            foreach (ListViewItem item in listView5.Items)
            {
                string partName = item.SubItems[0].Text.ToLower();
                
                if (partName.Contains(keyword))
                {
                    // 高亮匹配的项
                    item.BackColor = Color.LightYellow;
                    _fbSearchMatches.Add(item);
                    
                    // 添加到建议列表
                    if (!suggestions.Contains(item.SubItems[0].Text))
                    {
                        suggestions.Add(item.SubItems[0].Text);
                    }
                }
                else
                {
                    item.BackColor = Color.Transparent;
                }
            }
            
            listView5.EndUpdate();
            
            // 更新下拉建议列表
            FastbootUpdateSearchSuggestions(suggestions);
            
            // 滚动到第一个匹配项
            if (_fbSearchMatches.Count > 0)
            {
                _fbSearchMatches[0].Selected = true;
                _fbSearchMatches[0].EnsureVisible();
                _fbCurrentMatchIndex = 0;
            }
        }

        private void FastbootJumpToNextMatch()
        {
            if (_fbSearchMatches.Count == 0) return;
            
            // 取消当前选中项的选中状态
            if (_fbCurrentMatchIndex < _fbSearchMatches.Count)
            {
                _fbSearchMatches[_fbCurrentMatchIndex].Selected = false;
            }
            
            // 移动到下一个
            _fbCurrentMatchIndex = (_fbCurrentMatchIndex + 1) % _fbSearchMatches.Count;
            
            // 选中并滚动到新的匹配项
            _fbSearchMatches[_fbCurrentMatchIndex].Selected = true;
            _fbSearchMatches[_fbCurrentMatchIndex].EnsureVisible();
        }

        private void FastbootResetPartitionHighlights()
        {
            listView5.BeginUpdate();
            foreach (ListViewItem item in listView5.Items)
            {
                item.BackColor = Color.Transparent;
            }
            listView5.EndUpdate();
        }

        private void FastbootUpdateSearchSuggestions(List<string> suggestions)
        {
            string currentText = select5.Text;
            
            select5.Items.Clear();
            foreach (var name in suggestions)
            {
                select5.Items.Add(name);
            }
            
            select5.Text = currentText;
        }

        private void FastbootLocatePartitionByName(string partitionName)
        {
            FastbootResetPartitionHighlights();
            
            foreach (ListViewItem item in listView5.Items)
            {
                if (item.SubItems[0].Text.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                {
                    item.BackColor = Color.LightYellow;
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        #endregion

        #region 快捷操作 (设备管理器)
        
        /// <summary>
        /// 快捷重启系统 (优先 Fastboot，备选 ADB)
        /// </summary>
        private async Task QuickRebootSystemAsync()
        {
            AppendLog("执行: 重启系统...", Color.Cyan);
            
            // 优先尝试 Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootAsync();
                if (ok)
                {
                    AppendLog("Fastboot: 重启成功", Color.Green);
                    return;
                }
            }
            
            // 备选 ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootAsync();
            if (result.Success)
                AppendLog("ADB: 重启成功", Color.Green);
            else
                AppendLog($"重启失败: {result.Error}", Color.Red);
        }
        
        /// <summary>
        /// 快捷重启到 Bootloader (优先 Fastboot，备选 ADB)
        /// </summary>
        private async Task QuickRebootBootloaderAsync()
        {
            AppendLog("执行: 重启到 Fastboot...", Color.Cyan);
            
            // 优先尝试 Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootBootloaderAsync();
                if (ok)
                {
                    AppendLog("Fastboot: 重启到 Bootloader 成功", Color.Green);
                    return;
                }
            }
            
            // 备选 ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootBootloaderAsync();
            if (result.Success)
                AppendLog("ADB: 重启到 Bootloader 成功", Color.Green);
            else
                AppendLog($"重启失败: {result.Error}", Color.Red);
        }
        
        /// <summary>
        /// 快捷重启到 Fastbootd (优先 Fastboot，备选 ADB)
        /// </summary>
        private async Task QuickRebootFastbootdAsync()
        {
            AppendLog("执行: 重启到 Fastbootd...", Color.Cyan);
            
            // 优先尝试 Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootFastbootdAsync();
                if (ok)
                {
                    AppendLog("Fastboot: 重启到 Fastbootd 成功", Color.Green);
                    return;
                }
            }
            
            // 备选 ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootFastbootAsync();
            if (result.Success)
                AppendLog("ADB: 重启到 Fastbootd 成功", Color.Green);
            else
                AppendLog($"重启失败: {result.Error}", Color.Red);
        }
        
        /// <summary>
        /// 快捷重启到 Recovery (优先 Fastboot，备选 ADB)
        /// </summary>
        private async Task QuickRebootRecoveryAsync()
        {
            AppendLog("执行: 重启到 Recovery...", Color.Cyan);
            
            // 优先尝试 Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootRecoveryAsync();
                if (ok)
                {
                    AppendLog("Fastboot: 重启到 Recovery 成功", Color.Green);
                    return;
                }
            }
            
            // 备选 ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootRecoveryAsync();
            if (result.Success)
                AppendLog("ADB: 重启到 Recovery 成功", Color.Green);
            else
                AppendLog($"重启失败: {result.Error}", Color.Red);
        }
        
        /// <summary>
        /// MI踢EDL - Fastboot OEM EDL (仅限小米设备)
        /// </summary>
        private async Task QuickMiRebootEdlAsync()
        {
            AppendLog("执行: MI踢EDL (fastboot oem edl)...", Color.Cyan);
            
            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("请先连接 Fastboot 设备", Color.Orange);
                return;
            }
            
            bool ok = await _fastbootController.OemEdlAsync();
            if (ok)
                AppendLog("MI踢EDL: 成功，设备将进入 EDL 模式", Color.Green);
            else
                AppendLog("MI踢EDL: 失败，设备可能不支持此命令", Color.Red);
        }
        
        /// <summary>
        /// 联想或安卓踢EDL - ADB reboot edl
        /// </summary>
        private async Task QuickAdbRebootEdlAsync()
        {
            AppendLog("执行: 联想/安卓踢EDL (adb reboot edl)...", Color.Cyan);
            
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootEdlAsync();
            if (result.Success)
                AppendLog("ADB: 踢EDL成功，设备将进入 EDL 模式", Color.Green);
            else
                AppendLog($"踢EDL失败: {result.Error}", Color.Red);
        }
        
        /// <summary>
        /// 擦除谷歌锁 (Fastboot erase frp)
        /// </summary>
        private async Task QuickEraseFrpAsync()
        {
            AppendLog("执行: 擦除谷歌锁 (fastboot erase frp)...", Color.Cyan);
            
            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("请先连接 Fastboot 设备", Color.Orange);
                return;
            }
            
            bool ok = await _fastbootController.EraseFrpAsync();
            if (ok)
                AppendLog("擦除谷歌锁: 成功", Color.Green);
            else
                AppendLog("擦除谷歌锁: 失败，设备可能已锁定 Bootloader", Color.Red);
        }
        
        /// <summary>
        /// 切换槽位 (Fastboot set_active)
        /// </summary>
        private async Task QuickSwitchSlotAsync()
        {
            AppendLog("执行: 切换槽位...", Color.Cyan);
            
            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("请先连接 Fastboot 设备", Color.Orange);
                return;
            }
            
            // 获取当前槽位
            string currentSlot = await _fastbootController.GetCurrentSlotAsync();
            if (string.IsNullOrEmpty(currentSlot))
            {
                AppendLog("无法获取当前槽位，设备可能不支持 A/B 分区", Color.Orange);
                return;
            }
            
            // 切换到另一个槽位
            string targetSlot = currentSlot == "a" ? "b" : "a";
            AppendLog($"当前槽位: {currentSlot}，切换到: {targetSlot}", Color.White);
            
            bool ok = await _fastbootController.SetActiveSlotAsync(targetSlot);
            if (ok)
                AppendLog($"切换槽位成功: {currentSlot} -> {targetSlot}", Color.Green);
            else
                AppendLog("切换槽位失败", Color.Red);
        }
        
        #endregion
        
        #region 其他功能菜单
        
        /// <summary>
        /// 打开设备管理器
        /// </summary>
        private void OpenDeviceManager()
        {
            try
            {
                System.Diagnostics.Process.Start("devmgmt.msc");
                AppendLog("已打开设备管理器", Color.Blue);
            }
            catch (Exception ex)
            {
                AppendLog($"打开设备管理器失败: {ex.Message}", Color.Red);
            }
        }
        
        /// <summary>
        /// 打开 CMD 命令行 (在程序目录下，管理员权限)
        /// </summary>
        private void OpenCommandPrompt()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"  // 以管理员权限运行
                };
                System.Diagnostics.Process.Start(psi);
                AppendLog($"已打开管理员命令行: {psi.WorkingDirectory}", Color.Blue);
            }
            catch (Exception ex)
            {
                // 用户可能取消了 UAC 提示
                if (ex.Message.Contains("canceled") || ex.Message.Contains("取消"))
                    AppendLog("用户取消了管理员权限请求", Color.Orange);
                else
                    AppendLog($"打开命令行失败: {ex.Message}", Color.Red);
            }
        }
        
        /// <summary>
        /// 打开驱动安装程序
        /// </summary>
        /// <param name="driverType">驱动类型: android, mtk, qualcomm</param>
        private void OpenDriverInstaller(string driverType)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string driverPath = null;
                string driverName = null;
                
                switch (driverType.ToLower())
                {
                    case "android":
                        driverName = "安卓驱动";
                        // 尝试多个可能的路径
                        string[] androidPaths = {
                            Path.Combine(appDir, "drivers", "android_usb_driver.exe"),
                            Path.Combine(appDir, "drivers", "adb_driver.exe"),
                            Path.Combine(appDir, "ADB_Driver.exe")
                        };
                        driverPath = androidPaths.FirstOrDefault(File.Exists);
                        break;
                        
                    case "mtk":
                        driverName = "MTK驱动";
                        string[] mtkPaths = {
                            Path.Combine(appDir, "drivers", "mtk_usb_driver.exe"),
                            Path.Combine(appDir, "drivers", "MediaTek_USB_VCOM_Driver.exe"),
                            Path.Combine(appDir, "MTK_Driver.exe")
                        };
                        driverPath = mtkPaths.FirstOrDefault(File.Exists);
                        break;
                        
                    case "qualcomm":
                        driverName = "高通驱动";
                        string[] qcPaths = {
                            Path.Combine(appDir, "drivers", "qualcomm_usb_driver.exe"),
                            Path.Combine(appDir, "drivers", "Qualcomm_USB_Driver.exe"),
                            Path.Combine(appDir, "QC_Driver.exe")
                        };
                        driverPath = qcPaths.FirstOrDefault(File.Exists);
                        break;
                }
                
                if (string.IsNullOrEmpty(driverPath))
                {
                    AppendLog($"{driverName}安装程序未找到，请手动安装", Color.Orange);
                    MessageBox.Show($"{driverName}安装程序未找到。\n\n请前往官方网站下载对应驱动。", 
                        "驱动未找到", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                System.Diagnostics.Process.Start(driverPath);
                AppendLog($"已启动{driverName}安装程序", Color.Blue);
            }
            catch (Exception ex)
            {
                AppendLog($"启动驱动安装程序失败: {ex.Message}", Color.Red);
            }
        }
        
        #endregion
    }
}