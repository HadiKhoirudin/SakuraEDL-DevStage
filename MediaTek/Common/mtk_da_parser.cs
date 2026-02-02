// ============================================================================
// LoveAlways - MediaTek DA Parser
// MediaTek Download Agent Parser
// ============================================================================
// Reference: Penumbra project https://github.com/shomykohai/penumbra/blob/main/src/da/da.rs
// Supports multi-SoC DA file parsing, complete DA structure extraction
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.Text;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// DA Region
    /// </summary>
    public class DaRegion
    {
        /// <summary>Offset position in DA file</summary>
        public uint FileOffset { get; set; }

        /// <summary>Total length (including signature)</summary>
        public uint TotalLength { get; set; }

        /// <summary>Memory address loaded into device</summary>
        public uint LoadAddress { get; set; }

        /// <summary>Region length (excluding signature)</summary>
        public uint RegionLength { get; set; }

        /// <summary>Signature length</summary>
        public uint SignatureLength { get; set; }

        /// <summary>Actual data (excluding signature)</summary>
        public byte[] Data { get; set; }

        /// <summary>Signature data</summary>
        public byte[] Signature { get; set; }

        public override string ToString()
        {
            return $"Region@0x{LoadAddress:X8}: Offset=0x{FileOffset:X}, Length=0x{TotalLength:X} (Data=0x{RegionLength:X}, Sig=0x{SignatureLength:X})";
        }
    }

    /// <summary>
    /// DA Entry (DA configuration for a single chip)
    /// </summary>
    public class DaEntry
    {
        /// <summary>Magic (usually "DADA")</summary>
        public ushort Magic { get; set; }

        /// <summary>HW Code (chip code, e.g., 0x6768 for MT6768)</summary>
        public ushort HwCode { get; set; }

        /// <summary>HW Sub Code (chip sub-code, used to distinguish revisions)</summary>
        public ushort HwSubCode { get; set; }

        /// <summary>HW Version (chip version)</summary>
        public ushort HwVersion { get; set; }

        /// <summary>Entry Region Index</summary>
        public ushort RegionIndex { get; set; }

        /// <summary>Region count</summary>
        public ushort RegionCount { get; set; }

        /// <summary>All region list</summary>
        public List<DaRegion> Regions { get; set; }

        public DaEntry()
        {
            Regions = new List<DaRegion>();
        }

        public override string ToString()
        {
            return $"DA Entry: HW=0x{HwCode:X4}, SubCode=0x{HwSubCode:X4}, Ver=0x{HwVersion:X4}, Regions={RegionCount}";
        }
    }

    /// <summary>
    /// DA File (complete DA file)
    /// </summary>
    public class DaFile
    {
        /// <summary>DA file magic string ("MTK_DOWNLOAD_AGENT")</summary>
        public string Magic { get; set; }

        /// <summary>DA file ID ("MTK_AllInOne_DA_v3" for XFlash, "MTK_DA_v6" for XML)</summary>
        public string FileId { get; set; }

        /// <summary>DA version (usually 4)</summary>
        public uint Version { get; set; }

        /// <summary>DA Magic (0x99886622)</summary>
        public uint DaMagic { get; set; }

        /// <summary>SoC count (one DA file can contain configurations for multiple chips)</summary>
        public ushort SocCount { get; set; }

        /// <summary>All DA Entry list</summary>
        public List<DaEntry> Entries { get; set; }

        /// <summary>Whether it is V6 (XML) DA</summary>
        public bool IsV6 => FileId?.Contains("v6") == true;

        /// <summary>Whether it is V5 (XFlash) DA</summary>
        public bool IsV5 => FileId?.Contains("v3") == true;  // "AllInOne_DA_v3" is actually V5

        public DaFile()
        {
            Entries = new List<DaEntry>();
        }

        /// <summary>
        /// Find corresponding DA Entry based on HW Code
        /// </summary>
        public DaEntry FindEntry(ushort hwCode)
        {
            return Entries.Find(e => e.HwCode == hwCode);
        }

        public override string ToString()
        {
            return $"DA File: {FileId}, Version={Version}, SoCs={SocCount}, Type={(IsV6 ? "V6/XML" : IsV5 ? "V5/XFlash" : "Legacy")}";
        }
    }

    /// <summary>
    /// MTK DA Parser
    /// Supports Legacy (V3), XFlash (V5), XML (V6) DA files
    /// </summary>
    public static class MtkDaParser
    {
        private const string EXPECTED_MAGIC = "MTK_DOWNLOAD_AGENT";
        private const uint EXPECTED_DA_MAGIC = 0x99886622;

        private const int LEGACY_ENTRY_SIZE = 0xD8;
        private const int XFLASH_ENTRY_SIZE = 0xDC;
        private const int REGION_SIZE = 0x20;

        /// <summary>
        /// Parse DA file
        /// </summary>
        public static DaFile Parse(byte[] daData)
        {
            if (daData == null || daData.Length < 0x100)
                throw new ArgumentException("Invalid or too small DA file data");

            var da = new DaFile();
            int offset = 0;

            // Read Magic (0x00-0x12, 18 bytes)
            da.Magic = ReadString(daData, offset, 18);
            offset += 0x20;  // Jump to 0x20

            if (!da.Magic.StartsWith(EXPECTED_MAGIC))
                throw new FormatException($"Invalid DA Magic: {da.Magic}");

            // Read File ID (0x20-0x60, 64 bytes)
            da.FileId = ReadString(daData, offset, 64).TrimEnd('\0');
            offset += 0x40;  // Jump to 0x60

            // Read Version (0x60, 4 bytes)
            da.Version = ReadUInt32(daData, offset);
            offset += 4;

            // Read DA Magic (0x64, 4 bytes)
            da.DaMagic = ReadUInt32(daData, offset);
            offset += 4;

            if (da.DaMagic != EXPECTED_DA_MAGIC)
                throw new FormatException($"Invalid DA Magic: 0x{da.DaMagic:X8}, expected: 0x{EXPECTED_DA_MAGIC:X8}");

            // Read SoC Count (0x68, 4 bytes)
            da.SocCount = ReadUInt16(daData, offset);
            offset += 4;  // Skip full 4 bytes

            // Determine Entry size (Legacy=0xD8, XFlash/XML=0xDC)
            int entrySize = da.IsV5 || da.IsV6 ? XFLASH_ENTRY_SIZE : LEGACY_ENTRY_SIZE;

            // Parse all DA Entries
            for (int i = 0; i < da.SocCount; i++)
            {
                var entry = ParseEntry(daData, offset, entrySize, daData);
                if (entry != null)
                {
                    da.Entries.Add(entry);
                }
                offset += entrySize;
            }

            return da;
        }

        /// <summary>
        /// Parse single DA Entry
        /// </summary>
        private static DaEntry ParseEntry(byte[] data, int entryOffset, int entrySize, byte[] fullDaData)
        {
            if (entryOffset + entrySize > data.Length)
                return null;

            var entry = new DaEntry();
            int offset = entryOffset;

            // Magic (0x00, 2 bytes) - "DADA"
            entry.Magic = ReadUInt16(data, offset);
            offset += 2;

            // HW Code (0x02, 2 bytes)
            entry.HwCode = ReadUInt16(data, offset);
            offset += 2;

            // HW Sub Code (0x04, 2 bytes)
            entry.HwSubCode = ReadUInt16(data, offset);
            offset += 2;

            // HW Version (0x06, 2 bytes)
            entry.HwVersion = ReadUInt16(data, offset);
            offset += 2;

            // Skip reserved fields to 0x10
            offset = entryOffset + 0x10;

            // Entry Region Index (0x10, 2 bytes)
            entry.RegionIndex = ReadUInt16(data, offset);
            offset += 2;

            // Entry Region Count (0x12, 2 bytes)
            entry.RegionCount = ReadUInt16(data, offset);
            offset += 2;

            // Region Table starts at 0x14
            offset = entryOffset + 0x14;

            // Parse all regions
            for (int i = 0; i < entry.RegionCount && i < 6; i++)  // Max 6 regions
            {
                if (offset + REGION_SIZE > entryOffset + entrySize)
                    break;

                var region = ParseRegion(data, offset, fullDaData);
                if (region != null)
                {
                    entry.Regions.Add(region);
                }
                offset += REGION_SIZE;
            }

            return entry;
        }

        /// <summary>
        /// Parse single Region
        /// </summary>
        private static DaRegion ParseRegion(byte[] entryData, int regionOffset, byte[] fullDaData)
        {
            var region = new DaRegion();

            // Offset (0x00, 4 bytes)
            region.FileOffset = ReadUInt32(entryData, regionOffset);

            // Total Length (0x04, 4 bytes)
            region.TotalLength = ReadUInt32(entryData, regionOffset + 4);

            // Load Address (0x08, 4 bytes)
            region.LoadAddress = ReadUInt32(entryData, regionOffset + 8);

            // Region Length (0x0C, 4 bytes)
            region.RegionLength = ReadUInt32(entryData, regionOffset + 12);

            // Signature Length (0x10, 4 bytes)
            region.SignatureLength = ReadUInt32(entryData, regionOffset + 16);

            // Extract actual data (if within DA file range)
            if (region.FileOffset + region.TotalLength <= fullDaData.Length)
            {
                // Extract region data (excluding signature)
                region.Data = new byte[region.RegionLength];
                Array.Copy(fullDaData, region.FileOffset, region.Data, 0, region.RegionLength);

                // Extract signature data
                if (region.SignatureLength > 0 && region.FileOffset + region.RegionLength + region.SignatureLength <= fullDaData.Length)
                {
                    region.Signature = new byte[region.SignatureLength];
                    Array.Copy(fullDaData, region.FileOffset + region.RegionLength, region.Signature, 0, region.SignatureLength);
                }
            }

            return region;
        }

        #region Helper Methods

        private static string ReadString(byte[] data, int offset, int maxLength)
        {
            int length = 0;
            for (int i = 0; i < maxLength && offset + i < data.Length; i++)
            {
                if (data[offset + i] == 0) break;
                length++;
            }
            return Encoding.ASCII.GetString(data, offset, length);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        #endregion

        #region DA Extraction Helper Methods

        /// <summary>
        /// Extract DA1 data (first Region)
        /// </summary>
        public static byte[] ExtractDa1(DaFile daFile, ushort hwCode)
        {
            var entry = daFile.FindEntry(hwCode);
            if (entry == null || entry.Regions.Count == 0)
                return null;

            return entry.Regions[0].Data;
        }

        /// <summary>
        /// Extract DA2 data (second Region)
        /// </summary>
        public static byte[] ExtractDa2(DaFile daFile, ushort hwCode)
        {
            var entry = daFile.FindEntry(hwCode);
            if (entry == null || entry.Regions.Count < 2)
                return null;

            return entry.Regions[1].Data;
        }

        /// <summary>
        /// Get DA1 signature length
        /// </summary>
        public static uint GetDa1SigLen(DaFile daFile, ushort hwCode)
        {
            var entry = daFile.FindEntry(hwCode);
            if (entry == null || entry.Regions.Count == 0)
                return 0;

            return entry.Regions[0].SignatureLength;
        }

        /// <summary>
        /// Get DA2 signature length
        /// </summary>
        public static uint GetDa2SigLen(DaFile daFile, ushort hwCode)
        {
            var entry = daFile.FindEntry(hwCode);
            if (entry == null || entry.Regions.Count < 2)
                return 0;

            return entry.Regions[1].SignatureLength;
        }

        #endregion
    }
}
