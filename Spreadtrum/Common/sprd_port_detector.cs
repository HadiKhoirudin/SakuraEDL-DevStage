// LoveAlways - Spreadtrum Device Port Detection
// Spreadtrum/Unisoc USB Port Detector
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Spreadtrum.Protocol;
using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Spreadtrum.Common
{
    /// <summary>
    /// Spreadtrum Device Port Detector
    /// </summary>
    public class SprdPortDetector : IDisposable
    {
        private ManagementEventWatcher _deviceWatcher;
        private readonly object _lock = new object();
        private bool _isWatching = false;

        // Debounce control
        private DateTime _lastEventTime = DateTime.MinValue;
        private const int DebounceMs = 1000; // 1 second debounce
        private bool _isProcessingEvent = false;

        // Events
        public event Action<SprdDeviceInfo> OnDeviceConnected;
        public event Action<SprdDeviceInfo> OnDeviceDisconnected;
        public event Action<string> OnLog;

        // Known device list
        private readonly List<SprdDeviceInfo> _connectedDevices = new List<SprdDeviceInfo>();

        /// <summary>
        /// List of connected devices
        /// </summary>
        public IReadOnlyList<SprdDeviceInfo> ConnectedDevices
        {
            get
            {
                lock (_lock)
                {
                    return _connectedDevices.ToArray();
                }
            }
        }

        /// <summary>
        /// Start watching for device insertion/removal
        /// </summary>
        public void StartWatching()
        {
            if (_isWatching)
                return;

            try
            {
                // Initial scan and update device list
                var devices = ScanDevices(silent: false);
                UpdateDeviceList(devices);

                // Watch for device changes
                var query = new WqlEventQuery(
                    "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");

                _deviceWatcher = new ManagementEventWatcher(query);
                _deviceWatcher.EventArrived += OnDeviceChanged;
                _deviceWatcher.Start();

                _isWatching = true;
                Log("[Port Detector] Started watching for device changes");
            }
            catch (Exception ex)
            {
                Log("[Port Detector] Failed to start watching: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Stop watching
        /// </summary>
        public void StopWatching()
        {
            if (!_isWatching)
                return;

            try
            {
                _deviceWatcher?.Stop();
                _deviceWatcher?.Dispose();
                _deviceWatcher = null;
                _isWatching = false;
                Log("[Port Detector] Stopped watching");
            }
            catch { }
        }

        /// <summary>
        /// Scan currently connected devices
        /// </summary>
        public List<SprdDeviceInfo> ScanDevices(bool silent = false)
        {
            var devices = new List<SprdDeviceInfo>();

            try
            {
                // Search for COM devices
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Status='OK'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            string name = obj["Name"]?.ToString() ?? "";
                            string deviceId = obj["DeviceID"]?.ToString() ?? "";
                            var hardwareIds = obj["HardwareID"] as string[];

                            // Check if it's a Spreadtrum device
                            if (IsSprdDevice(name, deviceId, hardwareIds))
                            {
                                string comPort = ExtractComPort(name);
                                if (!string.IsNullOrEmpty(comPort))
                                {
                                    var info = ParseDeviceInfo(name, deviceId, hardwareIds, comPort);
                                    if (info != null)
                                    {
                                        devices.Add(info);
                                        if (!silent)
                                        {
                                            Log("[Port Detector] Found device: {0} ({1})", info.Name, info.ComPort);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[Port Detector] Failed to scan devices: {0}", ex.Message);
            }

            return devices;
        }

        /// <summary>
        /// Scan all COM port devices (for debugging)
        /// </summary>
        public List<ComPortInfo> ScanAllComPorts()
        {
            var ports = new List<ComPortInfo>();

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Status='OK'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            string name = obj["Name"]?.ToString() ?? "";

                            // Only handle devices with COM ports
                            string comPort = ExtractComPort(name);
                            if (string.IsNullOrEmpty(comPort))
                                continue;

                            string deviceId = obj["DeviceID"]?.ToString() ?? "";
                            var hardwareIds = obj["HardwareID"] as string[];
                            string hwIdStr = hardwareIds != null && hardwareIds.Length > 0 ? hardwareIds[0] : "";

                            // Parse VID/PID
                            int vid = 0, pid = 0;
                            var vidMatch = Regex.Match(hwIdStr, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                            if (vidMatch.Success)
                                vid = Convert.ToInt32(vidMatch.Groups[1].Value, 16);
                            var pidMatch = Regex.Match(hwIdStr, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                            if (pidMatch.Success)
                                pid = Convert.ToInt32(pidMatch.Groups[1].Value, 16);

                            // Determine if identified as Spreadtrum
                            bool isSprd = IsSprdDevice(name, deviceId, hardwareIds);

                            ports.Add(new ComPortInfo
                            {
                                ComPort = comPort,
                                Name = name,
                                DeviceId = deviceId,
                                HardwareId = hwIdStr,
                                Vid = vid,
                                Pid = pid,
                                IsSprdDetected = isSprd
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return ports;
        }

        /// <summary>
        /// Print all COM port device info (for debugging)
        /// </summary>
        public void PrintAllComPorts()
        {
            Log("[Device Manager] Scanning all COM ports...");
            var ports = ScanAllComPorts();

            if (ports.Count == 0)
            {
                Log("[Device Manager] No COM ports found");
                return;
            }

            Log("[Device Manager] Found {0} COM ports:", ports.Count);
            foreach (var port in ports)
            {
                string sprdFlag = port.IsSprdDetected ? " [Spreadtrum]" : "";
                Log("  {0}: VID={1:X4} PID={2:X4}{3}", port.ComPort, port.Vid, port.Pid, sprdFlag);
                Log("    Name: {0}", port.Name);
                Log("    HW ID: {0}", port.HardwareId);
            }
        }

        /// <summary>
        /// Update internal device list
        /// </summary>
        private void UpdateDeviceList(List<SprdDeviceInfo> devices)
        {
            lock (_lock)
            {
                _connectedDevices.Clear();
                _connectedDevices.AddRange(devices);
            }
        }

        /// <summary>
        /// Wait for device connection
        /// </summary>
        public async Task<SprdDeviceInfo> WaitForDeviceAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            Log("[Port Detector] Waiting for device connection...");

            var startTime = DateTime.Now;
            var previousDevices = new HashSet<string>();

            // Record current devices (silent mode)
            var initialDevices = ScanDevices(silent: true);
            UpdateDeviceList(initialDevices);
            foreach (var dev in initialDevices)
            {
                previousDevices.Add(dev.ComPort);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                {
                    Log("[Port Detector] Wait timed out");
                    return null;
                }

                await Task.Delay(500, cancellationToken);

                var currentDevices = ScanDevices(silent: true);
                UpdateDeviceList(currentDevices);

                foreach (var dev in currentDevices)
                {
                    if (!previousDevices.Contains(dev.ComPort))
                    {
                        Log("[Port Detector] New device connected: {0}", dev.ComPort);
                        return dev;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if it's a Spreadtrum device - dual verification (VID + Device Name)
        /// </summary>
        private bool IsSprdDevice(string name, string deviceId, string[] hardwareIds)
        {
            string nameUpper = name.ToUpper();
            string deviceIdUpper = deviceId.ToUpper();
            string hwIdStr = hardwareIds != null && hardwareIds.Length > 0 ? hardwareIds[0].ToUpper() : "";

            // ========== Step 1: Hard exclude other platforms ==========

            // Exclude MTK devices (VID 0x0E8D)
            if (deviceIdUpper.Contains("VID_0E8D") || hwIdStr.Contains("VID_0E8D"))
                return false;

            // Exclude MTK name keywords
            string[] mtkKeywords = { "MEDIATEK", "MTK", "PRELOADER", "DA USB" };
            foreach (var kw in mtkKeywords)
            {
                if (nameUpper.Contains(kw))
                    return false;
            }

            // Exclude Qualcomm devices (VID 0x05C6)
            if (deviceIdUpper.Contains("VID_05C6") || hwIdStr.Contains("VID_05C6"))
                return false;

            // Exclude Qualcomm name keywords (but keep possible Sprd DIAG)
            string[] qcKeywords = { "QUALCOMM", "QDL", "QHSUSB", "QDLOADER", "9008", "EDL" };
            foreach (var kw in qcKeywords)
            {
                if (nameUpper.Contains(kw))
                    return false;
            }

            // Exclude ADB/Fastboot (but allow Sprd ADB)
            if ((nameUpper.Contains("ADB") || nameUpper.Contains("ANDROID DEBUG")) &&
                !nameUpper.Contains("SPRD") && !nameUpper.Contains("UNISOC"))
                return false;

            // ========== Step 2: Spreadtrum VID detection ==========

            // Spreadtrum specific VID (0x1782)
            bool hasSprdVid = deviceIdUpper.Contains("VID_1782") || hwIdStr.Contains("VID_1782");

            // VID_1782 = Confirmed Spreadtrum
            if (hasSprdVid)
                return true;

            // ========== Step 3: Device name keyword detection ==========

            // Sprd specific name keywords (extended list)
            string[] sprdKeywords = {
                "SPRD", "SPREADTRUM", "UNISOC",
                "U2S DIAG", "U2S_DIAG", "SCI USB2SERIAL",
                "SPRD U2S", "UNISOC U2S",
                "USB2SERIAL", "SPRD SERIAL",
                "DOWNLOAD", "BROM",  // Download mode keywords
                "SC9", "SC8", "SC7",  // Chip model prefixes
                "UMS", "UDX", "UWS",  // New platform prefixes
                "T606", "T610", "T616", "T618", "T700", "T760", "T770"  // Common chips
            };

            foreach (var kw in sprdKeywords)
            {
                if (nameUpper.Contains(kw))
                    return true;
            }

            // ========== Step 4: Check specific vendor combinations (VID + PID) ==========

            // Samsung SPRD (VID_04E8 + specific PIDs)
            if (deviceIdUpper.Contains("VID_04E8"))
            {
                string[] samsungSprdPids = { "PID_685D", "PID_6860", "PID_6862", "PID_685C" };
                foreach (var pid in samsungSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // ZTE SPRD (VID_19D2 + specific PIDs)
            if (deviceIdUpper.Contains("VID_19D2"))
            {
                string[] zteSprdPids = { "PID_0016", "PID_0117", "PID_0076", "PID_0034", "PID_1403" };
                foreach (var pid in zteSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // Alcatel/TCL SPRD (VID_1BBB + specific PIDs)
            if (deviceIdUpper.Contains("VID_1BBB"))
            {
                string[] alcatelSprdPids = { "PID_0536", "PID_0530", "PID_0510" };
                foreach (var pid in alcatelSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // Huawei SPRD (VID_12D1 + specific PIDs)
            if (deviceIdUpper.Contains("VID_12D1"))
            {
                string[] huaweiSprdPids = { "PID_1001", "PID_1035", "PID_1C05" };
                foreach (var pid in huaweiSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // Realme/OPPO SPRD (VID_22D9)
            if (deviceIdUpper.Contains("VID_22D9"))
            {
                string[] realmeSprdPids = { "PID_2762", "PID_2763", "PID_2764" };
                foreach (var pid in realmeSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // Nokia SPRD (VID_0421)
            if (deviceIdUpper.Contains("VID_0421"))
            {
                string[] nokiaSprdPids = { "PID_0600", "PID_0601", "PID_0602" };
                foreach (var pid in nokiaSprdPids)
                {
                    if (deviceIdUpper.Contains(pid) || hwIdStr.Contains(pid))
                        return true;
                }
                return false;
            }

            // Infinix/Tecno/Itel (VID_2A47)
            if (deviceIdUpper.Contains("VID_2A47"))
            {
                // Transsion devices may use Spreadtrum chips
                return true;
            }

            // ========== Step 5: Relaxed DIAG mode detection ==========
            // If device name contains DIAG and is not from other known platforms
            if (nameUpper.Contains("DIAG") && !nameUpper.Contains("QUALCOMM"))
            {
                // Check if there's a COM port
                if (nameUpper.Contains("(COM"))
                    return true;
            }

            // Does not match any Spreadtrum device characteristics
            return false;
        }

        /// <summary>
        /// Extract COM port number from device name
        /// </summary>
        private string ExtractComPort(string name)
        {
            var match = Regex.Match(name, @"\(COM(\d+)\)");
            if (match.Success)
            {
                return "COM" + match.Groups[1].Value;
            }
            return null;
        }

        /// <summary>
        /// Parse device information
        /// </summary>
        private SprdDeviceInfo ParseDeviceInfo(string name, string deviceId, string[] hardwareIds, string comPort)
        {
            var info = new SprdDeviceInfo
            {
                Name = name,
                DeviceId = deviceId,
                ComPort = comPort
            };

            // Parse VID/PID
            string hwId = hardwareIds != null && hardwareIds.Length > 0 ? hardwareIds[0] : deviceId;

            var vidMatch = Regex.Match(hwId, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (vidMatch.Success)
            {
                info.Vid = Convert.ToInt32(vidMatch.Groups[1].Value, 16);
            }

            var pidMatch = Regex.Match(hwId, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (pidMatch.Success)
            {
                info.Pid = Convert.ToInt32(pidMatch.Groups[1].Value, 16);
            }

            // Determine device mode
            info.Mode = DetermineDeviceMode(info.Pid, name);

            return info;
        }

        /// <summary>
        /// Determine device mode
        /// </summary>
        private SprdDeviceMode DetermineDeviceMode(int pid, string name)
        {
            string nameUpper = name.ToUpper();

            // ========== Determine by PID ==========
            // Download mode PID
            if (SprdUsbIds.IsDownloadPid(pid))
                return SprdDeviceMode.Download;

            // Diagnostic mode PID
            if (SprdUsbIds.IsDiagPid(pid))
                return SprdDeviceMode.Diag;

            // Other known PIDs
            switch (pid)
            {
                case SprdUsbIds.PID_ADB:
                case SprdUsbIds.PID_ADB_2:
                    return SprdDeviceMode.Adb;
                case SprdUsbIds.PID_MTP:
                case SprdUsbIds.PID_MTP_2:
                    return SprdDeviceMode.Mtp;
                case SprdUsbIds.PID_FASTBOOT:
                    return SprdDeviceMode.Fastboot;
            }

            // ========== Determine by name keywords ==========
            // Download mode keywords
            string[] downloadKeywords = {
                "DOWNLOAD", "BOOT", "BROM", "U2S DIAG", "U2S_DIAG",
                "SPRD U2S", "SCI USB2SERIAL", "UNISOC U2S"
            };
            foreach (var keyword in downloadKeywords)
            {
                if (nameUpper.Contains(keyword))
                    return SprdDeviceMode.Download;
            }

            // Diagnostic mode keywords
            string[] diagKeywords = { "DIAG", "DIAGNOSTIC", "CP" };
            foreach (var keyword in diagKeywords)
            {
                if (nameUpper.Contains(keyword) && !nameUpper.Contains("U2S"))
                    return SprdDeviceMode.Diag;
            }

            // ADB mode keywords
            if (nameUpper.Contains("ADB") || nameUpper.Contains("ANDROID DEBUG"))
                return SprdDeviceMode.Adb;

            // MTP mode keywords
            if (nameUpper.Contains("MTP") || nameUpper.Contains("MEDIA TRANSFER"))
                return SprdDeviceMode.Mtp;

            // CDC/ACM is usually also Download mode
            if (nameUpper.Contains("CDC") || nameUpper.Contains("ACM") || nameUpper.Contains("SERIAL"))
                return SprdDeviceMode.Download;

            return SprdDeviceMode.Unknown;
        }

        /// <summary>
        /// Device change event
        /// </summary>
        private void OnDeviceChanged(object sender, EventArrivedEventArgs e)
        {
            // Debounce: ignore repeated events in short time
            lock (_lock)
            {
                var now = DateTime.Now;
                if ((now - _lastEventTime).TotalMilliseconds < DebounceMs)
                {
                    return; // Ignore duplicate events
                }

                if (_isProcessingEvent)
                {
                    return; // Event already being processed
                }

                _lastEventTime = now;
                _isProcessingEvent = true;
            }

            // Delay scan, wait for device stabilization
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800); // Increase delay to wait for device stabilization

                    // Get previous device list
                    var previousDevices = new HashSet<string>();
                    lock (_lock)
                    {
                        foreach (var dev in _connectedDevices)
                            previousDevices.Add(dev.ComPort);
                    }

                    // Silent scan, do not log
                    var currentDevices = ScanDevices(silent: true);

                    // Update device list
                    UpdateDeviceList(currentDevices);

                    // Detect new connections
                    foreach (var dev in currentDevices)
                    {
                        if (!previousDevices.Contains(dev.ComPort))
                        {
                            Log("[Port Detector] New device: {0} ({1})", dev.Name, dev.ComPort);
                            OnDeviceConnected?.Invoke(dev);
                        }
                    }

                    // Detect disconnections
                    var currentPorts = new HashSet<string>();
                    foreach (var dev in currentDevices)
                        currentPorts.Add(dev.ComPort);

                    foreach (var port in previousDevices)
                    {
                        if (!currentPorts.Contains(port))
                        {
                            Log("[Port Detector] Device disconnected: {0}", port);
                            OnDeviceDisconnected?.Invoke(new SprdDeviceInfo { ComPort = port });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Prevent background task exceptions from crashing the program
                    System.Diagnostics.Debug.WriteLine($"[Port Detector] Device change processing exception: {ex.Message}");
                }
                finally
                {
                    lock (_lock)
                    {
                        _isProcessingEvent = false;
                    }
                }
            });
        }

        private void Log(string format, params object[] args)
        {
            OnLog?.Invoke(string.Format(format, args));
        }

        public void Dispose()
        {
            StopWatching();
        }
    }

    /// <summary>
    /// Spreadtrum Device Information
    /// </summary>
    public class SprdDeviceInfo
    {
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string ComPort { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public SprdDeviceMode Mode { get; set; }

        /// <summary>
        /// COM port number (numeric)
        /// </summary>
        public int ComPortNumber
        {
            get
            {
                if (ComPort != null && ComPort.StartsWith("COM"))
                {
                    int num;
                    if (int.TryParse(ComPort.Substring(3), out num))
                        return num;
                }
                return 0;
            }
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}) - {2}", Name, ComPort, Mode);
        }
    }

    /// <summary>
    /// Spreadtrum Device Mode
    /// </summary>
    public enum SprdDeviceMode
    {
        Unknown,
        Download,   // Download mode (flashable)
        Diag,       // Diagnostic mode
        Adb,        // ADB mode
        Mtp,        // MTP mode
        Fastboot,   // Fastboot mode
        Normal      // Normal mode
    }

    /// <summary>
    /// COM Port Information (for debugging)
    /// </summary>
    public class ComPortInfo
    {
        public string ComPort { get; set; }
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string HardwareId { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public bool IsSprdDetected { get; set; }

        public override string ToString()
        {
            return string.Format("{0}: {1} (VID={2:X4} PID={3:X4}) {4}",
                ComPort, Name, Vid, Pid, IsSprdDetected ? "[Spreadtrum]" : "");
        }
    }
}
