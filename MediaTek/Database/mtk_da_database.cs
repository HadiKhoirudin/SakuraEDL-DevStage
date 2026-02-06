// ============================================================================
// LoveAlways - MediaTek Download Agent Database
// MediaTek Download Agent Database
// ============================================================================
// DA Loader Database Management
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoveAlways.MediaTek.Database
{
    /// <summary>
    /// DA Record
    /// </summary>
    public class MtkDaRecord
    {
        /// <summary>HW Code</summary>
        public ushort HwCode { get; set; }

        /// <summary>DA Name</summary>
        public string Name { get; set; }

        /// <summary>DA Type (Legacy/XFlash/XML)</summary>
        public int DaType { get; set; }

        /// <summary>DA Version</summary>
        public int Version { get; set; }

        /// <summary>DA1 Load Address</summary>
        public uint Da1Address { get; set; }

        /// <summary>DA2 Load Address</summary>
        public uint Da2Address { get; set; }

        /// <summary>DA1 Signature Length</summary>
        public int Da1SigLen { get; set; }

        /// <summary>DA2 Signature Length</summary>
        public int Da2SigLen { get; set; }

        /// <summary>Embedded DA1 Data (if available)</summary>
        public byte[] EmbeddedDa1Data { get; set; }

        /// <summary>Embedded DA2 Data (if available)</summary>
        public byte[] EmbeddedDa2Data { get; set; }

        /// <summary>Whether Exploit is supported</summary>
        public bool SupportsExploit { get; set; }
    }

    /// <summary>
    /// MTK DA Database
    /// </summary>
    public static class MtkDaDatabase
    {
        private static readonly Dictionary<ushort, MtkDaRecord> _daRecords = new Dictionary<ushort, MtkDaRecord>();
        private static byte[] _allInOneDaData = null;
        private static string _daFilePath = null;

        static MtkDaDatabase()
        {
            InitializeDatabase();
        }

        /// <summary>
        /// Initialize database
        /// </summary>
        private static void InitializeDatabase()
        {
            // V6 (XML DA) Default configuration (verified chips only)
            var v6Chips = new ushort[]
            {
                0x0551, 0x0562, 0x0588, 0x0600, 0x0690, 0x0699, 0x0707,
                0x0717, 0x0725, 0x0766, 0x0788, 0x0813, 0x0816, 0x0886,
                0x0959, 0x0989, 0x0996, 0x1172, 0x1208, 0x1129
            };

            foreach (var hwCode in v6Chips)
            {
                AddDaRecord(new MtkDaRecord
                {
                    HwCode = hwCode,
                    Name = $"DA_V6_{hwCode:X4}",
                    DaType = (int)DaMode.Xml,
                    Version = 6,
                    Da1Address = 0x200000,
                    Da2Address = 0x40000000,
                    Da1SigLen = 0x30,
                    Da2SigLen = 0x30,
                    SupportsExploit = true
                });
            }

            // TODO: Add special configuration chips (needs verification)
            // Some high-end chips use special DA1 addresses (e.g., 0x1000000)

            // XFlash DA Configuration
            var xflashChips = new ushort[]
            {
                0x0279, 0x0321, 0x0326, 0x0335, 0x0601, 0x0688
            };

            foreach (var hwCode in xflashChips)
            {
                AddDaRecord(new MtkDaRecord
                {
                    HwCode = hwCode,
                    Name = $"DA_XFlash_{hwCode:X4}",
                    DaType = (int)DaMode.XFlash,
                    Version = 5,
                    Da1Address = 0x200000,
                    Da2Address = 0x40000000,
                    Da1SigLen = 0x100,
                    Da2SigLen = 0x100,
                    SupportsExploit = false
                });
            }

            // Legacy DA Configuration
            var legacyChips = new ushort[]
            {
                0x6261, 0x6572, 0x6582, 0x6589, 0x6592, 0x6752, 0x6795
            };

            foreach (var hwCode in legacyChips)
            {
                AddDaRecord(new MtkDaRecord
                {
                    HwCode = hwCode,
                    Name = $"DA_Legacy_{hwCode:X4}",
                    DaType = (int)DaMode.Legacy,
                    Version = 3,
                    Da1Address = 0x200000,
                    Da2Address = 0x40000000,
                    Da1SigLen = 0x100,
                    Da2SigLen = 0x100,
                    SupportsExploit = false
                });
            }
        }

        /// <summary>
        /// Add DA record
        /// </summary>
        private static void AddDaRecord(MtkDaRecord record)
        {
            _daRecords[record.HwCode] = record;
        }

        /// <summary>
        /// Get DA record
        /// </summary>
        public static MtkDaRecord GetDaRecord(ushort hwCode)
        {
            return _daRecords.TryGetValue(hwCode, out var record) ? record : null;
        }

        /// <summary>
        /// Get all DA records
        /// </summary>
        public static IReadOnlyList<MtkDaRecord> GetAllDaRecords()
        {
            return _daRecords.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Set AllInOne DA file path
        /// </summary>
        public static void SetDaFilePath(string filePath)
        {
            if (File.Exists(filePath))
            {
                _daFilePath = filePath;
                _allInOneDaData = null;  // Clear cache
            }
        }

        /// <summary>
        /// Load AllInOne DA data
        /// </summary>
        public static bool LoadAllInOneDa()
        {
            if (!string.IsNullOrEmpty(_daFilePath) && File.Exists(_daFilePath))
            {
                _allInOneDaData = File.ReadAllBytes(_daFilePath);
                return true;
            }

            // Try default paths
            var defaultPaths = new[]
            {
                "MtkResources/MTK_AllInOne_DA.bin",
                "Resources/MTK_AllInOne_DA.bin",
                "DA/MTK_AllInOne_DA.bin"
            };

            foreach (var path in defaultPaths)
            {
                if (File.Exists(path))
                {
                    _allInOneDaData = File.ReadAllBytes(path);
                    _daFilePath = path;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get AllInOne DA data
        /// </summary>
        public static byte[] GetAllInOneDaData()
        {
            if (_allInOneDaData == null)
            {
                LoadAllInOneDa();
            }
            return _allInOneDaData;
        }

        /// <summary>
        /// Get DA1 load address for chip
        /// </summary>
        public static uint GetDa1Address(ushort hwCode)
        {
            var record = GetDaRecord(hwCode);
            if (record != null)
                return record.Da1Address;

            // Return default value based on chip type
            var chip = MtkChipDatabase.GetChip(hwCode);
            if (chip != null)
                return chip.DaPayloadAddr;

            return 0x200000;  // Default value
        }

        /// <summary>
        /// Get DA2 load address for chip
        /// </summary>
        public static uint GetDa2Address(ushort hwCode)
        {
            var record = GetDaRecord(hwCode);
            return record?.Da2Address ?? 0x40000000;
        }

        /// <summary>
        /// Get DA mode for chip
        /// </summary>
        public static Protocol.DaMode GetDaMode(ushort hwCode)
        {
            var record = GetDaRecord(hwCode);
            if (record != null)
                return (Protocol.DaMode)record.DaType;

            var chip = MtkChipDatabase.GetChip(hwCode);
            if (chip != null)
                return (Protocol.DaMode)chip.DaMode;

            return Protocol.DaMode.Xml;  // Default to XML DA
        }

        /// <summary>
        /// Get signature length for chip
        /// </summary>
        public static int GetSignatureLength(ushort hwCode, bool isDa2 = false)
        {
            var record = GetDaRecord(hwCode);
            if (record != null)
                return isDa2 ? record.Da2SigLen : record.Da1SigLen;

            // Return default signature length based on DA type
            var daMode = GetDaMode(hwCode);
            return daMode switch
            {
                DaMode.Xml => 0x30,
                DaMode.XFlash => 0x100,
                DaMode.Legacy => 0x100,
                _ => 0x30
            };
        }

        /// <summary>
        /// Check if chip supports exploit
        /// </summary>
        public static bool SupportsExploit(ushort hwCode)
        {
            var record = GetDaRecord(hwCode);
            return record?.SupportsExploit ?? false;
        }

        /// <summary>
        /// Extract DA for specified chip from AllInOne DA file
        /// </summary>
        public static (DaEntry da1, DaEntry da2)? ExtractDaFromAllInOne(ushort hwCode, DaLoader loader)
        {
            var data = GetAllInOneDaData();
            if (data == null)
                return null;

            return loader.ParseDaData(data, hwCode);
        }

        /// <summary>
        /// Register custom DA data
        /// </summary>
        public static void RegisterCustomDa(ushort hwCode, byte[] da1Data, byte[] da2Data = null)
        {
            var record = GetDaRecord(hwCode);
            if (record == null)
            {
                record = new MtkDaRecord
                {
                    HwCode = hwCode,
                    Name = $"Custom_DA_{hwCode:X4}",
                    DaType = (int)DaMode.Xml,
                    Version = 6,
                    Da1Address = 0x200000,
                    Da2Address = 0x40000000,
                    Da1SigLen = 0x30,
                    Da2SigLen = 0x30
                };
                AddDaRecord(record);
            }

            record.EmbeddedDa1Data = da1Data;
            record.EmbeddedDa2Data = da2Data;
        }

        /// <summary>
        /// Get custom DA data
        /// </summary>
        public static (byte[] da1, byte[] da2) GetCustomDa(ushort hwCode)
        {
            var record = GetDaRecord(hwCode);
            if (record == null)
                return (null, null);

            return (record.EmbeddedDa1Data, record.EmbeddedDa2Data);
        }

        /// <summary>
        /// Get statistics
        /// </summary>
        public static (int total, int v6Count, int xflashCount, int legacyCount) GetStatistics()
        {
            int total = _daRecords.Count;
            int v6Count = _daRecords.Values.Count(r => r.DaType == (int)DaMode.Xml);
            int xflashCount = _daRecords.Values.Count(r => r.DaType == (int)DaMode.XFlash);
            int legacyCount = _daRecords.Values.Count(r => r.DaType == (int)DaMode.Legacy);

            return (total, v6Count, xflashCount, legacyCount);
        }
    }
}
