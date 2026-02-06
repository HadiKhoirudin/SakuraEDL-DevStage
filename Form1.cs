using LoveAlways.Common;
using LoveAlways.Fastboot.Common;
using LoveAlways.Fastboot.UI;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Qualcomm.UI;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoveAlways
{
    public partial class Form1 : AntdUI.Window
    {
        private string logFilePath;
        private string selectedLocalImagePath = "";
        private string input8OriginalText = "";
        private bool isEnglish = false;

        // Image URL History
        private List<string> urlHistory = new List<string>();

        // Image Preview Cache
        private List<Image> previewImages = new List<Image>();
        private const int MAX_PREVIEW_IMAGES = 5; // Save at most 5 previews

        // Original control positions and sizes
        private Point originalinput6Location;
        private Point originalbutton4Location;
        private Point originalcheckbox13Location;
        private Point originalinput7Location;
        private Point originalinput9Location;
        private Point originallistView2Location;
        private Size originallistView2Size;
        private Point originaluiGroupBox4Location;
        private Size originaluiGroupBox4Size;

        // Qualcomm UI Controller
        private QualcommUIController _qualcommController;
        private System.Windows.Forms.Timer _portRefreshTimer;
        private string _lastPortList = "";
        private int _lastEdlCount = 0;
        private bool _isOnFastbootTab = false;  // Current tab is Fastboot
        private string _selectedXmlDirectory = "";  // Directory of selected XML file

        // Fastboot UI Controller
        private FastbootUIController _fastbootController;

        public Form1()
        {
            InitializeComponent();

            this.FormClosing += Form1_FormClosing;

            // Enable double buffering to reduce flicker (optimized for low-end PCs)
            if (PerformanceConfig.EnableDoubleBuffering)
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint, true);
                UpdateStyles();
            }

            // Initialize Log System
            InitializeLogSystem();

            checkbox14.Checked = true;
            radio3.Checked = true;
            // Load system info (use preload data)
            this.Load += (sender, e) =>
            {
                try
                {
                    // Use preload system info
                    string sysInfo = PreloadManager.SystemInfo ?? "Unknown";
                    uiLabel4.Text = $"Computer: {sysInfo}";

                    // Write system info to log header
                    WriteLogHeader(sysInfo);
                    AppendLog("Loading...OK", Color.Green);
                }
                catch (Exception ex)
                {
                    uiLabel4.Text = $"System Info Error: {ex.Message}";
                    AppendLog($"Initialization Failed: {ex.Message}", Color.Red);
                }
            };

            // Bind Button Events
            button2.Click += Button2_Click;
            button3.Click += Button3_Click;
            slider1.ValueChanged += Slider1_ValueChanged;
            uiComboBox4.SelectedIndexChanged += UiComboBox4_SelectedIndexChanged;

            // Bind select3 event
            select3.SelectedIndexChanged += Select3_SelectedIndexChanged;

            // Save original control positions and sizes
            SaveOriginalPositions();

            // Bind checkbox17 and checkbox19 events
            checkbox17.CheckedChanged += Checkbox17_CheckedChanged;
            checkbox19.CheckedChanged += Checkbox19_CheckedChanged;

            // Initialize URL ComboBox
            InitializeUrlComboBox();

            // Initialize Image Preview Control
            InitializeImagePreview();

            // Apply Default Layout
            ApplyCompactLayout();

            // Initialize Qualcomm Module
            InitializeQualcommModule();

            // Initialize Fastboot Module
            InitializeFastbootModule();

            // Initialize EDL Loader List
            InitializeEdlLoaderList();

            // Initialize Spreadtrum Module
            InitializeSpreadtrumModule();

            // Initialize MediaTek Module
            InitializeMediaTekModule();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        #region Qualcomm Module

        private void InitializeQualcommModule()
        {
            try
            {
                // Create Qualcomm UI Controller (pass two log delegates: UI log + detailed debug log)
                _qualcommController = new QualcommUIController(
                    (msg, color) => AppendLog(msg, color),
                    msg => AppendLogDetail(msg));

                // Subscribe to Xiaomi Auth Token event (popup display token when built-in signature fails)
                _qualcommController.XiaomiAuthTokenRequired += OnXiaomiAuthTokenRequired;

                // Set listView2 to support multi-select and checkboxes
                listView2.MultiSelect = true;
                listView2.CheckBoxes = true;
                listView2.FullRowSelect = true;

                // Bind controls - Qualcomm controls on tabPage2
                // checkbox12 = Skip Loader, checkbox16 = Protect Partition, input6 = Loader Path
                _qualcommController.BindControls(
                    portComboBox: uiComboBox1,           // Global port selection
                    partitionListView: listView2,        // Partition list
                    progressBar: uiProcessBar1,          // Total Progress Bar (Long) - Shows overall operation progress
                    statusLabel: null,
                    skipSaharaCheckbox: checkbox12,      // Skip Sahara
                    protectPartitionsCheckbox: checkbox16, // Protect Partition
                    programmerPathTextbox: null,         // input6 is AntdUI.Input type, needs special handling
                    outputPathTextbox: null,
                    timeLabel: uiLabel6,                 // Time label
                    speedLabel: uiLabel7,                // Speed label
                    operationLabel: uiLabel8,            // Current operation label
                    subProgressBar: uiProcessBar2,       // Sub Progress Bar (Short) - Shows real-time progress of single operation
                                                         // Device Info Labels (uiGroupBox3)
                    brandLabel: uiLabel9,                // Brand
                    chipLabel: uiLabel11,                // Chip
                    modelLabel: uiLabel3,                // Model
                    serialLabel: uiLabel10,              // Serial
                    storageLabel: uiLabel13,             // Storage
                    unlockLabel: uiLabel14,              // Model 2
                    otaVersionLabel: uiLabel12           // OTA Version
                );

                // ========== tabPage2 Qualcomm Page Button Events ==========
                // uiButton6 = Read GPT, uiButton7 = Read Partition
                // uiButton8 = Write Partition, uiButton9 = Erase Partition
                uiButton6.Click += async (s, e) => await QualcommReadPartitionTableAsync();
                uiButton7.Click += async (s, e) => await QualcommReadPartitionAsync();
                uiButton8.Click += async (s, e) => await QualcommWritePartitionAsync();
                uiButton9.Click += async (s, e) => await QualcommErasePartitionAsync();

                // ========== File Selection ==========
                // input8 = Double click to select Loader (Programmer/Firehose)
                input8.DoubleClick += (s, e) => QualcommSelectProgrammer();

                // input9 = Double click to select Digest file (For VIP Auth)
                input9.DoubleClick += (s, e) => QualcommSelectDigest();

                // input7 = Double click to select Signature file (For VIP Auth)
                input7.DoubleClick += (s, e) => QualcommSelectSignature();

                // input6 = Double click to select rawprogram.xml
                input6.DoubleClick += (s, e) => QualcommSelectRawprogramXml();

                // button4 = Browse button next to input6 (Select Raw XML)
                button4.Click += (s, e) => QualcommSelectRawprogramXml();

                // Partition Search (select4 = Find Partition)
                select4.TextChanged += (s, e) => QualcommSearchPartition();
                select4.SelectedIndexChanged += (s, e) => { _isSelectingFromDropdown = true; };

                // Storage Type Selection (radio3 = UFS, radio4 = eMMC)
                radio3.CheckedChanged += (s, e) => { if (radio3.Checked) _storageType = "ufs"; };
                radio4.CheckedChanged += (s, e) => { if (radio4.Checked) _storageType = "emmc"; };

                // 注意: checkbox17/checkbox19 的事件已在构造函数中绑定 (Checkbox17_CheckedChanged / Checkbox19_CheckedChanged)
                // 那里会调用 UpdateAuthMode()，这里不再重复绑定

                // ========== checkbox13 全选/取消全选 ==========
                checkbox13.CheckedChanged += (s, e) => QualcommSelectAllPartitions(checkbox13.Checked);

                // ========== listView2 双击选择镜像文件 ==========
                listView2.DoubleClick += (s, e) => QualcommPartitionDoubleClick();

                // ========== checkbox11 Generate XML Option ==========
                // 这只是一个开关，表示回读分区时是否同时生成 XML
                // 实际生成在回读完成后执行

                // ========== checkbox15 Auto Reboot (After flash) ==========
                // 状态读取已在 QualcommErasePartitionAsync 等操作中检查

                // ========== EDL Operation Menu Events ==========
                toolStripMenuItem4.Click += async (s, e) => await _qualcommController.RebootToEdlAsync();
                toolStripMenuItem5.Click += async (s, e) => await _qualcommController.RebootToSystemAsync();
                eDL切换槽位ToolStripMenuItem.Click += async (s, e) => await QualcommSwitchSlotAsync();
                激活LUNToolStripMenuItem.Click += async (s, e) => await QualcommSetBootLunAsync();

                // ========== Quick Action Menu Events (Device Manager) ==========
                // Reboot System (ADB/Fastboot)
                toolStripMenuItem2.Click += async (s, e) => await QuickRebootSystemAsync();
                // Reboot to Fastboot (ADB/Fastboot)
                toolStripMenuItem6.Click += async (s, e) => await QuickRebootBootloaderAsync();
                // Reboot to Fastbootd (ADB/Fastboot)
                toolStripMenuItem7.Click += async (s, e) => await QuickRebootFastbootdAsync();
                // Reboot to Recovery (ADB/Fastboot)
                重启恢复ToolStripMenuItem.Click += async (s, e) => await QuickRebootRecoveryAsync();
                // MI Reboot EDL (Fastboot only)
                mIToolStripMenuItem.Click += async (s, e) => await QuickMiRebootEdlAsync();
                // Lenovo or Android Reboot EDL (ADB only)
                联想或安卓踢EDLToolStripMenuItem.Click += async (s, e) => await QuickAdbRebootEdlAsync();
                // Erase FRP (Fastboot only)
                擦除谷歌锁ToolStripMenuItem.Click += async (s, e) => await QuickEraseFrpAsync();
                // Switch Slot (Fastboot only)
                切换槽位ToolStripMenuItem.Click += async (s, e) => await QuickSwitchSlotAsync();

                // ========== Other Menu Events ==========
                // Device Manager
                设备管理器ToolStripMenuItem.Click += (s, e) => OpenDeviceManager();
                // CMD Command Line
                cMD命令行ToolStripMenuItem.Click += (s, e) => OpenCommandPrompt();
                // Android Driver
                安卓驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("android");
                // MTK Driver
                mTK驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("mtk");
                // Qualcomm Driver
                高通驱动ToolStripMenuItem.Click += (s, e) => OpenDriverInstaller("qualcomm");

                // ========== 停止按钮 ==========
                uiButton1.Click += (s, e) => StopCurrentOperation();

                // ========== Refresh Ports ==========
                // Refresh port list on init (silent mode)
                //_lastEdlCount = _qualcommController.RefreshPorts(silent: true);

                // 启动端口自动检测定时器 (每2秒检测一次，只在端口列表变化时才刷新)

                //_portRefreshTimer = new System.Windows.Forms.Timer();
                //_portRefreshTimer.Interval = 5000;
                //_portRefreshTimer.Tick += (s, e) => RefreshPortsIfIdle();
                //_portRefreshTimer.Start();

                AppendLog("[Qualcomm] Module initialized", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"[Qualcomm] Module initialization failed: {ex.Message}", Color.Red);
            }
        }

        private string _storageType = "ufs";
        private string _authMode = "none";

        /// <summary>
        /// Refresh ports when idle (detect device connection/disconnection)
        /// </summary>
        private void RefreshPortsIfIdle()
        {
            try
            {
                // If currently on Fastboot tab, do not refresh Qualcomm ports
                if (_isOnFastbootTab)
                    return;

                // If operation in progress, do not refresh
                if (_qualcommController != null && _qualcommController.HasPendingOperation)
                    return;

                // Get current port list for change detection
                var ports = LoveAlways.Qualcomm.Common.PortDetector.DetectAllPorts();
                var edlPorts = LoveAlways.Qualcomm.Common.PortDetector.DetectEdlPorts();
                string currentPortList = string.Join(",", ports.ConvertAll(p => p.PortName));

                // Refresh only if port list changed
                if (currentPortList != _lastPortList)
                {
                    bool hadEdl = _lastEdlCount > 0;
                    bool newEdlDetected = edlPorts.Count > 0 && !hadEdl;
                    _lastPortList = currentPortList;

                    // Silent refresh, returns EDL port count
                    int edlCount = _qualcommController?.RefreshPorts(silent: true) ?? 0;

                    // Prompt when new EDL device detected
                    if (newEdlDetected && edlPorts.Count > 0)
                    {
                        AppendLog($"Detected EDL Device: {edlPorts[0].PortName} - {edlPorts[0].Description}", Color.LimeGreen);
                    }

                    _lastEdlCount = edlCount;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EDL 端口检测异常: {ex.Message}");
            }
        }

        private void UpdateAuthMode()
        {
            if (checkbox17.Checked && checkbox19.Checked)
            {
                checkbox19.Checked = false; // Mutually exclusive, prioritize OnePlus
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
                ofd.Title = "Select Loader (Programmer/Firehose)";
                ofd.Filter = "Loader File|*.mbn;*.elf|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input8.Text = ofd.FileName;
                    AppendLog($"Selected Loader: {Path.GetFileName(ofd.FileName)}", Color.Green);
                }
            }
        }

        private async void QualcommSelectDigest()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Digest File (VIP Auth)";
                ofd.Filter = "Digest File|*.elf;*.bin;*.mbn|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input9.Text = ofd.FileName;
                    AppendLog($"Selected Digest: {Path.GetFileName(ofd.FileName)}", Color.Green);

                    // If device connected and Signature selected, auto execute VIP auth
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        string signaturePath = input7.Text;
                        if (!string.IsNullOrEmpty(signaturePath) && File.Exists(signaturePath))
                        {
                            try
                            {
                                AppendLog("Selected complete VIP auth files, starting auth...", Color.Blue);
                                await QualcommPerformVipAuthAsync();
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"VIP Auth Exception: {ex.Message}", Color.Red);
                            }
                        }
                    }
                }
            }
        }

        private async void QualcommSelectSignature()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Signature File (VIP Auth)";
                ofd.Filter = "Signature File|*.bin;signature*|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input7.Text = ofd.FileName;
                    AppendLog($"Selected Signature: {Path.GetFileName(ofd.FileName)}", Color.Green);

                    // If device connected and Digest selected, auto execute VIP auth
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        string digestPath = input9.Text;
                        if (!string.IsNullOrEmpty(digestPath) && File.Exists(digestPath))
                        {
                            try
                            {
                                AppendLog("Selected complete VIP auth files, starting auth...", Color.Blue);
                                await QualcommPerformVipAuthAsync();
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"VIP Auth Exception: {ex.Message}", Color.Red);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Manually execute VIP Auth (OPPO/Realme)
        /// </summary>
        private async Task QualcommPerformVipAuthAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("Please connect device first", Color.Orange);
                return;
            }

            string digestPath = input9.Text;
            string signaturePath = input7.Text;

            if (string.IsNullOrEmpty(digestPath) || !File.Exists(digestPath))
            {
                AppendLog("Please select Digest file (Double click input box)", Color.Orange);
                return;
            }

            if (string.IsNullOrEmpty(signaturePath) || !File.Exists(signaturePath))
            {
                AppendLog("Please select Signature file (Double click input box)", Color.Orange);
                return;
            }

            bool success = await _qualcommController.PerformVipAuthAsync(digestPath, signaturePath);
            if (success)
            {
                AppendLog("VIP Auth success, can now operate sensitive partitions", Color.Green);
            }
        }

        private void QualcommSelectRawprogramXml()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Rawprogram XML File (Multi-select allowed)";
                ofd.Filter = "XML File|rawprogram*.xml;*.xml|All Files|*.*";
                ofd.Multiselect = true;  // Support Multi-select

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // Save XML file directory (used for searching patch files later)
                    _selectedXmlDirectory = Path.GetDirectoryName(ofd.FileNames[0]) ?? "";

                    if (ofd.FileNames.Length == 1)
                    {
                        input6.Text = ofd.FileName;
                        AppendLog($"Selected XML: {Path.GetFileName(ofd.FileName)}", Color.Green);
                    }
                    else
                    {
                        input6.Text = $"Selected {ofd.FileNames.Length} files";
                        foreach (var file in ofd.FileNames)
                        {
                            AppendLog($"Selected XML: {Path.GetFileName(file)}", Color.Green);
                        }
                    }

                    // Parse all selected XML files
                    LoadMultipleRawprogramXml(ofd.FileNames);
                }
            }
        }

        private void LoadMultipleRawprogramXml(string[] xmlPaths)
        {
            var allTasks = new List<Qualcomm.Common.FlashTask>();
            string programmerPath = "";
            string[] filesToLoad = xmlPaths;

            // If user selected only one file, and filename contains rawprogram, automatically search for other LUNs in same directory
            if (xmlPaths.Length == 1 && Path.GetFileName(xmlPaths[0]).Contains("rawprogram"))
            {
                string dir = Path.GetDirectoryName(xmlPaths[0]);
                // Match only rawprogram0.xml, rawprogram1.xml etc.
                // Exclude _BLANK_GPT, _WIPE_PARTITIONS, _ERASE etc.
                var siblingFiles = Directory.GetFiles(dir, "rawprogram*.xml")
                    .Where(f =>
                    {
                        string fileName = Path.GetFileNameWithoutExtension(f).ToLower();
                        // Only accept rawprogram + digit format (e.g. rawprogram0, rawprogram1, rawprogram_unsparse)
                        // Exclude files containing blank, wipe, erase
                        if (fileName.Contains("blank") || fileName.Contains("wipe") || fileName.Contains("erase"))
                            return false;
                        return true;
                    })
                    .OrderBy(f =>
                    {
                        // Sort by digit
                        string name = Path.GetFileNameWithoutExtension(f);
                        var numStr = new string(name.Where(char.IsDigit).ToArray());
                        int num;
                        return int.TryParse(numStr, out num) ? num : 999;
                    })
                    .ToArray();

                if (siblingFiles.Length > 1)
                {
                    filesToLoad = siblingFiles;
                    AppendLog($"Detected multiple LUNs, automatically loaded {siblingFiles.Length} XML files", Color.Blue);
                }
            }

            foreach (var xmlPath in filesToLoad)
            {
                try
                {
                    string dir = Path.GetDirectoryName(xmlPath);
                    var parser = new Qualcomm.Common.RawprogramParser(dir, msg => { /* Avoid excessive redundant logs */ });

                    // Parse current XML file
                    var tasks = parser.ParseRawprogramXml(xmlPath);

                    // Add only non-existing tasks (Judge by LUN + StartSector + Label)
                    foreach (var task in tasks)
                    {
                        if (!allTasks.Any(t => t.Lun == task.Lun && t.StartSector == task.StartSector && t.Label == task.Label))
                        {
                            allTasks.Add(task);
                        }
                    }

                    AppendLog($"Parsing {Path.GetFileName(xmlPath)}: {tasks.Count} tasks (Total: {allTasks.Count})", Color.Blue);

                    // Auto identify matching patch file
                    string patchPath = FindMatchingPatchFile(xmlPath);
                    if (!string.IsNullOrEmpty(patchPath))
                    {
                        // Record to global variable or post-processing
                    }

                    // Auto identify matching programmer file (only once)
                    if (string.IsNullOrEmpty(programmerPath))
                    {
                        programmerPath = parser.FindProgrammer();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Parsing {Path.GetFileName(xmlPath)} Failed: {ex.Message}", Color.Red);
                }
            }

            if (allTasks.Count > 0)
            {
                AppendLog($"Loaded {allTasks.Count} flash tasks", Color.Green);

                if (!string.IsNullOrEmpty(programmerPath))
                {
                    input8.Text = programmerPath;
                    AppendLog($"Auto-detected Loader: {Path.GetFileName(programmerPath)}", Color.Green);
                }

                // Pre-check patch files
                if (!string.IsNullOrEmpty(_selectedXmlDirectory) && Directory.Exists(_selectedXmlDirectory))
                {
                    var patchFiles = Directory.GetFiles(_selectedXmlDirectory, "patch*.xml", SearchOption.TopDirectoryOnly)
                        .Where(f =>
                        {
                            string fn = Path.GetFileName(f).ToLower();
                            return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                        })
                        .OrderBy(f =>
                        {
                            string name = Path.GetFileNameWithoutExtension(f);
                            var numStr = new string(name.Where(char.IsDigit).ToArray());
                            int num;
                            return int.TryParse(numStr, out num) ? num : 999;
                        })
                        .ToList();

                    if (patchFiles.Count > 0)
                    {
                        AppendLog($"Detected {patchFiles.Count} Patch Files: {string.Join(", ", patchFiles.Select(f => Path.GetFileName(f)))}", Color.Blue);
                    }
                    else
                    {
                        AppendLog("No Patch files detected", Color.Gray);
                    }
                }

                // Fill all tasks into partition list
                FillPartitionListFromTasks(allTasks);
            }
            else
            {
                AppendLog("No valid flash tasks found in XML", Color.Orange);
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

                // Try other patch files
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
                // Convert to PartitionInfo for unified processing
                var partition = new PartitionInfo
                {
                    Name = task.Label,
                    Lun = task.Lun,
                    StartSector = task.StartSector,
                    NumSectors = task.NumSectors,
                    SectorSize = task.SectorSize
                };

                // Calculate Address
                long startAddress = task.StartSector * task.SectorSize;
                long endSector = task.StartSector + task.NumSectors - 1;
                long endAddress = (endSector + 1) * task.SectorSize;

                // Column Order: Partition, LUN, Size, Start Sector, End Sector, Sector Count, Start Addr, End Addr, File Path
                var item = new ListViewItem(task.Label);                           // Partition
                item.SubItems.Add(task.Lun.ToString());                             // LUN
                item.SubItems.Add(task.FormattedSize);                              // Size
                item.SubItems.Add(task.StartSector.ToString());                     // Start Sector
                item.SubItems.Add(endSector.ToString());                            // End Sector
                item.SubItems.Add(task.NumSectors.ToString());                      // Sector Count
                item.SubItems.Add($"0x{startAddress:X}");                           // Start Address
                item.SubItems.Add($"0x{endAddress:X}");                             // End Address
                item.SubItems.Add(string.IsNullOrEmpty(task.FilePath) ? task.Filename : task.FilePath);  // File Path
                item.Tag = partition;

                // Check if image file exists
                bool fileExists = !string.IsNullOrEmpty(task.FilePath) && File.Exists(task.FilePath);

                // Auto CHECK if file exists (exclude sensitive partitions)
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
                // Check if quick reconnect is possible (Port released but Firehose still available)
                if (_qualcommController.CanQuickReconnect)
                {
                    AppendLog("Attempting quick reconnect...", Color.Blue);
                    bool reconnected = await _qualcommController.QuickReconnectAsync();
                    if (reconnected)
                    {
                        AppendLog("Quick reconnect success", Color.Green);
                        // 已有分区数据，不需要重新读取
                        if (_qualcommController.Partitions != null && _qualcommController.Partitions.Count > 0)
                        {
                            AppendLog($"Already have {_qualcommController.Partitions.Count} partition data", Color.Gray);
                            return;
                        }
                    }
                    else
                    {
                        AppendLog("Quick reconnect failed, full configuration needed", Color.Orange);
                        checkbox12.Checked = false; // Cancel Skip Loader
                    }
                }

                // Quick reconnect failed or unavailable, try full connect
                if (!_qualcommController.IsConnected)
                {
                    bool connected = await QualcommConnectAsync();
                    if (!connected) return;
                }
            }

            await _qualcommController.ReadPartitionTableAsync();
        }

        private async Task<bool> QualcommConnectAsync()
        {
            if (_qualcommController == null) return false;

            string selectedLoader = select3.Text;
            bool isCloudMatch = selectedLoader.Contains("Cloud Auto Match");
            bool skipSahara = checkbox12.Checked;

            // Skip Loader Mode - Connect to Firehose directly (Device already in Firehose mode)
            if (skipSahara)
            {
                AppendLog("[Qualcomm] Skipping Sahara, connecting to Firehose directly...", Color.Blue);
                return await _qualcommController.ConnectWithOptionsAsync(
                    "", _storageType, true, _authMode,
                    input9.Text?.Trim() ?? "",
                    input7.Text?.Trim() ?? ""
                );
            }

            // ========== Cloud Auto Match Mode ==========
            if (isCloudMatch)
            {
                return await QualcommConnectWithCloudMatchAsync();
            }

            // Normal Mode (Custom Loader)
            // input8 = Loader Path
            string programmerPath = input8.Text?.Trim() ?? "";

            if (!skipSahara && string.IsNullOrEmpty(programmerPath))
            {
                AppendLog("Please select loader or use cloud match", Color.Orange);
                return false;
            }

            // Use custom connection logic
            return await _qualcommController.ConnectWithOptionsAsync(
                programmerPath,
                _storageType,
                skipSahara,
                _authMode,
                input9.Text?.Trim() ?? "",
                input7.Text?.Trim() ?? ""
            );
        }

        /// <summary>
        /// Cloud Auto Match Connection
        /// </summary>
        private async Task<bool> QualcommConnectWithCloudMatchAsync()
        {
            AppendLog("[Cloud] Getting device info...", Color.Cyan);

            // 1. Execute Sahara Handshake to get device info (Do not upload Loader)
            var deviceInfo = await _qualcommController.GetSaharaDeviceInfoAsync();

            if (deviceInfo == null)
            {
                AppendLog("[Cloud] Cannot get device info, check connection", Color.Red);
                return false;
            }

            AppendLog($"[Cloud] Device: MSM={deviceInfo.MsmId}, OEM={deviceInfo.OemId}", Color.Blue);
            if (!string.IsNullOrEmpty(deviceInfo.PkHash) && deviceInfo.PkHash.Length >= 16)
            {
                AppendLog($"[Cloud] PK Hash: {deviceInfo.PkHash.Substring(0, 16)}...", Color.Gray);
            }

            // 2. Call Cloud API Match
            var cloudService = LoveAlways.Qualcomm.Services.CloudLoaderService.Instance;
            var result = await cloudService.MatchLoaderAsync(
                deviceInfo.MsmId,
                deviceInfo.PkHash,
                deviceInfo.OemId,
                _storageType
            );

            if (result == null || result.Data == null)
            {
                AppendLog("[Cloud] No matching Loader found", Color.Orange);
                AppendLog("[Cloud] Please try selecting loader manually", Color.Yellow);

                // Report no match
                cloudService.ReportDeviceLog(
                    deviceInfo.MsmId,
                    deviceInfo.PkHash,
                    deviceInfo.OemId,
                    _storageType,
                    "not_found"
                );

                return false;
            }

            // 3. Match Success
            AppendLog($"[Cloud] Match Success: {result.Filename}", Color.Green);
            AppendLog($"[Cloud] Vendor: {result.Vendor}, Chip: {result.Chip}", Color.Blue);
            AppendLog($"[Cloud] Confidence: {result.Confidence}%, Match Type: {result.MatchType}", Color.Gray);

            // 4. Select connection method based on auth type
            string authMode = result.AuthType?.ToLower() switch
            {
                "miauth" => "xiaomi",
                "demacia" => "oneplus",
                "vip" => "vip",
                _ => "none"
            };

            if (authMode != "none")
            {
                AppendLog($"[Cloud] Auth Type: {result.AuthType}", Color.Cyan);
            }

            // 5. Continue connect - Upload cloud matched Loader
            AppendLog($"[Cloud] Sending Loader ({result.Data.Length / 1024} KB)...", Color.Cyan);

            bool success = await _qualcommController.ContinueConnectWithCloudLoaderAsync(
                result.Data,
                _storageType,
                authMode
            );

            // 6. Report device log
            cloudService.ReportDeviceLog(
                deviceInfo.MsmId,
                deviceInfo.PkHash,
                deviceInfo.OemId,
                _storageType,
                success ? "success" : "failed"
            );

            if (success)
            {
                AppendLog("[Cloud] Device connected successfully", Color.Green);
            }
            else
            {
                AppendLog("[Cloud] Device connection failed", Color.Red);
            }

            return success;
        }

        private void GeneratePartitionXml()
        {
            try
            {
                if (_qualcommController.Partitions == null || _qualcommController.Partitions.Count == 0)
                {
                    AppendLog("Please read partition table first", Color.Orange);
                    return;
                }

                // Select save directory (generates multiple files... rawprogram0.xml, patch0.xml etc.)
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select XML Save Directory (Will generate multiple rawprogram and patch files based on LUN)";

                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        string saveDir = fbd.SelectedPath;
                        var parser = new LoveAlways.Qualcomm.Common.GptParser();
                        int sectorSize = _qualcommController.Partitions.Count > 0
                            ? _qualcommController.Partitions[0].SectorSize
                            : 4096;

                        // 1. Generate rawprogramX.xml
                        var rawprogramDict = parser.GenerateRawprogramXmls(_qualcommController.Partitions, sectorSize);
                        foreach (var kv in rawprogramDict)
                        {
                            string fileName = Path.Combine(saveDir, $"rawprogram{kv.Key}.xml");
                            File.WriteAllText(fileName, kv.Value);
                            AppendLog($"Generated: {Path.GetFileName(fileName)}", Color.Blue);
                        }

                        // 2. Generate patchX.xml
                        var patchDict = parser.GeneratePatchXmls(_qualcommController.Partitions, sectorSize);
                        foreach (var kv in patchDict)
                        {
                            string fileName = Path.Combine(saveDir, $"patch{kv.Key}.xml");
                            File.WriteAllText(fileName, kv.Value);
                            AppendLog($"Generated: {Path.GetFileName(fileName)}", Color.Blue);
                        }

                        // 3. Generate single merged partition.xml (Optional)
                        string partitionXml = parser.GeneratePartitionXml(_qualcommController.Partitions, sectorSize);
                        string pFileName = Path.Combine(saveDir, "partition.xml");
                        File.WriteAllText(pFileName, partitionXml);

                        AppendLog($"XML collection saved to: {saveDir}", Color.Green);

                        // Display Slot Info
                        string currentSlot = _qualcommController.GetCurrentSlot();
                        if (currentSlot != "nonexistent")
                        {
                            AppendLog($"Current Slot: {currentSlot}", Color.Blue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Generate XML Failed: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Generate XML for specific partitions (called during readback)
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

                // Group by LUN to generate rawprogram XML
                var byLun = partitions.GroupBy(p => p.Lun).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kv in byLun)
                {
                    int lun = kv.Key;
                    var lunPartitions = kv.Value;

                    // Generate rawprogram XML for this LUN
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" ?>");
                    sb.AppendLine("<data>");
                    sb.AppendLine("  <!-- Generated by MultiFlash TOOL - Readback Partitions -->");

                    foreach (var p in lunPartitions)
                    {
                        // Generate program entry (for flashing readback partition)
                        sb.AppendFormat("  <program SECTOR_SIZE_IN_BYTES=\"{0}\" file_sector_offset=\"0\" " +
                            "filename=\"{1}.img\" label=\"{1}\" num_partition_sectors=\"{2}\" " +
                            "physical_partition_number=\"{3}\" start_sector=\"{4}\" />\n",
                            sectorSize, p.Name, p.NumSectors, lun, p.StartSector);
                    }

                    sb.AppendLine("</data>");

                    string fileName = Path.Combine(saveDir, $"rawprogram{lun}.xml");
                    File.WriteAllText(fileName, sb.ToString());
                    AppendLog($"Generated readback partition XML: {Path.GetFileName(fileName)}", Color.Blue);
                }

                AppendLog($"Readback partition XML saved to: {saveDir}", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"Generate Readback XML Failed: {ex.Message}", Color.Orange);
            }
        }

        private async Task QualcommReadPartitionAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("Please connect device and read partition table first", Color.Orange);
                return;
            }

            // Get checked or selected partitions
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("Please select or check partitions to read", Color.Orange);
                return;
            }

            // Select Save Directory
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = checkedItems.Count == 1 ? "Select Save Location" : $"Select Save Directory (Reading {checkedItems.Count} partitions)";

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string saveDir = fbd.SelectedPath;

                    if (checkedItems.Count == 1)
                    {
                        // Single Partition
                        var partition = checkedItems[0];
                        string savePath = Path.Combine(saveDir, partition.Name + ".img");
                        await _qualcommController.ReadPartitionAsync(partition.Name, savePath);
                    }
                    else
                    {
                        // Batch Read
                        var partitionsToRead = new List<Tuple<string, string>>();
                        foreach (var p in checkedItems)
                        {
                            string savePath = Path.Combine(saveDir, p.Name + ".img");
                            partitionsToRead.Add(Tuple.Create(p.Name, savePath));
                        }
                        await _qualcommController.ReadPartitionsBatchAsync(partitionsToRead);
                    }

                    // After readback, if Generate XML checked, generate XML for readback partitions
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

            // Prioritize checked items
            foreach (ListViewItem item in listView2.CheckedItems)
            {
                var p = item.Tag as PartitionInfo;
                if (p != null) result.Add(p);
            }

            // If no checks, use selected items
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
                AppendLog("Please connect device and read partition table first", Color.Orange);
                return;
            }

            // Get checked or selected partitions
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("Please select or check partitions to write", Color.Orange);
                return;
            }

            if (checkedItems.Count == 1)
            {
                // Single Partition Write
                var partition = checkedItems[0];
                string filePath = "";

                // Check if file path exists (double clicked or parsed from XML)
                foreach (ListViewItem item in listView2.Items)
                {
                    var p = item.Tag as PartitionInfo;
                    if (p != null && p.Name == partition.Name)
                    {
                        filePath = item.SubItems.Count > 8 ? item.SubItems[8].Text : "";
                        break;
                    }
                }

                // If no file path or file not exists, pop selection dialog
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    // If MetaSuper checked and is super partition, guide user to select firmware dir
                    if (checkbox18.Checked && partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var fbd = new FolderBrowserDialog())
                        {
                            fbd.Description = "MetaSuper Enabled! Select OPLUS Firmware Root (contains IMAGES and META)";
                            if (fbd.ShowDialog() == DialogResult.OK)
                            {
                                await _qualcommController.FlashOplusSuperAsync(fbd.SelectedPath);
                                return;
                            }
                        }
                    }

                    using (var ofd = new OpenFileDialog())
                    {
                        ofd.Title = $"Select image file to write to {partition.Name}";
                        ofd.Filter = "Image File|*.img;*.bin|All Files|*.*";

                        if (ofd.ShowDialog() != DialogResult.OK)
                            return;

                        filePath = ofd.FileName;
                    }
                }
                else
                {
                    // Even if path exists, if MetaSuper enabled and is super partition, execute unpack logic
                    if (checkbox18.Checked && partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try inferring firmware root from file path (usually image under IMAGES folder)
                        string firmwareRoot = Path.GetDirectoryName(Path.GetDirectoryName(filePath));
                        if (Directory.Exists(Path.Combine(firmwareRoot, "META")))
                        {
                            await _qualcommController.FlashOplusSuperAsync(firmwareRoot);
                            return;
                        }
                        else
                        {
                            // If inference fails, select manually
                            using (var fbd = new FolderBrowserDialog())
                            {
                                fbd.Description = "MetaSuper Enabled! Select OPLUS Firmware Root (contains IMAGES and META)";
                                if (fbd.ShowDialog() == DialogResult.OK)
                                {
                                    await _qualcommController.FlashOplusSuperAsync(fbd.SelectedPath);
                                    return;
                                }
                            }
                        }
                    }
                }

                // Execute Write
                AppendLog($"Start writing {Path.GetFileName(filePath)} -> {partition.Name}", Color.Blue);
                bool success = await _qualcommController.WritePartitionAsync(partition.Name, filePath);

                if (success && checkbox15.Checked)
                {
                    AppendLog("Write complete, auto rebooting...", Color.Blue);
                    await _qualcommController.RebootToSystemAsync();
                }
            }
            else
            {
                // Batch Write - Get file paths from XML tasks
                // Use tuple with LUN and StartSector to handle PrimaryGPT/BackupGPT
                var partitionsToWrite = new List<Tuple<string, string, int, long>>();
                var missingFiles = new List<string>();

                foreach (ListViewItem item in listView2.CheckedItems)
                {
                    var partition = item.Tag as PartitionInfo;
                    if (partition == null) continue;

                    // Get file path (from SubItems)
                    string filePath = item.SubItems.Count > 8 ? item.SubItems[8].Text : "";

                    // Try finding file in current or XML dir
                    if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
                    {
                        // Try finding in input6 (XML Path) dir
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
                        // Pass (Name, Path, LUN, StartSector)
                        partitionsToWrite.Add(Tuple.Create(partition.Name, filePath, partition.Lun, partition.StartSector));
                    }
                    else
                    {
                        missingFiles.Add(partition.Name);
                    }
                }

                if (missingFiles.Count > 0)
                {
                    AppendLog($"Missing image files for partitions: {string.Join(", ", missingFiles)}", Color.Orange);
                }

                if (partitionsToWrite.Count > 0)
                {
                    // Collect Patch Files
                    List<string> patchFiles = new List<string>();

                    // Prioritize stored XML dir...
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
                        AppendLog($"Searching for Patch files in dir: {xmlDir}", Color.Gray);

                        // 1. Search current dir (sibling dir)
                        try
                        {
                            var sameDir = Directory.GetFiles(xmlDir, "patch*.xml", SearchOption.TopDirectoryOnly)
                                .Where(f =>
                                {
                                    string fn = Path.GetFileName(f).ToLower();
                                    return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                })
                                .ToList();
                            patchFiles.AddRange(sameDir);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Patch file search exception: {ex.Message}");
                        }

                        // 2. If not found in current, search subdirs
                        if (patchFiles.Count == 0)
                        {
                            try
                            {
                                var subDirs = Directory.GetFiles(xmlDir, "patch*.xml", SearchOption.AllDirectories)
                                    .Where(f =>
                                    {
                                        string fn = Path.GetFileName(f).ToLower();
                                        return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                    })
                                    .ToList();
                                patchFiles.AddRange(subDirs);
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"Error searching subdirs for Patch files: {ex.Message}", Color.Orange);
                            }
                        }

                        // 3. If still not found, search parent dir
                        if (patchFiles.Count == 0)
                        {
                            try
                            {
                                string parentDir = Path.GetDirectoryName(xmlDir);
                                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                                {
                                    AppendLog($"Current dir not found, searching parent: {parentDir}", Color.Gray);
                                    var parentPatches = Directory.GetFiles(parentDir, "patch*.xml", SearchOption.AllDirectories)
                                        .Where(f =>
                                        {
                                            string fn = Path.GetFileName(f).ToLower();
                                            return !fn.Contains("blank") && !fn.Contains("wipe") && !fn.Contains("erase");
                                        })
                                        .ToList();
                                    patchFiles.AddRange(parentPatches);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Parent dir Patch search exception: {ex.Message}");
                            }
                        }

                        // Sort patch files
                        patchFiles = patchFiles.Distinct().OrderBy(f =>
                        {
                            string name = Path.GetFileNameWithoutExtension(f);
                            var numStr = new string(name.Where(char.IsDigit).ToArray());
                            int num;
                            return int.TryParse(numStr, out num) ? num : 999;
                        }).ToList();

                        if (patchFiles.Count > 0)
                        {
                            AppendLog($"Detected {patchFiles.Count} Patch files:", Color.Blue);
                            foreach (var pf in patchFiles)
                            {
                                AppendLog($"  - {Path.GetFileName(pf)}", Color.Gray);
                            }
                        }
                        else
                        {
                            AppendLog("No Patch files detected, skipping patch step", Color.Gray);
                        }
                    }
                    else
                    {
                        AppendLog($"Cannot get XML dir path (xmlDir={xmlDir ?? "null"})", Color.Orange);
                    }

                    // UFS needs active boot LUN...
                    bool activateBootLun = _storageType == "ufs";
                    if (activateBootLun)
                    {
                        AppendLog("UFS: Readback GPT and activate boot LUN after write", Color.Blue);
                    }
                    else
                    {
                        AppendLog("eMMC: Only LUN0, no boot activation needed", Color.Gray);
                    }

                    int success = await _qualcommController.WritePartitionsBatchAsync(partitionsToWrite, patchFiles, activateBootLun);

                    if (success > 0 && checkbox15.Checked)
                    {
                        AppendLog("Batch write complete, auto rebooting...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
                else
                {
                    AppendLog("No valid images found, ensure XML parsed correctly or select manually", Color.Orange);
                }
            }
        }

        private async Task QualcommErasePartitionAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("Please connect device and read partition table first", Color.Orange);
                return;
            }

            // Get checked or selected partitions
            var checkedItems = GetCheckedOrSelectedPartitions();
            if (checkedItems.Count == 0)
            {
                AppendLog("Please select or check partitions to erase", Color.Orange);
                return;
            }

            // Erase Confirmation
            string message = checkedItems.Count == 1
                ? $"Confirm erase partition {checkedItems[0].Name}?\n\nIrreversible!"
                : $"Confirm erase {checkedItems.Count} partitions?\n\nPartitions: {string.Join(", ", checkedItems.ConvertAll(p => p.Name))}\n\nIrreversible!";

            var result = MessageBox.Show(
                message,
                "Confirm Erase",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                if (checkedItems.Count == 1)
                {
                    // Single Erase
                    bool success = await _qualcommController.ErasePartitionAsync(checkedItems[0].Name);

                    if (success && checkbox15.Checked)
                    {
                        AppendLog("Erase complete, auto rebooting...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
                else
                {
                    // Batch Erase
                    var partitionNames = checkedItems.ConvertAll(p => p.Name);
                    int success = await _qualcommController.ErasePartitionsBatchAsync(partitionNames);

                    if (success > 0 && checkbox15.Checked)
                    {
                        AppendLog("Batch erase complete, auto rebooting...", Color.Blue);
                        await _qualcommController.RebootToSystemAsync();
                    }
                }
            }
        }

        private async Task QualcommSwitchSlotAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("Please connect device", Color.Orange);
                return;
            }

            // Ask for Slot
            var result = MessageBox.Show("Switch to Slot A?\n\nYes for A\nNo for B",
                "Switch Slot", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
                await _qualcommController.SwitchSlotAsync("a");
            else if (result == DialogResult.No)
                await _qualcommController.SwitchSlotAsync("b");
        }

        private async Task QualcommSetBootLunAsync()
        {
            if (_qualcommController == null || !_qualcommController.IsConnected)
            {
                AppendLog("Please connect device", Color.Orange);
                return;
            }

            // UFS: 0, 1, 2, 4(Boot A), 5(Boot B)
            // eMMC: 0
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter LUN:\n\nUFS: 0, 1, 2, 4(Boot A), 5(Boot B)\neMMC: 0",
                "Activate LUN", "0");

            int lun;
            if (int.TryParse(input, out lun))
            {
                await _qualcommController.SetBootLunAsync(lun);
            }
        }

        private void StopCurrentOperation()
        {
            bool hasCancelled = false;

            // Get current tab
            int currentTab = tabs1.SelectedIndex;

            // tabPage2 (index 1) = Qualcomm

            // Cancel operation based on current tab
            switch (currentTab)
            {
                case 2: // Qualcomm
                    if (_qualcommController != null && _qualcommController.HasPendingOperation)
                    {
                        _qualcommController.CancelOperation();
                        AppendLog("[Qualcomm] Operation Cancelled", Color.Orange);
                        hasCancelled = true;
                    }
                    break;

                case 3: // MTK
                    if (MtkHasPendingOperation)
                    {
                        MtkCancelOperation();
                        hasCancelled = true;
                    }
                    break;

                case 4: // Spreadtrum
                    if (_spreadtrumController != null)
                    {
                        _spreadtrumController.CancelOperation();
                        hasCancelled = true;
                    }
                    break;

                case 0: // Fastboot (tabPage1)
                case 1: // Fastboot (tabPage3)
                    if (_fastbootController != null)
                    {
                        try
                        {
                            _fastbootController.CancelOperation();
                            AppendLog("[Fastboot] Operation Cancelled", Color.Orange);
                            hasCancelled = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Cancel Fastboot op exception: {ex.Message}");
                        }
                    }
                    break;
            }

            if (hasCancelled)
            {
                // Reset Progress Bar
                uiProcessBar1.Value = 0;
                uiProcessBar2.Value = 0;
                progress1.Value = 0;
                progress2.Value = 0;
            }
            else
            {
                AppendLog("No operations in progress", Color.Gray);
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

            AppendLog(selectAll ? "All partitions selected" : "Unchecked all", Color.Blue);
        }

        /// <summary>
        /// Double click partition item to select image
        /// </summary>
        private void QualcommPartitionDoubleClick()
        {
            if (listView2.SelectedItems.Count == 0) return;

            var item = listView2.SelectedItems[0];
            var partition = item.Tag as PartitionInfo;
            if (partition == null)
            {
                // If no Tag, try getting from name
                string partitionName = item.Text;
                if (string.IsNullOrEmpty(partitionName)) return;

                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = $"Select image file for partition {partitionName}";
                    ofd.Filter = $"Image File|{partitionName}.img;{partitionName}.bin;*.img;*.bin|All Files|*.*";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        // Update file path column (last col)
                        int lastCol = item.SubItems.Count - 1;
                        if (lastCol >= 0)
                        {
                            item.SubItems[lastCol].Text = ofd.FileName;
                            item.Checked = true; // Auto check
                            AppendLog($"Selected file for partition {partitionName}: {Path.GetFileName(ofd.FileName)}", Color.Blue);
                        }
                    }
                }
                return;
            }

            // Case with PartitionInfo
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"Select image file for partition {partition.Name}";
                ofd.Filter = $"Image File|{partition.Name}.img;{partition.Name}.bin;*.img;*.bin|All Files|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // Update file path column (last col)
                    int lastCol = item.SubItems.Count - 1;
                    if (lastCol >= 0)
                    {
                        item.SubItems[lastCol].Text = ofd.FileName;
                        item.Checked = true; // Auto-check
                        AppendLog($"Selected file for partition {partition.Name}: {Path.GetFileName(ofd.FileName)}", Color.Blue);
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
            // If triggered from dropdown selection, locate directly without updating dropdown
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

            // If search box is empty, reset all highlights
            if (string.IsNullOrEmpty(keyword))
            {
                ResetPartitionHighlights();
                _lastSearchKeyword = "";
                _searchMatches.Clear();
                _currentMatchIndex = 0;
                return;
            }

            // If keyword is same, jump to next match
            if (keyword == _lastSearchKeyword && _searchMatches.Count > 1)
            {
                JumpToNextMatch();
                return;
            }

            _lastSearchKeyword = keyword;
            _searchMatches.Clear();
            _currentMatchIndex = 0;

            // Collect matching partition names for dropdown suggestions
            var suggestions = new List<string>();

            listView2.BeginUpdate();

            foreach (ListViewItem item in listView2.Items)
            {
                string partitionName = item.Text?.ToLower() ?? "";
                string originalName = item.Text ?? "";
                bool isMatch = partitionName.Contains(keyword);

                if (isMatch)
                {
                    // Exact match use dark color, partial match use light color
                    item.BackColor = (partitionName == keyword) ? Color.Gold : Color.LightYellow;
                    _searchMatches.Add(item);

                    // Add to dropdown suggestions (Max 10)
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

            // Update dropdown suggestions list
            UpdateSearchSuggestions(suggestions);

            // Scroll to first match
            if (_searchMatches.Count > 0)
            {
                _searchMatches[0].Selected = true;
                _searchMatches[0].EnsureVisible();

                // Show match count (avoid redundant logs)
                if (_searchMatches.Count > 1)
                {
                    // Show in status bar or elsewhere to avoid screen flooding
                }
            }
            else if (keyword.Length >= 2)
            {
                // Only prompt not found if input is 2+ chars
                AppendLog($"Partition not found: {keyword}", Color.Orange);
            }
        }

        private void JumpToNextMatch()
        {
            if (_searchMatches.Count == 0) return;

            // Cancel current selection
            if (_currentMatchIndex < _searchMatches.Count)
            {
                _searchMatches[_currentMatchIndex].Selected = false;
            }

            // Jump to next
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
            // Save current input text
            string currentText = select4.Text;

            // Update dropdown items
            select4.Items.Clear();
            foreach (var name in suggestions)
            {
                select4.Items.Add(name);
            }

            // Restore input text (prevent clearing)
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

        /// <summary>
        /// Xiaomi Auth Token Event Handler - Pop up to show token for user to copy
        /// </summary>
        private void OnXiaomiAuthTokenRequired(string token)
        {
            // Ensure execution on UI thread
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(OnXiaomiAuthTokenRequired), token);
                return;
            }

            // Create popup
            using (var form = new Form())
            {
                form.Text = "Xiaomi Auth Token";
                form.Size = new Size(500, 220);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;


                // Token TextBox
                var textBox = new TextBox
                {
                    Text = token,
                    Location = new Point(15, 45),
                    Size = new Size(455, 60),
                    Multiline = true,
                    ReadOnly = true,
                    Font = new Font("Consolas", 9F),
                    ScrollBars = ScrollBars.Vertical
                };
                form.Controls.Add(textBox);

                // Copy Button
                var copyButton = new Button
                {
                    Text = "Copy Token",
                    Location = new Point(150, 115),
                    Size = new Size(90, 30),
                    Font = new Font("Microsoft YaHei UI", 9F)
                };
                copyButton.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(token);
                        MessageBox.Show("Token copied to clipboard", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Copy failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                form.Controls.Add(copyButton);

                // Close Button
                var closeButton = new Button
                {
                    Text = "Close",
                    Location = new Point(260, 115),
                    Size = new Size(90, 30),
                    Font = new Font("Microsoft YaHei UI", 9F),
                    DialogResult = DialogResult.Cancel
                };
                form.Controls.Add(closeButton);

                // Tip Info
                var tipLabel = new Label
                {
                    Text = "Tip: Token format is Base64 string starting with VQ",
                    Location = new Point(15, 155),
                    Size = new Size(460, 20),
                    ForeColor = Color.Gray,
                    Font = new Font("Microsoft YaHei UI", 8F)
                };
                form.Controls.Add(tipLabel);

                form.ShowDialog(this);
            }
        }

        private void InitializeUrlComboBox()
        {
            // Only keep verified available APIs
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
            // Clear preview control
            ClearImagePreview();

            // Set preview control properties
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.BackColor = Color.Black;

        }

        private void SaveOriginalPositions()
        {
            try
            {
                // Save original positions and sizes
                originalinput6Location = input6.Location;
                originalbutton4Location = button4.Location;
                originalcheckbox13Location = checkbox13.Location;
                originalinput7Location = input7.Location;
                originalinput9Location = input9.Location;
                originallistView2Location = listView2.Location;
                originallistView2Size = listView2.Size;
                originaluiGroupBox4Location = uiGroupBox4.Location;
                originaluiGroupBox4Size = uiGroupBox4.Size;

            }
            catch (Exception ex)
            {
                AppendLog($"Save original positions failed: {ex.Message}", Color.Red);
            }
        }

        // Log counter, used to limit number of entries
        private int _logEntryCount = 0;
        private readonly object _logLock = new object();

        private void AppendLog(string message, Color? color = null)
        {
            if (uiRichTextBox1.InvokeRequired)
            {
                uiRichTextBox1.BeginInvoke(new Action<string, Color?>(AppendLog), message, color);
                return;
            }

            // Color mapping for white background (Make colors clearer)
            Color logColor = MapLogColor(color ?? Color.Black);

            // Write to file
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] {message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log write failed: {ex.Message}");
            }

            // Check and limit log entry count (Reduce memory usage)
            int maxEntries = Common.PerformanceConfig.MaxLogEntries;
            lock (_logLock)
            {
                _logEntryCount++;
                if (_logEntryCount > maxEntries)
                {
                    // Clean up first half of logs
                    try
                    {
                        string[] lines = uiRichTextBox1.Text.Split('\n');
                        if (lines.Length > maxEntries / 2)
                        {
                            int removeCount = lines.Length - maxEntries / 2;
                            uiRichTextBox1.Text = string.Join("\n", lines.Skip(removeCount));
                            _logEntryCount = lines.Length - removeCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Log cleanup exception: {ex.Message}");
                    }
                }
            }

            // Show to UI (Reduce redraw)
            uiRichTextBox1.SuspendLayout();
            try
            {
                uiRichTextBox1.SelectionColor = logColor;
                uiRichTextBox1.AppendText(message + "\n");
                uiRichTextBox1.SelectionStart = uiRichTextBox1.Text.Length;
                uiRichTextBox1.ScrollToCaret();
            }
            finally
            {
                uiRichTextBox1.ResumeLayout();
            }
        }

        /// <summary>
        /// Map color to version suitable for white background (darker and clearer)
        /// </summary>
        private Color MapLogColor(Color originalColor)
        {
            // White background color scheme - Use darker colors
            if (originalColor == Color.White) return Color.Black;
            if (originalColor == Color.Blue) return Color.FromArgb(0, 80, 180);      // Dark Blue
            if (originalColor == Color.Gray) return Color.FromArgb(100, 100, 100);   // Dark Gray
            if (originalColor == Color.Green) return Color.FromArgb(0, 140, 0);      // Dark Green
            if (originalColor == Color.Red) return Color.FromArgb(200, 0, 0);        // Dark Red
            if (originalColor == Color.Orange) return Color.FromArgb(200, 120, 0);   // Dark Orange
            if (originalColor == Color.LimeGreen) return Color.FromArgb(0, 160, 0);  // Dark Yellow Green
            if (originalColor == Color.Cyan) return Color.FromArgb(0, 140, 160);     // Dark Cyan
            if (originalColor == Color.Yellow) return Color.FromArgb(180, 140, 0);   // Dark Yellow
            if (originalColor == Color.Magenta) return Color.FromArgb(160, 0, 160);  // Dark Purple

            // Others remain unchanged
            return originalColor;
        }

        /// <summary>
        /// Detailed Debug Log - Write to file only, not shown in UI
        /// </summary>
        private void AppendLogDetail(string message)
        {
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] [DEBUG] {message}" + Environment.NewLine);
            }
            catch { /* Ignore debug log write failure */ }
        }

        /// <summary>
        /// Initialize Log System
        /// </summary>
        private void InitializeLogSystem()
        {
            try
            {
                // Use Logs folder under application directory
                string logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(logFolderPath))
                {
                    Directory.CreateDirectory(logFolderPath);
                }

                // Clean old logs from 7 days ago
                CleanOldLogs(logFolderPath, 7);

                string logFileName = $"{DateTime.Now:yyyy-MM-dd_HH.mm.ss}_log.txt";
                logFilePath = Path.Combine(logFolderPath, logFileName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log init failed: {ex.Message}");
                // Use temp directory if log init failed
                logFilePath = Path.Combine(Path.GetTempPath(), $"MultiFlash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
        }

        /// <summary>
        /// Clean old logs before specified days
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
                    try { File.Delete(file); } catch { /* Ignore delete failed */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clean old logs exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Write log file header info
        /// </summary>
        private void WriteLogHeader(string sysInfo)
        {
            try
            {
                var header = new StringBuilder();
                header.AppendLine($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine($"System: {sysInfo}");
                header.AppendLine($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                header.AppendLine();

                File.WriteAllText(logFilePath, header.ToString());
            }
            catch { /* Ignore log header write failure */ }
        }

        /// <summary>
        /// View Log Menu Click Event - Open log folder and select current log
        /// </summary>
        private void ViewLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string logFolder = Path.GetDirectoryName(logFilePath);

                if (!Directory.Exists(logFolder))
                {
                    Directory.CreateDirectory(logFolder);
                }

                // If current log file exists, open with Explorer and select it
                if (File.Exists(logFilePath))
                {
                    // Use /select to open explorer and select file
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logFilePath}\"");
                    AppendLog($"Opened log folder: {logFolder}", Color.Blue);
                }
                else
                {
                    // File not exists, open folder directly
                    System.Diagnostics.Process.Start("explorer.exe", logFolder);
                    AppendLog($"Opened log folder: {logFolder}", Color.Blue);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Open log failed: {ex.Message}", Color.Red);
                MessageBox.Show($"Cannot open log folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Select Local Image";
            openFileDialog.Filter = "Image File|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*";
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                selectedLocalImagePath = openFileDialog.FileName;
                AppendLog($"Selected local file: {selectedLocalImagePath}", Color.Green);

                // Use async load to avoid UI freeze
                Task.Run(() => LoadLocalImage(selectedLocalImagePath));
            }
        }

        private void LoadLocalImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    SafeInvoke(() => AppendLog("File not exists", Color.Red));
                    return;
                }

                // Check file size
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length > 50 * 1024 * 1024) // 50MB Limit
                {
                    SafeInvoke(() => AppendLog($"File too large（{fi.Length / 1024 / 1024}MB），please select image < 50MB", Color.Red));
                    return;
                }

                // Method 1: Use ultra low quality load
                using (Bitmap original = LoadImageWithLowQuality(filePath))
                {
                    if (original != null)
                    {
                        // Create thumbnail fitting form size
                        Size targetSize = Size.Empty;
                        SafeInvoke(() => targetSize = this.ClientSize);
                        if (targetSize.IsEmpty) return;

                        using (Bitmap resized = ResizeImageToFitWithLowMemory(original, targetSize))
                        {
                            if (resized != null)
                            {
                                SafeInvoke(() =>
                                {
                                    // Release old image
                                    if (this.BackgroundImage != null)
                                    {
                                        this.BackgroundImage.Dispose();
                                        this.BackgroundImage = null;
                                    }

                                    // Set new image
                                    this.BackgroundImage = resized.Clone() as Bitmap;
                                    this.BackgroundImageLayout = ImageLayout.Stretch;

                                    // Add to preview
                                    AddImageToPreview(resized.Clone() as Image, Path.GetFileName(filePath));

                                    AppendLog($"Local image set success ({resized.Width}x{resized.Height})", Color.Green);
                                });
                            }
                        }
                    }
                    else
                    {
                        SafeInvoke(() => AppendLog("Cannot load image, file may be corrupted", Color.Red));
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                SafeInvoke(() =>
                {
                    AppendLog("Out of memory, please try restart app", Color.Red);
                    AppendLog("Suggest: Close other apps to release memory", Color.Yellow);
                });
            }
            catch (Exception ex)
            {
                SafeInvoke(() => AppendLog($"Load image failed: {ex.Message}", Color.Red));
            }
        }

        private Bitmap LoadImageWithLowQuality(string filePath)
        {
            try
            {
                // Load image using minimum memory
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Read image info but don't load all data
                    using (Image img = Image.FromStream(fs, false, false))
                    {
                        // If image is large, create thumbnail first
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
                            // Return new Bitmap directly
                            return new Bitmap(img);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Load image failed: {ex.Message}", Color.Red);
                return null;
            }
        }

        private Bitmap ResizeImageToFitWithLowMemory(Image original, Size targetSize)
        {
            try
            {
                // Limit preview image size
                int maxWidth = Math.Min(800, targetSize.Width);
                int maxHeight = Math.Min(600, targetSize.Height);

                int newWidth, newHeight;

                // Calculate new size
                double ratioX = (double)maxWidth / original.Width;
                double ratioY = (double)maxHeight / original.Height;
                double ratio = Math.Min(ratioX, ratioY);

                newWidth = (int)(original.Width * ratio);
                newHeight = (int)(original.Height * ratio);

                // Ensure min size
                newWidth = Math.Max(100, newWidth);
                newHeight = Math.Max(100, newHeight);

                // Create new Bitmap
                Bitmap result = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);

                using (Graphics g = Graphics.FromImage(result))
                {
                    // Use lowest quality settings to save memory
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
                AppendLog($"Resize image failed: {ex.Message}", Color.Red);
                return null;
            }
        }

        private async void Button3_Click(object sender, EventArgs e)
        {
            string url = uiComboBox3.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                AppendLog("Please input or select wallpaper URL", Color.Red);
                return;
            }

            // Clean URL
            url = url.Trim('`', '\'');
            AppendLog($"Getting wallpaper from URL: {url}", Color.Blue);

            try
            {
                // Use simplest way
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15); // Increase timeout
                    client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/*"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));

                    // Show loading hint
                    AppendLog("Downloading image...", Color.Blue);

                    byte[] imageData = null;

                    // Handle some APIs specially
                    if (url.Contains("picsum.photos"))
                    {
                        // Add random param to avoid cache
                        url += $"?random={DateTime.Now.Ticks}";
                    }
                    else if (url.Contains("loliapi.com"))
                    {
                        // Handle loliapi.com API response specially...
                        AppendLog("Processing loliapi.com API response...", Color.Blue);
                        // Note: loliapi.com returns binary data directly, no JSON param needed
                    }

                    // Send request and get response
                    using (HttpResponseMessage response = await client.GetAsync(url))
                    {
                        response.EnsureSuccessStatusCode();

                        // Check response content type
                        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                        AppendLog($"Response Content Type: {contentType}", Color.Blue);

                        // Check if it is image
                        if (contentType.StartsWith("image/"))
                        {
                            imageData = await response.Content.ReadAsByteArrayAsync();
                            AppendLog($"Downloaded image size: {imageData.Length} bytes", Color.Blue);
                        }
                        else if (contentType.Contains("json"))
                        {
                            // Handle JSON response
                            string jsonContent = await response.Content.ReadAsStringAsync();
                            AppendLog($"JSON response length: {jsonContent.Length}", Color.Blue);

                            // Try to extract image URL from JSON
                            string imageUrl = ExtractImageUrlFromJson(jsonContent);
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                AppendLog($"Extracted image URL from JSON: {imageUrl}", Color.Blue);
                                // Download extracted image
                                using (HttpResponseMessage imageResponse = await client.GetAsync(imageUrl))
                                {
                                    imageResponse.EnsureSuccessStatusCode();
                                    imageData = await imageResponse.Content.ReadAsByteArrayAsync();
                                    AppendLog($"Downloaded image size: {imageData.Length} bytes", Color.Blue);
                                }
                            }
                            else
                            {
                                AppendLog("Cannot extract image URL from JSON response", Color.Red);
                                AppendLog($"JSON Content: {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}...", Color.Yellow);
                                return;
                            }
                        }
                        else
                        {
                            // Maybe redirect or HTML response
                            string content = await response.Content.ReadAsStringAsync();
                            AppendLog($"Response is not image, length: {content.Length}", Color.Yellow);

                            // Try to extract image URL from HTML
                            string imageUrl = ExtractImageUrlFromHtml(content);
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                AppendLog($"Extracted image URL from HTML: {imageUrl}", Color.Blue);
                                // Download extracted image
                                using (HttpResponseMessage imageResponse = await client.GetAsync(imageUrl))
                                {
                                    imageResponse.EnsureSuccessStatusCode();
                                    imageData = await imageResponse.Content.ReadAsByteArrayAsync();
                                    AppendLog($"Downloaded image size: {imageData.Length} bytes", Color.Blue);
                                }
                            }
                            else
                            {
                                AppendLog("Cannot extract image URL from response", Color.Red);
                                // Show partial response for debug
                                if (content.Length > 0)
                                {
                                    AppendLog($"Response preview: {content.Substring(0, Math.Min(500, content.Length))}...", Color.Yellow);
                                }
                                return;
                            }
                        }
                    }

                    if (imageData == null || imageData.Length < 1000)
                    {
                        AppendLog("Downloaded data invalid", Color.Red);
                        return;
                    }

                    // Load image directly from memory, avoid extension issues
                    LoadAndSetBackgroundFromMemory(imageData, url);
                }
            }
            catch (HttpRequestException ex)
            {
                AppendLog($"Network request failed: {ex.Message}", Color.Red);
                AppendLog("Please check network or try other URL", Color.Yellow);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("参数无效") || ex.Message.Contains("Invalid parameter"))
                {
                    AppendLog("Image format might not be supported, try other URL", Color.Yellow);
                    // AppendLog($"Error Detail: {ex.Message}", Color.Red);
                }
                else
                {
                    AppendLog($"Get wallpaper failed: {ex.Message}", Color.Red);
                    //    AppendLog($"Error details: {ex.ToString()}", Color.Yellow);
                }
            }
        }

        private string ExtractImageUrlFromJson(string jsonContent)
        {
            try
            {
                // Try simple JSON parsing
                jsonContent = jsonContent.Trim();

                // Handle common JSON format
                if (jsonContent.StartsWith("{") && jsonContent.EndsWith("}"))
                {
                    // Try extract url field
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

                    // Try extract data field
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
                    // Handle array format
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

                // Try regex extraction
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
                AppendLog($"Parse JSON failed: {ex.Message}", Color.Red);
                return null;
            }
        }

        private string ExtractImageUrlFromHtml(string html)
        {
            try
            {
                // Simple Regex to extract image URL
                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                    @"https?://[^\s""'<>]+?\.(?:jpg|jpeg|png|gif|webp)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                System.Text.RegularExpressions.Match match = regex.Match(html);
                if (match.Success)
                {
                    return match.Value;
                }

                // Try extract all possible URLs
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
                AppendLog($"Extract Image URL failed: {ex.Message}", Color.Red);
                return null;
            }
        }

        private void LoadAndSetBackgroundFromMemory(byte[] imageData, string sourceUrl)
        {
            try
            {
                // Check if data is valid
                if (imageData == null || imageData.Length < 100)
                {
                    AppendLog("Image data invalid or too small", Color.Red);
                    return;
                }

                // Check if valid image data (header)
                string fileHeader = BitConverter.ToString(imageData, 0, Math.Min(8, imageData.Length)).ToLower();
                bool isImage = false;

                // Check common image headers
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
                    AppendLog("File is not a valid image format", Color.Red);
                    AppendLog($"File Header: {fileHeader}", Color.Yellow);
                    return;
                }

                // Special handling for WebP
                bool isWebP = fileHeader.StartsWith("52-49-46-46");
                if (isWebP)
                {
                    AppendLog("WebP format detected, using special handling...", Color.Blue);
                }

                // Create Memory Stream
                using (MemoryStream ms = new MemoryStream(imageData))
                {
                    ms.Position = 0; // Ensure at start

                    try
                    {
                        using (Image original = Image.FromStream(ms, false, false))
                        {
                            if (original != null)
                            {
                                AppendLog($"Image loaded successfully, size: {original.Width}x{original.Height}", Color.Blue);

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
                                AppendLog("Downloaded file is not a valid image", Color.Red);
                            }
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("参数无效") || ex.Message.Contains("Invalid parameter"))
                    {
                        // Handle "Invalid Parameter", mostly WebP support issue
                        AppendLog("Image format might not be supported, trying conversion...", Color.Yellow);

                        // Save as temp file and reload
                        string tempFile = Path.GetTempFileName() + (isWebP ? ".webp" : ".jpg");
                        try
                        {
                            File.WriteAllBytes(tempFile, imageData);
                            //   AppendLog($"Temp file saved: {tempFile}", Color.Blue);

                            // Try loading with different method
                            using (Image original = Image.FromFile(tempFile))
                            {
                                if (original != null)
                                {
                                    //  AppendLog($"Successfully loaded image from file, size: {original.Width}x{original.Height}", Color.Blue);

                                    Size targetSize = this.ClientSize;
                                    using (Bitmap resized = ResizeImageToFitWithLowMemory(original, targetSize))
                                    {
                                        if (resized != null)
                                        {
                                            // Release old image
                                            if (this.BackgroundImage != null)
                                            {
                                                this.BackgroundImage.Dispose();
                                                this.BackgroundImage = null;
                                            }

                                            // Set new image
                                            this.BackgroundImage = resized.Clone() as Bitmap;
                                            this.BackgroundImageLayout = ImageLayout.Stretch;

                                            // Add to preview
                                            AddImageToPreview(resized.Clone() as Image, "Online Image");

                                            //   AppendLog($"Online image set success ({resized.Width}x{resized.Height})", Color.Green);

                                            // Add to history
                                            if (!urlHistory.Contains(sourceUrl))
                                            {
                                                urlHistory.Add(sourceUrl);
                                            }

                                            // Update ComboBox
                                            UpdateUrlComboBox(sourceUrl);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Try GDI+ other methods
                            try
                            {
                                //    AppendLog("Trying GDI+ direct draw...", Color.Yellow);

                                // Create new Bitmap and draw manually
                                using (Bitmap tempBmp = new Bitmap(800, 600))
                                using (Graphics g = Graphics.FromImage(tempBmp))
                                {
                                    g.Clear(Color.White);

                                    // Try WebClient download and draw
                                    AppendLog("Image Load Failed", Color.Yellow);
                                    AppendLog("Please try other URL", Color.Yellow);
                                }
                            }
                            catch (Exception)
                            {
                                AppendLog("Cannot process this image format", Color.Red);
                            }
                        }
                        finally
                        {
                            // Clean temp file
                            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* Ignore delete failed */ }
                        }
                    }
                }

                // GC
                GC.Collect();
            }
            catch (OutOfMemoryException)
            {
                AppendLog("Out of memory, cannot process image", Color.Red);
            }
            catch (Exception ex)
            {
                AppendLog($"Process image failed: {ex.Message}", Color.Red);
                // Print detailed error
                //   AppendLog($"Error Detail: {ex.ToString()}", Color.Yellow);
            }
        }

        private void AddImageToPreview(Image image, string description)
        {
            if (image == null) return;

            try
            {
                // Limit preview images
                if (previewImages.Count >= MAX_PREVIEW_IMAGES)
                {
                    // Remove oldest
                    Image oldImage = previewImages[0];
                    previewImages.RemoveAt(0);
                    oldImage.Dispose();
                }

                // Add new preview
                previewImages.Add(image);

                // Update preview control
                UpdateImagePreview();

                //      AppendLog($"Added to preview: {description}", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"Update preview failed: {ex.Message}", Color.Red);
            }
        }

        private void UpdateImagePreview()
        {
            if (previewImages.Count == 0)
            {
                // Show default image or clear
                pictureBox1.Image = null;
                pictureBox1.Invalidate();
                return;
            }

            try
            {
                // Show latest preview image
                Image latestImage = previewImages[previewImages.Count - 1];
                pictureBox1.Image = latestImage;

                // Update preview label
                UpdatePreviewLabel();
            }
            catch (Exception ex)
            {
                AppendLog($"Show preview failed: {ex.Message}", Color.Red);
            }
        }

        private void UpdatePreviewLabel()
        {
            if (previewImages.Count > 0 && label3 != null)
            {
                Image currentImage = pictureBox1.Image;
                if (currentImage != null)
                {
                    string language = uiComboBox4.SelectedItem?.ToString() ?? "Chinese";
                    bool isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);

                    if (isEnglish)
                    {
                        label3.Text = $"Preview: {currentImage.Width}×{currentImage.Height} ({previewImages.Count} images)";
                    }
                    else
                    {
                        label3.Text = $"Preview: {currentImage.Width}×{currentImage.Height} ({previewImages.Count} images)";
                    }
                }
            }
        }

        private void ClearImagePreview()
        {
            try
            {
                // Clear preview control
                pictureBox1.Image = null;

                // Release all preview images
                foreach (Image img in previewImages)
                {
                    img?.Dispose();
                }
                previewImages.Clear();

                // Reset label
                label3.Text = "Preview";
            }
            catch (Exception ex)
            {
                AppendLog($"Clear preview failed: {ex.Message}", Color.Red);
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
            string selectedLanguage = uiComboBox4.SelectedItem?.ToString() ?? "Chinese";
            SwitchLanguage(selectedLanguage);
        }

        private void SwitchLanguage(string language)
        {
            isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);

            if (isEnglish)
            {
                // English Interface
                tabPage6.Text = "Settings";
                label1.Text = "Background Blur";
                label2.Text = "Wallpaper";
                label3.Text = "Preview";
                label4.Text = "Language";
                button2.Text = "Local Wallpaper";
                button3.Text = "Apply";
                uiComboBox3.Watermark = "URL";

                // Update other tabs
                tabPage2.Text = "Home";
                tabPage2.Text = "Qualcomm";
                tabPage4.Text = "MTK";
                tabPage5.Text = "Spreadtrum";

                // Update menu
                快捷重启ToolStripMenuItem.Text = "Quick Restart";
                toolStripMenuItem1.Text = "EDL Operations";
                其他ToolStripMenuItem.Text = "Others";

                // Update buttons
                uiButton2.Text = "Erase Partition";
                uiButton3.Text = "Write Partition";
                uiButton4.Text = "Read Partition";
                uiButton5.Text = "Read GPT";
                select4.PlaceholderText = "Find Partition";
            }
            else
            {
                // Chinese Interface
                tabPage6.Text = "Settings";
                label1.Text = "Background Blur";
                label2.Text = "Wallpaper";
                label3.Text = "Preview";
                label4.Text = "Language";
                button2.Text = "Local Wallpaper";
                button3.Text = "Apply";
                uiComboBox3.Watermark = "URL";

                // Update other tabs
                tabPage2.Text = "Home";
                tabPage2.Text = "Qualcomm";
                tabPage4.Text = "MTK";
                tabPage5.Text = "Spreadtrum";

                // Update menu
                快捷重启ToolStripMenuItem.Text = "Quick Restart";
                toolStripMenuItem1.Text = "EDL Operations";
                其他ToolStripMenuItem.Text = "Others";

                // Update buttons
                uiButton2.Text = "Erase Partition";
                uiButton3.Text = "Write Partition";
                uiButton4.Text = "Read Partition";
                uiButton5.Text = "Read GPT";
                select4.PlaceholderText = "Find Partition";
            }

            // 更新预览标签
            UpdatePreviewLabel();

            AppendLog($"Interface language switched to: {language}", Color.Green);
        }

        private void Checkbox17_CheckedChanged(object sender, EventArgs e)
        {
            if (checkbox17.Checked)
            {
                // Auto uncheck checkbox19
                checkbox19.Checked = false;

                // Check if already compact layout (by check input7 visibility)
                if (input7.Visible)
                {
                    // If input7 visible, means default layout, need apply compact layout
                    ApplyCompactLayout();
                }
                // If input7 invisible, means already compact
            }

            // Update Auth mode
            UpdateAuthMode();
        }

        private void Checkbox19_CheckedChanged(object sender, EventArgs e)
        {
            if (checkbox19.Checked)
            {
                // Auto uncheck checkbox17
                checkbox17.Checked = false;

                RestoreOriginalLayout();
            }
            else
            {
                ApplyCompactLayout();
            }

            // Update Auth mode
            UpdateAuthMode();
        }

        private void ApplyCompactLayout()
        {
            try
            {
                // Suspend layout update, reduce flickering
                this.SuspendLayout();
                uiGroupBox4.SuspendLayout();
                listView2.SuspendLayout();

                // Hide input9, input7
                input9.Visible = false;
                input7.Visible = false;

                // Move up input6, button4 to position of input7 and input9
                input6.Location = new Point(input6.Location.X, input7.Location.Y);
                button4.Location = new Point(button4.Location.X, input9.Location.Y);

                // Move up checkbox13 to fixed pos
                checkbox13.Location = new Point(6, 25);

                // Move up and resize uiGroupBox4 and listView2
                const int VERTICAL_ADJUSTMENT = 38; // Constant adjustment
                uiGroupBox4.Location = new Point(uiGroupBox4.Location.X, uiGroupBox4.Location.Y - VERTICAL_ADJUSTMENT);
                uiGroupBox4.Size = new Size(uiGroupBox4.Size.Width, uiGroupBox4.Size.Height + VERTICAL_ADJUSTMENT);
                listView2.Size = new Size(listView2.Size.Width, listView2.Size.Height + VERTICAL_ADJUSTMENT);

                // Resume layout
                listView2.ResumeLayout(false);
                uiGroupBox4.ResumeLayout(false);
                this.ResumeLayout(false);
                this.PerformLayout();

            }
            catch (Exception ex)
            {
                AppendLog($"Apply layout failed: {ex.Message}", Color.Red);
            }
        }

        private void RestoreOriginalLayout()
        {
            try
            {
                // Suspend layout
                this.SuspendLayout();
                uiGroupBox4.SuspendLayout();
                listView2.SuspendLayout();

                // Restore input9, input7 visibility
                input9.Visible = true;
                input7.Visible = true;

                // Restore original location
                input6.Location = originalinput6Location;
                button4.Location = originalbutton4Location;
                // Restore checkbox13 to fixed pos (6, 25)
                checkbox13.Location = new Point(6, 25);

                // Restore original size and location
                uiGroupBox4.Location = originaluiGroupBox4Location;
                uiGroupBox4.Size = originaluiGroupBox4Size;
                listView2.Size = originallistView2Size;

                // Resume layout
                listView2.ResumeLayout(false);
                uiGroupBox4.ResumeLayout(false);
                this.ResumeLayout(false);
                this.PerformLayout();

            }
            catch (Exception ex)
            {
                AppendLog($"Restore layout failed: {ex.Message}", Color.Red);
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
            bool isCloudMatch = selectedItem.Contains("Cloud Auto Match");
            bool isLocalSelect = selectedItem.Contains("Local Select");

            // Handle Cloud Auto Match
            if (isCloudMatch)
            {
                // Disable custom loader input
                input9.Enabled = false;
                input8.Enabled = false;
                input7.Enabled = false;

                // Show Cloud Match Hint
                input8.Text = "[Cloud] Auto Match Loader";
                input9.Text = "";
                input7.Text = "";

                // Reset Auth Mode (Cloud detects auto)
                checkbox17.Checked = false;
                checkbox19.Checked = false;
                _authMode = "none";
            }
            // Handle Local Select
            else if (isLocalSelect)
            {
                // Enable custom loader input
                input9.Enabled = true;
                input8.Enabled = true;
                input7.Enabled = true;

                // Clear inputs (Show placeholder)
                input8.Text = "";
                input9.Text = "";
                input7.Text = "";

                // Reset Auth Mode
                checkbox17.Checked = false;
                checkbox19.Checked = false;
                _authMode = "none";
            }
        }

        /// <summary>
        /// Extract Loader ID from EDL selection
        /// </summary>
        private string ExtractEdlLoaderIdFromSelection(string selection)
        {
            // "[Huawei] 888 (Generic)" -> "Huawei_888"
            // "[Meizu] Meizu21Pro" -> "Meizu_Meizu21Pro"
            if (string.IsNullOrEmpty(selection)) return "";

            // Extract brand and name
            int bracketEnd = selection.IndexOf(']');
            if (bracketEnd < 0) return "";

            string brand = selection.Substring(1, bracketEnd - 1);
            string rest = selection.Substring(bracketEnd + 1).Trim();

            // Handle Generic loader
            if (rest.EndsWith("(Generic)") || rest.EndsWith("(通用)"))
            {
                string chip = rest.Replace("(Generic)", "").Replace("(通用)", "").Trim();
                return $"{brand}_{chip}";
            }

            // Handle Specific loader
            // Extract model from rest
            string model = rest.Replace($"{brand} ", "").Trim();
            // Remove Chip info (Parenthesis)
            int parenIndex = model.IndexOf('(');
            if (parenIndex > 0)
            {
                model = model.Substring(0, parenIndex).Trim();
            }

            return $"{brand}_{model}";
        }

        /// <summary>
        /// Extract Platform Name from VIP Selection
        /// </summary>
        private string ExtractPlatformFromVipSelection(string selection)
        {
            // "[VIP] SM8550 - Snapdragon 8Gen2/8+Gen2" -> "SM8550"
            if (string.IsNullOrEmpty(selection)) return "";

            // Remove "[VIP] " prefix
            string trimmed = selection.Replace("[VIP] ", "");

            // Get part before " - "
            int dashIndex = trimmed.IndexOf(" - ");
            if (dashIndex > 0)
            {
                return trimmed.Substring(0, dashIndex).Trim();
            }

            return trimmed.Trim();
        }

        #region EDL Loader Initialization

        // Cache EDL Loader items
        private List<string> _edlLoaderItems = null;

        /// <summary>
        /// Initialize EDL Loader List - Cloud Auto Match + Local Select
        /// </summary>
        private void InitializeEdlLoaderList()
        {
            try
            {
                // Clear default items in Designer
                select3.Items.Clear();

                // Add options
                select3.Items.Add("☁️ Cloud Auto Match");
                select3.Items.Add("📁 Local Select");

                // Set default to Cloud Auto Match
                select3.SelectedIndex = 0;

                // Initialize Cloud Service
                InitializeCloudLoaderService();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Load Loader List Exception: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Initialize Cloud Loader Service
        /// </summary>
        private void InitializeCloudLoaderService()
        {
            var cloudService = LoveAlways.Qualcomm.Services.CloudLoaderService.Instance;
            cloudService.SetLogger(
                msg => AppendLog(msg, Color.Cyan),
                msg => AppendLog(msg, Color.Gray)
            );
            // Config API Address (Production)
            // cloudService.ApiBase = "https://api.xiriacg.top/api";
        }

        /// <summary>
        /// Build EDL Loader Items (Deprecated - Use Cloud Match)
        /// </summary>
        [Obsolete("Use Cloud Auto Match instead of Local PAK")]
        private List<string> BuildEdlLoaderItems()
        {
            // No longer build local PAK list, fully use cloud match
            return new List<string>();
        }

        /// <summary>
        /// Get Brand Display Name
        /// </summary>
        private string GetBrandDisplayName(string brand)
        {
            switch (brand.ToLower())
            {
                case "huawei": return "Huawei/Honor";
                case "zte": return "ZTE/Nubia/RedMagic";
                case "xiaomi": return "Xiaomi/Redmi";
                case "blackshark": return "BlackShark";
                case "vivo": return "vivo/iQOO";
                case "meizu": return "Meizu";
                case "lenovo": return "Lenovo/Motorola";
                case "samsung": return "Samsung";
                case "nothing": return "Nothing";
                case "rog": return "Asus ROG";
                case "lg": return "LG";
                case "smartisan": return "Smartisan";
                case "xtc": return "XTC";
                case "360": return "360";
                case "bbk": return "BBK";
                case "royole": return "Royole";
                case "oplus": return "OPPO/OnePlus/Realme";
                default: return brand;
            }
        }

        #endregion

        #region Fastboot Module

        private void InitializeFastbootModule()
        {
            try
            {
                // Set fastboot.exe path
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastboot.exe");
                FastbootCommand.SetFastbootPath(fastbootPath);

                // Create Fastboot UI Controller
                _fastbootController = new FastbootUIController(
                    (msg, color) => AppendLog(msg, color),
                    msg => AppendLogDetail(msg));

                // Set listView5 checkable
                listView5.MultiSelect = true;
                listView5.CheckBoxes = true;
                listView5.FullRowSelect = true;

                // Bind Controls - Fastboot Controls on tabPage3
                // Note: Device Info Labels (in uiGroupBox3) shared with Qualcomm module, updated by tab switch
                _fastbootController.BindControls(
                    deviceComboBox: uiComboBox1,          // Use global port combo box (Shared)
                    partitionListView: listView5,         // Partition List
                    progressBar: uiProcessBar1,           // Total Progress Bar (Shared)
                    subProgressBar: uiProcessBar2,        // Sub Progress Bar (Shared)
                    commandComboBox: uiComboBox2,         // Fast Command Combo (device, unlock, etc)
                    payloadTextBox: uiTextBox1,           // Payload Path
                    outputPathTextBox: input1,            // Output Path
                                                          // Device Info Labels (uiGroupBox3 - Shared)
                    brandLabel: uiLabel9,                 // Brand
                    chipLabel: uiLabel11,                 // Chip
                    modelLabel: uiLabel3,                 // Model
                    serialLabel: uiLabel10,               // Serial
                    storageLabel: uiLabel13,              // Storage
                    unlockLabel: uiLabel14,               // Unlock State
                    slotLabel: uiLabel12,                 // Slot (Reuse OTA Version Label)
                                                          // Time/Speed/Operation Labels (Shared)
                    timeLabel: uiLabel6,                  // Time
                    speedLabel: uiLabel7,                 // Speed
                    operationLabel: uiLabel8,             // Current Operation
                    deviceCountLabel: uiLabel4,           // Device Count (Reuse)
                                                          // Checkbox Controls
                    autoRebootCheckbox: checkbox44,       // Auto Reboot
                    switchSlotCheckbox: checkbox41,       // Switch Slot A
                    eraseGoogleLockCheckbox: checkbox43,  // Erase FRP
                    keepDataCheckbox: checkbox50,         // Keep Data
                    fbdFlashCheckbox: checkbox45,         // FBD Flash
                    unlockBlCheckbox: checkbox22,         // Unlock BL
                    lockBlCheckbox: checkbox21            // Lock BL
                );

                // ========== tabPage3 Fastboot Page Button Events ==========

                // uiButton11 = Parse Payload (Local File or Cloud URL)
                uiButton11.Click += (s, e) => FastbootOpenPayloadDialog();

                // uiButton18 = Read GPT (Also Read Device Info)
                uiButton18.Click += async (s, e) => await FastbootReadPartitionTableWithInfoAsync();

                // uiButton19 = Extract Image (Support Extract from Payload, Custom or All)
                uiButton19.Click += async (s, e) => await FastbootExtractPartitionsWithOptionsAsync();

                // uiButton20 = Write Partition
                uiButton20.Click += async (s, e) => await FastbootFlashPartitionsAsync();

                // uiButton21 = Erase Partition
                uiButton21.Click += async (s, e) => await FastbootErasePartitionsAsync();

                // uiButton22 = Repair FBD (To be implemented)
                uiButton22.Click += (s, e) => AppendLog("FBD Repair Feature WIP...", Color.Orange);

                // uiButton10 = Execute (Execute Flash Script or Quick Command)
                uiButton10.Click += async (s, e) => await FastbootExecuteAsync();

                // button8 = Browse (Select Flash Script)
                button8.Click += (s, e) => FastbootSelectScript();

                // button9 = Browse (Left Click Select File, Right Click Select Folder)
                button9.Click += (s, e) => FastbootSelectPayloadFile();
                button9.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                        FastbootSelectPayloadFolder();
                };

                // uiTextBox1 = Payload/URL Input, Support Enter Key to Parse
                uiTextBox1.Watermark = "Select Payload/Folder or Input Cloud Link (Right Click Browse = Select Folder)";
                uiTextBox1.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        FastbootParsePayloadInput(uiTextBox1.Text);
                    }
                };

                // Modify Button Text
                uiButton11.Text = "Cloud Parse";
                uiButton18.Text = "Read GPT";
                uiButton19.Text = "Extract";

                // checkbox22 = 解锁BL (手动操作时执行，脚本执行时作为标志)
                // checkbox21 = 锁定BL (手动操作时执行，脚本执行时作为标志)
                // 注意：不再自动执行，而是在刷机完成后根据选项执行

                // checkbox41 = 切换A槽 (刷写完成后执行)
                // checkbox43 = 擦除谷歌锁 (刷写完成后执行)
                // 这些复选框只作为标记，不立即执行操作

                // checkbox42 = 分区全选
                checkbox42.CheckedChanged += (s, e) => FastbootSelectAllPartitions(checkbox42.Checked);

                // listView5 Double click to select image file
                listView5.DoubleClick += (s, e) => FastbootPartitionDoubleClick();

                // select5 = Partition Search
                select5.TextChanged += (s, e) => FastbootSearchPartition();
                select5.SelectedIndexChanged += (s, e) => { _fbIsSelectingFromDropdown = true; };

                // Note: Do not start Fastboot Device Monitoring at initialization
                // Only start when user swtich to Fastboot tab
                // Avoid overwriting Qualcomm port list
                // _fastbootController.StartDeviceMonitoring();

                // Bind Tab Change Event - Update Right Device Info
                tabs1.SelectedIndexChanged += OnTabPageChanged;

                AppendLog("[Fastboot] Module Initialized", Color.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"[Fastboot] Module Init Failed: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Tab Changed Event - Switch Device Info and Device List
        /// </summary>
        private void OnTabPageChanged(object sender, EventArgs e)
        {
            try
            {
                ClearLogs();

                // Get selected tab
                int selectedIndex = tabs1.SelectedIndex;
                var selectedTab = tabs1.Pages[selectedIndex];

                // tabPage3 is Fastboot
                if (selectedTab == tabPage3)
                {
                    // Switch to Fastboot Tab
                    _isOnFastbootTab = true;

                    // Stop other monitors
                    _portRefreshTimer?.Stop();
                    _mtkController?.StopPortMonitoring();
                    ClearLogs();

                    _spreadtrumController?.StopDeviceMonitor();
                    ClearLogs();

                    // Update Fastboot Device Info
                    if (_fastbootController != null)
                    {
                        // Start Fastboot Monitor
                        _fastbootController.StartDeviceMonitoring();
                        _fastbootController.UpdateDeviceInfoLabels();

                        // Update Device Count
                        int deviceCount = _fastbootController.DeviceCount;
                        if (deviceCount == 0)
                        {
                            uiLabel4.Text = "FB Dev: 0";
                        }
                        else if (deviceCount == 1)
                        {
                            uiLabel4.Text = $"FB Dev: Connected";
                        }
                        else
                        {
                            uiLabel4.Text = $"FB Dev: {deviceCount}";
                        }
                    }
                }
                // tabPage2 is Qualcomm (EDL)
                else if (selectedTab == tabPage2)
                {
                    // Switch to Qualcomm Tab
                    _isOnFastbootTab = false;

                    // Stop other monitors
                    _fastbootController?.StopDeviceMonitoring();
                    ClearLogs();

                    _mtkController?.StopPortMonitoring();
                    ClearLogs();

                    _spreadtrumController?.StopDeviceMonitor();
                    ClearLogs();

                    // Start Qualcomm Port Refresh
                    _portRefreshTimer?.Start();

                    // Refresh Qualcomm Ports to ComboBox
                    _qualcommController?.RefreshPorts(silent: true);

                    // Restore Qualcomm Device Info
                    if (_qualcommController != null && _qualcommController.IsConnected)
                    {
                        // Qualcomm controller auto updates, no extra action needed
                    }
                    else
                    {
                        // Reset to Wait Connection
                        uiLabel9.Text = "Brand: Waiting";
                        uiLabel11.Text = "Chip: Waiting";
                        uiLabel3.Text = "Model: Waiting";
                        uiLabel10.Text = "Serial: Waiting";
                        uiLabel13.Text = "Storage: Waiting";
                        uiLabel14.Text = "State: Waiting";
                        uiLabel12.Text = "OTA: Waiting";
                    }
                }
                // tabPage4 is MTK
                else if (selectedTab == tabPage4)
                {
                    // Switch to MTK Tab
                    _isOnFastbootTab = false;

                    // Stop other monitors
                    _fastbootController?.StopDeviceMonitoring();
                    ClearLogs();

                    _portRefreshTimer?.Stop();

                    _spreadtrumController?.StopDeviceMonitor();
                    ClearLogs();

                    // Start MTK Port Monitor
                    _mtkController?.StartPortMonitoring();

                    // Update Right Info Panel for MTK
                    UpdateMtkInfoPanel();
                }
                // tabPage5 is Spreadtrum
                else if (selectedTab == tabPage5)
                {
                    // Switch to Spreadtrum Tab
                    _isOnFastbootTab = false;

                    // Stop other monitors
                    _fastbootController?.StopDeviceMonitoring();
                    ClearLogs();

                    _portRefreshTimer?.Stop();

                    _mtkController?.StopPortMonitoring();
                    ClearLogs();

                    // Start SPD Device Monitor and Refresh
                    _spreadtrumController?.RefreshDevices();

                    // Update Right Info Panel for SPD
                    UpdateSprdInfoPanel();
                }
                else
                {
                    // Other Tabs
                    _isOnFastbootTab = false;
                    // Stop All Monitors
                    _fastbootController?.StopDeviceMonitoring();
                    ClearLogs();

                    _mtkController?.StopPortMonitoring();
                    ClearLogs();

                    _spreadtrumController?.StopDeviceMonitor();
                    ClearLogs();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tab Switch Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear Logs
        /// </summary>
        private void ClearLogs()
        {

            if (uiRichTextBox1.InvokeRequired)
            {
                uiRichTextBox1.Invoke(new Action(() => uiRichTextBox1.Clear()));
            }
            else
            {
                uiRichTextBox1.Clear();
            }
        }

        /// <summary>
        /// Fastboot Cloud Link Parse
        /// </summary>
        private void FastbootOpenPayloadDialog()
        {
            // If textbox has content, parse it directly
            if (!string.IsNullOrWhiteSpace(uiTextBox1.Text))
            {
                FastbootParsePayloadInput(uiTextBox1.Text.Trim());
                return;
            }

            // Textbox empty, show input box
            string url = Microsoft.VisualBasic.Interaction.InputBox(
                "Please Enter OTA Download Link:\n\nSupport OnePlus/OPPO/Realme Official Link\nor direct ZIP/Payload Link",
                "Cloud Link Parse",
                "");

            if (!string.IsNullOrWhiteSpace(url))
            {
                uiTextBox1.Text = url.Trim();
                FastbootParsePayloadInput(url.Trim());
            }
        }

        /// <summary>
        /// Fastboot Read GPT (Also Read Device Info)
        /// </summary>
        private async Task FastbootReadPartitionTableWithInfoAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("Connecting Fastboot device...", Color.Blue);
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected)
                {
                    AppendLog("Connection failed, please check if device is in Fastboot mode", Color.Red);
                    return;
                }
            }

            // Read GPT and Device Info
            await _fastbootController.ReadPartitionTableAsync();
        }

        /// <summary>
        /// Fastboot Extract Partitions (Extract Checked Partitions)
        /// </summary>
        private async Task FastbootExtractPartitionsWithOptionsAsync()
        {
            if (_fastbootController == null) return;

            // Check if Payload loaded (Local or Cloud)
            bool hasLocalPayload = _fastbootController.PayloadSummary != null;
            bool hasRemotePayload = _fastbootController.IsRemotePayloadLoaded;

            if (!hasLocalPayload && !hasRemotePayload)
            {
                AppendLog("Please parse Payload first (Local file or Cloud Link)", Color.Orange);
                return;
            }

            // Let user select save directory
            string outputDir;
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Partition Extraction Directory";
                // Use default path if selected before
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

            // Select extract method based on loaded type
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
        /// Fastboot Read Device Info (Keep Compatibility)
        /// </summary>
        private async Task FastbootReadInfoAsync()
        {
            await FastbootReadPartitionTableWithInfoAsync();
        }

        /// <summary>
        /// Fastboot Read GPT (Keep Compatibility)
        /// </summary>
        private async Task FastbootReadPartitionTableAsync()
        {
            await FastbootReadPartitionTableWithInfoAsync();
        }

        /// <summary>
        /// Fastboot Flash Partition
        /// Support: Payload.bin / URL Parse / Extracted Folder / Common Image
        /// Flash Mode: OnePlus/OPPO Flash / Pure FBD / AB Flash / Normal Flash
        /// </summary>
        private async Task FastbootFlashPartitionsAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("Please connect Fastboot device first", Color.Orange);
                return;
            }

            // Check Flash Mode Options
            bool useOugaMode = checkbox7.Checked;      // Ouga Flash = OnePlus/OPPO Main Flow
            bool usePureFbdMode = checkbox45.Checked;  // FBD Flash = Pure FBD Mode
            bool switchSlotA = checkbox41.Checked;     // Switch Slot A = set_active a after flash
            bool clearData = !checkbox50.Checked;      // Opposite of Keep Data
            bool eraseFrp = checkbox43.Checked;        // Erase FRP
            bool autoReboot = checkbox44.Checked;      // Auto Reboot

            // Only use OnePlus flow when "Ouga Flash" or "FBD Flash" is checked
            // "Switch Slot A" no longer triggers OnePlus flow, executes set_active after normal flash
            if (useOugaMode || usePureFbdMode)
            {
                // OnePlus/OPPO Flash Mode (Support Payload/Folder/Image)
                string modeDesc = usePureFbdMode ? "Pure FBD" : "OnePlus/OPPO";
                AppendLog($"Using OnePlus/OPPO {modeDesc} Mode", Color.Blue);

                // Build Flash Partition List (Support Payload Partitions, Unpacked Folder, Script Tasks, Normal Images)
                var partitions = _fastbootController.BuildOnePlusFlashPartitions();
                if (partitions.Count == 0)
                {
                    AppendLog("No flashable partitions (Please Parse Payload or Select Image Files)", Color.Orange);
                    return;
                }

                // Show Partition Source Stats
                int payloadCount = partitions.Count(p => p.IsPayloadPartition);
                int fileCount = partitions.Count - payloadCount;
                if (payloadCount > 0)
                    AppendLog($"Selected {partitions.Count} Partitions (Payload: {payloadCount}, File: {fileCount})", Color.Blue);

                // Build Flash Options (Ouga Mode defaults to Slot A)
                var options = new LoveAlways.Fastboot.UI.FastbootUIController.OnePlusFlashOptions
                {
                    ABFlashMode = false,  // Deprecated AB Flash Mode
                    PureFBDMode = usePureFbdMode,
                    PowerFlashMode = false,
                    ClearData = clearData,
                    EraseFrp = eraseFrp,
                    AutoReboot = autoReboot,
                    TargetSlot = "a"
                };

                // Execute OnePlus Flash Flow (Auto Extract Payload Partitions)
                await _fastbootController.ExecuteOnePlusFlashAsync(partitions, options);
            }
            else
            {
                // Use Normal Flash Flow when Ouga/FBD Mode not checked
                bool hasLocalPayload = _fastbootController.PayloadSummary != null;
                bool hasRemotePayload = _fastbootController.IsRemotePayloadLoaded;

                if (hasRemotePayload)
                {
                    // Cloud Payload Flash (Download and Flash)
                    AppendLog("Using Cloud Payload Normal Flash Mode", Color.Blue);
                    await _fastbootController.FlashFromRemotePayloadAsync();
                }
                else if (hasLocalPayload)
                {
                    // Local Payload Flash
                    AppendLog("Using Local Payload Normal Flash Mode", Color.Blue);
                    await _fastbootController.FlashFromPayloadAsync();
                }
                else
                {
                    // Normal Flash (Need select image files)
                    await _fastbootController.FlashSelectedPartitionsAsync();
                }
            }
        }

        /// <summary>
        /// Fastboot Erase Partition
        /// </summary>
        private async Task FastbootErasePartitionsAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                AppendLog("Please connect Fastboot device first", Color.Orange);
                return;
            }

            await _fastbootController.EraseSelectedPartitionsAsync();
        }

        /// <summary>
        /// Fastboot Execute Flash Script or Quick Command
        /// </summary>
        private async Task FastbootExecuteAsync()
        {
            if (_fastbootController == null) return;

            if (!_fastbootController.IsConnected)
            {
                bool connected = await _fastbootController.ConnectAsync();
                if (!connected) return;
            }

            // Priority 1: If quick command selected, execute command
            if (_fastbootController.HasSelectedCommand())
            {
                await _fastbootController.ExecuteSelectedCommandAsync();
            }
            // Priority 2: If flash script task loaded, execute flash script (Higher priority than Payload)
            else if (_fastbootController.FlashTasks != null && _fastbootController.FlashTasks.Count > 0)
            {
                // Read user options
                bool keepData = checkbox50.Checked;   // Keep Data
                bool lockBl = checkbox21.Checked;     // Lock BL

                await _fastbootController.ExecuteFlashScriptAsync(keepData, lockBl);
            }
            // Priority 3: If Payload loaded, execute Payload Flash
            else if (_fastbootController.IsPayloadLoaded)
            {
                await _fastbootController.FlashFromPayloadAsync();
            }
            // Priority 4: If partitions checked and have images, execute flash selected partitions
            else if (_fastbootController.HasSelectedPartitionsWithFiles())
            {
                await _fastbootController.FlashSelectedPartitionsAsync();
            }
            else
            {
                // Nothing selected, prompt user
                AppendLog("Please select Quick Command, Load Flash Script, or Check Partitions before Execute", Color.Orange);
            }
        }

        /// <summary>
        /// Fastboot Execute Quick Command
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
        /// Fastboot Extract Partitions (From Payload, Support Local and Cloud)
        /// </summary>
        private async Task FastbootExtractPartitionsAsync()
        {
            // Call method with options directly
            await FastbootExtractPartitionsWithOptionsAsync();
        }

        /// <summary>
        /// Fastboot Select Output Path
        /// </summary>
        private void FastbootSelectOutputPath()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Output Directory";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    input1.Text = fbd.SelectedPath;
                    AppendLog($"Output Path: {fbd.SelectedPath}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot 选择 Payload 文件或文件夹
        /// </summary>
        private void FastbootSelectPayloadFile()
        {
            // 弹出选择对话框
            // 弹出choose对话框
            var result = MessageBox.Show(
                "Please Select load type：\n\n" +
                "「yes」choose Payload/use file\n" +
                "「no」Choose the extracted folder",
                "choose type",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // 选择文件
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Payload or Flash Script";
                    ofd.Filter = "Payload|*.bin;*.zip|Flash Script|*.bat;*.sh;*.cmd|All Files|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        uiTextBox1.Text = ofd.FileName;
                        FastbootParsePayloadInput(ofd.FileName);
                    }
                }
            }
            else
            {
                // 选择文件夹
                FastbootSelectPayloadFolder();
            }
        }

        /// <summary>
        /// Fastboot Select Extracted Folder
        /// </summary>
        private void FastbootSelectPayloadFolder()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Extracted Firmware Folder (Contains .img files)";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    uiTextBox1.Text = fbd.SelectedPath;
                    FastbootParsePayloadInput(fbd.SelectedPath);
                }
            }
        }

        /// <summary>
        /// Parse Payload Input (Support Local File, Folder and URL)
        /// </summary>
        private void FastbootParsePayloadInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            input = input.Trim();

            // Check if URL, File or Folder
            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // URL - Cloud Parse
                AppendLog($"Cloud URL detected, starting parse...", Color.Blue);
                _ = FastbootLoadPayloadFromUrlAsync(input);
            }
            else if (Directory.Exists(input))
            {
                // Extracted Folder
                AppendLog($"Folder Selected: {input}", Color.Blue);
                _fastbootController?.LoadExtractedFolder(input);
            }
            else if (File.Exists(input))
            {
                // Local File
                AppendLog($"Selected: {Path.GetFileName(input)}", Color.Blue);

                string ext = Path.GetExtension(input).ToLowerInvariant();
                string fileName = Path.GetFileName(input).ToLowerInvariant();

                if (ext == ".bat" || ext == ".sh" || ext == ".cmd")
                {
                    // Flash Script
                    FastbootLoadScript(input);
                }
                else if (ext == ".bin" || ext == ".zip" || fileName == "payload.bin")
                {
                    // Payload File
                    _ = FastbootLoadPayloadAsync(input);
                }
            }
            else
            {
                AppendLog($"Invalid Input: File/Folder not exist or URL Invalid", Color.Red);
            }
        }

        /// <summary>
        /// Fastboot Load Payload File
        /// </summary>
        private async Task FastbootLoadPayloadAsync(string payloadPath)
        {
            if (_fastbootController == null) return;

            bool success = await _fastbootController.LoadPayloadAsync(payloadPath);

            if (success)
            {
                // Update output path to Payload directory
                input1.Text = Path.GetDirectoryName(payloadPath);

                // Show Payload Summary
                var summary = _fastbootController.PayloadSummary;
                if (summary != null)
                {
                    AppendLog($"[Payload] Partitions: {summary.PartitionCount}, Total Size: {summary.TotalSizeFormatted}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot Load Cloud Payload From URL
        /// </summary>
        private async Task FastbootLoadPayloadFromUrlAsync(string url)
        {
            if (_fastbootController == null) return;

            bool success = await _fastbootController.LoadPayloadFromUrlAsync(url);

            if (success)
            {
                // Show Remote Payload Summary
                var summary = _fastbootController.RemotePayloadSummary;
                if (summary != null)
                {
                    AppendLog($"[Cloud Payload] Partitions: {summary.PartitionCount}, File Size: {summary.TotalSizeFormatted}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Fastboot Select Flash Script File
        /// </summary>
        private void FastbootSelectScript()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Flash Script (flash_all.bat)";
                ofd.Filter = "Flash Script|*.bat;*.sh;*.cmd|All Files|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    input1.Text = ofd.FileName;
                    AppendLog($"Script Selected: {Path.GetFileName(ofd.FileName)}", Color.Blue);

                    // Load Script
                    FastbootLoadScript(ofd.FileName);
                }
            }
        }

        /// <summary>
        /// Fastboot Load Flash Script
        /// </summary>
        private void FastbootLoadScript(string scriptPath)
        {
            if (_fastbootController == null) return;

            bool success = _fastbootController.LoadFlashScript(scriptPath);

            if (success)
            {
                // Update output path to script directory
                input1.Text = Path.GetDirectoryName(scriptPath);

                // Auto Select Options based on script
                AutoSelectOptionsFromScript(scriptPath);
            }
        }

        /// <summary>
        /// Auto Check UI Options based on Script Type
        /// </summary>
        private void AutoSelectOptionsFromScript(string scriptPath)
        {
            string fileName = Path.GetFileName(scriptPath).ToLowerInvariant();

            // Reset Options
            checkbox50.Checked = false;  // Keep Data
            checkbox21.Checked = false;  // Lock BL

            // Check script name for type
            if (fileName.Contains("except_storage") || fileName.Contains("except-storage") ||
                fileName.Contains("keep_data") || fileName.Contains("keepdata"))
            {
                // Keep Data Script
                checkbox50.Checked = true;
                AppendLog("Keep Data Script detected, checked 'Keep Data'", Color.Blue);
            }
            else if (fileName.Contains("_lock") || fileName.Contains("-lock") ||
                     fileName.EndsWith("lock.bat") || fileName.EndsWith("lock.sh"))
            {
                // Lock BL Script
                checkbox21.Checked = true;
                AppendLog("Lock BL Script detected, checked 'Lock BL'", Color.Blue);
            }
            else
            {
                // Normal Flash Script
                AppendLog("Normal Flash Script, will wipe all data", Color.Orange);
            }
        }

        /// <summary>
        /// Fastboot Select All/None Partitions
        /// </summary>
        private void FastbootSelectAllPartitions(bool selectAll)
        {
            foreach (ListViewItem item in listView5.Items)
            {
                item.Checked = selectAll;
            }
        }

        /// <summary>
        /// Fastboot Partition Double Click Select Image
        /// </summary>
        private void FastbootPartitionDoubleClick()
        {
            if (listView5.SelectedItems.Count == 0) return;

            var selectedItem = listView5.SelectedItems[0];
            _fastbootController?.SelectImageForPartition(selectedItem);
        }

        // Fastboot Partition Search Variables
        private string _fbLastSearchKeyword = "";
        private List<ListViewItem> _fbSearchMatches = new List<ListViewItem>();
        private int _fbCurrentMatchIndex = 0;
        private bool _fbIsSelectingFromDropdown = false;

        /// <summary>
        /// Fastboot Partition Search
        /// </summary>
        private void FastbootSearchPartition()
        {
            // If triggered from dropdown selection, locate directly
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

            // If search box empty, reset highlights
            if (string.IsNullOrEmpty(keyword))
            {
                FastbootResetPartitionHighlights();
                _fbLastSearchKeyword = "";
                _fbSearchMatches.Clear();
                _fbCurrentMatchIndex = 0;
                return;
            }

            // If keyword same, jump to next match
            if (keyword == _fbLastSearchKeyword && _fbSearchMatches.Count > 1)
            {
                FastbootJumpToNextMatch();
                return;
            }

            _fbLastSearchKeyword = keyword;
            _fbSearchMatches.Clear();
            _fbCurrentMatchIndex = 0;

            // Collect matching partition names for suggestions
            var suggestions = new List<string>();

            listView5.BeginUpdate();

            foreach (ListViewItem item in listView5.Items)
            {
                string partName = item.SubItems[0].Text.ToLower();

                if (partName.Contains(keyword))
                {
                    // Highlight matched item
                    item.BackColor = Color.LightYellow;
                    _fbSearchMatches.Add(item);

                    // Add to suggestion list
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

            // Update suggestion list
            FastbootUpdateSearchSuggestions(suggestions);

            // Scroll to first match
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

            // Deselect current item
            if (_fbCurrentMatchIndex < _fbSearchMatches.Count)
            {
                _fbSearchMatches[_fbCurrentMatchIndex].Selected = false;
            }

            // Move to next
            _fbCurrentMatchIndex = (_fbCurrentMatchIndex + 1) % _fbSearchMatches.Count;

            // Select and scroll to new match
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

        #region Quick Operations (Device Manager)

        /// <summary>
        /// Quick Reboot System (Priority Fastboot, fallback ADB)
        /// </summary>
        private async Task QuickRebootSystemAsync()
        {
            AppendLog("Executing: Reboot System...", Color.Cyan);

            // Priority Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootAsync();
                if (ok)
                {
                    AppendLog("Fastboot: Reboot Success", Color.Green);
                    return;
                }
            }

            // Fallback ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootAsync();
            if (result.Success)
                AppendLog("ADB: Reboot Success", Color.Green);
            else
                AppendLog($"Reboot Failed: {result.Error}", Color.Red);
        }

        /// <summary>
        /// Quick Reboot to Bootloader (Priority Fastboot, fallback ADB)
        /// </summary>
        private async Task QuickRebootBootloaderAsync()
        {
            AppendLog("Executing: Reboot to Fastboot...", Color.Cyan);

            // Priority Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootBootloaderAsync();
                if (ok)
                {
                    AppendLog("Fastboot: Reboot to Bootloader Success", Color.Green);
                    return;
                }
            }

            // Fallback ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootBootloaderAsync();
            if (result.Success)
                AppendLog("ADB: Reboot to Bootloader Success", Color.Green);
            else
                AppendLog($"Reboot Failed: {result.Error}", Color.Red);
        }

        /// <summary>
        /// Quick Reboot to Fastbootd (Priority Fastboot, fallback ADB)
        /// </summary>
        private async Task QuickRebootFastbootdAsync()
        {
            AppendLog("Executing: Reboot to Fastbootd...", Color.Cyan);

            // Priority Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootFastbootdAsync();
                if (ok)
                {
                    AppendLog("Fastboot: Reboot to Fastbootd Success", Color.Green);
                    return;
                }
            }

            // Fallback ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootFastbootAsync();
            if (result.Success)
                AppendLog("ADB: Reboot to Fastbootd Success", Color.Green);
            else
                AppendLog($"Reboot Failed: {result.Error}", Color.Red);
        }

        /// <summary>
        /// Quick Reboot to Recovery (Priority Fastboot, fallback ADB)
        /// </summary>
        private async Task QuickRebootRecoveryAsync()
        {
            AppendLog("Executing: Reboot to Recovery...", Color.Cyan);

            // Priority Fastboot
            if (_fastbootController != null && _fastbootController.IsConnected)
            {
                bool ok = await _fastbootController.RebootRecoveryAsync();
                if (ok)
                {
                    AppendLog("Fastboot: Reboot to Recovery Success", Color.Green);
                    return;
                }
            }

            // Fallback ADB
            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootRecoveryAsync();
            if (result.Success)
                AppendLog("ADB: Reboot to Recovery Success", Color.Green);
            else
                AppendLog($"Reboot Failed: {result.Error}", Color.Red);
        }

        /// <summary>
        /// MI Reboot EDL - Fastboot OEM EDL (Xiaomi Only)
        /// </summary>
        private async Task QuickMiRebootEdlAsync()
        {
            AppendLog("Executing: MI Reboot EDL (fastboot oem edl)...", Color.Cyan);

            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("Please connect Fastboot device first", Color.Orange);
                return;
            }

            bool ok = await _fastbootController.OemEdlAsync();
            if (ok)
                AppendLog("MI Reboot EDL: Success, device entering EDL mode", Color.Green);
            else
                AppendLog("MI Reboot EDL: Failed, device may not support this command", Color.Red);
        }

        /// <summary>
        /// Lenovo/Android Reboot EDL - ADB reboot edl
        /// </summary>
        private async Task QuickAdbRebootEdlAsync()
        {
            AppendLog("Executing: Android Reboot EDL (adb reboot edl)...", Color.Cyan);

            var result = await LoveAlways.Fastboot.Common.AdbHelper.RebootEdlAsync();
            if (result.Success)
                AppendLog("ADB: Reboot EDL Success, device entering EDL mode", Color.Green);
            else
                AppendLog($"Reboot EDL Failed: {result.Error}", Color.Red);
        }

        /// <summary>
        /// Erase FRP (Fastboot erase frp)
        /// </summary>
        private async Task QuickEraseFrpAsync()
        {
            AppendLog("Executing: Erase FRP (fastboot erase frp)...", Color.Cyan);

            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("Please connect Fastboot device first", Color.Orange);
                return;
            }

            bool ok = await _fastbootController.EraseFrpAsync();
            if (ok)
                AppendLog("Erase FRP: Success", Color.Green);
            else
                AppendLog("Erase FRP: Failed, device may include locked Bootloader", Color.Red);
        }

        /// <summary>
        /// Switch Slot (Fastboot set_active)
        /// </summary>
        private async Task QuickSwitchSlotAsync()
        {
            AppendLog("Executing: Switch Slot...", Color.Cyan);

            if (_fastbootController == null || !_fastbootController.IsConnected)
            {
                AppendLog("Please connect Fastboot device first", Color.Orange);
                return;
            }

            // Get current slot
            string currentSlot = await _fastbootController.GetCurrentSlotAsync();
            if (string.IsNullOrEmpty(currentSlot))
            {
                AppendLog("Failed to get current slot, device may not support A/B partitions", Color.Orange);
                return;
            }

            // Switch to another slot
            string targetSlot = currentSlot == "a" ? "b" : "a";
            AppendLog($"Current Slot: {currentSlot}, Switching to: {targetSlot}", Color.White);

            bool ok = await _fastbootController.SetActiveSlotAsync(targetSlot);
            if (ok)
                AppendLog($"Switch Slot Success: {currentSlot} -> {targetSlot}", Color.Green);
            else
                AppendLog("Switch Slot Failed", Color.Red);
        }

        #endregion

        #region Other Function Menu

        /// <summary>
        /// Open Device Manager
        /// </summary>
        private void OpenDeviceManager()
        {
            try
            {
                System.Diagnostics.Process.Start("devmgmt.msc");
                AppendLog("Device Manager Opened", Color.Blue);
            }
            catch (Exception ex)
            {
                AppendLog($"Open Device Manager Failed: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Open CMD Command Prompt (In App Dir, Admin)
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
                    Verb = "runas"  // Run as Admin
                };
                System.Diagnostics.Process.Start(psi);
                AppendLog($"CMD Opened (Admin): {psi.WorkingDirectory}", Color.Blue);
            }
            catch (Exception ex)
            {
                // User may cancelled UAC
                if (ex.Message.Contains("canceled") || ex.Message.Contains("取消"))
                    AppendLog("User cancelled Admin permission request", Color.Orange);
                else
                    AppendLog($"Open CMD Failed: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Open Driver Installer
        /// </summary>
        /// <param name="driverType">Driver Type: android, mtk, qualcomm</param>
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
                        driverName = "Android Driver";
                        // Try multiple possible paths
                        string[] androidPaths = {
                            Path.Combine(appDir, "drivers", "android_usb_driver.exe"),
                            Path.Combine(appDir, "drivers", "adb_driver.exe"),
                            Path.Combine(appDir, "ADB_Driver.exe")
                        };
                        driverPath = androidPaths.FirstOrDefault(File.Exists);
                        break;

                    case "mtk":
                        driverName = "MTK Driver";
                        string[] mtkPaths = {
                            Path.Combine(appDir, "drivers", "mtk_usb_driver.exe"),
                            Path.Combine(appDir, "drivers", "MediaTek_USB_VCOM_Driver.exe"),
                            Path.Combine(appDir, "MTK_Driver.exe")
                        };
                        driverPath = mtkPaths.FirstOrDefault(File.Exists);
                        break;

                    case "qualcomm":
                        driverName = "Qualcomm Driver";
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
                    AppendLog($"{driverName} Installer not found, please install manually", Color.Orange);
                    MessageBox.Show($"{driverName} Installer not found.\n\nPlease download driver from official website.",
                        "Driver Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                System.Diagnostics.Process.Start(driverPath);
                AppendLog($"Started {driverName} Installer", Color.Blue);
            }
            catch (Exception ex)
            {
                AppendLog($"Start Driver Installer Failed: {ex.Message}", Color.Red);
            }
        }

        #endregion

        private void checkbox22_CheckedChanged(object sender, AntdUI.BoolEventArgs e)
        {

        }

        private void mtkBtnConnect_Click_1(object sender, EventArgs e)
        {

        }
    }
}