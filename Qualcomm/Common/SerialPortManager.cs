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
        // USB 2.0 High Speed = 480 Mbps，波特率设置不影响实际速度
        public int BaudRate { get; set; } = 921600;          // 最高波特率
        public int ReadTimeout { get; set; } = 30000;        // 增加超时
        public int WriteTimeout { get; set; } = 30000;       // 增加超时
        public int ReadBufferSize { get; set; } = 16 * 1024 * 1024;  // 16MB 大缓冲
        public int WriteBufferSize { get; set; } = 16 * 1024 * 1024; // 16MB 大缓冲

        /// <summary>
        /// 当前串口是否打开
        /// </summary>
        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        /// <summary>
        /// 当前端口名
        /// </summary>
        public string PortName
        {
            get { return _currentPortName; }
        }

        /// <summary>
        /// 打开串口 (带重试机制)
        /// </summary>
        /// <param name="portName">端口名称</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="discardBuffer">是否清空缓冲区 (Sahara 协议必须设为 false)</param>
        public bool Open(string portName, int maxRetries = 3, bool discardBuffer = false)
        {
            lock (_lock)
            {
                // 如果已打开同一端口，直接返回
                if (_port != null && _port.IsOpen && _currentPortName == portName)
                    return true;

                // 关闭之前的端口
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

        /// <summary>
        /// 异步打开串口
        /// </summary>
        public Task<bool> OpenAsync(string portName, int maxRetries = 3, bool discardBuffer = false, CancellationToken ct = default(CancellationToken))
        {
            return Task.Run(() => Open(portName, maxRetries, discardBuffer), ct);
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
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
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }
                        catch { }

                        try
                        {
                            _port.DtrEnable = false;
                            _port.RtsEnable = false;
                        }
                        catch { }

                        Thread.Sleep(50);
                        _port.Close();
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        _port.Dispose();
                    }
                    catch { }
                    _port = null;
                    _currentPortName = "";
                }
            }
        }

        /// <summary>
        /// 强制释放端口
        /// </summary>
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

        /// <summary>
        /// 写入数据
        /// </summary>
        public void Write(byte[] data, int offset, int count)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("串口未打开");

            _port.Write(data, offset, count);
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        public void Write(byte[] data)
        {
            Write(data, 0, data.Length);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("串口未打开");

            return _port.Read(buffer, offset, count);
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

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        public void DiscardInBuffer()
        {
            if (_port != null)
                _port.DiscardInBuffer();
        }

        /// <summary>
        /// 清空发送缓冲区
        /// </summary>
        public void DiscardOutBuffer()
        {
            if (_port != null)
                _port.DiscardOutBuffer();
        }

        /// <summary>
        /// 获取可用字节数
        /// </summary>
        public int BytesToRead
        {
            get { return _port != null ? _port.BytesToRead : 0; }
        }

        /// <summary>
        /// 获取所有可用串口
        /// </summary>
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
                if (disposing)
                {
                    Close();
                }
                _disposed = true;
            }
        }

        ~SerialPortManager()
        {
            Dispose(false);
        }
    }
}
