
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoveAlways.Fastboot.Common;
using LoveAlways.Fastboot.Models;
using LoveAlways.Fastboot.Payload;
using LoveAlways.Fastboot.Services;
using LoveAlways.Qualcomm.Common;

namespace LoveAlways.Fastboot.UI
{
    /// <summary>
    /// Fastboot UI Controller
    /// Responsible for connecting UI controls with Fastboot services
    /// </summary>
    public class FastbootUIController : IDisposable
    {
        private readonly Action<string, Color?> _log;
        private readonly Action<string> _logDetail;

        private FastbootService _service;
        private CancellationTokenSource _cts;
        private System.Windows.Forms.Timer _deviceRefreshTimer;
        private bool _disposed;

        // UI Control Binding
        private dynamic _deviceComboBox;      // Device selection combo box (independent)
        private dynamic _partitionListView;   // Partition list
        private dynamic _progressBar;         // Main progress bar
        private dynamic _subProgressBar;      // Sub progress bar
        private dynamic _commandComboBox;     // Quick command combo box
        private dynamic _payloadTextBox;      // Payload path
        private dynamic _outputPathTextBox;   // Output path

        // Device information labels (top right area)
        private dynamic _brandLabel;          // Brand
        private dynamic _chipLabel;           // Chip/Platform
        private dynamic _modelLabel;          // Model
        private dynamic _serialLabel;         // Serial number
        private dynamic _storageLabel;        // Storage type
        private dynamic _unlockLabel;         // Unlock status
        private dynamic _slotLabel;           // Current slot

        // Time/Speed/Operation status labels
        private dynamic _timeLabel;           // Time label
        private dynamic _speedLabel;          // Speed label
        private dynamic _operationLabel;      // Current operation label
        private dynamic _deviceCountLabel;    // Device count label

        // Checkbox controls
        private dynamic _autoRebootCheckbox;      // Auto reboot
        private dynamic _switchSlotCheckbox;      // Switch slot A
        private dynamic _eraseGoogleLockCheckbox; // Erase Google Lock
        private dynamic _keepDataCheckbox;        // Keep data
        private dynamic _fbdFlashCheckbox;        // FBD flash
        private dynamic _unlockBlCheckbox;        // Unlock BL
        private dynamic _lockBlCheckbox;          // Lock BL

        // Stopwatch and speed calculation
        private Stopwatch _operationStopwatch;
        private long _lastBytes;
        private DateTime _lastSpeedUpdate;
        private double _currentSpeed; // Current speed (bytes/s)
        private long _totalOperationBytes;
        private long _completedBytes;
        private string _currentOperationName;
        
        // Multi-partition flash progress tracking
        private int _flashTotalPartitions;
        private int _flashCurrentPartitionIndex;
        
        // Progress update throttling
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private double _lastSubProgressValue = -1;
        private double _lastMainProgressValue = -1;
        private const int ProgressUpdateIntervalMs = 16; // Approx 60fps

        // Device list cache
        private List<FastbootDeviceListItem> _cachedDevices = new List<FastbootDeviceListItem>();

        // Payload service
        private PayloadService _payloadService;
        private RemotePayloadService _remotePayloadService;

        // State
        public bool IsBusy { get; private set; }
        public bool IsConnected => _service?.IsConnected ?? false;
        public FastbootDeviceInfo DeviceInfo => _service?.DeviceInfo;
        public List<FastbootPartitionInfo> Partitions => _service?.DeviceInfo?.GetPartitions();
        public int DeviceCount => _cachedDevices?.Count ?? 0;
        
        // Payload state
        public bool IsPayloadLoaded => (_payloadService?.IsLoaded ?? false) || (_remotePayloadService?.IsLoaded ?? false);
        public IReadOnlyList<PayloadPartition> PayloadPartitions => _payloadService?.Partitions;
        public PayloadSummary PayloadSummary => _payloadService?.GetSummary();
        
        // Remote Payload state
        public bool IsRemotePayloadLoaded => _remotePayloadService?.IsLoaded ?? false;
        public IReadOnlyList<RemotePayloadPartition> RemotePayloadPartitions => _remotePayloadService?.Partitions;
        public RemotePayloadSummary RemotePayloadSummary => _remotePayloadService?.GetSummary();

        // Events
        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<List<FastbootPartitionInfo>> PartitionsLoaded;
        public event EventHandler<List<FastbootDeviceListItem>> DevicesRefreshed;
        public event EventHandler<PayloadSummary> PayloadLoaded;
        public event EventHandler<PayloadExtractProgress> PayloadExtractProgress;

        public FastbootUIController(Action<string, Color?> log, Action<string> logDetail = null)
        {
            _log = log ?? ((msg, color) => { });
            _logDetail = logDetail ?? (msg => { });

            // Initialize stopwatch
            _operationStopwatch = new Stopwatch();
            _lastSpeedUpdate = DateTime.Now;

            // Initialize device refresh timer
            _deviceRefreshTimer = new System.Windows.Forms.Timer();
            _deviceRefreshTimer.Interval = 2000; // Refresh every 2 seconds
            _deviceRefreshTimer.Tick += async (s, e) => await RefreshDeviceListAsync();
        }

        #region Log Methods

        private void Log(string message, Color? color = null)
        {
            _log(message, color);
        }

        #endregion

        #region Control Binding

        /// <summary>
        /// Bind UI controls
        /// </summary>
        public void BindControls(
            object deviceComboBox = null,
            object partitionListView = null,
            object progressBar = null,
            object subProgressBar = null,
            object commandComboBox = null,
            object payloadTextBox = null,
            object outputPathTextBox = null,
            // Device information labels
            object brandLabel = null,
            object chipLabel = null,
            object modelLabel = null,
            object serialLabel = null,
            object storageLabel = null,
            object unlockLabel = null,
            object slotLabel = null,
            // Time/Speed/Operation labels
            object timeLabel = null,
            object speedLabel = null,
            object operationLabel = null,
            object deviceCountLabel = null,
            // Checkbox controls
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

            // Device information labels
            _brandLabel = brandLabel;
            _chipLabel = chipLabel;
            _modelLabel = modelLabel;
            _serialLabel = serialLabel;
            _storageLabel = storageLabel;
            _unlockLabel = unlockLabel;
            _slotLabel = slotLabel;

            // Time/Speed/Operation labels
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

            // Initialize partition list
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

            // Initialize quick command combo box
            InitializeCommandComboBox();

            // Initialize device information display
            ResetDeviceInfoLabels();
        }

        /// <summary>
        /// Initialize quick command combo box (auto-complete)
        /// </summary>
        private void InitializeCommandComboBox()
        {
            if (_commandComboBox == null) return;

            try
            {
                // Standard Fastboot command list
                var commands = new string[]
                {
                    // Device info
                    "devices",
                    "getvar all",
                    "getvar product",
                    "getvar serialno",
                    "getvar version",
                    "getvar secure",
                    "getvar unlocked",
                    "getvar current-slot",
                    "getvar slot-count",
                    "getvar max-download-size",
                    "getvar is-userspace",
                    "getvar hw-revision",
                    "getvar variant",
                    
                    // Reboot commands
                    "reboot",
                    "reboot-bootloader",
                    "reboot-recovery",
                    "reboot-fastboot",
                    
                    // Unlock/Lock
                    "flashing unlock",
                    "flashing lock",
                    "flashing unlock_critical",
                    "flashing get_unlock_ability",
                    
                    // Slot operations
                    "set_active a",
                    "set_active b",
                    
                    // OEM commands
                    "oem device-info",
                    "oem unlock",
                    "oem lock",
                    "oem get_unlock_ability",
                    
                    // Erase
                    "erase frp",
                    "erase userdata",
                    "erase cache",
                    "erase metadata",
                };

                // Set combo box data source
                _commandComboBox.Items.Clear();
                foreach (var cmd in commands)
                {
                    _commandComboBox.Items.Add(cmd);
                }

                // Set auto-complete
                _commandComboBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
                _commandComboBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            }
            catch { }
        }

        /// <summary>
        /// Reset device information labels to default values
        /// </summary>
        public void ResetDeviceInfoLabels()
        {
            UpdateLabelSafe(_brandLabel, "Brand: Waiting");
            UpdateLabelSafe(_chipLabel, "Chip: Waiting");
            UpdateLabelSafe(_modelLabel, "Model: Waiting");
            UpdateLabelSafe(_serialLabel, "Serial: Waiting");
            UpdateLabelSafe(_storageLabel, "Storage: Waiting");
            UpdateLabelSafe(_unlockLabel, "Unlock: Waiting");
            UpdateLabelSafe(_slotLabel, "Slot: Waiting");
            UpdateLabelSafe(_timeLabel, "Time: 00:00");
            UpdateLabelSafe(_speedLabel, "Speed: 0 KB/s");
            UpdateLabelSafe(_operationLabel, "Operation: Idle");
            UpdateLabelSafe(_deviceCountLabel, "FB Devices: 0");
        }

        /// <summary>
        /// Update device information labels
        /// </summary>
        public void UpdateDeviceInfoLabels()
        {
            if (DeviceInfo == null)
            {
                ResetDeviceInfoLabels();
                return;
            }

            // Brand/Manufacturer
            string brand = DeviceInfo.GetVariable("ro.product.brand") 
                ?? DeviceInfo.GetVariable("manufacturer") 
                ?? "Unknown";
            UpdateLabelSafe(_brandLabel, $"Brand: {brand}");

            // Chip/Platform - Prefer variant, then map hw-revision
            string chip = DeviceInfo.GetVariable("variant");
            if (string.IsNullOrEmpty(chip) || chip == "Unknown")
            {
                string hwRev = DeviceInfo.GetVariable("hw-revision");
                chip = MapChipId(hwRev);
            }
            if (string.IsNullOrEmpty(chip) || chip == "Unknown")
            {
                chip = DeviceInfo.GetVariable("ro.boot.hardware") ?? "Unknown";
            }
            UpdateLabelSafe(_chipLabel, $"Chip: {chip}");

            // Model
            string model = DeviceInfo.GetVariable("product") 
                ?? DeviceInfo.GetVariable("ro.product.model") 
                ?? "Unknown";
            UpdateLabelSafe(_modelLabel, $"Model: {model}");

            // Serial
            string serial = DeviceInfo.Serial ?? "Unknown";
            UpdateLabelSafe(_serialLabel, $"Serial: {serial}");

            // Storage type
            string storage = DeviceInfo.GetVariable("partition-type:userdata") ?? "Unknown";
            if (storage.Contains("ext4") || storage.Contains("f2fs"))
                storage = "eMMC/UFS";
            UpdateLabelSafe(_storageLabel, $"Storage: {storage}");

            // Unlock status
            string unlocked = DeviceInfo.GetVariable("unlocked");
            string secureState = DeviceInfo.GetVariable("secure");
            string unlockStatus = "Unknown";
            if (!string.IsNullOrEmpty(unlocked))
            {
                unlockStatus = unlocked.ToLower() == "yes" || unlocked == "1" ? "Unlocked" : "Locked";
            }
            else if (!string.IsNullOrEmpty(secureState))
            {
                unlockStatus = secureState.ToLower() == "no" || secureState == "0" ? "Unlocked" : "Locked";
            }
            UpdateLabelSafe(_unlockLabel, $"Unlock: {unlockStatus}");

            // Current slot - support multiple variable names
            string slot = DeviceInfo.GetVariable("current-slot") 
                ?? DeviceInfo.CurrentSlot;
            string slotCount = DeviceInfo.GetVariable("slot-count");
            
            if (string.IsNullOrEmpty(slot))
            {
                // Check if A/B partitions are supported
                if (!string.IsNullOrEmpty(slotCount) && slotCount != "0")
                    slot = "Unknown";
                else
                    slot = "N/A";
            }
            else if (!slot.StartsWith("_"))
            {
                slot = "_" + slot;
            }
            UpdateLabelSafe(_slotLabel, $"Slot: {slot}");
        }

        /// <summary>
        /// Map Qualcomm chip ID to name
        /// </summary>
        private string MapChipId(string hwRevision)
        {
            if (string.IsNullOrEmpty(hwRevision)) return "Unknown";
            
            // Qualcomm Chip ID Map
            var chipMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Snapdragon 8xx series
                { "20001", "SDM845" },
                { "20002", "SDM845" },
                { "339", "SDM845" },
                { "321", "SDM835" },
                { "318", "SDM835" },
                { "360", "SDM855" },
                { "356", "SM8150" },
                { "415", "SM8250" },
                { "457", "SM8350" },
                { "530", "SM8450" },
                { "536", "SM8550" },
                { "591", "SM8650" },
                
                // Snapdragon 7xx series
                { "365", "SDM730" },
                { "366", "SDM730G" },
                { "400", "SDM765G" },
                { "434", "SM7250" },
                { "475", "SM7325" },
                
                // Snapdragon 6xx series
                { "317", "SDM660" },
                { "324", "SDM670" },
                { "345", "SDM675" },
                { "355", "SDM690" },
                
                // Snapdragon 4xx series
                { "293", "SDM450" },
                { "353", "SM4250" },
                
                // MTK series
                { "mt6893", "Dimensity 1200" },
                { "mt6885", "Dimensity 1000+" },
                { "mt6853", "Dimensity 720" },
                { "mt6873", "Dimensity 800" },
                { "mt6983", "Dimensity 9000" },
                { "mt6895", "Dimensity 8100" },
            };
            
            if (chipMap.TryGetValue(hwRevision, out string chipName))
                return chipName;
            
            // If not in map, check if it's pure number (possibly unknown Qualcomm ID)
            if (int.TryParse(hwRevision, out _))
                return $"QC-{hwRevision}";
            
            return hwRevision;
        }

        /// <summary>
        /// Safely update Label text
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
        /// Start device monitoring
        /// </summary>
        public void StartDeviceMonitoring()
        {
            _deviceRefreshTimer.Start();
            Task.Run(() => RefreshDeviceListAsync());
        }

        /// <summary>
        /// Stop device monitoring
        /// </summary>
        public void StopDeviceMonitoring()
        {
            _deviceRefreshTimer.Stop();
        }

        #endregion

        #region Device Operations

        /// <summary>
        /// Refresh device list
        /// </summary>
        public async Task RefreshDeviceListAsync()
        {
            try
            {
                using (var tempService = new FastbootService(msg => { }))
                {
                    var devices = await tempService.GetDevicesAsync();
                    _cachedDevices = devices ?? new List<FastbootDeviceListItem>();
                    
                    // Update in UI thread
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

                    // Update device count display
                    UpdateDeviceCountLabel();

                    DevicesRefreshed?.Invoke(this, devices);
                }
            }
            catch (Exception ex)
            {
                _logDetail($"[Fastboot] Exception refreshing device list: {ex.Message}");
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

                // Try to restore previous selection
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
        /// Update device count label
        /// </summary>
        private void UpdateDeviceCountLabel()
        {
            int count = _cachedDevices?.Count ?? 0;
            string text = count == 0 ? "FB Device: 0" 
                : count == 1 ? $"FB Device: {_cachedDevices[0].Serial}" 
                : $"FB Devices: {count}";
            
            UpdateLabelSafe(_deviceCountLabel, text);
        }

        /// <summary>
        /// Connect to selected device
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            string selectedDevice = GetSelectedDevice();
            if (string.IsNullOrEmpty(selectedDevice))
            {
                Log("Please select a Fastboot device", Color.Red);
                return false;
            }

            // Extract serial number from "serial (status)" format
            string serial = selectedDevice.Split(' ')[0];

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Connect Device");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "Operation: Connecting Device");

                _service = new FastbootService(
                    msg => Log(msg, null),
                    (current, total) => UpdateProgressWithSpeed(current, total),
                    _logDetail
                );
                
                // Subscribe to flash progress events
                _service.FlashProgressChanged += OnFlashProgressChanged;

                UpdateProgressBar(30);
                bool success = await _service.SelectDeviceAsync(serial, _cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);
                    Log("Fastboot device connected successfully", Color.Green);
                    
                    // Update device information labels
                    UpdateDeviceInfoLabels();
                    
                    // Update partition list
                    UpdatePartitionListView();
                    
                    UpdateProgressBar(100);
                    ConnectionStateChanged?.Invoke(this, true);
                    PartitionsLoaded?.Invoke(this, Partitions);
                }
                else
                {
                    Log("Fastboot device connection failed", Color.Red);
                    ResetDeviceInfoLabels();
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"Connection exception: {ex.Message}", Color.Red);
                ResetDeviceInfoLabels();
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Disconnect
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

        #region Partition Operations

        /// <summary>
        /// Read partition table (refresh device info)
        /// </summary>
        public async Task<bool> ReadPartitionTableAsync()
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Read GPT");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "Operation: Reading Partition Table");
                UpdateLabelSafe(_speedLabel, "Speed: Reading...");

                Log("Reading Fastboot partition table...", Color.Blue);

                UpdateProgressBar(30);
                bool success = await _service.RefreshDeviceInfoAsync(_cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);
                    
                    // Update device information labels
                    UpdateDeviceInfoLabels();
                    
                    UpdatePartitionListView();
                    UpdateProgressBar(100);
                    
                    Log($"Successfully read {Partitions?.Count ?? 0} partitions", Color.Green);
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
                Log($"Failed to read partition table: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Update partition list view
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
                        "-",  // Operation column
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
        /// Flash selected partitions
        /// </summary>
        public async Task<bool> FlashSelectedPartitionsAsync()
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            var selectedItems = GetSelectedPartitionItems();
            if (selectedItems.Count == 0)
            {
                Log("Please select partitions to flash", Color.Orange);
                return false;
            }

            // Check for image files
            var partitionsWithFiles = new List<Tuple<string, string>>();
            foreach (ListViewItem item in selectedItems)
            {
                string partName = item.SubItems[0].Text;
                string filePath = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
                
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Log($"Partition {partName} has no image file selected", Color.Orange);
                    continue;
                }

                partitionsWithFiles.Add(Tuple.Create(partName, filePath));
            }

            if (partitionsWithFiles.Count == 0)
            {
                Log("No partitions to flash (please double-click to select image file)", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Flash Partition");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_operationLabel, $"Operation: Flashing {partitionsWithFiles.Count} partitions");
                UpdateLabelSafe(_speedLabel, "Speed: Calculating...");

                Log($"Starting to flash {partitionsWithFiles.Count} partitions...", Color.Blue);

                int successCount = 0;
                int total = partitionsWithFiles.Count;
                
                // Set progress tracking fields
                _flashTotalPartitions = total;

                for (int i = 0; i < total; i++)
                {
                    _flashCurrentPartitionIndex = i;
                    
                    var part = partitionsWithFiles[i];
                    UpdateLabelSafe(_operationLabel, $"Operation: Flashing {part.Item1} ({i + 1}/{total})");
                    // Sub-progress: current partition flash started
                    UpdateSubProgressBar(0);

                    var flashStart = DateTime.Now;
                    var fileSize = new FileInfo(part.Item2).Length;
                    
                    bool result = await _service.FlashPartitionAsync(part.Item1, part.Item2, false, _cts.Token);
                    
                    // Sub-progress: current partition flash completed
                    UpdateSubProgressBar(100);
                    // Update total progress
                    UpdateProgressBar(((i + 1) * 100.0) / total);
                    
                    // Calculate and display speed
                    var elapsed = (DateTime.Now - flashStart).TotalSeconds;
                    if (elapsed > 0)
                    {
                        double speed = fileSize / elapsed;
                        UpdateSpeedLabel(FormatSpeed(speed));
                    }
                    
                    if (result)
                        successCount++;
                }
                
                // Reset progress tracking
                _flashTotalPartitions = 0;
                _flashCurrentPartitionIndex = 0;

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"Flash complete: {successCount}/{total} successful", 
                    successCount == total ? Color.Green : Color.Orange);

                // Execute post-flash additional operations (switch slot, erase Google Lock, etc.)
                if (successCount > 0)
                {
                    await ExecutePostFlashOperationsAsync();
                }

                // Auto reboot
                if (IsAutoRebootEnabled() && successCount > 0)
                {
                    await _service.RebootAsync(_cts.Token);
                }

                return successCount == total;
            }
            catch (Exception ex)
            {
                Log($"Flash failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Erase selected partitions
        /// </summary>
        public async Task<bool> EraseSelectedPartitionsAsync()
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            var selectedItems = GetSelectedPartitionItems();
            if (selectedItems.Count == 0)
            {
                Log("Please select partitions to erase", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Erase Partition");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Erasing...");

                int success = 0;
                int total = selectedItems.Count;
                int current = 0;

                Log($"Starting to erase {total} partitions...", Color.Blue);

                foreach (ListViewItem item in selectedItems)
                {
                    string partName = item.SubItems[0].Text;
                    UpdateLabelSafe(_operationLabel, $"Operation: Erasing {partName} ({current + 1}/{total})");
                    // Total progress: based on completed partitions
                    UpdateProgressBar((current * 100.0) / total);
                    // Sub-progress: start erase
                    UpdateSubProgressBar(0);
                    
                    if (await _service.ErasePartitionAsync(partName, _cts.Token))
                    {
                        success++;
                    }
                    
                    // Sub-progress: current partition erase complete
                    UpdateSubProgressBar(100);
                    current++;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"Erase complete: {success}/{total} successful", 
                    success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (Exception ex)
            {
                Log($"Erase failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
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
        /// Check for checked normal partitions (non-script, with image file)
        /// </summary>
        public bool HasSelectedPartitionsWithFiles()
        {
            if (_partitionListView == null) return false;

            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    // Skip script tasks and Payload partitions
                    if (item.Tag is BatScriptParser.FlashTask) continue;
                    if (item.Tag is PayloadPartition) continue;
                    if (item.Tag is RemotePayloadPartition) continue;

                    // Check for image file path
                    string filePath = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Select image file for partition
        /// </summary>
        public void SelectImageForPartition(ListViewItem item)
        {
            if (item == null) return;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"Select {item.SubItems[0].Text} partition image";
                ofd.Filter = "Image Files|*.img;*.bin;*.mbn|All Files|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    // Update operation and file path columns
                    if (item.SubItems.Count > 1)
                        item.SubItems[1].Text = "Write";
                    if (item.SubItems.Count > 3)
                        item.SubItems[3].Text = ofd.FileName;
                    else
                    {
                        while (item.SubItems.Count < 4)
                            item.SubItems.Add("");
                        item.SubItems[3].Text = ofd.FileName;
                    }

                    item.Checked = true;
                    Log($"Image selected: {Path.GetFileName(ofd.FileName)} -> {item.SubItems[0].Text}", Color.Blue);
                }
            }
        }

        /// <summary>
        /// Load extracted folder (contains .img files)
        /// Auto-identify partition names and add to list, parse device info
        /// </summary>
        public void LoadExtractedFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                Log("Folder does not exist", Color.Red);
                return;
            }

            if (_partitionListView == null)
            {
                Log("Partition list not initialized", Color.Red);
                return;
            }

            // Scan all .img files in folder
            var imgFiles = Directory.GetFiles(folderPath, "*.img", SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToList();

            if (imgFiles.Count == 0)
            {
                Log($"No .img files found in folder: {folderPath}", Color.Orange);
                return;
            }

            Log($"Scanned {imgFiles.Count} image files", Color.Blue);

            // Clear existing list
            _partitionListView.Items.Clear();

            int addedCount = 0;
            long totalSize = 0;

            foreach (var imgPath in imgFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(imgPath);
                var fileInfo = new FileInfo(imgPath);
                
                // Determine partition type
                bool isLogical = FastbootService.IsLogicalPartition(fileName);
                bool isModem = FastbootService.IsModemPartition(fileName);
                string partType = isLogical ? "Logical" : (isModem ? "Modem" : "Physical");

                // Create list item (Order: Name, Operation, Size, Type, Path)
                var item = new ListViewItem(new[]
                {
                    fileName,                           // Name
                    "flash",                            // Operation
                    FormatSize(fileInfo.Length),       // Size
                    partType,                           // Type
                    imgPath                             // Path
                });

                // Store file information in Tag
                item.Tag = new ExtractedImageInfo
                {
                    PartitionName = fileName,
                    FilePath = imgPath,
                    FileSize = fileInfo.Length,
                    IsLogical = isLogical,
                    IsModem = isModem
                };

                item.Checked = true;  // Check all by default
                _partitionListView.Items.Add(item);
                addedCount++;
                totalSize += fileInfo.Length;
            }

            Log($"Loaded {addedCount} partitions, total size: {FormatSize(totalSize)}", Color.Green);
            Log($"Source folder: {folderPath}", Color.Blue);

            // Asynchronously parse firmware info
            int partitionCount = addedCount;
            long size = totalSize;
            _ = Task.Run(async () =>
            {
                try
                {
                    Log("Parsing firmware information...", Color.Blue);
                    CurrentFirmwareInfo = await ParseFirmwareInfoAsync(folderPath);
                    CurrentFirmwareInfo.TotalPartitions = partitionCount;
                    CurrentFirmwareInfo.TotalSize = size;

                    // Display parsing results on UI thread
                    if (_partitionListView?.InvokeRequired == true)
                    {
                        _partitionListView.Invoke(new Action(() => DisplayFirmwareInfo()));
                    }
                    else
                    {
                        DisplayFirmwareInfo();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Firmware info parsing failed: {ex.Message}", Color.Red);
                    _logDetail($"Firmware info parsing exception: {ex}");
                }
            });
        }

        /// <summary>
        /// Display firmware info
        /// </summary>
        private void DisplayFirmwareInfo()
        {
            if (CurrentFirmwareInfo == null)
            {
                Log("Firmware Info: Metadata file not found, unable to identify device info", Color.Orange);
                return;
            }

            var info = CurrentFirmwareInfo;
            var sb = new System.Text.StringBuilder();
            sb.Append("Firmware Info: ");
            bool hasInfo = false;

            // Display model
            if (!string.IsNullOrEmpty(info.DeviceModel))
            {
                sb.Append($"Model={info.DeviceModel} ");
                hasInfo = true;
            }

            // Display device name (codename)
            if (!string.IsNullOrEmpty(info.DeviceName))
            {
                sb.Append($"Name={info.DeviceName} ");
                hasInfo = true;
            }

            // Android version
            if (!string.IsNullOrEmpty(info.AndroidVersion))
            {
                sb.Append($"Android={info.AndroidVersion} ");
                hasInfo = true;
            }

            // OS version
            if (!string.IsNullOrEmpty(info.OsVersion))
            {
                sb.Append($"OS={info.OsVersion} ");
                hasInfo = true;
            }

            // Security patch
            if (!string.IsNullOrEmpty(info.SecurityPatch))
            {
                sb.Append($"Security Patch={info.SecurityPatch} ");
                hasInfo = true;
            }

            // OTA type
            if (!string.IsNullOrEmpty(info.OtaType))
            {
                sb.Append($"Type={info.OtaType} ");
                hasInfo = true;
            }

            if (hasInfo)
            {
                Log(sb.ToString().Trim(), Color.Cyan);
                
                // If version name exists, display separately
                if (!string.IsNullOrEmpty(info.BuildNumber))
                {
                    Log($"Version: {info.BuildNumber}", Color.Gray);
                }
            }
            else
            {
                Log("Firmware info: Metadata file not found", Color.Orange);
            }
        }

        /// <summary>
        /// Verify hash values for all partition files
        /// </summary>
        public async Task<bool> VerifyPartitionHashesAsync(CancellationToken ct = default)
        {
            if (_partitionListView == null || _partitionListView.Items.Count == 0)
            {
                Log("No partitions to verify", Color.Orange);
                return false;
            }

            Log("Starting to verify partition integrity...", Color.Blue);
            UpdateLabelSafe(_operationLabel, "Operation: Verifying Files");

            int total = _partitionListView.CheckedItems.Count;
            int current = 0;
            int verified = 0;
            int failed = 0;

            foreach (ListViewItem item in _partitionListView.CheckedItems)
            {
                ct.ThrowIfCancellationRequested();

                if (item.Tag is ExtractedImageInfo info && !string.IsNullOrEmpty(info.FilePath))
                {
                    current++;
                    UpdateProgressBar(current * 100.0 / total);
                    UpdateLabelSafe(_operationLabel, $"Verify: {info.PartitionName} ({current}/{total})");

                    // Check if file exists
                    if (!File.Exists(info.FilePath))
                    {
                        Log($"  ✗ {info.PartitionName}: File does not exist", Color.Red);
                        failed++;
                        continue;
                    }

                    // Check file size
                    var fileInfo = new FileInfo(info.FilePath);
                    if (fileInfo.Length != info.FileSize)
                    {
                        Log($"  ✗ {info.PartitionName}: Size mismatch (Expected={FormatSize(info.FileSize)}, Actual={FormatSize(fileInfo.Length)})", Color.Red);
                        failed++;
                        continue;
                    }

                    // Calculate MD5 hash
                    string hash = await Task.Run(() => CalculateMd5(info.FilePath), ct);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        info.Md5Hash = hash;
                        info.HashVerified = true;
                        verified++;
                        _logDetail($"  ✓ {info.PartitionName}: MD5={hash}");
                    }
                    else
                    {
                        failed++;
                        Log($"  ✗ {info.PartitionName}: Hash calculation failed", Color.Red);
                    }
                }
            }

            UpdateProgressBar(100);
            UpdateLabelSafe(_operationLabel, "Operation: Idle");

            if (failed == 0)
            {
                Log($"✓ Verification complete: All {verified} partitions passed", Color.Green);
                return true;
            }
            else
            {
                Log($"⚠ Verification complete: {verified} passed, {failed} failed", Color.Orange);
                return false;
            }
        }

        /// <summary>
        /// Extracted image file info
        /// </summary>
        public class ExtractedImageInfo
        {
            public string PartitionName { get; set; }
            public string FilePath { get; set; }
            public long FileSize { get; set; }
            public bool IsLogical { get; set; }
            public bool IsModem { get; set; }
            public string Md5Hash { get; set; }
            public string Sha256Hash { get; set; }
            public bool HashVerified { get; set; }
        }

        /// <summary>
        /// Firmware package info (parsed from metadata)
        /// </summary>
        public class FirmwareInfo
        {
            public string DeviceModel { get; set; }       // Device model (e.g. PJD110)
            public string DeviceName { get; set; }        // Device codename (e.g. OP5929L1)
            public string AndroidVersion { get; set; }    // Android version
            public string OsVersion { get; set; }         // OS version (ColorOS/OxygenOS)
            public string BuildNumber { get; set; }       // Build number/Version name
            public string SecurityPatch { get; set; }     // Security patch date
            public string Fingerprint { get; set; }       // Full fingerprint
            public string BasebandVersion { get; set; }   // Baseband version
            public string OtaType { get; set; }           // OTA type (AB/Non-AB)
            public string FolderPath { get; set; }        // Source folder
            public int TotalPartitions { get; set; }      // Total partitions
            public long TotalSize { get; set; }           // Total size
        }

        /// <summary>
        /// Currently loaded firmware info
        /// </summary>
        public FirmwareInfo CurrentFirmwareInfo { get; private set; }

        /// <summary>
        /// Calculate file MD5 hash
        /// </summary>
        private string CalculateMd5(string filePath)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Calculate file SHA256 hash
        /// </summary>
        private string CalculateSha256(string filePath)
        {
            try
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Asynchronously calculate hashes for all partitions
        /// </summary>
        public async Task CalculatePartitionHashesAsync(CancellationToken ct = default)
        {
            if (_partitionListView == null) return;

            Log("Starting to calculate partition hashes...", Color.Blue);
            UpdateLabelSafe(_operationLabel, "Operation: Calculating Hashes");
            int total = _partitionListView.Items.Count;
            int current = 0;

            foreach (ListViewItem item in _partitionListView.Items)
            {
                ct.ThrowIfCancellationRequested();
                
                if (item.Tag is ExtractedImageInfo info && !string.IsNullOrEmpty(info.FilePath))
                {
                    current++;
                    UpdateProgressBar(current * 100.0 / total);
                    UpdateLabelSafe(_operationLabel, $"Calculate Hash: {info.PartitionName} ({current}/{total})");

                    // Calculate hash in background thread
                    info.Md5Hash = await Task.Run(() => CalculateMd5(info.FilePath), ct);
                    info.HashVerified = !string.IsNullOrEmpty(info.Md5Hash);
                }
            }

            UpdateProgressBar(100);
            UpdateLabelSafe(_operationLabel, "Operation: Idle");
            Log($"Hash calculation complete, total {total} partitions", Color.Green);
        }

        /// <summary>
        /// Parse device info from firmware folder
        /// Prioritize reading from META-INF/com/android/metadata
        /// </summary>
        private async Task<FirmwareInfo> ParseFirmwareInfoAsync(string folderPath)
        {
            var info = new FirmwareInfo { FolderPath = folderPath };

            try
            {
                // Get parent directory (firmware package root)
                string parentDir = Directory.GetParent(folderPath)?.FullName ?? folderPath;

                // 1. Prioritize reading from META-INF/com/android/metadata (standard OTA package format)
                string[] metadataPaths = {
                    Path.Combine(parentDir, "META-INF", "com", "android", "metadata"),
                    Path.Combine(folderPath, "META-INF", "com", "android", "metadata"),
                    Path.Combine(parentDir, "metadata"),
                    Path.Combine(folderPath, "metadata")
                };

                foreach (var metaPath in metadataPaths)
                {
                    if (File.Exists(metaPath))
                    {
                        _logDetail($"Parsing from metadata file: {metaPath}");
                        await ParseMetadataFileAsync(metaPath, info);
                        if (!string.IsNullOrEmpty(info.DeviceName) || !string.IsNullOrEmpty(info.OsVersion))
                            break;
                    }
                }

                // 2. Read supplementary info from payload_properties.txt
                string[] propPaths = {
                    Path.Combine(parentDir, "payload_properties.txt"),
                    Path.Combine(folderPath, "payload_properties.txt")
                };

                foreach (var propPath in propPaths)
                {
                    if (File.Exists(propPath))
                    {
                        _logDetail($"Parsing from payload_properties.txt: {propPath}");
                        await ParsePayloadPropertiesAsync(propPath, info);
                        break;
                    }
                }

                // 3. Attempt to read info from META folder (OPLUS firmware)
                string metaDir = Path.Combine(folderPath, "META");
                if (!Directory.Exists(metaDir))
                    metaDir = Path.Combine(parentDir, "META");

                if (Directory.Exists(metaDir))
                {
                    string miscInfo = Path.Combine(metaDir, "misc_info.txt");
                    if (File.Exists(miscInfo))
                    {
                        var lines = await Task.Run(() => File.ReadAllLines(miscInfo));
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("build_fingerprint="))
                                info.Fingerprint = info.Fingerprint ?? line.Substring(18);
                        }
                    }
                }

                // 4. Attempt to read build.prop (if extracted files exist)
                string[] buildPropPaths = {
                    Path.Combine(folderPath, "build.prop"),
                    Path.Combine(folderPath, "system_build.prop"),
                    Path.Combine(folderPath, "vendor_build.prop")
                };

                foreach (var propPath in buildPropPaths)
                {
                    if (File.Exists(propPath))
                    {
                        await ParseBuildPropAsync(propPath, info);
                        break;
                    }
                }

                // 5. Infer baseband info from modem.img
                string modemPath = Path.Combine(folderPath, "modem.img");
                if (File.Exists(modemPath))
                {
                    var modemInfo = new FileInfo(modemPath);
                    info.BasebandVersion = $"Modem ({FormatSize(modemInfo.Length)})";
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to parse firmware info: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Parse META-INF/com/android/metadata file
        /// </summary>
        private async Task ParseMetadataFileAsync(string metadataPath, FirmwareInfo info)
        {
            try
            {
                var lines = await Task.Run(() => File.ReadAllLines(metadataPath));
                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                        continue;

                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string key = line.Substring(0, eqIndex).Trim();
                        string value = line.Substring(eqIndex + 1).Trim();
                        props[key] = value;
                    }
                }

                // Parse key attributes
                if (props.TryGetValue("product_name", out string product))
                    info.DeviceModel = product;

                if (props.TryGetValue("pre-device", out string device))
                    info.DeviceName = device;

                if (props.TryGetValue("android_version", out string android))
                    info.AndroidVersion = android;

                if (props.TryGetValue("os_version", out string os))
                    info.OsVersion = os;
                else if (props.TryGetValue("display_os_version", out os))
                    info.OsVersion = os;

                if (props.TryGetValue("version_name", out string version))
                    info.BuildNumber = version;
                else if (props.TryGetValue("version_name_show", out version))
                    info.BuildNumber = version;

                if (props.TryGetValue("security_patch", out string patch))
                    info.SecurityPatch = patch;
                else if (props.TryGetValue("post-security-patch-level", out patch))
                    info.SecurityPatch = patch;

                if (props.TryGetValue("post-build", out string fingerprint))
                    info.Fingerprint = fingerprint;

                if (props.TryGetValue("ota-type", out string otaType))
                    info.OtaType = otaType;

                _logDetail($"Parsed {props.Count} properties from metadata");
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to parse metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse payload_properties.txt file
        /// </summary>
        private async Task ParsePayloadPropertiesAsync(string propsPath, FirmwareInfo info)
        {
            try
            {
                var lines = await Task.Run(() => File.ReadAllLines(propsPath));

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                        continue;

                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string key = line.Substring(0, eqIndex).Trim();
                        string value = line.Substring(eqIndex + 1).Trim();

                        switch (key.ToLower())
                        {
                            case "android_version":
                                info.AndroidVersion = info.AndroidVersion ?? value;
                                break;
                            case "oplus_rom_version":
                                info.OsVersion = info.OsVersion ?? value;
                                break;
                            case "security_patch":
                                info.SecurityPatch = info.SecurityPatch ?? value;
                                break;
                            case "ota_target_version":
                                info.BuildNumber = info.BuildNumber ?? value;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to parse payload_properties.txt: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse firmware info from Payload file (ZIP or same directory)
        /// </summary>
        private async Task ParseFirmwareInfoFromPayloadAsync(string payloadPath)
        {
            try
            {
                var info = new FirmwareInfo { FolderPath = Path.GetDirectoryName(payloadPath) };
                string ext = Path.GetExtension(payloadPath).ToLowerInvariant();
                string parentDir = Path.GetDirectoryName(payloadPath);

                // If ZIP file, try to read metadata from inside
                if (ext == ".zip")
                {
                    await Task.Run(() => ParseFirmwareInfoFromZip(payloadPath, info));
                }

                // If not found in ZIP, try to read from files in the same directory
                if (string.IsNullOrEmpty(info.DeviceModel) && string.IsNullOrEmpty(info.DeviceName))
                {
                    // Find metadata file in the same directory
                    string[] metadataPaths = {
                        Path.Combine(parentDir, "META-INF", "com", "android", "metadata"),
                        Path.Combine(parentDir, "metadata")
                    };

                    foreach (var metaPath in metadataPaths)
                    {
                        if (File.Exists(metaPath))
                        {
                            await ParseMetadataFileAsync(metaPath, info);
                            break;
                        }
                    }

                    // Find payload_properties.txt
                    string propsPath = Path.Combine(parentDir, "payload_properties.txt");
                    if (File.Exists(propsPath))
                    {
                        await ParsePayloadPropertiesAsync(propsPath, info);
                    }
                }

                // If info parsed, display it
                if (!string.IsNullOrEmpty(info.DeviceModel) || !string.IsNullOrEmpty(info.DeviceName) ||
                    !string.IsNullOrEmpty(info.AndroidVersion) || !string.IsNullOrEmpty(info.OsVersion))
                {
                    CurrentFirmwareInfo = info;
                    DisplayFirmwareInfo();
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to parse firmware info from Payload: {ex.Message}");
            }
        }

        /// <summary>
        /// Read firmware info from inside ZIP file
        /// </summary>
        private void ParseFirmwareInfoFromZip(string zipPath, FirmwareInfo info)
        {
            try
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    // Find META-INF/com/android/metadata
                    var metadataEntry = archive.GetEntry("META-INF/com/android/metadata");
                    if (metadataEntry != null)
                    {
                        using (var stream = metadataEntry.Open())
                        using (var reader = new StreamReader(stream))
                        {
                            ParseMetadataContent(reader.ReadToEnd(), info);
                        }
                        _logDetail($"Parsed from metadata inside ZIP successfully");
                    }

                    // Find payload_properties.txt
                    var propsEntry = archive.GetEntry("payload_properties.txt");
                    if (propsEntry != null)
                    {
                        using (var stream = propsEntry.Open())
                        using (var reader = new StreamReader(stream))
                        {
                            ParsePayloadPropertiesContent(reader.ReadToEnd(), info);
                        }
                        _logDetail($"Parsed from payload_properties.txt inside ZIP successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to read firmware info from ZIP: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse metadata content string
        /// </summary>
        private void ParseMetadataContent(string content, FirmwareInfo info)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains("=")) continue;

                int eqIndex = line.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = line.Substring(0, eqIndex).Trim();
                    string value = line.Substring(eqIndex + 1).Trim();
                    props[key] = value;
                }
            }

            if (props.TryGetValue("product_name", out string product))
                info.DeviceModel = product;
            if (props.TryGetValue("pre-device", out string device))
                info.DeviceName = device;
            if (props.TryGetValue("android_version", out string android))
                info.AndroidVersion = android;
            if (props.TryGetValue("os_version", out string os))
                info.OsVersion = os;
            else if (props.TryGetValue("display_os_version", out os))
                info.OsVersion = os;
            if (props.TryGetValue("version_name", out string version))
                info.BuildNumber = version;
            else if (props.TryGetValue("version_name_show", out version))
                info.BuildNumber = version;
            if (props.TryGetValue("security_patch", out string patch))
                info.SecurityPatch = patch;
            else if (props.TryGetValue("post-security-patch-level", out patch))
                info.SecurityPatch = patch;
            if (props.TryGetValue("post-build", out string fingerprint))
                info.Fingerprint = fingerprint;
            if (props.TryGetValue("ota-type", out string otaType))
                info.OtaType = otaType;
        }

        /// <summary>
        /// Parse payload_properties.txt content string
        /// </summary>
        private void ParsePayloadPropertiesContent(string content, FirmwareInfo info)
        {
            foreach (var line in content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains("=")) continue;

                int eqIndex = line.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = line.Substring(0, eqIndex).Trim();
                    string value = line.Substring(eqIndex + 1).Trim();

                    switch (key.ToLower())
                    {
                        case "android_version":
                            info.AndroidVersion = info.AndroidVersion ?? value;
                            break;
                        case "oplus_rom_version":
                            info.OsVersion = info.OsVersion ?? value;
                            break;
                        case "security_patch":
                            info.SecurityPatch = info.SecurityPatch ?? value;
                            break;
                        case "ota_target_version":
                            info.BuildNumber = info.BuildNumber ?? value;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Parse build.prop file
        /// </summary>
        private async Task ParseBuildPropAsync(string propPath, FirmwareInfo info)
        {
            try
            {
                var lines = await Task.Run(() => File.ReadAllLines(propPath));
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "ro.product.model":
                        case "ro.product.system.model":
                            if (string.IsNullOrEmpty(info.DeviceName))
                                info.DeviceName = value;
                            break;
                        case "ro.product.device":
                        case "ro.product.system.device":
                            if (string.IsNullOrEmpty(info.DeviceModel))
                                info.DeviceModel = value;
                            break;
                        case "ro.build.version.release":
                            info.AndroidVersion = value;
                            break;
                        case "ro.build.display.id":
                        case "ro.system.build.version.incremental":
                            if (string.IsNullOrEmpty(info.BuildNumber))
                                info.BuildNumber = value;
                            break;
                        case "ro.build.version.security_patch":
                            info.SecurityPatch = value;
                            break;
                        case "ro.build.fingerprint":
                            info.Fingerprint = value;
                            break;
                        case "ro.oplus.version":
                        case "ro.build.version.ota":
                            info.OsVersion = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to parse build.prop: {ex.Message}");
            }
        }

        #endregion

        #region Reboot Operations

        /// <summary>
        /// Reboot to system
        /// </summary>
        public async Task<bool> RebootToSystemAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.RebootAsync();
        }

        /// <summary>
        /// Reboot to Bootloader
        /// </summary>
        public async Task<bool> RebootToBootloaderAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.RebootBootloaderAsync();
        }

        /// <summary>
        /// Reboot to Fastbootd
        /// </summary>
        public async Task<bool> RebootToFastbootdAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.RebootFastbootdAsync();
        }

        /// <summary>
        /// Reboot to Recovery
        /// </summary>
        public async Task<bool> RebootToRecoveryAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.RebootRecoveryAsync();
        }
        
        // Alias methods (for quick use)
        public Task<bool> RebootAsync() => RebootToSystemAsync();
        public Task<bool> RebootBootloaderAsync() => RebootToBootloaderAsync();
        public Task<bool> RebootFastbootdAsync() => RebootToFastbootdAsync();
        public Task<bool> RebootRecoveryAsync() => RebootToRecoveryAsync();
        
        /// <summary>
        /// OEM EDL - Xiaomi kick to EDL (fastboot oem edl)
        /// </summary>
        public async Task<bool> OemEdlAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.OemEdlAsync();
        }
        
        /// <summary>
        /// Erase FRP (Google Lock)
        /// </summary>
        public async Task<bool> EraseFrpAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.EraseFrpAsync();
        }
        
        /// <summary>
        /// Get current slot
        /// </summary>
        public async Task<string> GetCurrentSlotAsync()
        {
            if (!await EnsureConnectedAsync()) return null;
            return await _service.GetCurrentSlotAsync();
        }
        
        /// <summary>
        /// Set active slot
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot)
        {
            if (!await EnsureConnectedAsync()) return false;
            return await _service.SetActiveSlotAsync(slot, _cts?.Token ?? CancellationToken.None);
        }

        #endregion

        #region Unlock/Lock

        /// <summary>
        /// Execute unlock operation
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync()
        {
            if (!await EnsureConnectedAsync()) return false;

            string method = "flashing unlock";
            
            // Choose unlock method based on checkbox state
            // Command can be retrieved from _commandComboBox
            string selectedCmd = GetSelectedCommand();
            if (!string.IsNullOrEmpty(selectedCmd) && selectedCmd.Contains("unlock"))
            {
                method = selectedCmd;
            }

            return await _service.UnlockBootloaderAsync(method);
        }

        /// <summary>
        /// Execute lock operation
        /// </summary>
        public async Task<bool> LockBootloaderAsync()
        {
            if (!await EnsureConnectedAsync()) return false;

            string method = "flashing lock";
            
            string selectedCmd = GetSelectedCommand();
            if (!string.IsNullOrEmpty(selectedCmd) && selectedCmd.Contains("lock"))
            {
                method = selectedCmd;
            }

            return await _service.LockBootloaderAsync(method);
        }

        #endregion

        #region A/B Slots

        /// <summary>
        /// Switch A/B slot
        /// </summary>
        public async Task<bool> SwitchSlotAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            
            bool success = await _service.SwitchSlotAsync();
            
            if (success)
            {
                await ReadPartitionTableAsync();
            }

            return success;
        }

        #endregion

        #region Quick Commands

        /// <summary>
        /// Execute selected quick command
        /// </summary>
        public async Task<bool> ExecuteSelectedCommandAsync()
        {
            if (!await EnsureConnectedAsync()) return false;

            string command = GetSelectedCommand();
            if (string.IsNullOrEmpty(command))
            {
                Log("Please select a command to execute", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                Log($"Executing command: {command}", Color.Blue);
                var result = await _service.ExecuteCommandAsync(command, _cts.Token);
                
                if (!string.IsNullOrEmpty(result))
                {
                    // Display command execution result
                    Log($"Result: {result}", Color.Green);
                    return true;
                }
                else
                {
                    Log("Command executed successfully (no return value)", Color.Gray);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Command execution failed: {ex.Message}", Color.Red);
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
                string cmd = _commandComboBox.SelectedItem?.ToString() ?? _commandComboBox.Text;
                
                if (string.IsNullOrEmpty(cmd)) return null;
                
                // Automatically remove "fastboot " prefix
                if (cmd.StartsWith("fastboot ", StringComparison.OrdinalIgnoreCase))
                {
                    cmd = cmd.Substring(9).Trim();
                }
                
                return cmd;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a quick command is selected
        /// </summary>
        public bool HasSelectedCommand()
        {
            string cmd = GetSelectedCommand();
            return !string.IsNullOrWhiteSpace(cmd);
        }

        #endregion

        #region Helper Methods

        private bool EnsureConnected()
        {
            if (_service == null || !_service.IsConnected)
            {
                // Check for available devices, prompt user to connect
                if (_cachedDevices != null && _cachedDevices.Count > 0)
                {
                    Log("Please click the \"Connect\" button to connect to the Fastboot device first", Color.Red);
                }
                else
                {
                    Log("No Fastboot device detected, please ensure the device is in Fastboot mode", Color.Red);
                }
                return false;
            }
            return true;
        }
        
        /// <summary>
        /// Ensure device is connected (async version, supports auto-connect)
        /// </summary>
        private async Task<bool> EnsureConnectedAsync()
        {
            if (_service != null && _service.IsConnected)
                return true;
            
            // Auto-connect attempt
            string selectedDevice = GetSelectedDevice();
            if (!string.IsNullOrEmpty(selectedDevice))
            {
                Log("Auto-connecting to Fastboot device...", Color.Blue);
                return await ConnectAsync();
            }
            
            // Check for available devices
            if (_cachedDevices != null && _cachedDevices.Count > 0)
            {
                Log("Please select and connect to a Fastboot device first", Color.Red);
            }
            else
            {
                Log("No Fastboot device detected, please ensure the device is in Fastboot mode", Color.Red);
            }
            return false;
        }

        /// <summary>
        /// Start operation timer
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
        /// Stop operation timer
        /// </summary>
        private void StopOperationTimer()
        {
            _operationStopwatch.Stop();
            UpdateTimeLabel();
        }

        /// <summary>
        /// Update time label
        /// </summary>
        private void UpdateTimeLabel()
        {
            if (_timeLabel == null) return;

            var elapsed = _operationStopwatch.Elapsed;
            string timeText = elapsed.Hours > 0
                ? $"Time: {elapsed:hh\\:mm\\:ss}"
                : $"Time: {elapsed:mm\\:ss}";
            
            UpdateLabelSafe(_timeLabel, timeText);
        }

        /// <summary>
        /// Update speed label
        /// </summary>
        private void UpdateSpeedLabel()
        {
            if (_speedLabel == null) return;

            string speedText;
            if (_currentSpeed >= 1024 * 1024)
                speedText = $"Speed: {_currentSpeed / (1024 * 1024):F1} MB/s";
            else if (_currentSpeed >= 1024)
                speedText = $"Speed: {_currentSpeed / 1024:F1} KB/s";
            else
                speedText = $"Speed: {_currentSpeed:F0} B/s";
            
            UpdateLabelSafe(_speedLabel, speedText);
        }
        
        /// <summary>
        /// Update speed label (using formatted speed string)
        /// </summary>
        private void UpdateSpeedLabel(string formattedSpeed)
        {
            if (_speedLabel == null) return;
            UpdateLabelSafe(_speedLabel, $"Speed: {formattedSpeed}");
        }
        
        /// <summary>
        /// Flash progress callback
        /// </summary>
        private void OnFlashProgressChanged(FlashProgress progress)
        {
            if (progress == null) return;
            
            // Calculate progress value
            double subProgress = progress.Percent;
            double mainProgress = _flashTotalPartitions > 0 
                ? (_flashCurrentPartitionIndex * 100.0 + progress.Percent) / _flashTotalPartitions 
                : 0;
            
            // Time interval check
            var now = DateTime.Now;
            bool timeElapsed = (now - _lastProgressUpdate).TotalMilliseconds >= ProgressUpdateIntervalMs;
            bool forceUpdate = progress.Percent >= 95;
            
            // Update anyway (to ensure smoothness)
            if (!forceUpdate && !timeElapsed)
                return;
            
            _lastProgressUpdate = now;
            
            // Update sub progress bar (current partition progress)
            _lastSubProgressValue = subProgress;
            UpdateSubProgressBar(subProgress);
            
            // Update total progress bar (for multi-partition flashing)
            if (_flashTotalPartitions > 0)
            {
                _lastMainProgressValue = mainProgress;
                UpdateProgressBar(mainProgress);
            }
            
            // Update speed display
            if (progress.SpeedKBps > 0)
            {
                UpdateSpeedLabel(progress.SpeedFormatted);
            }
            
            // Update time in real-time
            UpdateTimeLabel();
        }
        
        /// <summary>
        /// Format speed display
        /// </summary>
        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "Calculating...";
            
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
        /// Update progress bar (percentage)
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
        /// Update sub progress bar
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
        /// Progress update with speed calculation (for file transfer)
        /// </summary>
        private void UpdateProgressWithSpeed(long current, long total)
        {
            // Calculate progress            if (total > 0)
            {
                double percent = 100.0 * current / total;
                UpdateSubProgressBar(percent);
            }

            // Calculate speed
            long bytesDelta = current - _lastBytes;
            double timeDelta = (DateTime.Now - _lastSpeedUpdate).TotalSeconds;
            
            if (timeDelta >= 0.2 && bytesDelta > 0) // Update every 200ms
            {
                double instantSpeed = bytesDelta / timeDelta;
                // Exponential moving average for smooth speed
                _currentSpeed = (_currentSpeed > 0) ? (_currentSpeed * 0.6 + instantSpeed * 0.4) : instantSpeed;
                _lastBytes = current;
                _lastSpeedUpdate = DateTime.Now;
                
                // Update speed and time display
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

        private bool IsSwitchSlotEnabled()
        {
            try
            {
                return _switchSlotCheckbox?.Checked ?? false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsEraseGoogleLockEnabled()
        {
            try
            {
                return _eraseGoogleLockCheckbox?.Checked ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Execute additional operations after flashing (switch slot, erase Google Lock, etc.)
        /// </summary>
        private async Task ExecutePostFlashOperationsAsync()
        {
            // Switch to Slot A
            if (IsSwitchSlotEnabled())
            {
                Log("Switching to Slot A...", Color.Blue);
                bool success = await _service.SetActiveSlotAsync("a", _cts?.Token ?? CancellationToken.None);
                Log(success ? "Successfully switched to Slot A" : "Failed to switch slot", success ? Color.Green : Color.Red);
            }

            // Erase Google Lock (FRP)
            if (IsEraseGoogleLockEnabled())
            {
                Log("Erasing Google Lock (FRP)...", Color.Blue);
                // Try to erase frp partition
                bool success = await _service.ErasePartitionAsync("frp", _cts?.Token ?? CancellationToken.None);
                if (!success)
                {
                    // Some devices use config or persistent partitions
                    success = await _service.ErasePartitionAsync("config", _cts?.Token ?? CancellationToken.None);
                }
                Log(success ? "Google Lock erased" : "Failed to erase Google Lock (partitions might not exist)", success ? Color.Green : Color.Orange);
            }
        }

        /// <summary>
        /// Cancel current operation
        /// </summary>
        public void CancelOperation()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            StopOperationTimer();
            UpdateLabelSafe(_operationLabel, "Operation: Cancelled");
        }

        /// <summary>
        /// Safely reset CancellationTokenSource (release old instance and create new one)
        /// </summary>
        private void ResetCancellationToken()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } 
                catch (Exception ex) { Debug.WriteLine($"[Fastboot] Cancel token exception: {ex.Message}"); }
                try { _cts.Dispose(); } 
                catch (Exception ex) { Debug.WriteLine($"[Fastboot] Dispose token exception: {ex.Message}"); }
            }
            _cts = new CancellationTokenSource();
        }

        #endregion

        #region Bat Script Parsing

        // Store parsed flash tasks
        private List<BatScriptParser.FlashTask> _flashTasks;
        
        /// <summary>
        /// Get currently loaded flash tasks
        /// </summary>
        public List<BatScriptParser.FlashTask> FlashTasks => _flashTasks;

        /// <summary>
        /// Load bat/sh flash script
        /// </summary>
        public bool LoadFlashScript(string scriptPath)
        {
            if (!File.Exists(scriptPath))
            {
                Log($"Script file does not exist: {scriptPath}", Color.Red);
                return false;
            }

            try
            {
                Log($"Parsing script: {Path.GetFileName(scriptPath)}...", Color.Blue);

                string baseDir = Path.GetDirectoryName(scriptPath);
                var parser = new BatScriptParser(baseDir, msg => _logDetail(msg));

                _flashTasks = parser.ParseScript(scriptPath);

                if (_flashTasks.Count == 0)
                {
                    Log("No valid flash commands found in script", Color.Orange);
                    return false;
                }

                // Statistics
                int flashCount = _flashTasks.Count(t => t.Operation == "flash");
                int eraseCount = _flashTasks.Count(t => t.Operation == "erase");
                int existCount = _flashTasks.Count(t => t.ImageExists);
                long totalSize = _flashTasks.Where(t => t.ImageExists).Sum(t => t.FileSize);

                Log($"Parsing complete: {flashCount} flashes, {eraseCount} erases", Color.Green);
                Log($"Image files: {existCount} exist, total size {FormatSize(totalSize)}", Color.Blue);

                // Update partition list display
                UpdatePartitionListFromScript();

                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to parse script: {ex.Message}", Color.Red);
                return false;
            }
        }

        /// <summary>
        /// Update partition list from script tasks
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
                    // Set different display according to operation type
                    string operationText = task.Operation;
                    string sizeText = "-";
                    string filePathText = "";

                    if (task.Operation == "flash")
                    {
                        operationText = task.ImageExists ? "Write" : "Write (Missing)";
                        sizeText = task.FileSizeFormatted;
                        filePathText = task.ImagePath;
                    }
                    else if (task.Operation == "erase")
                    {
                        operationText = "Erase";
                    }
                    else if (task.Operation == "set_active")
                    {
                        operationText = "Activate Slot";
                    }
                    else if (task.Operation == "reboot")
                    {
                        operationText = "Reboot";
                    }

                    var item = new ListViewItem(new string[]
                    {
                        task.PartitionName,
                        operationText,
                        sizeText,
                        filePathText
                    });

                    item.Tag = task;

                    // Set color according to status
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

                    // Check all flash and erase operations by default
                    if ((task.Operation == "flash" && task.ImageExists) || task.Operation == "erase")
                    {
                        item.Checked = true;
                    }

                    _partitionListView.Items.Add(item);
                }
            }
            catch { }
        }

        /// <summary>
        /// Execute loaded flash script
        /// </summary>
        /// <param name="keepData">Whether to keep data (skip userdata flash)</param>
        /// <param name="lockBl">Whether to lock BL after flashing</param>
        public async Task<bool> ExecuteFlashScriptAsync(bool keepData = false, bool lockBl = false)
        {
            if (_flashTasks == null || _flashTasks.Count == 0)
            {
                Log("Please load a flash script first", Color.Orange);
                return false;
            }

            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            // Get selected tasks
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
                Log("Please select flash tasks to execute", Color.Orange);
                return false;
            }

            // Filter tasks based on options
            if (keepData)
            {
                // Keep data: skip userdata related partitions
                int beforeCount = selectedTasks.Count;
                selectedTasks = selectedTasks.Where(t => 
                    !t.PartitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase) &&
                    !t.PartitionName.Equals("userdata_ab", StringComparison.OrdinalIgnoreCase) &&
                    !t.PartitionName.Equals("metadata", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                if (selectedTasks.Count < beforeCount)
                {
                    Log("Keep Data mode: skipping userdata/metadata partitions", Color.Blue);
                }
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Execute Flash Script");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Preparing...");

                int total = selectedTasks.Count;
                int success = 0;
                int failed = 0;

                Log($"Starting to execute {total} flash tasks...", Color.Blue);
                if (keepData) Log("Mode: Keep Data", Color.Blue);
                if (lockBl) Log("Mode: Lock BL after flashing", Color.Blue);

                for (int i = 0; i < total; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var task = selectedTasks[i];
                    // Total progress: based on task count
                    UpdateProgressBar((i * 100.0) / total);
                    // Sub-progress: current task started
                    UpdateSubProgressBar(0);
                    UpdateLabelSafe(_operationLabel, $"Operation: {task.Operation} {task.PartitionName} ({i + 1}/{total})");

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
                                Log($"Skipping {task.PartitionName}: image file does not exist", Color.Orange);
                            }
                            break;

                        case "erase":
                            // Skip userdata erase in Keep Data mode
                            if (keepData && (task.PartitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase) ||
                                             task.PartitionName.Equals("metadata", StringComparison.OrdinalIgnoreCase)))
                            {
                                Log($"Skipping erase of {task.PartitionName} (Keep Data)", Color.Gray);
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
                            // Reboot operation executed last
                            if (i == total - 1)
                            {
                                // If need to lock BL, execute before reboot
                                if (lockBl)
                                {
                                    Log("Locking Bootloader...", Color.Blue);
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
                                Log("Skipping intermediate reboot command", Color.Gray);
                                taskSuccess = true;
                            }
                            break;
                    }

                    // Sub-progress: current task complete
                    UpdateSubProgressBar(100);
                    
                    if (taskSuccess)
                        success++;
                    else
                        failed++;
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                // Execute post-flash additional operations (switch slot, erase Google lock, etc.)
                if (success > 0)
                {
                    await ExecutePostFlashOperationsAsync();
                }

                // If no reboot command but BL locking is needed, execute here
                bool hasReboot = selectedTasks.Any(t => t.Operation == "reboot");
                if (lockBl && !hasReboot)
                {
                    Log("Locking Bootloader...", Color.Blue);
                    await _service.LockBootloaderAsync("flashing lock", _cts.Token);
                }

                Log($"Flashing complete: {success} successful, {failed} failed", 
                    failed == 0 ? Color.Green : Color.Orange);

                return failed == 0;
            }
            catch (OperationCanceledException)
            {
                Log("Flashing operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Flashing failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Scan flash scripts in directory
        /// </summary>
        public List<string> ScanFlashScripts(string directory)
        {
            return BatScriptParser.FindFlashScripts(directory);
        }

        /// <summary>
        /// Format file size
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

        #region Payload Parsing
        /// <summary>
        /// Load remote Payload from URL (cloud parsing)
        /// </summary>
        public async Task<bool> LoadPayloadFromUrlAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Log("Please enter a URL", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Parse Cloud Payload");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "Operation: Parsing Cloud Payload");
                UpdateLabelSafe(_speedLabel, "Speed: Connecting...");

                Log($"Parsing cloud Payload...", Color.Blue);

                // Create or reuse RemotePayloadService
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
                        // Update speed display
                        if (e.SpeedBytesPerSecond > 0)
                        {
                            UpdateSpeedLabel(e.SpeedFormatted);
                        }
                    };
                }

                // Get real URL first (handle redirects)
                UpdateProgressBar(10);
                var (realUrl, expiresTime) = await _remotePayloadService.GetRedirectUrlAsync(url, _cts.Token);
                
                if (string.IsNullOrEmpty(realUrl))
                {
                    Log("Unable to get download link", Color.Red);
                    UpdateProgressBar(0);
                    return false;
                }

                if (realUrl != url)
                {
                    Log("Real download link retrieved", Color.Green);
                    if (expiresTime.HasValue)
                    {
                        Log($"Link expiration time: {expiresTime.Value:yyyy-MM-dd HH:mm:ss}", Color.Blue);
                    }
                }

                UpdateProgressBar(30);
                bool success = await _remotePayloadService.LoadFromUrlAsync(realUrl, _cts.Token);

                if (success)
                {
                    UpdateProgressBar(70);

                    var summary = _remotePayloadService.GetSummary();
                    Log($"Cloud Payload parsed successfully: {summary.PartitionCount} partitions", Color.Green);
                    Log($"File size: {summary.TotalSizeFormatted}", Color.Blue);

                    // Update partition list display
                    UpdatePartitionListFromRemotePayload();

                    UpdateProgressBar(100);
                }
                else
                {
                    Log("Cloud Payload parsing failed", Color.Red);
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"Cloud Payload load failed: {ex.Message}", Color.Red);
                _logDetail($"Cloud Payload load error: {ex}");
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Update partition list from remote Payload
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
                        "Cloud Extraction",  // Operation column
                        partition.SizeFormatted,
                        $"{partition.Operations.Count} ops"  // Op count
                    });

                    item.Tag = partition;
                    item.Checked = true;  // Check by default

                    // Mark common partitions
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
        /// Extract selected partitions from cloud
        /// </summary>
        public async Task<bool> ExtractSelectedRemotePartitionsAsync(string outputDir)
        {
            if (_remotePayloadService == null || !_remotePayloadService.IsLoaded)
            {
                Log("Please load cloud Payload first", Color.Orange);
                return false;
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                Log("Please specify an output directory", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            // Get selected partition names
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
                Log("Please select partitions to extract", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Cloud Extract Partition");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Preparing...");

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                Log($"Starting to extract {selectedNames.Count} partitions from cloud to: {outputDir}", Color.Blue);

                int success = 0;
                int total = selectedNames.Count;
                int currentIndex = 0;

                // Register progress event handler
                EventHandler<RemoteExtractProgress> progressHandler = (s, e) =>
                {
                    // Sub-progress bar: current partition extraction progress
                    UpdateSubProgressBar(e.Percent);
                    // Update speed display
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

                        UpdateLabelSafe(_operationLabel, $"Operation: Cloud Extracting {name} ({i + 1}/{total})");
                        // Total progress: based on completed partitions
                        UpdateProgressBar((i * 100.0) / total);
                        // Sub-progress: start extraction
                        UpdateSubProgressBar(0);

                        if (await _remotePayloadService.ExtractPartitionAsync(name, outputPath, _cts.Token))
                        {
                            success++;
                            Log($"Extraction successful: {name}.img", Color.Green);
                        }
                        else
                        {
                            Log($"Extraction failed: {name}", Color.Red);
                        }
                        
                        // Sub-progress: current partition extraction complete
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

                Log($"Cloud extraction complete: {success}/{total} successful", success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("Extraction operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Extraction failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Load Payload file (supports .bin and .zip)
        /// </summary>
        public async Task<bool> LoadPayloadAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Log("Please select a Payload file", Color.Orange);
                return false;
            }

            if (!File.Exists(filePath))
            {
                Log($"File does not exist: {filePath}", Color.Red);
                return false;
            }

            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Parse Payload");
                UpdateProgressBar(0);
                UpdateLabelSafe(_operationLabel, "Operation: Parsing Payload");
                UpdateLabelSafe(_speedLabel, "Speed: Parsing...");

                Log($"Loading Payload: {Path.GetFileName(filePath)}...", Color.Blue);

                // Create or reuse PayloadService
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
                    Log($"Payload parsed successfully: {summary.PartitionCount} partitions", Color.Green);
                    Log($"Total size: {summary.TotalSizeFormatted}, Compressed: {summary.TotalCompressedSizeFormatted}", Color.Blue);

                    // Update partition list display
                    UpdatePartitionListFromPayload();

                    // Try to parse firmware info from ZIP or same directory
                    await ParseFirmwareInfoFromPayloadAsync(filePath);

                    UpdateProgressBar(100);
                    PayloadLoaded?.Invoke(this, summary);
                }
                else
                {
                    Log("Payload parsing failed", Color.Red);
                    UpdateProgressBar(0);
                }

                StopOperationTimer();
                return success;
            }
            catch (Exception ex)
            {
                Log($"Payload load failed: {ex.Message}", Color.Red);
                _logDetail($"Payload load error: {ex}");
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Update partition list from Payload
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
                        "Extract",  // Operation column
                        partition.SizeFormatted,
                        partition.CompressedSizeFormatted  // Compressed size
                    });

                    item.Tag = partition;
                    item.Checked = true;  // Check by default

                    // Mark common partitions
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
        /// Extract selected Payload partitions
        /// </summary>
        public async Task<bool> ExtractSelectedPayloadPartitionsAsync(string outputDir)
        {
            if (_payloadService == null || !_payloadService.IsLoaded)
            {
                Log("Please load Payload file first", Color.Orange);
                return false;
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                Log("Please specify an output directory", Color.Orange);
                return false;
            }

            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            // Get selected partition names
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
                Log("Please select partitions to extract", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Extract Payload Partitions");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Preparing...");

                Log($"Starting to extract {selectedNames.Count} partitions to: {outputDir}", Color.Blue);

                int success = 0;
                int total = selectedNames.Count;

                for (int i = 0; i < total; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    string name = selectedNames[i];
                    string outputPath = Path.Combine(outputDir, $"{name}.img");

                    UpdateLabelSafe(_operationLabel, $"Operation: Extracting {name} ({i + 1}/{total})");
                    // Total progress: based on completed partitions
                    UpdateProgressBar((i * 100.0) / total);
                    // Sub-progress: start extraction
                    UpdateSubProgressBar(0);

                    if (await _payloadService.ExtractPartitionAsync(name, outputPath, _cts.Token))
                    {
                        success++;
                        Log($"Extraction successful: {name}.img", Color.Green);
                    }
                    else
                    {
                        Log($"Extraction failed: {name}", Color.Red);
                    }
                    
                    // Sub-progress: current partition extraction complete
                    UpdateSubProgressBar(100);
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"Extraction complete: {success}/{total} successful", success == total ? Color.Green : Color.Orange);

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("Extraction operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Extraction failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Extract Payload partition and flash directly to device
        /// </summary>
        public async Task<bool> FlashFromPayloadAsync()
        {
            if (_payloadService == null || !_payloadService.IsLoaded)
            {
                Log("Please load Payload file first", Color.Orange);
                return false;
            }

            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            // Get selected partitions
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
                Log("Please select partitions to flash", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Payload Flash");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Preparing...");

                Log($"Starting to flash {selectedPartitions.Count} partitions from Payload...", Color.Blue);

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

                        UpdateLabelSafe(_operationLabel, $"Operation: Extracting+Flashing {partition.Name} ({i + 1}/{total})");
                        // Total progress: based on completed partitions
                        UpdateProgressBar((i * 100.0) / total);
                        // Sub-progress: start extraction
                        UpdateSubProgressBar(0);

                        // 1. Extract partition
                        Log($"Extracting {partition.Name}...", Color.Blue);
                        // Sub-progress: extraction phase (0-50%)
                        UpdateSubProgressBar(10);
                        if (!await _payloadService.ExtractPartitionAsync(partition.Name, tempPath, _cts.Token))
                        {
                            Log($"Failed to extract {partition.Name}, skipping flash", Color.Red);
                            continue;
                        }
                        
                        // Sub-progress: extraction complete (50%)
                        UpdateSubProgressBar(50);

                        // 2. Flash partition
                        Log($"Flashing {partition.Name}...", Color.Blue);
                        var flashStart = DateTime.Now;
                        var fileSize = new FileInfo(tempPath).Length;
                        
                        if (await _service.FlashPartitionAsync(partition.Name, tempPath, false, _cts.Token))
                        {
                            success++;
                            Log($"Flash successful: {partition.Name}", Color.Green);
                            
                            // Calculate and display speed
                            var elapsed = (DateTime.Now - flashStart).TotalSeconds;
                            if (elapsed > 0)
                            {
                                double speed = fileSize / elapsed;
                                UpdateSpeedLabel(FormatSpeed(speed));
                            }
                        }
                        else
                        {
                            Log($"Flash failed: {partition.Name}", Color.Red);
                        }
                        
                        // Sub-progress: flash complete (100%)
                        UpdateSubProgressBar(100);

                        // 3. Delete temporary file
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                finally
                {
                    // Clean up temporary directory
                    try { Directory.Delete(tempDir, true); } catch { }
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log($"Payload flash complete: {success}/{total} successful", success == total ? Color.Green : Color.Orange);

                // Execute post-flash operations (switch slots, erase Google lock, etc.)
                if (success > 0)
                {
                    await ExecutePostFlashOperationsAsync();
                }

                // Auto reboot
                if (IsAutoRebootEnabled() && success > 0)
                {
                    await _service.RebootAsync(_cts.Token);
                }

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("Flash operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Flash failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Extract and flash partitions directly from cloud Payload to device
        /// </summary>
        public async Task<bool> FlashFromRemotePayloadAsync()
        {
            if (_remotePayloadService == null || !_remotePayloadService.IsLoaded)
            {
                Log("Please parse cloud Payload first", Color.Orange);
                return false;
            }

            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!await EnsureConnectedAsync()) return false;

            // Get selected partitions
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
                Log("Please select partitions to flash", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                StartOperationTimer("Cloud Payload Flash");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                UpdateLabelSafe(_speedLabel, "Speed: Preparing...");

                Log($"Starting to flash {selectedPartitions.Count} partitions from cloud...", Color.Blue);

                int success = 0;
                int total = selectedPartitions.Count;

                // Register stream flash progress event
                EventHandler<RemotePayloadService.StreamFlashProgressEventArgs> progressHandler = (s, e) =>
                {
                    // Total progress: based on completed partitions + current partition progress
                    double overallPercent = ((success * 100.0) + e.Percent) / total;
                    UpdateProgressBar(overallPercent);
                    // Sub-progress: current partition operation progress
                    UpdateSubProgressBar(e.Percent);
                    
                    // Display different speed according to phase
                    if (e.Phase == RemotePayloadService.StreamFlashPhase.Downloading)
                    {
                        UpdateSpeedLabel($"{e.DownloadSpeedFormatted} (Download)");
                    }
                    else if (e.Phase == RemotePayloadService.StreamFlashPhase.Flashing)
                    {
                        UpdateSpeedLabel($"{e.FlashSpeedFormatted} (Flash)");
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
                        
                        UpdateLabelSafe(_operationLabel, $"Operation: Downloading+Flashing {partition.Name} ({i + 1}/{total})");

                        // Use stream flash
                        bool flashResult = await _remotePayloadService.ExtractAndFlashPartitionAsync(
                            partition.Name,
                            async (tempPath) =>
                            {
                                // Flash callback - measurement of Fastboot communication speed
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
                            Log($"Flash successful: {partition.Name}", Color.Green);
                        }
                        else
                        {
                            Log($"Flash failed: {partition.Name}", Color.Red);
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
                    Log($"✓ All {total} partitions flashed successfully", Color.Green);
                }
                else
                {
                    Log($"Flash complete: {success}/{total} successful", success > 0 ? Color.Orange : Color.Red);
                }

                return success == total;
            }
            catch (OperationCanceledException)
            {
                Log("Flash operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Flash failed: {ex.Message}", Color.Red);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");
            }
        }

        /// <summary>
        /// Close Payload
        /// </summary>
        public void ClosePayload()
        {
            _payloadService?.Close();
            Log("Payload closed", Color.Gray);
        }

        #endregion

        #region OnePlus/OPPO Flash Process

        /// <summary>
        /// Flash configuration options
        /// </summary>
        public class OnePlusFlashOptions
        {
            /// <summary>Whether to enable AB dual-slot flash mode (flash both A/B slots simultaneously)</summary>
            public bool ABFlashMode { get; set; } = false;
            
            /// <summary>Whether to enable Power Flash mode (extra processing for Super partition)</summary>
            public bool PowerFlashMode { get; set; } = false;
            
            /// <summary>Whether to enable Pure FBD mode (flash everything under FastbootD)</summary>
            public bool PureFBDMode { get; set; } = false;
            
            /// <summary>Whether to clear user data</summary>
            public bool ClearData { get; set; } = false;
            
            /// <summary>Whether to erase FRP (Google Lock)</summary>
            public bool EraseFrp { get; set; } = true;
            
            /// <summary>Whether to auto reboot</summary>
            public bool AutoReboot { get; set; } = false;
            
            /// <summary>Target slot (used in AB mode, 'a' or 'b')</summary>
            public string TargetSlot { get; set; } = "a";
        }

        /// <summary>
        /// Flash partition info
        /// </summary>
        public class OnePlusFlashPartition
        {
            public string PartitionName { get; set; }
            public string FilePath { get; set; }
            public long FileSize { get; set; }
            public bool IsLogical { get; set; }
            public bool IsModem { get; set; }
            
            /// <summary>Whether it comes from Payload.bin (extract needed first)</summary>
            public bool IsPayloadPartition { get; set; }
            /// <summary>Payload partition info (for extraction)</summary>
            public PayloadPartition PayloadInfo { get; set; }
            /// <summary>Remote Payload partition info</summary>
            public RemotePayloadPartition RemotePayloadInfo { get; set; }
            
            public string FileSizeFormatted
            {
                get
                {
                    if (FileSize >= 1024L * 1024 * 1024)
                        return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
                    if (FileSize >= 1024 * 1024)
                        return $"{FileSize / (1024.0 * 1024):F2} MB";
                    if (FileSize >= 1024)
                        return $"{FileSize / 1024.0:F2} KB";
                    return $"{FileSize} B";
                }
            }
        }

        /// <summary>
        /// Execute OnePlus/OPPO flash process
        /// </summary>
        public async Task<bool> ExecuteOnePlusFlashAsync(
            List<OnePlusFlashPartition> partitions,
            OnePlusFlashOptions options,
            CancellationToken ct = default)
        {
            if (partitions == null || partitions.Count == 0)
            {
                Log("Error: No partitions selected for flashing", Color.Red);
                return false;
            }

            if (IsBusy)
            {
                Log("Operation in progress", Color.Orange);
                return false;
            }

            if (!await EnsureConnectedAsync())
                return false;

            // Temporary extraction directory (for Payload partition extraction)
            string extractDir = null;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
                ct = linkedCts.Token;

                StartOperationTimer("OnePlus Flash");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);

                // Calculate total flash bytes
                long totalFlashBytes = partitions.Sum(p => p.FileSize);
                long currentFlashedBytes = 0;
                string totalSizeStr = FormatSize(totalFlashBytes);

                Log($"Starting OnePlus flash process, total {partitions.Count} partitions...", Color.Blue);

                // Step 1: Detect device status
                Log("Detecting device connection status...", Color.Blue);
                UpdateLabelSafe(_operationLabel, "Operation: Detecting Device");

                bool isFastbootd = await _service.IsFastbootdModeAsync(ct);
                Log($"Device mode: {(isFastbootd ? "FastbootD" : "Fastboot")}", Color.Green);

                // Step 2: If not in FastbootD mode, need to switch
                if (!isFastbootd)
                {
                    Log("Rebooting to FastbootD mode...", Color.Blue);
                    UpdateLabelSafe(_operationLabel, "Operation: Rebooting to FastbootD");

                    if (!await _service.RebootFastbootdAsync(ct))
                    {
                        Log("Unable to reboot to FastbootD mode", Color.Red);
                        return false;
                    }

                    // Wait for device to reconnect
                    Log("Waiting for device to reconnect...", Color.Blue);
                    bool reconnected = await WaitForDeviceReconnectAsync(60, ct);
                    if (!reconnected)
                    {
                        Log("Device failed to reconnect within 60 seconds", Color.Red);
                        return false;
                    }
                                        Log("FastbootD device connected", Color.Green);
                }

                // Step 3: Delete COW snapshot partitions
                Log("Parsing COW snapshot partitions...", Color.Blue);
                UpdateLabelSafe(_operationLabel, "Operation: Cleaning COW Partitions");
                await _service.DeleteCowPartitionsAsync(ct);
                Log("COW partition cleaning complete", Color.Green);

                // Step 4: Get current slot
                string currentSlot = await _service.GetCurrentSlotAsync(ct);
                if (string.IsNullOrEmpty(currentSlot)) currentSlot = "a";
                Log($"Current slot: {currentSlot.ToUpper()}", Color.Blue);

                // Step 5: AB flash mode preprocessing
                if (options.ABFlashMode)
                {
                    Log($"AB Dual-Slot Mode: Target slot {options.TargetSlot.ToUpper()}", Color.Blue);

                    // If current slot differs from target, need to switch and rebuild partitions
                    if (!string.Equals(currentSlot, options.TargetSlot, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Switching slot to {options.TargetSlot.ToUpper()}...", Color.Blue);
                        await _service.SetActiveSlotAsync(options.TargetSlot, ct);

                        Log("Rebuilding logical partition structure...", Color.Blue);
                        await _service.RebuildLogicalPartitionsAsync(options.TargetSlot, ct);
                    }
                }

                await Task.Delay(2000, ct);  // Wait for device status to stabilize

                // Step 6: Sort partitions (by size ascending)
                var sortedPartitions = partitions
                    .Where(p => !string.IsNullOrEmpty(p.FilePath) && File.Exists(p.FilePath))
                    .OrderBy(p => p.FileSize)
                    .ToList();

                // Pure FBD mode: all partitions flashed under FastbootD
                // Standard Oplus mode: Modem partitions flashed under Fastboot, others under FastbootD
                List<OnePlusFlashPartition> fbdPartitions;
                List<OnePlusFlashPartition> modemPartitions;

                if (options.PureFBDMode)
                {
                    // Pure FBD mode: all partitions flashed under FastbootD
                    fbdPartitions = sortedPartitions;
                    modemPartitions = new List<OnePlusFlashPartition>();
                    Log("Pure FBD mode: all partitions flashed under FastbootD", Color.Blue);
                }
                else
                {
                    // Standard Oplus mode: isolate Modem partitions
                    fbdPartitions = sortedPartitions.Where(p => !p.IsModem).ToList();
                    modemPartitions = sortedPartitions.Where(p => p.IsModem).ToList();
                    if (modemPartitions.Count > 0)
                    {
                        Log($"Oplus mode: {fbdPartitions.Count} partitions in FastbootD, {modemPartitions.Count} Modem partitions in Fastboot", Color.Blue);
                    }
                }

                int totalPartitions = options.ABFlashMode 
                    ? fbdPartitions.Sum(p => p.IsLogical ? 1 : 2) + modemPartitions.Count * 2
                    : sortedPartitions.Count;
                int currentPartitionIndex = 0;

                // Step 6.5: Check and extract Payload partitions
                var payloadPartitions = sortedPartitions.Where(p => p.IsPayloadPartition).ToList();
                if (payloadPartitions.Count > 0)
                {
                    Log($"Detected {payloadPartitions.Count} Payload partitions, extracting...", Color.Blue);
                    UpdateLabelSafe(_operationLabel, "Operation: Extracting Payload Partitions");

                    // Create temporary extraction directory
                    extractDir = Path.Combine(Path.GetTempPath(), $"payload_extract_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(extractDir);

                    foreach (var pp in payloadPartitions)
                    {
                        ct.ThrowIfCancellationRequested();

                        string extractedPath = Path.Combine(extractDir, $"{pp.PartitionName}.img");

                        if (pp.PayloadInfo != null && _payloadService != null)
                        {
                            // Local Payload extraction (by partition name)
                            Log($"  Extracting {pp.PartitionName}...", null);
                            bool extracted = await _payloadService.ExtractPartitionAsync(
                                pp.PartitionName, extractedPath, ct);

                            if (extracted && File.Exists(extractedPath))
                            {
                                pp.FilePath = extractedPath;
                                Log($"  ✓ {pp.PartitionName} extraction complete", Color.Green);
                            }
                            else
                            {
                                Log($"  ✗ {pp.PartitionName} extraction failed", Color.Red);
                            }
                        }
                        else if (pp.RemotePayloadInfo != null && _remotePayloadService != null)
                        {
                            // Remote Payload extraction (by partition name)
                            Log($"  Downloading and extracting {pp.PartitionName}...", null);
                            bool extracted = await _remotePayloadService.ExtractPartitionAsync(
                                pp.PartitionName, extractedPath, ct);

                            if (extracted && File.Exists(extractedPath))
                            {
                                pp.FilePath = extractedPath;
                                Log($"  ✓ {pp.PartitionName} download and extraction complete", Color.Green);
                            }
                            else
                            {
                                Log($"  ✗ {pp.PartitionName} download or extraction failed", Color.Red);
                            }
                        }
                    }

                    // Update file size (actual size after extraction)
                    foreach (var pp in payloadPartitions)
                    {
                        if (!string.IsNullOrEmpty(pp.FilePath) && File.Exists(pp.FilePath))
                        {
                            pp.FileSize = new FileInfo(pp.FilePath).Length;
                        }
                    }

                    // Recalculate total bytes
                    totalFlashBytes = sortedPartitions
                        .Where(p => !string.IsNullOrEmpty(p.FilePath) && File.Exists(p.FilePath))
                        .Sum(p => p.FileSize);
                    totalSizeStr = FormatSize(totalFlashBytes);
                }

                Log($"Starting to flash {sortedPartitions.Count} partitions (Total size: {totalSizeStr})...", Color.Blue);

                // Step 7: Flash partitions in FastbootD mode
                foreach (var partition in fbdPartitions)
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip partitions without files
                    if (string.IsNullOrEmpty(partition.FilePath) || !File.Exists(partition.FilePath))
                    {
                        Log($"  ⚠ {partition.PartitionName} No valid file, skipping", Color.Orange);
                        continue;
                    }

                    string fileName = Path.GetFileName(partition.FilePath);
                    string targetSlot = options.ABFlashMode ? options.TargetSlot : currentSlot;

                    if (options.ABFlashMode && !partition.IsLogical)
                    {
                        // Non-logical partitions need to be flashed to both slots in AB mode
                        foreach (var slot in new[] { "a", "b" })
                        {
                            string targetName = $"{partition.PartitionName}_{slot}";
                            UpdateLabelSafe(_operationLabel, $"Operation: Flashing {targetName}");
                            Log($"[Writing Image] {fileName} -> {targetName}", null);

                            long bytesBeforeThis = currentFlashedBytes;
                            Action<long, long> progressCallback = (sent, total) =>
                            {
                                long globalBytes = bytesBeforeThis + sent;
                                double percent = totalFlashBytes > 0 ? globalBytes * 100.0 / totalFlashBytes : 0;
                                UpdateProgressBar(percent);
                                UpdateSubProgressBar(total > 0 ? sent * 100.0 / total : 0);
                                UpdateSpeedLabel(FormatSpeed(_currentSpeed));
                            };

                            bool ok = await _service.FlashPartitionToSlotAsync(
                                partition.PartitionName, partition.FilePath, slot, progressCallback, ct);

                            if (ok)
                            {
                                Log($"  ✓ {targetName} successful", Color.Green);
                            }
                            else
                            {
                                Log($"  ✗ {targetName} failed", Color.Red);
                            }

                            currentFlashedBytes += partition.FileSize / 2;  // Each slot counts as half in AB mode
                            currentPartitionIndex++;
                        }
                    }
                    else
                    {
                        // Logical partitions or normal mode flash only one slot
                        string targetName = $"{partition.PartitionName}_{targetSlot}";
                        UpdateLabelSafe(_operationLabel, $"Operation: Flashing {targetName}");
                        Log($"[Writing Image] {fileName} -> {targetName}", null);

                        long bytesBeforeThis = currentFlashedBytes;
                        Action<long, long> progressCallback = (sent, total) =>
                        {
                            long globalBytes = bytesBeforeThis + sent;
                            double percent = totalFlashBytes > 0 ? globalBytes * 100.0 / totalFlashBytes : 0;
                            UpdateProgressBar(percent);
                            UpdateSubProgressBar(total > 0 ? sent * 100.0 / total : 0);
                        };

                        bool ok = await _service.FlashPartitionToSlotAsync(
                            partition.PartitionName, partition.FilePath, targetSlot, progressCallback, ct);

                        if (ok)
                        {
                            Log($"  ✓ {targetName} successful", Color.Green);
                        }
                        else
                        {
                            Log($"  ✗ {targetName} failed", Color.Red);
                        }

                        currentFlashedBytes += partition.FileSize;
                        currentPartitionIndex++;
                    }
                }

                // Step 8: If there are Modem partitions (Oplus mode), reboot to Bootloader to flash
                if (modemPartitions.Count > 0 && !options.PureFBDMode)
                {
                    Log("Modem partitions need to be flashed in Fastboot mode...", Color.Blue);
                    UpdateLabelSafe(_operationLabel, "Operation: Rebooting to Fastboot");

                    if (!await _service.RebootBootloaderAsync(ct))
                    {
                        Log("Unable to reboot to Fastboot mode", Color.Red);
                    }
                    else
                    {
                        // Wait for device to reconnect
                        bool reconnected = await WaitForDeviceReconnectAsync(60, ct);
                        if (reconnected)
                        {
                            foreach (var modem in modemPartitions)
                            {
                                ct.ThrowIfCancellationRequested();

                                // Skip partitions without files
                                if (string.IsNullOrEmpty(modem.FilePath) || !File.Exists(modem.FilePath))
                                {
                                    Log($"  ⚠ {modem.PartitionName} No valid file, skipping", Color.Orange);
                                    continue;
                                }

                                string fileName = Path.GetFileName(modem.FilePath);

                                // Modem partitions also get flashed to both slots in AB mode
                                foreach (var slot in options.ABFlashMode ? new[] { "a", "b" } : new[] { currentSlot })
                                {
                                    string targetName = $"{modem.PartitionName}_{slot}";
                                    UpdateLabelSafe(_operationLabel, $"Operation: Flashing {targetName}");
                                    Log($"[Writing Image] {fileName} -> {targetName}", null);

                                    bool ok = await _service.FlashPartitionToSlotAsync(
                                        modem.PartitionName, modem.FilePath, slot, null, ct);

                                    Log(ok ? $"  ✓ {targetName} successful" : $"  ✗ {targetName} failed",
                                        ok ? Color.Green : Color.Red);
                                }
                            }

                            // If wiping data or erasing FRP is needed after flashing Modem, must return to FastbootD
                            if (options.ClearData || options.EraseFrp)
                            {
                                Log("Rebooting to FastbootD to continue subsequent operations...", Color.Blue);
                                await _service.RebootFastbootdAsync(ct);
                                await WaitForDeviceReconnectAsync(60, ct);
                            }
                        }
                    }
                }

                // Step 9: Erase FRP (Google Lock) - can be automatically executed for all devices
                if (options.EraseFrp)
                {
                    Log("Erasing FRP...", Color.Blue);
                    UpdateLabelSafe(_operationLabel, "Operation: Erasing FRP");
                    bool frpOk = await _service.EraseFrpAsync(ct);
                    Log(frpOk ? "FRP erase successful" : "FRP erase failed", frpOk ? Color.Green : Color.Orange);
                }

                // Step 10: Wipe Data - automatically executed for Qualcomm devices only, MediaTek requires manual action
                if (options.ClearData)
                {
                    // Detect device platform: Qualcomm (abl) vs MediaTek (lk)
                    var devicePlatform = await _service.GetDevicePlatformAsync(ct);
                    bool isQualcommDevice = devicePlatform == FastbootService.DevicePlatform.Qualcomm;
                    
                    if (isQualcommDevice)
                    {
                        Log("Wiping user data...", Color.Blue);
                        UpdateLabelSafe(_operationLabel, "Operation: Wiping Data");
                        bool wipeOk = await _service.WipeDataAsync(ct);
                        Log(wipeOk ? "Data wipe successful" : "Data wipe failed", wipeOk ? Color.Green : Color.Orange);
                    }
                    else
                    {
                        // MediaTek devices (lk) require manual data wipe in Recovery
                        Log("⚠ MediaTek devices, please wipe data manually (Enter Recovery -> Wipe data/factory reset)", Color.Orange);
                    }
                }

                // Step 11: Auto Reboot
                if (options.AutoReboot)
                {
                    Log("Rebooting device...", Color.Blue);
                    UpdateLabelSafe(_operationLabel, "Operation: Rebooting");
                    await _service.RebootAsync(ct);
                }

                UpdateProgressBar(100);
                UpdateSubProgressBar(100);
                StopOperationTimer();

                Log("✓ OnePlus flash process complete", Color.Green);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log("Flash operation cancelled", Color.Orange);
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            catch (Exception ex)
            {
                Log($"Error occurred during flash: {ex.Message}", Color.Red);
                _logDetail($"OnePlus flash exception: {ex}");
                UpdateProgressBar(0);
                UpdateSubProgressBar(0);
                StopOperationTimer();
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLabelSafe(_operationLabel, "Operation: Idle");

                // Clean up temporary extraction directory
                if (!string.IsNullOrEmpty(extractDir) && Directory.Exists(extractDir))
                {
                    try
                    {
                        Directory.Delete(extractDir, true);
                        _logDetail($"Cleaned up temporary directory: {extractDir}");
                    }
                    catch (Exception ex)
                    {
                        _logDetail($"Failed to clean up temporary directory: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Wait for device to reconnect
        /// </summary>
        private async Task<bool> WaitForDeviceReconnectAsync(int timeoutSeconds, CancellationToken ct)
        {
            int attempts = timeoutSeconds / 5;
            for (int i = 0; i < attempts; i++)
            {
                await Task.Delay(5000, ct);
                
                // Refresh device list
                await RefreshDeviceListAsync();
                
                if (_cachedDevices != null && _cachedDevices.Count > 0)
                {
                    // Attempt to automatically connect to the first device
                    var device = _cachedDevices[0];
                    _service = new FastbootService(
                        msg => Log(msg, null),
                        (current, total) => UpdateProgressWithSpeed(current, total),
                        _logDetail
                    );
                    _service.FlashProgressChanged += OnFlashProgressChanged;
                    
                    if (await _service.SelectDeviceAsync(device.Serial, ct))
                    {
                        return true;
                    }
                }
                
                Log($"Waiting for device... ({(i + 1) * 5}/{timeoutSeconds}s)", null);
            }
            return false;
        }

        /// <summary>
        /// Build OnePlus flash partition list from currently selected partitions
        /// Supports: Payload partitions, unpacked folders, script tasks, normal images
        /// </summary>
        public List<OnePlusFlashPartition> BuildOnePlusFlashPartitions()
        {
            var result = new List<OnePlusFlashPartition>();

            if (_partitionListView == null) return result;

            try
            {
                foreach (ListViewItem item in _partitionListView.CheckedItems)
                {
                    string partName = item.SubItems[0].Text;
                    string filePath = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";

                    // Local Payload partition (requires extraction first)
                    if (item.Tag is PayloadPartition payloadPart)
                    {
                        result.Add(new OnePlusFlashPartition
                        {
                            PartitionName = payloadPart.Name,
                            FilePath = null,  // Set later during extraction
                            FileSize = (long)payloadPart.Size,  // Size after decompression
                            IsLogical = FastbootService.IsLogicalPartition(payloadPart.Name),
                            IsModem = FastbootService.IsModemPartition(payloadPart.Name),
                            IsPayloadPartition = true,
                            PayloadInfo = payloadPart
                        });
                        continue;
                    }

                    // Remote Payload partition (cloud stream flash)
                    if (item.Tag is RemotePayloadPartition remotePart)
                    {
                        result.Add(new OnePlusFlashPartition
                        {
                            PartitionName = remotePart.Name,
                            FilePath = null,
                            FileSize = (long)remotePart.Size,  // Size after decompression
                            IsLogical = FastbootService.IsLogicalPartition(remotePart.Name),
                            IsModem = FastbootService.IsModemPartition(remotePart.Name),
                            IsPayloadPartition = true,
                            RemotePayloadInfo = remotePart
                        });
                        continue;
                    }

                    // Images in extracted folders
                    if (item.Tag is ExtractedImageInfo extractedInfo)
                    {
                        result.Add(new OnePlusFlashPartition
                        {
                            PartitionName = extractedInfo.PartitionName,
                            FilePath = extractedInfo.FilePath,
                            FileSize = extractedInfo.FileSize,
                            IsLogical = extractedInfo.IsLogical,
                            IsModem = extractedInfo.IsModem,
                            IsPayloadPartition = false
                        });
                        continue;
                    }

                    // Script tasks (flash_all.bat parsing)
                    if (item.Tag is BatScriptParser.FlashTask task)
                    {
                        if (task.Operation == "flash" && task.ImageExists)
                        {
                            result.Add(new OnePlusFlashPartition
                            {
                                PartitionName = task.PartitionName,
                                FilePath = task.ImagePath,
                                FileSize = task.FileSize,
                                IsLogical = FastbootService.IsLogicalPartition(task.PartitionName),
                                IsModem = FastbootService.IsModemPartition(task.PartitionName),
                                IsPayloadPartition = false
                            });
                        }
                        continue;
                    }

                    // Normal partitions (existing image files)
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        result.Add(new OnePlusFlashPartition
                        {
                            PartitionName = partName,
                            FilePath = filePath,
                            FileSize = fileInfo.Length,
                            IsLogical = FastbootService.IsLogicalPartition(partName),
                            IsModem = FastbootService.IsModemPartition(partName),
                            IsPayloadPartition = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logDetail($"Failed to build flash partition list: {ex.Message}");
            }

            return result;
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
