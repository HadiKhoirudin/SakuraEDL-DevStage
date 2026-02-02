// ============================================================================
// LoveAlways - Qualcomm Flash Service
// Qualcomm Flash Service - High-level API integrating Sahara and Firehose
// ============================================================================
// Module: Qualcomm.Services
// Function: Device Connection, Partition Read/Write, Flash Process Management
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Common;
using LoveAlways.Qualcomm.Authentication;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.Models;
using LoveAlways.Qualcomm.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Services
{
    /// <summary>
    /// Connection State
    /// </summary>
    public enum QualcommConnectionState
    {
        Disconnected,
        Connecting,
        SaharaMode,
        FirehoseMode,
        Ready,
        Error
    }

    /// <summary>
    /// Qualcomm Flash Service
    /// </summary>
    public class QualcommService : IDisposable
    {
        private SerialPortManager _portManager;
        private SaharaClient _sahara;
        private FirehoseClient _firehose;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;  // Detailed debug log (Write to file only)
        private readonly Action<long, long> _progress;
        private readonly OplusSuperFlashManager _oplusSuperManager;
        private readonly DeviceInfoService _deviceInfoService;
        private bool _disposed;

        // Watchdog Mechanism
        private Watchdog _watchdog;

        // State
        public QualcommConnectionState State { get; private set; }
        public QualcommChipInfo ChipInfo { get { return _sahara != null ? _sahara.ChipInfo : null; } }
        public bool IsVipDevice { get; private set; }
        public string StorageType { get { return _firehose != null ? _firehose.StorageType : "ufs"; } }
        public int SectorSize { get { return _firehose != null ? _firehose.SectorSize : 4096; } }
        public string CurrentSlot { get { return _firehose != null ? _firehose.CurrentSlot : "nonexistent"; } }

        // Last used connection parameters (For status display)
        public string LastPortName { get; private set; }
        public string LastStorageType { get; private set; }

        // Partition Cache
        private Dictionary<int, List<PartitionInfo>> _partitionCache;

        // Port management flags (Release port after operation completion)
        private bool _portClosed = false;          // Is port closed
        private bool _keepPortOpen = false;        // Keep port open (For continuous operation)
        private QualcommChipInfo _cachedChipInfo;  // Cached chip info (Retained after port closed)

        /// <summary>
        /// State changed event
        /// </summary>
        public event EventHandler<QualcommConnectionState> StateChanged;

        /// <summary>
        /// Port disconnected event (Triggered when device disconnects itself)
        /// </summary>
        public event EventHandler PortDisconnected;

        /// <summary>
        /// Xiaomi Auth Token Event (Triggered when built-in signature fails, requires popup to show token)
        /// Token Format: Base64 string starting with VQ
        /// </summary>
        public event Action<string> XiaomiAuthTokenRequired;

        /// <summary>
        /// Check if truly connected (Validates port state)
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (State != QualcommConnectionState.Ready)
                    return false;

                // Validate if port is truly available
                if (_portManager == null || !_portManager.ValidateConnection())
                {
                    // Port disconnected, update state
                    HandlePortDisconnected();
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Quick check connection state (No port validation, for high-frequency UI display)
        /// </summary>
        public bool IsConnectedFast
        {
            get { return State == QualcommConnectionState.Ready && _portManager != null && _portManager.IsOpen; }
        }

        /// <summary>
        /// Validate connection validity
        /// </summary>
        public bool ValidateConnection()
        {
            if (State != QualcommConnectionState.Ready)
                return false;

            if (_portManager == null)
                return false;

            // Check if port exists in system
            if (!_portManager.IsPortAvailable())
            {
                _logDetail("[Qualcomm] Port removed from system");
                HandlePortDisconnected();
                return false;
            }

            // Validate port connection
            if (!_portManager.ValidateConnection())
            {
                _logDetail("[Qualcomm] Port connection validation failed");
                HandlePortDisconnected();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Handle port disconnected (Device disconnected itself)
        /// </summary>
        private void HandlePortDisconnected()
        {
            if (State == QualcommConnectionState.Disconnected)
                return;

            _log("[Qualcomm] Device disconnection detected");

            // Cleanup resources (Ignore dispose exceptions, ensure complete cleanup)
            if (_portManager != null)
            {
                try { _portManager.Close(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[QualcommService] Close port exception: {ex.Message}"); }
                try { _portManager.Dispose(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[QualcommService] Dispose port exception: {ex.Message}"); }
                _portManager = null;
            }

            if (_firehose != null)
            {
                try { _firehose.Dispose(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[QualcommService] Dispose Firehose exception: {ex.Message}"); }
                _firehose = null;
            }

            // Clear partition cache (Cache invalid after device disconnect)
            _partitionCache.Clear();

            SetState(QualcommConnectionState.Disconnected);
            PortDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public QualcommService(Action<string> log = null, Action<long, long> progress = null, Action<string> logDetail = null)
        {
            _log = log ?? delegate { };
            _logDetail = logDetail ?? delegate { };
            _progress = progress;
            _oplusSuperManager = new OplusSuperFlashManager(_log);
            _deviceInfoService = new DeviceInfoService(_log, _logDetail);
            _partitionCache = new Dictionary<int, List<PartitionInfo>>();
            State = QualcommConnectionState.Disconnected;

            // Initialize Watchdog
            _watchdog = new Watchdog("Qualcomm", WatchdogManager.DefaultTimeouts.Qualcomm, _logDetail);
            _watchdog.OnTimeout += OnWatchdogTimeout;
        }

        /// <summary>
        /// Watchdog timeout handling
        /// </summary>
        private void OnWatchdogTimeout(object sender, WatchdogTimeoutEventArgs e)
        {
            _log($"[Qualcomm] Watchdog timeout: {e.OperationName} (Waited {e.ElapsedTime.TotalSeconds:F1}s)");

            // Try to reset if too many timeouts
            if (e.TimeoutCount >= 3)
            {
                _log("[Qualcomm] Multiple timeouts, attempting to reset connection...");
                e.ShouldReset = false; // Stop Watchdog

                // Trigger port disconnected event
                HandlePortDisconnected();
            }
        }

        /// <summary>
        /// Feed Watchdog - Call during long operations to reset watchdog timer
        /// </summary>
        public void FeedWatchdog()
        {
            _watchdog?.Feed();
        }

        /// <summary>
        /// Start Watchdog
        /// </summary>
        public void StartWatchdog(string operation)
        {
            _watchdog?.Start(operation);
        }

        /// <summary>
        /// Stop Watchdog
        /// </summary>
        public void StopWatchdog()
        {
            _watchdog?.Stop();
        }

        #region Connection Management

        /// <summary>
        /// Get Sahara device info only (Do not upload Loader)
        /// Used for cloud auto-matching
        /// </summary>
        /// <param name="portName">COM Port Name</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>Device info, returns null on failure</returns>
        public async Task<QualcommChipInfo> GetSaharaDeviceInfoOnlyAsync(string portName, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                _log("[Cloud] Getting device info...");

                // Initialize Serial Port
                _portManager = new SerialPortManager();

                // Sahara mode must keep initial Hello packet, do not clear buffer
                bool opened = await _portManager.OpenAsync(portName, 3, false, ct);
                if (!opened)
                {
                    _log("[Cloud] Cannot open port");
                    return null;
                }

                // Create Sahara Client
                _sahara = new SaharaClient(_portManager, _log, _logDetail, null);

                // Only perform handshake to get device info (Do not upload Loader)
                bool infoOk = await _sahara.GetDeviceInfoOnlyAsync(ct);

                if (!infoOk || _sahara.ChipInfo == null)
                {
                    _log("[Cloud] Cannot get device info");
                    _portManager.Close();
                    return null;
                }

                _log("[Cloud] Device info retrieved successfully");

                // Save chip info
                _cachedChipInfo = _sahara.ChipInfo;

                // Keep port open, will be used later
                return _sahara.ChipInfo;
            }
            catch (Exception ex)
            {
                _log($"[Cloud] Get device info exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Continue connection with retrieved device info (Upload Loader)
        /// </summary>
        /// <param name="loaderData">Loader Data</param>
        /// <param name="storageType">Storage Type</param>
        /// <param name="authMode">Auth Mode</param>
        /// <param name="ct">Cancellation Token</param>
        public async Task<bool> ContinueConnectWithLoaderAsync(byte[] loaderData, string storageType = "ufs",
            string authMode = "none", CancellationToken ct = default(CancellationToken))
        {
            try
            {
                if (_sahara == null || _portManager == null)
                {
                    _log("[Cloud] Please call GetSaharaDeviceInfoOnlyAsync first");
                    return false;
                }

                SetState(QualcommConnectionState.SaharaMode);

                // Use existing Sahara client to continue uploading Loader
                bool uploadOk = await _sahara.UploadLoaderAsync(loaderData, ct);
                if (!uploadOk)
                {
                    _log("[Cloud] Loader upload failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Set flag based on user selected auth mode
                IsVipDevice = (authMode.ToLowerInvariant() == "vip" || authMode.ToLowerInvariant() == "oplus");

                // Wait for Firehose ready
                _log("Sending Firehose loader : Success");
                await Task.Delay(1000, ct);

                // Reopen port (Firehose mode)
                string portName = _portManager.PortName;
                _portManager.Close();
                await Task.Delay(500, ct);

                bool opened = await _portManager.OpenAsync(portName, 5, true, ct);
                if (!opened)
                {
                    _log("[Cloud] Cannot reopen port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Firehose Configuration
                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress, _logDetail);

                // Pass chip info
                if (ChipInfo != null)
                {
                    _firehose.ChipSerial = ChipInfo.SerialHex;
                    _firehose.ChipHwId = ChipInfo.HwIdHex;
                    _firehose.ChipPkHash = ChipInfo.PkHash;
                }

                // Perform auth (If needed)
                string authModeLower = authMode.ToLowerInvariant();
                if (authModeLower == "xiaomi" || (authModeLower == "none" && IsXiaomiDevice()))
                {
                    _log("[Cloud] Executing Xiaomi Auth...");
                    var xiaomi = new XiaomiAuthStrategy(_log);
                    xiaomi.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                    bool authOk = await xiaomi.AuthenticateAsync(_firehose, null, ct);
                    if (authOk)
                        _log("[Cloud] Xiaomi Auth success");
                    else
                        _log("[Cloud] Xiaomi Auth failed");
                }
                else if (authModeLower == "oneplus")
                {
                    _log("[Cloud] Executing OnePlus Auth...");
                    var oneplus = new OnePlusAuthStrategy(_log);
                    bool authOk = await oneplus.AuthenticateAsync(_firehose, null, ct);
                    if (authOk)
                        _log("[Cloud] OnePlus Auth success");
                    else
                        _log("[Cloud] OnePlus Auth failed");
                }

                _log("Configuring Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("Configure Firehose : Failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("Configure Firehose : Success");

                SetState(QualcommConnectionState.Ready);
                return true;
            }
            catch (Exception ex)
            {
                _log($"[Cloud] Connection exception: {ex.Message}");
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// Connect Device
        /// </summary>
        /// <param name="portName">COM 端口名</param>
        /// <param name="programmerPath">Programmer File Path</param>
        /// <param name="storageType">Storage Type (ufs/emmc)</param>
        /// <param name="authMode">Auth Mode: none, vip, oneplus, xiaomi</param>
        /// <param name="digestPath">VIP Digest File Path</param>
        /// <param name="signaturePath">VIP Signature File Path</param>
        /// <param name="ct">取消令牌</param>
        public async Task<bool> ConnectAsync(string portName, string programmerPath, string storageType = "ufs",
            string authMode = "none", string digestPath = "", string signaturePath = "",
            CancellationToken ct = default(CancellationToken))
        {
            try
            {
                SetState(QualcommConnectionState.Connecting);
                _log("Waiting for Qualcomm EDL USB Device : Success");
                _log(string.Format("USB Port : {0}", portName));
                _log("Connecting to Device : Success");

                // Validate Programmer File
                if (!File.Exists(programmerPath))
                {
                    _log("[Qualcomm] Programmer file not found: " + programmerPath);
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Initialize Serial Port
                _portManager = new SerialPortManager();

                // Sahara mode must keep initial Hello packet, do not clear buffer
                bool opened = await _portManager.OpenAsync(portName, 3, false, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot open port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Sahara Handshake
                SetState(QualcommConnectionState.SaharaMode);

                // Create Sahara Client and pass progress callback
                Action<double> saharaProgress = null;
                if (_progress != null)
                {
                    saharaProgress = percent => _progress((long)percent, 100);
                }
                _sahara = new SaharaClient(_portManager, _log, _logDetail, saharaProgress);

                bool saharaOk = await _sahara.HandshakeAndUploadAsync(programmerPath, ct);
                if (!saharaOk)
                {
                    _log("[Qualcomm] Sahara Handshake failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Set flag based on user selected auth mode (No auto-detection)
                IsVipDevice = (authMode.ToLowerInvariant() == "vip" || authMode.ToLowerInvariant() == "oplus");

                // Wait for Firehose ready
                _log("Sending Firehose loader : Success");
                await Task.Delay(1000, ct);

                // Reopen port (Firehose mode)
                _portManager.Close();
                await Task.Delay(500, ct);

                opened = await _portManager.OpenAsync(portName, 5, true, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot reopen port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Firehose Configuration
                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress, _logDetail);

                // Pass chip info
                if (ChipInfo != null)
                {
                    _firehose.ChipSerial = ChipInfo.SerialHex;
                    _firehose.ChipHwId = ChipInfo.HwIdHex;
                    _firehose.ChipPkHash = ChipInfo.PkHash;
                }

                // Perform auth based on user selection (Pre-config auth)
                string authModeLower = authMode.ToLowerInvariant();
                bool preConfigAuth = (authModeLower == "vip" || authModeLower == "oplus" || authModeLower == "xiaomi");

                // Xiaomi device auto auth: Even if user selects none, auto perform Xiaomi auth
                bool isXiaomi = IsXiaomiDevice();
                if (authModeLower == "none" && isXiaomi)
                {
                    _log("[Qualcomm] Xiaomi device detected (SecBoot), auto executing MiAuth...");
                    var xiaomi = new XiaomiAuthStrategy(_log);
                    xiaomi.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                    bool authOk = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    if (authOk)
                        _log("[Qualcomm] Xiaomi Auth success");
                    else
                        _log("[Qualcomm] Xiaomi Auth failed, device might need official authorization");
                }
                else if (preConfigAuth && authModeLower != "none")
                {
                    _log(string.Format("[Qualcomm] Executing {0} Auth (Pre-config)...", authMode.ToUpper()));
                    bool authOk = false;

                    if (authModeLower == "vip" || authModeLower == "oplus")
                    {
                        // VIP Auth must be before configuration
                        if (!string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath))
                        {
                            authOk = await PerformVipAuthManualAsync(digestPath, signaturePath, ct);
                        }
                        else
                        {
                            _log("[Qualcomm] VIP Auth requires Digest and Signature files, falling back to normal mode");
                            // No auth files, fallback to normal mode
                            IsVipDevice = false;
                        }
                    }
                    else if (authModeLower == "xiaomi")
                    {
                        var xiaomi = new XiaomiAuthStrategy(_log);
                        xiaomi.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                        authOk = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    }

                    if (authOk)
                    {
                        _log(string.Format("[Qualcomm] {0} Auth success", authMode.ToUpper()));
                    }
                    else if (IsVipDevice)
                    {
                        // VIP Auth failed but has files, fallback to normal mode
                        _log(string.Format("[Qualcomm] {0} Auth failed, fallback to normal read mode", authMode.ToUpper()));
                        IsVipDevice = false;
                    }
                }

                _log("Configuring Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("Configure Firehose : Failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("Configure Firehose : Success");

                // Post-config Auth (OnePlus)
                if (!preConfigAuth && authModeLower != "none")
                {
                    _log(string.Format("[Qualcomm] Executing {0} Auth (Post-config)...", authMode.ToUpper()));
                    bool authOk = false;

                    if (authModeLower == "oneplus")
                    {
                        var oneplus = new OnePlusAuthStrategy(_log);
                        authOk = await oneplus.AuthenticateAsync(_firehose, programmerPath, ct);
                    }

                    if (authOk)
                        _log(string.Format("[Qualcomm] {0} Auth success", authMode.ToUpper()));
                    else
                        _log(string.Format("[Qualcomm] {0} Auth failed", authMode.ToUpper()));
                }

                // Save connection parameters
                LastPortName = portName;
                LastStorageType = storageType;

                // Register port disconnected event
                if (_portManager != null)
                {
                    _portManager.PortDisconnected += (s, e) => HandlePortDisconnected();
                }

                SetState(QualcommConnectionState.Ready);
                _log("[Qualcomm] Connected successfully");

                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[Qualcomm] Connection canceled");
                SetState(QualcommConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Connection error - {0}", ex.Message));
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// Connect device using embedded Loader data (VIP mode, no auth)
        /// </summary>
        public async Task<bool> ConnectWithLoaderDataAsync(string portName, byte[] loaderData, string storageType = "ufs", CancellationToken ct = default(CancellationToken))
        {
            return await ConnectWithVipAuthAsync(portName, loaderData, "", "", storageType, ct);
        }

        /// <summary>
        /// Connect using embedded Loader data and perform VIP Auth (Using file path)
        /// Important: VIP Auth executed after Loader upload, before Firehose configuration
        /// </summary>
        /// <param name="portName">Port Name</param>
        /// <param name="loaderData">Loader Binary Data</param>
        /// <param name="digestPath">VIP Digest File Path (Optional)</param>
        /// <param name="signaturePath">VIP Signature File Path (Optional)</param>
        /// <param name="storageType">存储类型</param>
        /// <param name="ct">取消令牌</param>
        public async Task<bool> ConnectWithVipAuthAsync(string portName, byte[] loaderData, string digestPath, string signaturePath, string storageType = "ufs", CancellationToken ct = default(CancellationToken))
        {
            try
            {
                SetState(QualcommConnectionState.Connecting);
                _log("[Qualcomm] Connecting using embedded Loader...");
                _log(string.Format("USB Port : {0}", portName));

                if (loaderData == null || loaderData.Length == 0)
                {
                    _log("[Qualcomm] Loader data is empty");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Initialize Serial Port
                _portManager = new SerialPortManager();
                bool opened = await _portManager.OpenAsync(portName, 3, false, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot open port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Sahara Handshake and upload embedded Loader
                SetState(QualcommConnectionState.SaharaMode);
                Action<double> saharaProgress = null;
                if (_progress != null)
                {
                    saharaProgress = percent => _progress((long)percent, 100);
                }
                _sahara = new SaharaClient(_portManager, _log, _logDetail, saharaProgress);

                bool saharaOk = await _sahara.HandshakeAndUploadAsync(loaderData, "VIP_Loader", ct);
                if (!saharaOk)
                {
                    _log("[Qualcomm] Sahara Handshake/Loader upload failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // ChipInfo saved via _sahara.ChipInfo, automatically retrieved
                // Note: IsVipDevice will be set after VIP Auth success

                // Wait for Firehose ready
                _log("Sending Firehose loader : Success");
                await Task.Delay(1000, ct);

                // Reopen port (Firehose mode)
                _portManager.Close();
                await Task.Delay(500, ct);

                opened = await _portManager.OpenAsync(portName, 5, true, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot reopen port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                // Create Firehose Client
                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress, _logDetail);

                // ========== VIP Auth (Critical: Must be executed before Firehose configuration) ==========
                // Send binary data using file path
                bool vipAuthOk = false;
                if (!string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath) &&
                    System.IO.File.Exists(digestPath) && System.IO.File.Exists(signaturePath))
                {
                    var digestInfo = new System.IO.FileInfo(digestPath);
                    var sigInfo = new System.IO.FileInfo(signaturePath);
                    _log(string.Format("[Qualcomm] Executing VIP Auth (Digest={0}B, Sign={1}B)...", digestInfo.Length, sigInfo.Length));

                    // Send using file path
                    vipAuthOk = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, ct);
                    if (!vipAuthOk)
                    {
                        _log("[Qualcomm] VIP Auth failed, falling back to normal mode...");
                        IsVipDevice = false;  // Important: Use normal read mode on auth failure
                    }
                    else
                    {
                        _log("[Qualcomm] VIP Auth success, high privilege mode activated");
                        IsVipDevice = true;
                    }
                }
                else
                {
                    // No auth data provided or file not found, use normal mode
                    if (!string.IsNullOrEmpty(digestPath) || !string.IsNullOrEmpty(signaturePath))
                    {
                        _log("[Qualcomm] VIP Auth file not found, using normal mode");
                    }
                    IsVipDevice = false;
                }

                // Firehose Configuration
                _log("Configuring Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("Configure Firehose : Failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("Configure Firehose : Success");

                // Save connection parameters
                LastPortName = portName;
                LastStorageType = storageType;

                // Register port disconnected event
                if (_portManager != null)
                {
                    _portManager.PortDisconnected += (s, e) => HandlePortDisconnected();
                }

                SetState(QualcommConnectionState.Ready);
                _log("[Qualcomm] VIP Loader connected successfully");

                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[Qualcomm] Connection canceled");
                SetState(QualcommConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Connection error - {0}", ex.Message));
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// Connect Firehose Directly (Skip Sahara)
        /// </summary>
        public async Task<bool> ConnectFirehoseDirectAsync(string portName, string storageType = "ufs", CancellationToken ct = default(CancellationToken))
        {
            try
            {
                SetState(QualcommConnectionState.Connecting);
                _log(string.Format("[Qualcomm] Direct Connect Firehose: {0}...", portName));

                _portManager = new SerialPortManager();
                bool opened = await _portManager.OpenAsync(portName, 3, true, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot open port");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }

                SetState(QualcommConnectionState.FirehoseMode);
                _firehose = new FirehoseClient(_portManager, _log, _progress, _logDetail);

                _log("Configuring Firehose...");
                bool configOk = await _firehose.ConfigureAsync(storageType, 0, ct);
                if (!configOk)
                {
                    _log("Configure Firehose : Failed");
                    SetState(QualcommConnectionState.Error);
                    return false;
                }
                _log("Configure Firehose : Success");

                // 保存连接参数
                LastPortName = portName;
                LastStorageType = storageType;

                // 注册端口断开事件
                if (_portManager != null)
                {
                    _portManager.PortDisconnected += (s, e) => HandlePortDisconnected();
                }

                SetState(QualcommConnectionState.Ready);
                _log("[Qualcomm] Firehose direct connect success");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log("[Qualcomm] Connection canceled");
                SetState(QualcommConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Connection error - {0}", ex.Message));
                SetState(QualcommConnectionState.Error);
                return false;
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            _log("[Qualcomm] Disconnecting");

            if (_portManager != null)
            {
                _portManager.Close();
                _portManager.Dispose();
                _portManager = null;
            }

            if (_sahara != null)
            {
                _sahara.Dispose();
                _sahara = null;
            }

            if (_firehose != null)
            {
                _firehose.Dispose();
                _firehose = null;
            }

            _partitionCache.Clear();
            IsVipDevice = false;
            _portClosed = false;
            _cachedChipInfo = null;

            SetState(QualcommConnectionState.Disconnected);
        }

        /// <summary>
        /// Release Port (Call after operation, keep device object and state)
        /// </summary>
        /// <remarks>
        /// Best practice for EDL tools: Release port after operation, allow other programs to connect.
        /// After calling this method:
        /// - Port closed, serial resource released
        /// - Device object retained (ChipInfo, partition cache etc)
        /// - Port will be auto reopened before next operation
        /// </remarks>
        public void ReleasePort()
        {
            // If keep port open is set, skip release
            if (_keepPortOpen)
            {
                _logDetail("[Qualcomm] Port kept open (Continuous operation mode)");
                return;
            }

            if (_portManager == null || !_portManager.IsOpen)
                return;

            try
            {
                // Cache chip info (Accessible after port closed)
                if (_sahara != null && _sahara.ChipInfo != null)
                {
                    _cachedChipInfo = _sahara.ChipInfo;
                }

                // Close port but do not destroy device object
                _portManager.Close();
                _portClosed = true;

                _logDetail("[Qualcomm] Port released (Device info retained)");
            }
            catch (Exception ex)
            {
                _logDetail("[Qualcomm] Release port exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Ensure port is open (Call before operation)
        /// </summary>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>Is port available</returns>
        public async Task<bool> EnsurePortOpenAsync(CancellationToken ct = default(CancellationToken))
        {
            // If port is open and available, return directly
            if (_portManager != null && _portManager.IsOpen && !_portClosed)
                return true;

            // If no port name recorded, cannot reopen
            if (string.IsNullOrEmpty(LastPortName))
            {
                _log("[Qualcomm] Cannot reopen port: No port name recorded");
                return false;
            }

            // Check if port is available in system
            if (_portManager != null && !_portManager.IsPortAvailable())
            {
                _log("[Qualcomm] Port removed from system, device might be disconnected");
                HandlePortDisconnected();
                return false;
            }

            // Reopen port
            try
            {
                _logDetail(string.Format("[Qualcomm] Reopening port: {0}", LastPortName));

                if (_portManager == null)
                {
                    _portManager = new SerialPortManager();
                }

                bool opened = await _portManager.OpenAsync(LastPortName, 3, true, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot reopen port");
                    return false;
                }

                _portClosed = false;

                // Note: Firehose client retained, no need to recreate
                // If _firehose is null, connection has issues, require full reconnection
                if (_firehose == null)
                {
                    _log("[Qualcomm] Firehose client lost, require full reconnection");
                    return false;
                }

                _logDetail("[Qualcomm] Port reopened successfully");
                return true;
            }
            catch (Exception ex)
            {
                _log("[Qualcomm] Reopen port failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Set if keep port open (For continuous operation, e.g. batch flashing)
        /// </summary>
        /// <param name="keepOpen">Keep Open</param>
        public void SetKeepPortOpen(bool keepOpen)
        {
            _keepPortOpen = keepOpen;
            if (keepOpen)
                _logDetail("[Qualcomm] Setting: Keep port open");
            else
                _logDetail("[Qualcomm] Setting: Allow release port");
        }

        /// <summary>
        /// Get chip info (Accessible even if port closed)
        /// </summary>
        public QualcommChipInfo GetChipInfo()
        {
            if (_sahara != null && _sahara.ChipInfo != null)
                return _sahara.ChipInfo;
            return _cachedChipInfo;
        }

        /// <summary>
        /// Is port released
        /// </summary>
        public bool IsPortReleased { get { return _portClosed; } }

        /// <summary>
        /// Reset stuck Sahara state
        /// Used when device is stuck in Sahara mode due to other software or boot errors
        /// </summary>
        /// <param name="portName">Port Name</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>Is success</returns>
        public async Task<bool> ResetSaharaAsync(string portName, CancellationToken ct = default(CancellationToken))
        {
            _log("[Qualcomm] Attempting to reset stuck Sahara state...");

            try
            {
                // Ensure previous connection closed
                Disconnect();
                await Task.Delay(200, ct);

                // Open port
                _portManager = new SerialPortManager();
                bool opened = await _portManager.OpenAsync(portName, 3, true, ct);
                if (!opened)
                {
                    _log("[Qualcomm] Cannot open port");
                    return false;
                }

                // Create temporary Sahara Client
                _sahara = new SaharaClient(_portManager, _log, _logDetail, null);

                // Attempt reset
                bool success = await _sahara.TryResetSaharaAsync(ct);

                if (success)
                {
                    _log("[Qualcomm] ✓ Sahara state reset");
                    _log("[Qualcomm] Device ready, please click [Connect] to reconnect");

                    // Disconnect after reset success, allow user to reconnect
                    // Save port name for later connection
                    string savedPortName = portName;

                    // Close current connection (Release port resources)
                    if (_portManager != null)
                    {
                        _portManager.Close();
                        _portManager.Dispose();
                        _portManager = null;
                    }
                    if (_sahara != null)
                    {
                        _sahara.Dispose();
                        _sahara = null;
                    }

                    // Set to disconnected state, wait for user reconnection
                    SetState(QualcommConnectionState.Disconnected);
                    LastPortName = savedPortName;  // Save port name
                }
                else
                {
                    _log("[Qualcomm] ❌ Cannot reset Sahara, please try power cycle device");
                    // Close connection
                    Disconnect();
                }

                return success;
            }
            catch (Exception ex)
            {
                _log("[Qualcomm] Reset Sahara exception: " + ex.Message);
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Hard Reset Device (Full Reboot)
        /// </summary>
        /// <param name="portName">Port Name</param>
        /// <param name="ct">Cancellation Token</param>
        public async Task<bool> HardResetDeviceAsync(string portName, CancellationToken ct = default(CancellationToken))
        {
            _log("[Qualcomm] Sending Hard Reset command...");

            try
            {
                // If Firehose connected, reset via Firehose
                if (_firehose != null && State == QualcommConnectionState.Ready)
                {
                    bool ok = await _firehose.ResetAsync("reset", ct);
                    Disconnect();
                    return ok;
                }

                // Otherwise try reset via Sahara
                if (_portManager == null || !_portManager.IsOpen)
                {
                    _portManager = new SerialPortManager();
                    await _portManager.OpenAsync(portName, 3, true, ct);
                }

                if (_sahara == null)
                {
                    _sahara = new SaharaClient(_portManager, _log, _logDetail, null);
                }

                _sahara.SendHardReset();
                _log("[Qualcomm] Hard Reset command sent, device will reboot");

                await Task.Delay(500, ct);
                Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                _log("[Qualcomm] Hard Reset exception: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Perform Auth
        /// </summary>
        public async Task<bool> AuthenticateAsync(string authMode, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Firehose not connected, cannot perform auth");
                return false;
            }

            try
            {
                switch (authMode.ToLowerInvariant())
                {
                    case "oneplus":
                        _log("[Qualcomm] Executing OnePlus Auth...");
                        var oneplusAuth = new Authentication.OnePlusAuthStrategy();
                        // OnePlus Auth does not need external file, use empty string
                        return await oneplusAuth.AuthenticateAsync(_firehose, "", ct);

                    case "vip":
                    case "oplus":
                        _log("[Qualcomm] Executing VIP/OPPO Auth...");
                        // VIP Auth usually needs signature file, using default path
                        string vipDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vip");
                        string digestPath = System.IO.Path.Combine(vipDir, "digest.bin");
                        string signaturePath = System.IO.Path.Combine(vipDir, "signature.bin");
                        if (!System.IO.File.Exists(digestPath) || !System.IO.File.Exists(signaturePath))
                        {
                            _log("[Qualcomm] VIP Auth file not found, attempting no-signature auth...");
                            // If no signature file, return true to continue (Some devices may not need auth)
                            return true;
                        }
                        bool ok = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, ct);
                        if (ok) IsVipDevice = true;
                        return ok;

                    case "xiaomi":
                        _log("[Qualcomm] Executing Xiaomi Auth...");
                        var xiaomiAuth = new Authentication.XiaomiAuthStrategy(_log);
                        xiaomiAuth.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                        return await xiaomiAuth.AuthenticateAsync(_firehose, "", ct);

                    default:
                        _log(string.Format("[Qualcomm] Unknown Auth Mode: {0}", authMode));
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Auth failed: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Perform OnePlus Auth
        /// </summary>
        public async Task<bool> PerformOnePlusAuthAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Firehose not connected, cannot perform OnePlus Auth");
                return false;
            }

            try
            {
                _log("[Qualcomm] Executing OnePlus Auth...");
                var oneplusAuth = new Authentication.OnePlusAuthStrategy(_log);
                bool ok = await oneplusAuth.AuthenticateAsync(_firehose, "", ct);
                if (ok)
                    _log("[Qualcomm] OnePlus Auth success");
                else
                    _log("[Qualcomm] OnePlus Auth failed");
                return ok;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] OnePlus Auth exception: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Perform Xiaomi Auth
        /// </summary>
        public async Task<bool> PerformXiaomiAuthAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Firehose not connected, cannot perform Xiaomi Auth");
                return false;
            }

            try
            {
                _log("[Qualcomm] Executing Xiaomi Auth...");
                var xiaomiAuth = new Authentication.XiaomiAuthStrategy(_log);
                xiaomiAuth.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                bool ok = await xiaomiAuth.AuthenticateAsync(_firehose, "", ct);
                if (ok)
                    _log("[Qualcomm] Xiaomi Auth success");
                else
                    _log("[Qualcomm] Xiaomi Auth failed");
                return ok;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Xiaomi Auth exception: {0}", ex.Message));
                return false;
            }
        }

        private void SetState(QualcommConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                if (StateChanged != null)
                    StateChanged(this, newState);
            }
        }

        #endregion

        #region Auto Auth Logic

        /// <summary>
        /// Auto Auth - Only executed for Xiaomi devices
        /// Other devices (OnePlus/OPPO/Realme etc) selected by user manually
        /// </summary>
        private async Task<bool> AutoAuthenticateAsync(string programmerPath, CancellationToken ct)
        {
            if (_firehose == null) return true;

            // Only Xiaomi devices auto auth
            if (IsXiaomiDevice())
            {
                _log("[Qualcomm] Xiaomi device detected, auto executing MiAuth...");
                try
                {
                    var xiaomi = new XiaomiAuthStrategy(_log);
                    xiaomi.OnAuthTokenRequired += token => XiaomiAuthTokenRequired?.Invoke(token);
                    bool result = await xiaomi.AuthenticateAsync(_firehose, programmerPath, ct);
                    if (result)
                    {
                        _log("[Qualcomm] Xiaomi Auth success");
                    }
                    else
                    {
                        _log("[Qualcomm] Xiaomi Auth failed, device might need official authorization");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _log(string.Format("[Qualcomm] Xiaomi Auth exception: {0}", ex.Message));
                    return false;
                }
            }

            // Other devices no auto auth, manual selection by user
            return true;
        }

        /// <summary>
        /// Check if Xiaomi device (Via OEM ID or other features)
        /// </summary>
        public bool IsXiaomiDevice()
        {
            if (ChipInfo == null) return false;

            // Check via OEM ID (0x0072 = Xiaomi official)
            if (ChipInfo.OemId == 0x0072) return true;

            // Check via PK Hash prefix (Common Xiaomi PK Hash)
            if (!string.IsNullOrEmpty(ChipInfo.PkHash))
            {
                string pkLower = ChipInfo.PkHash.ToLowerInvariant();
                // Xiaomi device PK Hash prefix list (Continuously updated)
                string[] xiaomiPkHashPrefixes = new[]
                {
                    "c924a35f",  // Common Xiaomi devices
                    "3373d5c8",
                    "e07be28b",
                    "6f5c4e17",
                    "57158eaf",
                    "355d47f9",
                    "a7b8b825",
                    "1c845b80",
                    "58b4add1",
                    "dd0cba2f",
                    "1bebe386"
                };

                foreach (var prefix in xiaomiPkHashPrefixes)
                {
                    if (pkLower.StartsWith(prefix))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Manually execute OPLUS VIP Auth (Based on Digest and Signature)
        /// </summary>
        public async Task<bool> PerformVipAuthManualAsync(string digestPath, string signaturePath, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Device not connected");
                return false;
            }

            _log("[Qualcomm] Starting OPLUS VIP Auth (Digest + Sign)...");
            try
            {
                bool result = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, ct);
                if (result)
                {
                    _log("[Qualcomm] VIP Auth success, high privilege mode activated");
                    IsVipDevice = true;
                }
                else
                {
                    _log("[Qualcomm] VIP Auth failed: Verification failed");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] VIP Auth exception: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Manually execute OPLUS VIP Auth (Based on byte[] data)
        /// Supports writing signature data directly after sending Digest
        /// </summary>
        /// <param name="digestData">Digest Data (Hash Segment, ~20-30KB)</param>
        /// <param name="signatureData">Signature Data (256 Bytes RSA-2048)</param>
        public async Task<bool> PerformVipAuthAsync(byte[] digestData, byte[] signatureData, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Device not connected");
                return false;
            }

            _log(string.Format("[Qualcomm] Starting VIP Auth (Digest={0}B, Sign={1}B)...",
                digestData?.Length ?? 0, signatureData?.Length ?? 0));
            try
            {
                bool result = await _firehose.PerformVipAuthAsync(digestData, signatureData, ct);
                if (result)
                {
                    _log("[Qualcomm] VIP Auth success, high privilege mode activated");
                    IsVipDevice = true;
                }
                else
                {
                    _log("[Qualcomm] VIP Auth failed: Verification failed");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] VIP Auth exception: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Step-by-step VIP Auth - Step 1: Send Digest
        /// </summary>
        public async Task<bool> SendVipDigestAsync(byte[] digestData, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;
            return await _firehose.SendVipDigestAsync(digestData, ct);
        }

        /// <summary>
        /// Step-by-step VIP Auth - Step 2-3: Prepare VIP Mode
        /// </summary>
        public async Task<bool> PrepareVipModeAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;
            return await _firehose.PrepareVipModeAsync(ct);
        }

        /// <summary>
        /// Step-by-step VIP Auth - Step 4: Send Signature (256 Bytes)
        /// Core method: Write signature after sending Digest
        /// </summary>
        public async Task<bool> SendVipSignatureAsync(byte[] signatureData, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;
            return await _firehose.SendVipSignatureAsync(signatureData, ct);
        }

        /// <summary>
        /// Step-by-step VIP Auth - Step 5: Finalize Auth
        /// </summary>
        public async Task<bool> FinalizeVipAuthAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;
            return await _firehose.FinalizeVipAuthAsync(ct);
        }

        /// <summary>
        /// Perform VIP Auth using embedded Chimera signature data
        /// </summary>
        /// <param name="platform">Platform Code (e.g. SM8550, SM8650 etc)</param>
        public async Task<bool> PerformChimeraAuthAsync(string platform, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Device not connected");
                return false;
            }

            // Get signature data from embedded database
            var signData = ChimeraSignDatabase.Get(platform);
            if (signData == null)
            {
                _log(string.Format("[Qualcomm] Unsupported platform: {0}", platform));
                _log("[Qualcomm] Supported platforms: " + string.Join(", ", ChimeraSignDatabase.GetSupportedPlatforms()));
                return false;
            }

            _log(string.Format("[Qualcomm] Using Chimera signature: {0} ({1})", signData.Name, signData.Platform));
            _log(string.Format("[Qualcomm] Digest: {0} Bytes, Signature: {1} Bytes",
                signData.DigestSize, signData.SignatureSize));

            return await PerformVipAuthAsync(signData.Digest, signData.Signature, ct);
        }

        /// <summary>
        /// Auto detect platform and use Chimera signature auth
        /// </summary>
        public async Task<bool> PerformChimeraAuthAutoAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
            {
                _log("[Qualcomm] Device not connected");
                return false;
            }

            // Try to get chip info from Sahara
            string platform = null;
            if (_sahara != null && _sahara.ChipInfo != null)
            {
                platform = _sahara.ChipInfo.ChipName;
                if (string.IsNullOrEmpty(platform) || platform == "Unknown")
                {
                    // Try to infer from MSM ID
                    uint msmId = _sahara.ChipInfo.MsmId;
                    platform = QualcommDatabase.GetChipName(msmId);
                }
            }

            if (string.IsNullOrEmpty(platform) || platform == "Unknown")
            {
                _log("[Qualcomm] Cannot auto detect platform, please specify manually");
                _log("[Qualcomm] Supported platforms: " + string.Join(", ", ChimeraSignDatabase.GetSupportedPlatforms()));
                return false;
            }

            _log(string.Format("[Qualcomm] Auto detected platform: {0}", platform));
            return await PerformChimeraAuthAsync(platform, ct);
        }

        /// <summary>
        /// Get supported Chimera platform list
        /// </summary>
        public string[] GetSupportedChimeraPlatforms()
        {
            return ChimeraSignDatabase.GetSupportedPlatforms();
        }

        /// <summary>
        /// Check if platform supports Chimera signature
        /// </summary>
        public bool IsChimeraSupported(string platform)
        {
            return ChimeraSignDatabase.IsSupported(platform);
        }

        /// <summary>
        /// Get device challenge (For online signing)
        /// </summary>
        public async Task<string> GetVipChallengeAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return null;
            return await _firehose.GetVipChallengeAsync(ct);
        }

        #endregion

        #region Partition Operations

        /// <summary>
        /// Read GPT partition table of all LUNs
        /// </summary>
        public async Task<List<PartitionInfo>> ReadAllGptAsync(int maxLuns = 6, CancellationToken ct = default(CancellationToken))
        {
            return await ReadAllGptAsync(maxLuns, null, null, ct);
        }

        /// <summary>
        /// Read GPT partition table of all LUNs (With progress callback)
        /// </summary>
        /// <param name="maxLuns">Max LUN Count</param>
        /// <param name="totalProgress">Total Progress Callback (Current LUN, Total LUN)</param>
        /// <param name="subProgress">Sub Progress Callback (0-100)</param>
        /// <param name="ct">Cancellation Token</param>
        public async Task<List<PartitionInfo>> ReadAllGptAsync(
            int maxLuns,
            IProgress<Tuple<int, int>> totalProgress,
            IProgress<double> subProgress,
            CancellationToken ct = default(CancellationToken))
        {
            var allPartitions = new List<PartitionInfo>();

            if (_firehose == null)
                return allPartitions;

            _logDetail("Reading GUID partition table...");

            // Report start
            if (totalProgress != null) totalProgress.Report(Tuple.Create(0, maxLuns));
            if (subProgress != null) subProgress.Report(0);

            // LUN progress callback - Realtime update progress
            var lunProgress = new Progress<int>(lun =>
            {
                if (totalProgress != null) totalProgress.Report(Tuple.Create(lun, maxLuns));
                if (subProgress != null) subProgress.Report(100.0 * lun / maxLuns);
            });

            var partitions = await _firehose.ReadGptPartitionsAsync(IsVipDevice, ct, lunProgress);

            // Report intermediate progress
            if (subProgress != null) subProgress.Report(80);

            if (partitions != null && partitions.Count > 0)
            {
                allPartitions.AddRange(partitions);
                _log(string.Format("Read GUID partition table : Success [{0}]", partitions.Count));

                // Cache partitions
                _partitionCache.Clear();
                foreach (var p in partitions)
                {
                    if (!_partitionCache.ContainsKey(p.Lun))
                        _partitionCache[p.Lun] = new List<PartitionInfo>();
                    _partitionCache[p.Lun].Add(p);
                }
            }

            // Report completion
            if (subProgress != null) subProgress.Report(100);
            if (totalProgress != null) totalProgress.Report(Tuple.Create(maxLuns, maxLuns));

            _log(string.Format("[Qualcomm] Found {0} partitions in total", allPartitions.Count));
            return allPartitions;
        }

        /// <summary>
        /// Get partition list of specified LUN
        /// </summary>
        public List<PartitionInfo> GetCachedPartitions(int lun = -1)
        {
            var result = new List<PartitionInfo>();

            if (lun == -1)
            {
                foreach (var kv in _partitionCache)
                    result.AddRange(kv.Value);
            }
            else
            {
                List<PartitionInfo> list;
                if (_partitionCache.TryGetValue(lun, out list))
                    result.AddRange(list);
            }

            return result;
        }

        /// <summary>
        /// Find Partition
        /// </summary>
        public PartitionInfo FindPartition(string name)
        {
            foreach (var kv in _partitionCache)
            {
                foreach (var p in kv.Value)
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            return null;
        }

        /// <summary>
        /// Read Partition to File
        /// </summary>
        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[Qualcomm] Partition not found " + partitionName);
                return false;
            }

            _log(string.Format("[Qualcomm] Reading partition {0} ({1})", partitionName, partition.FormattedSize));

            try
            {
                int sectorsPerChunk = _firehose.MaxPayloadSize / partition.SectorSize;
                long totalSectors = partition.NumSectors;
                long readSectors = 0;
                long totalBytes = partition.Size;
                long readBytes = 0;

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4 * 1024 * 1024))
                {
                    while (readSectors < totalSectors && !ct.IsCancellationRequested)
                    {
                        int toRead = (int)Math.Min(sectorsPerChunk, totalSectors - readSectors);
                        byte[] data = await _firehose.ReadSectorsAsync(
                            partition.Lun, partition.StartSector + readSectors, toRead, ct, IsVipDevice, partitionName);

                        if (data == null)
                        {
                            _log("[Qualcomm] Read failed");
                            return false;
                        }

                        fs.Write(data, 0, data.Length);
                        readSectors += toRead;
                        readBytes += data.Length;

                        // Call byte-level progress callback (For speed calculation)
                        _firehose.ReportProgress(readBytes, totalBytes);

                        // Percentage progress (Use double)
                        if (progress != null)
                            progress.Report(100.0 * readBytes / totalBytes);
                    }
                }

                _log(string.Format("[Qualcomm] Partition {0} saved to {1}", partitionName, outputPath));
                return true;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Qualcomm] Read error - {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Write Partition
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, string filePath, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[Qualcomm] Partition not found " + partitionName);
                return false;
            }

            // Some OPLUS partitions need SHA256 checksum wrapping
            bool useSha256 = IsOplusDevice && (partitionName.ToLower() == "xbl" || partitionName.ToLower() == "abl" || partitionName.ToLower() == "imagefv");
            if (useSha256) await _firehose.Sha256InitAsync(ct);

            // VIP device use masquerade mode for writing
            bool success = await _firehose.FlashPartitionFromFileAsync(
                partitionName, filePath, partition.Lun, partition.StartSector, progress, ct, IsVipDevice);

            if (useSha256) await _firehose.Sha256FinalAsync(ct);

            return success;
        }

        private bool IsOplusDevice
        {
            get
            {
                if (IsVipDevice) return true;
                if (ChipInfo != null && (ChipInfo.Vendor == "OPPO" || ChipInfo.Vendor == "Realme" || ChipInfo.Vendor == "OnePlus")) return true;
                return false;
            }
        }

        /// <summary>
        /// Direct write to specified LUN and StartSector (For special partitions like PrimaryGPT/BackupGPT)
        /// Supports official NUM_DISK_SECTORS-N negative sector format
        /// </summary>
        public async Task<bool> WriteDirectAsync(string label, string filePath, int lun, long startSector, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            // Negative sector uses official format directly sent to device (No dependency on client GPT cache)
            if (startSector < 0)
            {
                _logDetail(string.Format("[Qualcomm] Write: {0} -> LUN{1} @ NUM_DISK_SECTORS{2}", label, lun, startSector));

                // Use official NUM_DISK_SECTORS-N format, let device calculate absolute address
                return await _firehose.FlashPartitionWithNegativeSectorAsync(
                    label, filePath, lun, startSector, progress, ct);
            }
            else
            {
                _logDetail(string.Format("[Qualcomm] Write: {0} -> LUN{1} @ sector {2}", label, lun, startSector));

                // Positive sector normal write
                return await _firehose.FlashPartitionFromFileAsync(
                    label, filePath, lun, startSector, progress, ct, IsVipDevice);
            }
        }

        /// <summary>
        /// Erase Partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[Qualcomm] Partition not found " + partitionName);
                return false;
            }

            // VIP device use masquerade mode for erasing
            return await _firehose.ErasePartitionAsync(partition, ct, IsVipDevice);
        }

        /// <summary>
        /// Read partition data at specified offset
        /// </summary>
        /// <param name="partitionName">Partition Name</param>
        /// <param name="offset">Offset (Bytes)</param>
        /// <param name="size">Size (Bytes)</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>Read Data</returns>
        public async Task<byte[]> ReadPartitionDataAsync(string partitionName, long offset, int size, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return null;

            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                _log("[Qualcomm] Partition not found " + partitionName);
                return null;
            }

            // Calculate sector position
            int sectorSize = SectorSize > 0 ? SectorSize : 4096;
            long startSector = partition.StartSector + (offset / sectorSize);
            int numSectors = (size + sectorSize - 1) / sectorSize;

            // Only use VIP mode for reading after VIP auth success
            // IsVipDevice = true means VIP auth was successful
            // IsOplusDevice only used for SHA256 checksum determination, not for read mode
            bool useVipMode = IsVipDevice;

            // Read data
            byte[] data = await _firehose.ReadSectorsAsync(partition.Lun, startSector, numSectors, ct, useVipMode, partitionName);
            if (data == null) return null;

            // If offset alignment issue, extract correct data
            int offsetInSector = (int)(offset % sectorSize);
            if (offsetInSector > 0 || data.Length > size)
            {
                int actualSize = Math.Min(size, data.Length - offsetInSector);
                if (actualSize <= 0) return null;

                byte[] result = new byte[actualSize];
                Array.Copy(data, offsetInSector, result, 0, actualSize);
                return result;
            }

            return data;
        }

        /// <summary>
        /// Get Firehose Client (For internal use)
        /// </summary>
        internal Protocol.FirehoseClient GetFirehoseClient()
        {
            return _firehose;
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reboot Device
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.ResetAsync("reset", ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// Power Off
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.PowerOffAsync(ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// Reboot to EDL Mode
        /// </summary>
        public async Task<bool> RebootToEdlAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            bool result = await _firehose.RebootToEdlAsync(ct);
            if (result)
                Disconnect();

            return result;
        }

        /// <summary>
        /// Set Active Slot
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(string slot, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.SetActiveSlotAsync(slot, ct);
        }

        /// <summary>
        /// Fix GPT
        /// </summary>
        public async Task<bool> FixGptAsync(int lun = -1, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.FixGptAsync(lun, true, ct);
        }

        /// <summary>
        /// Set Boot LUN
        /// </summary>
        public async Task<bool> SetBootLunAsync(int lun, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.SetBootLunAsync(lun, ct);
        }

        /// <summary>
        /// Ping Test Connection
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            return await _firehose.PingAsync(ct);
        }

        /// <summary>
        /// Apply Patch XML File
        /// </summary>
        public async Task<int> ApplyPatchXmlAsync(string patchXmlPath, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return 0;

            return await _firehose.ApplyPatchXmlAsync(patchXmlPath, ct);
        }

        /// <summary>
        /// Apply Multiple Patch XML Files
        /// </summary>
        public async Task<int> ApplyPatchFilesAsync(IEnumerable<string> patchFiles, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return 0;

            int totalPatches = 0;
            foreach (var patchFile in patchFiles)
            {
                if (ct.IsCancellationRequested) break;
                totalPatches += await _firehose.ApplyPatchXmlAsync(patchFile, ct);
            }
            return totalPatches;
        }

        #endregion

        #region Batch Flashing

        /// <summary>
        /// Batch Flash Partitions
        /// </summary>
        public async Task<bool> FlashMultipleAsync(IEnumerable<FlashPartitionInfo> partitions, IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null)
                return false;

            var list = new List<FlashPartitionInfo>(partitions);
            int total = list.Count;
            int current = 0;
            bool allSuccess = true;

            foreach (var p in list)
            {
                if (ct.IsCancellationRequested)
                    break;

                _log(string.Format("[Qualcomm] Flashing [{0}/{1}] {2}", current + 1, total, p.Name));

                bool ok = await WritePartitionAsync(p.Name, p.Filename, null, ct);
                if (!ok)
                {
                    allSuccess = false;
                    _log("[Qualcomm] Flash failed - " + p.Name);
                }

                current++;
                if (progress != null)
                    progress.Report(100.0 * current / total);
            }

            return allSuccess;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
                }
                _disposed = true;
            }
        }

        ~QualcommService()
        {
            Dispose(false);
        }

        #endregion
        /// <summary>
        /// Flash OPLUS firmware Super logical partition (Decompose and write)
        /// </summary>
        public async Task<bool> FlashOplusSuperAsync(string firmwareRoot, string nvId = "", IProgress<double> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (_firehose == null) return false;

            // 1. Find super partition info
            var superPart = FindPartition("super");
            if (superPart == null)
            {
                _log("[Qualcomm] Super partition not found on device");
                return false;
            }

            // 2. Prepare tasks
            _log("[Qualcomm] Parsing OPLUS firmware Super layout...");
            string activeSlot = CurrentSlot;
            if (activeSlot == "nonexistent" || string.IsNullOrEmpty(activeSlot))
                activeSlot = "a";

            var tasks = await _oplusSuperManager.PrepareSuperTasksAsync(firmwareRoot, superPart.StartSector, (int)superPart.SectorSize, activeSlot, nvId);

            if (tasks.Count == 0)
            {
                _log("[Qualcomm] No available Super logical partition images found");
                return false;
            }

            // 3. Execute tasks
            long totalBytes = tasks.Sum(t => t.SizeInBytes);
            long totalWritten = 0;

            _log(string.Format("[Qualcomm] Start decomposing and writing {0} logical images (Total expanded size: {1} MB)...", tasks.Count, totalBytes / 1024 / 1024));

            foreach (var task in tasks)
            {
                if (ct.IsCancellationRequested) break;

                _log(string.Format("[Qualcomm] Writing {0} [{1}] to physical sector {2}...", task.PartitionName, Path.GetFileName(task.FilePath), task.PhysicalSector));

                // Nested progress calculation
                var taskProgress = new Progress<double>(p =>
                {
                    if (progress != null)
                    {
                        double currentTaskWeight = (double)task.SizeInBytes / totalBytes;
                        double overallPercent = ((double)totalWritten / totalBytes * 100) + (p * currentTaskWeight);
                        progress.Report(overallPercent);
                    }
                });

                bool success = await _firehose.FlashPartitionFromFileAsync(
                    task.PartitionName,
                    task.FilePath,
                    superPart.Lun,
                    task.PhysicalSector,
                    taskProgress,
                    ct,
                    IsVipDevice);

                if (!success)
                {
                    _log(string.Format("[Qualcomm] Write {0} failed, process aborted", task.PartitionName));
                    return false;
                }

                totalWritten += task.SizeInBytes;
            }

            _log("[Qualcomm] OPLUS Super decompose and write completed");
            return true;
        }
    }
}
