
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Transport
{
    /// <summary>
    /// Fastboot Transport Layer Interface
    /// Supports both USB and TCP transport modes
    /// </summary>
    public interface IFastbootTransport : IDisposable
    {
        /// <summary>
        /// Whether connected
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Device identifier (serial number or address)
        /// </summary>
        string DeviceId { get; }
        
        /// <summary>
        /// Connect to device
        /// </summary>
        Task<bool> ConnectAsync(CancellationToken ct = default);
        
        /// <summary>
        /// Disconnect
        /// </summary>
        void Disconnect();
        
        /// <summary>
        /// Send data
        /// </summary>
        Task<int> SendAsync(byte[] data, int offset, int count, CancellationToken ct = default);
        
        /// <summary>
        /// Receive data
        /// </summary>
        Task<int> ReceiveAsync(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken ct = default);
        
        /// <summary>
        /// Send and receive response
        /// </summary>
        Task<byte[]> TransferAsync(byte[] command, int timeoutMs, CancellationToken ct = default);
    }
    
    /// <summary>
    /// Fastboot Device Information
    /// </summary>
    public class FastbootDeviceDescriptor
    {
        public string Serial { get; set; }
        public string DevicePath { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string Manufacturer { get; set; }
        public string Product { get; set; }
        public TransportType Type { get; set; }
        
        // TCP connection info
        public string Host { get; set; }
        public int Port { get; set; }
        
        public override string ToString()
        {
            if (Type == TransportType.Tcp)
                return $"{Host}:{Port}";
            return $"{Serial} ({VendorId:X4}:{ProductId:X4})";
        }
    }
    
    /// <summary>
    /// Transport Type
    /// </summary>
    public enum TransportType
    {
        Usb,
        Tcp
    }
}
