// ============================================================================
// LoveAlways - 串口管理器
// Serial Port Manager - 线程安全的串口资源管理
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 处理端口打开、关闭、读写和错误恢复
// ============================================================================

using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// 串口管理器 - 线程安全的资源管理
    /// </summary>
    public class SerialPortManager : IDisposable
    {
        private SerialPort _port;
        private readonly object _lock = new object();
        private bool _disposed;
        private string _currentPortName = "";

        // 串口配置 (9008 EDL 模式用 USB CDC 模拟串口)
        public int BaudRate { get; set; } = 921600;
        public int ReadTimeout { get; set; } = 30000;
        public int WriteTimeout { get; set; } = 30000;
        public int ReadBufferSize { get; set; } = 16 * 1024 * 1024;
        public int WriteBufferSize { get; set; } = 16 * 1024 * 1024;

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        public string PortName
        {
            get { return _currentPortName; }
        }

        public int BytesToRead
        {
            get { return _port != null ? _port.BytesToRead : 0; }
        }

        public bool Open(string portName, int maxRetries = 3, bool discardBuffer = false)
        {
            lock (_lock)
            {
                if (_port != null && _port.IsOpen && _currentPortName == portName)
                    return true;

                CloseInternal();

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        if (discardBuffer)
                        {
                            ForceReleasePort(portName);
                            Thread.Sleep(100);
                        }

                        _port = new SerialPort
                        {
                            PortName = portName,
                            BaudRate = BaudRate,
                            DataBits = 8,
                            Parity = Parity.None,
                            StopBits = StopBits.One,
                            Handshake = Handshake.None,
                            ReadTimeout = 5000,
                            WriteTimeout = 5000,
                            ReadBufferSize = ReadBufferSize,
                            WriteBufferSize = WriteBufferSize
                        };

                        _port.Open();
                        _currentPortName = portName;

                        if (discardBuffer)
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }

                        return true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(500 * (i + 1));
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(300 * (i + 1));
                    }
                    catch (Exception)
                    {
                        if (i == maxRetries - 1)
                            throw;
                        Thread.Sleep(200);
                    }
                }

                return false;
            }
        }

        public Task<bool> OpenAsync(string portName, int maxRetries = 3, bool discardBuffer = false, CancellationToken ct = default(CancellationToken))
        {
            return Task.Run(() => Open(portName, maxRetries, discardBuffer), ct);
        }

        public void Close()
        {
            lock (_lock)
            {
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            if (_port != null)
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } catch { }
                        try { _port.DtrEnable = false; _port.RtsEnable = false; } catch { }
                        Thread.Sleep(50);
                        _port.Close();
                    }
                }
                catch { }
                finally
                {
                    try { _port.Dispose(); } catch { }
                    _port = null;
                    _currentPortName = "";
                }
            }
        }

        private static void ForceReleasePort(string portName)
        {
            try
            {
                using (var tempPort = new SerialPort(portName))
                {
                    tempPort.Open();
                    tempPort.Close();
                }
            }
            catch { }
        }

        public void Write(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                if (_port == null || !_port.IsOpen) throw new InvalidOperationException("串口未打开");
                _port.Write(data, offset, count);
            }
        }

        public void Write(byte[] data)
        {
            Write(data, 0, data.Length);
        }

        public async Task<bool> WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (!IsOpen) return false;
            try
            {
                await _port.BaseStream.WriteAsync(buffer, offset, count, ct);
                return true;
            }
            catch { return false; }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_port == null || !_port.IsOpen) throw new InvalidOperationException("串口未打开");
            return _port.Read(buffer, offset, count);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_port == null || !_port.IsOpen) return 0;
            return await _port.BaseStream.ReadAsync(buffer, offset, count, ct);
        }

        /// <summary>
        /// 异步读取指定长度数据 (超时返回 null)
        /// </summary>
        public Task<byte[]> TryReadExactAsync(int length, int timeout = 10000, CancellationToken ct = default(CancellationToken))
        {
            if (_port == null || !_port.IsOpen)
                return Task.FromResult<byte[]>(null);

            return Task.Run(() =>
            {
                try
                {
                    var buffer = new byte[length];
                    int totalRead = 0;
                    int startTime = Environment.TickCount;
                    int originalTimeout = _port.ReadTimeout;

                    _port.ReadTimeout = Math.Max(100, timeout / 10);

                    try
                    {
                        while (totalRead < length && (Environment.TickCount - startTime) < timeout)
                        {
                            if (ct.IsCancellationRequested)
                                return null;

                            int bytesAvailable = _port.BytesToRead;

                            if (bytesAvailable > 0)
                            {
                                int toRead = Math.Min(length - totalRead, bytesAvailable);
                                try
                                {
                                    int read = _port.Read(buffer, totalRead, toRead);
                                    if (read > 0)
                                        totalRead += read;
                                }
                                catch (TimeoutException) { }
                            }
                            else
                            {
                                try
                                {
                                    int read = _port.Read(buffer, totalRead, length - totalRead);
                                    if (read > 0)
                                        totalRead += read;
                                }
                                catch (TimeoutException)
                                {
                                    if (totalRead == 0 && (Environment.TickCount - startTime) > timeout / 2)
                                        break;
                                    Thread.Sleep(10);
                                }
                            }
                        }
                    }
                    finally
                    {
                        try { _port.ReadTimeout = originalTimeout; } catch { }
                    }

                    return totalRead == length ? buffer : null;
                }
                catch { return null; }
            }, ct);
        }

        public Stream BaseStream
        {
            get { return _port?.BaseStream; }
        }

        public void DiscardInBuffer()
        {
            if (_port != null) _port.DiscardInBuffer();
        }

        public void DiscardOutBuffer()
        {
            if (_port != null) _port.DiscardOutBuffer();
        }

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing) Close();
                _disposed = true;
            }
        }

        ~SerialPortManager()
        {
            Dispose(false);
        }
    }
}
