// ============================================================================
// LoveAlways - FDL Index Manager
// Manage relationship between FDL files and device models
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LoveAlways.Spreadtrum.Resources
{
    /// <summary>
    /// FDL Index Manager - Manages the mapping between FDL and device models
    /// </summary>
    public static class FdlIndex
    {
        #region Data Structures

        /// <summary>
        /// FDL Index Entry
        /// </summary>
        public class FdlIndexEntry
        {
            /// <summary>Chip name (e.g. SC8541E)</summary>
            public string ChipName { get; set; }

            /// <summary>Chip ID</summary>
            public uint ChipId { get; set; }

            /// <summary>Device model (e.g. A23-Pro)</summary>
            public string DeviceModel { get; set; }

            /// <summary>Brand (e.g. Samsung)</summary>
            public string Brand { get; set; }

            /// <summary>Market name (e.g. Galaxy A23)</summary>
            public string MarketName { get; set; }

            /// <summary>FDL1 filename</summary>
            public string Fdl1File { get; set; }

            /// <summary>FDL2 filename</summary>
            public string Fdl2File { get; set; }

            /// <summary>FDL1 load address</summary>
            public uint Fdl1Address { get; set; }

            /// <summary>FDL2 load address</summary>
            public uint Fdl2Address { get; set; }

            /// <summary>FDL1 file hash (for verification)</summary>
            public string Fdl1Hash { get; set; }

            /// <summary>FDL2 file hash</summary>
            public string Fdl2Hash { get; set; }

            /// <summary>Notes</summary>
            public string Notes { get; set; }

            /// <summary>Whether verified and working</summary>
            public bool Verified { get; set; }

            /// <summary>Unique key</summary>
            public string Key => $"{ChipName}/{DeviceModel}".ToLower();
        }

        /// <summary>
        /// FDL Index File
        /// </summary>
        public class FdlIndexFile
        {
            public int Version { get; set; } = 1;
            public string UpdateTime { get; set; }
            public int TotalDevices { get; set; }
            public List<FdlIndexEntry> Entries { get; set; } = new List<FdlIndexEntry>();
        }

        #endregion

        #region Status

        private static Dictionary<string, FdlIndexEntry> _index = new Dictionary<string, FdlIndexEntry>();
        private static readonly object _lock = new object();
        private static bool _initialized;

        /// <summary>
        /// Number of index entries
        /// </summary>
        public static int Count => _index.Count;

        #endregion

        #region Initialization

        /// <summary>
        /// Load index from JSON file
        /// </summary>
        public static bool LoadIndex(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return false;

            try
            {
                var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var indexFile = serializer.Deserialize<FdlIndexFile>(json);

                lock (_lock)
                {
                    _index.Clear();
                    foreach (var entry in indexFile.Entries)
                    {
                        _index[entry.Key] = entry;
                    }
                    _initialized = true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initialize index from database
        /// </summary>
        public static void InitializeFromDatabase()
        {
            lock (_lock)
            {
                if (_initialized)
                    return;

                _index.Clear();

                // Load from SprdFdlDatabase
                var chips = Database.SprdFdlDatabase.Chips;
                var devices = Database.SprdFdlDatabase.DeviceFdls;

                foreach (var device in devices)
                {
                    var chip = chips.FirstOrDefault(c =>
                        c.ChipName.Equals(device.ChipName, StringComparison.OrdinalIgnoreCase));

                    var entry = new FdlIndexEntry
                    {
                        ChipName = device.ChipName,
                        ChipId = chip?.ChipId ?? 0,
                        DeviceModel = device.DeviceName,
                        Brand = device.Brand,
                        Fdl1File = device.Fdl1FileName,
                        Fdl2File = device.Fdl2FileName,
                        Fdl1Address = chip?.Fdl1Address ?? 0x5000,
                        Fdl2Address = chip?.Fdl2Address ?? 0x9EFFFE00
                    };

                    _index[entry.Key] = entry;
                }

                _initialized = true;
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// Get FDL info for a device
        /// </summary>
        public static FdlIndexEntry GetEntry(string chipName, string deviceModel)
        {
            EnsureInitialized();
            var key = $"{chipName}/{deviceModel}".ToLower();
            lock (_lock)
            {
                return _index.TryGetValue(key, out var entry) ? entry : null;
            }
        }

        /// <summary>
        /// Get all devices for a chip
        /// </summary>
        public static FdlIndexEntry[] GetDevicesForChip(string chipName)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _index.Values
                    .Where(e => e.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.Brand)
                    .ThenBy(e => e.DeviceModel)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all devices for a brand
        /// </summary>
        public static FdlIndexEntry[] GetDevicesForBrand(string brand)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _index.Values
                    .Where(e => e.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.ChipName)
                    .ThenBy(e => e.DeviceModel)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all chip names
        /// </summary>
        public static string[] GetAllChipNames()
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _index.Values
                    .Select(e => e.ChipName)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all brands
        /// </summary>
        public static string[] GetAllBrands()
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _index.Values
                    .Select(e => e.Brand)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Search devices
        /// </summary>
        public static FdlIndexEntry[] Search(string keyword)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(keyword))
                return new FdlIndexEntry[0];

            keyword = keyword.ToLower();
            lock (_lock)
            {
                return _index.Values
                    .Where(e =>
                        e.ChipName.ToLower().Contains(keyword) ||
                        e.DeviceModel.ToLower().Contains(keyword) ||
                        e.Brand.ToLower().Contains(keyword) ||
                        (e.MarketName != null && e.MarketName.ToLower().Contains(keyword)))
                    .OrderBy(e => e.Brand)
                    .ThenBy(e => e.DeviceModel)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all entries
        /// </summary>
        public static FdlIndexEntry[] GetAllEntries()
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _index.Values.ToArray();
            }
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                // Try to load from default location
                var indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "SprdResources", "fdl_index.json");

                if (!LoadIndex(indexPath))
                {
                    InitializeFromDatabase();
                }
            }
        }

        #endregion

        #region Export

        /// <summary>
        /// Export index to JSON file
        /// </summary>
        public static void ExportIndex(string outputPath)
        {
            EnsureInitialized();

            var indexFile = new FdlIndexFile
            {
                Version = 1,
                UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalDevices = _index.Count,
                Entries = _index.Values.OrderBy(e => e.ChipName).ThenBy(e => e.DeviceModel).ToList()
            };

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            var json = serializer.Serialize(indexFile);

            // Format JSON
            json = FormatJson(json);

            File.WriteAllText(outputPath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Export as CSV format
        /// </summary>
        public static void ExportCsv(string outputPath)
        {
            EnsureInitialized();

            var sb = new StringBuilder();
            sb.AppendLine("Chip,ChipID,DeviceModel,Brand,FDL1Address,FDL2Address,FDL1File,FDL2File,Verified");

            foreach (var entry in _index.Values.OrderBy(e => e.ChipName).ThenBy(e => e.DeviceModel))
            {
                sb.AppendLine(string.Format("{0},0x{1:X},\"{2}\",{3},0x{4:X8},0x{5:X8},{6},{7},{8}",
                    entry.ChipName,
                    entry.ChipId,
                    entry.DeviceModel,
                    entry.Brand,
                    entry.Fdl1Address,
                    entry.Fdl2Address,
                    entry.Fdl1File,
                    entry.Fdl2File,
                    entry.Verified ? "Yes" : "No"));
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Format JSON
        /// </summary>
        private static string FormatJson(string json)
        {
            var indent = 0;
            var quoted = false;
            var sb = new StringBuilder();

            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];

                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            sb.Append(new string(' ', ++indent * 2));
                        }
                        break;

                    case '}':
                    case ']':
                        if (!quoted)
                        {
                            sb.AppendLine();
                            sb.Append(new string(' ', --indent * 2));
                        }
                        sb.Append(ch);
                        break;

                    case '"':
                        sb.Append(ch);
                        if (i > 0 && json[i - 1] != '\\')
                            quoted = !quoted;
                        break;

                    case ',':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                        }
                        break;

                    case ':':
                        sb.Append(ch);
                        if (!quoted)
                            sb.Append(" ");
                        break;

                    default:
                        sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get statistics
        /// </summary>
        public static FdlStatistics GetStatistics()
        {
            EnsureInitialized();

            lock (_lock)
            {
                var stats = new FdlStatistics
                {
                    TotalDevices = _index.Count,
                    TotalChips = _index.Values.Select(e => e.ChipName).Distinct().Count(),
                    TotalBrands = _index.Values.Select(e => e.Brand).Distinct().Count(),
                    VerifiedCount = _index.Values.Count(e => e.Verified)
                };

                // Statistics by chip
                stats.DevicesByChip = _index.Values
                    .GroupBy(e => e.ChipName)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Statistics by brand
                stats.DevicesByBrand = _index.Values
                    .GroupBy(e => e.Brand)
                    .ToDictionary(g => g.Key, g => g.Count());

                return stats;
            }
        }

        /// <summary>
        /// FDL Statistics
        /// </summary>
        public class FdlStatistics
        {
            public int TotalDevices { get; set; }
            public int TotalChips { get; set; }
            public int TotalBrands { get; set; }
            public int VerifiedCount { get; set; }
            public Dictionary<string, int> DevicesByChip { get; set; }
            public Dictionary<string, int> DevicesByBrand { get; set; }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== FDL Statistics ===");
                sb.AppendLine($"Total Devices: {TotalDevices}");
                sb.AppendLine($"Chip Types: {TotalChips}");
                sb.AppendLine($"Brand Count: {TotalBrands}");
                sb.AppendLine($"Verified: {VerifiedCount}");
                sb.AppendLine();
                sb.AppendLine("By Chip:");
                foreach (var kv in DevicesByChip.OrderByDescending(x => x.Value))
                {
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                sb.AppendLine();
                sb.AppendLine("By Brand:");
                foreach (var kv in DevicesByBrand.OrderByDescending(x => x.Value))
                {
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                return sb.ToString();
            }
        }

        #endregion
    }
}
