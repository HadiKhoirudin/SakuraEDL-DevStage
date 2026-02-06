// ============================================================================
// LoveAlways - MediaTek Port Detector
// MediaTek Port Detector
// ============================================================================
// Automatically detects USB ports for MediaTek devices
// Supports BROM mode and Preloader mode
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// MediaTek Port Detector
    /// </summary>
    public class MtkPortDetector : IDisposable
    {
        private readonly Action<string> _log;
        private ManagementEventWatcher _insertWatcher;
        private ManagementEventWatcher _removeWatcher;
        private CancellationTokenSource _cts;
        private bool _isMonitoring { get; set; }
        private bool _disposed;

        // MediaTek Device VID/PID
        private static readonly (int Vid, int Pid, string Description)[] MtkDeviceIds = new[]
        {
            (0x0E8D, 0x0003, "MTK BROM"),
            (0x0E8D, 0x2000, "MTK Preloader"),
            (0x0E8D, 0x2001, "MTK Preloader"),
            (0x0E8D, 0x0023, "MTK Composite"),
            (0x0E8D, 0x3000, "MTK SP Flash"),
            (0x0E8D, 0x0002, "MTK BROM Legacy"),
            (0x0E8D, 0x00A5, "MTK DA"),
            (0x0E8D, 0x00A2, "MTK DA"),
            (0x0E8D, 0x2006, "MTK CDC"),
            (0x1004, 0x6000, "LGE MTK"),    // LG MTK devices
            (0x22D9, 0x2766, "OPPO MTK"),   // OPPO MTK devices
            (0x2717, 0xFF40, "Xiaomi MTK"), // Xiaomi MTK devices
            (0x2A45, 0x0C02, "Meizu MTK"),  // Meizu MTK devices
        };

        // Events
        public event Action<MtkPortInfo> OnDeviceArrived;
        public event Action<string> OnDeviceRemoved;

        public MtkPortDetector(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        #region Port Detection

        /// <summary>
        /// Get all MediaTek device ports
        /// </summary>
        public List<MtkPortInfo> GetMtkPorts()
        {
            var ports = new List<MtkPortInfo>();

            try
            {
                // Method 1: Use WMI to query USB devices
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            string deviceId = obj["DeviceID"]?.ToString() ?? "";
                            string name = obj["Name"]?.ToString() ?? "";
                            string caption = obj["Caption"]?.ToString() ?? "";

                            // Check if it is a MediaTek device
                            var portInfo = ParseMtkDevice(deviceId, name, caption);
                            if (portInfo != null)
                            {
                                ports.Add(portInfo);
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MediaTek Port] Detect port exception: {ex.Message}"); }
                    }
                }

                // Method 2: Scan serial ports
                foreach (string portName in SerialPort.GetPortNames())
                {
                    if (!ports.Any(p => p.ComPort == portName))
                    {
                        var portInfo = ProbePort(portName);
                        if (portInfo != null)
                        {
                            ports.Add(portInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[MediaTek Port] Port detection error: {ex.Message}");
            }

            return ports;
        }

        /// <summary>
        /// Parse MediaTek device info - Double validation (VID + Device name)
        /// </summary>
        private MtkPortInfo ParseMtkDevice(string deviceId, string name, string caption)
        {
            string nameUpper = name.ToUpper();
            string captionUpper = caption.ToUpper();

            // ========== Step 1: Hard exclude other platforms ==========

            // Exclude Spreadtrum devices (VID 0x1782)
            if (deviceId.ToUpper().Contains("VID_1782"))
                return null;

            // Exclude Spreadtrum device keywords
            string[] sprdKeywords = { "SPRD", "SPREADTRUM", "UNISOC", "U2S DIAG", "SCI USB2SERIAL" };
            foreach (var kw in sprdKeywords)
            {
                if (nameUpper.Contains(kw) || captionUpper.Contains(kw))
                    return null;
            }

            // Exclude Qualcomm devices (VID 0x05C6)
            if (deviceId.ToUpper().Contains("VID_05C6"))
                return null;

            // Exclude Qualcomm device keywords
            string[] qcKeywords = { "QUALCOMM", "QDL", "QHSUSB", "QDLOADER" };
            foreach (var kw in qcKeywords)
            {
                if (nameUpper.Contains(kw) || captionUpper.Contains(kw))
                    return null;
            }

            // Exclude ADB/Fastboot
            if (nameUpper.Contains("ADB INTERFACE") || nameUpper.Contains("FASTBOOT"))
                return null;

            // ========== Step 2: Parse VID/PID ==========

            var vidMatch = Regex.Match(deviceId, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            var pidMatch = Regex.Match(deviceId, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);

            if (!vidMatch.Success || !pidMatch.Success)
                return null;

            int vid = Convert.ToInt32(vidMatch.Groups[1].Value, 16);
            int pid = Convert.ToInt32(pidMatch.Groups[1].Value, 16);

            // ========== Step 3: Double validate MediaTek device ==========

            // MediaTek specific VID (0x0E8D)
            bool hasMtkVid = (vid == 0x0E8D);

            // MediaTek specific device name keywords
            string[] mtkKeywords = { "MEDIATEK", "MTK", "PRELOADER", "DA USB", "BROM" };
            bool hasMtkKeyword = false;
            foreach (var kw in mtkKeywords)
            {
                if (nameUpper.Contains(kw) || captionUpper.Contains(kw))
                {
                    hasMtkKeyword = true;
                    break;
                }
            }

            // Case 1: VID 0x0E8D = Confirmed MTK (no keyword needed)
            if (hasMtkVid)
            {
                // Continue processing
            }
            // Case 2: Known manufacturer MTK device (VID + PID combination)
            else
            {
                var mtkDevice = MtkDeviceIds.FirstOrDefault(d => d.Vid == vid && d.Pid == pid);
                if (mtkDevice.Vid == 0)
                {
                    // Not a known MediaTek device combination
                    // Check if it has MediaTek keywords
                    if (!hasMtkKeyword)
                        return null;
                }
            }

            // ========== Step 4: Extract COM port number ==========

            var comMatch = Regex.Match(name, @"\(COM(\d+)\)", RegexOptions.IgnoreCase);
            if (!comMatch.Success)
            {
                comMatch = Regex.Match(caption, @"\(COM(\d+)\)", RegexOptions.IgnoreCase);
            }

            string comPort = comMatch.Success ? $"COM{comMatch.Groups[1].Value}" : null;
            if (string.IsNullOrEmpty(comPort))
                return null;

            // ========== Step 5: Determine device mode ==========

            var knownDevice = MtkDeviceIds.FirstOrDefault(d => d.Vid == vid && d.Pid == pid);
            string description = knownDevice.Description ?? name;

            bool isBrom = (pid == 0x0003 || pid == 0x0002) || nameUpper.Contains("BROM");
            bool isPreloader = (pid == 0x2000 || pid == 0x2001) || nameUpper.Contains("PRELOADER");

            return new MtkPortInfo
            {
                ComPort = comPort,
                Vid = vid,
                Pid = pid,
                DeviceId = deviceId,
                Description = description,
                IsBromMode = isBrom,
                IsPreloaderMode = isPreloader
            };
        }

        /// <summary>
        /// Probe if port is a MediaTek device
        /// </summary>
        private MtkPortInfo ProbePort(string portName)
        {
            try
            {
                using (var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = 500;
                    port.WriteTimeout = 500;
                    port.Open();

                    // Send MediaTek handshake byte
                    port.Write(new byte[] { 0xA0 }, 0, 1);
                    Thread.Sleep(50);

                    if (port.BytesToRead > 0)
                    {
                        byte[] response = new byte[port.BytesToRead];
                        port.Read(response, 0, response.Length);

                        // Check if it is a MediaTek response
                        if (response.Any(b => b == 0x5F || b == 0xA0))
                        {
                            return new MtkPortInfo
                            {
                                ComPort = portName,
                                Description = "MTK Device (Probed)",
                                IsBromMode = true
                            };
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        #endregion

        #region Hotplug Monitoring

        /// <summary>
        /// Start device monitoring
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            try
            {
                _cts = new CancellationTokenSource();

                // Device arrival monitoring
                var insertQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                    "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                    "AND TargetInstance.ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");

                _insertWatcher = new ManagementEventWatcher(insertQuery);
                _insertWatcher.EventArrived += OnDeviceInserted;
                _insertWatcher.Start();

                // Device removal monitoring
                var removeQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 2 " +
                    "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                    "AND TargetInstance.ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");

                _removeWatcher = new ManagementEventWatcher(removeQuery);
                _removeWatcher.EventArrived += OnDeviceRemovedEvent;
                _removeWatcher.Start();

                //_log("[MediaTek Port] Device monitoring started");
            }
            catch (Exception ex)
            {
                _log($"[MediaTek Port] Start monitoring failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop device monitoring
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring)
                return;

            try
            {
                _cts?.Cancel();

                _insertWatcher?.Stop();
                _insertWatcher?.Dispose();
                _insertWatcher = null;

                _removeWatcher?.Stop();
                _removeWatcher?.Dispose();
                _removeWatcher = null;

                _isMonitoring = false;
                //_log("[MediaTek Port] Device monitoring stopped");
            }
            catch { }
        }

        /// <summary>
        /// Device insertion event handler
        /// </summary>
        private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                Debug.WriteLine("MediaTek USB Inserted....");
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string deviceId = targetInstance["DeviceID"]?.ToString() ?? "";
                string name = targetInstance["Name"]?.ToString() ?? "";
                string caption = targetInstance["Caption"]?.ToString() ?? "";

                var portInfo = ParseMtkDevice(deviceId, name, caption);
                if (portInfo != null)
                {
                    //_log($"[MediaTek Port] Device detected: {portInfo.ComPort} ({portInfo.Description})");
                    OnDeviceArrived?.Invoke(portInfo);
                }
            }
            catch { }
        }

        /// <summary>
        /// Device removal event handler
        /// </summary>
        private void OnDeviceRemovedEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string name = targetInstance["Name"]?.ToString() ?? "";

                var comMatch = Regex.Match(name, @"\(COM(\d+)\)", RegexOptions.IgnoreCase);
                if (comMatch.Success)
                {
                    string comPort = $"COM{comMatch.Groups[1].Value}";
                    //_log($"[MediaTek Port] Device removed: {comPort}");
                    OnDeviceRemoved?.Invoke(comPort);
                }
            }
            catch { }
        }

        #endregion

        #region Asynchronously wait for device

        /// <summary>
        /// Wait for MediaTek device connection
        /// </summary>
        public async Task<MtkPortInfo> WaitForDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default)
        {
            _log("[MediaTek Port] Waiting for device connection...");

            var tcs = new TaskCompletionSource<MtkPortInfo>();

            void OnArrived(MtkPortInfo info)
            {
                tcs.TrySetResult(info);
            }

            try
            {
                OnDeviceArrived += OnArrived;
                //StartMonitoring();

                // Check if device is already connected first
                var existingPorts = GetMtkPorts();
                if (existingPorts.Count > 0)
                {
                    _log($"[MediaTek Port] Found connected device: {existingPorts[0].ComPort}");
                    return existingPorts[0];
                }

                // Wait for new device
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(timeoutMs);

                    try
                    {
                        var completedTask = await Task.WhenAny(
                            tcs.Task,
                            Task.Delay(timeoutMs, cts.Token)
                        );

                        if (completedTask == tcs.Task)
                        {
                            return await tcs.Task;
                        }
                    }
                    catch (OperationCanceledException) { }
                }

                _log("[MediaTek Port] Wait for device timeout");
                return null;
            }
            finally
            {
                OnDeviceArrived -= OnArrived;
            }
        }

        /// <summary>
        /// Wait for specific type of MediaTek device
        /// </summary>
        public async Task<MtkPortInfo> WaitForBromDeviceAsync(int timeoutMs = 60000, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (ct.IsCancellationRequested)
                    return null;

                var ports = GetMtkPorts();
                var bromPort = ports.FirstOrDefault(p => p.IsBromMode);

                if (bromPort != null)
                {
                    _log($"[MediaTek Port] Found BROM device: {bromPort.ComPort}");
                    return bromPort;
                }

                await Task.Delay(500, ct);
            }

            return null;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Check if port is available
        /// </summary>
        public static bool IsPortAvailable(string portName)
        {
            try
            {
                using (var port = new SerialPort(portName))
                {
                    port.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get port friendly name
        /// </summary>
        public static string GetPortFriendlyName(string portName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%{portName}%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["Caption"]?.ToString() ?? portName;
                    }
                }
            }
            catch { }

            return portName;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                StopMonitoring();
                _cts?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// MediaTek Port Info
    /// </summary>
    public class MtkPortInfo
    {
        /// <summary>COM port name</summary>
        public string ComPort { get; set; }

        /// <summary>USB VID</summary>
        public int Vid { get; set; }

        /// <summary>USB PID</summary>
        public int Pid { get; set; }

        /// <summary>Device ID</summary>
        public string DeviceId { get; set; }

        /// <summary>Device description</summary>
        public string Description { get; set; }

        /// <summary>Whether in BROM mode</summary>
        public bool IsBromMode { get; set; }

        /// <summary>Whether in Preloader mode</summary>
        public bool IsPreloaderMode { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string DisplayName => $"{ComPort} - {Description}";

        /// <summary>
        /// Mode description
        /// </summary>
        public string ModeDescription
        {
            get
            {
                if (IsBromMode) return "BROM Mode";
                if (IsPreloaderMode) return "Preloader Mode";
                return "Unknown Mode";
            }
        }

        public override string ToString()
        {
            return $"{ComPort} ({Description}) [{ModeDescription}]";
        }
    }
}
