// ============================================================================
// LoveAlways - Spreadtrum HDLC Protocol Implementation
// Spreadtrum/Unisoc HDLC Frame Protocol
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;

namespace LoveAlways.Spreadtrum.Protocol
{
    /// <summary>
    /// Spreadtrum HDLC protocol implementation
    /// Frame format: Flag(0x7E) + Type(1) + Length(2) + Payload(N) + CRC16(2) + Flag(0x7E)
    /// </summary>
    public class HdlcProtocol
    {
        // HDLC frame delimiters
        public const byte HDLC_FLAG = 0x7E;
        public const byte HDLC_ESCAPE = 0x7D;
        public const byte HDLC_ESCAPE_XOR = 0x20;

        // CRC-16 lookup tables
        private static readonly ushort[] CrcTable = GenerateCrcTable();
        private static readonly ushort[] CrcTableModbus = GenerateCrcTableModbus();

        private readonly Action<string> _log;

        // Whether to skip receiver CRC check (Spreadtrum BROM compatibility mode)
        public bool SkipRxCrcCheck { get; set; } = true;

        /// <summary>
        /// Validation mode: true = CRC16 (BROM phase), false = Checksum (FDL phase)
        /// BROM phase uses CRC-16-CCITT, FDL phase uses Spreadtrum proprietary checksum
        /// </summary>
        public bool UseCrc16Mode { get; set; } = true;

        /// <summary>
        /// Transcoding mode: true = enabled (default), false = disabled
        /// When transcoding is enabled, 0x7D and 0x7E bytes are escaped
        /// Transcoding is usually disabled after FDL2 execution to improve efficiency
        /// </summary>
        public bool UseTranscode { get; set; } = true;

        public HdlcProtocol(Action<string> log = null)
        {
            _log = log;
        }

        /// <summary>
        /// Switch to FDL mode (uses Spreadtrum checksum)
        /// </summary>
        public void SetFdlMode()
        {
            UseCrc16Mode = false;
            _log?.Invoke("[HDLC] Switched to FDL mode (Checksum)");
        }

        /// <summary>
        /// Switch to BROM mode (uses CRC16)
        /// </summary>
        public void SetBromMode()
        {
            UseCrc16Mode = true;
            _log?.Invoke("[HDLC] Switched to BROM mode (CRC16)");
        }

        /// <summary>
        /// Toggle checksum mode (Reference: SPRDClientCore)
        /// </summary>
        public void ToggleChecksumMode()
        {
            UseCrc16Mode = !UseCrc16Mode;
            _log?.Invoke($"[HDLC] Toggled checksum mode: {(UseCrc16Mode ? "CRC16" : "Checksum")}");
        }

        /// <summary>
        /// Disable transcoding (Required step for FDL2)
        /// Reference: spd_dump.c - io->flags &= ~FLAGS_TRANSCODE
        /// </summary>
        public void DisableTranscode()
        {
            UseTranscode = false;
            _log?.Invoke("[HDLC] Transcoding disabled");
        }

        /// <summary>
        /// Enable transcoding (default)
        /// </summary>
        public void EnableTranscode()
        {
            UseTranscode = true;
            _log?.Invoke("[HDLC] Transcoding enabled");
        }

        /// <summary>
        /// Build HDLC frame
        /// </summary>
        /// <param name="type">Command type</param>
        /// <param name="payload">Data payload</param>
        /// <returns>Complete HDLC frame</returns>
        public byte[] BuildFrame(byte type, byte[] payload)
        {
            if (payload == null)
                payload = new byte[0];

            using (var ms = new MemoryStream())
            {
                // Frame header
                ms.WriteByte(HDLC_FLAG);

                // Build data part (Big-Endian format, matching Spreadtrum protocol)
                // Format: Type(2, big-endian) + Length(2, big-endian) + Payload + CRC(2, big-endian)
                var data = new List<byte>();

                // Type: 2 bytes, big-endian
                data.Add(0x00);  // Type high byte (SubType)
                data.Add(type);  // Type low byte (Command)

                // Length: 2 bytes, big-endian
                ushort length = (ushort)payload.Length;
                data.Add((byte)((length >> 8) & 0xFF));  // Length high byte
                data.Add((byte)(length & 0xFF));         // Length low byte

                // Add payload
                if (payload.Length > 0)
                    data.AddRange(payload);

                // Choose checksum algorithm based on mode
                ushort checksum;
                if (UseCrc16Mode)
                {
                    // BROM mode: Use CRC-16-CCITT
                    checksum = CalculateCRC16Ccitt(data.ToArray());
                }
                else
                {
                    // FDL mode: Use Spreadtrum proprietary checksum
                    checksum = CalculateSprdChecksum(data.ToArray());
                }

                // Checksum (Big-Endian, matches open source spd_dump)
                data.Add((byte)((checksum >> 8) & 0xFF));  // high byte (Big-Endian)
                data.Add((byte)(checksum & 0xFF));         // low byte (Big-Endian)

                // Escape and write
                foreach (byte b in data)
                {
                    WriteEscaped(ms, b);
                }

                // Frame trailer
                ms.WriteByte(HDLC_FLAG);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build simple command frame (no payload)
        /// </summary>
        public byte[] BuildCommand(byte type)
        {
            return BuildFrame(type, null);
        }

        /// <summary>
        /// Build data frame with address
        /// </summary>
        public byte[] BuildDataFrame(byte type, uint address, byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                // Write address (4 bytes, little-endian)
                ms.Write(BitConverter.GetBytes(address), 0, 4);

                // Write data length (4 bytes)
                ms.Write(BitConverter.GetBytes((uint)data.Length), 0, 4);

                // Write data
                ms.Write(data, 0, data.Length);

                return BuildFrame(type, ms.ToArray());
            }
        }

        /// <summary>
        /// Parse HDLC frame
        /// </summary>
        /// <param name="frame">Original frame data</param>
        /// <returns>Command type and payload data</returns>
        public HdlcFrame ParseFrame(byte[] frame)
        {
            HdlcFrame result;
            HdlcParseError error;
            if (!TryParseFrame(frame, out result, out error))
            {
                throw new InvalidDataException(GetErrorMessage(error));
            }
            return result;
        }

        /// <summary>
        /// Try to parse HDLC frame (no exceptions)
        /// Supports automatic checksum mode switching (Reference: SPRDClientCore)
        /// </summary>
        public bool TryParseFrame(byte[] frame, out HdlcFrame result, out HdlcParseError error)
        {
            result = null;
            error = HdlcParseError.None;

            if (frame == null || frame.Length < 7)
            {
                error = HdlcParseError.FrameTooShort;
                return false;
            }

            if (frame[0] != HDLC_FLAG || frame[frame.Length - 1] != HDLC_FLAG)
            {
                error = HdlcParseError.InvalidDelimiter;
                return false;
            }

            // Unescape data
            var data = new List<byte>();
            bool escaped = false;

            for (int i = 1; i < frame.Length - 1; i++)
            {
                if (escaped)
                {
                    data.Add((byte)(frame[i] ^ HDLC_ESCAPE_XOR));
                    escaped = false;
                }
                else if (frame[i] == HDLC_ESCAPE)
                {
                    escaped = true;
                }
                else
                {
                    data.Add(frame[i]);
                }
            }

            if (data.Count < 6) // Type(2) + Length(2) + CRC(2)
            {
                error = HdlcParseError.FrameIncomplete;
                return false;
            }

            // Parse fields - Spreadtrum uses Big-Endian format
            // Format: [Type Hi] [Type Lo] [Length Hi] [Length Lo] [Payload...] [CRC Hi] [CRC Lo]
            byte subType = data[0];  // Type high byte (usually 0x00)
            byte type = data[1];     // Type low byte (Command)
            ushort length = (ushort)((data[2] << 8) | data[3]);  // Big-endian

            // Extract payload
            byte[] payload = new byte[length];
            if (length > 0)
            {
                if (data.Count < 4 + length + 2)
                {
                    error = HdlcParseError.PayloadMismatch;
                    return false;
                }

                for (int i = 0; i < length; i++)
                    payload[i] = data[4 + i];
            }

            // Validate CRC (Big-Endian, matches open source spd_dump)
            int crcOffset = 4 + length;
            ushort receivedCrc = (ushort)((data[crcOffset] << 8) | data[crcOffset + 1]);  // Big-Endian

            // Spreadtrum BROM uses different CRC algorithms, skip validation in compatibility mode
            if (!SkipRxCrcCheck)
            {
                byte[] crcData = data.GetRange(0, crcOffset).ToArray();

                // Calculate using current validation mode
                ushort calculatedCrc = UseCrc16Mode
                    ? CalculateCRC16Ccitt(crcData)
                    : CalculateSprdChecksum(crcData);

                if (receivedCrc != calculatedCrc)
                {
                    // Automatically try another checksum mode (Reference: SPRDClientCore)
                    ushort alternativeCrc = UseCrc16Mode
                        ? CalculateSprdChecksum(crcData)
                        : CalculateCRC16Ccitt(crcData);

                    if (receivedCrc == alternativeCrc)
                    {
                        // Auto-switch checksum mode
                        UseCrc16Mode = !UseCrc16Mode;
                        _log?.Invoke(string.Format("[HDLC] Auto-switched checksum mode: {0}",
                            UseCrc16Mode ? "CRC16" : "Checksum"));
                    }
                    else
                    {
                        _log?.Invoke(string.Format("[HDLC] CRC check failed: received=0x{0:X4}, CRC16=0x{1:X4}, Checksum=0x{2:X4}",
                            receivedCrc,
                            UseCrc16Mode ? calculatedCrc : alternativeCrc,
                            UseCrc16Mode ? alternativeCrc : calculatedCrc));
                        error = HdlcParseError.CrcMismatch;
                        return false;
                    }
                }
            }

            result = new HdlcFrame
            {
                Type = type,
                Length = length,
                Payload = payload,
                Crc = receivedCrc
            };
            return true;
        }

        private string GetErrorMessage(HdlcParseError error)
        {
            switch (error)
            {
                case HdlcParseError.FrameTooShort: return "Frame too short";
                case HdlcParseError.InvalidDelimiter: return "Invalid frame delimiter";
                case HdlcParseError.FrameIncomplete: return "Frame incomplete";
                case HdlcParseError.PayloadMismatch: return "Payload length mismatch";
                case HdlcParseError.CrcMismatch: return "CRC check failed";
                default: return "Unknown error";
            }
        }

        /// <summary>
        /// Try to extract complete frame from data stream
        /// </summary>
        public bool TryExtractFrame(byte[] buffer, int length, out byte[] frame, out int consumed)
        {
            frame = null;
            consumed = 0;

            // Search for frame start
            int startIndex = -1;
            for (int i = 0; i < length; i++)
            {
                if (buffer[i] == HDLC_FLAG)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
                return false;

            // Search for frame end
            for (int i = startIndex + 1; i < length; i++)
            {
                if (buffer[i] == HDLC_FLAG)
                {
                    // Found complete frame
                    int frameLength = i - startIndex + 1;
                    frame = new byte[frameLength];
                    Array.Copy(buffer, startIndex, frame, 0, frameLength);
                    consumed = i + 1;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Write escaped byte
        /// </summary>
        private void WriteEscaped(Stream stream, byte b)
        {
            if (b == HDLC_FLAG || b == HDLC_ESCAPE)
            {
                stream.WriteByte(HDLC_ESCAPE);
                stream.WriteByte((byte)(b ^ HDLC_ESCAPE_XOR));
            }
            else
            {
                stream.WriteByte(b);
            }
        }

        /// <summary>
        /// Calculate CRC-16-CCITT (Algorithm used by Spreadtrum BROM)
        /// Uses standard polynomial 0x1021 (MSB-first), matches open source spd_dump
        /// </summary>
        public ushort CalculateCRC16Ccitt(byte[] data)
        {
            // Reference: spd_crc16 implementation in spd_dump.c
            // Polynomial: 0x1021 (CCITT), MSB-first, initial value 0
            uint crc = 0;

            foreach (byte b in data)
            {
                crc ^= (uint)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (crc << 1) ^ 0x11021;
                    else
                        crc <<= 1;
                }
            }

            return (ushort)(crc & 0xFFFF);
        }

        /// <summary>
        /// Calculate CRC-16-CCITT (Legacy LSB-first, for compatibility)
        /// </summary>
        public ushort CalculateCRC16CcittLsb(byte[] data)
        {
            ushort crc = 0;

            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0x8408);  // Inverted polynomial
                    else
                        crc >>= 1;
                }
            }

            return crc;
        }

        /// <summary>
        /// Calculate Spreadtrum proprietary checksum (used for communication after FDL1)
        /// Reference: calc_sprdcheck implementation in sprdproto/sprd_io.c
        /// </summary>
        public ushort CalculateSprdChecksum(byte[] data)
        {
            uint ctr = 0;
            int len = data.Length;
            int i = 0;

            // Process 2 bytes at a time (Little-Endian)
            while (len > 1)
            {
                ctr += (uint)(data[i] | (data[i + 1] << 8));  // Little-Endian
                i += 2;
                len -= 2;
            }

            // Process remaining single byte
            if (len > 0)
                ctr += data[i];

            // Fold to 16-bit and invert
            ctr = (ctr >> 16) + (ctr & 0xFFFF);
            ctr = ~(ctr + (ctr >> 16)) & 0xFFFF;

            // Critical: Byte swap (consistent with sprdproto)
            return (ushort)((ctr >> 8) | ((ctr & 0xFF) << 8));
        }

        /// <summary>
        /// Verify CRC (for debugging)
        /// </summary>
        public bool VerifyCrc(byte[] data, ushort expectedCrc)
        {
            ushort calculated = CalculateCRC16Ccitt(data);
            _log?.Invoke(string.Format("[HDLC] CRC Verification: Calculated=0x{0:X4}, Expected=0x{1:X4}", calculated, expectedCrc));
            return calculated == expectedCrc;
        }

        /// <summary>
        /// Calculate CRC-16 (Legacy method, for compatibility)
        /// </summary>
        public ushort CalculateCRC16(byte[] data)
        {
            ushort crc = 0;

            foreach (byte b in data)
            {
                crc = (ushort)((crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF]);
            }

            return crc;
        }

        /// <summary>
        /// Generate CRC-16 lookup table (Legacy method, for compatibility)
        /// </summary>
        private static ushort[] GenerateCrcTable()
        {
            ushort[] table = new ushort[256];
            const ushort polynomial = 0x8408;

            for (int i = 0; i < 256; i++)
            {
                ushort crc = (ushort)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ polynomial);
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }

            return table;
        }

        /// <summary>
        /// Generate CRC-16-MODBUS lookup table (Legacy method, for compatibility)
        /// </summary>
        private static ushort[] GenerateCrcTableModbus()
        {
            ushort[] table = new ushort[256];
            const ushort polynomial = 0xA001;

            for (int i = 0; i < 256; i++)
            {
                ushort crc = (ushort)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ polynomial);
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }

            return table;
        }

        /// <summary>
        /// Format frame as hex string (for debugging)
        /// </summary>
        public static string FormatHex(byte[] data, int maxLength = 64)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            int displayLength = Math.Min(data.Length, maxLength);
            var hex = BitConverter.ToString(data, 0, displayLength).Replace("-", " ");

            if (data.Length > maxLength)
                hex += string.Format(" ... ({0} bytes total)", data.Length);

            return hex;
        }
    }

    /// <summary>
    /// HDLC frame structure
    /// </summary>
    public class HdlcFrame
    {
        public byte Type { get; set; }
        public ushort Length { get; set; }
        public byte[] Payload { get; set; }
        public ushort Crc { get; set; }

        public override string ToString()
        {
            return string.Format("HdlcFrame[Type=0x{0:X2}, Length={1}, CRC=0x{2:X4}]", Type, Length, Crc);
        }
    }

    /// <summary>
    /// HDLC parsing error types
    /// </summary>
    public enum HdlcParseError
    {
        None,               // No error
        FrameTooShort,      // Frame data too short
        InvalidDelimiter,   // Invalid frame delimiter
        FrameIncomplete,    // Frame data incomplete
        PayloadMismatch,    // Payload length mismatch
        CrcMismatch         // CRC check failed
    }
}
