// ============================================================================
// LoveAlways - Spreadtrum Unified Resource Pack Manager
// Pack/Load resources like Exploit, FDL, Config, etc.
// Supports SPAK v2 format
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LoveAlways.Spreadtrum.Resources
{
    /// <summary>
    /// Spreadtrum Unified Resource Pack Manager
    /// Format: SPAK v2
    /// 
    /// +------------------+
    /// | Header (32 B)    | Magic "SPAK", Version, Entry Count, Flags, Checksum
    /// +------------------+
    /// | Entry Table      | 128 bytes per entry
    /// | (N × 128 B)      |
    /// +------------------+
    /// | Compressed Data  | GZip compressed resource data
    /// +------------------+
    /// </summary>
    public static class SprdPakManager
    {
        #region Constants

        private const uint PAK_MAGIC = 0x4B415053;  // "SPAK"
        private const uint PAK_VERSION = 0x0200;     // v2.0
        private const int HEADER_SIZE = 32;
        private const int ENTRY_SIZE = 128;

        #endregion

        #region Resource Types

        /// <summary>
        /// Resource Type
        /// </summary>
        public enum ResourceType : uint
        {
            Unknown = 0,
            Exploit = 1,        // Exploit payload
            Fdl1 = 2,           // FDL1 file
            Fdl2 = 3,           // FDL2 file
            ChipData = 4,       // Chip data
            Config = 5,         // Config file
            Script = 6,         // Script file
            Firmware = 7        // Firmware file
        }

        #endregion

        #region Data Structures

        /// <summary>
        /// PAK File Header
        /// </summary>
        public class PakHeader
        {
            public uint Magic { get; set; }
            public uint Version { get; set; }
            public uint EntryCount { get; set; }
            public uint Flags { get; set; }
            public uint Checksum { get; set; }
            public uint DataOffset { get; set; }
            public byte[] Reserved { get; set; } = new byte[8];

            public byte[] ToBytes()
            {
                var data = new byte[HEADER_SIZE];
                BitConverter.GetBytes(Magic).CopyTo(data, 0);
                BitConverter.GetBytes(Version).CopyTo(data, 4);
                BitConverter.GetBytes(EntryCount).CopyTo(data, 8);
                BitConverter.GetBytes(Flags).CopyTo(data, 12);
                BitConverter.GetBytes(Checksum).CopyTo(data, 16);
                BitConverter.GetBytes(DataOffset).CopyTo(data, 20);
                Array.Copy(Reserved, 0, data, 24, 8);
                return data;
            }

            public static PakHeader FromBytes(byte[] data)
            {
                return new PakHeader
                {
                    Magic = BitConverter.ToUInt32(data, 0),
                    Version = BitConverter.ToUInt32(data, 4),
                    EntryCount = BitConverter.ToUInt32(data, 8),
                    Flags = BitConverter.ToUInt32(data, 12),
                    Checksum = BitConverter.ToUInt32(data, 16),
                    DataOffset = BitConverter.ToUInt32(data, 20),
                    Reserved = data.Skip(24).Take(8).ToArray()
                };
            }
        }

        /// <summary>
        /// PAK Entry
        /// </summary>
        public class PakEntry
        {
            public string Name { get; set; }          // 32: Resource name
            public string Category { get; set; }      // 16: Category (Chip name/Device name)
            public string SubCategory { get; set; }   // 16: Subcategory
            public uint DataOffset { get; set; }      // 4: Data offset
            public uint CompressedSize { get; set; }  // 4: Compressed size
            public uint OriginalSize { get; set; }    // 4: Original size
            public uint Checksum { get; set; }        // 4: CRC32
            public ResourceType Type { get; set; }    // 4: Resource type
            public uint Flags { get; set; }           // 4: Flags
            public uint Address { get; set; }         // 4: Load address (FDL specific)
            public byte[] Reserved { get; set; } = new byte[36]; // 36: Reserved

            public bool IsCompressed => (Flags & 0x01) != 0;
            public string Key => $"{Category}/{SubCategory}/{Name}".ToLower();

            public byte[] ToBytes()
            {
                var data = new byte[ENTRY_SIZE];
                WriteString(data, 0, Name, 32);
                WriteString(data, 32, Category, 16);
                WriteString(data, 48, SubCategory, 16);
                BitConverter.GetBytes(DataOffset).CopyTo(data, 64);
                BitConverter.GetBytes(CompressedSize).CopyTo(data, 68);
                BitConverter.GetBytes(OriginalSize).CopyTo(data, 72);
                BitConverter.GetBytes(Checksum).CopyTo(data, 76);
                BitConverter.GetBytes((uint)Type).CopyTo(data, 80);
                BitConverter.GetBytes(Flags).CopyTo(data, 84);
                BitConverter.GetBytes(Address).CopyTo(data, 88);
                Array.Copy(Reserved, 0, data, 92, 36);
                return data;
            }

            public static PakEntry FromBytes(byte[] data)
            {
                return new PakEntry
                {
                    Name = ReadString(data, 0, 32),
                    Category = ReadString(data, 32, 16),
                    SubCategory = ReadString(data, 48, 16),
                    DataOffset = BitConverter.ToUInt32(data, 64),
                    CompressedSize = BitConverter.ToUInt32(data, 68),
                    OriginalSize = BitConverter.ToUInt32(data, 72),
                    Checksum = BitConverter.ToUInt32(data, 76),
                    Type = (ResourceType)BitConverter.ToUInt32(data, 80),
                    Flags = BitConverter.ToUInt32(data, 84),
                    Address = BitConverter.ToUInt32(data, 88),
                    Reserved = data.Skip(92).Take(36).ToArray()
                };
            }

            private static void WriteString(byte[] data, int offset, string value, int maxLen)
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? "");
                Array.Copy(bytes, 0, data, offset, Math.Min(bytes.Length, maxLen - 1));
            }

            private static string ReadString(byte[] data, int offset, int maxLen)
            {
                int end = offset;
                while (end < offset + maxLen && data[end] != 0) end++;
                return Encoding.UTF8.GetString(data, offset, end - offset);
            }
        }

        #endregion

        #region Status Management

        private static readonly object _lock = new object();
        private static Dictionary<string, PakEntry> _entries = new Dictionary<string, PakEntry>();
        private static Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>();
        private static string _loadedPakPath;
        private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

        /// <summary>
        /// Whether resource pack is loaded
        /// </summary>
        public static bool IsLoaded => _loadedPakPath != null;

        /// <summary>
        /// Number of loaded entries
        /// </summary>
        public static int EntryCount => _entries.Count;

        /// <summary>
        /// Default resource pack path
        /// </summary>
        public static string DefaultPakPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "sprd.pak");

        #endregion

        #region Load Resource Pack

        /// <summary>
        /// Load resource pack
        /// </summary>
        public static bool LoadPak(string pakPath = null)
        {
            pakPath = pakPath ?? DefaultPakPath;
            if (!File.Exists(pakPath))
                return false;

            lock (_lock)
            {
                if (_loadedPakPath == pakPath)
                    return true;

                try
                {
                    using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var br = new BinaryReader(fs))
                    {
                        // Read header
                        var headerData = br.ReadBytes(HEADER_SIZE);
                        var header = PakHeader.FromBytes(headerData);

                        if (header.Magic != PAK_MAGIC)
                            throw new InvalidDataException("Invalid SPAK magic");

                        // Read entries
                        _entries.Clear();
                        _cache.Clear();

                        for (int i = 0; i < header.EntryCount; i++)
                        {
                            var entryData = br.ReadBytes(ENTRY_SIZE);
                            var entry = PakEntry.FromBytes(entryData);
                            _entries[entry.Key] = entry;
                        }

                        _loadedPakPath = pakPath;
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Unload resource pack
        /// </summary>
        public static void UnloadPak()
        {
            lock (_lock)
            {
                _entries.Clear();
                _cache.Clear();
                _loadedPakPath = null;
            }
        }

        #endregion

        #region Get Resources

        /// <summary>
        /// Get resource data
        /// </summary>
        public static byte[] GetResource(string category, string subCategory, string name)
        {
            var key = $"{category}/{subCategory}/{name}".ToLower();
            return GetResourceByKey(key);
        }

        /// <summary>
        /// Get FDL data
        /// </summary>
        public static byte[] GetFdlData(string chipName, string deviceName, bool isFdl1)
        {
            // Try multiple naming formats
            string[] names = isFdl1
                ? new[] { "fdl1-sign.bin", "fdl1.bin", "fdl1" }
                : new[] { "fdl2-sign.bin", "fdl2.bin", "fdl2" };

            foreach (var name in names)
            {
                var data = GetResource(chipName, deviceName, name);
                if (data != null)
                    return data;
            }

            // Try generic device
            foreach (var name in names)
            {
                var data = GetResource(chipName, "generic", name);
                if (data != null)
                    return data;
            }

            // Load from embedded resources
            return null;
        }

        /// <summary>
        /// Get Exploit data
        /// </summary>
        public static byte[] GetExploitData(string exploitId)
        {
            // From resource pack
            var data = GetResource("exploit", "payload", $"exploit_{exploitId}.bin");
            if (data != null)
                return data;

            // From embedded resources
            return LoadEmbeddedResource($"exploit_{exploitId}.bin");
        }

        /// <summary>
        /// Get data by key
        /// </summary>
        private static byte[] GetResourceByKey(string key)
        {
            lock (_lock)
            {
                // Check cache
                if (_cache.TryGetValue(key, out var cached))
                    return cached;

                // Check entries
                if (!_entries.TryGetValue(key, out var entry))
                    return null;

                // Read data
                if (string.IsNullOrEmpty(_loadedPakPath))
                    return null;

                try
                {
                    using (var fs = new FileStream(_loadedPakPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(entry.DataOffset, SeekOrigin.Begin);
                        var compressedData = new byte[entry.CompressedSize];
                        fs.Read(compressedData, 0, (int)entry.CompressedSize);

                        byte[] data = entry.IsCompressed
                            ? Decompress(compressedData)
                            : compressedData;

                        // Verify checksum
                        if (CalculateCrc32(data) != entry.Checksum)
                            return null;

                        _cache[key] = data;
                        return data;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Load from embedded resources
        /// </summary>
        private static byte[] LoadEmbeddedResource(string resourceName)
        {
            string prefix = "LoveAlways.Spreadtrum.Resources.";
            try
            {
                using (var stream = _assembly.GetManifestResourceStream(prefix + resourceName))
                {
                    if (stream == null)
                    {
                        // Try other names
                        foreach (var name in _assembly.GetManifestResourceNames())
                        {
                            if (name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                            {
                                using (var s = _assembly.GetManifestResourceStream(name))
                                {
                                    if (s != null)
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            s.CopyTo(ms);
                                            return ms.ToArray();
                                        }
                                    }
                                }
                            }
                        }
                        return null;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Get all chip names
        /// </summary>
        public static string[] GetChipNames()
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.Type == ResourceType.Fdl1 || e.Type == ResourceType.Fdl2)
                    .Select(e => e.Category)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all devices for a chip
        /// </summary>
        public static string[] GetDeviceNames(string chipName)
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.Category.Equals(chipName, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.SubCategory)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all exploit names
        /// </summary>
        public static string[] GetExploitNames()
        {
            lock (_lock)
            {
                var names = _entries.Values
                    .Where(e => e.Type == ResourceType.Exploit)
                    .Select(e => e.Name)
                    .ToList();

                // Add embedded exploits                names.AddRange(new[] { "exploit_4ee8.bin", "exploit_65015f08.bin", "exploit_65015f48.bin" });
                return names.Distinct().ToArray();
            }
        }

        /// <summary>
        /// Check if a resource exists
        /// </summary>
        public static bool HasResource(string category, string subCategory, string name)
        {
            var key = $"{category}/{subCategory}/{name}".ToLower();
            lock (_lock)
            {
                return _entries.ContainsKey(key);
            }
        }

        /// <summary>
        /// Get FDL entry info
        /// </summary>
        public static PakEntry GetFdlEntry(string chipName, string deviceName, bool isFdl1)
        {
            string[] names = isFdl1
                ? new[] { "fdl1-sign.bin", "fdl1.bin" }
                : new[] { "fdl2-sign.bin", "fdl2.bin" };

            lock (_lock)
            {
                foreach (var name in names)
                {
                    var key = $"{chipName}/{deviceName}/{name}".ToLower();
                    if (_entries.TryGetValue(key, out var entry))
                        return entry;
                }
            }
            return null;
        }

        #endregion

        #region Build Resource Pack

        /// <summary>
        /// Build resource pack from FDL directory
        /// </summary>
        public static void BuildPak(string fdlSourceDir, string outputPath, bool compress = true)
        {
            var entries = new List<PakEntry>();
            var dataBlocks = new List<byte[]>();
            uint dataOffset = 0;

            // Iterate FDL directory
            if (Directory.Exists(fdlSourceDir))
            {
                foreach (var file in Directory.GetFiles(fdlSourceDir, "*.bin", SearchOption.AllDirectories))
                {
                    var relativePath = file.Substring(fdlSourceDir.Length).TrimStart('\\', '/');
                    var parts = relativePath.Split('\\', '/');
                    if (parts.Length < 2) continue;

                    var fileName = Path.GetFileName(file);
                    var chipName = parts[0];
                    var deviceName = parts.Length >= 3 ? parts[parts.Length - 2] : "generic";

                    // Read file
                    var originalData = File.ReadAllBytes(file);
                    var checksum = CalculateCrc32(originalData);

                    // Compress
                    byte[] compressedData = compress ? Compress(originalData) : originalData;

                    // Determine type
                    var type = fileName.ToLower().Contains("fdl1") ? ResourceType.Fdl1
                             : fileName.ToLower().Contains("fdl2") ? ResourceType.Fdl2
                             : fileName.ToLower().Contains("exploit") ? ResourceType.Exploit
                             : ResourceType.Unknown;

                    entries.Add(new PakEntry
                    {
                        Name = fileName,
                        Category = chipName,
                        SubCategory = deviceName,
                        DataOffset = dataOffset,
                        CompressedSize = (uint)compressedData.Length,
                        OriginalSize = (uint)originalData.Length,
                        Checksum = checksum,
                        Type = type,
                        Flags = compress ? 0x01u : 0u
                    });

                    dataBlocks.Add(compressedData);
                    dataOffset += (uint)compressedData.Length;
                }
            }

            // Calculate offsets
            uint entryTableStart = HEADER_SIZE;
            uint dataStart = entryTableStart + (uint)(entries.Count * ENTRY_SIZE);

            // Update offsets
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].DataOffset += dataStart;
            }

            // Write to file
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Header
                var header = new PakHeader
                {
                    Magic = PAK_MAGIC,
                    Version = PAK_VERSION,
                    EntryCount = (uint)entries.Count,
                    Flags = compress ? 1u : 0u,
                    DataOffset = dataStart
                };
                bw.Write(header.ToBytes());

                // Entry table
                foreach (var entry in entries)
                {
                    bw.Write(entry.ToBytes());
                }

                // Data
                foreach (var data in dataBlocks)
                {
                    bw.Write(data);
                }
            }
        }

        /// <summary>
        /// Extract pack to directory
        /// </summary>
        public static void ExtractPak(string pakPath, string outputDir)
        {
            using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                var headerData = br.ReadBytes(HEADER_SIZE);
                var header = PakHeader.FromBytes(headerData);

                if (header.Magic != PAK_MAGIC)
                    throw new InvalidDataException("Invalid SPAK magic");

                var entries = new List<PakEntry>();
                for (int i = 0; i < header.EntryCount; i++)
                {
                    var entryData = br.ReadBytes(ENTRY_SIZE);
                    entries.Add(PakEntry.FromBytes(entryData));
                }

                foreach (var entry in entries)
                {
                    var outputPath = Path.Combine(outputDir, entry.Category, entry.SubCategory, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    fs.Seek(entry.DataOffset, SeekOrigin.Begin);
                    var compressedData = br.ReadBytes((int)entry.CompressedSize);

                    var data = entry.IsCompressed ? Decompress(compressedData) : compressedData;
                    File.WriteAllBytes(outputPath, data);
                }
            }
        }

        #endregion

        #region Temporary Files

        private static string _tempDir;

        /// <summary>
        /// Extract FDL to temporary file
        /// </summary>
        public static string ExtractFdlToTemp(string chipName, string deviceName, bool isFdl1)
        {
            var data = GetFdlData(chipName, deviceName, isFdl1);
            if (data == null)
                return null;

            if (_tempDir == null)
            {
                _tempDir = Path.Combine(Path.GetTempPath(), "SprdPak_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(_tempDir);
            }

            var fileName = isFdl1 ? "fdl1.bin" : "fdl2.bin";
            var filePath = Path.Combine(_tempDir, chipName, deviceName, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            File.WriteAllBytes(filePath, data);
            return filePath;
        }

        /// <summary>
        /// Cleanup temporary files
        /// </summary>
        public static void CleanupTemp()
        {
            if (_tempDir != null && Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
                _tempDir = null;
            }
        }

        #endregion

        #region Compression/Decompression/CRC

        private static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        private static uint[] _crc32Table;

        private static uint CalculateCrc32(byte[] data)
        {
            if (_crc32Table == null)
                InitCrc32Table();

            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = _crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        private static void InitCrc32Table()
        {
            _crc32Table = new uint[256];
            const uint poly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) == 1 ? (crc >> 1) ^ poly : crc >> 1;
                _crc32Table[i] = crc;
            }
        }

        #endregion
    }
}
