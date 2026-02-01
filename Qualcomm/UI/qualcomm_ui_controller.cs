// ============================================================================
// LoveAlways - Qualcomm UI Controller
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private readonly Action<string> _logDetail;  // Detailed debug log (written to file only)
        private bool _disposed;

        // UI Control References - Use dynamic or reflection to handle different types of controls
        private dynamic _portComboBox;
        private ListView _partitionListView;
        private dynamic _progressBar;        // Total Progress Bar (Long)
        private dynamic _subProgressBar;     // Sub Progress Bar (Short)
        private dynamic _statusLabel;
        private dynamic _skipSaharaCheckbox;
        private dynamic _protectPartitionsCheckbox;
        private dynamic _programmerPathTextbox;
        private dynamic _outputPathTextbox;
        
        // Time/Speed/Operation Status Labels
        private dynamic _timeLabel;
        private dynamic _speedLabel;
        private dynamic _operationLabel;
        
        // Device Info Labels
        private dynamic _brandLabel;         // Brand
        private dynamic _chipLabel;          // Chip
        private dynamic _modelLabel;         // Device Model
        private dynamic _serialLabel;        // Serial Number
        private dynamic _storageLabel;       // Storage Type
        private dynamic _unlockLabel;        // Device Model 2 (Second Model Label)
        private dynamic _otaVersionLabel;    // OTA Version
        
        // Timer and Speed Calculation
        private Stopwatch _operationStopwatch;
        private long _lastBytes;
        private DateTime _lastSpeedUpdate;
        private double _currentSpeed; // Current Speed (bytes/s)
        
        // Port Status Monitor Timer
        private System.Windows.Forms.Timer _portMonitorTimer;
        private string _connectedPortName; // Currently Connected Port Name
        
        // Total Progress Tracking
        private int _totalSteps;
        private int _currentStep;
        private long _totalOperationBytes;    // Total bytes of the current total task
        private long _completedStepBytes;     // Total bytes of completed steps
        private long _currentStepBytes;       // Bytes of the current step (for accurate speed calculation)
        private string _currentOperationName; // Current operation name saved

        /// <summary>
        /// Quick check connection status (without triggering port validation to avoid accidental disconnection)
        /// </summary>
        public bool IsConnected { get { return _service != null && _service.IsConnectedFast; } }
        
        /// <summary>
        /// Check if quick reconnect is possible (port released but Firehose still available)
        /// </summary>
        public bool CanQuickReconnect { get { return _service != null && _service.IsPortReleased && _service.State == QualcommConnectionState.Ready; } }
        
        public bool IsBusy { get; private set; }
        public List<PartitionInfo> Partitions { get; private set; }

        /// <summary>
        /// Get current slot ("a", "b", "undefined", "nonexistent")
        /// </summary>
        public string GetCurrentSlot()
        {
            if (_service == null) return "nonexistent";
            return _service.CurrentSlot ?? "nonexistent";
        }

        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<List<PartitionInfo>> PartitionsLoaded;
        
        /// <summary>
        /// Xiaomi Auth Token Event (Triggered when built-in signature fails, requires popup to show token)
        /// Token Format: Base64 string starting with VQ
        /// </summary>
        public event Action<string> XiaomiAuthTokenRequired;
        
        /// <summary>
        /// Xiaomi Auth Token Event Handler (Triggered when built-in signature fails)
        /// </summary>
        private void OnXiaomiAuthTokenRequired(string token)
        {
            Log("[Xiaomi Auth] Built-in signature invalid, online authorization required", Color.Orange);
            Log(string.Format("[Xiaomi Auth] Token: {0}", token), Color.Cyan);
            
            // Trigger public event to let Form1 show popup
            XiaomiAuthTokenRequired?.Invoke(token);
        }
        
        /// <summary>
        /// Port Disconnected Event Handler (Triggered when device disconnects itself)
        /// </summary>
        private void OnServicePortDisconnected(object sender, EventArgs e)
        {
            // Ensure execution on UI thread
            if (_partitionListView != null && _partitionListView.InvokeRequired)
            {
                _partitionListView.BeginInvoke(new Action(() => OnServicePortDisconnected(sender, e)));
                return;
            }
            
            // Stop port monitoring
            StopPortMonitor();
            
            Log("Device disconnected, full reconfiguration required", Color.Red);
            
            // Cancel ongoing operation
            CancelOperation();
            
            // Disconnect service and release resources
            if (_service != null)
            {
                try
                {
                    _service.PortDisconnected -= OnServicePortDisconnected;
                    _service.Disconnect();
                    _service.Dispose();
                }
                catch (Exception ex) 
                { 
                    _logDetail?.Invoke($"[UI] Disconnect service exception: {ex.Message}"); 
                }
                _service = null;
            }
            
            // Clear partition list
            Partitions?.Clear();
            if (_partitionListView != null)
            {
                _partitionListView.BeginUpdate();
                _partitionListView.Items.Clear();
                _partitionListView.EndUpdate();
            }
            
            // Reset progress bar
            UpdateProgressBarDirect(_progressBar, 0);
            UpdateProgressBarDirect(_subProgressBar, 0);
            
            // Automatically uncheck "Skip Loader", full reconfiguration required after device disconnection
            SetSkipSaharaChecked(false);
            
            // Update UI status
            ConnectionStateChanged?.Invoke(this, false);
            ClearDeviceInfoLabels();
            
            // Refresh port list, wait for device reconnection
            RefreshPorts();
            
            Log("Please wait for the device to re-enter EDL mode before reconnecting", Color.Orange);
        }
        
        /// <summary>
        /// Validate connection status (Call before operation)
        /// </summary>
        public bool ValidateConnection()
        {
            if (_service == null)
            {
                Log("Device not connected", Color.Red);
                return false;
            }
            
            if (!_service.ValidateConnection())
            {
                Log("Device connection lost, full reconfiguration required", Color.Red);
                // Uncheck "Skip Loader", full reconfiguration required
                SetSkipSaharaChecked(false);
                ConnectionStateChanged?.Invoke(this, false);
                ClearDeviceInfoLabels();
                RefreshPorts();
                return false;
            }
            
            return true;
        }

        public QualcommUIController(Action<string, Color?> log = null, Action<string> logDetail = null)
        {
            _log = log ?? delegate { };
            _logDetail = logDetail ?? delegate { };
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
            // Device Info Labels
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
            
            // Bind Device Info Labels
            _brandLabel = brandLabel;
            _chipLabel = chipLabel;
            _modelLabel = modelLabel;
            _serialLabel = serialLabel;
            _storageLabel = storageLabel;
            _unlockLabel = unlockLabel;
            _otaVersionLabel = otaVersionLabel;
            
            // Initialize port status monitor timer (Check every 2 seconds)
            _portMonitorTimer = new System.Windows.Forms.Timer();
            _portMonitorTimer.Interval = 2000;
            _portMonitorTimer.Tick += OnPortMonitorTick;
        }
        
        /// <summary>
        /// Port Status Monitor Timer Callback
        /// </summary>
        private void OnPortMonitorTick(object sender, EventArgs e)
        {
            // If not connected, no need to check
            if (string.IsNullOrEmpty(_connectedPortName) || _service == null)
                return;
            
            // Check if port still exists in Device Manager
            var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
            bool portExists = Array.Exists(availablePorts, p => 
                p.Equals(_connectedPortName, StringComparison.OrdinalIgnoreCase));
            
            if (!portExists)
            {
                // Port disappeared from Device Manager - Show in main log
                Log(string.Format("Detected port {0} disconnected", _connectedPortName), Color.Orange);
                
                // Stop timer
                _portMonitorTimer.Stop();
                
                // Trigger disconnect handling
                OnServicePortDisconnected(this, EventArgs.Empty);
            }
        }
        
        /// <summary>
        /// Start Port Monitoring
        /// </summary>
        private void StartPortMonitor(string portName)
        {
            _connectedPortName = portName;
            if (_portMonitorTimer != null && !_portMonitorTimer.Enabled)
            {
                _portMonitorTimer.Start();
                _logDetail(string.Format("[Port Monitor] Start monitoring port: {0}", portName));
            }
        }
        
        /// <summary>
        /// Stop Port Monitoring
        /// </summary>
        private void StopPortMonitor()
        {
            if (_portMonitorTimer != null && _portMonitorTimer.Enabled)
            {
                _portMonitorTimer.Stop();
                _logDetail("[Port Monitor] Stop monitoring");
            }
            _connectedPortName = null;
        }

        /// <summary>
        /// Refresh Port List
        /// </summary>
        /// <param name="silent">Silent mode, no log output</param>
        /// <returns>Number of detected EDL ports</returns>
        public int RefreshPorts(bool silent = false)
        {
            if (_portComboBox == null) return 0;

            try
            {
                var ports = PortDetector.DetectAllPorts();
                var edlPorts = PortDetector.DetectEdlPorts();
                
                // Save currently selected port name
                string previousSelectedPort = GetSelectedPortName();
                
                _portComboBox.Items.Clear();

                if (ports.Count == 0)
                {
                    // Show default text when no device
                    _portComboBox.Text = "Device Status: No device connected";
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

                    // Simple selection logic:
                    // 1. Prioritize restoring previous selection (if exists)
                    // 2. Otherwise select the first EDL port
                    // 3. Otherwise select the first port
                    
                    int selectedIndex = -1;
                    
                    // Try to restore previous selection
                    if (!string.IsNullOrEmpty(previousSelectedPort))
                    {
                        for (int i = 0; i < _portComboBox.Items.Count; i++)
                        {
                            if (_portComboBox.Items[i].ToString().Contains(previousSelectedPort))
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                    }
                    
                    // If no previous selection or selected port no longer exists, select EDL port
                    if (selectedIndex < 0 && edlPorts.Count > 0)
                    {
                        for (int i = 0; i < _portComboBox.Items.Count; i++)
                        {
                            if (_portComboBox.Items[i].ToString().Contains(edlPorts[0].PortName))
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                    }
                    
                    // Default select the first one
                    if (selectedIndex < 0 && _portComboBox.Items.Count > 0)
                    {
                        selectedIndex = 0;
                    }
                    
                    // Set selection
                    if (selectedIndex >= 0)
                    {
                        _portComboBox.SelectedIndex = selectedIndex;
                    }
                }

                return edlPorts.Count;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Log(string.Format("Failed to refresh ports: {0}", ex.Message), Color.Red);
                }
                return 0;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            return await ConnectWithOptionsAsync("", "ufs", IsSkipSaharaEnabled(), "none");
        }
        
        /// <summary>
        /// Get Sahara Device Info Only (For cloud auto-matching)
        /// </summary>
        /// <returns>Device info object, returns null if failed</returns>
        public async Task<LoveAlways.Qualcomm.Services.SaharaDeviceInfo> GetSaharaDeviceInfoAsync()
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return null; }

            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName)) { Log("Please select a port", Color.Red); return null; }

            try
            {
                IsBusy = true;
                ResetCancellationToken();

                _service = new QualcommService(
                    msg => Log(msg, null),
                    null,
                    _logDetail
                );

                var chipInfo = await _service.GetSaharaDeviceInfoOnlyAsync(portName, _cts.Token);
                
                if (chipInfo == null)
                {
                    Log("Unable to get device info", Color.Red);
                    return null;
                }

                // Convert to SaharaDeviceInfo
                return new LoveAlways.Qualcomm.Services.SaharaDeviceInfo
                {
                    MsmId = chipInfo.MsmId.ToString("X8"),
                    PkHash = chipInfo.PkHash ?? "",
                    OemId = "0x" + chipInfo.OemId.ToString("X4"),
                    HwId = chipInfo.HwIdHex ?? "",
                    Serial = chipInfo.SerialHex ?? "",
                    IsUfs = true
                };
            }
            catch (Exception ex)
            {
                Log("Exception getting device info: " + ex.Message, Color.Red);
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        /// <summary>
        /// Continue connection with cloud-matched Loader
        /// </summary>
        public async Task<bool> ContinueConnectWithCloudLoaderAsync(byte[] loaderData, string storageType, string authMode)
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (_service == null) { Log("Please get device info first", Color.Red); return false; }

            try
            {
                IsBusy = true;

                bool success = await _service.ContinueConnectWithLoaderAsync(loaderData, storageType, authMode, _cts.Token);

                if (success)
                {
                    string portName = GetSelectedPortName();
                    Log("Connected successfully!", Color.Green);
                    UpdateDeviceInfoLabels();
                    
                    _service.PortDisconnected += OnServicePortDisconnected;
                    _service.XiaomiAuthTokenRequired += OnXiaomiAuthTokenRequired;
                    StartPortMonitor(portName);
                    SetSkipSaharaChecked(true);
                    
                    ConnectionStateChanged?.Invoke(this, true);
                }
                else
                {
                    Log("Connection failed", Color.Red);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log("Connection exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> ConnectWithOptionsAsync(string programmerPath, string storageType, bool skipSahara, string authMode, string digestPath = "", string signaturePath = "")
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName)) { Log("Please select a port", Color.Red); return false; }

            if (!skipSahara && string.IsNullOrEmpty(programmerPath))
            {
                Log("Please select a programmer file", Color.Red);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                // Start progress bar - Connection process has 4 stages: Sahara(40%) -> Firehose Config(20%) -> Auth(20%) -> Complete(20%)
                StartOperationTimer("Connect Device", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);

                _service = new QualcommService(
                    msg => Log(msg, null),
                    (current, total) => {
                        // Sahara stage progress mapped to 0-40%
                        if (total > 0)
                        {
                            double percent = 40.0 * current / total;
                            UpdateProgressBarDirect(_progressBar, percent);
                            UpdateProgressBarDirect(_subProgressBar, 100.0 * current / total);
                        }
                    },
                    _logDetail  // Pass detailed debug log delegate
                );

                bool success;
                if (skipSahara)
                {
                    UpdateProgressBarDirect(_progressBar, 40); // Skip Sahara
                    success = await _service.ConnectFirehoseDirectAsync(portName, storageType, _cts.Token);
                    UpdateProgressBarDirect(_progressBar, 60);
                }
                else
                {
                    Log(string.Format("Connecting device (Storage: {0}, Auth: {1})...", storageType, authMode), Color.Blue);
                    // Pass auth mode and file path to ConnectAsync, auth is executed internally in correct order
                    success = await _service.ConnectAsync(portName, programmerPath, storageType, 
                        authMode, digestPath, signaturePath, _cts.Token);
                    UpdateProgressBarDirect(_progressBar, 80); // Sahara + Auth + Firehose Config complete
                    
                    if (success)
                        SetSkipSaharaChecked(true);
                }

                if (success)
                {
                    Log("Connected successfully!", Color.Green);
                    UpdateProgressBarDirect(_progressBar, 100);
                    UpdateProgressBarDirect(_subProgressBar, 100);
                    UpdateDeviceInfoLabels();
                    
                    // Register port disconnected event (Triggered when device disconnects itself)
                    _service.PortDisconnected += OnServicePortDisconnected;
                    
                    // Register Xiaomi Auth Token event
                    _service.XiaomiAuthTokenRequired += OnXiaomiAuthTokenRequired;
                    
                    // Start port monitor (Check port status in Device Manager)
                    StartPortMonitor(portName);
                    
                    ConnectionStateChanged?.Invoke(this, true);
                }
                else
                {
                    Log("Connection failed", Color.Red);
                    UpdateProgressBarDirect(_progressBar, 0);
                    UpdateProgressBarDirect(_subProgressBar, 0);
                }

                return success;
            }
            catch (Exception ex)
            {
                Log("Connection exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Connect device using full VIP data (Loader + Digest + Signature file paths)
        /// </summary>
        /// <param name="storageType">Storage Type (ufs/emmc)</param>
        /// <param name="platform">Platform Name (e.g. SM8550)</param>
        /// <param name="loaderData">Loader (Firehose) Data</param>
        /// <param name="digestPath">Digest File Path</param>
        /// <param name="signaturePath">Signature File Path</param>
        public async Task<bool> ConnectWithVipDataAsync(string storageType, string platform, byte[] loaderData, string digestPath, string signaturePath)
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName)) { Log("Please select a port", Color.Red); return false; }

            if (loaderData == null)
            {
                Log("Loader data invalid", Color.Red);
                return false;
            }

            // Check auth files
            bool hasAuth = !string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath) &&
                          File.Exists(digestPath) && File.Exists(signaturePath);

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                // Start progress bar
                StartOperationTimer("VIP Connect", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);

                Log(string.Format("[VIP] Platform: {0}", platform), Color.Blue);
                Log(string.Format("[VIP] Loader: {0} KB (Resource Pack)", loaderData.Length / 1024), Color.Gray);
                
                if (hasAuth)
                {
                    var digestInfo = new FileInfo(digestPath);
                    var sigInfo = new FileInfo(signaturePath);
                    Log(string.Format("[VIP] Digest: {0} KB (Resource Pack)", digestInfo.Length / 1024), Color.Gray);
                    Log(string.Format("[VIP] Signature: {0} Bytes (Resource Pack)", sigInfo.Length), Color.Gray);
                }
                else
                {
                    Log("[VIP] No auth files, using normal mode", Color.Orange);
                }

                _service = new QualcommService(
                    msg => Log(msg, null),
                    (current, total) => {
                        if (total > 0)
                        {
                            // Sahara stage progress mapped to 0-40%
                            double percent = 40.0 * current / total;
                            UpdateProgressBarDirect(_progressBar, percent);
                            UpdateProgressBarDirect(_subProgressBar, 100.0 * current / total);
                        }
                    },
                    _logDetail
                );

                // One step: Upload Loader + VIP Auth + Firehose Config
                // Important: VIP Auth must be executed before Firehose Config
                UpdateProgressBarDirect(_progressBar, 5);
                Log("[VIP] Connecting (Loader + Auth + Config)...", Color.Blue);
                
                // Perform VIP Auth using file paths
                bool connectOk = await _service.ConnectWithVipAuthAsync(portName, loaderData, digestPath ?? "", signaturePath ?? "", storageType, _cts.Token);
                UpdateProgressBarDirect(_progressBar, 85);

                if (connectOk)
                {
                    Log("[VIP] Connected successfully! High privilege mode activated", Color.Green);
                    UpdateProgressBarDirect(_progressBar, 100);
                    UpdateProgressBarDirect(_subProgressBar, 100);
                    UpdateDeviceInfoLabels();
                    
                    _service.PortDisconnected += OnServicePortDisconnected;
                    _service.XiaomiAuthTokenRequired += OnXiaomiAuthTokenRequired;
                    
                    // Start port monitor (Check port status in Device Manager)
                    StartPortMonitor(portName);
                    
                    ConnectionStateChanged?.Invoke(this, true);
                    
                    // Automatically check Skip Sahara (Can connect directly next time)
                    SetSkipSaharaChecked(true);
                    
                    return true;
                }
                else
                {
                    Log("[VIP] Connection failed, please check if Loader/Signature match", Color.Red);
                    UpdateProgressBarDirect(_progressBar, 0);
                    UpdateProgressBarDirect(_subProgressBar, 0);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("[VIP] Connection exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Connect using embedded Loader data
        /// Suitable for generic EDL Loader, supports optional auth
        /// </summary>
        /// <param name="storageType">Storage Type (ufs/emmc)</param>
        /// <param name="loaderData">Loader Binary Data</param>
        /// <param name="loaderName">Loader Name (for logging)</param>
        /// <param name="authMode">Auth Mode: none, oneplus</param>
        public async Task<bool> ConnectWithLoaderDataAsync(string storageType, byte[] loaderData, string loaderName, string authMode = "none")
        {
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName)) { Log("Please select a port", Color.Red); return false; }

            if (loaderData == null || loaderData.Length < 100)
            {
                Log("Loader data invalid", Color.Red);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                // Start progress bar
                StartOperationTimer("EDL Connect", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);

                string authInfo = authMode != "none" ? $", Auth: {authMode}" : "";
                Log(string.Format("[EDL] Loader: {0} ({1} KB{2})", loaderName, loaderData.Length / 1024, authInfo), Color.Cyan);

                _service = new QualcommService(
                    msg => Log(msg, null),
                    (current, total) => {
                        if (total > 0)
                        {
                            double percent = 70.0 * current / total;
                            UpdateProgressBarDirect(_progressBar, percent);
                            UpdateProgressBarDirect(_subProgressBar, 100.0 * current / total);
                        }
                    },
                    _logDetail
                );

                // Upload Loader via Sahara
                Log("[EDL] Uploading Loader (Sahara)...", Color.Cyan);
                
                bool success = await _service.ConnectWithLoaderDataAsync(portName, loaderData, storageType, _cts.Token);
                
                if (!success)
                {
                    Log("[EDL] Sahara handshake/Loader upload failed", Color.Red);
                    UpdateProgressBarDirect(_progressBar, 0);
                    UpdateProgressBarDirect(_subProgressBar, 0);
                    return false;
                }
                
                UpdateProgressBarDirect(_progressBar, 75);
                Log("[EDL] Loader uploaded successfully, entered Firehose mode", Color.Green);
                
                // Execute brand-specific auth
                string authLower = authMode.ToLowerInvariant();
                
                if (authLower == "oneplus")
                {
                    Log("[EDL] Performing OnePlus Auth...", Color.Cyan);
                    bool authOk = await _service.PerformOnePlusAuthAsync(_cts.Token);
                    UpdateProgressBarDirect(_progressBar, 90);
                    
                    if (authOk)
                    {
                        Log("[EDL] OnePlus Auth success", Color.Green);
                    }
                    else
                    {
                        Log("[EDL] OnePlus Auth failed, some features may be restricted", Color.Orange);
                    }
                }
                else if (authLower == "xiaomi")
                {
                    Log("[EDL] Performing Xiaomi Auth...", Color.Cyan);
                    bool authOk = await _service.PerformXiaomiAuthAsync(_cts.Token);
                    UpdateProgressBarDirect(_progressBar, 90);
                    
                    if (authOk)
                    {
                        Log("[EDL] Xiaomi Auth success", Color.Green);
                    }
                    else
                    {
                        Log("[EDL] Xiaomi Auth failed, some features may be restricted", Color.Orange);
                    }
                }
                else if (authLower == "none" && _service.IsXiaomiDevice())
                {
                    // Xiaomi Device Auto Auth
                    Log("[EDL] Xiaomi device detected, performing auto auth...", Color.Cyan);
                    bool authOk = await _service.PerformXiaomiAuthAsync(_cts.Token);
                    UpdateProgressBarDirect(_progressBar, 90);
                    
                    if (authOk)
                    {
                        Log("[EDL] Xiaomi auto auth success", Color.Green);
                    }
                    else
                    {
                        Log("[EDL] Xiaomi auto auth failed, some features may be restricted", Color.Orange);
                    }
                }
                
                Log("[EDL] Connected successfully!", Color.Green);
                UpdateProgressBarDirect(_progressBar, 100);
                UpdateProgressBarDirect(_subProgressBar, 100);
                UpdateDeviceInfoLabels();
                
                _service.PortDisconnected += OnServicePortDisconnected;
                _service.XiaomiAuthTokenRequired += OnXiaomiAuthTokenRequired;
                
                // Start port monitor (Check port status in Device Manager)
                StartPortMonitor(portName);
                
                ConnectionStateChanged?.Invoke(this, true);
                
                // Automatically check Skip Sahara
                SetSkipSaharaChecked(true);

                return true;
            }
            catch (Exception ex)
            {
                Log("[EDL] Connection exception: " + ex.Message, Color.Red);
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
            Log("Disconnected", Color.Gray);
        }
        
        /// <summary>
        /// Reset stuck Sahara state
        /// Used when device is stuck in Sahara mode due to software or loader errors
        /// </summary>
        public async Task<bool> ResetSaharaAsync()
        {
            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName))
            {
                Log("Please select a port", Color.Red);
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
                
                Log("Resetting Sahara state...", Color.Blue);
                
                // Ensure service exists
                if (_service == null)
                {
                    _service = new QualcommService(
                        msg => Log(msg, null),
                        null,
                        _logDetail
                    );
                }
                
                bool success = await _service.ResetSaharaAsync(portName, _cts.Token);
                
                if (success)
                {
                    Log("------------------------------------------------", Color.Gray);
                    Log("✓ Sahara state reset successfully!", Color.Green);
                    Log("Please click [Connect] button to reconnect device", Color.Blue);
                    Log("------------------------------------------------", Color.Gray);
                    
                    // Uncheck "Skip Loader", full handshake required
                    SetSkipSaharaChecked(false);
                    // Refresh ports
                    RefreshPorts();
                    // Clear device info display
                    ClearDeviceInfoLabels();
                    // Notify connection status change
                    ConnectionStateChanged?.Invoke(this, false);
                }
                else
                {
                    Log("------------------------------------------------", Color.Gray);
                    Log("❌ Unable to reset Sahara state", Color.Red);
                    Log("Please try the following steps:", Color.Orange);
                    Log("  1. Disconnect USB cable", Color.Orange);
                    Log("  2. Power cycle device (remove battery or hold power button)", Color.Orange);
                    Log("  3. Reconnect USB", Color.Orange);
                    Log("------------------------------------------------", Color.Gray);
                }
                
                return success;
            }
            catch (OperationCanceledException)
            {
                Log("Reset canceled", Color.Orange);
                return false;
            }
            catch (Exception ex)
            {
                Log("Reset exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                _cts = null;
            }
        }
        
        /// <summary>
        /// Hard Reset Device (Full Reboot)
        /// </summary>
        public async Task<bool> HardResetDeviceAsync()
        {
            string portName = GetSelectedPortName();
            if (string.IsNullOrEmpty(portName))
            {
                Log("Please select a port", Color.Red);
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
                
                Log("Sending hard reset command...", Color.Blue);
                
                if (_service == null)
                {
                    _service = new QualcommService(
                        msg => Log(msg, null),
                        null,
                        _logDetail
                    );
                }
                
                bool success = await _service.HardResetDeviceAsync(portName, _cts.Token);
                
                if (success)
                {
                    Log("Device is rebooting, please wait for device to re-enter EDL mode", Color.Green);
                    ConnectionStateChanged?.Invoke(this, false);
                    ClearDeviceInfoLabels();
                    SetSkipSaharaChecked(false);
                    
                    // Wait a moment before refreshing ports
                    await Task.Delay(2000);
                    RefreshPorts();
                }
                else
                {
                    Log("Hard reset failed", Color.Red);
                }
                
                return success;
            }
            catch (OperationCanceledException)
            {
                Log("Operation canceled", Color.Orange);
                return false;
            }
            catch (Exception ex)
            {
                Log("Hard reset exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                _cts = null;
            }
        }

        #region Device Info Display

        private DeviceInfoService _deviceInfoService;
        private DeviceFullInfo _currentDeviceInfo;

        /// <summary>
        /// Get current chip info
        /// </summary>
        public QualcommChipInfo ChipInfo
        {
            get { return _service != null ? _service.ChipInfo : null; }
        }

        /// <summary>
        /// Get current full device info
        /// </summary>
        public DeviceFullInfo CurrentDeviceInfo
        {
            get { return _currentDeviceInfo; }
        }

        /// <summary>
        /// Update device info labels (Info obtained from Sahara + Firehose)
        /// </summary>
        public void UpdateDeviceInfoLabels()
        {
            if (_service == null) return;

            // Initialize device info service
            if (_deviceInfoService == null)
            {
                _deviceInfoService = new DeviceInfoService(
                    msg => Log(msg, null),
                    msg => { } // Detailed log optional
                );
            }

            // Get device info from Qualcomm service
            _currentDeviceInfo = _deviceInfoService.GetInfoFromQualcommService(_service);

            var chipInfo = _service.ChipInfo;
            
            // Info obtained from Sahara mode
            if (chipInfo != null)
            {
                // Brand (Identified from PK Hash or OEM ID)
                string brand = _currentDeviceInfo.Vendor;
                if (brand == "Unknown" && !string.IsNullOrEmpty(chipInfo.PkHash))
                {
                    brand = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                    _currentDeviceInfo.Vendor = brand;
                }
                UpdateLabelSafe(_brandLabel, "Brand: " + (brand != "Unknown" ? brand : "Identifying..."));
                
                // Chip Model - Map from Sahara read MSM ID using database
                string chipDisplay = "Identifying...";
                
                // Prioritize using database mapped chip codename
                string chipCodename = QualcommDatabase.GetChipCodename(chipInfo.MsmId);
                if (!string.IsNullOrEmpty(chipCodename))
                {
                    chipDisplay = chipCodename;
                }
                else if (!string.IsNullOrEmpty(chipInfo.ChipName) && chipInfo.ChipName != "Unknown")
                {
                    // Use parsed chip name
                    int parenIndex = chipInfo.ChipName.IndexOf('(');
                    chipDisplay = parenIndex > 0 ? chipInfo.ChipName.Substring(0, parenIndex).Trim() : chipInfo.ChipName;
                }
                else if (chipInfo.MsmId != 0)
                {
                    // Show MSM ID (Easier to add to database)
                    chipDisplay = string.Format("0x{0:X8}", chipInfo.MsmId);
                }
                else if (!string.IsNullOrEmpty(chipInfo.HwIdHex) && chipInfo.HwIdHex.Length >= 4)
                {
                    // Show HWID
                    chipDisplay = chipInfo.HwIdHex.StartsWith("0x") ? chipInfo.HwIdHex : "0x" + chipInfo.HwIdHex;
                }
                
                UpdateLabelSafe(_chipLabel, "Chip: " + chipDisplay);
                
                // Serial Number - Forced lock to Sahara read chip serial
                UpdateLabelSafe(_serialLabel, "Chip Serial: " + (!string.IsNullOrEmpty(chipInfo.SerialHex) ? chipInfo.SerialHex : "Not Obtained"));
                
                // Device Model - Requires reading partition info from Firehose
                UpdateLabelSafe(_modelLabel, "Model: Deep Scan Pending");
            }
            else
            {
                // Sahara did not get chip info, show defaults
                UpdateLabelSafe(_brandLabel, "Brand: Unknown");
                UpdateLabelSafe(_chipLabel, "Chip: Unknown");
                UpdateLabelSafe(_serialLabel, "Chip Serial: Not Obtained");
                UpdateLabelSafe(_modelLabel, "Model: Deep Scan Pending");
            }
            
            // Info obtained from Firehose mode
            string storageType = _service.StorageType ?? "UFS";
            int sectorSize = _service.SectorSize;
            UpdateLabelSafe(_storageLabel, string.Format("Storage: {0} ({1}B)", storageType.ToUpper(), sectorSize));
            
            // Device Model (Deep Scan Pending)
            UpdateLabelSafe(_unlockLabel, "Model: Deep Scan Pending");
            
            // OTA Version
            UpdateLabelSafe(_otaVersionLabel, "Version: Deep Scan Pending");
        }

        /// <summary>
        /// Update more device info after reading partition table
        /// </summary>
        public void UpdateDeviceInfoFromPartitions()
        {
            if (_service == null || Partitions == null || Partitions.Count == 0) return;

            if (_currentDeviceInfo == null)
            {
                _currentDeviceInfo = new DeviceFullInfo();
            }

            // 1. Try reading hardware partitions (devinfo, proinfo)
            Task.Run(async () => {
                // devinfo (Generic/Xiaomi/OPPO)
                var devinfoPart = Partitions.FirstOrDefault(p => p.Name == "devinfo");
                if (devinfoPart != null)
                {
                    byte[] data = await _service.ReadPartitionDataAsync("devinfo", 0, 4096, _cts.Token);
                    if (data != null)
                    {
                        _deviceInfoService.ParseDevInfo(data, _currentDeviceInfo);
                    }
                }

                // proinfo (Lenovo)
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

            // 2. Check A/B partition structure
            bool hasAbSlot = Partitions.Exists(p => p.Name.EndsWith("_a") || p.Name.EndsWith("_b"));
            _currentDeviceInfo.IsAbDevice = hasAbSlot;
            
            // Update basic description
            string storageDesc = string.Format("Storage: {0} ({1})", 
                _service.StorageType.ToUpper(), 
                hasAbSlot ? "A/B Partition" : "Normal Partition");
            UpdateLabelSafe(_storageLabel, storageDesc);

            // If brand info already exists, do not overwrite here
            if (string.IsNullOrEmpty(_currentDeviceInfo.Brand) || _currentDeviceInfo.Brand == "Unknown")
            {
                bool isOplus = Partitions.Exists(p => p.Name.StartsWith("my_") || p.Name.Contains("oplus") || p.Name.Contains("oppo"));
                bool isXiaomi = Partitions.Exists(p => p.Name == "cust" || p.Name == "persist");
                bool isLenovo = Partitions.Exists(p => p.Name.Contains("lenovo") || p.Name == "proinfo" || p.Name == "lenovocust");
                
                if (isOplus) _currentDeviceInfo.Brand = "OPPO/Realme";
                else if (isXiaomi) _currentDeviceInfo.Brand = "Xiaomi/Redmi";
                else if (isLenovo)
                {
                    // Check if it is Legion series
                    bool isLegion = Partitions.Exists(p => p.Name.Contains("legion"));
                    _currentDeviceInfo.Brand = isLegion ? "Lenovo (Legion)" : "Lenovo";
                }
                
                if (!string.IsNullOrEmpty(_currentDeviceInfo.Brand))
                {
                    UpdateLabelSafe(_brandLabel, "Brand: " + _currentDeviceInfo.Brand);
                }
            }
            
            UpdateLabelSafe(_unlockLabel, "Model: Partition Table Read");
        }

        /// <summary>
        /// Print full device info log in professional flash tool format
        /// </summary>
        public void PrintFullDeviceLog()
        {
            if (_service == null || _currentDeviceInfo == null) return;

            var chip = _service.ChipInfo;
            var info = _currentDeviceInfo;

            Log("------------------------------------------------", Color.Gray);
            Log("Read Device Info : Success", Color.Green);

            // 1. Core Identity Info
            string marketName = !string.IsNullOrEmpty(info.MarketName) ? info.MarketName : 
                               (!string.IsNullOrEmpty(info.Brand) && !string.IsNullOrEmpty(info.Model) ? info.Brand + " " + info.Model : "Unknown");
            Log(string.Format("- Market Name : {0}", marketName), Color.Blue);
            
            // Product Name
            if (!string.IsNullOrEmpty(info.MarketNameEn) && info.MarketNameEn != marketName)
                Log(string.Format("- Product Name : {0}", info.MarketNameEn), Color.Blue);
            else if (!string.IsNullOrEmpty(info.DeviceCodename))
                Log(string.Format("- Product Name : {0}", info.DeviceCodename), Color.Blue);
            
            // Model
            if (!string.IsNullOrEmpty(info.Model))
                Log(string.Format("- Device Model : {0}", info.Model), Color.Blue);
            
            // Manufacturer
            if (!string.IsNullOrEmpty(info.Brand))
                Log(string.Format("- Manufacturer : {0}", info.Brand), Color.Blue);
            
            // 2. System Version Info
            if (!string.IsNullOrEmpty(info.AndroidVersion))
                Log(string.Format("- Android Ver : {0}{1}", info.AndroidVersion, 
                    !string.IsNullOrEmpty(info.SdkVersion) ? " [SDK:" + info.SdkVersion + "]" : ""), Color.Blue);
            
            if (!string.IsNullOrEmpty(info.SecurityPatch))
                Log(string.Format("- Security Patch : {0}", info.SecurityPatch), Color.Blue);
            
            // 3. Device/Product Info
            if (!string.IsNullOrEmpty(info.DevProduct))
                Log(string.Format("- Chip Platform : {0}", info.DevProduct), Color.Blue);
            
            if (!string.IsNullOrEmpty(info.Product))
                Log(string.Format("- Product Code : {0}", info.Product), Color.Blue);
            
            // Market Region
            if (!string.IsNullOrEmpty(info.MarketRegion))
                Log(string.Format("- Market Region : {0}", info.MarketRegion), Color.Blue);
            
            // Region Code
            if (!string.IsNullOrEmpty(info.Region))
                Log(string.Format("- Region Code : {0}", info.Region), Color.Blue);
            
            // 4. Build Info
            if (!string.IsNullOrEmpty(info.BuildId))
                Log(string.Format("- Build ID : {0}", info.BuildId), Color.Blue);
            
            if (!string.IsNullOrEmpty(info.DisplayId))
                Log(string.Format("- Display ID : {0}", info.DisplayId), Color.Blue);
            
            if (!string.IsNullOrEmpty(info.BuiltDate))
                Log(string.Format("- Build Date : {0}", info.BuiltDate), Color.Blue);
            
            if (!string.IsNullOrEmpty(info.BuildTimestamp))
                Log(string.Format("- Timestamp : {0}", info.BuildTimestamp), Color.Blue);
            
            // 5. OTA Version Info (Highlighted)
            if (!string.IsNullOrEmpty(info.OtaVersion))
                Log(string.Format("- OTA Version : {0}", info.OtaVersion), Color.Green);
            
            if (!string.IsNullOrEmpty(info.OtaVersionFull) && info.OtaVersionFull != info.OtaVersion)
                Log(string.Format("- Full OTA : {0}", info.OtaVersionFull), Color.Green);
            
            // 6. Full Build Fingerprint
            if (!string.IsNullOrEmpty(info.Fingerprint))
                Log(string.Format("- Build Fingerprint : {0}", info.Fingerprint), Color.Blue);
            
            // 7. Vendor Specific Info (OPLUS)
            if (!string.IsNullOrEmpty(info.OplusProject))
                Log(string.Format("- OPLUS Project : {0}", info.OplusProject), Color.Blue);
            if (!string.IsNullOrEmpty(info.OplusNvId))
                Log(string.Format("- OPLUS NV ID : {0}", info.OplusNvId), Color.Blue);
            
            Log("------------------------------------------------", Color.Gray);
        }

        /// <summary>
        /// Infer device model from partition list
        /// </summary>
        private string GetDeviceModelFromPartitions()
        {
            if (Partitions == null || Partitions.Count == 0) return null;

            // Based on chip info
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

            // Infer device type based on partition names
            bool isOnePlus = Partitions.Exists(p => p.Name.Contains("oem") && p.Name.Contains("op"));
            bool isXiaomi = Partitions.Exists(p => p.Name.Contains("cust") || p.Name == "persist");
            bool isOppo = Partitions.Exists(p => p.Name.Contains("oplus") || p.Name.Contains("my_"));

            if (isOnePlus) return "OnePlus";
            if (isXiaomi) return "Xiaomi";
            if (isOppo) return "OPPO/Realme";
            
            return null;
        }

        /// <summary>
        /// Internal method: Try to read build.prop (No IsBusy check)
        /// Automatically select parsing strategy based on vendor
        /// </summary>
        private async Task TryReadBuildPropInternalAsync()
        {
            // Create total timeout protection (60 seconds)
            using (var totalTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, totalTimeoutCts.Token))
            {
                try
                {
                    await TryReadBuildPropCoreAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (totalTimeoutCts.IsCancellationRequested)
                    {
                        Log("Device info parsing timed out (60s), skipped", Color.Orange);
                    }
                    else
                    {
                        Log("Device info parsing canceled", Color.Orange);
                    }
                }
                catch (Exception ex)
                {
                    Log(string.Format("Device info parsing failed: {0}", ex.Message), Color.Orange);
                }
            }
        }

        /// <summary>
        /// Device info parsing core logic (With cancellation token support)
        /// </summary>
        private async Task TryReadBuildPropCoreAsync(CancellationToken ct)
        {
            try
            {
                // Check if there are partitions available for reading device info
                bool hasSuper = Partitions != null && Partitions.Exists(p => p.Name == "super");
                bool hasVendor = Partitions != null && Partitions.Exists(p => p.Name == "vendor" || p.Name.StartsWith("vendor_"));
                bool hasSystem = Partitions != null && Partitions.Exists(p => p.Name == "system" || p.Name.StartsWith("system_"));
                bool hasMyManifest = Partitions != null && Partitions.Exists(p => p.Name.StartsWith("my_manifest"));
                
                // If no available partitions, return directly
                if (!hasSuper && !hasVendor && !hasSystem && !hasMyManifest)
                {
                    Log("Device has no super/vendor/system partitions, skipping device info read", Color.Orange);
                    return;
                }

                if (_deviceInfoService == null)
                {
                    _deviceInfoService = new DeviceInfoService(
                        msg => Log(msg, null),
                        msg => { }
                    );
                }

                // Create partition read delegate with timeout (Using passed cancellation token)
                // Increase timeout to 30 seconds as reading is slower in VIP mode
                Func<string, long, int, Task<byte[]>> readPartition = async (partName, offset, size) =>
                {
                    // Check cancellation
                    if (ct.IsCancellationRequested) return null;
                    
                    // Check if partition exists
                    if (Partitions == null || !Partitions.Exists(p => p.Name == partName || p.Name.StartsWith(partName + "_")))
                    {
                        return null;
                    }
                    
                    try
                    {
                        // Increase to 30 seconds timeout protection (VIP mode needs more time)
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                        {
                            return await _service.ReadPartitionDataAsync(partName, offset, size, linkedCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // If external cancellation, return null instead of throwing exception
                        if (ct.IsCancellationRequested) return null;
                        // Otherwise it is timeout, silently return null
                        _logDetail(string.Format("Read {0} timed out", partName));
                        return null;
                    }
                    catch (Exception ex)
                    {
                        _logDetail(string.Format("Read {0} exception: {1}", partName, ex.Message));
                        return null;
                    }
                };

                // Get current status
                string activeSlot = _service.CurrentSlot;
                long superStart = 0;
                if (hasSuper)
                {
                    var superPart = Partitions.Find(p => p.Name == "super");
                    if (superPart != null) superStart = (long)superPart.StartSector;
                }
                int sectorSize = _service.SectorSize > 0 ? _service.SectorSize : 512;

                // Auto identify vendor and select corresponding parsing strategy
                string detectedVendor = DetectDeviceVendor();
                Log(string.Format("Detected Device Vendor: {0}", detectedVendor), Color.Blue);
                
                // Update progress: Vendor identification complete (85%)
                UpdateProgressBarDirect(_progressBar, 85);
                UpdateProgressBarDirect(_subProgressBar, 25);

                BuildPropInfo buildProp = null;

                // Use corresponding reading strategy based on vendor
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
                        // Generic strategy - Only try when super partition exists
                        if (hasSuper)
                        {
                            UpdateProgressBarDirect(_subProgressBar, 40);
                            buildProp = await _deviceInfoService.ReadBuildPropFromDevice(
                                readPartition, activeSlot, hasSuper, superStart, sectorSize);
                        }
                        break;
                }

                // Update progress: Parsing complete (95%)
                UpdateProgressBarDirect(_progressBar, 95);
                UpdateProgressBarDirect(_subProgressBar, 80);

                if (buildProp != null)
                {
                    Log("Successfully read device build.prop", Color.Green);
                    ApplyBuildPropInfo(buildProp);
                    
                    // Print full device info log
                    PrintFullDeviceLog();
                }
                else
                {
                    Log("Failed to read device info (Device might not support or partition format incompatible)", Color.Orange);
                }
            }
            catch (OperationCanceledException)
            {
                // Rethrow, handled by outer layer
                throw;
            }
            catch (Exception ex)
            {
                Log(string.Format("Failed to read device info: {0}", ex.Message), Color.Orange);
            }
        }

        /// <summary>
        /// Auto detect device vendor (Combine Sahara chip info + Partition characteristics)
        /// Note: OEM ID and PK Hash might be inaccurate, prioritize partition characteristics
        /// </summary>
        private string DetectDeviceVendor()
        {
            var chipInfo = _service?.ChipInfo;
            string detectedSource = "";
            string detectedVendor = "Unknown";

            // 1. First get from device info (if available)
            if (_currentDeviceInfo != null && !string.IsNullOrEmpty(_currentDeviceInfo.Vendor) && 
                _currentDeviceInfo.Vendor != "Unknown" && !_currentDeviceInfo.Vendor.Contains("Unknown"))
            {
                detectedVendor = NormalizeVendorName(_currentDeviceInfo.Vendor);
                detectedSource = "Device Info";
                _logDetail(string.Format("Vendor Detect [{0}]: {1}", detectedSource, detectedVendor));
                return detectedVendor;
            }

            // 2. Prioritize detection from partition characteristics (Most reliable source, as OEM ID and PK Hash might be inaccurate)
            if (Partitions != null && Partitions.Count > 0)
            {
                // Collect all partition names for debugging
                var partNames = new List<string>();
                foreach (var p in Partitions) partNames.Add(p.Name);
                _logDetail(string.Format("Detected {0} partitions, starting characteristic analysis...", Partitions.Count));

                // Lenovo specific partitions (Prioritize detection)
                bool hasLenovoMarker = Partitions.Exists(p => 
                    p.Name == "proinfo" || 
                    p.Name == "lenovocust" || 
                    p.Name.Contains("lenovo"));
                if (hasLenovoMarker)
                {
                    _logDetail("Detected Lenovo characteristic partition (proinfo/lenovocust)");
                    return "Lenovo";
                }

                // OPLUS series - Strict detection: Must have explicit oplus/oppo marker, or at least 2 OPLUS specific partitions
                bool hasOplusExplicit = Partitions.Exists(p => 
                    p.Name.Contains("oplus") || p.Name.Contains("oppo") || p.Name.Contains("realme"));
                int oplusSpecificCount = 0;
                foreach (var p in Partitions)
                {
                    // OPLUS specific partitions (Xiaomi devices won't have these)
                    if (p.Name == "my_engineering" || p.Name == "my_carrier" || 
                        p.Name == "my_stock" || p.Name == "my_region" || 
                        p.Name == "my_custom" || p.Name == "my_bigball" ||
                        p.Name == "my_preload" || p.Name == "my_company" ||
                        p.Name == "reserve1" || p.Name == "reserve2" ||
                        p.Name.StartsWith("my_engineering") ||
                        p.Name.StartsWith("my_carrier") ||
                        p.Name.StartsWith("my_stock"))
                    {
                        oplusSpecificCount++;
                    }
                }
                // Needs explicit marker or at least 2 specific partitions to determine OPLUS
                if (hasOplusExplicit || oplusSpecificCount >= 2)
                {
                    _logDetail(string.Format("Detected OPLUS characteristics: Explicit market={0}, Specific partition count={1}", hasOplusExplicit, oplusSpecificCount));
                    return "OPLUS";
                }

                // Xiaomi series detection (After OPLUS strict detection)
                bool hasXiaomiMarker = Partitions.Exists(p => 
                    p.Name.Contains("xiaomi") || p.Name.Contains("miui") || p.Name.Contains("redmi"));
                bool hasCust = Partitions.Exists(p => p.Name == "cust");
                bool hasPersist = Partitions.Exists(p => p.Name == "persist");
                bool hasSpunvm = Partitions.Exists(p => p.Name == "spunvm"); // Xiaomi common baseband partition
                
                // Xiaomi characteristics: Has explicit marker, or cust+persist combination (and not Lenovo/OPLUS)
                if (hasXiaomiMarker || (hasCust && hasPersist))
                {
                    _logDetail(string.Format("Detected Xiaomi characteristics: Explicit marker={0}, cust={1}, persist={2}", 
                        hasXiaomiMarker, hasCust, hasPersist));
                    return "Xiaomi";
                }

                // ZTE series (ZTE/nubia/RedMagic)
                if (Partitions.Exists(p => p.Name.Contains("zte") || p.Name.Contains("nubia")))
                {
                    _logDetail("Detected ZTE characteristic partition");
                    return "ZTE";
                }
            }

            // 3. Identify from Chip OEM ID (Obtained in Sahara stage) - As backup
            if (chipInfo != null && chipInfo.OemId > 0)
            {
                string vendorFromOem = QualcommDatabase.GetVendorName(chipInfo.OemId);
                if (!string.IsNullOrEmpty(vendorFromOem) && !vendorFromOem.Contains("Unknown"))
                {
                    detectedVendor = NormalizeVendorName(vendorFromOem);
                    _logDetail(string.Format("Vendor Detect [OEM ID 0x{0:X4}]: {1}", chipInfo.OemId, detectedVendor));
                    return detectedVendor;
                }
            }

            // 4. From Chip Vendor field
            if (chipInfo != null && !string.IsNullOrEmpty(chipInfo.Vendor) && 
                chipInfo.Vendor != "Unknown" && !chipInfo.Vendor.Contains("Unknown"))
            {
                detectedVendor = NormalizeVendorName(chipInfo.Vendor);
                _logDetail(string.Format("Vendor Detect [Chip Vendor]: {0}", detectedVendor));
                return detectedVendor;
            }

            // 5. Identify from PK Hash (Last backup)
            if (chipInfo != null && !string.IsNullOrEmpty(chipInfo.PkHash))
            {
                string vendor = QualcommDatabase.GetVendorByPkHash(chipInfo.PkHash);
                if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown")
                {
                    detectedVendor = NormalizeVendorName(vendor);
                    _logDetail(string.Format("Vendor Detect [PK Hash]: {0}", detectedVendor));
                    return detectedVendor;
                }
            }

            _logDetail("Unable to determine device vendor, using generic strategy");
            return "Unknown";
        }

        /// <summary>
        /// Normalize Vendor Name
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
        /// OPLUS (OPPO/Realme/OnePlus) specific read strategy
        /// Uses DeviceInfoService generic strategy directly, which reads my_manifest in correct order
        /// Order: system -> system_ext -> product -> vendor -> odm -> my_manifest (High priority overrides low priority)
        /// </summary>
        private async Task<BuildPropInfo> ReadOplusBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("Using OPLUS specific parsing strategy...", Color.Blue);
            
            // OPLUS device's my_manifest is EROFS filesystem, not plain text
            // Using DeviceInfoService generic strategy (Correctly parses EROFS and merges properties by priority)
            var result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize, "OnePlus");
            
            if (result != null && !string.IsNullOrEmpty(result.MarketName))
            {
                Log("Successfully read device info from OPLUS partitions", Color.Green);
            }
            else
            {
                Log("OPLUS device info parsing incomplete, some fields might be missing", Color.Orange);
            }

            return result;
        }

        /// <summary>
        /// Xiaomi (Xiaomi/Redmi/POCO) specific read strategy
        /// Priority: vendor > product > system
        /// </summary>
        private async Task<BuildPropInfo> ReadXiaomiBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("Using Xiaomi specific parsing strategy...", Color.Blue);
            
            // Xiaomi devices use standard strategy, but prioritize vendor partition
            var result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize, "Xiaomi");
            
            // Xiaomi specific property enhancement: Detect MIUI/HyperOS version
            if (result != null)
            {
                // Correct brand display
                if (string.IsNullOrEmpty(result.Brand) || result.Brand.ToLower() == "xiaomi")
                {
                    // Determine series from OTA version
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
        /// Lenovo (Lenovo/Motorola) specific read strategy
        /// Priority: lenovocust > proinfo > vendor
        /// </summary>
        private async Task<BuildPropInfo> ReadLenovoBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("Using Lenovo specific parsing strategy...", Color.Blue);

            BuildPropInfo result = null;

            // Lenovo specific partition: lenovocust
            var lenovoCustPart = Partitions?.FirstOrDefault(p => p.Name == "lenovocust");
            if (lenovoCustPart != null)
            {
                try
                {
                    Log("Attempting to read from lenovocust...", Color.Gray);
                    byte[] data = await readPartition("lenovocust", 0, 512 * 1024);
                    if (data != null)
                    {
                        string content = System.Text.Encoding.UTF8.GetString(data);
                        result = _deviceInfoService.ParseBuildProp(content);
                    }
                }
                catch (Exception ex)
                {
                    _logDetail?.Invoke($"Read lenovocust partition exception: {ex.Message}");
                }
            }

            // Lenovo proinfo partition (Contains serial number etc.)
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
                catch (Exception ex)
                {
                    _logDetail?.Invoke($"Read proinfo partition exception: {ex.Message}");
                }
            }

            // Fallback to generic strategy
            if (result == null || string.IsNullOrEmpty(result.MarketName))
            {
                result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize, "Lenovo");
            }

            // Lenovo specific processing: Identify Legion series
            if (result != null)
            {
                string model = result.MarketName ?? result.Model ?? "";
                if (model.Contains("Y700") || model.Contains("Legion") || model.Contains("TB"))
                {
                    if (!model.Contains("Legion"))
                        result.MarketName = "Lenovo Legion Tablet " + model;
                    result.Brand = "Lenovo (Legion)";
                }
            }

            return result;
        }

        /// <summary>
        /// ZTE/Nubia specific read strategy
        /// </summary>
        private async Task<BuildPropInfo> ReadZteBuildPropAsync(Func<string, long, int, Task<byte[]>> readPartition, 
            string activeSlot, bool hasSuper, long superStart, int sectorSize)
        {
            Log("Using ZTE/nubia specific parsing strategy...", Color.Blue);
            
            var result = await _deviceInfoService.ReadBuildPropFromDevice(readPartition, activeSlot, hasSuper, superStart, sectorSize, "ZTE");
            
            // ZTE/Nubia specific processing
            if (result != null)
            {
                string brand = result.Brand?.ToLower() ?? "";
                string ota = result.OtaVersion ?? "";

                // Identify RedMagic series
                if (ota.Contains("RedMagic") || brand.Contains("nubia"))
                {
                    string model = result.MarketName ?? result.Model ?? "Phone";
                    if (!model.Contains("RedMagic") && ota.Contains("RedMagic"))
                    {
                        result.MarketName = "Nubia RedMagic " + model;
                    }
                    result.Brand = "Nubia";
                }
                else if (brand.Contains("zte"))
                {
                    result.Brand = "ZTE";
                }
            }

            return result;
        }

        /// <summary>
        /// Apply build.prop info to UI
        /// </summary>
        private void ApplyBuildPropInfo(BuildPropInfo buildProp)
        {
            if (buildProp == null) return;

            if (_currentDeviceInfo == null)
            {
                _currentDeviceInfo = new DeviceFullInfo();
            }

            // Brand (Formatted display)
            if (!string.IsNullOrEmpty(buildProp.Brand))
            {
                string displayBrand = FormatBrandForDisplay(buildProp.Brand);
                _currentDeviceInfo.Brand = displayBrand;
                UpdateLabelSafe(_brandLabel, "Brand: " + displayBrand);
            }

            // Model and Market Name (Highest priority)
            if (!string.IsNullOrEmpty(buildProp.MarketName))
            {
                // Generic enhancement logic: If market name contains key codename, try to format display
                string finalMarket = buildProp.MarketName;
                
                // Generic Lenovo correction
                if ((finalMarket.Contains("Y700") || finalMarket.Contains("Legion")) && !finalMarket.Contains("Rescue"))
                    finalMarket = "Lenovo Legion Tablet " + finalMarket;

                _currentDeviceInfo.MarketName = finalMarket;
                UpdateLabelSafe(_modelLabel, "Model: " + finalMarket);
            }
            else if (!string.IsNullOrEmpty(buildProp.Model))
            {
                _currentDeviceInfo.Model = buildProp.Model;
                UpdateLabelSafe(_modelLabel, "Model: " + buildProp.Model);
            }

            // Version Info (OTA Version/Region)
            // Priority: OtaVersion > Incremental > DisplayId
            string otaVer = "";
            if (!string.IsNullOrEmpty(buildProp.OtaVersion))
                otaVer = buildProp.OtaVersion;
            else if (!string.IsNullOrEmpty(buildProp.Incremental))
                otaVer = buildProp.Incremental;
            else if (!string.IsNullOrEmpty(buildProp.DisplayId))
                otaVer = buildProp.DisplayId;

            if (!string.IsNullOrEmpty(otaVer))
            {
                // Get brand for judgment
                string brandLower = (buildProp.Brand ?? "").ToLowerInvariant();
                string manufacturerLower = (buildProp.Manufacturer ?? "").ToLowerInvariant();
                
                // OPLUS Device (OnePlus/OPPO/Realme) Version Number Cleanup
                // Original format e.g.: PJD110_14.0.0.801(CN01) -> 14.0.0.801(CN01)
                bool isOneplus = brandLower.Contains("oneplus") || manufacturerLower.Contains("oneplus");
                bool isOppo = brandLower.Contains("oppo") || manufacturerLower.Contains("oppo");
                bool isRealme = brandLower.Contains("realme") || manufacturerLower.Contains("realme");
                bool isOplus = isOneplus || isOppo || isRealme || brandLower.Contains("oplus");
                
                if (isOplus)
                {
                    // Extract version number: "PJD110_14.0.0.801(CN01)" -> "14.0.0.801(CN01)"
                    // Or "A.70" format -> Skip
                    var versionMatch = Regex.Match(otaVer, @"(\d+\.\d+\.\d+\.\d+(?:\([A-Z]{2}\d+\))?)");
                    if (versionMatch.Success)
                    {
                        string cleanVersion = versionMatch.Groups[1].Value;
                        
                        // Add system name prefix based on brand
                        if (isOneplus)
                            otaVer = "OxygenOS " + cleanVersion;
                        else if (isRealme)
                            otaVer = "realme UI " + cleanVersion;
                        else // OPPO
                            otaVer = "ColorOS " + cleanVersion;
                    }
                }
                // Lenovo ZUI Version: Extract 17.0.x.x format
                else if (brandLower.Contains("lenovo"))
                {
                    var zuiMatch = Regex.Match(otaVer, @"(\d+\.\d+\.\d+\.\d+)");
                    if (zuiMatch.Success && !otaVer.Contains("ZUI"))
                        otaVer = "ZUI " + zuiMatch.Groups[1].Value;
                }
                // Xiaomi HyperOS 3.0 (Android 16+)
                else if (otaVer.StartsWith("OS3.") && !otaVer.Contains("HyperOS"))
                {
                    otaVer = "HyperOS 3.0 " + otaVer;
                }
                // Xiaomi HyperOS 1.0/2.0
                else if (otaVer.StartsWith("OS") && !otaVer.Contains("HyperOS"))
                {
                    otaVer = "HyperOS " + otaVer;
                }
                // Xiaomi MIUI Era
                else if (otaVer.StartsWith("V") && !otaVer.Contains("MIUI") && (brandLower.Contains("xiaomi") || brandLower.Contains("redmi")))
                {
                    otaVer = "MIUI " + otaVer;
                }
                // RedMagic RedMagicOS
                else if (otaVer.Contains("RedMagic") && !otaVer.StartsWith("RedMagicOS"))
                {
                    otaVer = otaVer.Replace("RedMagic", "RedMagicOS ");
                }
                // ZTE NebulaOS/MiFavor
                else if (brandLower.Contains("zte") && !otaVer.Contains("NebulaOS"))
                {
                    otaVer = "NebulaOS " + otaVer;
                }

                _currentDeviceInfo.OtaVersion = otaVer;
                UpdateLabelSafe(_otaVersionLabel, "Version: " + otaVer);
            }

            // Android Version
            if (!string.IsNullOrEmpty(buildProp.AndroidVersion))
            {
                _currentDeviceInfo.AndroidVersion = buildProp.AndroidVersion;
                _currentDeviceInfo.SdkVersion = buildProp.SdkVersion;
            }
            
            // Device Codename (Use unlockLabel to display internal device codename)
            // Priority: Codename > Device > DeviceName
            string codename = "";
            if (!string.IsNullOrEmpty(buildProp.Codename))
                codename = buildProp.Codename;
            else if (!string.IsNullOrEmpty(buildProp.Device))
                codename = buildProp.Device;
            else if (!string.IsNullOrEmpty(buildProp.DeviceName))
                codename = buildProp.DeviceName;
            
            if (!string.IsNullOrEmpty(codename))
            {
                _currentDeviceInfo.DeviceCodename = codename;
                UpdateLabelSafe(_unlockLabel, "Codename: " + codename);
            }
            else if (!string.IsNullOrEmpty(buildProp.Model))
            {
                // If no codename, use model as alternative
                _currentDeviceInfo.Model = buildProp.Model;
                UpdateLabelSafe(_unlockLabel, "Codename: " + buildProp.Model);
            }

            // Region Info
            if (!string.IsNullOrEmpty(buildProp.Region))
                _currentDeviceInfo.Region = buildProp.Region;
            if (!string.IsNullOrEmpty(buildProp.MarketRegion))
                _currentDeviceInfo.MarketRegion = buildProp.MarketRegion;
            if (!string.IsNullOrEmpty(buildProp.DevProduct))
                _currentDeviceInfo.DevProduct = buildProp.DevProduct;
            if (!string.IsNullOrEmpty(buildProp.Product))
                _currentDeviceInfo.Product = buildProp.Product;
            
            // Build Info
            if (!string.IsNullOrEmpty(buildProp.BuildId))
                _currentDeviceInfo.BuildId = buildProp.BuildId;
            if (!string.IsNullOrEmpty(buildProp.DisplayId))
                _currentDeviceInfo.DisplayId = buildProp.DisplayId;
            if (!string.IsNullOrEmpty(buildProp.Fingerprint))
                _currentDeviceInfo.Fingerprint = buildProp.Fingerprint;
            if (!string.IsNullOrEmpty(buildProp.BuildDate))
                _currentDeviceInfo.BuiltDate = buildProp.BuildDate;
            if (!string.IsNullOrEmpty(buildProp.BuildUtc))
                _currentDeviceInfo.BuildTimestamp = buildProp.BuildUtc;
            if (!string.IsNullOrEmpty(buildProp.SecurityPatch))
                _currentDeviceInfo.SecurityPatch = buildProp.SecurityPatch;
            if (!string.IsNullOrEmpty(buildProp.OtaVersionFull))
                _currentDeviceInfo.OtaVersionFull = buildProp.OtaVersionFull;

            // OPLUS/Realme Specific Properties
            if (!string.IsNullOrEmpty(buildProp.OplusProject))
                _currentDeviceInfo.OplusProject = buildProp.OplusProject;
            if (!string.IsNullOrEmpty(buildProp.OplusNvId))
                _currentDeviceInfo.OplusNvId = buildProp.OplusNvId;

            // Lenovo Specific Properties
            if (!string.IsNullOrEmpty(buildProp.LenovoSeries))
            {
                _currentDeviceInfo.LenovoSeries = buildProp.LenovoSeries;
                Log(string.Format("  Lenovo Series: {0}", buildProp.LenovoSeries), Color.Blue);
                // Lenovo series is usually more intuitive than model, can use it if MarketName is empty
                if (string.IsNullOrEmpty(_currentDeviceInfo.MarketName))
                {
                    UpdateLabelSafe(_modelLabel, "Model: " + buildProp.LenovoSeries);
                }
            }

            // ZTE/Nubia/RedMagic special processing (Model name correction)
            if (!string.IsNullOrEmpty(buildProp.Brand))
            {
                string b = buildProp.Brand.ToLower();
                if (b == "nubia" || b == "zte")
                {
                    // If RedMagic series, update model name
                    string ota = buildProp.OtaVersion ?? buildProp.DisplayId ?? "";
                    if (ota.Contains("RedMagic"))
                    {
                        // RedMagic series model formatting
                        string rmMarket = "";
                        if (buildProp.Model == "NX789J")
                            rmMarket = "RedMagic 10 Pro (NX789J)";
                        else if (!string.IsNullOrEmpty(buildProp.MarketName))
                            rmMarket = buildProp.MarketName.Contains("RedMagic") ? buildProp.MarketName : "RedMagic " + buildProp.MarketName;
                        else
                            rmMarket = "RedMagic " + buildProp.Model;
                        
                        _currentDeviceInfo.MarketName = rmMarket;
                        UpdateLabelSafe(_modelLabel, "Model: " + rmMarket);
                    }
                    else if (buildProp.Model == "NX789J")
                    {
                        _currentDeviceInfo.MarketName = "RedMagic 10 Pro";
                        UpdateLabelSafe(_modelLabel, "Model: RedMagic 10 Pro");
                    }
                }
            }
        }

        /// <summary>
        /// Read build.prop online from device Super partition and update device info (Public method, can be called independently)
        /// </summary>
        public async Task<bool> ReadBuildPropFromDeviceAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            // Check if super partition exists
            bool hasSuper = Partitions != null && Partitions.Exists(p => p.Name == "super");
            if (!hasSuper)
            {
                Log("Super partition not found, cannot read build.prop", Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Read Device Info", 1, 0);
                Log("Reading build.prop from device...", Color.Blue);

                await TryReadBuildPropInternalAsync();
                
                UpdateTotalProgress(1, 1);
                return _currentDeviceInfo != null && !string.IsNullOrEmpty(_currentDeviceInfo.MarketName);
            }
            catch (Exception ex)
            {
                Log("Failed to read build.prop: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// Format Brand Display Name
        /// </summary>
        private string FormatBrandForDisplay(string brand)
        {
            if (string.IsNullOrEmpty(brand)) return "Unknown";
            
            string lower = brand.ToLowerInvariant();
            
            // OnePlus
            if (lower.Contains("oneplus"))
                return "OnePlus";
            // OPPO
            if (lower == "oppo")
                return "OPPO";
            // realme
            if (lower.Contains("realme"))
                return "realme";
            // Xiaomi / MIUI
            if (lower == "xiaomi" || lower == "miui" || lower.Contains("xiaomi"))
                return "Xiaomi";
            // Redmi
            if (lower == "redmi" || lower.Contains("redmi"))
                return "Redmi";
            // POCO
            if (lower == "poco" || lower.Contains("poco"))
                return "POCO";
            // nubia
            if (lower == "nubia")
                return "Nubia";
            // ZTE
            if (lower == "zte")
                return "ZTE";
            // Lenovo
            if (lower.Contains("lenovo"))
                return "Lenovo";
            // Motorola
            if (lower.Contains("motorola") || lower.Contains("moto"))
                return "Motorola";
            // Samsung
            if (lower.Contains("samsung"))
                return "Samsung";
            // Meizu
            if (lower.Contains("meizu"))
                return "Meizu";
            // vivo
            if (lower == "vivo")
                return "vivo";
            // iQOO
            if (lower == "iqoo")
                return "iQOO";
            
            // Return with first letter capitalized
            return char.ToUpper(brand[0]) + brand.Substring(1).ToLower();
        }

        /// <summary>
        /// Clear Device Info Labels
        /// </summary>
        public void ClearDeviceInfoLabels()
        {
            _currentDeviceInfo = null;
            UpdateLabelSafe(_brandLabel, "Brand: Waiting for connection");
            UpdateLabelSafe(_chipLabel, "Chip: Waiting for connection");
            UpdateLabelSafe(_modelLabel, "Model: Waiting for connection");
            UpdateLabelSafe(_serialLabel, "Chip Serial: Waiting for connection");
            UpdateLabelSafe(_storageLabel, "Storage: Waiting for connection");
            UpdateLabelSafe(_unlockLabel, "Codename: Waiting for connection");
            UpdateLabelSafe(_otaVersionLabel, "Version: Waiting for connection");
        }

        #endregion

        public async Task<bool> ReadPartitionTableAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                // Read partition table: Two stages - GPT Read (80%) + Device Info Parse (20%)
                StartOperationTimer("Read GPT", 100, 0);
                UpdateProgressBarDirect(_progressBar, 0);
                UpdateProgressBarDirect(_subProgressBar, 0);
                Log("Reading Partition Table (GPT)...", Color.Blue);

                // Progress callback - GPT read mapped to 0-80%
                int maxLuns = 6;
                var totalProgress = new Progress<Tuple<int, int>>(t => {
                    double percent = 80.0 * t.Item1 / t.Item2;
                    UpdateProgressBarDirect(_progressBar, percent);
                });
                var subProgress = new Progress<double>(p => UpdateProgressBarDirect(_subProgressBar, p));

                // Use ReadAllGptAsync with progress
                var partitions = await _service.ReadAllGptAsync(maxLuns, totalProgress, subProgress, _cts.Token);
                
                UpdateProgressBarDirect(_progressBar, 80);
                UpdateProgressBarDirect(_subProgressBar, 100);

                if (partitions != null && partitions.Count > 0)
                {
                    Partitions = partitions;
                    UpdatePartitionListView(partitions);
                    UpdateDeviceInfoFromPartitions();  // Update device info (Get more info from partitions)
                    PartitionsLoaded?.Invoke(this, partitions);
                    Log(string.Format("Successfully read {0} partitions", partitions.Count), Color.Green);
                    
                    // After reading partition table, try to read device info (build.prop) - Takes 80-100%
                    var superPart = partitions.Find(p => p.Name.Equals("super", StringComparison.OrdinalIgnoreCase));
                    var systemPart = partitions.Find(p => p.Name.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("system_a", StringComparison.OrdinalIgnoreCase));
                    var vendorPart = partitions.Find(p => p.Name.Equals("vendor", StringComparison.OrdinalIgnoreCase) ||
                                                          p.Name.Equals("vendor_a", StringComparison.OrdinalIgnoreCase));
                    
                    if (superPart != null || systemPart != null || vendorPart != null)
                    {
                        string partType = superPart != null ? "super" : (systemPart != null ? "system" : "vendor");
                        Log(string.Format("Detected {0} partition, starting to read device info...", partType), Color.Blue);
                        UpdateProgressBarDirect(_subProgressBar, 0);
                        await TryReadBuildPropInternalAsync();
                    }
                    else
                    {
                        Log("Super/system/vendor partition not detected, skipping device info read", Color.Orange);
                    }
                    
                    UpdateProgressBarDirect(_progressBar, 100);
                    UpdateProgressBarDirect(_subProgressBar, 100);
                    
                    return true;
                }
                else
                {
                    Log("No partitions read", Color.Orange);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to read partition table: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after reading partition table
            }
        }

        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath)
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            var p = _service.FindPartition(partitionName);
            long totalBytes = p?.Size ?? 0;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Read " + partitionName, 1, 0, totalBytes);
                Log(string.Format("Reading partition {0}...", partitionName), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

                if (success) Log(string.Format("Partition {0} saved to {1}", partitionName, outputPath), Color.Green);
                else Log(string.Format("Read {0} failed", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("Read partition failed: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after operation completion
            }
        }

        public async Task<bool> WritePartitionAsync(string partitionName, string filePath)
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!File.Exists(filePath)) { Log("File not found: " + filePath, Color.Red); return false; }

            if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
            {
                Log(string.Format("Skipping sensitive partition: {0}", partitionName), Color.Orange);
                return false;
            }

            long totalBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Write " + partitionName, 1, 0, totalBytes);
                Log(string.Format("Writing partition {0}...", partitionName), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.WritePartitionAsync(partitionName, filePath, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

                if (success) Log(string.Format("Partition {0} write success", partitionName), Color.Green);
                else Log(string.Format("Write {0} failed", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("Write partition failed: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after operation completion
            }
        }

        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
            {
                Log(string.Format("Skipping sensitive partition: {0}", partitionName), Color.Orange);
                return false;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Erase " + partitionName, 1, 0);
                Log(string.Format("Erasing partition {0}...", partitionName), Color.Blue);
                UpdateLabelSafe(_speedLabel, "Speed: Erasing...");

                // Erase has no granular progress, simulate progress
                UpdateProgressBarDirect(_subProgressBar, 50);

                bool success = await _service.ErasePartitionAsync(partitionName, _cts.Token);

                UpdateProgressBarDirect(_subProgressBar, 100);
                UpdateTotalProgress(1, 1);

                if (success) Log(string.Format("Partition {0} erased", partitionName), Color.Green);
                else Log(string.Format("Erase {0} failed", partitionName), Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("Erase partition failed: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after operation completion
            }
        }

        #region Batch Operation (Supports Dual Progress Bar)

        /// <summary>
        /// Batch Read Partitions
        /// </summary>
        public async Task<int> ReadPartitionsBatchAsync(List<Tuple<string, string>> partitionsToRead)
        {
            if (!await EnsureConnectedAsync()) return 0;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return 0; }

            int total = partitionsToRead.Count;
            int success = 0;
            
            // Pre-fetch total size of partitions for smooth progress bar
            long totalBytes = 0;
            foreach (var item in partitionsToRead)
            {
                var p = _service.FindPartition(item.Item1);
                if (p != null) totalBytes += p.Size;
            }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Batch Read", total, 0, totalBytes);
                Log(string.Format("Starting batch read of {0} partitions (Total: {1:F2} MB)...", total, totalBytes / 1024.0 / 1024.0), Color.Blue);

                long currentCompletedBytes = 0;
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var item = partitionsToRead[i];
                    string partitionName = item.Item1;
                    string outputPath = item.Item2;
                    
                    var p = _service.FindPartition(partitionName);
                    long pSize = p?.Size ?? 0;

                    // Pass current partition size for accurate speed calculation
                    UpdateTotalProgress(i, total, currentCompletedBytes, pSize);
                    UpdateLabelSafe(_operationLabel, string.Format("Reading {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<double>(percent => UpdateSubProgressFromPercent(percent));
                    bool ok = await _service.ReadPartitionAsync(partitionName, outputPath, progress, _cts.Token);

                    if (ok)
                    {
                        success++;
                        currentCompletedBytes += pSize;
                        Log(string.Format("[{0}/{1}] {2} read success", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} read failed", i + 1, total, partitionName), Color.Red);
                    }
                }

                UpdateTotalProgress(total, total, totalBytes, 0);
                Log(string.Format("Batch read complete: {0}/{1} success", success, total), success == total ? Color.Green : Color.Orange);
                return success;
            }
            catch (Exception ex)
            {
                Log("Batch read failed: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after batch operation
            }
        }

        /// <summary>
        /// Batch Write Partitions (Simple Version)
        /// </summary>
        public async Task<int> WritePartitionsBatchAsync(List<Tuple<string, string>> partitionsToWrite)
        {
            // Convert to new format (Use LUN=0, StartSector=0 as placeholder)
            var converted = partitionsToWrite.Select(t => Tuple.Create(t.Item1, t.Item2, 0, 0L)).ToList();
            return await WritePartitionsBatchAsync(converted, null, false);
        }

        /// <summary>
        /// Batch Write Partitions (Supports Patch and Boot Partition Activation)
        /// </summary>
        /// <param name="partitionsToWrite">Partition info list (Name, File Path, LUN, StartSector)</param>
        /// <param name="patchFiles">Patch XML file list (Optional)</param>
        /// <param name="activateBootLun">Activate Boot LUN (UFS)</param>
        public async Task<int> WritePartitionsBatchAsync(List<Tuple<string, string, int, long>> partitionsToWrite, List<string> patchFiles, bool activateBootLun)
        {
            if (!await EnsureConnectedAsync()) return 0;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return 0; }

            int total = partitionsToWrite.Count;
            int success = 0;
            bool hasPatch = patchFiles != null && patchFiles.Count > 0;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                // Calculate total steps: Partition write + Patch + Activate
                int totalSteps = total + (hasPatch ? 1 : 0) + (activateBootLun ? 1 : 0);
                
                // Pre-fetch total bytes for smooth progress bar
                long totalBytes = 0;
                foreach (var item in partitionsToWrite)
                {
                    string path = item.Item2;
                    if (File.Exists(path))
                    {
                        if (SparseStream.IsSparseFile(path))
                        {
                            // Use real data size, not expanded size
                            using (var ss = SparseStream.Open(path))
                                totalBytes += ss.GetRealDataSize();
                        }
                        else
                        {
                            totalBytes += new FileInfo(path).Length;
                        }
                    }
                }

                StartOperationTimer("Batch Write", totalSteps, 0, totalBytes);
                Log(string.Format("Starting batch write of {0} partitions (Real data: {1:F2} MB)...", total, totalBytes / 1024.0 / 1024.0), Color.Blue);

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
                            // Use real data size, not expanded size
                            using (var ss = SparseStream.Open(filePath))
                                fSize = ss.GetRealDataSize();
                        }
                        else
                        {
                            fSize = new FileInfo(filePath).Length;
                        }
                    }

                    if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
                    {
                        Log(string.Format("[{0}/{1}] Skipping sensitive partition: {2}", i + 1, total, partitionName), Color.Orange);
                        currentCompletedBytes += fSize;
                        continue;
                    }

                    // Pass current file size for accurate speed calculation
                    UpdateTotalProgress(i, totalSteps, currentCompletedBytes, fSize);
                    UpdateLabelSafe(_operationLabel, string.Format("Writing {0} ({1}/{2})", partitionName, i + 1, total));

                    var progress = new Progress<double>(percent => UpdateSubProgressFromPercent(percent));
                    bool ok;

                    // If LUN and StartSector info exists in XML, use direct write
                    // This ensures writing to position defined in XML, not relying on device GPT
                    bool useDirectWrite = partitionName == "PrimaryGPT" || partitionName == "BackupGPT" || 
                        partitionName.StartsWith("gpt_main") || partitionName.StartsWith("gpt_backup") ||
                        startSector != 0;  // If explicit start sector, use direct write
                    
                    if (useDirectWrite)
                    {
                        ok = await _service.WriteDirectAsync(partitionName, filePath, lun, startSector, progress, _cts.Token);
                    }
                    else
                    {
                        // When no explicit sector info, try finding by partition name (Relies on device GPT)
                        ok = await _service.WritePartitionAsync(partitionName, filePath, progress, _cts.Token);
                    }

                    if (ok)
                    {
                        success++;
                        currentCompletedBytes += fSize;
                        // Do not print log for success, only summarize at the end
                    }
                    else
                    {
                        // Print detailed info on failure
                        Log(string.Format("Write failed: {0}", partitionName), Color.Red);
                    }
                }

                // Summarize results
                if (success == total)
                    Log(string.Format("Partition write complete: All {0} partitions success", total), Color.Green);
                else
                    Log(string.Format("Partition write complete: {0}/{1} success, {2} failed", success, total, total - success), Color.Orange);

                // 2. Apply Patch (If any)
                if (hasPatch && !_cts.Token.IsCancellationRequested)
                {
                    UpdateTotalProgress(total, totalSteps, currentCompletedBytes, 0);
                    UpdateLabelSafe(_operationLabel, "Applying patch...");
                    Log(string.Format("Starting to apply {0} Patch files...", patchFiles.Count), Color.Blue);

                    int patchCount = await _service.ApplyPatchFilesAsync(patchFiles, _cts.Token);
                    Log(string.Format("Successfully applied {0} patches", patchCount), patchCount > 0 ? Color.Green : Color.Orange);
                }
                else if (!hasPatch)
                {
                    Log("No Patch files, skipping patch step", Color.Gray);
                }

                // 3. Fix GPT (Critical step! Fix primary/backup GPT and CRC)
                if (!_cts.Token.IsCancellationRequested)
                {
                    UpdateLabelSafe(_operationLabel, "Fixing GPT...");
                    Log("Fixing GPT partition table (Sync Primary/Backup + CRC)...", Color.Blue);
                    
                    // Fix GPT for all LUNs (-1 means all LUNs)
                    bool fixOk = await _service.FixGptAsync(-1, _cts.Token);
                    if (fixOk)
                        Log("GPT fix success", Color.Green);
                    else
                        Log("GPT fix failed (May cause boot failure)", Color.Orange);
                }

                // 4. Activate Boot Partition (UFS devices need activation, eMMC only LUN0)
                if (activateBootLun && !_cts.Token.IsCancellationRequested)
                {
                    UpdateTotalProgress(total + (hasPatch ? 1 : 0), totalSteps, currentCompletedBytes);
                    UpdateLabelSafe(_operationLabel, "Reading back partition table to detect slot...");
                    
                    // Read back GPT to detect current slot
                    Log("Reading back GPT to detect current slot...", Color.Blue);
                    var partitions = await _service.ReadAllGptAsync(6, _cts.Token);
                    
                    string currentSlot = _service.CurrentSlot;
                    Log(string.Format("Detected current slot: {0}", currentSlot), Color.Blue);

                    // Determine boot LUN based on slot - Strictly follow A/B partition state
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
                        // A/B partitions exist but activation state not set, try to infer from written partitions
                        // Check if partitions with _a or _b suffix were written
                        int slotACount = partitionsToWrite.Count(p => p.Item1.EndsWith("_a"));
                        int slotBCount = partitionsToWrite.Count(p => p.Item1.EndsWith("_b"));
                        
                        if (slotACount > slotBCount)
                        {
                            bootLun = 1;
                            bootSlotName = "boot_a (Inferred from written partitions)";
                            Log("Slot not activated, inferred LUN1 usage based on written _a partition", Color.Blue);
                        }
                        else if (slotBCount > slotACount)
                        {
                            bootLun = 2;
                            bootSlotName = "boot_b (Inferred from written partitions)";
                            Log("Slot not activated, inferred LUN2 usage based on written _b partition", Color.Blue);
                        }
                        else if (slotACount > 0 && slotBCount > 0)
                        {
                            // Full flash: Written both _a and _b partitions, default activate slot_a
                            bootLun = 1;
                            bootSlotName = "boot_a (Full flash default)";
                            Log("Full flash mode, default activate slot_a (LUN1)", Color.Blue);
                        }
                        else
                        {
                            // No A/B partitions, skipping activation
                            Log("A/B partitions not detected, skipping boot partition activation", Color.Gray);
                        }
                    }
                    else if (currentSlot == "nonexistent")
                    {
                        // Device does not support A/B partitions, skipping activation
                        Log("Device does not support A/B partitions, skipping boot partition activation", Color.Gray);
                    }

                    // Only activate after determining bootLun
                    if (bootLun > 0)
                    {
                        UpdateLabelSafe(_operationLabel, string.Format("Activating boot partition LUN{0}...", bootLun));
                        Log(string.Format("Activating LUN{0} ({1})...", bootLun, bootSlotName), Color.Blue);

                        bool bootOk = await _service.SetBootLunAsync(bootLun, _cts.Token);
                        if (bootOk)
                            Log(string.Format("LUN{0} activation success", bootLun), Color.Green);
                        else
                            Log(string.Format("LUN{0} activation failed (Some devices might not support)", bootLun), Color.Orange);
                    }
                }

                UpdateTotalProgress(totalSteps, totalSteps);
                return success;
            }
            catch (Exception ex)
            {
                Log("Batch write failed: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// Apply Patch File
        /// </summary>
        public async Task<int> ApplyPatchFilesAsync(List<string> patchFiles)
        {
            if (!await EnsureConnectedAsync()) return 0;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return 0; }
            if (patchFiles == null || patchFiles.Count == 0) return 0;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Apply Patch", patchFiles.Count, 0);
                Log(string.Format("Starting to apply {0} Patch files...", patchFiles.Count), Color.Blue);

                int totalPatches = 0;
                for (int i = 0; i < patchFiles.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    UpdateTotalProgress(i, patchFiles.Count);
                    UpdateLabelSafe(_operationLabel, string.Format("Patch {0}/{1}", i + 1, patchFiles.Count));

                    int count = await _service.ApplyPatchXmlAsync(patchFiles[i], _cts.Token);
                    totalPatches += count;
                    Log(string.Format("[{0}/{1}] {2}: {3} patches", i + 1, patchFiles.Count, 
                        Path.GetFileName(patchFiles[i]), count), Color.Green);
                }

                UpdateTotalProgress(patchFiles.Count, patchFiles.Count);
                Log(string.Format("Patch complete: Total {0} patches", totalPatches), Color.Green);
                return totalPatches;
            }
            catch (Exception ex)
            {
                Log("Apply Patch failed: " + ex.Message, Color.Red);
                return 0;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// Batch Erase Partitions
        /// </summary>
        public async Task<int> ErasePartitionsBatchAsync(List<string> partitionNames)
        {
            if (!await EnsureConnectedAsync()) return 0;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return 0; }

            int total = partitionNames.Count;
            int success = 0;

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("Batch Erase", total, 0);
                Log(string.Format("Starting batch erase of {0} partitions...", total), Color.Blue);
                UpdateLabelSafe(_speedLabel, "Speed: Erasing...");

                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    string partitionName = partitionNames[i];

                    if (IsProtectPartitionsEnabled() && RawprogramParser.IsSensitivePartition(partitionName))
                    {
                        Log(string.Format("[{0}/{1}] Skipping sensitive partition: {2}", i + 1, total, partitionName), Color.Orange);
                        continue;
                    }

                    UpdateTotalProgress(i, total);
                    UpdateLabelSafe(_operationLabel, string.Format("Erasing {0} ({1}/{2})", partitionName, i + 1, total));

                    // Erase has no granular progress, directly update sub progress
                    UpdateProgressBarDirect(_subProgressBar, 50);
                    
                    bool ok = await _service.ErasePartitionAsync(partitionName, _cts.Token);

                    UpdateProgressBarDirect(_subProgressBar, 100);

                    if (ok)
                    {
                        success++;
                        Log(string.Format("[{0}/{1}] {2} erase success", i + 1, total, partitionName), Color.Green);
                    }
                    else
                    {
                        Log(string.Format("[{0}/{1}] {2} erase failed", i + 1, total, partitionName), Color.Red);
                    }
                }

                UpdateTotalProgress(total, total);
                Log(string.Format("Batch erase complete: {0}/{1} success", success, total), success == total ? Color.Green : Color.Orange);
                return success;
            }
            catch (Exception ex)
            {
                Log("Batch erase failed: " + ex.Message, Color.Red);
                return success;
            }
            finally
            {
                EndOperation(releasePort: true);  // Release port after batch operation
            }
        }

        #endregion

        public async Task<bool> RebootToEdlAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                bool success = await _service.RebootToEdlAsync(_cts?.Token ?? CancellationToken.None);
                if (success) Log("Sent Reboot to EDL command", Color.Green);
                return success;
            }
            catch (Exception ex) { Log("Reboot to EDL failed: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> RebootToSystemAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                bool success = await _service.RebootAsync(_cts?.Token ?? CancellationToken.None);
                if (success) { Log("Device is rebooting to system", Color.Green); Disconnect(); }
                return success;
            }
            catch (Exception ex) { Log("Reboot failed: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> SwitchSlotAsync(string slot)
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                bool success = await _service.SetActiveSlotAsync(slot, _cts?.Token ?? CancellationToken.None);
                if (success) Log(string.Format("Switched to slot {0}", slot), Color.Green);
                else Log("Switch slot failed", Color.Red);
                return success;
            }
            catch (Exception ex) { Log("Switch slot failed: " + ex.Message, Color.Red); return false; }
        }

        public async Task<bool> SetBootLunAsync(int lun)
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                bool success = await _service.SetBootLunAsync(lun, _cts?.Token ?? CancellationToken.None);
                if (success) Log(string.Format("LUN {0} activated", lun), Color.Green);
                else Log("Activate LUN failed", Color.Red);
                return success;
            }
            catch (Exception ex) { Log("Activate LUN failed: " + ex.Message, Color.Red); return false; }
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
            catch { /* UI control access failure can be ignored */ }
        }

        /// <summary>
        /// Try quick reconnect (Only reopen port, do not reconfigure Firehose)
        /// Applicable when port is released after operation
        /// </summary>
        /// <returns>Whether reconnect successful</returns>
        public async Task<bool> QuickReconnectAsync()
        {
            if (_service == null)
            {
                Log("Device not connected", Color.Red);
                return false;
            }
            
            if (!_service.IsPortReleased)
            {
                // Port not released, check if still available
                if (_service.IsConnectedFast)
                    return true;
            }
            
            _logDetail("[UI] Attempting quick reconnect...");
            
            // Try to reopen port
            bool success = await _service.EnsurePortOpenAsync(CancellationToken.None);
            if (success)
            {
                _logDetail("[UI] Quick reconnect success");
                return true;
            }
            
            _logDetail("[UI] Quick reconnect failed");
            return false;
        }
        
        private async Task<bool> EnsureConnectedAsync()
        {
            if (_service == null)
            {
                Log("Device not connected", Color.Red);
                return false;
            }
            
            // If port is released, try to reopen
            if (_service.IsPortReleased)
            {
                _logDetail("[UI] Port released, attempting to reopen...");
                if (!await _service.EnsurePortOpenAsync(CancellationToken.None))
                {
                    // Do not report error and clear state immediately, let caller decide how to handle
                    _logDetail("[UI] Port reopen failed");
                    return false;
                }
                _logDetail("[UI] Port reopen success");
            }
            
            // Use ValidateConnection to detect if port is truly available
            if (!_service.ValidateConnection())
            {
                _logDetail("[UI] Port validation failed");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Ensure connection available, show error and clear state on failure
        /// </summary>
        private async Task<bool> EnsureConnectedWithCleanupAsync()
        {
            if (await EnsureConnectedAsync())
                return true;
            
            // Connection failed, clear state
            Log("Device connection lost, full reconfiguration required", Color.Red);
            SetSkipSaharaChecked(false);
            ConnectionStateChanged?.Invoke(this, false);
            ClearDeviceInfoLabels();
            RefreshPorts();
            return false;
        }

        /// <summary>
        /// Cancel current operation
        /// </summary>
        public void CancelOperation()
        {
            if (_cts != null) 
            { 
                Log("Canceling operation...", Color.Orange);
                _cts.Cancel(); 
                _cts.Dispose(); 
                _cts = null; 
            }
        }

        /// <summary>
        /// Safely reset CancellationTokenSource (Create new instance after disposing old one)
        /// </summary>
        private void ResetCancellationToken()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } 
                catch (Exception ex) { _logDetail?.Invoke($"[UI] Cancel token exception: {ex.Message}"); }
                try { _cts.Dispose(); } 
                catch (Exception ex) { _logDetail?.Invoke($"[UI] Dispose token exception: {ex.Message}"); }
            }
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Is there an operation in progress
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
            // Calculate real-time speed (current=transferred bytes, total=total bytes)
            if (total > 0 && _operationStopwatch != null)
            {
                // Calculate real-time speed
                long bytesDelta = current - _lastBytes;
                double timeDelta = (DateTime.Now - _lastSpeedUpdate).TotalSeconds;
                
                if (timeDelta >= 0.2 && bytesDelta > 0) // Update every 200ms
                {
                    double instantSpeed = bytesDelta / timeDelta;
                    // Exponential moving average smoothing speed
                    _currentSpeed = (_currentSpeed > 0) ? (_currentSpeed * 0.6 + instantSpeed * 0.4) : instantSpeed;
                    _lastBytes = current;
                    _lastSpeedUpdate = DateTime.Now;
                    
                    // Update speed display
                    UpdateSpeedDisplayInternal();
                    
                    // Update time
                    var elapsed = _operationStopwatch.Elapsed;
                    string timeText = string.Format("Time: {0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
                    UpdateLabelSafe(_timeLabel, timeText);
                }
                
                // 1. Calculate sub progress (With decimal precision)
                double subPercent = (100.0 * current / total);
                subPercent = Math.Max(0, Math.Min(100, subPercent));
                UpdateProgressBarDirect(_subProgressBar, subPercent);
                
                // 2. Calculate total progress (High speed smooth version - Based on total bytes)
                if (_totalOperationBytes > 0 && _progressBar != null)
                {
                    long totalProcessed = _completedStepBytes + current;
                    double totalPercent = (100.0 * totalProcessed / _totalOperationBytes);
                    totalPercent = Math.Max(0, Math.Min(100, totalPercent));
                    UpdateProgressBarDirect(_progressBar, totalPercent);

                    // 3. Display percentage with two decimal places on UI
                    UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalPercent));
                }
                else if (_totalSteps > 0 && _progressBar != null)
                {
                    // Fallback: Based on steps
                    double totalProgress = (_currentStep + subPercent / 100.0) / _totalSteps * 100.0;
                    UpdateProgressBarDirect(_progressBar, totalProgress);
                    UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalProgress));
                }
            }
        }
        
        /// <summary>
        /// Update progress bar directly (Supports double precision, anti-flicker optimization)
        /// </summary>
        private int _lastProgressValue = -1;
        private int _lastSubProgressValue = -1;
        
        private void UpdateProgressBarDirect(dynamic progressBar, double percent)
        {
            if (progressBar == null) return;
            try
            {
                // Map 0-100 to 0-10000 for higher precision
                int intValue = (int)Math.Max(0, Math.Min(10000, percent * 100));
                
                // Check which progress bar, avoid duplicate updates
                bool isMainProgress = (progressBar == _progressBar);
                int lastValue = isMainProgress ? _lastProgressValue : _lastSubProgressValue;
                
                // Skip update if value unchanged to prevent flicker
                if (intValue == lastValue) return;
                
                // Update cached value
                if (isMainProgress) _lastProgressValue = intValue;
                else _lastSubProgressValue = intValue;
                
                if (progressBar.InvokeRequired)
                {
                    progressBar.BeginInvoke(new Action(() => {
                        if (progressBar.Maximum != 10000) progressBar.Maximum = 10000;
                        if (progressBar.Value != intValue) progressBar.Value = intValue;
                    }));
                }
                else
                {
                    if (progressBar.Maximum != 10000) progressBar.Maximum = 10000;
                    if (progressBar.Value != intValue) progressBar.Value = intValue;
                }
            }
            catch { /* UI progress bar update failure can be ignored */ }
        }
        
        private void UpdateSpeedDisplayInternal()
        {
            if (_speedLabel == null) return;
            
            string speedText;
            if (_currentSpeed >= 1024 * 1024)
                speedText = string.Format("Speed: {0:F1} MB/s", _currentSpeed / (1024 * 1024));
            else if (_currentSpeed >= 1024)
                speedText = string.Format("Speed: {0:F1} KB/s", _currentSpeed / 1024);
            else if (_currentSpeed > 0)
                speedText = string.Format("Speed: {0:F0} B/s", _currentSpeed);
            else
                speedText = "Speed: --";
            
            UpdateLabelSafe(_speedLabel, speedText);
        }
        
        /// <summary>
        /// Update sub progress bar (Short) - From percentage
        /// Improvement: Use current step bytes to calculate real speed
        /// </summary>
        private void UpdateSubProgressFromPercent(double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            UpdateProgressBarDirect(_subProgressBar, percent);
            
            // Estimate transferred bytes based on current step bytes (Not total operation bytes!)
            long currentStepTransferred = 0;
            if (_currentStepBytes > 0)
            {
                // Use current step bytes, not total operation bytes
                currentStepTransferred = (long)(_currentStepBytes * percent / 100.0);
            }
            
            // Calculate real-time speed (Based on delta)
            if (_operationStopwatch != null)
            {
                // Update time display
                var elapsed = _operationStopwatch.Elapsed;
                string timeText = string.Format("Time: {0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
                UpdateLabelSafe(_timeLabel, timeText);
                
                // Real-time speed calculation - Based on actual transferred bytes of current step
                long bytesDelta = currentStepTransferred - _lastBytes;
                double timeDelta = (DateTime.Now - _lastSpeedUpdate).TotalSeconds;
                
                if (timeDelta >= 0.2 && bytesDelta > 0) // Update every 200ms
                {
                    double instantSpeed = bytesDelta / timeDelta;
                    // Exponential moving average smoothing speed (Heavier historical weight to avoid jumping)
                    _currentSpeed = (_currentSpeed > 0) ? (_currentSpeed * 0.7 + instantSpeed * 0.3) : instantSpeed;
                    _lastBytes = currentStepTransferred;
                    _lastSpeedUpdate = DateTime.Now;
                    
                    // Update speed display
                    UpdateSpeedDisplayInternal();
                }
            }
            
            // Update operation label (Show percentage) - Use total operation bytes to calculate total progress
            if (_totalOperationBytes > 0)
            {
                long totalProcessed = _completedStepBytes + currentStepTransferred;
                double totalPercent = Math.Min(100, 100.0 * totalProcessed / _totalOperationBytes);
                UpdateProgressBarDirect(_progressBar, totalPercent);
                UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalPercent));
            }
            else if (_totalSteps > 0)
            {
                // Calculate total progress based on steps
                double totalProgress = (_currentStep + percent / 100.0) / _totalSteps * 100.0;
                UpdateProgressBarDirect(_progressBar, totalProgress);
                UpdateLabelSafe(_operationLabel, string.Format("{0} [{1:F2}%]", _currentOperationName, totalProgress));
            }
        }
        
        /// <summary>
        /// Update total progress bar (Long) - Total progress of multi-step operation
        /// </summary>
        /// <param name="currentStep">Current step index</param>
        /// <param name="totalSteps">Total steps</param>
        /// <param name="completedBytes">Total bytes of completed steps</param>
        /// <param name="currentStepBytes">Current step bytes (For accurate speed calculation)</param>
        public void UpdateTotalProgress(int currentStep, int totalSteps, long completedBytes = 0, long currentStepBytes = 0)
        {
            _currentStep = currentStep;
            _totalSteps = totalSteps;
            _completedStepBytes = completedBytes;
            _currentStepBytes = currentStepBytes;
            
            // Reset speed calculation variables (New step starts)
            _lastBytes = 0;
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
            catch { /* UI label update failure can be ignored */ }
        }
        
        /// <summary>
        /// Start timer (Single step operation)
        /// </summary>
        public void StartOperationTimer(string operationName)
        {
            StartOperationTimer(operationName, 0, 0, 0);
        }
        
        /// <summary>
        /// Start timer (Multi-step operation)
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
            _currentStepBytes = totalBytes; // For single file operation, current step bytes = total bytes
            _currentOperationName = operationName;
            
            // Reset progress bar cache (Anti-flicker)
            _lastProgressValue = -1;
            _lastSubProgressValue = -1;
            
            UpdateLabelSafe(_operationLabel, "Current Operation: " + operationName);
            UpdateLabelSafe(_timeLabel, "Time: 00:00");
            UpdateLabelSafe(_speedLabel, "Speed: --");
            
            // Reset progress bar to 0 (Use high precision mode)
            UpdateProgressBarDirect(_progressBar, 0);
            UpdateProgressBarDirect(_subProgressBar, 0);
        }
        
        /// <summary>
        /// Reset sub progress bar (Call before single operation starts)
        /// </summary>
        public void ResetSubProgress()
        {
            _lastBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            _currentSpeed = 0;
            _lastSubProgressValue = -1; // Reset cache to ensure next update takes effect
            UpdateProgressBarDirect(_subProgressBar, 0);
        }
        
        /// <summary>
        /// Stop timer
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
            UpdateLabelSafe(_operationLabel, "Current Operation: Completed");
            UpdateProgressBarDirect(_progressBar, 100);
            UpdateProgressBarDirect(_subProgressBar, 100);
        }
        
        /// <summary>
        /// End operation and release port (Call after operation completion)
        /// </summary>
        /// <param name="releasePort">Release port (Default true)</param>
        private void EndOperation(bool releasePort = true)
        {
            IsBusy = false;
            StopOperationTimer();
            
            // Release port to allow other programs to connect to device
            if (releasePort && _service != null)
            {
                _service.ReleasePort();
            }
        }
        
        /// <summary>
        /// Set whether to keep port open (Used in batch operations)
        /// </summary>
        public void SetKeepPortOpen(bool keepOpen)
        {
            _service?.SetKeepPortOpen(keepOpen);
        }
        
        /// <summary>
        /// Reset all progress displays
        /// </summary>
        public void ResetProgress()
        {
            _totalSteps = 0;
            _currentStep = 0;
            _lastBytes = 0;
            _currentSpeed = 0;
            _lastProgressValue = -1;    // Reset cache
            _lastSubProgressValue = -1; // Reset cache
            UpdateProgressBarDirect(_progressBar, 0);
            UpdateProgressBarDirect(_subProgressBar, 0);
            UpdateLabelSafe(_timeLabel, "Time: 00:00");
            UpdateLabelSafe(_speedLabel, "Speed: --");
            UpdateLabelSafe(_operationLabel, "Current Operation: Standby");
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
                // Calculate address
                long startAddress = p.StartSector * p.SectorSize;
                long endSector = p.StartSector + p.NumSectors - 1;
                long endAddress = (endSector + 1) * p.SectorSize;

                // Column order: Partition, LUN, Size, Start Sector, End Sector, Sector Count, Start Address, End Address, File Path
                var item = new ListViewItem(p.Name);                           // Partition
                item.SubItems.Add(p.Lun.ToString());                           // LUN
                item.SubItems.Add(p.FormattedSize);                            // Size
                item.SubItems.Add(p.StartSector.ToString());                   // Start Sector
                item.SubItems.Add(endSector.ToString());                       // End Sector
                item.SubItems.Add(p.NumSectors.ToString());                    // Sector Count
                item.SubItems.Add(string.Format("0x{0:X}", startAddress));     // Start Address
                item.SubItems.Add(string.Format("0x{0:X}", endAddress));       // End Address
                item.SubItems.Add("");                                         // File Path (No file when reading GPT)
                item.Tag = p;

                // Sensitive partitions show gray only when "Protect Partitions" is checked
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
                // Stop port monitor timer
                StopPortMonitor();
                if (_portMonitorTimer != null)
                {
                    _portMonitorTimer.Dispose();
                    _portMonitorTimer = null;
                }
                
                CancelOperation();
                Disconnect();
                _disposed = true;
            }
        }
        /// <summary>
        /// Perform VIP Auth Manually (Based on Digest and Signature)
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath)
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                StartOperationTimer("VIP Auth", 1, 0);
                Log("Executing OPLUS VIP Auth (Digest + Sign)...", Color.Blue);

                bool success = await _service.PerformVipAuthManualAsync(digestPath, signaturePath, _cts.Token);
                
                UpdateTotalProgress(1, 1);

                if (success) Log("VIP auth success, high privilege partitions unlocked", Color.Green);
                else Log("VIP auth failed, please check if files match", Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("VIP auth exception: " + ex.Message, Color.Red);
                return false;
            }
            finally
            {
                IsBusy = false;
                StopOperationTimer();
            }
        }

        /// <summary>
        /// Get VIP Challenge (For online signature retrieval)
        /// </summary>
        public async Task<string> GetVipChallengeAsync()
        {
            if (!await EnsureConnectedAsync()) return null;
            return await _service.GetVipChallengeAsync(_cts?.Token ?? default(CancellationToken));
        }

        public bool IsVipDevice { get { return _service != null && _service.IsVipDevice; } }
        public string DeviceVendor { get { return _service != null ? QualcommDatabase.GetVendorByPkHash(_service.ChipInfo?.PkHash) : "Unknown"; } }

        public async Task<bool> FlashOplusSuperAsync(string firmwareRoot)
        {
            if (!await EnsureConnectedAsync()) return false;
            if (IsBusy) { Log("Operation in progress", Color.Orange); return false; }
            if (!Directory.Exists(firmwareRoot)) { Log("Firmware directory not found: " + firmwareRoot, Color.Red); return false; }

            try
            {
                IsBusy = true;
                ResetCancellationToken();
                
                Log("[Qualcomm] Analyzing OPLUS Super layout deeply...", Color.Blue);
                
                // 1. Get super partition info
                var superPart = _service.FindPartition("super");
                if (superPart == null)
                {
                    Log("Super partition not found on device", Color.Red);
                    return false;
                }

                // 2. Pre-analyze tasks to get total bytes
                string activeSlot = _service.CurrentSlot;
                if (activeSlot == "nonexistent" || string.IsNullOrEmpty(activeSlot)) activeSlot = "a";
                
                string nvId = _currentDeviceInfo?.OplusNvId ?? "";
                
                var tasks = await new LoveAlways.Qualcomm.Services.OplusSuperFlashManager(s => Log(s, Color.Gray)).PrepareSuperTasksAsync(
                    firmwareRoot, superPart.StartSector, (int)superPart.SectorSize, activeSlot, nvId);

                if (tasks.Count == 0)
                {
                    Log("No available Super logic partition image found", Color.Red);
                    return false;
                }

                long totalBytes = tasks.Sum(t => t.SizeInBytes);

                StartOperationTimer("OPLUS Super Write", 1, 0, totalBytes);
                Log(string.Format("[Qualcomm] Starting OPLUS Super dismantle write ({0} images, Total unpacked {1:F2} MB)...", 
                    tasks.Count, totalBytes / 1024.0 / 1024.0), Color.Blue);

                var progress = new Progress<double>(p => UpdateSubProgressFromPercent(p));
                bool success = await _service.FlashOplusSuperAsync(firmwareRoot, nvId, progress, _cts.Token);

                UpdateTotalProgress(1, 1, totalBytes);

                if (success) Log("[Qualcomm] OPLUS Super write complete", Color.Green);
                else Log("[Qualcomm] OPLUS Super write failed", Color.Red);

                return success;
            }
            catch (Exception ex)
            {
                Log("OPLUS Super write exception: " + ex.Message, Color.Red);
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
