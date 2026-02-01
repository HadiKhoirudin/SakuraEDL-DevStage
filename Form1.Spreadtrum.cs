// ============================================================================
// LoveAlways - Form1 Spreadtrum Module Partial Class
// Spreadtrum/Unisoc Module Partial Class
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.Spreadtrum.UI;
using LoveAlways.Spreadtrum.Common;
using LoveAlways.Spreadtrum.Protocol;
using LoveAlways.Spreadtrum.Exploit;

namespace LoveAlways
{
    public partial class Form1
    {
        // ========== Spreadtrum Controller ==========
        private SpreadtrumUIController _spreadtrumController;
        private uint _selectedChipId = 0;
        
        /// <summary>
        /// Safe UI Update Invoke (Handle Window Closed)
        /// </summary>
        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                    return;
                    
                if (InvokeRequired)
                    Invoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
        
        // Custom FDL Configuration
        private string _customFdl1Path = null;
        private string _customFdl2Path = null;
        private uint _customFdl1Addr = 0;
        private uint _customFdl2Addr = 0;
        
        // Detected Device
        private string _detectedSprdPort = null;
        private LoveAlways.Spreadtrum.Common.SprdDeviceMode _detectedSprdMode = LoveAlways.Spreadtrum.Common.SprdDeviceMode.Unknown;

        // Chip List - Loaded Dynamically from Database
        private static Dictionary<string, uint> _sprdChipList;
        private static Dictionary<string, uint> SprdChipList
        {
            get
            {
                if (_sprdChipList == null)
                {
                    _sprdChipList = new Dictionary<string, uint>();
                    _sprdChipList.Add("Auto Detect", 0);
                    
                    // Load Chips by Series from Database
                    var chipsBySeries = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetChipsBySeries();
                    foreach (var series in chipsBySeries.OrderBy(s => s.Key))
                    {
                        // Add Series Separator
                        _sprdChipList.Add($"── {series.Key} ──", 0xFFFF);
                        
                        // Add Chips in Series
                        foreach (var chip in series.Value.OrderBy(c => c.ChipName))
                        {
                            string displayName = chip.HasExploit 
                                ? $"{chip.ChipName} ★" 
                                : chip.ChipName;
                            _sprdChipList.Add(displayName, chip.ChipId);
                        }
                    }
                }
                return _sprdChipList;
            }
        }

        /// <summary>
        /// Initialize Spreadtrum Module
        /// </summary>
        private void InitializeSpreadtrumModule()
        {
            try
            {
                // Initialize Chip Selector
                InitializeChipSelector();

                // Create Spreadtrum Controller
                _spreadtrumController = new SpreadtrumUIController(
                    (msg, color) => AppendLog(msg, color),
                    msg => AppendLogDetail(msg));

                // Bind Events
                BindSpreadtrumEvents();

                // Note: Device monitoring starts when switching to Spreadtrum tab to avoid conflicts

                AppendLog("[Spreadtrum] Module Initialized", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"[Spreadtrum] Initialize Failed: {ex.Message}", Color.Red);
            }
        }
        
        /// <summary>
        /// Scan and Show All COM Ports in Device Manager (For Debugging)
        /// </summary>
        private void SprdScanAllComPorts()
        {
            try
            {
                var detector = new SprdPortDetector();
                detector.OnLog += msg => AppendLog(msg, Color.Gray);
                
                AppendLog("[Device Manager] Scanning all COM ports...", Color.Cyan);
                var ports = detector.ScanAllComPorts();
                
                if (ports.Count == 0)
                {
                    AppendLog("[Device Manager] No COM ports found", Color.Orange);
                    return;
                }
                
                AppendLog(string.Format("[Device Manager] Found {0} COM ports:", ports.Count), Color.Green);
                
                foreach (var port in ports)
                {
                    string sprdFlag = port.IsSprdDetected ? " ★Spreadtrum★" : "";
                    Color color = port.IsSprdDetected ? Color.Lime : Color.White;
                    
                    AppendLog(string.Format("  {0}: VID={1:X4} PID={2:X4}{3}", 
                        port.ComPort, port.Vid, port.Pid, sprdFlag), color);
                    AppendLog(string.Format("    Name: {0}", port.Name), Color.Gray);
                    
                    if (!string.IsNullOrEmpty(port.HardwareId))
                    {
                        AppendLog(string.Format("    HW ID: {0}", port.HardwareId), Color.DarkGray);
                    }
                }
                
                // Stats
                int sprdCount = 0;
                foreach (var p in ports)
                {
                    if (p.IsSprdDetected) sprdCount++;
                }
                
                AppendLog(string.Format("[Device Manager] Total: {0} ports, {1} identified as Spreadtrum", 
                    ports.Count, sprdCount), Color.Cyan);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format("[Device Manager] Scan Exception: {0}", ex.Message), Color.Red);
            }
        }

        /// <summary>
        /// Initialize Chip Selector
        /// </summary>
        private void InitializeChipSelector()
        {
            // Fill Chip List
            var items = new List<object>();
            foreach (var chip in SprdChipList)
            {
                items.Add(chip.Key);
            }
            sprdSelectChip.Items.Clear();
            sprdSelectChip.Items.AddRange(items.ToArray());
            sprdSelectChip.SelectedIndex = 0; // Default "Auto Detect"
            
            // Initialize Device Selection Empty
            sprdSelectDevice.Items.Clear();
            sprdSelectDevice.Items.Add("Auto Detect");
            sprdSelectDevice.SelectedIndex = 0;
        }

        /// <summary>
        /// Update Device List (Scan directories with FDL files based on selected chip)
        /// </summary>
        private void UpdateDeviceList(string chipName)
        {
            // Ensure execution on UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateDeviceList(chipName)));
                return;
            }
            
            // Prepare Device List
            var items = new List<object>();
            items.Add("Auto Detect");
            
            if (!string.IsNullOrEmpty(chipName) && chipName != "Auto Detect")
            {
                // Scan device directories with FDL files directly from file system
                var deviceDirs = ScanFdlDeviceDirectories(chipName);
                
                foreach (var deviceName in deviceDirs.OrderBy(d => d))
                {
                    items.Add(deviceName);
                }
                
                if (deviceDirs.Count > 0)
                {
                    AppendLog($"[Spreadtrum] Available Devices: {deviceDirs.Count}", Color.Gray);
                }
                else
                {
                    AppendLog($"[Spreadtrum] {chipName} No Available Device FDL", Color.Orange);
                }
            }
            
            // Update at once
            sprdSelectDevice.Items.Clear();
            sprdSelectDevice.Items.AddRange(items.ToArray());
            
            // Set Default Selection
            if (sprdSelectDevice.Items.Count > 0)
            {
                sprdSelectDevice.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Scan device directories with FDL files under chip directory
        /// </summary>
        private List<string> ScanFdlDeviceDirectories(string chipName)
        {
            var result = new List<string>();
            string baseDir = GetSprdResourcesBasePath();
            
            // Get search path by chip
            var searchPaths = GetChipSearchPaths(chipName);
            
            foreach (var searchPath in searchPaths)
            {
                string fullPath = Path.Combine(baseDir, searchPath);
                if (!Directory.Exists(fullPath))
                    continue;
                
                // Iterate subdirectories
                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    // Check if directory has FDL1 or FDL2 files
                    var fdlFiles = Directory.GetFiles(dir, "fdl*.bin", SearchOption.AllDirectories);
                    if (fdlFiles.Length > 0)
                    {
                        string deviceName = Path.GetFileName(dir);
                        // Exclude purely numeric or special directories
                        if (!string.IsNullOrEmpty(deviceName) && 
                            !deviceName.All(char.IsDigit) &&
                            deviceName != "max" && deviceName != "1" && deviceName != "2")
                        {
                            result.Add(deviceName);
                        }
                    }
                }
            }
            
            return result.Distinct().ToList();
        }

        /// <summary>
        /// Get FDL Search Paths for Chip
        /// </summary>
        private List<string> GetChipSearchPaths(string chipName)
        {
            var paths = new List<string>();
            
            switch (chipName.ToUpper())
            {
                case "SC8541E":
                case "SC9832E":
                    paths.Add(@"sc_sp_sl\98xx_85xx\9832E_8541E");
                    break;
                case "SC9863A":
                case "SC8581A":
                    paths.Add(@"sc_sp_sl\98xx_85xx\9863A_8581A");
                    break;
                case "SC7731E":
                    paths.Add(@"sc_sp_sl\old\7731e");
                    break;
                case "SC9850K":
                    paths.Add(@"sc_sp_sl\98xx_85xx\9850K");
                    break;
                case "UMS512":
                    paths.Add(@"ums\ums512");
                    break;
                case "UMS9230":
                case "T606":
                    paths.Add(@"ums\ums9230");
                    break;
                case "UMS312":
                    paths.Add(@"ums\ums312");
                    break;
                case "UWS6152":
                    paths.Add(@"uws\uws6152");
                    paths.Add(@"uws\uws6152E");
                    break;
                case "UWS6131":
                    paths.Add(@"uws\uws6131");
                    break;
                case "UWS6137E":
                    paths.Add(@"uws\uws6137E");
                    break;
                case "UDX710":
                    paths.Add(@"other\udx710");
                    break;
                default:
                    // Try generic search
                    paths.Add(chipName.ToLower());
                    break;
            }
            
            return paths;
        }

        /// <summary>
        /// Get SPD Resource Base Path
        /// </summary>
        private string GetSprdResourcesBasePath()
        {
            // Try multiple possible paths
            var candidates = new[]
            {
                // 1. SprdResources in current directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "sprd_fdls"),
                // 2. Project root (debugging)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SprdResources", "sprd_fdls"),
                // 3. Parent directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SprdResources", "sprd_fdls")
            };
            
            foreach (var path in candidates)
            {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    return fullPath;
            }
            
            // Default return first (may not exist)
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "sprd_fdls");
        }

        /// <summary>
        /// Find FDL Files for Device
        /// </summary>
        private (string fdl1, string fdl2) FindDeviceFdlFiles(string chipName, string deviceName)
        {
            string fdl1 = null;
            string fdl2 = null;
            
            string baseDir = GetSprdResourcesBasePath();
            var searchPaths = GetChipSearchPaths(chipName);
            
            foreach (var searchPath in searchPaths)
            {
                string deviceDir = Path.Combine(baseDir, searchPath, deviceName);
                if (!Directory.Exists(deviceDir))
                    continue;
                
                // Find FDL1
                var fdl1Files = Directory.GetFiles(deviceDir, "fdl1*.bin", SearchOption.AllDirectories);
                if (fdl1Files.Length > 0)
                {
                    // Prefer signed version
                    fdl1 = fdl1Files.FirstOrDefault(f => f.Contains("-sign")) ?? fdl1Files[0];
                }
                
                // Find FDL2
                var fdl2Files = Directory.GetFiles(deviceDir, "fdl2*.bin", SearchOption.AllDirectories);
                if (fdl2Files.Length > 0)
                {
                    fdl2 = fdl2Files.FirstOrDefault(f => f.Contains("-sign")) ?? fdl2Files[0];
                }
                
                if (fdl1 != null || fdl2 != null)
                    break;
            }
            
            return (fdl1, fdl2);
        }

        /// <summary>
        /// Bind Spreadtrum Events
        /// </summary>
        private void BindSpreadtrumEvents()
        {
            // Chip Selection Changed
            sprdSelectChip.SelectedIndexChanged += (s, e) =>
            {
                string selected = sprdSelectChip.SelectedValue?.ToString() ?? "";
                
                // Remove ★ mark
                string chipName = selected.Replace(" ★", "").Trim();
                
                // Skip Separator
                if (selected.StartsWith("──"))
                    return;
                
                if (SprdChipList.TryGetValue(selected, out uint chipId) && chipId != 0xFFFF)
                {
                    _selectedChipId = chipId;
                    if (chipId > 0)
                    {
                        // Get Chip Details from Database
                        var chipInfo = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetChipById(chipId);
                        if (chipInfo != null)
                        {
                            string exploitInfo = chipInfo.HasExploit ? $" [Exploit: {chipInfo.ExploitId}]" : "";
                            AppendLog($"[Spreadtrum] Select Chip: {chipInfo.DisplayName}{exploitInfo}", Color.Cyan);
                            AppendLog($"[Spreadtrum] Default Addr - FDL1: {chipInfo.Fdl1AddressHex}, FDL2: {chipInfo.Fdl2AddressHex}", Color.Gray);
                            AppendLog($"[Spreadtrum] Tip: You can select FDL files below to override default config", Color.Gray);
                            
                            _spreadtrumController?.SetChipId(chipId);
                            
                            // Set FDL Default Address (Keep selected file path)
                            _customFdl1Addr = chipInfo.Fdl1Address;
                            _customFdl2Addr = chipInfo.Fdl2Address;
                            _spreadtrumController?.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                            _spreadtrumController?.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                            
                            // Keep FDL inputs enabled, use chip default address
                            SetFdlInputsEnabled(false, clearPaths: false);
                            
                            // Auto fill address (Only when address box is empty)
                            if (string.IsNullOrEmpty(input5.Text))
                                input5.Text = chipInfo.Fdl1AddressHex;
                            if (string.IsNullOrEmpty(input10.Text))
                                input10.Text = chipInfo.Fdl2AddressHex;
                            
                            // Update Device List
                            UpdateDeviceList(chipInfo.ChipName);
                        }
                        else
                        {
                            // Fallback to old method
                            uint fdl1Addr = SprdPlatform.GetFdl1Address(chipId);
                            uint fdl2Addr = SprdPlatform.GetFdl2Address(chipId);
                            AppendLog($"[Spreadtrum] Select Chip: {chipName}", Color.Cyan);
                            AppendLog($"[Spreadtrum] Default Addr - FDL1: 0x{fdl1Addr:X}, FDL2: 0x{fdl2Addr:X}", Color.Gray);
                            AppendLog($"[Spreadtrum] Tip: You can select FDL files below to override default config", Color.Gray);
                            _spreadtrumController?.SetChipId(chipId);
                            
                            // Set FDL Default Address (Keep selected file path)
                            _customFdl1Addr = fdl1Addr;
                            _customFdl2Addr = fdl2Addr;
                            _spreadtrumController?.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                            _spreadtrumController?.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                            
                            SetFdlInputsEnabled(false, clearPaths: false);
                            if (string.IsNullOrEmpty(input5.Text))
                                input5.Text = $"0x{fdl1Addr:X}";
                            if (string.IsNullOrEmpty(input10.Text))
                                input10.Text = $"0x{fdl2Addr:X}";
                            
                            // Update Device List
                            UpdateDeviceList(chipName);
                        }
                    }
                    else
                    {
                        // Auto Detect Mode, fully enable custom FDL input
                        AppendLog("[Spreadtrum] Chip set to Auto Detect, please config FDL manually", Color.Gray);
                        _spreadtrumController?.SetChipId(0);
                        
                        // Enable all FDL inputs
                        SetFdlInputsEnabled(true, clearPaths: true);
                        
                        // Clear Address
                        input5.Text = "";
                        input10.Text = "";
                        
                        // Clear Device List
                        UpdateDeviceList(null);
                    }
                }
            };
            
            // Device Selection Changed
            // Double Click Device Selector = Scan All COM Ports (Debug)
            sprdSelectDevice.DoubleClick += (s, e) => SprdScanAllComPorts();
            
            sprdSelectDevice.SelectedIndexChanged += (s, e) =>
            {
                string selected = sprdSelectDevice.SelectedValue?.ToString() ?? "";
                
                if (selected == "Auto Detect" || string.IsNullOrEmpty(selected))
                {
                    _customFdl1Path = null;
                    _customFdl2Path = null;
                    input2.Text = "";
                    input4.Text = "";
                    AppendLog("[Spreadtrum] Device set to Auto Detect", Color.Gray);
                    return;
                }
                
                // Device name is directory name
                string deviceName = selected;
                
                // Get currently selected chip name
                string chipSelected = sprdSelectChip.SelectedValue?.ToString() ?? "";
                string chipName = chipSelected.Replace(" ★", "").Trim();
                
                // Find FDL files directly from file system
                var fdlPaths = FindDeviceFdlFiles(chipName, deviceName);
                
                if (fdlPaths.fdl1 != null || fdlPaths.fdl2 != null)
                {
                    AppendLog($"[Spreadtrum] Select Device: {deviceName}", Color.Cyan);
                    
                    if (fdlPaths.fdl1 != null)
                    {
                        _customFdl1Path = fdlPaths.fdl1;
                        input2.Text = Path.GetFileName(fdlPaths.fdl1);
                        AppendLog($"[Spreadtrum] FDL1: {Path.GetFileName(fdlPaths.fdl1)}", Color.Gray);
                    }
                    
                    if (fdlPaths.fdl2 != null)
                    {
                        _customFdl2Path = fdlPaths.fdl2;
                        input4.Text = Path.GetFileName(fdlPaths.fdl2);
                        AppendLog($"[Spreadtrum] FDL2: {Path.GetFileName(fdlPaths.fdl2)}", Color.Gray);
                    }
                    
                    // Update Controller
                    _spreadtrumController?.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                    _spreadtrumController?.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                }
                else
                {
                    AppendLog($"[Spreadtrum] Device FDL Not Found: {deviceName}", Color.Orange);
                }
            };

            // Double Click PAC Input to Browse
            sprdInputPac.DoubleClick += (s, e) => SprdBrowsePac();

            // ========== FDL Custom Config ==========
            
            // FDL1 File Browse (Double Click input2)
            input2.DoubleClick += (s, e) =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select FDL1 File";
                    ofd.Filter = "FDL File (*.bin)|*.bin|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        input2.Text = ofd.FileName;
                        _customFdl1Path = ofd.FileName;
                        AppendLog($"[Spreadtrum] FDL1 File: {Path.GetFileName(ofd.FileName)}", Color.Cyan);
                        _spreadtrumController?.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                    }
                }
            };

            // FDL2 File Browse (Double Click input4)
            input4.DoubleClick += (s, e) =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select FDL2 File";
                    ofd.Filter = "FDL File (*.bin)|*.bin|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        input4.Text = ofd.FileName;
                        _customFdl2Path = ofd.FileName;
                        AppendLog($"[Spreadtrum] FDL2 File: {Path.GetFileName(ofd.FileName)}", Color.Cyan);
                        _spreadtrumController?.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                    }
                }
            };

            // FDL1 Address Input (input5)
            input5.TextChanged += (s, e) =>
            {
                string text = input5.Text.Trim();
                if (TryParseHexAddress(text, out uint addr))
                {
                    _customFdl1Addr = addr;
                    _spreadtrumController?.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                }
            };

            // FDL2 Address Input (input10)
            input10.TextChanged += (s, e) =>
            {
                string text = input10.Text.Trim();
                if (TryParseHexAddress(text, out uint addr))
                {
                    _customFdl2Addr = addr;
                    _spreadtrumController?.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                }
            };

            // Write Partition (Support Single/Multi/Whole PAC)
            sprdBtnWritePartition.Click += async (s, e) => await SprdWritePartitionAsync();

            // Read Partition (Support Single/Multi)
            sprdBtnReadPartition.Click += async (s, e) => await SprdReadPartitionAsync();

            // Erase Partition
            sprdBtnErasePartition.Click += async (s, e) => await SprdErasePartitionAsync();

            // Extract PAC
            sprdBtnExtract.Click += async (s, e) => await SprdExtractPacAsync();

            // Reboot Device
            sprdBtnReboot.Click += async (s, e) => await _spreadtrumController.RebootDeviceAsync();

            // Read GPT
            sprdBtnReadGpt.Click += async (s, e) => await SprdReadPartitionTableAsync();

            // Select All
            sprdChkSelectAll.CheckedChanged += (s, e) =>
            {
                foreach (ListViewItem item in sprdListPartitions.Items)
                {
                    item.Checked = sprdChkSelectAll.Checked;
                }
            };

            // ========== Second Row Operation Buttons ==========
            sprdBtnReadImei.Click += async (s, e) => await SprdReadImeiAsync();
            sprdBtnWriteImei.Click += async (s, e) => await SprdWriteImeiAsync();
            sprdBtnBackupCalib.Click += async (s, e) => await SprdBackupCalibrationAsync();
            sprdBtnRestoreCalib.Click += async (s, e) => await SprdRestoreCalibrationAsync();
            sprdBtnFactoryReset.Click += async (s, e) => await SprdFactoryResetAsync();
            sprdBtnUnlockBL.Click += async (s, e) => await SprdUnlockBootloaderAsync();
            sprdBtnNvManager.Click += async (s, e) => await SprdOpenNvManagerAsync();

            // Device Connected Event - Show info only, no auto connect
            _spreadtrumController.OnDeviceConnected += dev =>
            {
                SafeInvoke(() =>
                {
                    AppendLog($"[Spreadtrum] Device Detected: {dev.ComPort} ({dev.Mode})", Color.Green);
                    
                    // Save Detected Port
                    _detectedSprdPort = dev.ComPort;
                    _detectedSprdMode = dev.Mode;
                    
                    // Update Right Info Panel
                    UpdateSprdInfoPanel();
                    
                    if (dev.Mode == LoveAlways.Spreadtrum.Common.SprdDeviceMode.Download)
                    {
                        AppendLog($"[Spreadtrum] Device entered Download Mode", Color.Cyan);
                        AppendLog("[Spreadtrum] Please select chip model or load PAC, then click [Read GPT]", Color.Yellow);
                    }
                });
            };

            _spreadtrumController.OnDeviceDisconnected += dev =>
            {
                SafeInvoke(() =>
                {
                    AppendLog($"[Spreadtrum] Device Disconnected: {dev.ComPort}", Color.Orange);
                    
                    // Clear Detected Port
                    _detectedSprdPort = null;
                    _detectedSprdMode = LoveAlways.Spreadtrum.Common.SprdDeviceMode.Unknown;
                    
                    // Update Right Info Panel
                    UpdateSprdInfoPanel();
                });
            };

            // PAC Loaded Event
            _spreadtrumController.OnPacLoaded += pac =>
            {
                SafeInvoke(() =>
                {
                    // Update Partition List Title
                    sprdGroupPartitions.Text = $"Partition List - {pac.Header.ProductName} ({pac.Files.Count} Files)";

                    // Update Partition List
                    sprdListPartitions.Items.Clear();
                    foreach (var file in pac.Files)
                    {
                        if (file.Size == 0 || string.IsNullOrEmpty(file.FileName))
                            continue;

                        var item = new ListViewItem(file.PartitionName);
                        item.SubItems.Add(file.FileName);
                        item.SubItems.Add(FormatSize(file.Size));
                        item.SubItems.Add(file.Type.ToString());
                        item.SubItems.Add(file.Address > 0 ? $"0x{file.Address:X}" : "--");
                        item.SubItems.Add($"0x{file.DataOffset:X}");
                        item.SubItems.Add(file.IsSparse ? "Yes" : "No");
                        item.Tag = file;

                        // Default select non-FDL/XML/Userdata files
                        bool shouldCheck = file.Type != PacFileType.FDL1 && 
                                          file.Type != PacFileType.FDL2 &&
                                          file.Type != PacFileType.XML;
                        
                        // If Skip Userdata is checked
                        if (sprdChkSkipUserdata.Checked && file.Type == PacFileType.UserData)
                            shouldCheck = false;

                        item.Checked = shouldCheck;
                        sprdListPartitions.Items.Add(item);
                    }
                });
            };

            // State Changed Event - Use Right Panel Display
            _spreadtrumController.OnStateChanged += state =>
            {
                SafeInvoke(() =>
                {
                    string statusText = "";
                    switch (state)
                    {
                        case SprdDeviceState.Connected:
                            statusText = "[Spreadtrum] Device Connected (ROM)";
                            uiLabel8.Text = "Current Op: Spreadtrum ROM Mode";
                            break;
                        case SprdDeviceState.Fdl1Loaded:
                            statusText = "[Spreadtrum] FDL1 Loaded";
                            uiLabel8.Text = "Current Op: FDL1 Loaded";
                            break;
                        case SprdDeviceState.Fdl2Loaded:
                            statusText = "[Spreadtrum] FDL2 Loaded (Ready to Flash)";
                            uiLabel8.Text = "Current Op: Spreadtrum FDL2 Ready";
                            break;
                        case SprdDeviceState.Disconnected:
                            statusText = "[Spreadtrum] Device Disconnected";
                            uiLabel8.Text = "Current Op: Waiting for Device";
                            SprdClearDeviceInfo();
                            break;
                        case SprdDeviceState.Error:
                            statusText = "[Spreadtrum] Device Error";
                            uiLabel8.Text = "Current Op: Device Error";
                            break;
                    }
                    if (!string.IsNullOrEmpty(statusText))
                        AppendLog(statusText, state == SprdDeviceState.Error ? Color.Red : Color.Cyan);
                });

                // After FDL2 loaded, do not auto read partition table (Triggered by user)
                // This avoids freezing due to multiple calls
            };

            // Partition Table Loaded Event
            _spreadtrumController.OnPartitionTableLoaded += partitions =>
            {
                SafeInvoke(() =>
                {
                    // Update Partition List Title
                    sprdGroupPartitions.Text = $"Partition Table (Device) - {partitions.Count} Partitions";

                    // Clear and Fill Partition List
                    sprdListPartitions.Items.Clear();
                    foreach (var part in partitions)
                    {
                        var item = new ListViewItem(part.Name);
                        item.SubItems.Add("--");  // File Name (Device partition has no file name)
                        item.SubItems.Add(FormatSize(part.Size));
                        item.SubItems.Add("Partition");
                        item.SubItems.Add($"0x{part.Offset:X}");  // Offset as Address
                        item.SubItems.Add($"0x{part.Offset:X}");
                        item.SubItems.Add("--");  // Sparse
                        item.Tag = part;
                        item.Checked = false;  // Default unchecked
                        sprdListPartitions.Items.Add(item);
                    }
                });
            };

            // Progress Event
            _spreadtrumController.OnProgress += (current, total) =>
            {
                SafeInvoke(() =>
                {
                    int percent = total > 0 ? (int)(current * 100 / total) : 0;
                    uiProcessBar1.Value = percent;
                });
            };

            // Partition Search
            sprdSelectSearch.TextChanged += (s, e) =>
            {
                string search = sprdSelectSearch.Text.ToLower();
                foreach (ListViewItem item in sprdListPartitions.Items)
                {
                    item.BackColor = item.Text.ToLower().Contains(search) && !string.IsNullOrEmpty(search)
                        ? Color.LightYellow
                        : Color.White;
                }
            };

            // ========== Double Click Partition to Select External Image to Flash ==========
            sprdListPartitions.DoubleClick += async (s, e) =>
            {
                if (sprdListPartitions.SelectedItems.Count == 0)
                    return;

                // Get Selected Partitions
                var selectedPartitions = new List<string>();
                foreach (ListViewItem item in sprdListPartitions.SelectedItems)
                {
                    selectedPartitions.Add(item.Text);
                }

                if (selectedPartitions.Count == 1)
                {
                    // Single Partition - Select Single Image
                    await SprdFlashSinglePartitionAsync(selectedPartitions[0]);
                }
                else
                {
                    // Multiple Partitions - Select Folder
                    await SprdFlashMultiplePartitionsAsync(selectedPartitions);
                }
            };
        }

        /// <summary>
        /// Flash Single Partition (Select External Image)
        /// </summary>
        private async Task SprdFlashSinglePartitionAsync(string partitionName)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"Select {partitionName} Partition Image";
                ofd.Filter = "Image File (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to flash {Path.GetFileName(ofd.FileName)} to {partitionName} partition?\n\nThis operation cannot be undone!",
                        "Confirm Flash",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        AppendLog($"[Spreadtrum] Flash Partition: {partitionName} <- {Path.GetFileName(ofd.FileName)}", Color.Cyan);
                        bool success = await _spreadtrumController.FlashImageFileAsync(partitionName, ofd.FileName);
                        
                        if (success)
                        {
                            MessageBox.Show($"Partition {partitionName} flashed successfully!", "Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Flash Multiple Partitions (Match from Folder)
        /// </summary>
        private async Task SprdFlashMultiplePartitionsAsync(List<string> partitionNames)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = $"Select folder containing image files\nWill auto match: {string.Join(", ", partitionNames)}";

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    // Find Matched Files
                    var matchedFiles = new Dictionary<string, string>();

                    foreach (var partName in partitionNames)
                    {
                        // Try multiple file name formats
                        string[] patterns = new[]
                        {
                            $"{partName}.img",
                            $"{partName}.bin",
                            $"{partName}_a.img",
                            $"{partName}_b.img"
                        };

                        foreach (var pattern in patterns)
                        {
                            string filePath = Path.Combine(fbd.SelectedPath, pattern);
                            if (File.Exists(filePath))
                            {
                                matchedFiles[partName] = filePath;
                                break;
                            }
                        }
                    }

                    if (matchedFiles.Count == 0)
                    {
                        MessageBox.Show("No matched image files found!\n\nFilenames should be: partition_name.img or partition_name.bin", "Files Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Show Match Results
                    var msg = $"Found {matchedFiles.Count}/{partitionNames.Count} matched files:\n\n";
                    foreach (var kvp in matchedFiles)
                    {
                        msg += $"  {kvp.Key} <- {Path.GetFileName(kvp.Value)}\n";
                    }
                    msg += "\nAre you sure you want to flash? This operation cannot be undone!";

                    var result = MessageBox.Show(msg, "Confirm Flash", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        await _spreadtrumController.FlashMultipleImagesAsync(matchedFiles);
                    }
                }
            }
        }

        /// <summary>
        /// Browse PAC File
        /// </summary>
        private void SprdBrowsePac()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Spreadtrum PAC Firmware Package";
                ofd.Filter = "PAC Firmware Package (*.pac)|*.pac|All Files (*.*)|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    sprdInputPac.Text = ofd.FileName;
                    _spreadtrumController.LoadPacFirmware(ofd.FileName);
                }
            }
        }

        /// <summary>
        /// Write Partition - Simplified, direct select from partition table
        /// </summary>
        private async System.Threading.Tasks.Task SprdWritePartitionAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            // Get Selected Partition Name
            var partitions = GetSprdSelectedPartitions();
            
            // When no partition selected, provide selection
            if (partitions.Count == 0)
            {
                // If PAC exists, ask whether to flash entire PAC
                if (_spreadtrumController.CurrentPac != null)
                {
                    var confirm = MessageBox.Show(
                        "No partition selected. Flash entire PAC package?",
                        "Write Partition",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    
                    if (confirm == DialogResult.Yes)
                    {
                        await SprdWriteEntirePacAsync();
                    }
                    return;
                }
                
                AppendLog("[Spreadtrum] Please select partition to write in partition table", Color.Orange);
                return;
            }

            // Single Partition - Select Single File
            if (partitions.Count == 1)
            {
                string partName = partitions[0].name;
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = $"Select file to write to {partName} partition";
                    ofd.Filter = "Image File (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        AppendLog($"[Spreadtrum] Writing {partName}...", Color.Cyan);
                        await _spreadtrumController.FlashPartitionAsync(partName, ofd.FileName);
                    }
                }
                return;
            }

            // Multiple Partitions - Select Directory, Auto Match File Name
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = $"Select directory containing {partitions.Count} partition images\n(File name must match partition name, e.g. boot.img)";
                
                if (fbd.ShowDialog() != DialogResult.OK) return;

                string inputDir = fbd.SelectedPath;
                int success = 0, fail = 0, skip = 0;

                foreach (var (partName, _) in partitions)
                {
                    // Auto Find Matched Files
                    string imgPath = System.IO.Path.Combine(inputDir, $"{partName}.img");
                    string binPath = System.IO.Path.Combine(inputDir, $"{partName}.bin");
                    string filePath = System.IO.File.Exists(imgPath) ? imgPath : 
                                     (System.IO.File.Exists(binPath) ? binPath : null);

                    if (filePath == null)
                    {
                        AppendLog($"[Spreadtrum] Skip {partName} (File Not Found)", Color.Gray);
                        skip++;
                        continue;
                    }

                    AppendLog($"[Spreadtrum] Writing {partName}...", Color.Cyan);
                    if (await _spreadtrumController.FlashPartitionAsync(partName, filePath))
                        success++;
                    else
                        fail++;
                }

                AppendLog($"[Spreadtrum] Write Complete: {success} Success, {fail} Failed, {skip} Skipped", 
                    fail > 0 ? Color.Orange : Color.Green);
            }
        }

        private enum WriteMode { Cancel, SingleImage, MultipleImages, EntirePac }

        /// <summary>
        /// Show Write Mode Dialog (Deprecated, Keep Compatible)
        /// </summary>
        private WriteMode ShowWriteModeDialog(int selectedCount)
        {
            // Simplified, no longer use this dialog
            return WriteMode.Cancel;
        }

        /// <summary>
        /// Write Single Image File to Selected Partition
        /// </summary>
        private async System.Threading.Tasks.Task SprdWriteSingleImageAsync(List<string> selectedPartitions)
        {
            // If no partition selected, let user select
            string targetPartition;
            if (selectedPartitions.Count == 0)
            {
                // Let user input partition name
                targetPartition = Microsoft.VisualBasic.Interaction.InputBox(
                    "Please input target partition name:",
                    "Select Partition",
                    "boot",
                    -1, -1);
                if (string.IsNullOrEmpty(targetPartition))
                    return;
            }
            else if (selectedPartitions.Count == 1)
            {
                targetPartition = selectedPartitions[0];
            }
            else
            {
                // Multiple selected, write sequentially
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Image File";
                    ofd.Filter = "Image File (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";
                    ofd.Multiselect = true;

                    if (ofd.ShowDialog() != DialogResult.OK)
                        return;

                    if (ofd.FileNames.Length != selectedPartitions.Count)
                    {
                        MessageBox.Show(
                            $"Selected file count ({ofd.FileNames.Length}) does not match partition count ({selectedPartitions.Count})!\n\n" +
                            "Please ensure number of files matches number of selected partitions.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    var confirm = MessageBox.Show(
                        $"Are you sure you want to write {ofd.FileNames.Length} files to corresponding partitions?\n\nThis operation cannot be undone!",
                        "Confirm Write",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes)
                        return;

                    // Write in order
                    for (int i = 0; i < selectedPartitions.Count; i++)
                    {
                        AppendLog($"[Spreadtrum] Writing {selectedPartitions[i]}...", Color.Cyan);
                        bool success = await _spreadtrumController.FlashImageFileAsync(
                            selectedPartitions[i], ofd.FileNames[i]);
                        if (!success)
                        {
                            AppendLog($"[Spreadtrum] Write {selectedPartitions[i]} Failed", Color.Red);
                            return;
                        }
                    }
                    AppendLog("[Spreadtrum] All Partitions Write Complete", Color.Green);
                    return;
                }
            }

            // Single Partition Write
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"Select image file to write to {targetPartition}";
                ofd.Filter = "Image File (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                var confirm = MessageBox.Show(
                    $"Are you sure you want to write \"{System.IO.Path.GetFileName(ofd.FileName)}\" to partition \"{targetPartition}\"?\n\nThis operation cannot be undone!",
                    "Confirm Write",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                AppendLog($"[Spreadtrum] Writing {targetPartition}...", Color.Cyan);
                bool success = await _spreadtrumController.FlashImageFileAsync(targetPartition, ofd.FileName);
                if (success)
                    AppendLog($"[Spreadtrum] {targetPartition} Write Complete", Color.Green);
            }
        }

        /// <summary>
        /// Batch Write Multiple Image Files (Auto Match Partition Name)
        /// </summary>
        private async System.Threading.Tasks.Task SprdWriteMultipleImagesAsync()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Image File (Filename should be partition name)";
                ofd.Filter = "Image File (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                // Build partition-file map
                var partitionFiles = new Dictionary<string, string>();
                foreach (var file in ofd.FileNames)
                {
                    string partName = System.IO.Path.GetFileNameWithoutExtension(file);
                    partitionFiles[partName] = file;
                }

                string fileList = string.Join("\n", partitionFiles.Keys);
                var confirm = MessageBox.Show(
                    $"Will write to the following {partitionFiles.Count} partitions:\n\n{fileList}\n\nAre you sure to continue?",
                    "Confirm Batch Write",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                AppendLog($"[Spreadtrum] Start Batch Write {partitionFiles.Count} Partitions...", Color.Cyan);
                bool success = await _spreadtrumController.FlashMultipleImagesAsync(partitionFiles);
                if (success)
                    AppendLog("[Spreadtrum] Batch Write Complete", Color.Green);
            }
        }

        /// <summary>
        /// Flash Entire PAC Firmware Package
        /// </summary>
        private async System.Threading.Tasks.Task SprdWriteEntirePacAsync()
        {
            if (_spreadtrumController.CurrentPac == null)
            {
                AppendLog("[Spreadtrum] Please select PAC Firmware Package first", Color.Orange);
                return;
            }

            // Get Selected Partitions (Checked)
            var selectedPartitions = new List<string>();
            foreach (ListViewItem item in sprdListPartitions.CheckedItems)
            {
                selectedPartitions.Add(item.Text);
            }

            if (selectedPartitions.Count == 0)
            {
                // No checked, flash all partitions
                var result = MessageBox.Show(
                    "No partition selected, will flash all partitions in PAC.\n\nAre you sure to continue?",
                    "Confirm Flash",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    return;

                // Add all partitions
                foreach (ListViewItem item in sprdListPartitions.Items)
                {
                    selectedPartitions.Add(item.Text);
                }
            }
            else
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to flash {selectedPartitions.Count} partitions?\n\nThis operation cannot be undone!",
                    "Confirm Flash",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    return;
            }

            // If not connected, wait for connection
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Waiting for device connection, please connect device to PC (Hold Volume Down)...", Color.Yellow);
                bool connected = await _spreadtrumController.WaitAndConnectAsync(60);
                if (!connected)
                {
                    AppendLog("[Spreadtrum] Device Connection Timeout", Color.Red);
                    return;
                }
            }

            bool success = await _spreadtrumController.StartFlashAsync(selectedPartitions);

            // Reboot after flash
            if (success && sprdChkRebootAfter.Checked)
            {
                await _spreadtrumController.RebootDeviceAsync();
            }
        }

        /// <summary>
        /// Read GPT - Auto Connect, Download FDL, Read Process
        /// </summary>
        private async System.Threading.Tasks.Task SprdReadPartitionTableAsync()
        {
            // 1. Check connection status, try connecting to detected device if not connected
            if (!_spreadtrumController.IsConnected)
            {
                if (string.IsNullOrEmpty(_detectedSprdPort))
                {
                    AppendLog("[Spreadtrum] Device not detected, please connect device to PC and enter Download Mode", Color.Orange);
                    AppendLog("[Spreadtrum] (Hold Volume Down while Power Off, then connect USB)", Color.Gray);
                    return;
                }
                
                AppendLog($"[Spreadtrum] Connecting device: {_detectedSprdPort}...", Color.Cyan);
                bool connected = await _spreadtrumController.ConnectDeviceAsync(_detectedSprdPort);
                if (!connected)
                {
                    AppendLog("[Spreadtrum] Device Connection Failed", Color.Red);
                    return;
                }
                AppendLog("[Spreadtrum] Device Connected Successfully", Color.Green);
            }

            // 2. If already in FDL2 mode, read partition table directly
            if (_spreadtrumController.CurrentStage == FdlStage.FDL2)
            {
                AppendLog("[Spreadtrum] Reading Partition Table...", Color.Cyan);
                await _spreadtrumController.ReadPartitionTableAsync();
                return;
            }

            // 3. BROM Mode needs FDL download
            if (_spreadtrumController.IsBromMode)
            {
                AppendLog("[Spreadtrum] Device in BROM mode, downloading FDL...", Color.Yellow);
                
                // Check FDL Source Priority: PAC > Custom FDL > Database Chip Config
                bool hasFdlConfig = false;
                
                // Method 1: FDL in PAC (Highest Priority)
                if (_spreadtrumController.CurrentPac != null)
                {
                    AppendLog("[Spreadtrum] Initialize device using FDL in PAC...", Color.Cyan);
                    hasFdlConfig = true;
                }
                // Method 2: User Custom FDL File (Second Priority)
                else if (!string.IsNullOrEmpty(_customFdl1Path) && System.IO.File.Exists(_customFdl1Path) &&
                         !string.IsNullOrEmpty(_customFdl2Path) && System.IO.File.Exists(_customFdl2Path))
                {
                    AppendLog("[Spreadtrum] Using Custom FDL File...", Color.Cyan);
                    AppendLog($"[Spreadtrum] FDL1: {System.IO.Path.GetFileName(_customFdl1Path)}", Color.Gray);
                    AppendLog($"[Spreadtrum] FDL2: {System.IO.Path.GetFileName(_customFdl2Path)}", Color.Gray);
                    
                    // If chip selected, use chip address config
                    if (_selectedChipId > 0 && _selectedChipId != 0xFFFF)
                    {
                        var chipInfo = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetChipById(_selectedChipId);
                        if (chipInfo != null)
                        {
                            AppendLog($"[Spreadtrum] Using Chip Address Config: {chipInfo.ChipName}", Color.Gray);
                            _spreadtrumController.SetCustomFdl1(_customFdl1Path, chipInfo.Fdl1Address);
                            _spreadtrumController.SetCustomFdl2(_customFdl2Path, chipInfo.Fdl2Address);
                        }
                        else
                        {
                            _spreadtrumController.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                            _spreadtrumController.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                        }
                    }
                    else
                    {
                        _spreadtrumController.SetCustomFdl1(_customFdl1Path, _customFdl1Addr);
                        _spreadtrumController.SetCustomFdl2(_customFdl2Path, _customFdl2Addr);
                    }
                    hasFdlConfig = true;
                }
                // Method 3: Database Chip Config (Third Priority)
                else if (_selectedChipId > 0 && _selectedChipId != 0xFFFF)
                {
                    var chipInfo = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetChipById(_selectedChipId);
                    if (chipInfo != null)
                    {
                        AppendLog($"[Spreadtrum] Using Chip Config: {chipInfo.ChipName}", Color.Cyan);
                        AppendLog($"[Spreadtrum] FDL1 Addr: {chipInfo.Fdl1AddressHex}, FDL2 Addr: {chipInfo.Fdl2AddressHex}", Color.Gray);
                        
                        // Check if there is device FDL file for this chip
                        var devices = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetDeviceNames(chipInfo.ChipName);
                        if (devices.Length > 0)
                        {
                            // Use first device FDL (or let user select)
                            var deviceFdl = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetDeviceFdlsByChip(chipInfo.ChipName).FirstOrDefault();
                            if (deviceFdl != null)
                            {
                                string fdl1Path = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetFdlPath(deviceFdl, true);
                                string fdl2Path = LoveAlways.Spreadtrum.Database.SprdFdlDatabase.GetFdlPath(deviceFdl, false);
                                
                                if (System.IO.File.Exists(fdl1Path) && System.IO.File.Exists(fdl2Path))
                                {
                                    AppendLog($"[Spreadtrum] Using Device FDL: {deviceFdl.DeviceName}", Color.Gray);
                                    _spreadtrumController.SetCustomFdl1(fdl1Path, chipInfo.Fdl1Address);
                                    _spreadtrumController.SetCustomFdl2(fdl2Path, chipInfo.Fdl2Address);
                                    hasFdlConfig = true;
                                }
                            }
                        }
                        
                        if (!hasFdlConfig)
                        {
                            // Database has no FDL, prompt user to select manually
                            AppendLog("[Spreadtrum] FDL file for this chip not found in database", Color.Orange);
                            AppendLog("[Spreadtrum] Please double click FDL1/FDL2 input box to select file", Color.Orange);
                            return;
                        }
                    }
                }
                
                if (!hasFdlConfig)
                {
                    AppendLog("[Spreadtrum] Error: No available FDL config", Color.Red);
                    AppendLog("[Spreadtrum] Please perform one of the following:", Color.Orange);
                    AppendLog("[Spreadtrum]   1. Load PAC Firmware Package", Color.Gray);
                    AppendLog("[Spreadtrum]   2. Double click FDL1/FDL2 input box to select file", Color.Gray);
                    AppendLog("[Spreadtrum]   3. Select Chip Model (Database needs corresponding FDL)", Color.Gray);
                    return;
                }
                
                // Initialize Device (Download FDL1 and FDL2)
                AppendLog("[Spreadtrum] Initializing Device (Downloading FDL)...", Color.Yellow);
                bool initialized = await _spreadtrumController.InitializeDeviceAsync();
                if (!initialized)
                {
                    AppendLog("[Spreadtrum] Device Initialization Failed", Color.Red);
                    return;
                }
                AppendLog("[Spreadtrum] Device Initialization Complete", Color.Green);
            }

            // 4. Read GPT
            AppendLog("[Spreadtrum] Reading Partition Table...", Color.Cyan);
            await _spreadtrumController.ReadPartitionTableAsync();
        }

        /// <summary>
        /// Read Partition - Simplified, direct select from partition table
        /// </summary>
        private async System.Threading.Tasks.Task SprdReadPartitionAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            // Get Selected Partitions (Checked first, otherwise use selected)
            var partitions = GetSprdSelectedPartitions();
            if (partitions.Count == 0)
            {
                AppendLog("[Spreadtrum] Please select partition to read in partition table", Color.Orange);
                return;
            }

            // Single Partition - Select Save File
            if (partitions.Count == 1)
            {
                var (partName, size) = partitions[0];
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = $"Save {partName}";
                    sfd.FileName = $"{partName}.img";
                    sfd.Filter = "Image File (*.img)|*.img|All Files (*.*)|*.*";

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        AppendLog($"[Spreadtrum] Reading partition {partName}...", Color.Cyan);
                        await _spreadtrumController.ReadPartitionToFileAsync(partName, sfd.FileName, size);
                    }
                }
                return;
            }

            // Multiple Partitions - Select Save Directory
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = $"Select directory to save {partitions.Count} partitions";
                
                if (fbd.ShowDialog() != DialogResult.OK) return;

                string outputDir = fbd.SelectedPath;
                int success = 0, fail = 0;

                foreach (var (partName, size) in partitions)
                {
                    string outputPath = System.IO.Path.Combine(outputDir, $"{partName}.img");
                    AppendLog($"[Spreadtrum] Reading {partName}...", Color.Cyan);
                    
                    if (await _spreadtrumController.ReadPartitionToFileAsync(partName, outputPath, size))
                        success++;
                    else
                        fail++;
                }

                AppendLog($"[Spreadtrum] Read Complete: {success} Success, {fail} Failed", 
                    fail > 0 ? Color.Orange : Color.Green);
            }
        }

        /// <summary>
        /// Get Selected Partitions in Spreadtrum Partition Table (Checked first, otherwise use selected)
        /// </summary>
        private List<(string name, uint size)> GetSprdSelectedPartitions()
        {
            var result = new List<(string name, uint size)>();
            
            // Checked first, otherwise use blue selected
            var items = sprdListPartitions.CheckedItems.Count > 0 
                ? (System.Collections.IEnumerable)sprdListPartitions.CheckedItems 
                : sprdListPartitions.SelectedItems;

            foreach (ListViewItem item in items)
            {
                string partName = item.Text;
                
                // Get Partition Size (Prefer real size from controller)
                uint size = _spreadtrumController.GetPartitionSize(partName);
                
                // If not, parse from list
                if (size == 0 && item.SubItems.Count > 2)
                    TryParseSize(item.SubItems[2].Text, out size);
                
                // Default 100MB
                if (size == 0)
                    size = 100 * 1024 * 1024;
                
                result.Add((partName, size));
            }
            
            return result;
        }

        /// <summary>
        /// Parse Size String (e.g. "100 MB", "1.5 GB")
        /// </summary>
        private bool TryParseSize(string sizeText, out uint size)
        {
            size = 0;
            if (string.IsNullOrEmpty(sizeText))
                return false;

            sizeText = sizeText.Trim().ToUpper();
            
            try
            {
                if (sizeText.EndsWith("GB"))
                {
                    double gb = double.Parse(sizeText.Replace("GB", "").Trim());
                    size = (uint)(gb * 1024 * 1024 * 1024);
                    return true;
                }
                else if (sizeText.EndsWith("MB"))
                {
                    double mb = double.Parse(sizeText.Replace("MB", "").Trim());
                    size = (uint)(mb * 1024 * 1024);
                    return true;
                }
                else if (sizeText.EndsWith("KB"))
                {
                    double kb = double.Parse(sizeText.Replace("KB", "").Trim());
                    size = (uint)(kb * 1024);
                    return true;
                }
                else if (sizeText.EndsWith("B"))
                {
                    size = uint.Parse(sizeText.Replace("B", "").Trim());
                    return true;
                }
                else
                {
                    // Try parse as number directly
                    size = uint.Parse(sizeText);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Erase Partition - Simplified, support batch erase
        /// </summary>
        private async System.Threading.Tasks.Task SprdErasePartitionAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            var partitions = GetSprdSelectedPartitions();
            if (partitions.Count == 0)
            {
                AppendLog("[Spreadtrum] Please select partition to erase in partition table", Color.Orange);
                return;
            }

            // Confirm Erase
            string partNames = string.Join(", ", partitions.ConvertAll(p => p.name));
            string message = partitions.Count == 1 
                ? $"Are you sure to erase partition \"{partitions[0].name}\"?"
                : $"Are you sure to erase {partitions.Count} partitions?\n\n{partNames}";

            var result = MessageBox.Show(
                message + "\n\nThis operation cannot be undone!",
                "Confirm Erase",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            int success = 0, fail = 0;
            foreach (var (partName, _) in partitions)
            {
                AppendLog($"[Spreadtrum] Erasing {partName}...", Color.Yellow);
                if (await _spreadtrumController.ErasePartitionAsync(partName))
                    success++;
                else
                    fail++;
            }

            if (partitions.Count > 1)
            {
                AppendLog($"[Spreadtrum] Erase Complete: {success} Success, {fail} Failed", 
                    fail > 0 ? Color.Orange : Color.Green);
            }
        }

        /// <summary>
        /// Extract PAC File
        /// </summary>
        private async System.Threading.Tasks.Task SprdExtractPacAsync()
        {
            if (_spreadtrumController.CurrentPac == null)
            {
                AppendLog("[Spreadtrum] Please select PAC Firmware Package first", Color.Orange);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Extraction Directory";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    await _spreadtrumController.ExtractPacAsync(fbd.SelectedPath);
                    AppendLog($"[Spreadtrum] PAC Extraction Complete: {fbd.SelectedPath}", Color.Green);
                }
            }
        }

        /// <summary>
        /// Format File Size
        /// </summary>
        private string FormatSize(ulong size)
        {
            if (size >= 1024UL * 1024 * 1024)
                return $"{size / (1024.0 * 1024 * 1024):F2} GB";
            if (size >= 1024 * 1024)
                return $"{size / (1024.0 * 1024):F2} MB";
            if (size >= 1024)
                return $"{size / 1024.0:F2} KB";
            return $"{size} B";
        }

        /// <summary>
        /// Parse Hex Address (Support 0x prefix)
        /// </summary>
        private bool TryParseHexAddress(string text, out uint address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            
            // Remove 0x or 0X prefix
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        /// <summary>
        /// Set FDL Input Control Enable State
        /// </summary>
        /// <param name="enabled">Enabled</param>
        /// <param name="clearPaths">Clear previously selected file paths</param>
        private void SetFdlInputsEnabled(bool enabled, bool clearPaths = false)
        {
            // FDL1 File - Always enabled, allow user overwrite
            input2.Enabled = true;
            // FDL2 File - Always enabled, allow user overwrite
            input4.Enabled = true;
            // FDL1 Address - Decided by param
            input5.Enabled = enabled;
            // FDL2 Address - Decided by param
            input10.Enabled = enabled;

            if (clearPaths)
            {
                // Clear file paths only when explicitly requested
                _customFdl1Path = null;
                _customFdl2Path = null;
                input2.Text = "";
                input4.Text = "";
            }
        }

        /// <summary>
        /// Auto Detect Security Info and Vulnerability
        /// </summary>
        private async Task SprdAutoDetectSecurityAsync()
        {
            try
            {
                AppendLog("[Spreadtrum] Auto detecting security info...", Color.Gray);

                // 1. Read Security Info
                var secInfo = await _spreadtrumController.GetSecurityInfoAsync();
                if (secInfo != null)
                {
                    // Show Security Status
                    if (!secInfo.IsSecureBootEnabled)
                    {
                        AppendLog("[Spreadtrum] ✓ Secure Boot: Disabled (Unfused) - Can flash any firmware", Color.Green);
                    }
                    else
                    {
                        AppendLog("[Spreadtrum] Secure Boot: Enabled", Color.Yellow);
                        
                        if (secInfo.IsEfuseLocked)
                            AppendLog("[Spreadtrum]   eFuse: Locked", Color.Gray);
                        
                        if (secInfo.IsAntiRollbackEnabled)
                            AppendLog($"[Spreadtrum]   Anti-Rollback: Enabled (Ver {secInfo.SecurityVersion})", Color.Gray);
                    }
                }

                // 2. Auto Detect Vulnerability
                var vulnResult = _spreadtrumController.CheckVulnerability();
                if (vulnResult != null && vulnResult.HasVulnerability)
                {
                    AppendLog($"[Spreadtrum] ✓ Detected {vulnResult.AvailableExploits.Count} available exploits", Color.Yellow);
                    AppendLog($"[Spreadtrum]   Recommended: {vulnResult.RecommendedExploit}", Color.Gray);
                }

                // 3. Read Flash Info
                var flashInfo = await _spreadtrumController.GetFlashInfoAsync();
                if (flashInfo != null)
                {
                    AppendLog($"[Spreadtrum] Flash: {flashInfo}", Color.Gray);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Spreadtrum] Security Detect Exception: {ex.Message}", Color.Orange);
            }
        }

        /// <summary>
        /// Update Device Info (Top Right)
        /// </summary>
        private async Task SprdUpdateDeviceInfoAsync()
        {
            try
            {
                // Read Chip Info
                string chipName = await _spreadtrumController.ReadChipInfoAsync();
                if (!string.IsNullOrEmpty(chipName))
                {
                    SafeInvoke(() =>
                    {
                        uiLabel9.Text = $"Brand: Spreadtrum/Unisoc";
                        uiLabel11.Text = $"Chip: {chipName}";
                    });
                }

                // Read Flash Info
                var flashInfo = await _spreadtrumController.GetFlashInfoAsync();
                if (flashInfo != null)
                {
                    SafeInvoke(() =>
                    {
                        uiLabel13.Text = $"Storage: {flashInfo}";
                    });
                }

                // Try Read IMEI
                string imei = await _spreadtrumController.ReadImeiAsync();
                if (!string.IsNullOrEmpty(imei))
                {
                    SafeInvoke(() =>
                    {
                        uiLabel10.Text = $"IMEI: {imei}";
                    });
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Spreadtrum] Read Device Info Exception: {ex.Message}", Color.Orange);
            }
        }

        /// <summary>
        /// Clear Device Info Display
        /// </summary>
        private void SprdClearDeviceInfo()
        {
            uiLabel9.Text = "Platform: Spreadtrum";
            uiLabel11.Text = "Chip: Waiting";
            uiLabel13.Text = "Stage: --";
            uiLabel10.Text = "IMEI: --";
            uiLabel3.Text = "Port: Waiting";
            uiLabel12.Text = "Mode: --";
            uiLabel14.Text = "Status: Disconnected";
        }

        /// <summary>
        /// Update Right Info Panel for Spreadtrum Display
        /// </summary>
        private void UpdateSprdInfoPanel()
        {
            if (_spreadtrumController != null && _spreadtrumController.IsConnected)
            {
                // Connected State
                string chipName = "Unknown";
                uint chipId = _spreadtrumController.GetChipId();
                if (chipId > 0)
                {
                    chipName = LoveAlways.Spreadtrum.Protocol.SprdPlatform.GetPlatformName(chipId);
                }
                
                string stageStr = _spreadtrumController.CurrentStage.ToString();
                
                uiLabel9.Text = "Platform: Spreadtrum";
                uiLabel11.Text = $"Chip: {chipName}";
                uiLabel13.Text = $"Stage: {stageStr}";
                uiLabel10.Text = "IMEI: --";
                uiLabel3.Text = $"Port: {_detectedSprdPort ?? "--"}";
                uiLabel12.Text = $"Mode: {_detectedSprdMode}";
                uiLabel14.Text = "Status: Connected";
            }
            else if (!string.IsNullOrEmpty(_detectedSprdPort))
            {
                // Device detected but not connected
                uiLabel9.Text = "Platform: Spreadtrum";
                uiLabel11.Text = "Chip: Waiting Init";
                uiLabel13.Text = "Stage: --";
                uiLabel10.Text = "IMEI: --";
                uiLabel3.Text = $"Port: {_detectedSprdPort}";
                uiLabel12.Text = $"Mode: {_detectedSprdMode}";
                uiLabel14.Text = "Status: Pending Connect";
            }
            else
            {
                // Device not detected
                SprdClearDeviceInfo();
            }
        }

        #region IMEI Read/Write

        /// <summary>
        /// Backup Calibration Data
        /// </summary>
        private async Task SprdBackupCalibrationAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Calibration Data Backup Directory";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    AppendLog("[Spreadtrum] Backing up calibration data...", Color.Cyan);
                    bool success = await _spreadtrumController.BackupCalibrationDataAsync(fbd.SelectedPath);
                    if (success)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", fbd.SelectedPath);
                    }
                }
            }
        }

        /// <summary>
        /// Restore Calibration Data
        /// </summary>
        private async Task SprdRestoreCalibrationAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select directory containing calibration backup";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    var result = MessageBox.Show(
                        "Are you sure you want to restore calibration data?\n\nThis will overwrite current calibration data!",
                        "Confirm Restore",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        AppendLog("[Spreadtrum] Restoring calibration data...", Color.Cyan);
                        await _spreadtrumController.RestoreCalibrationDataAsync(fbd.SelectedPath);
                    }
                }
            }
        }

        /// <summary>
        /// Factory Reset
        /// </summary>
        private async Task SprdFactoryResetAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to factory reset?\n\nThis will erase:\n- Userdata (userdata)\n- Cache (cache)\n- Metadata (metadata)\n\nThis operation cannot be undone!",
                "Confirm Factory Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                AppendLog("[Spreadtrum] Performing factory reset...", Color.Yellow);
                bool success = await _spreadtrumController.FactoryResetAsync();
                if (success)
                {
                    MessageBox.Show("Factory reset complete!\n\nDevice will reboot automatically.", "Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await _spreadtrumController.RebootDeviceAsync();
                }
            }
        }

        /// <summary>
        /// Read IMEI
        /// </summary>
        private async Task SprdReadImeiAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            AppendLog("[Spreadtrum] Reading IMEI...", Color.Cyan);
            
            string imei = await _spreadtrumController.ReadImeiAsync();
            if (!string.IsNullOrEmpty(imei))
            {
                AppendLog($"[Spreadtrum] IMEI: {imei}", Color.Green);
                SafeInvoke(() =>
                {
                    uiLabel10.Text = $"IMEI: {imei}";
                });
                
                // Copy to clipboard
                Clipboard.SetText(imei);
                AppendLog("[Spreadtrum] IMEI copied to clipboard", Color.Gray);
            }
            else
            {
                AppendLog("[Spreadtrum] Read IMEI Failed", Color.Red);
            }
        }

        /// <summary>
        /// Write IMEI
        /// </summary>
        private async Task SprdWriteImeiAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            // Pop input box
            string newImei = Microsoft.VisualBasic.Interaction.InputBox(
                "Please input new IMEI (15 digits):",
                "Write IMEI",
                "",
                -1, -1);

            if (string.IsNullOrEmpty(newImei))
            {
                AppendLog("[Spreadtrum] Write IMEI Cancelled", Color.Gray);
                return;
            }

            // Validate IMEI format
            newImei = newImei.Trim();
            if (newImei.Length != 15 || !newImei.All(char.IsDigit))
            {
                AppendLog("[Spreadtrum] IMEI format error, must be 15 digits", Color.Red);
                MessageBox.Show("IMEI format error!\n\nIMEI must be 15 digits", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Confirm
            var result = MessageBox.Show(
                $"Are you sure you want to write IMEI as:\n\n{newImei}\n\nThis may affect network functionality!",
                "Confirm Write IMEI",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            AppendLog($"[Spreadtrum] Writing IMEI: {newImei}...", Color.Yellow);

            bool success = await _spreadtrumController.WriteImeiAsync(newImei);
            if (success)
            {
                AppendLog("[Spreadtrum] IMEI Write Success", Color.Green);
                SafeInvoke(() =>
                {
                    uiLabel10.Text = $"IMEI: {newImei}";
                });
                MessageBox.Show("IMEI Write Success!\n\nReboot recommended to take effect.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog("[Spreadtrum] IMEI Write Failed", Color.Red);
                MessageBox.Show("IMEI Write Failed!\n\nPlease check device connection.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region NV Read/Write

        /// <summary>
        /// Open NV Manager Dialog
        /// </summary>
        private Task SprdOpenNvManagerAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return Task.CompletedTask;
            }

            // Show NV Operation Selection Menu
            var menu = new ContextMenuStrip();
            menu.Items.Add("Read Bluetooth Address", null, async (s, e) => await SprdReadNvAsync(LoveAlways.Spreadtrum.Protocol.SprdNvItems.NV_BT_ADDR, "Bluetooth Address"));
            menu.Items.Add("Read WiFi Address", null, async (s, e) => await SprdReadNvAsync(LoveAlways.Spreadtrum.Protocol.SprdNvItems.NV_WIFI_ADDR, "WiFi Address"));
            menu.Items.Add("Read Serial Number", null, async (s, e) => await SprdReadNvAsync(LoveAlways.Spreadtrum.Protocol.SprdNvItems.NV_SERIAL_NUMBER, "Serial Number"));
            menu.Items.Add("-");
            menu.Items.Add("Write Bluetooth Address...", null, async (s, e) => await SprdWriteNvAsync(LoveAlways.Spreadtrum.Protocol.SprdNvItems.NV_BT_ADDR, "Bluetooth Address", 6));
            menu.Items.Add("Write WiFi Address...", null, async (s, e) => await SprdWriteNvAsync(LoveAlways.Spreadtrum.Protocol.SprdNvItems.NV_WIFI_ADDR, "WiFi Address", 6));
            menu.Items.Add("-");
            menu.Items.Add("Read Custom NV Item...", null, async (s, e) => await SprdReadCustomNvAsync());
            menu.Items.Add("Write Custom NV Item...", null, async (s, e) => await SprdWriteCustomNvAsync());
            
            // Show menu at button position
            menu.Show(Cursor.Position);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Read Specific NV Item
        /// </summary>
        private async Task SprdReadNvAsync(ushort itemId, string itemName)
        {
            AppendLog($"[Spreadtrum] Reading NV Item: {itemName} (ID={itemId})...", Color.Cyan);

            var data = await _spreadtrumController.ReadNvItemAsync(itemId);
            if (data != null && data.Length > 0)
            {
                string hexStr = BitConverter.ToString(data).Replace("-", ":");
                AppendLog($"[Spreadtrum] {itemName}: {hexStr}", Color.Green);
                
                // Copy to clipboard
                Clipboard.SetText(hexStr);
                AppendLog("[Spreadtrum] Copied to clipboard", Color.Gray);
                
                MessageBox.Show($"{itemName}:\n\n{hexStr}\n\nCopied to clipboard", "NV Read", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog($"[Spreadtrum] Read {itemName} Failed", Color.Red);
            }
        }

        /// <summary>
        /// Write Specific NV Item (MAC Address Format)
        /// </summary>
        private async Task SprdWriteNvAsync(ushort itemId, string itemName, int expectedLength)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Please input {itemName}:\n\nFormat: XX:XX:XX:XX:XX:XX (6 bytes hex)",
                $"Write {itemName}",
                "",
                -1, -1);

            if (string.IsNullOrEmpty(input))
                return;

            // Parse MAC Address Format
            input = input.Trim().ToUpper().Replace("-", ":").Replace(" ", ":");
            string[] parts = input.Split(':');
            
            if (parts.Length != expectedLength)
            {
                MessageBox.Show($"Format Error!\n\nShould be {expectedLength} bytes", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            byte[] data = new byte[expectedLength];
            try
            {
                for (int i = 0; i < expectedLength; i++)
                {
                    data[i] = Convert.ToByte(parts[i], 16);
                }
            }
            catch
            {
                MessageBox.Show("Format Error!\n\nPlease use Hex format", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure to write {itemName}?\n\n{input}",
                "Confirm Write",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            AppendLog($"[Spreadtrum] Writing NV Item: {itemName}...", Color.Yellow);

            bool success = await _spreadtrumController.WriteNvItemAsync(itemId, data);
            if (success)
            {
                AppendLog($"[Spreadtrum] {itemName} Write Success", Color.Green);
                MessageBox.Show($"{itemName} Write Success!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog($"[Spreadtrum] {itemName} Write Failed", Color.Red);
                MessageBox.Show($"{itemName} Write Failed!", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Read Custom NV Item
        /// </summary>
        private async Task SprdReadCustomNvAsync()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Please input NV Item ID (0-65535):",
                "Read Custom NV",
                "0",
                -1, -1);

            if (string.IsNullOrEmpty(input))
                return;

            ushort itemId;
            if (!ushort.TryParse(input.Trim(), out itemId))
            {
                // Try Parse Hex
                if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        itemId = Convert.ToUInt16(input.Substring(2), 16);
                    }
                    catch
                    {
                        MessageBox.Show("ID Format Error!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("ID Format Error!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            AppendLog($"[Spreadtrum] Reading NV Item ID={itemId}...", Color.Cyan);

            var data = await _spreadtrumController.ReadNvItemAsync(itemId);
            if (data != null && data.Length > 0)
            {
                string hexStr = BitConverter.ToString(data).Replace("-", " ");
                AppendLog($"[Spreadtrum] NV[{itemId}]: {hexStr}", Color.Green);
                
                // Try decode as string
                string asciiStr = "";
                try
                {
                    asciiStr = System.Text.Encoding.ASCII.GetString(data).TrimEnd('\0');
                }
                catch { }

                Clipboard.SetText(hexStr);
                
                string msg = $"NV[{itemId}] Length: {data.Length} bytes\n\n";
                msg += $"HEX: {hexStr}\n\n";
                if (!string.IsNullOrEmpty(asciiStr) && asciiStr.All(c => c >= 0x20 && c < 0x7F))
                    msg += $"ASCII: {asciiStr}\n\n";
                msg += "HEX copied to clipboard";
                
                MessageBox.Show(msg, "NV Read", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog($"[Spreadtrum] Read NV[{itemId}] Failed", Color.Red);
                MessageBox.Show($"Read NV[{itemId}] Failed!\n\nItem may not exist", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Write Custom NV Item
        /// </summary>
        private async Task SprdWriteCustomNvAsync()
        {
            string idInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Please input NV Item ID (0-65535):",
                "Write Custom NV",
                "0",
                -1, -1);

            if (string.IsNullOrEmpty(idInput))
                return;

            ushort itemId;
            if (!ushort.TryParse(idInput.Trim(), out itemId))
            {
                if (idInput.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    try { itemId = Convert.ToUInt16(idInput.Substring(2), 16); }
                    catch { MessageBox.Show("ID Format Error!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                }
                else
                {
                    MessageBox.Show("ID Format Error!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            string dataInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Please input data (Hex, space separated):\n\nExample: 01 02 03 04 05 06",
                $"Write NV[{itemId}]",
                "",
                -1, -1);

            if (string.IsNullOrEmpty(dataInput))
                return;

            // Parse Hex Data
            byte[] data;
            try
            {
                string[] parts = dataInput.Trim().Split(new char[] { ' ', ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
                data = parts.Select(p => Convert.ToByte(p, 16)).ToArray();
            }
            catch
            {
                MessageBox.Show("Data Format Error!\n\nPlease use Hex format, space separated", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to write NV[{itemId}]?\n\nLength: {data.Length} bytes\nData: {BitConverter.ToString(data).Replace("-", " ")}",
                "Confirm Write",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            AppendLog($"[Spreadtrum] Writing NV[{itemId}]...", Color.Yellow);

            bool success = await _spreadtrumController.WriteNvItemAsync(itemId, data);
            if (success)
            {
                AppendLog($"[Spreadtrum] NV[{itemId}] Write Success", Color.Green);
                MessageBox.Show($"NV[{itemId}] Write Success!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog($"[Spreadtrum] NV[{itemId}] Write Failed", Color.Red);
                MessageBox.Show($"NV[{itemId}] Write Failed!", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Bootloader Unlock

        /// <summary>
        /// Unlock Bootloader
        /// </summary>
        private async Task SprdUnlockBootloaderAsync()
        {
            if (!_spreadtrumController.IsConnected)
            {
                AppendLog("[Spreadtrum] Please connect device first", Color.Orange);
                return;
            }

            // Get Current Lock Status
            AppendLog("[Spreadtrum] Checking Bootloader Status...", Color.Cyan);
            var blStatus = await _spreadtrumController.GetBootloaderStatusAsync();
            
            if (blStatus == null)
            {
                AppendLog("[Spreadtrum] Failed to get Bootloader Status", Color.Red);
                return;
            }

            if (blStatus.IsUnlocked)
            {
                AppendLog("[Spreadtrum] Bootloader already unlocked", Color.Green);
                MessageBox.Show("Bootloader already unlocked!\n\nNo repetition required.", "Tip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Show Warning
            var result = MessageBox.Show(
                "⚠️ WARNING: Unlocking Bootloader will have the following consequences:\n\n" +
                "1. All user data will be erased\n" +
                "2. Device warranty may be voided\n" +
                "3. Some payment/banking apps may not work\n" +
                "4. OTA updates may fail\n\n" +
                $"Device Model: {blStatus.DeviceModel}\n" +
                $"Security Version: {blStatus.SecurityVersion}\n" +
                $"Unfused: {(blStatus.IsUnfused ? "Yes" : "No")}\n\n" +
                "Are you sure to continue unlocking?",
                "Unlock Bootloader",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                AppendLog("[Spreadtrum] Unlock Cancelled", Color.Gray);
                return;
            }

            // Double Confirm
            string confirmCode = Microsoft.VisualBasic.Interaction.InputBox(
                "Please input \"UNLOCK\" to confirm:",
                "Confirm Unlock",
                "",
                -1, -1);

            if (confirmCode?.ToUpper() != "UNLOCK")
            {
                AppendLog("[Spreadtrum] Confirm code error, operation cancelled", Color.Orange);
                return;
            }

            AppendLog("[Spreadtrum] Starting Unlock Bootloader...", Color.Yellow);

            // Check if exploit unlock is possible
            if (blStatus.IsUnfused)
            {
                AppendLog("[Spreadtrum] Unfused device detected, using signature bypass unlock", Color.Cyan);
                bool success = await _spreadtrumController.UnlockBootloaderAsync(true);
                if (success)
                {
                    AppendLog("[Spreadtrum] Bootloader Unlock Success!", Color.Green);
                    MessageBox.Show("Bootloader Unlock Success!\n\nDevice will reboot to Fastboot mode.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendLog("[Spreadtrum] Unlock Failed", Color.Red);
                    MessageBox.Show("Bootloader Unlock Failed!\n\nPlease check device support.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Need Manufacturer Unlock Code
                string unlockCode = Microsoft.VisualBasic.Interaction.InputBox(
                    "This device needs manufacturer unlock code.\n\nPlease input code (16 hex chars):\n\nTip: You can apply from manufacturer website",
                    "Input Unlock Code",
                    "",
                    -1, -1);

                if (string.IsNullOrEmpty(unlockCode))
                {
                    AppendLog("[Spreadtrum] Unlock Cancelled", Color.Gray);
                    return;
                }

                unlockCode = unlockCode.Trim().ToUpper();
                
                // Validate Format
                if (unlockCode.Length != 16 || !System.Text.RegularExpressions.Regex.IsMatch(unlockCode, "^[0-9A-F]+$"))
                {
                    AppendLog("[Spreadtrum] Unlock Code Format Error", Color.Red);
                    MessageBox.Show("Unlock Code Format Error!\n\nShould be 16 hex chars", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                bool success = await _spreadtrumController.UnlockBootloaderWithCodeAsync(unlockCode);
                if (success)
                {
                    AppendLog("[Spreadtrum] Bootloader Unlock Success!", Color.Green);
                    MessageBox.Show("Bootloader Unlock Success!\n\nDevice will reboot.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendLog("[Spreadtrum] Unlock Failed, code may be incorrect", Color.Red);
                    MessageBox.Show("Bootloader Unlock Failed!\n\nCode may be incorrect, or device not supported.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #endregion

        /// <summary>
        /// Backup Selected Partitions (Support Multiple)
        /// </summary>
        private async Task SprdBackupSelectedPartitionsAsync()
        {
            if (sprdListPartitions.CheckedItems.Count == 0)
            {
                AppendLog("[Spreadtrum] Please check partitions to backup", Color.Orange);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Backup Save Directory";

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    int total = sprdListPartitions.CheckedItems.Count;
                    int success = 0;

                    AppendLog($"[Spreadtrum] Starting backup {total} partitions...", Color.Cyan);

                    foreach (ListViewItem item in sprdListPartitions.CheckedItems)
                    {
                        string partName = item.Text;
                        string outputPath = Path.Combine(fbd.SelectedPath, $"{partName}.img");

                        AppendLog($"[Spreadtrum] Backup: {partName}...", Color.White);

                        // Get Partition Size
                        uint size = 0;
                        if (item.Tag is SprdPartitionInfo partInfo)
                        {
                            size = partInfo.Size;
                        }

                        bool result = await _spreadtrumController.ReadPartitionToFileAsync(partName, outputPath, size);
                        if (result)
                        {
                            success++;
                            AppendLog($"[Spreadtrum] {partName} Backup Success", Color.Gray);
                        }
                        else
                        {
                            AppendLog($"[Spreadtrum] {partName} Backup Failed", Color.Orange);
                        }
                    }

                    AppendLog($"[Spreadtrum] Backup Complete: {success}/{total} Success", success == total ? Color.Green : Color.Orange);

                    if (success > 0)
                    {
                        // Open Backup Directory
                        System.Diagnostics.Process.Start("explorer.exe", fbd.SelectedPath);
                    }
                }
            }
        }
    }
}
