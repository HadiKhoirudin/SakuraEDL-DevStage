// ============================================================================
// LoveAlways - CRC32 Calculation Utility
// Reference: mtkclient checksum implementation
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// CRC32 Calculation Utility (Compatible with MTK DA)
    /// </summary>
    public static class MtkCrc32
    {
        // CRC32 lookup table (Standard polynomial 0xEDB88320)
        private static readonly uint[] CrcTable = new uint[256];
        
        static MtkCrc32()
        {
            // Initialize CRC table
            const uint polynomial = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                CrcTable[i] = crc;
            }
        }

        /// <summary>
        /// Calculate CRC32 checksum for data
        /// </summary>
        public static uint Compute(byte[] data)
        {
            return Compute(data, 0, data.Length);
        }

        /// <summary>
        /// Calculate CRC32 checksum for data (specified range)
        /// </summary>
        public static uint Compute(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// Incremental CRC32 calculation (initialization)
        /// </summary>
        public static uint Initialize()
        {
            return 0xFFFFFFFF;
        }

        /// <summary>
        /// Incremental CRC32 calculation (update)
        /// </summary>
        public static uint Update(uint crc, byte[] data, int offset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }

        /// <summary>
        /// Incremental CRC32 calculation (finalization)
        /// </summary>
        public static uint Finalize(uint crc)
        {
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// Verify CRC32 checksum for data
        /// </summary>
        public static bool Verify(byte[] data, uint expectedCrc)
        {
            return Compute(data) == expectedCrc;
        }

        /// <summary>
        /// Verify CRC32 checksum for data (specified range)
        /// </summary>
        public static bool Verify(byte[] data, int offset, int length, uint expectedCrc)
        {
            return Compute(data, offset, length) == expectedCrc;
        }
    }
}
