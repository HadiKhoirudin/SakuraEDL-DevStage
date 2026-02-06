// ============================================================================
// LoveAlways - MediaTek XML DA Protocol Client
// MediaTek XML Download Agent Protocol Client (V6)
// ============================================================================
// Reference: mtkclient project xml_cmd.py, xml_lib.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.MediaTek.Common;
using LoveAlways.MediaTek.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DaEntry = LoveAlways.MediaTek.Models.DaEntry;

namespace LoveAlways.MediaTek.Protocol
{
    /// <summary>
    /// XML DA Protocol Client (V6)
    /// </summary>
    public class XmlDaClient : IDisposable
    {
        private SerialPort _port;
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<double> _progressCallback;
        private bool _disposed;

        // Thread safety: Port lock (can be shared from BromClient)
        private readonly SemaphoreSlim _portLock;
        private readonly bool _ownsPortLock;

        // XML Protocol Constants
        private const uint XML_MAGIC = 0xFEEEEEEF;
        private const int DEFAULT_TIMEOUT_MS = 30000;
        private const int MAX_BUFFER_SIZE = 65536;

        // XFlash Command Constants
        private const uint CMD_BOOT_TO = 0x72;

        // Data types
        private enum DataType : uint
        {
            ProtocolFlow = 0,
            ProtocolResponse = 1,
            ProtocolRaw = 2
        }

        // Connection status
        public bool IsConnected { get; private set; }
        public MtkDeviceState State { get; private set; }

        public XmlDaClient(SerialPort port, Action<string> log = null, Action<string> logDetail = null, Action<double> progressCallback = null, SemaphoreSlim portLock = null)
        {
            _port = port;
            _log = log ?? delegate { };
            _logDetail = logDetail ?? _log;
            _progressCallback = progressCallback;
            State = MtkDeviceState.Da1Loaded;

            // Use external lock if provided, otherwise create own
            if (portLock != null)
            {
                _portLock = portLock;
                _ownsPortLock = false;
            }
            else
            {
                _portLock = new SemaphoreSlim(1, 1);
                _ownsPortLock = true;
            }
        }

        /// <summary>
        /// Set serial port
        /// </summary>
        public void SetPort(SerialPort port)
        {
            _port = port;
        }

        /// <summary>
        /// Get port lock
        /// </summary>
        public SemaphoreSlim GetPortLock() => _portLock;

        #region XML Protocol Core

        /// <summary>
        /// Send XML command (Internal method, no lock)
        /// </summary>
        private async Task XSendInternalAsync(string xmlCmd, CancellationToken ct = default)
        {
            // Note: No longer clearing the buffer, as the device might send unsolicited messages (e.g., CMD:DOWNLOAD-FILE)
            // Clearing will cause important requests to be lost

            byte[] data = Encoding.UTF8.GetBytes(xmlCmd);

            // Build header: Magic (4) + DataType (4) + Length (4)
            byte[] header = new byte[12];
            MtkDataPacker.WriteUInt32LE(header, 0, XML_MAGIC);
            MtkDataPacker.WriteUInt32LE(header, 4, (uint)DataType.ProtocolFlow);
            MtkDataPacker.WriteUInt32LE(header, 8, (uint)data.Length);

            _port.Write(header, 0, 12);
            _port.Write(data, 0, data.Length);

            _logDetail($"[XML] Sending: {xmlCmd.Substring(0, Math.Min(100, xmlCmd.Length))}...");
        }

        /// <summary>
        /// Send XML command (Thread safe)
        /// </summary>
        private async Task XSendAsync(string xmlCmd, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                await XSendInternalAsync(xmlCmd, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Receive XML response (Internal method, no lock)
        /// </summary>
        private async Task<string> XRecvInternalAsync(int timeoutMs = DEFAULT_TIMEOUT_MS, CancellationToken ct = default)
        {
            // Read header
            byte[] header = await ReadBytesInternalAsync(12, timeoutMs, ct);
            if (header == null)
            {
                _log("[XML] Read header timeout");
                return null;
            }

            // Verify Magic
            uint magic = MtkDataPacker.UnpackUInt32LE(header, 0);
            if (magic != XML_MAGIC)
            {
                _log($"[XML] Magic mismatch: 0x{magic:X8}, expected: 0x{XML_MAGIC:X8}");
                _logDetail($"[XML] Header data: {BitConverter.ToString(header).Replace("-", " ")}");

                // Try to resync: Find Magic in subsequent data
                _log("[XML] Attempting to resync protocol stream...");
                bool synced = await TryResyncAsync(timeoutMs, ct);
                if (!synced)
                {
                    _log("[XML] Protocol synchronization failed");
                    return null;
                }

                // Read header again
                header = await ReadBytesInternalAsync(12, timeoutMs, ct);
                if (header == null)
                    return null;

                magic = MtkDataPacker.UnpackUInt32LE(header, 0);
                if (magic != XML_MAGIC)
                {
                    _log("[XML] Still unable to find valid Magic after resync");
                    return null;
                }

                _log("[XML] ✓ Protocol resynchronized successfully");
            }

            uint dataType = MtkDataPacker.UnpackUInt32LE(header, 4);
            uint length = MtkDataPacker.UnpackUInt32LE(header, 8);

            if (length == 0)
                return "";

            if (length > MAX_BUFFER_SIZE)
            {
                _log($"[XML] Data too large: {length} (max: {MAX_BUFFER_SIZE})");
                return null;
            }

            // Read data
            byte[] data = await ReadBytesInternalAsync((int)length, timeoutMs, ct);
            if (data == null)
            {
                _log("[XML] Read data timeout");
                return null;
            }

            string response = Encoding.UTF8.GetString(data);
            _logDetail($"[XML] Received: {response.Substring(0, Math.Min(100, response.Length))}...");

            return response;
        }

        /// <summary>
        /// Attempt to resynchronize XML protocol stream
        /// </summary>
        private async Task<bool> TryResyncAsync(int timeoutMs, CancellationToken ct)
        {
            // Read up to 1KB data to find Magic
            const int maxSearchBytes = 1024;
            byte[] searchBuffer = new byte[maxSearchBytes];
            int totalRead = 0;

            DateTime start = DateTime.Now;

            while (totalRead < maxSearchBytes && (DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (_port.BytesToRead > 0)
                {
                    int toRead = Math.Min(_port.BytesToRead, maxSearchBytes - totalRead);
                    int actualRead = _port.Read(searchBuffer, totalRead, toRead);
                    totalRead += actualRead;

                    // Search for Magic in read data
                    for (int i = 0; i <= totalRead - 4; i++)
                    {
                        uint candidate = MtkDataPacker.UnpackUInt32LE(searchBuffer, i);
                        if (candidate == XML_MAGIC)
                        {
                            _logDetail($"[XML] Found Magic at offset {i}");
                            // Discard data before Magic
                            // Put data after Magic back into buffer (if possible)
                            return true;
                        }
                    }
                }
                else
                {
                    await Task.Delay(10, ct);
                }
            }

            return false;
        }

        /// <summary>
        /// Receive XML response (Thread safe)
        /// </summary>
        private async Task<string> XRecvAsync(int timeoutMs = DEFAULT_TIMEOUT_MS, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                return await XRecvInternalAsync(timeoutMs, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Send command and wait for response (Thread safe)
        /// </summary>
        private async Task<XmlDocument> SendCommandAsync(string xmlCmd, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                await XSendInternalAsync(xmlCmd, ct);

                string response = await XRecvInternalAsync(DEFAULT_TIMEOUT_MS, ct);
                if (string.IsNullOrEmpty(response))
                    return null;

                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(response);
                    return doc;
                }
                catch (Exception ex)
                {
                    _log($"[XML] Failed to parse response: {ex.Message}");
                    Debug.WriteLine($"Error XML Response : \n{response}\n");
                    return null;
                }
            }
            finally
            {
                _portLock.Release();
            }
        }

        private async Task<bool> SendCommandAsyncRuntime(string xmlCmd, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                await XSendInternalAsync(xmlCmd, ct);

                string response = await XRecvInternalAsync(DEFAULT_TIMEOUT_MS, ct);
                if (string.IsNullOrEmpty(response)) return false;

                return response.Contains("OK");
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Send raw data (Internal method, no lock)
        /// </summary>
        private async Task XSendRawInternalAsync(byte[] data, CancellationToken ct = default)
        {
            byte[] header = new byte[12];
            MtkDataPacker.WriteUInt32LE(header, 0, XML_MAGIC);
            MtkDataPacker.WriteUInt32LE(header, 4, (uint)DataType.ProtocolRaw);
            MtkDataPacker.WriteUInt32LE(header, 8, (uint)data.Length);

            _port.Write(header, 0, 12);
            _port.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Send raw data (Thread safe)
        /// </summary>
        private async Task XSendRawAsync(byte[] data, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                await XSendRawInternalAsync(data, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        #endregion

        #region DA Connection

        /// <summary>
        /// Wait for DA to be ready
        /// </summary>
        public async Task<bool> WaitForDaReadyAsync(int timeoutMs = 30000, CancellationToken ct = default)
        {
            _log("[XML] Waiting for DA to be ready...");

            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (ct.IsCancellationRequested)
                    return false;

                try
                {
                    // Check if DA initial message is received
                    string response = await XRecvAsync(2000, ct);
                    if (!string.IsNullOrEmpty(response))
                    {
                        _logDetail($"[XML] Received: {response.Substring(0, Math.Min(100, response.Length))}...");

                        if (response.Contains("CMD:START") || response.Contains("ready"))
                        {
                            // Send "OK" confirmation (ChimeraTool protocol requirement)
                            await SendOkAsync(ct);
                            _log("[XML] ✓ DA is ready");
                            IsConnected = true;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Continue waiting
                }

                await Task.Delay(100, ct);
            }

            _log("[XML] DA ready timeout");
            return false;
        }

        /// <summary>
        /// Send OK confirmation message
        /// </summary>
        private async Task SendOkAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                // Build OK response: Magic + DataType(1) + Length(2) + "OK"
                byte[] header = new byte[12];
                MtkDataPacker.WriteUInt32LE(header, 0, XML_MAGIC);
                MtkDataPacker.WriteUInt32LE(header, 4, 1);  // DataType = 1
                MtkDataPacker.WriteUInt32LE(header, 8, 2);  // Length = 2

                _port.Write(header, 0, 12);
                _port.Write(Encoding.ASCII.GetBytes("OK"), 0, 2);

                _logDetail("[XML] Sending OK confirmation");
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Get Connection Agent (detect device boot source)
        /// Returns: "brom" or "preloader"
        /// </summary>
        public async Task<string> GetConnectionAgentAsync(CancellationToken ct = default)
        {
            _log("[XML] Getting Connection Agent...");

            await _portLock.WaitAsync(ct);
            try
            {
                // Send GET_CONNECTION_AGENT command (0x01)
                byte[] cmdHeader = new byte[12];
                MtkDataPacker.WriteUInt32LE(cmdHeader, 0, XML_MAGIC);
                MtkDataPacker.WriteUInt32LE(cmdHeader, 4, (uint)DataType.ProtocolFlow);
                MtkDataPacker.WriteUInt32LE(cmdHeader, 8, 4);
                _port.Write(cmdHeader, 0, 12);

                byte[] cmdData = new byte[4];
                MtkDataPacker.WriteUInt32LE(cmdData, 0, 0x01);  // GET_CONNECTION_AGENT
                _port.Write(cmdData, 0, 4);

                // Use correct XML protocol format to read response
                // Read 12-byte header first
                var header = await ReadBytesInternalAsync(12, 3000, ct);
                if (header == null || header.Length < 12)
                {
                    _log("[XML] Warning: Failed to read Connection Agent header");
                    return "preloader";
                }

                uint magic = MtkDataPacker.UnpackUInt32LE(header, 0);
                uint dataType = MtkDataPacker.UnpackUInt32LE(header, 4);
                uint length = MtkDataPacker.UnpackUInt32LE(header, 8);

                if (magic != XML_MAGIC)
                {
                    // Not XML format, try to parse directly
                    string rawStr = System.Text.Encoding.ASCII.GetString(header).ToLower();
                    _logDetail($"[XML] Non-standard response: {rawStr}");
                    if (rawStr.Contains("brom")) return "brom";
                    if (rawStr.Contains("preloader") || rawStr.Contains("pl")) return "preloader";
                    return "preloader";
                }

                // Read data part
                if (length > 0 && length < 1024)
                {
                    var data = await ReadBytesInternalAsync((int)length, 2000, ct);
                    if (data != null && data.Length > 0)
                    {
                        string agent = System.Text.Encoding.ASCII.GetString(data)
                            .TrimEnd('\0', ' ', '\r', '\n')
                            .ToLower();

                        _logDetail($"[XML] Connection Agent data: \"{agent}\"");

                        if (agent.Contains("brom") && !agent.Contains("preloader"))
                        {
                            _log("[XML] ✓ Connection Agent: brom (Booted from Boot ROM)");
                            return "brom";
                        }
                        else if (agent.Contains("preloader") || agent.Contains("pl"))
                        {
                            _log("[XML] ✓ Connection Agent: preloader (Booted from Preloader)");
                            return "preloader";
                        }
                    }
                }

                // Default to preloader (most devices boot from preloader)
                _log("[XML] Could not definitively determine, assuming by default: preloader");
                return "preloader";
            }
            catch (Exception ex)
            {
                _log($"[XML] Connection Agent exception: {ex.Message}");
                _logDetail($"[XML] Stack: {ex.StackTrace}");
                return "preloader";
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Set runtime parameters (Sent by ChimeraTool after CMD:START)
        /// </summary>
        public async Task<bool> SetRuntimeParametersAsync(CancellationToken ct = default)
        {
            _logDetail("[XML] Setting runtime parameters...");

            try
            {
                // Command sent by ChimeraTool
                string cmd = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                            "<da>" +
                            "<version>1.0</version>" +
                            "<command>CMD:SET-RUNTIME-PARAMETER</command>" +
                            "<arg>" +
                            "<checksum_level>NONE</checksum_level>" +
                            "<da_log_level>ERROR</da_log_level>" +
                            "<log_channel>UART</log_channel>" +
                            "<battery_exist>AUTO-DETECT</battery_exist>" +
                            "<system_os>LINUX</system_os>" +
                            "</arg>" +
                            "<adv>" +
                            "<initialize_dram>YES</initialize_dram>" +
                            "</adv>" +
                            "</da>";

                var response = await SendCommandAsyncRuntime(cmd, ct);
                if (response)
                {
                    _logDetail("[XML] ✓ Runtime parameters set successfully");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logDetail($"[XML] Runtime parameter exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// DA Handshake
        /// </summary>
        public async Task<bool> DaHandshakeAsync(CancellationToken ct = default)
        {
            _log("[XML] DA Handshake...");

            // Send handshake command
            string handshakeCmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                                  "<da><version>1.0</version><command>CMD:CONNECT</command></da>";

            var response = await SendCommandAsync(handshakeCmd, ct);
            if (response == null)
            {
                _log("[XML] Handshake no response");
                return false;
            }

            // Check response
            var statusNode = response.SelectSingleNode("//status");
            if (statusNode != null && statusNode.InnerText == "OK")
            {
                _log("[XML] ✓ DA Handshake successful");
                IsConnected = true;
                return true;
            }

            _log("[XML] DA Handshake failed");
            return false;
        }

        #endregion

        #region XFlash Commands (Carbonara Exploit)

        /// <summary>
        /// boot_to command - Write data to specified address
        /// This is the core of Carbonara exploit: ability to write data to any address in DA1 memory
        /// </summary>
        public async Task<bool> BootToAsync(uint address, byte[] data, bool display = true, int timeoutMs = 500, CancellationToken ct = default)
        {
            if (display)
                _log($"[XFlash] boot_to: Address=0x{address:X8}, size={data.Length}");

            await _portLock.WaitAsync(ct);
            try
            {
                // 1. Send BOOT_TO command
                byte[] cmdHeader = new byte[12];
                MtkDataPacker.WriteUInt32LE(cmdHeader, 0, XML_MAGIC);
                MtkDataPacker.WriteUInt32LE(cmdHeader, 4, (uint)DataType.ProtocolFlow);
                MtkDataPacker.WriteUInt32LE(cmdHeader, 8, 4);
                _port.Write(cmdHeader, 0, 12);

                byte[] cmdData = new byte[4];
                MtkDataPacker.WriteUInt32LE(cmdData, 0, CMD_BOOT_TO);
                _port.Write(cmdData, 0, 4);

                // Read status
                var statusResp = await ReadBytesInternalAsync(4, 5000, ct);
                if (statusResp == null)
                {
                    _log("[XFlash] boot_to command no response");
                    return false;
                }

                uint status = MtkDataPacker.UnpackUInt32LE(statusResp, 0);
                if (status != 0)
                {
                    _log($"[XFlash] boot_to command error: 0x{status:X}");
                    return false;
                }

                // 2. Send parameters (Address + Length, 8 bytes each for 64-bit)
                byte[] param = new byte[16];
                // Explicitly use Little-Endian 64-bit
                WriteUInt64LE(param, 0, address);
                WriteUInt64LE(param, 8, (ulong)data.Length);

                byte[] paramHeader = new byte[12];
                MtkDataPacker.WriteUInt32LE(paramHeader, 0, XML_MAGIC);
                MtkDataPacker.WriteUInt32LE(paramHeader, 4, (uint)DataType.ProtocolFlow);
                MtkDataPacker.WriteUInt32LE(paramHeader, 8, (uint)param.Length);
                _port.Write(paramHeader, 0, 12);
                _port.Write(param, 0, param.Length);

                // 3. Send data
                if (!await SendDataInternalAsync(data, ct))
                {
                    _log("[XFlash] boot_to data send failed");
                    return false;
                }

                // 4. Wait and read status
                if (timeoutMs > 0)
                    await Task.Delay(timeoutMs, ct);

                var finalStatus = await ReadBytesInternalAsync(4, 5000, ct);
                if (finalStatus == null)
                {
                    _log("[XFlash] boot_to final status read failed");
                    return false;
                }

                uint result = MtkDataPacker.UnpackUInt32LE(finalStatus, 0);
                // 0x434E5953 = "SYNC" or 0x0 = success
                if (result == 0x434E5953 || result == 0)
                {
                    if (display)
                        _log("[XFlash] ✓ boot_to success");
                    return true;
                }

                _log($"[XFlash] boot_to failed: 0x{result:X}");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[XFlash] boot_to exception: {ex.Message}");
                return false;
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Execute Carbonara exploit
        /// </summary>
        public async Task<bool> ExecuteCarbonaraAsync(
            uint da1Address,
            int hashOffset,
            byte[] newHash,
            uint da2Address,
            byte[] patchedDa2,
            CancellationToken ct = default)
        {
            _log("[Carbonara] Starting runtime exploit...");

            // 1. First boot_to: Write new hash to hash location in DA1 memory
            uint hashWriteAddress = da1Address + (uint)hashOffset;
            _log($"[Carbonara] Writing new hash to 0x{hashWriteAddress:X8}");

            if (!await BootToAsync(hashWriteAddress, newHash, display: true, timeoutMs: 100, ct: ct))
            {
                _log("[Carbonara] Hash write failed");
                return false;
            }

            _log("[Carbonara] ✓ Hash write successful");

            // 2. Second boot_to: Upload patched DA2
            _log($"[Carbonara] Uploading patched DA2 to 0x{da2Address:X8}");

            if (!await BootToAsync(da2Address, patchedDa2, display: true, timeoutMs: 500, ct: ct))
            {
                _log("[Carbonara] DA2 upload failed");
                return false;
            }

            _log("[Carbonara] ✓ Stage2 upload successful");

            // 3. Execute SLA authentication (if needed)
            await Task.Delay(100, ct);  // Wait for DA2 initialization

            _log("[Carbonara] Checking SLA status...");
            bool slaRequired = await CheckSlaStatusAsync(ct);

            if (slaRequired)
            {
                _log("[Carbonara] SLA Status: Enabled");
                _log("[Carbonara] Executing SLA authentication...");

                if (!await ExecuteSlaAuthAsync(ct))
                {
                    _log("[Carbonara] ⚠ SLA authentication failed, but continuing anyway");
                    // Don't return failure, continue trying
                }
                else
                {
                    _log("[Carbonara] ✓ SLA authentication successful");
                }
            }
            else
            {
                _log("[Carbonara] SLA Status: Disabled (No authentication needed)");
            }

            _log("[Carbonara] ✓ Runtime exploit successful!");
            State = MtkDeviceState.Da2Loaded;
            IsConnected = true;

            return true;
        }

        /// <summary>
        /// Check SLA status
        /// </summary>
        private async Task<bool> CheckSlaStatusAsync(CancellationToken ct = default)
        {
            try
            {
                // Send SLA status check command
                string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                            "<da><version>1.0</version>" +
                            "<command>CMD:GET-SLA</command>" +
                            "</da>";

                var response = await SendCommandAsync(cmd, ct);
                if (response != null)
                {
                    var statusNode = response.SelectSingleNode("//sla") ??
                                    response.SelectSingleNode("//status");
                    if (statusNode != null)
                    {
                        string status = statusNode.InnerText.ToUpper();
                        return status == "ENABLED" || status == "1" || status == "TRUE";
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Execute SLA authentication
        /// </summary>
        private async Task<bool> ExecuteSlaAuthAsync(CancellationToken ct = default)
        {
            try
            {
                // SLA Auth Flow:
                // 1. Send SLA-CHALLENGE request
                // 2. Receive challenge data
                // 3. Sign using SLA certificate
                // 4. Send signature response

                string challengeCmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                                     "<da><version>1.0</version>" +
                                     "<command>CMD:SLA-CHALLENGE</command>" +
                                     "</da>";

                var challengeResponse = await SendCommandAsync(challengeCmd, ct);
                if (challengeResponse == null)
                {
                    _logDetail("[SLA] Could not get challenge");
                    return false;
                }

                // Parse challenge
                var challengeNode = challengeResponse.SelectSingleNode("//challenge");
                if (challengeNode == null)
                {
                    // Maybe device doesn't need SLA or is already authorized
                    var statusNode = challengeResponse.SelectSingleNode("//status");
                    if (statusNode != null && statusNode.InnerText.Contains("OK"))
                    {
                        return true;
                    }
                    _logDetail("[SLA] No challenge data");
                    return false;
                }

                // Get challenge data
                string challengeHex = challengeNode.InnerText;
                byte[] challenge = HexToBytes(challengeHex);

                _logDetail($"[SLA] Received challenge: {challenge.Length} bytes");

                // Sign challenge using built-in SLA certificate (placeholder implementation)
                // Real implementation needs to load the correct SLA certificate based on device type
                byte[] signature = await MtkSlaAuth.SignChallengeAsync(challenge, ct);

                if (signature == null || signature.Length == 0)
                {
                    _logDetail("[SLA] Signature generation failed");
                    return false;
                }

                _logDetail($"[SLA] Generated signature: {signature.Length} bytes");
                _progressCallback?.Invoke(50);

                // Send signature response
                string signatureHex = BytesToHex(signature);
                string authCmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                                "<da><version>1.0</version>" +
                                "<command>CMD:SLA-AUTH</command>" +
                                "<arg>" +
                                $"<signature>{signatureHex}</signature>" +
                                "</arg>" +
                                "</da>";

                var authResponse = await SendCommandAsync(authCmd, ct);
                _progressCallback?.Invoke(100);

                if (authResponse != null)
                {
                    var resultNode = authResponse.SelectSingleNode("//status") ??
                                    authResponse.SelectSingleNode("//result");
                    if (resultNode != null)
                    {
                        string result = resultNode.InnerText.ToUpper();
                        return result == "OK" || result == "SUCCESS" || result == "0";
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logDetail($"[SLA] Authorization exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Convert Hex string to byte array
        /// </summary>
        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            hex = hex.Replace(" ", "").Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Convert byte array to Hex string
        /// </summary>
        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null) return "";
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// Send data (Internal method)
        /// </summary>
        private async Task<bool> SendDataInternalAsync(byte[] data, CancellationToken ct = default)
        {
            try
            {
                // Send data header
                byte[] header = new byte[12];
                MtkDataPacker.WriteUInt32LE(header, 0, XML_MAGIC);
                MtkDataPacker.WriteUInt32LE(header, 4, (uint)DataType.ProtocolRaw);
                MtkDataPacker.WriteUInt32LE(header, 8, (uint)data.Length);
                _port.Write(header, 0, 12);

                // Send data in chunks
                int chunkSize = 4096;
                int offset = 0;
                while (offset < data.Length)
                {
                    if (ct.IsCancellationRequested)
                        return false;

                    int toSend = Math.Min(chunkSize, data.Length - offset);
                    _port.Write(data, offset, toSend);
                    offset += toSend;

                    _progressCallback?.Invoke((double)offset * 100 / data.Length);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region DA2 Upload

        /// <summary>
        /// Upload DA2 - ChimeraTool Protocol
        /// Device actively sends CMD:DOWNLOAD-FILE request, host responds OK@size then sends data
        /// </summary>
        public async Task<bool> UploadDa2Async(DaEntry da2, CancellationToken ct = default)
        {
            if (da2 == null || da2.Data == null)
            {
                _log("[XML] DA2 data is empty");
                return false;
            }

            _log($"[XML] Uploading DA2: {da2.Data.Length} bytes");

            // 0. Handle prior messages (CMD:END, CMD:START, etc.)
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    string msg = await XRecvAsync(500, ct);
                    if (!string.IsNullOrEmpty(msg))
                    {
                        _logDetail($"[XML] Handling message: {msg.Substring(0, Math.Min(80, msg.Length))}...");
                        if (msg.Contains("CMD:END") || msg.Contains("CMD:START") || msg.Contains("CMD:PROGRESS"))
                        {
                            await SendOkAsync(ct);
                        }
                    }
                }
                catch { }
            }

            // 1. Send BOOT-TO command to trigger DA2 download
            _log("[XML] Sending BOOT-TO command...");
            string bootToCmd = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                              "<da>" +
                              "<version>1.0</version>" +
                              "<command>CMD:BOOT-TO</command>" +
                              "<arg>" +
                              $"<at_address>0x{da2.LoadAddr:X8}</at_address>" +
                              $"<jmp_address>0x{da2.LoadAddr:X8}</jmp_address>" +
                              "<source_file>Boot to</source_file>" +
                              "</arg>" +
                              "</da>";

            await XSendAsync(bootToCmd, ct);

            _log("[XML] Waiting for device to request DA2...");

            // 2. Wait for device to send CMD:DOWNLOAD-FILE request
            bool receivedRequest = false;
            int packetLength = 0x1000; // Default 4KB

            for (int retry = 0; retry < 30 && !receivedRequest; retry++)
            {
                try
                {
                    // Read device message
                    string msg = await XRecvAsync(1000, ct);
                    if (string.IsNullOrEmpty(msg))
                    {
                        continue;
                    }

                    _logDetail($"[XML] Received message: {msg.Substring(0, Math.Min(100, msg.Length))}...");

                    // Check if it's a DOWNLOAD-FILE request
                    if (msg.Contains("CMD:DOWNLOAD-FILE") && msg.Contains("2nd-DA"))
                    {
                        _log("[XML] ✓ Received DA2 download request");

                        // Parse packet_length
                        var match = System.Text.RegularExpressions.Regex.Match(
                            msg, @"<packet_length>0x([0-9A-Fa-f]+)</packet_length>");
                        if (match.Success)
                        {
                            packetLength = Convert.ToInt32(match.Groups[1].Value, 16);
                            _logDetail($"[XML] Chunk size: 0x{packetLength:X}");
                        }

                        // Important: Send OK to confirm DOWNLOAD-FILE request
                        _log("[XML] Sending OK to confirm DOWNLOAD-FILE...");
                        await SendOkAsync(ct);

                        receivedRequest = true;
                    }
                    else if (msg.Contains("CMD:START") || msg.Contains("CMD:PROGRESS-REPORT"))
                    {
                        // Device sent other messages, respond with OK
                        await SendOkAsync(ct);
                    }
                    else if (msg.Contains("CMD:END"))
                    {
                        // Command completed, respond with OK
                        await SendOkAsync(ct);
                    }
                }
                catch (TimeoutException)
                {
                    // Continue waiting
                }
            }

            if (!receivedRequest)
            {
                _log("[XML] Timeout: Did not receive DA2 download request");
                return false;
            }

            // 2. Respond with OK@<size> (inform device regarding file size)
            string sizeResponse = $"OK@{da2.Data.Length} ";
            _log($"[XML] Sending size response: {sizeResponse}");

            // Construct and send manually (more precise control)
            byte[] sizePayload = Encoding.ASCII.GetBytes(sizeResponse);
            byte[] sizeHeader = new byte[12];
            sizeHeader[0] = 0xEF; sizeHeader[1] = 0xEE; sizeHeader[2] = 0xEE; sizeHeader[3] = 0xFE;
            sizeHeader[4] = 0x01; sizeHeader[5] = 0x00; sizeHeader[6] = 0x00; sizeHeader[7] = 0x00;
            sizeHeader[8] = (byte)(sizePayload.Length & 0xFF);
            sizeHeader[9] = (byte)((sizePayload.Length >> 8) & 0xFF);
            sizeHeader[10] = 0; sizeHeader[11] = 0;

            _logDetail($"[XML] Sending header: {BitConverter.ToString(sizeHeader).Replace("-", " ")}");
            _logDetail($"[XML] Sending data: {BitConverter.ToString(sizePayload).Replace("-", " ")}");

            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(sizeHeader, 0, 12);
                _port.BaseStream.Flush();
                _port.Write(sizePayload, 0, sizePayload.Length);
                _port.BaseStream.Flush();
            }
            finally
            {
                _portLock.Release();
            }

            // Wait for device to process
            await Task.Delay(100, ct);

            // Check if serial port has data
            //_log($"[XML] Serial port buffer: {_port.BytesToRead} bytes");

            bool deviceReady = false;

            if (_port.BytesToRead > 0)
            {
                byte[] rawResp = new byte[Math.Min(_port.BytesToRead, 256)];
                int rawRead = _port.Read(rawResp, 0, rawResp.Length);
                //_log($"[XML] Raw response ({rawRead} bytes): {BitConverter.ToString(rawResp, 0, rawRead).Replace("-", " ")}");

                // Check for "OK" response
                if (rawRead >= 3 && rawResp[0] == 0xEF && rawResp[1] == 0xEE)
                {
                    _log("[XML] ✓ Received device confirmation");
                    // Send OK reply
                    await SendOkAsync(ct);

                    // Wait for second confirmation
                    await Task.Delay(50, ct);
                    if (_port.BytesToRead > 0)
                    {
                        byte[] ack2 = new byte[_port.BytesToRead];
                        _port.Read(ack2, 0, ack2.Length);
                        _logDetail($"[XML] Second response: {BitConverter.ToString(ack2).Replace("-", " ")}");
                    }

                    deviceReady = true;
                }
            }

            if (!deviceReady)
            {
                _log("[XML] ⚠ Device not responding, attempting to continue sending data anyway...");
            }

            // Start sending data immediately

            // 4. Send DA2 data in chunks
            _log($"[XML] Starting DA2 data transmission: {da2.Data.Length} bytes, chunk size: {packetLength}");

            int offset = 0;
            int totalChunks = (da2.Data.Length + packetLength - 1) / packetLength;
            int chunkIndex = 0;

            while (offset <= da2.Data.Length)
            {
                int remaining = da2.Data.Length - offset;

                if (remaining <= 0) break;

                int chunkSize = Math.Min(remaining, packetLength);

                byte[] chunk = new byte[chunkSize];
                Array.Copy(da2.Data, offset, chunk, 0, chunkSize);

                // Send data block using XML header format
                await XSendBinaryAsync(chunk, ct);

                offset += chunkSize;
                chunkIndex++;

                if (chunkIndex % 20 == 0 || offset >= da2.Data.Length)
                {
                    _log($"[XML] Sent: {offset}/{da2.Data.Length} ({100 * offset / da2.Data.Length}%)");
                }

                // Wait for device ACK
                await Task.Delay(15, ct); // Brief wait

                if (_port.BytesToRead > 0)
                {
                    byte[] rawAck = new byte[Math.Min(_port.BytesToRead, 144)];
                    int ackRead = _port.Read(rawAck, 0, rawAck.Length);
                    string ackStr = Encoding.ASCII.GetString(rawAck, 0, ackRead).TrimEnd('\0');

                    //if (chunkIndex <= 3)
                    //{
                        _log($"[XML] Chunk {chunkIndex} ACK ({ackRead} bytes): {BitConverter.ToString(rawAck, 0, ackRead).Replace("-", " ")}");
                    //}

                    // Check for ERR
                    if (ackStr.Contains("ERR"))
                    {
                        _log($"[XML] ✗ Device returned error");

                        // Attempt to read complete error message (may be XML)
                        await Task.Delay(100, ct);
                        if (_port.BytesToRead > 0)
                        {
                            byte[] errMsg = new byte[Math.Min(_port.BytesToRead, 1024)];
                            int errRead = _port.Read(errMsg, 0, errMsg.Length);
                            string errStr = Encoding.UTF8.GetString(errMsg, 0, errRead);
                            _log($"[XML] Error details: {errStr}");
                        }

                        return false;
                    }

                    // Check if OK ACK (EF EE EE FE ... 4F 4B 00)
                    if (ackRead >= 12 && rawAck[0] == 0xEF && rawAck[1] == 0xEE)
                    {
                        // Send OK confirmation
                        await SendOkAsync(ct);

                        // Wait for second ACK
                        await Task.Delay(20, ct);

                        if (_port.BytesToRead > 0)
                        {
                            byte[] ack2 = new byte[_port.BytesToRead];
                            _port.Read(ack2, 0, ack2.Length);
                        }
                    }

                    // Check if CMD:END
                    if (ackStr.Contains("CMD:END"))
                    {
                        _log("[XML] Received CMD:END");
                        await SendOkAsync(ct);
                        break;
                    }
                }
            }

            _log($"[XML] DA2 data transmission complete: {offset} bytes");

            // 4. Wait for final confirmation
            for (int i = 0; i < 4; i++)
            {
                string finalMsg = await XRecvAsync(1000, ct);
                if (!string.IsNullOrEmpty(finalMsg))
                {
                    _logDetail($"[XML] Final response: {finalMsg.Substring(0, Math.Min(100, finalMsg.Length))}...");

                    await SendOkAsync(ct);

                    if (finalMsg.Contains("CMD:END"))
                    {
                        if (finalMsg.Contains("OK") || finalMsg.Contains("result>OK"))
                        {
                            State = MtkDeviceState.Da2Loaded;
                            return true;
                        }
                    }
                    else if (finalMsg.Contains("CMD:START") || finalMsg.Contains("CMD:PROGRESS-REPORT"))
                    {
                        State = MtkDeviceState.Da2Loaded;
                        return true;
                    }
                }
            }

            _log("[XML] ✗ DA2 upload Error");
            State = MtkDeviceState.Error;
            return false;
        }

        /// <summary>
        /// Send raw string (with XML header)
        /// </summary>
        private async Task XSendRawAsync(string data, CancellationToken ct = default)
        {
            byte[] payload = Encoding.ASCII.GetBytes(data);

            // Build header: magic(4) + dataType(4) + length(4)
            byte[] header = new byte[12];
            header[0] = 0xEF; header[1] = 0xEE; header[2] = 0xEE; header[3] = 0xFE; // Magic
            header[4] = 0x01; header[5] = 0x00; header[6] = 0x00; header[7] = 0x00; // DataType = 1

            // Length (little-endian)
            int len = payload.Length;
            header[8] = (byte)(len & 0xFF);
            header[9] = (byte)((len >> 8) & 0xFF);
            header[10] = (byte)((len >> 16) & 0xFF);
            header[11] = (byte)((len >> 24) & 0xFF);

            await _portLock.WaitAsync(ct);
            try
            {
                _port.Write(header, 0, 12);
                _port.BaseStream.Flush();
                _port.Write(payload, 0, payload.Length);
                _port.BaseStream.Flush();
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Send binary data (with XML header)
        /// ChimeraTool sends header and data as two separate USB transactions
        /// </summary>
        private async Task XSendBinaryAsync(byte[] data, CancellationToken ct = default)
        {
            // Build header: magic(4) + dataType(4) + length(4)
            byte[] header = new byte[12];
            header[0] = 0xEF; header[1] = 0xEE; header[2] = 0xEE; header[3] = 0xFE; // Magic
            header[4] = 0x01; header[5] = 0x00; header[6] = 0x00; header[7] = 0x00; // DataType = 1

            // Length (little-endian)
            int len = data.Length;
            header[8] = (byte)(len & 0xFF);
            header[9] = (byte)((len >> 8) & 0xFF);
            header[10] = (byte)((len >> 16) & 0xFF);
            header[11] = (byte)((len >> 24) & 0xFF);

            await _portLock.WaitAsync(ct);
            try
            {
                // Send header and data separately (simulating two USB transactions)
                _port.Write(header, 0, 12);
                _port.BaseStream.Flush();

                // Short delay to ensure separate transfer
                await Task.Delay(10, ct);

                _port.Write(data, 0, data.Length);
                _port.BaseStream.Flush();
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Upload data block
        /// </summary>
        private async Task UploadDataAsync(byte[] data, CancellationToken ct = default)
        {
            int chunkSize = 4096;
            int totalSent = 0;

            while (totalSent < data.Length)
            {
                if (ct.IsCancellationRequested)
                    break;

                int remaining = data.Length - totalSent;
                int sendSize = Math.Min(chunkSize, remaining);

                byte[] chunk = new byte[sendSize];
                Array.Copy(data, totalSent, chunk, 0, sendSize);

                await XSendRawAsync(chunk, ct);
                totalSent += sendSize;

                // Update progress
                double progress = (double)totalSent * 100 / data.Length;
                _progressCallback?.Invoke(progress);
            }
        }

        #endregion

        #region Flash Operations

        /// <summary>
        /// Read partition table
        /// </summary>
        public async Task<MtkPartitionInfo[]> ReadPartitionTableAsync(CancellationToken ct = default)
        {
            _log("[XML] Reading partition table...");

            string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         "<da><version>1.0</version><command>CMD:READ_PARTITION_TABLE</command></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return null;

            var partitions = new System.Collections.Generic.List<MtkPartitionInfo>();
            var partitionNodes = response.SelectNodes("//partition");

            if (partitionNodes != null)
            {
                foreach (XmlNode node in partitionNodes)
                {
                    var partition = new MtkPartitionInfo
                    {
                        Name = node.SelectSingleNode("name")?.InnerText ?? "",
                        StartSector = ParseULong(node.SelectSingleNode("start_sector")?.InnerText),
                        SectorCount = ParseULong(node.SelectSingleNode("sector_count")?.InnerText),
                        Size = ParseULong(node.SelectSingleNode("size")?.InnerText),
                        Type = node.SelectSingleNode("type")?.InnerText ?? ""
                    };
                    partitions.Add(partition);
                }
            }

            _log($"[XML] Read {partitions.Count} partitions");
            return partitions.ToArray();
        }

        /// <summary>
        /// Read partition
        /// </summary>
        public async Task<byte[]> ReadPartitionAsync(string partitionName, ulong size, CancellationToken ct = default)
        {
            _log($"[XML] Reading partition: {partitionName}");

            string cmd = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         $"<da><version>1.0</version><command>CMD:READ_PARTITION</command>" +
                         $"<arg><partition_name>{partitionName}</partition_name>" +
                         $"<read_size>{size}</read_size></arg></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return null;

            var statusNode = response.SelectSingleNode("//status");
            if (statusNode == null || statusNode.InnerText != "READY")
                return null;

            // Receive data
            using (var ms = new MemoryStream())
            {
                ulong received = 0;
                while (received < size)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    var chunk = await ReadDataChunkAsync(ct);
                    if (chunk == null || chunk.Length == 0)
                        break;

                    ms.Write(chunk, 0, chunk.Length);
                    received += (ulong)chunk.Length;

                    double progress = (double)received * 100 / size;
                    _progressCallback?.Invoke(progress);
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Write partition
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, byte[] data, CancellationToken ct = default)
        {
            _log($"[XML] Writing partition: {partitionName} ({data.Length} bytes)");

            string cmd = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         $"<da><version>1.0</version><command>CMD:WRITE_PARTITION</command>" +
                         $"<arg><partition_name>{partitionName}</partition_name>" +
                         $"<write_size>{data.Length}</write_size></arg></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return false;

            var statusNode = response.SelectSingleNode("//status");
            if (statusNode == null || statusNode.InnerText != "READY")
                return false;

            // Send data
            await UploadDataAsync(data, ct);

            // Wait for completion
            string completeResponse = await XRecvAsync(DEFAULT_TIMEOUT_MS * 2, ct);
            if (completeResponse != null && completeResponse.Contains("OK"))
            {
                _log($"[XML] ✓ Partition {partitionName} written successfully");
                return true;
            }

            _log($"[XML] Partition {partitionName} writing failed");
            return false;
        }

        /// <summary>
        /// Erase partition
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            _log($"[XML] Erasing partition: {partitionName}");

            string cmd = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         $"<da><version>1.0</version><command>CMD:ERASE_PARTITION</command>" +
                         $"<arg><partition_name>{partitionName}</partition_name></arg></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return false;

            var statusNode = response.SelectSingleNode("//status");
            return statusNode != null && statusNode.InnerText == "OK";
        }

        /// <summary>
        /// Format all partitions
        /// </summary>
        public async Task<bool> FormatAllAsync(CancellationToken ct = default)
        {
            _log("[XML] Formatting all partitions...");

            string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         "<da><version>1.0</version><command>CMD:FORMAT_ALL</command></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return false;

            var statusNode = response.SelectSingleNode("//status");
            return statusNode != null && statusNode.InnerText == "OK";
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Reboot device
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            _log("[XML] Rebooting device...");

            string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         "<da><version>1.0</version><command>CMD:REBOOT</command></da>";

            await XSendAsync(cmd, ct);
            return true;
        }

        /// <summary>
        /// Shutdown
        /// </summary>
        public async Task<bool> ShutdownAsync(CancellationToken ct = default)
        {
            _log("[XML] Shutting down device...");

            string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         "<da><version>1.0</version><command>CMD:SHUTDOWN</command></da>";

            await XSendAsync(cmd, ct);
            return true;
        }

        /// <summary>
        /// Get Flash info
        /// </summary>
        public async Task<MtkFlashInfo> GetFlashInfoAsync(CancellationToken ct = default)
        {
            _log("[XML] Getting Flash info...");

            string cmd = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                         "<da><version>1.0</version><command>CMD:GET_FLASH_INFO</command></da>";

            var response = await SendCommandAsync(cmd, ct);
            if (response == null)
                return null;

            var flashInfo = new MtkFlashInfo
            {
                FlashType = response.SelectSingleNode("//flash_type")?.InnerText ?? "Unknown",
                Capacity = ParseULong(response.SelectSingleNode("//capacity")?.InnerText),
                BlockSize = (uint)ParseULong(response.SelectSingleNode("//block_size")?.InnerText),
                PageSize = (uint)ParseULong(response.SelectSingleNode("//page_size")?.InnerText),
                Model = response.SelectSingleNode("//model")?.InnerText ?? ""
            };

            return flashInfo;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Read data chunk (Thread safe)
        /// </summary>
        private async Task<byte[]> ReadDataChunkAsync(CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                byte[] header = await ReadBytesInternalAsync(12, DEFAULT_TIMEOUT_MS, ct);
                if (header == null)
                    return null;

                uint magic = MtkDataPacker.UnpackUInt32LE(header, 0);
                if (magic != XML_MAGIC)
                    return null;

                uint length = MtkDataPacker.UnpackUInt32LE(header, 8);
                if (length == 0 || length > MAX_BUFFER_SIZE)
                    return null;

                return await ReadBytesInternalAsync((int)length, DEFAULT_TIMEOUT_MS, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Read specified number of bytes (Internal method, no lock)
        /// </summary>
        private async Task<byte[]> ReadBytesInternalAsync(int count, int timeoutMs, CancellationToken ct = default)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            DateTime start = DateTime.Now;

            while (read < count)
            {
                if (ct.IsCancellationRequested)
                    return null;

                if ((DateTime.Now - start).TotalMilliseconds > timeoutMs)
                    return null;

                if (_port.BytesToRead > 0)
                {
                    int toRead = Math.Min(_port.BytesToRead, count - read);
                    int actualRead = _port.Read(buffer, read, toRead);
                    read += actualRead;
                }
                else
                {
                    await Task.Delay(10, ct);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Read specified number of bytes (Thread safe)
        /// </summary>
        private async Task<byte[]> ReadBytesAsync(int count, int timeoutMs, CancellationToken ct = default)
        {
            await _portLock.WaitAsync(ct);
            try
            {
                return await ReadBytesInternalAsync(count, timeoutMs, ct);
            }
            finally
            {
                _portLock.Release();
            }
        }

        /// <summary>
        /// Parse ulong string
        /// </summary>
        private ulong ParseULong(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt64(value, 16);

            return ulong.TryParse(value, out ulong result) ? result : 0;
        }

        /// <summary>
        /// Write 64-bit unsigned integer (Little-Endian) - Platform independent
        /// </summary>
        private static void WriteUInt64LE(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                IsConnected = false;

                // Only dispose if we own the lock
                if (_ownsPortLock)
                {
                    _portLock?.Dispose();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
