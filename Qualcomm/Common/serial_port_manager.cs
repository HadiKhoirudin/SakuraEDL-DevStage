// ============================================================================
// LoveAlways - 串口管理器
// Serial Port Manager - 线程安全的串口资源管理
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 处理端口打开、关闭、读写和错误恢复
// ============================================================================

using System;
using System.Diagnostics;
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
        private readonly Action<string> _log;

        // 串口配置 (9008 EDL 模式用 USB CDC 模拟串口)
        // 注意：缓冲区过大会增加内存拷贝开销，反而降低速度
        public int BaudRate { get; set; } = 921600;
        public int ReadTimeout { get; set; } = 30000;
        public int WriteTimeout { get; set; } = 30000;
        public int ReadBufferSize { get; set; } = 2 * 1024 * 1024;   // 2MB 读缓冲
        public int WriteBufferSize { get; set; } = 4 * 1024 * 1024;  // 4MB 写缓冲

        /// <summary>
        /// 端口断开事件
        /// </summary>
        public event EventHandler PortDisconnected;

        public SerialPortManager(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        /// <summary>
        /// 验证端口是否真正可用 (不仅检查 IsOpen，还尝试实际访问)
        /// </summary>
        public bool ValidateConnection()
        {
            lock (_lock)
            {
                if (_port == null || !_port.IsOpen)
                    return false;

                try
                {
                    // 尝试访问端口属性来验证连接
                    var _ = _port.BytesToRead;
                    return true;
                }
                catch (IOException)
                {
                    // 端口已断开
                    OnPortDisconnected();
                    return false;
                }
                catch (InvalidOperationException)
                {
                    // 端口已关闭
                    OnPortDisconnected();
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    // 端口被其他程序占用
                    OnPortDisconnected();
                    return false;
                }
            }
        }

        /// <summary>
        /// 检查端口是否在系统可用端口列表中
        /// </summary>
        public bool IsPortAvailable()
        {
            if (string.IsNullOrEmpty(_currentPortName))
                return false;

            var ports = SerialPort.GetPortNames();
            return Array.Exists(ports, p => p.Equals(_currentPortName, StringComparison.OrdinalIgnoreCase));
        }

        private void OnPortDisconnected()
        {
            CloseInternal();
            PortDisconnected?.Invoke(this, EventArgs.Empty);
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
                            // 优化超时设置以提高传输速度
                            // ReadTimeout: 单次读取超时，设置较短以支持非阻塞轮询
                            // WriteTimeout: 写入超时，保持较长以支持大数据块
                            ReadTimeout = 1000,   // 1秒读取超时 (配合异步读取)
                            WriteTimeout = 30000, // 30秒写入超时 (大文件需要)
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
                        // 清空缓冲区 (忽略异常，端口可能已断开)
                        try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); }
                        catch (Exception ex) { _log(string.Format("[SerialPort] 清空缓冲区异常: {0}", ex.Message)); }

                        // 禁用控制信号 (忽略异常)
                        try { _port.DtrEnable = false; _port.RtsEnable = false; }
                        catch (Exception ex) { _log(string.Format("[SerialPort] 禁用控制信号异常: {0}", ex.Message)); }

                        Thread.Sleep(50);
                        _port.Close();
                    }
                }
                catch (Exception ex)
                {
                    _log(string.Format("[SerialPort] 关闭端口异常: {0}", ex.Message));
                }
                finally
                {
                    try { _port.Dispose(); }
                    catch (Exception ex) { _log(string.Format("[SerialPort] 释放端口异常: {0}", ex.Message)); }
                    _port = null;
                    _currentPortName = "";
                }
            }
        }

        private void ForceReleasePort(string portName)
        {
            try
            {
                using (var tempPort = new SerialPort(portName))
                {
                    tempPort.Open();
                    tempPort.Close();
                }
            }
            catch (Exception ex)
            {
                // 端口可能被占用或不存在，这是预期的情况
                _log(string.Format("[SerialPort] 强制释放端口 {0} 失败: {1}", portName, ex.Message));
            }
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
            catch (Exception ex)
            {
                _log(string.Format("[SerialPort] 异步写入异常: {0}", ex.Message));
                return false;
            }
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
                    var stopwatch = Stopwatch.StartNew(); // 使用 Stopwatch 避免 TickCount 溢出
                    int originalTimeout = _port.ReadTimeout;

                    _port.ReadTimeout = Math.Max(100, timeout / 10);

                    try
                    {
                        while (totalRead < length && stopwatch.ElapsedMilliseconds < timeout)
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
                                catch (TimeoutException)
                                {
                                    // 读取超时，继续重试
                                }
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
                                    if (totalRead == 0 && stopwatch.ElapsedMilliseconds > timeout / 2)
                                        break;
                                    Thread.Sleep(10);
                                }
                            }
                        }
                    }
                    finally
                    {
                        try { _port.ReadTimeout = originalTimeout; }
                        catch (Exception ex) { _log(string.Format("[SerialPort] 恢复超时设置异常: {0}", ex.Message)); }
                    }

                    return totalRead == length ? buffer : null;
                }
                catch (Exception ex)
                {
                    _log(string.Format("[SerialPort] 读取数据异常: {0}", ex.Message));
                    return null;
                }
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
