// ============================================================================
// LoveAlways - 高通端口自动检测
// PortDetector - 自动识别 9008/9006 EDL 端口
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace LoveAlways.Qualcomm.Common
{
    public class DetectedPort
    {
        public string PortName { get; set; }
        public string Description { get; set; }
        public string DeviceId { get; set; }
        public PortType Type { get; set; }
        public bool IsEdl { get { return Type == PortType.Edl9008 || Type == PortType.Dload9006; } }

        public DetectedPort()
        {
            PortName = "";
            Description = "";
            DeviceId = "";
            Type = PortType.Unknown;
        }

        public override string ToString()
        {
            return string.Format("{0} - {1} ({2})", PortName, Description, Type);
        }
    }

    public enum PortType
    {
        Unknown,
        Edl9008,
        Dload9006,
        Diag9091,
        Adb,
        Fastboot,
        Other
    }

    public static class PortDetector
    {
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public static List<DetectedPort> DetectAllPorts()
        {
            var result = new List<DetectedPort>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        try
                        {
                            string name = device["Name"]?.ToString() ?? "";
                            string deviceId = device["DeviceID"]?.ToString() ?? "";
                            string caption = device["Caption"]?.ToString() ?? "";

                            var match = Regex.Match(name, @"\(COM(\d+)\)");
                            if (!match.Success) continue;

                            string portName = "COM" + match.Groups[1].Value;

                            var port = new DetectedPort
                            {
                                PortName = portName,
                                Description = caption,
                                DeviceId = deviceId,
                                Type = IdentifyPortType(deviceId, caption)
                            };

                            result.Add(port);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                foreach (string portName in SerialPort.GetPortNames())
                {
                    result.Add(new DetectedPort { PortName = portName, Description = "串口", Type = PortType.Unknown });
                }
            }

            return result;
        }

        public static List<DetectedPort> DetectEdlPorts()
        {
            var all = DetectAllPorts();
            var edl = new List<DetectedPort>();
            foreach (var port in all)
            {
                if (port.IsEdl) edl.Add(port);
            }
            return edl;
        }

        public static DetectedPort GetFirstEdlPort()
        {
            var ports = DetectEdlPorts();
            return ports.Count > 0 ? ports[0] : null;
        }

        private static PortType IdentifyPortType(string deviceId, string description)
        {
            string upper = (deviceId + " " + description).ToUpperInvariant();

            if (upper.Contains("VID_05C6&PID_9008") || upper.Contains("VID_2A70&PID_9008") ||
                upper.Contains("VID_22D9&PID_9008") || upper.Contains("VID_2717&PID_9008"))
                return PortType.Edl9008;

            if (upper.Contains("VID_05C6&PID_9006") || upper.Contains("VID_05C6&PID_9007"))
                return PortType.Dload9006;

            if (upper.Contains("QDLOADER") || upper.Contains("9008") || upper.Contains("HS-USB"))
                return PortType.Edl9008;

            if (upper.Contains("DLOAD") || upper.Contains("9006"))
                return PortType.Dload9006;

            return PortType.Unknown;
        }

        public static async System.Threading.Tasks.Task<DetectedPort> WaitForEdlPortAsync(
            int timeoutMs = 30000,
            Action<string> log = null,
            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            log = log ?? delegate { };
            int elapsed = 0;
            int interval = 500;

            while (elapsed < timeoutMs && !ct.IsCancellationRequested)
            {
                var port = GetFirstEdlPort();
                if (port != null)
                {
                    log(string.Format("[PortDetector] 检测到 EDL 端口: {0}", port.PortName));
                    return port;
                }

                await System.Threading.Tasks.Task.Delay(interval, ct);
                elapsed += interval;
            }

            return null;
        }
    }
}
