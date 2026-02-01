// LoveAlways - Form1.MediaTek.cs
// MediaTek Platform UI Integration
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.MediaTek.Common;
using LoveAlways.MediaTek.Database;
using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;
using LoveAlways.MediaTek.Services;
using LoveAlways.MediaTek.UI;

namespace LoveAlways
{
    public partial class Form1
    {
        private MediatekUIController _mtkController;
        private MediatekService _mtkService;
        private CancellationTokenSource _mtkCts;
        private bool _mtkIsConnected;
        
        // MTK Log Level: 0=Off, 1=Basic, 2=Detail, 3=Debug
        private int _mtkLogLevel = 2;

        #region MTK Log Helpers

        /// <summary>
        /// MTK Log Output (With Prefix and Color)
        /// </summary>
        private void MtkLog(string message, Color? color = null, int level = 1)
        {
            if (level > _mtkLogLevel) return;
            AppendLog($"[MTK] {message}", color ?? Color.White);
        }

        /// <summary>
        /// MTK Info Log
        /// </summary>
        private void MtkLogInfo(string message) => MtkLog(message, Color.Cyan, 1);

        /// <summary>
        /// MTK Success Log
        /// </summary>
        private void MtkLogSuccess(string message) => MtkLog(message, Color.Green, 1);

        /// <summary>
        /// MTK Warning Log
        /// </summary>
        private void MtkLogWarning(string message) => MtkLog(message, Color.Orange, 1);

        /// <summary>
        /// MTK Error Log
        /// </summary>
        private void MtkLogError(string message) => MtkLog(message, Color.Red, 1);

        /// <summary>
        /// MTK Detail Log (Requires level >= 2)
        /// </summary>
        private void MtkLogDetail(string message) => MtkLog(message, Color.Gray, 2);

        /// <summary>
        /// MTK Debug Log (Requires level >= 3)
        /// </summary>
        private void MtkLogDebug(string message) => MtkLog(message, Color.DarkGray, 3);

        /// <summary>
        /// MTK Protocol Log (Hex Data)
        /// </summary>
        private void MtkLogHex(string label, byte[] data, int maxLen = 32)
        {
            if (_mtkLogLevel < 3 || data == null) return;
            
            string hex = BitConverter.ToString(data, 0, Math.Min(data.Length, maxLen)).Replace("-", " ");
            if (data.Length > maxLen) hex += $" ... ({data.Length} bytes)";
            MtkLog($"{label}: {hex}", Color.DarkGray, 3);
        }

        #endregion

        #region MTK Initialization

        /// <summary>
        /// Initialize MediaTek Module
        /// </summary>
        private void InitializeMediaTekModule()
        {
            try
            {
                // Load Chip List
                LoadMtkChipList();
                
                // Load Exploit Types
                LoadMtkExploitTypes();

                // Bind Button Events
                // Note: Connect/Disconnect button hidden, connection logic auto executed when Read GPT
                mtkBtnReadGpt.Click += MtkBtnReadGpt_Click;
                mtkInputScatterFile.SuffixClick += MtkInputScatterFile_SuffixClick;
                mtkBtnWritePartition.Click += MtkBtnWritePartition_Click;
                mtkBtnReadPartition.Click += MtkBtnReadPartition_Click;
                mtkBtnErasePartition.Click += MtkBtnErasePartition_Click;
                mtkBtnReboot.Click += MtkBtnReboot_Click;
                mtkBtnReadImei.Click += MtkBtnReadImei_Click;
                mtkBtnWriteImei.Click += MtkBtnWriteImei_Click;
                mtkBtnBackupNvram.Click += MtkBtnBackupNvram_Click;
                mtkBtnRestoreNvram.Click += MtkBtnRestoreNvram_Click;
                mtkBtnFormatData.Click += MtkBtnFormatData_Click;
                mtkBtnUnlockBl.Click += MtkBtnUnlockBl_Click;
                mtkBtnExploit.Click += MtkBtnExploit_Click;
                mtkChkSelectAll.CheckedChanged += MtkChkSelectAll_CheckedChanged;
                mtkInputDaFile.SuffixClick += MtkInputDaFile_SuffixClick;
                mtkSelectChip.SelectedIndexChanged += MtkSelectChip_SelectedIndexChanged;

                // Set Initial State
                MtkSetConnectionState(false);
                mtkBtnExploit.Enabled = false;

                // Create UI Controller (For Background Tasks)
                _mtkController = new MediatekUIController(
                    (msg, color) => SafeInvoke(() => AppendLog(msg, color)),
                    msg => SafeInvoke(() => AppendLog(msg, Color.Gray))
                );

                // Bind Controller Events
                _mtkController.OnProgress += MtkController_OnProgress;
                _mtkController.OnStateChanged += MtkController_OnStateChanged;
                _mtkController.OnDeviceConnected += MtkController_OnDeviceConnected;
                _mtkController.OnDeviceDisconnected += MtkController_OnDeviceDisconnected;
                _mtkController.OnPartitionTableLoaded += MtkController_OnPartitionTableLoaded;

                MtkLogInfo("MediaTek Module Initialized");
            }
            catch (Exception ex)
            {
                MtkLogError($"Init Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Chip List
        /// </summary>
        private void LoadMtkChipList()
        {
            mtkSelectChip.Items.Clear();

            // Load from Database
            var chips = MtkChipDatabase.GetAllChips()
                .OrderBy(c => c.ChipName)
                .ToList();

            foreach (var chip in chips)
            {
                string displayName = $"{chip.ChipName} (0x{chip.HwCode:X4})";
                if (MtkDaDatabase.SupportsExploit(chip.HwCode))
                {
                    displayName += " [Exploit]";
                }
                mtkSelectChip.Items.Add(new AntdUI.SelectItem(displayName) { Tag = chip.HwCode });
            }

            // Add Auto Detect Option
            mtkSelectChip.Items.Insert(0, new AntdUI.SelectItem("Auto Detect") { Tag = (ushort)0 });
            mtkSelectChip.SelectedIndex = 0;
        }

        /// <summary>
        /// Load Exploit Types
        /// </summary>
        private void LoadMtkExploitTypes()
        {
            mtkSelectExploitType.Items.Clear();

            // Add Exploit Type Options
            mtkSelectExploitType.Items.Add(new AntdUI.SelectItem("Auto") { Tag = "Auto" });
            mtkSelectExploitType.Items.Add(new AntdUI.SelectItem("Carbonara") { Tag = "Carbonara" });
            mtkSelectExploitType.Items.Add(new AntdUI.SelectItem("AllInOne Signature") { Tag = "AllinoneSignature" });
            mtkSelectExploitType.Items.Add(new AntdUI.SelectItem("None") { Tag = "None" });

            mtkSelectExploitType.SelectedIndex = 0;
        }

        /// <summary>
        /// Update Exploit Type when Chip Selection Changed
        /// </summary>
        private void MtkSelectChip_SelectedIndexChanged(object sender, AntdUI.IntEventArgs e)
        {
            UpdateExploitTypeForSelectedChip();
        }

        /// <summary>
        /// Update Exploit Type based on Selected Chip
        /// </summary>
        private void UpdateExploitTypeForSelectedChip()
        {
            if (mtkSelectChip.SelectedIndex < 0 || mtkSelectChip.SelectedValue == null)
                return;

            var selectedItem = mtkSelectChip.SelectedValue as AntdUI.SelectItem;
            if (selectedItem?.Tag == null)
                return;

            ushort hwCode = (ushort)selectedItem.Tag;
            if (hwCode == 0)
            {
                // Auto Detect Mode, use Auto Exploit Type
                mtkSelectExploitType.SelectedIndex = 0;  // Auto
                return;
            }

            // Get Chip Exploit Type
            string exploitType = MtkChipDatabase.GetExploitType(hwCode);
            
            // Auto Select based on Exploit Type
            for (int i = 0; i < mtkSelectExploitType.Items.Count; i++)
            {
                var item = mtkSelectExploitType.Items[i] as AntdUI.SelectItem;
                if (item?.Tag?.ToString() == exploitType)
                {
                    mtkSelectExploitType.SelectedIndex = i;
                    break;
                }
            }

            // Update Exploit Button State
            bool hasExploit = MtkChipDatabase.GetChip(hwCode)?.HasExploit ?? false;
            bool isAllinone = MtkChipDatabase.IsAllinoneSignatureSupported(hwCode);
            
            if (isAllinone)
            {
                mtkBtnExploit.Text = "AllInOne Exploit";
                mtkBtnExploit.Enabled = _mtkIsConnected;
                AppendLog($"[MTK] Chip supports ALLINONE-SIGNATURE Exploit", Color.Cyan);
            }
            else if (hasExploit)
            {
                mtkBtnExploit.Text = "Execute Exploit";
                mtkBtnExploit.Enabled = _mtkIsConnected;
            }
            else
            {
                mtkBtnExploit.Text = "No Exploit";
                mtkBtnExploit.Enabled = false;
            }
        }

        /// <summary>
        /// Cleanup MediaTek Module
        /// </summary>
        private void CleanupMediaTekModule()
        {
            MtkDisconnect();
            _mtkController?.Dispose();
            _mtkController = null;
            
            // Dispose CancellationTokenSource
            _mtkCts?.Dispose();
            _mtkCts = null;
        }
        
        /// <summary>
        /// Safe Reset MTK CancellationTokenSource
        /// </summary>
        private void MtkResetCancellationToken()
        {
            _mtkCts?.Cancel();
            _mtkCts?.Dispose();
            _mtkCts = new CancellationTokenSource();
        }

        #endregion

        #region MTK Button Events

        private async void MtkBtnConnect_Click(object sender, EventArgs e)
        {
            if (_mtkIsConnected)
            {
                AppendLog("[MTK] Already Connected", Color.Orange);
                return;
            }

            MtkResetCancellationToken();
            MtkStartTimer();

            try
            {
                MtkSetButtonsEnabled(false);
                MtkUpdateProgress(0, 0, "Waiting for device...");

                // Create Service
                _mtkService = new MediatekService();

                // Bind Events - Only forward progress, logs handled by MtkLog
                _mtkService.OnProgress += (current, total) => SafeInvoke(() =>
                {
                    MtkUpdateProgress(current, total);
                });

                _mtkService.OnStateChanged += state => SafeInvoke(() =>
                {
                    MtkUpdateStateDisplay(state);
                });

                // Service layer logs - only show important information
                _mtkService.OnLog += (msg, color) => SafeInvoke(() =>
                {
                    // Filter duplicate [MTK] prefix logs
                    if (_mtkLogLevel >= 2 || color == Color.Red || color == Color.Green)
                        AppendLog(msg, color);
                });

                // Wait for device connection
                MtkLogInfo("Waiting for device connection...");

                MtkUpdateProgress(0, 0, "Please enter BROM mode");
                string comPort = await MtkWaitForDeviceAsync(_mtkCts.Token);

                if (string.IsNullOrEmpty(comPort))
                {
                    MtkUpdateProgress(0, 0, "Device Not Detected");
                    MtkLogWarning("Device Not Detected");
                    return;
                }

                // Connect Device
                MtkLogInfo($"Found Device: {comPort}");
                MtkUpdateProgress(0, 0, "Connecting...");
                bool success = await _mtkService.ConnectAsync(comPort, 115200, _mtkCts.Token);

                if (success)
                {
                    _mtkIsConnected = true;
                    MtkSetConnectionState(true);

                    // Update Device Info
                    if (_mtkService.CurrentDevice != null)
                    {
                        MtkUpdateDeviceInfo(_mtkService.CurrentDevice);
                    }

                    // Set DA File Path
                    if (_mtkUseSeparateDa)
                    {
                        // Use Separate DA1 + DA2 Files
                        if (!string.IsNullOrEmpty(_mtkCustomDa1Path) && File.Exists(_mtkCustomDa1Path))
                        {
                            _mtkService.SetCustomDa1(_mtkCustomDa1Path);
                            AppendLog($"[MTK] Use Custom DA1: {Path.GetFileName(_mtkCustomDa1Path)}", Color.Cyan);
                        }
                        
                        if (!string.IsNullOrEmpty(_mtkCustomDa2Path) && File.Exists(_mtkCustomDa2Path))
                        {
                            _mtkService.SetCustomDa2(_mtkCustomDa2Path);
                            AppendLog($"[MTK] Use Custom DA2: {Path.GetFileName(_mtkCustomDa2Path)}", Color.Cyan);
                        }
                    }
                    else
                    {
                        // Use AllInOne DA File
                        string daPath = mtkInputDaFile.Text?.Trim();
                        if (!string.IsNullOrEmpty(daPath) && File.Exists(daPath))
                        {
                            _mtkService.SetDaFilePath(daPath);
                        }
                        
                        // Compatibility: Also check separately set DA2
                        if (!string.IsNullOrEmpty(_mtkCustomDa2Path) && File.Exists(_mtkCustomDa2Path))
                        {
                            _mtkService.SetCustomDa2(_mtkCustomDa2Path);
                        }
                    }

                    // Load DA (If Exploit is used)
                    if (mtkChkExploit.Checked)
                    {
                        MtkUpdateProgress(0, 0, "Loading DA...");
                        await _mtkService.LoadDaAsync(_mtkCts.Token);
                    }

                    MtkUpdateProgress(100, 100, "Connected");
                    MtkLogSuccess("Device Connected Success");

                    // Update Right Panel
                    UpdateMtkInfoPanel();
                }
                else
                {
                    MtkUpdateProgress(0, 0, "Connection Failed");
                    MtkLogError("Device Connection Failed");
                }
            }
            catch (OperationCanceledException)
            {
                MtkUpdateProgress(0, 0, "Cancelled");
                MtkLogWarning("Operation Cancelled");
            }
            catch (Exception ex)
            {
                MtkUpdateProgress(0, 0, "Connection Error");
                MtkLogError($"Connection Error: {ex.Message}");
            }
            finally
            {
                MtkSetButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Wait for MTK Device Connection
        /// </summary>
        private async Task<string> MtkWaitForDeviceAsync(CancellationToken ct)
        {
            // Use Port Detector to wait for MTK device
            using (var detector = new MtkPortDetector())
            {
                var portInfo = await detector.WaitForDeviceAsync(30000, ct);
                return portInfo?.ComPort;
            }
        }

        private void MtkBtnDisconnect_Click(object sender, EventArgs e)
        {
            MtkDisconnect();
        }

        private void MtkDisconnect()
        {
            _mtkCts?.Cancel();
            _mtkService?.Dispose();
            _mtkService = null;
            _mtkIsConnected = false;

            MtkSetConnectionState(false);
            MtkClearDeviceInfo();
            mtkListPartitions.Items.Clear();

            MtkUpdateProgress(0, 0, "Disconnected");
            MtkLogDetail("Disconnected");
        }

        private async void MtkBtnReadGpt_Click(object sender, EventArgs e)
        {
            MtkResetCancellationToken();
            MtkStartTimer();

            try
            {
                MtkSetButtonsEnabled(false);

                // If not connected, auto connect first
                if (!_mtkIsConnected || _mtkService == null)
                {
                    bool connected = await MtkAutoConnectAsync();
                    if (!connected)
                    {
                        return;
                    }
                }

                // Read GPT
                MtkUpdateProgress(0, 0, "Reading Partition Table...");
                MtkLogInfo("Reading Partition Table...");

                var partitions = await _mtkService.ReadPartitionTableAsync(_mtkCts.Token);

                if (partitions != null && partitions.Count > 0)
                {
                    mtkListPartitions.Items.Clear();

                    foreach (var p in partitions)
                    {
                        var item = new ListViewItem(p.Name);
                        item.SubItems.Add(p.Type);
                        item.SubItems.Add(MtkFormatSize(p.Size));
                        item.SubItems.Add($"0x{p.StartSector * 512:X}");
                        item.SubItems.Add("--");
                        item.Tag = p;
                        mtkListPartitions.Items.Add(item);
                    }

                    string elapsed = MtkGetElapsedTime();
                    MtkUpdateProgress(100, 100, $"{partitions.Count} Partitions [{elapsed}]");
                    MtkLogSuccess($"Read {partitions.Count} Partitions ({elapsed})");
                }
                else
                {
                    MtkUpdateProgress(0, 0, "No Partition Data");
                    MtkLogWarning("No Partition Info Read");
                }
            }
            catch (OperationCanceledException)
            {
                MtkUpdateProgress(0, 0, "Cancelled");
                MtkLogWarning("Operation Cancelled");
            }
            catch (Exception ex)
            {
                MtkUpdateProgress(0, 0, "Read Failed");
                MtkLogError($"Read GPT Failed: {ex.Message}");
            }
            finally
            {
                MtkSetButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Auto Connect MTK Device
        /// </summary>
        private async Task<bool> MtkAutoConnectAsync()
        {
            MtkStartTimer();
            MtkUpdateProgress(0, 0, "Waiting for device...");
            MtkLogInfo("Waiting for device (Please enter BROM mode)");

            // Create Service
            _mtkService = new MediatekService();

            // Bind Events - Simple Log Output
            _mtkService.OnProgress += (current, total) => SafeInvoke(() =>
            {
                MtkUpdateProgress(current, total);
            });

            _mtkService.OnStateChanged += state => SafeInvoke(() =>
            {
                MtkUpdateStateDisplay(state);
            });

            _mtkService.OnLog += (msg, color) => SafeInvoke(() =>
            {
                // Only show important logs
                if (_mtkLogLevel >= 2 || color == Color.Red || color == Color.Green)
                    AppendLog(msg, color);
            });

            // Wait for device connection
            string comPort = await MtkWaitForDeviceAsync(_mtkCts.Token);

            if (string.IsNullOrEmpty(comPort))
            {
                MtkUpdateProgress(0, 0, "Timeout");
                MtkLogWarning("Device Not Detected");
                return false;
            }

            // Connect Device
            MtkLogInfo($"Found: {comPort}");
            MtkUpdateProgress(0, 0, "Connecting...");
            bool success = await _mtkService.ConnectAsync(comPort, 115200, _mtkCts.Token);

            if (!success)
            {
                MtkUpdateProgress(0, 0, "Connection Failed");
                MtkLogError("Device Connection Failed");
                return false;
            }

            _mtkIsConnected = true;
            MtkSetConnectionState(true);

            // Update Device Info
            if (_mtkService.CurrentDevice != null)
            {
                MtkUpdateDeviceInfo(_mtkService.CurrentDevice);
            }

            // Set DA File Path
            MtkApplyDaSettings();

            // Load DA (If needed)
            if (mtkChkExploit.Checked)
            {
                MtkUpdateProgress(0, 0, "Loading DA...");
                await _mtkService.LoadDaAsync(_mtkCts.Token);
            }

            AppendLog("[MTK] Device Connected Success", Color.Green);
            UpdateMtkInfoPanel();
            return true;
        }

        /// <summary>
        /// Apply DA Settings
        /// </summary>
        private void MtkApplyDaSettings()
        {
            if (_mtkService == null) return;

            if (_mtkUseSeparateDa)
            {
                // Use Separate DA1 + DA2 Files
                if (!string.IsNullOrEmpty(_mtkCustomDa1Path) && File.Exists(_mtkCustomDa1Path))
                {
                    _mtkService.SetCustomDa1(_mtkCustomDa1Path);
                    AppendLog($"[MTK] Use Custom DA1: {Path.GetFileName(_mtkCustomDa1Path)}", Color.Cyan);
                }
                
                if (!string.IsNullOrEmpty(_mtkCustomDa2Path) && File.Exists(_mtkCustomDa2Path))
                {
                    _mtkService.SetCustomDa2(_mtkCustomDa2Path);
                    AppendLog($"[MTK] Use Custom DA2: {Path.GetFileName(_mtkCustomDa2Path)}", Color.Cyan);
                }
            }
            else
            {
                // Use AllInOne DA File
                string daPath = mtkInputDaFile.Text?.Trim();
                if (!string.IsNullOrEmpty(daPath) && File.Exists(daPath))
                {
                    _mtkService.SetDaFilePath(daPath);
                }
                
                // Compatibility: Also check separately set DA2
                if (!string.IsNullOrEmpty(_mtkCustomDa2Path) && File.Exists(_mtkCustomDa2Path))
                {
                    _mtkService.SetCustomDa2(_mtkCustomDa2Path);
                }
            }
        }

        private async void MtkBtnWritePartition_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            var selectedPartitions = MtkGetSelectedPartitions();
            if (selectedPartitions.Length == 0)
            {
                MtkLogWarning("Please Select Partitions to Write");
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select Folder Containing Partition Images";
                if (folderDialog.ShowDialog() != DialogResult.OK)
                    return;

                MtkStartTimer();
                int current = 0;
                int total = selectedPartitions.Length;
                int success = 0;

                try
                {
                    MtkSetButtonsEnabled(false);

                    foreach (var partition in selectedPartitions)
                    {
                        current++;
                        
                        if (mtkChkSkipUserdata.Checked &&
                            (partition.Name.ToLower() == "userdata" || partition.Name.ToLower() == "data"))
                        {
                            MtkLogDetail($"Skip {partition.Name}");
                            continue;
                        }

                        string filePath = MtkFindPartitionFile(folderDialog.SelectedPath, partition.Name);
                        if (string.IsNullOrEmpty(filePath))
                        {
                            MtkLogWarning($"Not Found {partition.Name} Image");
                            continue;
                        }

                        MtkUpdateProgress(current, total, $"Writing {partition.Name} ({current}/{total})");
                        MtkLogInfo($"Writing: {partition.Name}");

                        bool result = await _mtkService.WritePartitionAsync(partition.Name, filePath, _mtkCts.Token);
                        if (result) success++;
                    }

                    string elapsed = MtkGetElapsedTime();
                    MtkUpdateProgress(100, 100, $"Completed {success}/{total} [{elapsed}]");
                    MtkLogSuccess($"Write Completed {success}/{total} ({elapsed})");

                    if (mtkChkRebootAfter.Checked)
                    {
                        MtkLogInfo("Rebooting device...");
                        await _mtkService.RebootAsync(_mtkCts.Token);
                    }
                }
                catch (Exception ex)
                {
                    MtkUpdateProgress(0, 0, "Write Failed");
                    MtkLogError($"Write Failed: {ex.Message}");
                }
                finally
                {
                    MtkSetButtonsEnabled(true);
                }
            }
        }

        private async void MtkBtnReadPartition_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            var selectedPartitions = MtkGetSelectedPartitions();
            if (selectedPartitions.Length == 0)
            {
                MtkLogWarning("Please Select Partitions to Read");
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select Save Location";
                if (folderDialog.ShowDialog() != DialogResult.OK)
                    return;

                MtkStartTimer();
                int current = 0;
                int total = selectedPartitions.Length;
                int success = 0;

                try
                {
                    MtkSetButtonsEnabled(false);

                    foreach (var partition in selectedPartitions)
                    {
                        current++;
                        MtkUpdateProgress(current, total, $"Reading {partition.Name} ({current}/{total})");
                        MtkLogInfo($"Reading: {partition.Name} ({MtkFormatSize(partition.Size)})");

                        string fileName = $"{partition.Name}.img";
                        string outputPath = Path.Combine(folderDialog.SelectedPath, fileName);

                        bool result = await _mtkService.ReadPartitionAsync(
                            partition.Name,
                            outputPath,
                            partition.Size,
                            _mtkCts.Token);

                        if (result)
                        {
                            MtkLogSuccess($"Saved: {fileName}");
                            success++;
                        }
                    }

                    string elapsed = MtkGetElapsedTime();
                    MtkUpdateProgress(100, 100, $"Completed {success}/{total} [{elapsed}]");
                    MtkLogSuccess($"Read Completed {success}/{total} ({elapsed})");
                }
                catch (Exception ex)
                {
                    MtkUpdateProgress(0, 0, "Read Failed");
                    MtkLogError($"Read Failed: {ex.Message}");
                }
                finally
                {
                    MtkSetButtonsEnabled(true);
                }
            }
        }

        private async void MtkBtnErasePartition_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            var selectedPartitions = MtkGetSelectedPartitions();
            if (selectedPartitions.Length == 0)
            {
                MtkLogWarning("Please Select Partitions to Erase");
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to erase selected {selectedPartitions.Length} partitions?\nThis operation cannot be undone!",
                "Confirm Erase",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes)
                return;

            MtkStartTimer();
            int current = 0;
            int total = selectedPartitions.Length;

            try
            {
                MtkSetButtonsEnabled(false);

                foreach (var partition in selectedPartitions)
                {
                    current++;
                    MtkUpdateProgress(current, total, $"Erasing {partition.Name} ({current}/{total})");
                    MtkLogInfo($"Erasing: {partition.Name}");

                    await _mtkService.ErasePartitionAsync(partition.Name, _mtkCts.Token);
                }

                string elapsed = MtkGetElapsedTime();
                MtkUpdateProgress(100, 100, $"Completed [{elapsed}]");
                MtkLogSuccess($"Erase Completed {total} Partitions ({elapsed})");
            }
            catch (Exception ex)
            {
                MtkUpdateProgress(0, 0, "Erase Failed");
                MtkLogError($"Erase Failed: {ex.Message}");
            }
            finally
            {
                MtkSetButtonsEnabled(true);
            }
        }

        private async void MtkBtnReboot_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            try
            {
                MtkUpdateProgress(0, 0, "Rebooting device...");
                await _mtkService.RebootAsync(_mtkCts.Token);
                MtkUpdateProgress(100, 100, "Reboot Command Sent");
                AppendLog("[MTK] Device Rebooting...", Color.Green);

                MtkDisconnect();
            }
            catch (Exception ex)
            {
                AppendLog($"[MTK] Reboot Failed: {ex.Message}", Color.Red);
            }
        }

        private void MtkBtnReadImei_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;
            AppendLog("[MTK] IMEI Read requires NVRAM access, not supported yet", Color.Orange);
        }

        private void MtkBtnWriteImei_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;
            AppendLog("[MTK] IMEI Write requires NVRAM access, not supported yet", Color.Orange);
        }

        private void MtkBtnBackupNvram_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;
            AppendLog("[MTK] NVRAM Backup not supported yet", Color.Orange);
        }

        private void MtkBtnRestoreNvram_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;
            AppendLog("[MTK] NVRAM Restore not supported yet", Color.Orange);
        }

        private async void MtkBtnFormatData_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            var result = MessageBox.Show(
                "Are you sure you want to format Data partition?\nThis will erase all user data!",
                "Confirm Format",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes)
                return;

            try
            {
                MtkSetButtonsEnabled(false);
                MtkUpdateProgress(0, 0, "Formatting Data...");

                await _mtkService.ErasePartitionAsync("userdata", _mtkCts.Token);

                MtkUpdateProgress(100, 100, "Format Completed");
                AppendLog("[MTK] Data Partition Formatted", Color.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"[MTK] Format Failed: {ex.Message}", Color.Red);
            }
            finally
            {
                MtkSetButtonsEnabled(true);
            }
        }

        private void MtkBtnUnlockBl_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;
            AppendLog("[MTK] Bootloader Unlock requires special permissions, not supported yet", Color.Orange);
        }

        /// <summary>
        /// Execute Exploit Button Click Event
        /// </summary>
        private async void MtkBtnExploit_Click(object sender, EventArgs e)
        {
            if (!MtkEnsureConnected()) return;

            // Get Selected Exploit Type
            var selectedExploitItem = mtkSelectExploitType.SelectedValue as AntdUI.SelectItem;
            string exploitType = selectedExploitItem?.Tag?.ToString() ?? "Auto";

            // If Auto Mode, determine by chip
            if (exploitType == "Auto")
            {
                ushort hwCode = _mtkService?.ChipInfo?.HwCode ?? 0;
                exploitType = MtkChipDatabase.GetExploitType(hwCode);
                
                if (exploitType == "None" || string.IsNullOrEmpty(exploitType))
                {
                    AppendLog("[MTK] Current chip does not support any known exploit", Color.Orange);
                    return;
                }
            }

            try
            {
                MtkSetButtonsEnabled(false);

                if (exploitType == "AllinoneSignature")
                {
                    await ExecuteAllinoneSignatureExploitAsync();
                }
                else if (exploitType == "Carbonara")
                {
                    await ExecuteCarbonaraExploitAsync();
                }
                else
                {
                    AppendLog($"[MTK] Unknown Exploit Type: {exploitType}", Color.Red);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[MTK] Exploit Exception: {ex.Message}", Color.Red);
            }
            finally
            {
                MtkSetButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Execute ALLINONE-SIGNATURE Exploit
        /// Only for Dimensity 9000 series (MT6989/MT6983/MT6985)
        /// </summary>
        private async Task ExecuteAllinoneSignatureExploitAsync()
        {
            ushort hwCode = _mtkService?.ChipInfo?.HwCode ?? 0;
            string chipName = _mtkService?.ChipInfo?.ChipName ?? "Unknown";

            // Check if chip supports
            if (!MtkChipDatabase.IsAllinoneSignatureSupported(hwCode))
            {
                AppendLog($"[MTK] Chip {chipName} (0x{hwCode:X4}) does not support ALLINONE-SIGNATURE Exploit", Color.Red);
                AppendLog("[MTK] This exploit only supports following chips:", Color.Yellow);
                
                var supportedChips = MtkChipDatabase.GetAllinoneSignatureChips();
                foreach (var chip in supportedChips)
                {
                    AppendLog($"[MTK]   • {chip.ChipName} - {chip.Description} (0x{chip.HwCode:X4})", Color.Yellow);
                }
                return;
            }

            AppendLog("[MTK] ═══════════════════════════════════════", Color.Yellow);
            AppendLog($"[MTK] Executing ALLINONE-SIGNATURE Exploit", Color.Yellow);
            AppendLog($"[MTK] Target Chip: {chipName} (0x{hwCode:X4})", Color.Yellow);
            AppendLog("[MTK] ═══════════════════════════════════════", Color.Yellow);

            MtkUpdateProgress(0, 0, "Executing Exploit...");

            // Check if DA2 Loaded
            if (_mtkService.State != MtkDeviceState.Da2Loaded)
            {
                AppendLog("[MTK] Please connect device and load DA2 first", Color.Orange);
                return;
            }

            bool success = await _mtkService.RunAllinoneSignatureExploitAsync(
                null,  // Use default shellcode
                null,  // Use default pointer table
                _mtkCts.Token);

            if (success)
            {
                MtkUpdateProgress(100, 100, "Exploit Success");
                AppendLog("[MTK] ✓ ALLINONE-SIGNATURE Exploit Success!", Color.Green);
                AppendLog("[MTK] Device Security Check Disabled", Color.Green);
            }
            else
            {
                MtkUpdateProgress(0, 0, "Exploit Failed");
                AppendLog("[MTK] ✗ ALLINONE-SIGNATURE Exploit Failed", Color.Red);
            }
        }

        /// <summary>
        /// Execute Carbonara Exploit
        /// For most MT67xx/MT68xx chips
        /// </summary>
        private async Task ExecuteCarbonaraExploitAsync()
        {
            ushort hwCode = _mtkService?.ChipInfo?.HwCode ?? 0;
            string chipName = _mtkService?.ChipInfo?.ChipName ?? "Unknown";

            AppendLog("[MTK] ═══════════════════════════════════════", Color.Yellow);
            AppendLog($"[MTK] Executing Carbonara Exploit", Color.Yellow);
            AppendLog($"[MTK] Target Chip: {chipName} (0x{hwCode:X4})", Color.Yellow);
            AppendLog("[MTK] ═══════════════════════════════════════", Color.Yellow);

            MtkUpdateProgress(0, 0, "Executing Exploit...");

            // Carbonara exploit auto executed in LoadDaAsync
            // Here just show info
            AppendLog("[MTK] Carbonara Exploit will be executed automatically when connecting", Color.Cyan);
            AppendLog("[MTK] If connection success, means exploit works", Color.Cyan);

            MtkUpdateProgress(100, 100, "Completed");
        }

        private void MtkChkSelectAll_CheckedChanged(object sender, AntdUI.BoolEventArgs e)
        {
            foreach (ListViewItem item in mtkListPartitions.Items)
            {
                item.Checked = e.Value;
            }
        }

        private void MtkInputDaFile_SuffixClick(object sender, EventArgs e)
        {
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.Filter = "DA Files|*.bin;*.da|All Files|*.*";
                openDialog.Multiselect = true;  // Support Multi-Select
                openDialog.Title = "Select DA File (Support Multi-Select DA1/DA2)";
                
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    // Reset Separate DA State
                    _mtkCustomDa1Path = null;
                    _mtkCustomDa2Path = null;
                    _mtkUseSeparateDa = false;
                    
                    if (openDialog.FileNames.Length == 1)
                    {
                        // Single File - AllInOne or Separate DA1
                        string fileName = Path.GetFileName(openDialog.FileName).ToLower();
                        
                        // Check if DA2 (If user only selected DA2)
                        if (fileName.Contains("da2") || fileName.Contains("stage2"))
                        {
                            _mtkCustomDa2Path = openDialog.FileName;
                            mtkInputDaFile.Text = openDialog.FileName;
                            AppendLog($"[MTK] ⚠ Only selected DA2, please select DA1 together", Color.Orange);
                        }
                        else
                        {
                            mtkInputDaFile.Text = openDialog.FileName;
                            AppendLog($"[MTK] DA File: {Path.GetFileName(openDialog.FileName)}", Color.Cyan);
                        }
                    }
                    else
                    {
                        // Multi File - Auto Detect DA1/DA2
                        var (da1Path, da2Path, allInOnePath) = AutoDetectDaFiles(openDialog.FileNames);
                        
                        if (!string.IsNullOrEmpty(allInOnePath))
                        {
                            // AllInOne DA File
                            mtkInputDaFile.Text = allInOnePath;
                            _mtkUseSeparateDa = false;
                            AppendLog($"[MTK] Detected AllInOne DA: {Path.GetFileName(allInOnePath)}", Color.Cyan);
                        }
                        else if (!string.IsNullOrEmpty(da1Path) && !string.IsNullOrEmpty(da2Path))
                        {
                            // Separate DA1 + DA2 (Complete)
                            _mtkCustomDa1Path = da1Path;
                            _mtkCustomDa2Path = da2Path;
                            _mtkUseSeparateDa = true;
                            
                            // Show as "DA1 + DA2" format
                            mtkInputDaFile.Text = $"{Path.GetFileName(da1Path)} + {Path.GetFileName(da2Path)}";
                            
                            AppendLog($"[MTK] Detected DA1: {Path.GetFileName(da1Path)}", Color.Cyan);
                            AppendLog($"[MTK] Detected DA2: {Path.GetFileName(da2Path)}", Color.Cyan);
                            AppendLog("[MTK] ✓ Auto Identified DA1 + DA2 (Separate Format)", Color.Green);
                        }
                        else if (!string.IsNullOrEmpty(da1Path))
                        {
                            // Only DA1
                            mtkInputDaFile.Text = da1Path;
                            AppendLog($"[MTK] Detected DA1: {Path.GetFileName(da1Path)}", Color.Cyan);
                            AppendLog("[MTK] ⚠ DA2 Not Detected, some functions may be limited", Color.Orange);
                        }
                        else if (!string.IsNullOrEmpty(da2Path))
                        {
                            // Only DA2
                            _mtkCustomDa2Path = da2Path;
                            mtkInputDaFile.Text = da2Path;
                            AppendLog($"[MTK] Detected DA2: {Path.GetFileName(da2Path)}", Color.Cyan);
                            AppendLog("[MTK] ⚠ DA1 Not Detected, please select DA1 together", Color.Orange);
                        }
                    }
                }
            }
        }
        
        // Store Auto Detected DA1/DA2 Paths (Separate Format)
        private string _mtkCustomDa1Path;
        private string _mtkCustomDa2Path;
        private bool _mtkUseSeparateDa;  // Use Separate DA1/DA2
        private string _mtkScatterPath;  // Scatter Config Path
        private List<MtkScatterEntry> _mtkScatterEntries;  // Scatter Partition Config
        
        /// <summary>
        /// Auto Detect DA File Type
        /// </summary>
        private (string da1Path, string da2Path, string allInOnePath) AutoDetectDaFiles(string[] filePaths)
        {
            string da1Path = null;
            string da2Path = null;
            string allInOnePath = null;
            
            foreach (var path in filePaths)
            {
                string fileName = Path.GetFileName(path).ToLower();
                
                // Detect AllInOne DA
                if (fileName.Contains("allinone") || fileName.Contains("all_in_one") || 
                    fileName.Contains("all-in-one") || fileName == "mtk_da.bin")
                {
                    allInOnePath = path;
                    continue;
                }
                
                // Detect DA1 (Stage1)
                if (fileName.Contains("da1") || fileName.Contains("stage1") || 
                    fileName.Contains("_1.") || fileName.Contains("-1.") ||
                    fileName.EndsWith("_da1.bin") || fileName.EndsWith("-da1.bin"))
                {
                    da1Path = path;
                    continue;
                }
                
                // Detect DA2 (Stage2)
                if (fileName.Contains("da2") || fileName.Contains("stage2") || 
                    fileName.Contains("_2.") || fileName.Contains("-2.") ||
                    fileName.EndsWith("_da2.bin") || fileName.EndsWith("-da2.bin"))
                {
                    da2Path = path;
                    continue;
                }
                
                // If filename ambiguous, check size
                try
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Length > 500000 && fileInfo.Length < 700000)
                    {
                        // ~600KB Usually DA1
                        if (da1Path == null) da1Path = path;
                    }
                    else if (fileInfo.Length > 300000 && fileInfo.Length < 400000)
                    {
                        // ~350KB Usually DA2
                        if (da2Path == null) da2Path = path;
                    }
                    else if (fileInfo.Length > 1000000)
                    {
                        // > 1MB Possibly AllInOne
                        if (allInOnePath == null) allInOnePath = path;
                    }
                }
                catch { }
            }
            
            return (da1Path, da2Path, allInOnePath);
        }

        #endregion

        #region MTK Controller Events

        private void MtkController_OnProgress(int current, int total)
        {
            SafeInvoke(() =>
            {
                MtkUpdateProgress(current, total);
            });
        }

        private void MtkController_OnStateChanged(MtkDeviceState state)
        {
            SafeInvoke(() =>
            {
                MtkUpdateStateDisplay(state);
            });
        }

        private void MtkController_OnDeviceConnected(MtkDeviceInfo device)
        {
            SafeInvoke(() =>
            {
                // Concise Device Info Log
                string chipName = device.ChipInfo?.GetChipName() ?? "Unknown";
                string hwCode = device.ChipInfo != null ? $"0x{device.ChipInfo.HwCode:X4}" : "--";
                string protocol = _mtkService?.IsXFlashMode == true ? "XFlash" : "XML";
                MtkLogSuccess($"Device Connected: {chipName} ({hwCode}) [{protocol}]");

                // Update Right Info Panel
                UpdateMtkInfoPanelFromDevice(device);
            });
        }

        private void MtkController_OnDeviceDisconnected(MtkDeviceInfo device)
        {
            SafeInvoke(() =>
            {
                MtkLogWarning("Device Disconnected");
                UpdateMtkInfoPanel();
            });
        }

        private void MtkController_OnPartitionTableLoaded(List<MtkPartitionInfo> partitions)
        {
            SafeInvoke(() =>
            {
                MtkLogInfo($"Loaded {partitions.Count} Partitions");
            });
        }

        #endregion

        #region MTK Right Info Panel

        /// <summary>
        /// Update Right Info Panel for MTK
        /// </summary>
        private void UpdateMtkInfoPanel()
        {
            if (_mtkIsConnected && _mtkService != null)
            {
                var deviceInfo = _mtkService.CurrentDevice;
                if (deviceInfo?.ChipInfo != null)
                {
                    string protocol = _mtkService.IsXFlashMode ? "XFlash" : "XML";
                    uiLabel9.Text = $"Platform: MediaTek";
                    uiLabel11.Text = $"Chip: {deviceInfo.ChipInfo.GetChipName()}";
                    uiLabel12.Text = $"HW Code: 0x{deviceInfo.ChipInfo.HwCode:X4}";
                    uiLabel10.Text = $"Protocol: {protocol}";
                    uiLabel3.Text = $"ME ID: {(deviceInfo.MeIdHex?.Length > 16 ? deviceInfo.MeIdHex.Substring(0, 16) + "..." : deviceInfo.MeIdHex ?? "--")}";
                    uiLabel13.Text = $"Status: Connected";
                    uiLabel14.Text = $"Exploit: {(MtkDaDatabase.SupportsExploit(deviceInfo.ChipInfo.HwCode) ? "Available" : "Unavailable")}";
                    return;
                }
            }

            // Waiting Status
            uiLabel9.Text = "Platform: MediaTek";
            uiLabel11.Text = "Chip: Waiting";
            uiLabel12.Text = "HW Code: --";
            uiLabel10.Text = "Protocol: --";
            uiLabel3.Text = "ME ID: --";
            uiLabel13.Text = "Status: Disconnected";
            uiLabel14.Text = "Exploit: --";
        }

        /// <summary>
        /// Update Right Info Panel after MTK Device Connected
        /// </summary>
        private void UpdateMtkInfoPanelFromDevice(MtkDeviceInfo deviceInfo)
        {
            SafeInvoke(() =>
            {
                if (deviceInfo?.ChipInfo != null)
                {
                    string protocol = _mtkService?.IsXFlashMode == true ? "XFlash" : "XML";
                    uiLabel9.Text = $"Platform: MediaTek";
                    uiLabel11.Text = $"Chip: {deviceInfo.ChipInfo.GetChipName()}";
                    uiLabel12.Text = $"HW Code: 0x{deviceInfo.ChipInfo.HwCode:X4}";
                    uiLabel10.Text = $"Protocol: {protocol}";
                    uiLabel3.Text = $"ME ID: {(deviceInfo.MeIdHex?.Length > 16 ? deviceInfo.MeIdHex.Substring(0, 16) + "..." : deviceInfo.MeIdHex ?? "--")}";
                    uiLabel13.Text = $"Status: Connected";
                    uiLabel14.Text = $"Exploit: {(MtkDaDatabase.SupportsExploit(deviceInfo.ChipInfo.HwCode) ? "Available" : "Unavailable")}";
                }
            });
        }

        #endregion

        #region MTK Helper Methods

        private bool MtkEnsureConnected()
        {
            if (!_mtkIsConnected || _mtkService == null)
            {
                AppendLog("[MTK] Please connect device first", Color.Orange);
                return false;
            }
            return true;
        }

        private void MtkSetConnectionState(bool connected)
        {
            _mtkIsConnected = connected;
            // Connect/Disconnect buttons hidden, no longer controlling them
            // mtkBtnConnect.Enabled = !connected;
            // mtkBtnDisconnect.Enabled = connected;
            
            // Read GPT always enabled, triggers auto connect
            mtkBtnReadGpt.Enabled = true;
            mtkBtnWritePartition.Enabled = connected;
            mtkBtnReadPartition.Enabled = connected;
            mtkBtnErasePartition.Enabled = connected;
            mtkBtnReboot.Enabled = connected;
            mtkBtnReadImei.Enabled = connected;
            mtkBtnWriteImei.Enabled = connected;
            mtkBtnBackupNvram.Enabled = connected;
            mtkBtnRestoreNvram.Enabled = connected;
            mtkBtnFormatData.Enabled = connected;
            mtkBtnUnlockBl.Enabled = connected;
            
            // Update Exploit Button State
            if (connected && _mtkService?.ChipInfo != null)
            {
                ushort hwCode = _mtkService.ChipInfo.HwCode;
                bool hasExploit = MtkChipDatabase.GetChip(hwCode)?.HasExploit ?? false;
                bool isAllinone = MtkChipDatabase.IsAllinoneSignatureSupported(hwCode);
                
                mtkBtnExploit.Enabled = hasExploit;
                
                if (isAllinone)
                {
                    mtkBtnExploit.Text = "AllInOne Exploit";
                }
                else if (hasExploit)
                {
                    mtkBtnExploit.Text = "Execute Exploit";
                }
                else
                {
                    mtkBtnExploit.Text = "No Exploit";
                }
            }
            else
            {
                mtkBtnExploit.Enabled = false;
            }
        }

        private void MtkSetButtonsEnabled(bool enabled)
        {
            // Read GPT button always controlled by enabled state (support auto connect)
            mtkBtnReadGpt.Enabled = enabled;
            
            if (_mtkIsConnected)
            {
                mtkBtnWritePartition.Enabled = enabled;
                mtkBtnReadPartition.Enabled = enabled;
                mtkBtnErasePartition.Enabled = enabled;
                mtkBtnReboot.Enabled = enabled;
                mtkBtnReadImei.Enabled = enabled;
                mtkBtnWriteImei.Enabled = enabled;
                mtkBtnBackupNvram.Enabled = enabled;
                mtkBtnRestoreNvram.Enabled = enabled;
                mtkBtnFormatData.Enabled = enabled;
                mtkBtnUnlockBl.Enabled = enabled;
            }
            // Connect/Disconnect buttons hidden
        }

        private void MtkUpdateDeviceInfo(MtkDeviceInfo info)
        {
            if (info.ChipInfo != null)
            {
                mtkLblHwCode.Text = $"HW: 0x{info.ChipInfo.HwCode:X4}";
                mtkLblChipName.Text = $"Chip: {info.ChipInfo.GetChipName()}";
                mtkLblDaMode.Text = $"Mode: {info.ChipInfo.DaMode}";
            }
        }

        private void MtkUpdateStateDisplay(MtkDeviceState state)
        {
            string stateText = state switch
            {
                MtkDeviceState.Disconnected => "Disconnected",
                MtkDeviceState.Handshaking => "Handshaking...",
                MtkDeviceState.Brom => "BROM Mode",
                MtkDeviceState.Preloader => "Preloader Mode",
                MtkDeviceState.Da1Loaded => "DA1 Loaded",
                MtkDeviceState.Da2Loaded => "DA2 Loaded",
                MtkDeviceState.Error => "Error",
                _ => "Unknown"
            };

            mtkLblStatus.Text = $"Status: {stateText}";

            Color stateColor = state switch
            {
                MtkDeviceState.Da2Loaded => Color.Green,
                MtkDeviceState.Da1Loaded => Color.Cyan,
                MtkDeviceState.Brom => Color.Orange,
                MtkDeviceState.Preloader => Color.Orange,
                MtkDeviceState.Error => Color.Red,
                _ => Color.Gray
            };

            mtkLblStatus.ForeColor = stateColor;
        }

        private void MtkClearDeviceInfo()
        {
            mtkLblStatus.Text = "Status: Disconnected";
            mtkLblStatus.ForeColor = Color.Gray;
            mtkLblHwCode.Text = "HW: --";
            mtkLblChipName.Text = "Chip: --";
            mtkLblDaMode.Text = "Mode: --";
        }

        private MtkPartitionInfo[] MtkGetSelectedPartitions()
        {
            return mtkListPartitions.CheckedItems
                .Cast<ListViewItem>()
                .Where(item => item.Tag is MtkPartitionInfo)
                .Select(item => (MtkPartitionInfo)item.Tag)
                .ToArray();
        }

        private string MtkFindPartitionFile(string folder, string partitionName)
        {
            string[] extensions = { ".img", ".bin", ".dat", "" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(folder, partitionName + ext);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private string MtkFormatSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        // MTK Operation Timer
        private DateTime _mtkOperationStartTime;
        private long _mtkLastBytes;
        private DateTime _mtkLastSpeedUpdate;
        
        /// <summary>
        /// Start MTK Operation Timer
        /// </summary>
        private void MtkStartTimer()
        {
            _mtkOperationStartTime = DateTime.Now;
            _mtkLastBytes = 0;
            _mtkLastSpeedUpdate = DateTime.Now;
        }
        
        /// <summary>
        /// Get Elapsed Time
        /// </summary>
        private string MtkGetElapsedTime()
        {
            var elapsed = DateTime.Now - _mtkOperationStartTime;
            if (elapsed.TotalHours >= 1)
                return $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        
        /// <summary>
        /// Calculate Transfer Speed
        /// </summary>
        private string MtkCalculateSpeed(long currentBytes)
        {
            var now = DateTime.Now;
            var timeDiff = (now - _mtkLastSpeedUpdate).TotalSeconds;
            
            if (timeDiff < 0.5) return "";  // Update at least once every 0.5 seconds
            
            var bytesDiff = currentBytes - _mtkLastBytes;
            var speed = bytesDiff / timeDiff;
            
            _mtkLastBytes = currentBytes;
            _mtkLastSpeedUpdate = now;
            
            if (speed < 1024) return $"{speed:F0} B/s";
            if (speed < 1024 * 1024) return $"{speed / 1024:F1} KB/s";
            return $"{speed / 1024 / 1024:F1} MB/s";
        }
        
        /// <summary>
        /// Update Progress - Use Existing Dual Progress Bars
        /// </summary>
        private void MtkUpdateProgress(int current, int total, string statusText = null)
        {
            SafeInvoke(() =>
            {
                string timeInfo = "";
                string speedInfo = "";
                
                if (total > 0)
                {
                    int percentage = (int)((double)current / total * 100);
                    uiProcessBar1.Value = Math.Min(percentage, 100);
                    uiProcessBar2.Value = Math.Min(percentage, 100);
                    
                    // Update circular progress bars
                    progress1.Value = (float)percentage / 100f;
                    progress2.Value = (float)percentage / 100f;
                    
                    // 计算时间和速度
                    timeInfo = MtkGetElapsedTime();
                    if (current > 0 && current < total)
                    {
                        // Estimate Remaining Time
                        var elapsed = DateTime.Now - _mtkOperationStartTime;
                        var remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds / current * (total - current));
                        if (remaining.TotalHours >= 1)
                            timeInfo += $" / Remaining {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                        else if (remaining.TotalSeconds > 5)
                            timeInfo += $" / Remaining {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                        
                        // Calculate Speed (Assume current is bytes)
                        if (total > 1000)  // Only calculate if > 1KB
                        {
                            speedInfo = MtkCalculateSpeed(current);
                        }
                    }
                }
                else
                {
                    uiProcessBar1.Value = 0;
                    uiProcessBar2.Value = 0;
                    progress1.Value = 0;
                    progress2.Value = 0;
                }

                // Update Status Display
                if (!string.IsNullOrEmpty(statusText))
                {
                    string fullStatus = statusText;
                    if (!string.IsNullOrEmpty(timeInfo))
                        fullStatus += $" [{timeInfo}]";
                    if (!string.IsNullOrEmpty(speedInfo))
                        fullStatus += $" {speedInfo}";
                    
                    mtkLblStatus.Text = $"Status: {fullStatus}";
                }
            });
        }
        
        /// <summary>
        /// Update Progress (With Bytes, for Speed Calculation)
        /// </summary>
        private void MtkUpdateProgressWithBytes(long currentBytes, long totalBytes, string operation)
        {
            SafeInvoke(() =>
            {
                int percentage = totalBytes > 0 ? (int)((double)currentBytes / totalBytes * 100) : 0;
                uiProcessBar1.Value = Math.Min(percentage, 100);
                uiProcessBar2.Value = Math.Min(percentage, 100);
                progress1.Value = (float)percentage / 100f;
                progress2.Value = (float)percentage / 100f;
                
                // Calculate Speed and Time
                string timeInfo = MtkGetElapsedTime();
                string speedInfo = MtkCalculateSpeed(currentBytes);
                string sizeInfo = $"{MtkFormatSize((ulong)currentBytes)}/{MtkFormatSize((ulong)totalBytes)}";
                
                // Estimate Remaining Time
                if (currentBytes > 0 && currentBytes < totalBytes)
                {
                    var elapsed = DateTime.Now - _mtkOperationStartTime;
                    var remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds / currentBytes * (totalBytes - currentBytes));
                    if (remaining.TotalSeconds > 5)
                    {
                        if (remaining.TotalHours >= 1)
                            timeInfo += $" Remaining {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                        else
                            timeInfo += $" Remaining {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                    }
                }
                
                mtkLblStatus.Text = $"{operation} {sizeInfo} [{timeInfo}] {speedInfo}";
            });
        }

        /// <summary>
        /// Check if MTK operation is in progress (including waiting for device)
        /// </summary>
        public bool MtkHasPendingOperation => _mtkCts != null && !_mtkCts.IsCancellationRequested;

        /// <summary>
        /// Cancel current MTK operation
        /// </summary>
        public void MtkCancelOperation()
        {
            if (_mtkCts != null && !_mtkCts.IsCancellationRequested)
            {
                _mtkCts.Cancel();
                MtkLogWarning("Operation Cancelled");
                MtkUpdateProgress(0, 0, "Cancelled");
            }
        }

        #endregion

        #region MTK Scatter Config Parsing

        /// <summary>
        /// MTK Scatter Partition Entry
        /// </summary>
        public class MtkScatterEntry
        {
            public string Name { get; set; }
            public string FileName { get; set; }
            public long StartAddr { get; set; }
            public long Length { get; set; }
            public string Type { get; set; }
            public bool IsDownload { get; set; }
            public string Operation { get; set; }
        }

        /// <summary>
        /// Scatter File Selection Button Click Event
        /// </summary>
        private void MtkInputScatterFile_SuffixClick(object sender, EventArgs e)
        {
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.Filter = "Scatter File|*scatter*.txt;*scatter*|All Files|*.*";
                openDialog.Title = "Select MTK Scatter Config File";

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    _mtkScatterPath = openDialog.FileName;
                    mtkInputScatterFile.Text = openDialog.FileName;
                    LoadScatterPartitions(_mtkScatterPath);
                }
            }
        }

        /// <summary>
        /// Load Partition Config from Scatter File
        /// </summary>
        private void LoadScatterPartitions(string scatterPath)
        {
            try
            {
                _mtkScatterEntries = ParseScatterFile(scatterPath);

                if (_mtkScatterEntries != null && _mtkScatterEntries.Count > 0)
                {
                    AppendLog($"[MTK] Scatter Config Loaded: {_mtkScatterEntries.Count} Partitions", Color.Green);

                    // Update Partition List Display
                    mtkListPartitions.Items.Clear();
                    foreach (var entry in _mtkScatterEntries)
                    {
                        var item = new ListViewItem(entry.Name);
                        item.SubItems.Add(entry.Type ?? "--");
                        item.SubItems.Add(MtkFormatSize((ulong)entry.Length));
                        item.SubItems.Add($"0x{entry.StartAddr:X}");
                        item.SubItems.Add(entry.FileName ?? "--");
                        item.Tag = entry;
                        item.Checked = entry.IsDownload;
                        mtkListPartitions.Items.Add(item);
                    }

                    // Show Config File Path
                    AppendLog($"[MTK] Scatter File: {Path.GetFileName(scatterPath)}", Color.Cyan);
                }
                else
                {
                    AppendLog("[MTK] Scatter File Parse Failed or Empty", Color.Orange);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[MTK] Parse Scatter File Failed: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Parse MTK Scatter File
        /// Supports multiple formats: Legacy and New YAML format
        /// </summary>
        private List<MtkScatterEntry> ParseScatterFile(string filePath)
        {
            var entries = new List<MtkScatterEntry>();

            try
            {
                string content = File.ReadAllText(filePath);
                string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                MtkScatterEntry currentEntry = null;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    // Detect New Entry Start (partition_name: or - partition_name:)
                    if (line.StartsWith("- partition_name:") || line.StartsWith("partition_name:"))
                    {
                        // Save Previous Entry
                        if (currentEntry != null && !string.IsNullOrEmpty(currentEntry.Name))
                        {
                            entries.Add(currentEntry);
                        }

                        currentEntry = new MtkScatterEntry();
                        string value = ExtractScatterValue(line, "partition_name");
                        currentEntry.Name = value;
                        continue;
                    }

                    if (currentEntry == null)
                        continue;

                    // Parse Fields
                    if (line.StartsWith("file_name:"))
                    {
                        currentEntry.FileName = ExtractScatterValue(line, "file_name");
                    }
                    else if (line.StartsWith("linear_start_addr:"))
                    {
                        string addr = ExtractScatterValue(line, "linear_start_addr");
                        currentEntry.StartAddr = ParseHexOrDecimal(addr);
                    }
                    else if (line.StartsWith("physical_start_addr:"))
                    {
                        // Prefer physical_start_addr
                        string addr = ExtractScatterValue(line, "physical_start_addr");
                        currentEntry.StartAddr = ParseHexOrDecimal(addr);
                    }
                    else if (line.StartsWith("partition_size:"))
                    {
                        string size = ExtractScatterValue(line, "partition_size");
                        currentEntry.Length = ParseHexOrDecimal(size);
                    }
                    else if (line.StartsWith("type:"))
                    {
                        currentEntry.Type = ExtractScatterValue(line, "type");
                    }
                    else if (line.StartsWith("is_download:"))
                    {
                        string val = ExtractScatterValue(line, "is_download").ToLower();
                        currentEntry.IsDownload = val == "true" || val == "yes" || val == "1";
                    }
                    else if (line.StartsWith("operation_type:"))
                    {
                        currentEntry.Operation = ExtractScatterValue(line, "operation_type");
                    }
                }

                // Add Last Entry
                if (currentEntry != null && !string.IsNullOrEmpty(currentEntry.Name))
                {
                    entries.Add(currentEntry);
                }

                AppendLog($"[MTK] Scatter Parsed Success: {entries.Count} Partitions", Color.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"[MTK] check Scatter abnormal: {ex.Message}", Color.Red);
            }

            return entries;
        }

        /// <summary>
        /// Extract Value from Scatter Line
        /// </summary>
        private string ExtractScatterValue(string line, string key)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                return "";

            string value = line.Substring(colonIndex + 1).Trim();

            // Remove Possible Comments
            int commentIndex = value.IndexOf('#');
            if (commentIndex >= 0)
                value = value.Substring(0, commentIndex).Trim();

            // Remove Quotes
            value = value.Trim('"', '\'', ' ');

            return value;
        }

        /// <summary>
        /// Parse Hex or Decimal Value
        /// </summary>
        private long ParseHexOrDecimal(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            value = value.Trim();

            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToInt64(value.Substring(2), 16);
                }
                return Convert.ToInt64(value);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Flash Partition by Scatter Config
        /// </summary>
        private async Task MtkFlashFromScatterAsync(string imageFolder)
        {
            if (_mtkScatterEntries == null || _mtkScatterEntries.Count == 0)
            {
                AppendLog("[MTK] Scatter Config Not Loaded", Color.Orange);
                return;
            }

            var downloadEntries = _mtkScatterEntries.Where(e => e.IsDownload).ToList();
            if (downloadEntries.Count == 0)
            {
                AppendLog("[MTK] No Partitions Changed to Download in Scatter", Color.Orange);
                return;
            }

            int total = downloadEntries.Count;
            int current = 0;
            int success = 0;

            foreach (var entry in downloadEntries)
            {
                current++;
                MtkUpdateProgress(current, total, $"Flashing {entry.Name}...");

                // Find Image File
                string imagePath = null;
                if (!string.IsNullOrEmpty(entry.FileName))
                {
                    imagePath = Path.Combine(imageFolder, entry.FileName);
                    if (!File.Exists(imagePath))
                    {
                        // Try other common extensions
                        imagePath = MtkFindPartitionFile(imageFolder, entry.Name);
                    }
                }
                else
                {
                    imagePath = MtkFindPartitionFile(imageFolder, entry.Name);
                }

                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    AppendLog($"[MTK] Skip {entry.Name}: Image File Not Found", Color.Gray);
                    continue;
                }

                try
                {
                    AppendLog($"[MTK] Flashing {entry.Name} <- {Path.GetFileName(imagePath)}", Color.Cyan);
                    await _mtkService.WritePartitionAsync(entry.Name, imagePath, _mtkCts.Token);
                    success++;
                }
                catch (Exception ex)
                {
                    AppendLog($"[MTK] Flash {entry.Name} Failed: {ex.Message}", Color.Red);
                }
            }

            MtkUpdateProgress(100, 100, $"Completed {success}/{total}");
            AppendLog($"[MTK] Scatter Flash Complete: {success}/{total} Success", success == total ? Color.Green : Color.Orange);
        }

        #endregion
    }
}
