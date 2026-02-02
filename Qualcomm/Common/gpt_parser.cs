// ============================================================================
// LoveAlways - GPT Partition Table Parser (inspired by gpttool logic)
// GPT Partition Table Parser - Enhanced version based on gpttool
// ============================================================================
// Module: Qualcomm.Common
// Function: Parse GPT partition table, supports auto sector size detection, CRC verification, slot detection
// ============================================================================

using LoveAlways.Qualcomm.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// GPT Header Info
    /// </summary>
    public class GptHeaderInfo
    {
        public string Signature { get; set; }           // "EFI PART"
        public uint Revision { get; set; }              // Revision (usually 0x00010000)
        public uint HeaderSize { get; set; }            // Header size (usually 92)
        public uint HeaderCrc32 { get; set; }           // Header CRC32
        public ulong MyLba { get; set; }                // Current Header LBA
        public ulong AlternateLba { get; set; }         // Alternate Header LBA
        public ulong FirstUsableLba { get; set; }       // First usable LBA
        public ulong LastUsableLba { get; set; }        // Last usable LBA
        public string DiskGuid { get; set; }            // Disk GUID
        public ulong PartitionEntryLba { get; set; }    // Partition entry start LBA
        public uint NumberOfPartitionEntries { get; set; }  // Number of partition entries
        public uint SizeOfPartitionEntry { get; set; }  // Size per entry (usually 128)
        public uint PartitionEntryCrc32 { get; set; }   // Partition entry CRC32

        public bool IsValid { get; set; }
        public bool CrcValid { get; set; }
        public string GptType { get; set; }             // "gptmain" or "gptbackup"
        public int SectorSize { get; set; }             // Sector size (512 or 4096)
    }

    /// <summary>
    /// Slot Info
    /// </summary>
    public class SlotInfo
    {
        public string CurrentSlot { get; set; }         // "a", "b", "undefined", "nonexistent"
        public string OtherSlot { get; set; }
        public bool HasAbPartitions { get; set; }
    }

    /// <summary>
    /// GPT Parse Result
    /// </summary>
    public class GptParseResult
    {
        public GptHeaderInfo Header { get; set; }
        public List<PartitionInfo> Partitions { get; set; }
        public SlotInfo SlotInfo { get; set; }
        public int Lun { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public GptParseResult()
        {
            Partitions = new List<PartitionInfo>();
            SlotInfo = new SlotInfo { CurrentSlot = "nonexistent", OtherSlot = "nonexistent" };
        }
    }

    /// <summary>
    /// GPT Partition Table Parser (inspired by gpttool)
    /// </summary>
    public class GptParser
    {
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;

        // GPT Signature
        private static readonly byte[] GPT_SIGNATURE = Encoding.ASCII.GetBytes("EFI PART");

        // A/B partition attribute flags
        private const int AB_FLAG_OFFSET = 6;
        private const int AB_PARTITION_ATTR_SLOT_ACTIVE = 0x1 << 2;

        // Static CRC32 table (avoids regenerating every time)
        private static readonly uint[] CRC32_TABLE = GenerateStaticCrc32Table();

        public GptParser(Action<string> log = null, Action<string> logDetail = null)
        {
            _log = log ?? (s => { });
            _logDetail = logDetail ?? _log;
        }

        #region Main Parsing Methods

        /// <summary>
        /// Parse GPT Data
        /// </summary>
        public GptParseResult Parse(byte[] gptData, int lun, int defaultSectorSize = 4096)
        {
            var result = new GptParseResult { Lun = lun };

            try
            {
                if (gptData == null || gptData.Length < 512)
                {
                    result.ErrorMessage = "GPT data too small";
                    return result;
                }

                // 1. Find GPT Header and auto detect sector size
                int headerOffset = FindGptHeader(gptData);
                if (headerOffset < 0)
                {
                    result.ErrorMessage = "GPT signature not found";
                    return result;
                }

                // 2. Parse GPT Header
                var header = ParseGptHeader(gptData, headerOffset, defaultSectorSize);
                if (!header.IsValid)
                {
                    result.ErrorMessage = "GPT Header invalid";
                    return result;
                }
                result.Header = header;

                // 3. Auto detect sector size (reference gpttool)
                // Disk_SecSize_b_Dec = HeaderArea_Start_InF_b_Dec / HeaderArea_Start_Sec_Dec
                if (header.MyLba > 0 && headerOffset > 0)
                {
                    int detectedSectorSize = headerOffset / (int)header.MyLba;
                    if (detectedSectorSize == 512 || detectedSectorSize == 4096)
                    {
                        header.SectorSize = detectedSectorSize;
                        _logDetail(string.Format("[GPT] Auto detected sector size: {0} bytes (Header offset={1}, MyLBA={2})",
                            detectedSectorSize, headerOffset, header.MyLba));
                    }
                    else
                    {
                        // Try to infer according to partition entry LBA
                        if (header.PartitionEntryLba == 2)
                        {
                            // Standard case: partition entry follows header
                            header.SectorSize = defaultSectorSize;
                            _logDetail(string.Format("[GPT] Using default sector size: {0} bytes", defaultSectorSize));
                        }
                        else
                        {
                            header.SectorSize = defaultSectorSize;
                        }
                    }
                }
                else
                {
                    header.SectorSize = defaultSectorSize;
                    _logDetail(string.Format("[GPT] MyLBA=0, using default sector size: {0} bytes", defaultSectorSize));
                }

                // 4. Verify CRC (optional)
                header.CrcValid = VerifyCrc32(gptData, headerOffset, header);

                // 5. Parse partition entries
                result.Partitions = ParsePartitionEntries(gptData, headerOffset, header, lun);

                // 6. Detect A/B slots
                result.SlotInfo = DetectSlot(result.Partitions);

                result.Success = true;
                _logDetail(string.Format("[GPT] LUN{0}: {1} partitions, slot: {2}",
                    lun, result.Partitions.Count, result.SlotInfo.CurrentSlot));
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _log(string.Format("[GPT] Parse exception: {0}", ex.Message));
            }

            return result;
        }

        #endregion

        #region GPT Header Parsing

        /// <summary>
        /// Find GPT Header position
        /// </summary>
        private int FindGptHeader(byte[] data)
        {
            // Common offset locations
            int[] searchOffsets = { 4096, 512, 0, 4096 * 2, 512 * 2 };

            foreach (int offset in searchOffsets)
            {
                if (offset + 92 <= data.Length && MatchSignature(data, offset))
                {
                    _logDetail(string.Format("[GPT] Found GPT Header at offset {0}", offset));
                    return offset;
                }
            }

            // Brute force search (every 512 bytes)
            for (int i = 0; i <= data.Length - 92; i += 512)
            {
                if (MatchSignature(data, i))
                {
                    _logDetail(string.Format("[GPT] Brute force search: found GPT Header at offset {0}", i));
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Match GPT signature
        /// </summary>
        private bool MatchSignature(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return false;
            for (int i = 0; i < 8; i++)
            {
                if (data[offset + i] != GPT_SIGNATURE[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Parse GPT Header
        /// </summary>
        private GptHeaderInfo ParseGptHeader(byte[] data, int offset, int defaultSectorSize)
        {
            var header = new GptHeaderInfo
            {
                SectorSize = defaultSectorSize
            };

            try
            {
                // Signature (0-8)
                header.Signature = Encoding.ASCII.GetString(data, offset, 8);
                if (header.Signature != "EFI PART")
                {
                    header.IsValid = false;
                    return header;
                }

                // Revision (8-12)
                header.Revision = BitConverter.ToUInt32(data, offset + 8);

                // Header size (12-16)
                header.HeaderSize = BitConverter.ToUInt32(data, offset + 12);

                // Header CRC32 (16-20)
                header.HeaderCrc32 = BitConverter.ToUInt32(data, offset + 16);

                // Reserved (20-24)

                // MyLBA (24-32)
                header.MyLba = BitConverter.ToUInt64(data, offset + 24);

                // AlternateLBA (32-40)
                header.AlternateLba = BitConverter.ToUInt64(data, offset + 32);

                // FirstUsableLBA (40-48)
                header.FirstUsableLba = BitConverter.ToUInt64(data, offset + 40);

                // LastUsableLBA (48-56)
                header.LastUsableLba = BitConverter.ToUInt64(data, offset + 48);

                // DiskGUID (56-72)
                header.DiskGuid = FormatGuid(data, offset + 56);

                // PartitionEntryLBA (72-80)
                header.PartitionEntryLba = BitConverter.ToUInt64(data, offset + 72);

                // NumberOfPartitionEntries (80-84)
                header.NumberOfPartitionEntries = BitConverter.ToUInt32(data, offset + 80);

                // SizeOfPartitionEntry (84-88)
                header.SizeOfPartitionEntry = BitConverter.ToUInt32(data, offset + 84);

                // PartitionEntryCRC32 (88-92)
                header.PartitionEntryCrc32 = BitConverter.ToUInt32(data, offset + 88);

                // Determine GPT type
                if (header.MyLba != 0 && header.AlternateLba != 0)
                {
                    header.GptType = header.MyLba < header.AlternateLba ? "gptmain" : "gptbackup";
                }
                else if (header.MyLba != 0)
                {
                    header.GptType = "gptmain";
                }
                else
                {
                    header.GptType = "gptbackup";
                }

                header.IsValid = true;
            }
            catch
            {
                header.IsValid = false;
            }

            return header;
        }

        #endregion

        #region Partition Entry Parsing

        /// <summary>
        /// Parse Partition Entries
        /// </summary>
        private List<PartitionInfo> ParsePartitionEntries(byte[] data, int headerOffset, GptHeaderInfo header, int lun)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                int sectorSize = header.SectorSize > 0 ? header.SectorSize : 4096;

                _logDetail(string.Format("[GPT] LUN{0} start parsing partition entries (data length={1}, HeaderOffset={2}, SectorSize={3})",
                    lun, data.Length, headerOffset, sectorSize));
                _logDetail(string.Format("[GPT] Header info: PartitionEntryLba={0}, NumberOfEntries={1}, EntrySize={2}, FirstUsableLba={3}",
                    header.PartitionEntryLba, header.NumberOfPartitionEntries, header.SizeOfPartitionEntry, header.FirstUsableLba));

                // ========== Calculate partition entry start position - Multiple strategies ==========
                int entryOffset = -1;
                string usedStrategy = "";

                // Strategy 1: Use PartitionEntryLba specified in Header
                if (header.PartitionEntryLba > 0)
                {
                    long calcOffset = (long)header.PartitionEntryLba * sectorSize;
                    if (calcOffset > 0 && calcOffset < data.Length - 128)
                    {
                        // Verify if this offset has valid partition entries
                        if (HasValidPartitionEntry(data, (int)calcOffset))
                        {
                            entryOffset = (int)calcOffset;
                            usedStrategy = string.Format("Strategy 1 (PartitionEntryLba): {0} * {1} = {2}",
                                header.PartitionEntryLba, sectorSize, entryOffset);
                        }
                        else
                        {
                            _logDetail(string.Format("[GPT] Strategy 1 calculated offset {0} has no valid partition, trying other strategies", calcOffset));
                        }
                    }
                }

                // Strategy 2: Try different sector size calculations
                if (entryOffset < 0 && header.PartitionEntryLba > 0)
                {
                    int[] trySectorSizes = { 512, 4096 };
                    foreach (int trySectorSize in trySectorSizes)
                    {
                        if (trySectorSize == sectorSize) continue; // Skip already tried

                        long calcOffset = (long)header.PartitionEntryLba * trySectorSize;
                        if (calcOffset > 0 && calcOffset < data.Length - 128 && HasValidPartitionEntry(data, (int)calcOffset))
                        {
                            entryOffset = (int)calcOffset;
                            sectorSize = trySectorSize; // Update sector size
                            header.SectorSize = trySectorSize;
                            usedStrategy = string.Format("Strategy 2 (Try sector size {0}B): {1} * {0} = {2}",
                                trySectorSize, header.PartitionEntryLba, entryOffset);
                            break;
                        }
                    }
                }

                // Strategy 3: Xiaomi/OPPO devices use 512B sectors, partition entries usually at LBA 2 = 1024
                if (entryOffset < 0)
                {
                    int xiaomiOffset = 1024; // LBA 2 * 512B
                    if (xiaomiOffset < data.Length - 128 && HasValidPartitionEntry(data, xiaomiOffset))
                    {
                        entryOffset = xiaomiOffset;
                        usedStrategy = string.Format("Strategy 3 (512B sector standard): offset {0}", entryOffset);
                    }
                }

                // Strategy 4: 4KB sectors, partition entries at LBA 2 = 8192
                if (entryOffset < 0)
                {
                    int ufsOffset = 8192; // LBA 2 * 4096B
                    if (ufsOffset < data.Length - 128 && HasValidPartitionEntry(data, ufsOffset))
                    {
                        entryOffset = ufsOffset;
                        usedStrategy = string.Format("Strategy 4 (4KB sector standard): offset {0}", entryOffset);
                    }
                }

                // Strategy 5: Partition entries follow Header (different sector sizes)
                if (entryOffset < 0)
                {
                    int[] tryGaps = { 512, 4096, 1024, 2048 };
                    foreach (int gap in tryGaps)
                    {
                        int relativeOffset = headerOffset + gap;
                        if (relativeOffset < data.Length - 128 && HasValidPartitionEntry(data, relativeOffset))
                        {
                            entryOffset = relativeOffset;
                            usedStrategy = string.Format("Strategy 5 (Header+{0}): {1} + {0} = {2}",
                                gap, headerOffset, entryOffset);
                            break;
                        }
                    }
                }

                // Strategy 6: Brute force probe more common offsets
                if (entryOffset < 0)
                {
                    // Common offsets: various combinations of sector sizes and LBAs
                    int[] commonOffsets = {
                        1024, 8192, 4096, 2048, 512,           // Basic offsets
                        4096 * 2, 512 * 4, 512 * 6,            // LBA 2 variants
                        16384, 32768,                          // Large sector/large offset
                        headerOffset + 92,                      // Follows Header (no padding)
                        headerOffset + 128                      // Follows Header (128 aligned)
                    };
                    foreach (int tryOffset in commonOffsets)
                    {
                        if (tryOffset > 0 && tryOffset < data.Length - 128 && HasValidPartitionEntry(data, tryOffset))
                        {
                            entryOffset = tryOffset;
                            usedStrategy = string.Format("Strategy 6 (Brute force probe): offset {0}", entryOffset);
                            break;
                        }
                    }
                }

                // Strategy 7: Search for the first valid partition every 128 bytes after Header
                if (entryOffset < 0)
                {
                    for (int searchOffset = headerOffset + 92; searchOffset < data.Length - 128 && searchOffset < headerOffset + 32768; searchOffset += 128)
                    {
                        if (HasValidPartitionEntry(data, searchOffset))
                        {
                            entryOffset = searchOffset;
                            usedStrategy = string.Format("Strategy 7 (Search): offset {0}", entryOffset);
                            break;
                        }
                    }
                }

                // Final check
                if (entryOffset < 0 || entryOffset >= data.Length - 128)
                {
                    _logDetail(string.Format("[GPT] Cannot determine valid partition entry offset, last attempted entryOffset={0}, dataLen={1}", entryOffset, data.Length));
                    return partitions;
                }

                _logDetail(string.Format("[GPT] {0}", usedStrategy));

                int entrySize = (int)header.SizeOfPartitionEntry;
                if (entrySize <= 0 || entrySize > 512) entrySize = 128;

                // ========== Calculate number of partition entries ==========
                int headerEntries = (int)header.NumberOfPartitionEntries;

                // Verify if the number of partitions specified in Header is reasonable
                // Some device Header NumberOfPartitionEntries might be 0 or incorrect
                if (headerEntries <= 0 || headerEntries > 1024)
                {
                    headerEntries = 128; // Default value
                    _logDetail(string.Format("[GPT] Header.NumberOfPartitionEntries abnormal ({0}), using default value 128",
                        header.NumberOfPartitionEntries));
                }

                // gpttool method: ParEntriesArea_Size = (FirstUsableLba - PartitionEntryLba) * SectorSize
                int actualAvailableEntries = 0;
                if (header.FirstUsableLba > header.PartitionEntryLba && header.PartitionEntryLba > 0)
                {
                    long parEntriesAreaSize = (long)(header.FirstUsableLba - header.PartitionEntryLba) * sectorSize;
                    actualAvailableEntries = (int)(parEntriesAreaSize / entrySize);
                    _logDetail(string.Format("[GPT] gpttool method: ({0}-{1})*{2}/{3}={4}",
                        header.FirstUsableLba, header.PartitionEntryLba, sectorSize, entrySize, actualAvailableEntries));
                }

                // Calculate max scannable entries from data length
                int maxFromData = Math.Max(0, (data.Length - entryOffset) / entrySize);

                // ========== Comprehensive calculation of max scan quantity ==========
                // 1. First use the number specified in Header
                // 2. If gpttool method calculates a larger quantity, use the larger value (some device Header information is inaccurate)
                // 3. Do not exceed data capacity
                // 4. Reasonable upper limit 1024 (Xiaomi and other devices might have many partitions)
                int maxEntries = headerEntries;

                // If the number calculated by gpttool is significantly greater than the number specified in the Header, use the gpttool value
                if (actualAvailableEntries > headerEntries && actualAvailableEntries <= 1024)
                {
                    maxEntries = actualAvailableEntries;
                    _logDetail(string.Format("[GPT] Using gpttool calculated entry count {0} (greater than Header specified {1})",
                        actualAvailableEntries, headerEntries));
                }

                // Ensure it does not exceed data capacity
                maxEntries = Math.Min(maxEntries, maxFromData);

                // Reasonable upper limit
                maxEntries = Math.Min(maxEntries, 1024);

                // Ensure at least 128 entries are scanned (standard value)
                maxEntries = Math.Max(maxEntries, Math.Min(128, maxFromData));

                _logDetail(string.Format("[GPT] Partition entries: offset={0}, size={1}, Header count={2}, gpttool={3}, data capacity={4}, final scan={5}",
                    entryOffset, entrySize, headerEntries, actualAvailableEntries, maxFromData, maxEntries));

                int parsedCount = 0;
                int totalEmptyCount = 0;

                // ========== Two-pass scan strategy ==========
                // First pass: scan all entries, find valid partitions
                var validEntries = new List<int>();
                for (int i = 0; i < maxEntries; i++)
                {
                    int offset = entryOffset + i * entrySize;
                    if (offset + 128 > data.Length) break;

                    // Check if partition type GUID is empty
                    bool isEmpty = true;
                    for (int j = 0; j < 16; j++)
                    {
                        if (data[offset + j] != 0)
                        {
                            isEmpty = false;
                            break;
                        }
                    }

                    if (!isEmpty)
                    {
                        validEntries.Add(i);
                    }
                    else
                    {
                        totalEmptyCount++;
                    }
                }

                _logDetail(string.Format("[GPT] First pass scan: found {0} non-empty entries, {1} empty entries",
                    validEntries.Count, totalEmptyCount));

                // Second pass: parse valid partition entries
                foreach (int i in validEntries)
                {
                    int offset = entryOffset + i * entrySize;

                    // Parse partition entry
                    var partition = ParsePartitionEntry(data, offset, lun, sectorSize, i + 1);
                    if (partition != null && !string.IsNullOrWhiteSpace(partition.Name))
                    {
                        partitions.Add(partition);
                        parsedCount++;

                        // Detailed log: record each parsed partition
                        if (parsedCount <= 10 || parsedCount % 20 == 0)
                        {
                            _logDetail(string.Format("[GPT] #{0}: {1} @ LBA {2}-{3} ({4})",
                                parsedCount, partition.Name, partition.StartSector,
                                partition.StartSector + partition.NumSectors - 1, partition.FormattedSize));
                        }
                    }
                }

                // If no partitions found, try fallback strategy: scan entire data without depending on Header info
                if (parsedCount == 0 && data.Length > entryOffset + 128)
                {
                    _logDetail("[GPT] Standard parsing failed, trying fallback strategy: brute force scan partition entries");

                    // Starting from entryOffset, check every 128 bytes
                    for (int offset = entryOffset; offset + 128 <= data.Length; offset += 128)
                    {
                        // Check if there is a valid partition name
                        if (HasValidPartitionEntry(data, offset))
                        {
                            var partition = ParsePartitionEntry(data, offset, lun, sectorSize, parsedCount + 1);
                            if (partition != null && !string.IsNullOrWhiteSpace(partition.Name))
                            {
                                // Check for duplicates
                                if (!partitions.Any(p => p.Name == partition.Name && p.StartSector == partition.StartSector))
                                {
                                    partitions.Add(partition);
                                    parsedCount++;
                                    _logDetail(string.Format("[GPT] Fallback strategy found: {0} @ offset {1}", partition.Name, offset));
                                }
                            }
                        }

                        // Prevent infinite loop
                        if (parsedCount > 256) break;
                    }
                }

                _logDetail(string.Format("[GPT] LUN{0} parsing completed: {1} valid partitions", lun, parsedCount));
            }
            catch (Exception ex)
            {
                _log(string.Format("[GPT] Parse partition entry exception: {0}", ex.Message));
            }

            return partitions;
        }

        /// <summary>
        /// Check for valid partition entries
        /// </summary>
        private bool HasValidPartitionEntry(byte[] data, int offset)
        {
            if (offset + 128 > data.Length) return false;

            // Check if partition type GUID is non-empty
            bool hasData = false;
            for (int i = 0; i < 16; i++)
            {
                if (data[offset + i] != 0)
                {
                    hasData = true;
                    break;
                }
            }
            if (!hasData) return false;

            // Check if partition name is readable
            try
            {
                string name = Encoding.Unicode.GetString(data, offset + 56, 72).TrimEnd('\0');
                return !string.IsNullOrWhiteSpace(name) && name.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse single partition entry
        /// </summary>
        private PartitionInfo ParsePartitionEntry(byte[] data, int offset, int lun, int sectorSize, int index)
        {
            try
            {
                // Partition type GUID (0-16)
                string typeGuid = FormatGuid(data, offset);

                // Partition unique GUID (16-32)
                string uniqueGuid = FormatGuid(data, offset + 16);

                // Start LBA (32-40)
                long startLba = BitConverter.ToInt64(data, offset + 32);

                // End LBA (40-48)
                long endLba = BitConverter.ToInt64(data, offset + 40);

                // Attributes (48-56)
                ulong attributes = BitConverter.ToUInt64(data, offset + 48);

                // Partition name UTF-16LE (56-128)
                string name = Encoding.Unicode.GetString(data, offset + 56, 72).TrimEnd('\0');

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                return new PartitionInfo
                {
                    Name = name,
                    Lun = lun,
                    StartSector = startLba,
                    NumSectors = endLba - startLba + 1,
                    SectorSize = sectorSize,
                    TypeGuid = typeGuid,
                    UniqueGuid = uniqueGuid,
                    Attributes = attributes,
                    EntryIndex = index,
                    GptEntriesStartSector = 2  // GPT entries usually start from LBA 2
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region A/B Slot Detection

        /// <summary>
        /// Detect A/B slot status
        /// </summary>
        private SlotInfo DetectSlot(List<PartitionInfo> partitions)
        {
            var info = new SlotInfo
            {
                CurrentSlot = "nonexistent",
                OtherSlot = "nonexistent",
                HasAbPartitions = false
            };

            // Find partitions with _a or _b suffix
            var abPartitions = partitions.Where(p =>
                p.Name.EndsWith("_a") || p.Name.EndsWith("_b")).ToList();

            if (abPartitions.Count == 0)
                return info;

            info.HasAbPartitions = true;
            info.CurrentSlot = "undefined";

            // Detect slot status of key partitions (boot, system, vendor, etc.)
            var keyPartitions = new[] { "boot", "system", "vendor", "abl", "xbl", "dtbo" };
            var checkPartitions = abPartitions.Where(p =>
            {
                string baseName = p.Name.EndsWith("_a") ? p.Name.Substring(0, p.Name.Length - 2) :
                                  p.Name.EndsWith("_b") ? p.Name.Substring(0, p.Name.Length - 2) : p.Name;
                return keyPartitions.Contains(baseName.ToLower());
            }).ToList();

            // If no key partitions, use all A/B partitions (exclude vendor_boot)
            if (checkPartitions.Count == 0)
            {
                checkPartitions = abPartitions.Where(p =>
                    p.Name != "vendor_boot_a" && p.Name != "vendor_boot_b").ToList();
            }

            int slotAActive = 0;
            int slotBActive = 0;

            foreach (var p in checkPartitions)
            {
                bool isActive = IsSlotActive(p.Attributes);
                bool isSuccessful = IsSlotSuccessful(p.Attributes);
                bool isUnbootable = IsSlotUnbootable(p.Attributes);

                // Debug log: print attributes of key partitions
                if (keyPartitions.Any(k => p.Name.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                {
                    _logDetail(string.Format("[GPT] Slot detection: {0} attr=0x{1:X16} active={2} success={3} unboot={4}",
                        p.Name, p.Attributes, isActive, isSuccessful, isUnbootable));
                }

                if (p.Name.EndsWith("_a") && isActive)
                    slotAActive++;
                else if (p.Name.EndsWith("_b") && isActive)
                    slotBActive++;
            }

            _logDetail(string.Format("[GPT] Slot statistics: A active={0}, B active={1} ({2} partitions checked)",
                slotAActive, slotBActive, checkPartitions.Count));

            if (slotAActive > slotBActive)
            {
                info.CurrentSlot = "a";
                info.OtherSlot = "b";
            }
            else if (slotBActive > slotAActive)
            {
                info.CurrentSlot = "b";
                info.OtherSlot = "a";
            }
            else if (slotAActive > 0 && slotBActive > 0)
            {
                info.CurrentSlot = "unknown";
                info.OtherSlot = "unknown";
            }
            else if (slotAActive == 0 && slotBActive == 0 && checkPartitions.Count > 0)
            {
                // No active flag, try to judge using successful flag
                int slotASuccessful = checkPartitions.Count(p => p.Name.EndsWith("_a") && IsSlotSuccessful(p.Attributes));
                int slotBSuccessful = checkPartitions.Count(p => p.Name.EndsWith("_b") && IsSlotSuccessful(p.Attributes));

                _logDetail(string.Format("[GPT] No active flag, using successful: A={0}, B={1}", slotASuccessful, slotBSuccessful));

                if (slotASuccessful > slotBSuccessful)
                {
                    info.CurrentSlot = "a";
                    info.OtherSlot = "b";
                }
                else if (slotBSuccessful > slotASuccessful)
                {
                    info.CurrentSlot = "b";
                    info.OtherSlot = "a";
                }
            }

            return info;
        }

        /// <summary>
        /// Check if slot is active (bit 50 in attributes)
        /// </summary>
        private bool IsSlotActive(ulong attributes)
        {
            // A/B attributes are in the high byte part of attributes
            // Bit 48: Priority bit 0
            // Bit 49: Priority bit 1
            // Bit 50: Active
            // Bit 51: Successful
            // Bit 52: Unbootable
            byte flagByte = (byte)((attributes >> (AB_FLAG_OFFSET * 8)) & 0xFF);
            return (flagByte & AB_PARTITION_ATTR_SLOT_ACTIVE) == AB_PARTITION_ATTR_SLOT_ACTIVE;
        }

        /// <summary>
        /// Check if slot successfully booted (bit 51 in attributes)
        /// </summary>
        private bool IsSlotSuccessful(ulong attributes)
        {
            byte flagByte = (byte)((attributes >> (AB_FLAG_OFFSET * 8)) & 0xFF);
            return (flagByte & 0x08) == 0x08;  // bit 3 in byte 6 = bit 51
        }

        /// <summary>
        /// Check if slot is unbootable (bit 52 in attributes)
        /// </summary>
        private bool IsSlotUnbootable(ulong attributes)
        {
            byte flagByte = (byte)((attributes >> (AB_FLAG_OFFSET * 8)) & 0xFF);
            return (flagByte & 0x10) == 0x10;  // bit 4 in byte 6 = bit 52
        }

        #endregion

        #region CRC32 Verification

        /// <summary>
        /// Verify CRC32
        /// </summary>
        private bool VerifyCrc32(byte[] data, int headerOffset, GptHeaderInfo header)
        {
            try
            {
                // Calculate Header CRC (CRC field needs to be zeroed out first)
                byte[] headerData = new byte[header.HeaderSize];
                Array.Copy(data, headerOffset, headerData, 0, (int)header.HeaderSize);

                // Zero out the CRC field
                headerData[16] = 0;
                headerData[17] = 0;
                headerData[18] = 0;
                headerData[19] = 0;

                uint calculatedCrc = CalculateCrc32(headerData);

                if (calculatedCrc != header.HeaderCrc32)
                {
                    _logDetail(string.Format("[GPT] Header CRC mismatch: Calculated={0:X8}, Stored={1:X8}",
                        calculatedCrc, header.HeaderCrc32));
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// CRC32 Calculation (using static table)
        /// </summary>
        private uint CalculateCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (byte b in data)
            {
                byte index = (byte)((crc ^ b) & 0xFF);
                crc = (crc >> 8) ^ CRC32_TABLE[index];
            }

            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// Statically initialize CRC32 table (generated only once at program startup)
        /// </summary>
        private static uint[] GenerateStaticCrc32Table()
        {
            uint[] table = new uint[256];
            const uint polynomial = 0xEDB88320;

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 8; j > 0; j--)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }

            return table;
        }

        #endregion

        #region GUID Formatting

        /// <summary>
        /// Format GUID (mixed endian format)
        /// </summary>
        private string FormatGuid(byte[] data, int offset)
        {

            // GPT GUID Format: 前3部分小端序，后2部分大端序
            // Format: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX
            var sb = new StringBuilder();

            // 第1部分 (4字节, 小端序)
            for (int i = 3; i >= 0; i--)
                sb.AppendFormat("{0:X2}", data[offset + i]);
            sb.Append("-");

            // 第2部分 (2字节, 小端序)
            for (int i = 5; i >= 4; i--)
                sb.AppendFormat("{0:X2}", data[offset + i]);
            sb.Append("-");

            // 第3部分 (2字节, 小端序)
            for (int i = 7; i >= 6; i--)
                sb.AppendFormat("{0:X2}", data[offset + i]);
            sb.Append("-");

            // 第4部分 (2字节, 大端序)
            for (int i = 8; i <= 9; i++)
                sb.AppendFormat("{0:X2}", data[offset + i]);
            sb.Append("-");

            // 第5部分 (6字节, 大端序)
            for (int i = 10; i <= 15; i++)
                sb.AppendFormat("{0:X2}", data[offset + i]);

            return sb.ToString();
        }

        #endregion

        #region XML 生成

        /// <summary>
        /// 生成合并后的 rawprogram.xml 内容 (包含所有 LUN)
        /// </summary>
        public string GenerateRawprogramXml(List<PartitionInfo> partitions, int sectorSize)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" ?>");
            sb.AppendLine("<data>");

            foreach (var p in partitions.OrderBy(x => x.Lun).ThenBy(x => x.StartSector))
            {
                long sizeKb = (p.NumSectors * (long)sectorSize) / 1024;
                long startByte = p.StartSector * (long)sectorSize;

                string filename = p.Name;
                if (!filename.EndsWith(".img", StringComparison.OrdinalIgnoreCase) &&
                    !filename.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    filename += ".img";
                }

                sb.AppendFormat("  <program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "file_sector_offset=\"0\" " +
                    "filename=\"{1}\" " +
                    "label=\"{2}\" " +
                    "num_partition_sectors=\"{3}\" " +
                    "partofsingleimage=\"false\" " +
                    "physical_partition_number=\"{4}\" " +
                    "readbackverify=\"false\" " +
                    "size_in_KB=\"{5}\" " +
                    "sparse=\"false\" " +
                    "start_byte_hex=\"0x{6:X}\" " +
                    "start_sector=\"{7}\" />\r\n",
                    sectorSize, filename, p.Name, p.NumSectors, p.Lun, sizeKb, startByte, p.StartSector);
            }

            sb.AppendLine("</data>");
            return sb.ToString();
        }

        /// <summary>
        /// 生成 rawprogram.xml 内容 (分 LUN 生成)
        /// </summary>
        public Dictionary<int, string> GenerateRawprogramXmls(List<PartitionInfo> partitions, int sectorSize)
        {
            var results = new Dictionary<int, string>();
            var luns = partitions.Select(p => p.Lun).Distinct().OrderBy(l => l);

            foreach (var lun in luns)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" ?>");
                sb.AppendLine("<data>");

                var lunPartitions = partitions.Where(p => p.Lun == lun).OrderBy(p => p.StartSector);
                foreach (var p in lunPartitions)
                {
                    long sizeKb = (p.NumSectors * (long)sectorSize) / 1024;
                    long startByte = p.StartSector * (long)sectorSize;

                    // 规范化文件名，如果有 .img 后缀则保留，没有则加上
                    string filename = p.Name;
                    if (!filename.EndsWith(".img", StringComparison.OrdinalIgnoreCase) &&
                        !filename.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    {
                        filename += ".img";
                    }

                    sb.AppendFormat("  <program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                        "file_sector_offset=\"0\" " +
                        "filename=\"{1}\" " +
                        "label=\"{2}\" " +
                        "num_partition_sectors=\"{3}\" " +
                        "partofsingleimage=\"false\" " +
                        "physical_partition_number=\"{4}\" " +
                        "readbackverify=\"false\" " +
                        "size_in_KB=\"{5}\" " +
                        "sparse=\"false\" " +
                        "start_byte_hex=\"0x{6:X}\" " +
                        "start_sector=\"{7}\" />\r\n",
                        sectorSize, filename, p.Name, p.NumSectors, p.Lun, sizeKb, startByte, p.StartSector);
                }

                sb.AppendLine("</data>");
                results[lun] = sb.ToString();
            }

            return results;
        }

        /// <summary>
        /// 生成基础 patch.xml 内容 (分 LUN 生成)
        /// </summary>
        public Dictionary<int, string> GeneratePatchXmls(List<PartitionInfo> partitions, int sectorSize)
        {
            var results = new Dictionary<int, string>();
            var luns = partitions.Select(p => p.Lun).Distinct().OrderBy(l => l);

            foreach (var lun in luns)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" ?>");
                sb.AppendLine("<data>");

                // 添加标准的 GPT 修复补丁模板 (实际值需要工具写入时动态计算，这里提供占位)
                sb.AppendLine(string.Format("  <!-- GPT Header CRC Patches for LUN {0} -->", lun));
                sb.AppendFormat("  <patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"16\" filename=\"DISK\" physical_partition_number=\"{1}\" size_in_bytes=\"4\" start_sector=\"1\" value=\"0\" />\r\n", sectorSize, lun);
                sb.AppendFormat("  <patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"88\" filename=\"DISK\" physical_partition_number=\"{1}\" size_in_bytes=\"4\" start_sector=\"1\" value=\"0\" />\r\n", sectorSize, lun);

                sb.AppendLine("</data>");
                results[lun] = sb.ToString();
            }

            return results;
        }

        /// <summary>
        /// 生成 partition.xml 内容
        /// </summary>
        public string GeneratePartitionXml(List<PartitionInfo> partitions, int sectorSize)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" ?>");
            sb.AppendLine("<partitions>");

            foreach (var p in partitions.OrderBy(x => x.Lun).ThenBy(x => x.StartSector))
            {
                long sizeKb = (p.NumSectors * sectorSize) / 1024;

                sb.AppendFormat("  <partition label=\"{0}\" " +
                    "size_in_kb=\"{1}\" " +
                    "type=\"{2}\" " +
                    "bootable=\"false\" " +
                    "readonly=\"true\" " +
                    "filename=\"{0}.img\" />\r\n",
                    p.Name, sizeKb, p.TypeGuid ?? "00000000-0000-0000-0000-000000000000");
            }

            sb.AppendLine("</partitions>");
            return sb.ToString();
        }

        #endregion
    }
}
