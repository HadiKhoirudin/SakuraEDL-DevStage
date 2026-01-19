using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Fastboot.Protocol;

namespace LoveAlways.Fastboot.Transport
{
    /// <summary>
    /// USB 传输层实现
    /// 使用 WinUSB API 直接与 Fastboot 设备通信
    /// </summary>
    public class UsbTransport : IFastbootTransport
    {
        private IntPtr _deviceHandle = IntPtr.Zero;
        private IntPtr _winusbHandle = IntPtr.Zero;
        private byte _bulkInPipe;
        private byte _bulkOutPipe;
        private bool _disposed;
        
        public bool IsConnected => _winusbHandle != IntPtr.Zero;
        public string DeviceId { get; private set; }
        
        private readonly FastbootDeviceDescriptor _descriptor;
        
        public UsbTransport(FastbootDeviceDescriptor descriptor)
        {
            _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            DeviceId = descriptor.Serial;
        }
        
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 打开设备
                    _deviceHandle = NativeMethods.CreateFile(
                        _descriptor.DevicePath,
                        NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        NativeMethods.OPEN_EXISTING,
                        NativeMethods.FILE_FLAG_OVERLAPPED,
                        IntPtr.Zero);
                    
                    if (_deviceHandle == NativeMethods.INVALID_HANDLE_VALUE)
                    {
                        return false;
                    }
                    
                    // 初始化 WinUSB
                    if (!NativeMethods.WinUsb_Initialize(_deviceHandle, out _winusbHandle))
                    {
                        NativeMethods.CloseHandle(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }
                    
                    // 查找 Bulk 端点
                    if (!FindBulkEndpoints())
                    {
                        Disconnect();
                        return false;
                    }
                    
                    return true;
                }
                catch
                {
                    Disconnect();
                    return false;
                }
            }, ct);
        }
        
        private bool FindBulkEndpoints()
        {
            NativeMethods.USB_INTERFACE_DESCRIPTOR interfaceDesc;
            if (!NativeMethods.WinUsb_QueryInterfaceSettings(_winusbHandle, 0, out interfaceDesc))
            {
                return false;
            }
            
            for (byte i = 0; i < interfaceDesc.bNumEndpoints; i++)
            {
                NativeMethods.WINUSB_PIPE_INFORMATION pipeInfo;
                if (NativeMethods.WinUsb_QueryPipe(_winusbHandle, 0, i, out pipeInfo))
                {
                    if (pipeInfo.PipeType == NativeMethods.USBD_PIPE_TYPE.UsbdPipeTypeBulk)
                    {
                        if ((pipeInfo.PipeId & 0x80) != 0)
                        {
                            _bulkInPipe = pipeInfo.PipeId;
                        }
                        else
                        {
                            _bulkOutPipe = pipeInfo.PipeId;
                        }
                    }
                }
            }
            
            return _bulkInPipe != 0 && _bulkOutPipe != 0;
        }
        
        public void Disconnect()
        {
            if (_winusbHandle != IntPtr.Zero)
            {
                NativeMethods.WinUsb_Free(_winusbHandle);
                _winusbHandle = IntPtr.Zero;
            }
            
            if (_deviceHandle != IntPtr.Zero && _deviceHandle != NativeMethods.INVALID_HANDLE_VALUE)
            {
                NativeMethods.CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
        }
        
        public async Task<int> SendAsync(byte[] data, int offset, int count, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("设备未连接");
            
            return await Task.Run(() =>
            {
                uint bytesWritten;
                byte[] buffer = new byte[count];
                Array.Copy(data, offset, buffer, 0, count);
                
                if (NativeMethods.WinUsb_WritePipe(_winusbHandle, _bulkOutPipe, buffer, (uint)count, out bytesWritten, IntPtr.Zero))
                {
                    return (int)bytesWritten;
                }
                
                throw new Exception($"USB 写入失败: {Marshal.GetLastWin32Error()}");
            }, ct);
        }
        
        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("设备未连接");
            
            return await Task.Run(() =>
            {
                // 设置超时
                uint timeout = (uint)timeoutMs;
                NativeMethods.WinUsb_SetPipePolicy(_winusbHandle, _bulkInPipe, 
                    NativeMethods.PIPE_TRANSFER_TIMEOUT, 4, ref timeout);
                
                uint bytesRead;
                byte[] tempBuffer = new byte[count];
                
                if (NativeMethods.WinUsb_ReadPipe(_winusbHandle, _bulkInPipe, tempBuffer, (uint)count, out bytesRead, IntPtr.Zero))
                {
                    Array.Copy(tempBuffer, 0, buffer, offset, (int)bytesRead);
                    return (int)bytesRead;
                }
                
                int error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_SEM_TIMEOUT)
                {
                    return 0; // 超时
                }
                
                throw new Exception($"USB 读取失败: {error}");
            }, ct);
        }
        
        public async Task<byte[]> TransferAsync(byte[] command, int timeoutMs, CancellationToken ct = default)
        {
            // 发送命令
            await SendAsync(command, 0, command.Length, ct);
            
            // 接收响应
            byte[] buffer = new byte[FastbootProtocol.MAX_RESPONSE_LENGTH];
            int received = await ReceiveAsync(buffer, 0, buffer.Length, timeoutMs, ct);
            
            if (received > 0)
            {
                byte[] result = new byte[received];
                Array.Copy(buffer, result, received);
                return result;
            }
            
            return null;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
        
        /// <summary>
        /// 枚举所有 Fastboot 设备
        /// </summary>
        public static List<FastbootDeviceDescriptor> EnumerateDevices()
        {
            var devices = new List<FastbootDeviceDescriptor>();
            
            // 使用 SetupAPI 枚举 USB 设备
            Guid winusbGuid = new Guid("dee824ef-729b-4a0e-9c14-b7117d33a817");
            
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
                ref winusbGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            
            if (deviceInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            {
                return devices;
            }
            
            try
            {
                NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);
                
                for (uint i = 0; NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref winusbGuid, i, ref interfaceData); i++)
                {
                    // 获取设备路径
                    int requiredSize = 0;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                    
                    IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);
                        
                        if (NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                        {
                            string devicePath = Marshal.PtrToStringAuto(new IntPtr(detailDataBuffer.ToInt64() + 4));
                            
                            // 检查是否是 Fastboot 设备
                            if (devicePath.ToLower().Contains("vid_18d1") || // Google
                                devicePath.ToLower().Contains("vid_2717") || // Xiaomi
                                devicePath.ToLower().Contains("vid_22d9") || // OPPO
                                devicePath.ToLower().Contains("vid_2a70") || // OnePlus
                                devicePath.ToLower().Contains("vid_05c6"))   // Qualcomm
                            {
                                var descriptor = new FastbootDeviceDescriptor
                                {
                                    DevicePath = devicePath,
                                    Type = TransportType.Usb
                                };
                                
                                // 解析 VID/PID
                                ParseVidPid(devicePath, descriptor);
                                
                                devices.Add(descriptor);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
            
            return devices;
        }
        
        private static void ParseVidPid(string devicePath, FastbootDeviceDescriptor descriptor)
        {
            try
            {
                string lower = devicePath.ToLower();
                int vidIndex = lower.IndexOf("vid_");
                int pidIndex = lower.IndexOf("pid_");
                
                if (vidIndex >= 0)
                {
                    descriptor.VendorId = Convert.ToInt32(lower.Substring(vidIndex + 4, 4), 16);
                }
                
                if (pidIndex >= 0)
                {
                    descriptor.ProductId = Convert.ToInt32(lower.Substring(pidIndex + 4, 4), 16);
                }
                
                // 从设备路径提取序列号
                // 设备路径格式: \\?\usb#vid_18d1&pid_d00d#SERIAL#{GUID}
                string[] parts = devicePath.Split('#');
                if (parts.Length >= 3)
                {
                    // 第三部分是序列号（在 VID&PID 之后，GUID 之前）
                    string serial = parts[2];
                    // 确保不是 GUID（GUID 以 { 开头）
                    if (!string.IsNullOrEmpty(serial) && !serial.StartsWith("{"))
                    {
                        descriptor.Serial = serial;
                    }
                }
            }
            catch { }
        }
    }
    
    /// <summary>
    /// Windows Native Methods
    /// </summary>
    internal static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        
        public const uint DIGCF_PRESENT = 0x02;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        
        public const uint PIPE_TRANSFER_TIMEOUT = 0x03;
        public const int ERROR_SEM_TIMEOUT = 121;
        
        public enum USBD_PIPE_TYPE
        {
            UsbdPipeTypeControl = 0,
            UsbdPipeTypeIsochronous = 1,
            UsbdPipeTypeBulk = 2,
            UsbdPipeTypeInterrupt = 3
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct USB_INTERFACE_DESCRIPTOR
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct WINUSB_PIPE_INFORMATION
        {
            public USBD_PIPE_TYPE PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_Initialize(IntPtr DeviceHandle, out IntPtr InterfaceHandle);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_Free(IntPtr InterfaceHandle);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_QueryInterfaceSettings(
            IntPtr InterfaceHandle,
            byte AlternateInterfaceNumber,
            out USB_INTERFACE_DESCRIPTOR UsbAltInterfaceDescriptor);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_QueryPipe(
            IntPtr InterfaceHandle,
            byte AlternateInterfaceNumber,
            byte PipeIndex,
            out WINUSB_PIPE_INFORMATION PipeInformation);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_SetPipePolicy(
            IntPtr InterfaceHandle,
            byte PipeID,
            uint PolicyType,
            uint ValueLength,
            ref uint Value);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_WritePipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            out uint LengthTransferred,
            IntPtr Overlapped);
        
        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_ReadPipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            out uint LengthTransferred,
            IntPtr Overlapped);
        
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);
        
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
        
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            ref int RequiredSize,
            IntPtr DeviceInfoData);
        
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
    }
}
