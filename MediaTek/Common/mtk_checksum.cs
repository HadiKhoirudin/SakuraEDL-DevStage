// ============================================================================
// LoveAlways - MediaTek Checksum Utilities
// MediaTek Checksum Utilities
// ============================================================================
// Reference: mtkclient project checksum algorithm
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// MTK Checksum Utility Classes
    /// </summary>
    public static class MtkChecksum
    {
        /// <summary>
        /// Calculate 16-bit XOR checksum (for DA upload)
        /// </summary>
        /// <param name="data">Data</param>
        /// <returns>16-bit checksum</returns>
        public static ushort CalculateXor16(byte[] data)
        {
            if (data == null || data.Length == 0)
                return 0;

            ushort checksum = 0;
            int i = 0;

            // XOR for every 2 bytes
            for (; i + 1 < data.Length; i += 2)
            {
                ushort word = (ushort)(data[i] | (data[i + 1] << 8));
                checksum ^= word;
            }

            // Handle remaining single byte
            if (i < data.Length)
            {
                checksum ^= data[i];
            }

            return checksum;
        }

        /// <summary>
        /// Calculate XFlash 32-bit checksum
        /// </summary>
        /// <param name="data">Data</param>
        /// <returns>32-bit checksum</returns>
        public static uint CalculateXFlash32(byte[] data)
        {
            if (data == null || data.Length == 0)
                return 0;

            uint checksum = 0;
            int pos = 0;

            // Accumulate every 4 bytes
            for (int i = 0; i < data.Length / 4; i++)
            {
                checksum += BitConverter.ToUInt32(data, i * 4);
                pos += 4;
            }

            // Handle remaining bytes
            if (data.Length % 4 != 0)
            {
                for (int i = 0; i < 4 - (data.Length % 4); i++)
                {
                    if (pos < data.Length)
                    {
                        checksum += data[pos];
                        pos++;
                    }
                }
            }

            return checksum;
        }

        /// <summary>
        /// Prepare data for upload (calculate checksum and handle signature)
        /// </summary>
        /// <param name="data">Main data</param>
        /// <param name="sigData">Signature data (optional)</param>
        /// <param name="maxSize">Maximum size (0 means use full data)</param>
        /// <returns>(Checksum, processed data)</returns>
        public static (ushort checksum, byte[] processedData) PrepareData(byte[] data, byte[] sigData = null, int maxSize = 0)
        {
            if (data == null)
                data = Array.Empty<byte>();

            if (sigData == null)
                sigData = Array.Empty<byte>();

            // Trim data to maxSize
            byte[] trimmedData = data;
            if (maxSize > 0 && data.Length > maxSize)
            {
                trimmedData = new byte[maxSize];
                Array.Copy(data, trimmedData, maxSize);
            }

            // Combine data and signature
            byte[] combined = new byte[trimmedData.Length + sigData.Length];
            Array.Copy(trimmedData, 0, combined, 0, trimmedData.Length);
            Array.Copy(sigData, 0, combined, trimmedData.Length, sigData.Length);

            // If length is odd, pad with zero
            if (combined.Length % 2 != 0)
            {
                byte[] padded = new byte[combined.Length + 1];
                Array.Copy(combined, padded, combined.Length);
                combined = padded;
            }

            // Calculate 16-bit XOR checksum
            ushort checksum = CalculateXor16(combined);

            return (checksum, combined);
        }

        /// <summary>
        /// Verify 16-bit checksum
        /// </summary>
        public static bool VerifyXor16(byte[] data, ushort expectedChecksum)
        {
            ushort calculated = CalculateXor16(data);
            return calculated == expectedChecksum;
        }

        /// <summary>
        /// Verify XFlash checksum
        /// </summary>
        public static bool VerifyXFlash32(byte[] data, uint expectedChecksum)
        {
            uint calculated = CalculateXFlash32(data);
            return calculated == expectedChecksum;
        }

        /// <summary>
        /// Calculate CRC16-CCITT checksum
        /// </summary>
        public static ushort CalculateCrc16Ccitt(byte[] data, ushort initial = 0xFFFF)
        {
            if (data == null || data.Length == 0)
                return initial;

            ushort crc = initial;

            foreach (byte b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }

            return crc;
        }

        /// <summary>
        /// Calculate simple byte summation checksum
        /// </summary>
        public static byte CalculateByteSum(byte[] data)
        {
            if (data == null || data.Length == 0)
                return 0;

            byte sum = 0;
            foreach (byte b in data)
            {
                sum += b;
            }
            return sum;
        }

        /// <summary>
        /// Calculate 32-bit word summation checksum
        /// </summary>
        public static uint CalculateWordSum32(byte[] data)
        {
            if (data == null || data.Length == 0)
                return 0;

            uint sum = 0;
            int i = 0;

            // Accumulate every 4 bytes
            for (; i + 3 < data.Length; i += 4)
            {
                sum += BitConverter.ToUInt32(data, i);
            }

            // Handle remaining bytes
            uint remaining = 0;
            int shift = 0;
            for (; i < data.Length; i++)
            {
                remaining |= (uint)(data[i] << shift);
                shift += 8;
            }
            sum += remaining;

            return sum;
        }
    }

    /// <summary>
    /// MTK Data Packer Tools
    /// </summary>
    public static class MtkDataPacker
    {
        /// <summary>
        /// Pack 32-bit unsigned integer (Big-Endian)
        /// </summary>
        public static byte[] PackUInt32BE(uint value)
        {
            return new byte[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        /// <summary>
        /// Pack 32-bit unsigned integer (Little-Endian)
        /// </summary>
        public static byte[] PackUInt32LE(uint value)
        {
            return new byte[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            };
        }

        /// <summary>
        /// Pack 16-bit unsigned integer (Big-Endian)
        /// </summary>
        public static byte[] PackUInt16BE(ushort value)
        {
            return new byte[]
            {
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        /// <summary>
        /// Pack 16-bit unsigned integer (Little-Endian)
        /// </summary>
        public static byte[] PackUInt16LE(ushort value)
        {
            return new byte[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF)
            };
        }

        /// <summary>
        /// Unpack 32-bit unsigned integer (Big-Endian)
        /// </summary>
        public static uint UnpackUInt32BE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 4)
                throw new ArgumentException("Insufficient data");

            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   data[offset + 3];
        }

        /// <summary>
        /// Unpack 32-bit unsigned integer (Little-Endian)
        /// </summary>
        public static uint UnpackUInt32LE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 4)
                throw new ArgumentException("Insufficient data");

            return data[offset] |
                   ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) |
                   ((uint)data[offset + 3] << 24);
        }

        /// <summary>
        /// Unpack 16-bit unsigned integer (Big-Endian)
        /// </summary>
        public static ushort UnpackUInt16BE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 2)
                throw new ArgumentException("Insufficient data");

            return (ushort)(((uint)data[offset] << 8) | data[offset + 1]);
        }

        /// <summary>
        /// Unpack 16-bit unsigned integer (Little-Endian)
        /// </summary>
        public static ushort UnpackUInt16LE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 2)
                throw new ArgumentException("Insufficient data");

            return (ushort)(data[offset] | ((uint)data[offset + 1] << 8));
        }

        /// <summary>
        /// Write 32-bit to buffer (Big-Endian)
        /// </summary>
        public static void WriteUInt32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Write 32-bit to buffer (Little-Endian)
        /// </summary>
        public static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// Write 16-bit to buffer (Big-Endian)
        /// </summary>
        public static void WriteUInt16BE(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Write 16-bit to buffer (Little-Endian)
        /// </summary>
        public static void WriteUInt16LE(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// Pack 64-bit unsigned integer (Little-Endian)
        /// </summary>
        public static byte[] PackUInt64LE(ulong value)
        {
            return new byte[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 32) & 0xFF),
                (byte)((value >> 40) & 0xFF),
                (byte)((value >> 48) & 0xFF),
                (byte)((value >> 56) & 0xFF)
            };
        }

        /// <summary>
        /// Unpack 64-bit unsigned integer (Little-Endian)
        /// </summary>
        public static ulong UnpackUInt64LE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 8)
                throw new ArgumentException("Insufficient data");

            return (ulong)data[offset] |
                   ((ulong)data[offset + 1] << 8) |
                   ((ulong)data[offset + 2] << 16) |
                   ((ulong)data[offset + 3] << 24) |
                   ((ulong)data[offset + 4] << 32) |
                   ((ulong)data[offset + 5] << 40) |
                   ((ulong)data[offset + 6] << 48) |
                   ((ulong)data[offset + 7] << 56);
        }

        /// <summary>
        /// Write 64-bit to buffer (Little-Endian)
        /// </summary>
        public static void WriteUInt64LE(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 4] = (byte)((value >> 32) & 0xFF);
            buffer[offset + 5] = (byte)((value >> 40) & 0xFF);
            buffer[offset + 6] = (byte)((value >> 48) & 0xFF);
            buffer[offset + 7] = (byte)((value >> 56) & 0xFF);
        }

        /// <summary>
        /// Pack 64-bit unsigned integer (Big-Endian)
        /// </summary>
        public static byte[] PackUInt64BE(ulong value)
        {
            return new byte[]
            {
                (byte)((value >> 56) & 0xFF),
                (byte)((value >> 48) & 0xFF),
                (byte)((value >> 40) & 0xFF),
                (byte)((value >> 32) & 0xFF),
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        /// <summary>
        /// Unpack 64-bit unsigned integer (Big-Endian)
        /// </summary>
        public static ulong UnpackUInt64BE(byte[] data, int offset = 0)
        {
            if (data == null || data.Length < offset + 8)
                throw new ArgumentException("Insufficient data");

            return ((ulong)data[offset] << 56) |
                   ((ulong)data[offset + 1] << 48) |
                   ((ulong)data[offset + 2] << 40) |
                   ((ulong)data[offset + 3] << 32) |
                   ((ulong)data[offset + 4] << 24) |
                   ((ulong)data[offset + 5] << 16) |
                   ((ulong)data[offset + 6] << 8) |
                   (ulong)data[offset + 7];
        }

        /// <summary>
        /// Write 64-bit to buffer (Big-Endian)
        /// </summary>
        public static void WriteUInt64BE(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)((value >> 56) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 48) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 40) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 32) & 0xFF);
            buffer[offset + 4] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 5] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 6] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 7] = (byte)(value & 0xFF);
        }
    }
}
