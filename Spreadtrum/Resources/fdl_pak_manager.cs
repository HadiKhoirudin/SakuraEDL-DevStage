// ============================================================================
// LoveAlways - Spreadtrum FDL Resource Pack Manager
// Spreadtrum FDL PAK Resource Manager
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LoveAlways.Spreadtrum.Resources
{
    /// <summary>
    /// FDL Resource Pack Manager - Pack/Unpack/Load FDL files
    /// PAK Format: [Header][Entry Table][Compressed Data]
    /// </summary>
    public class FdlPakManager
    {
        // PAK File Magic
        private const uint PAK_MAGIC = 0x4B415046;  // "FPAK" (FDL PAK)
        private const uint PAK_VERSION = 0x0100;    // v1.0

        // Default resource pack path
        private static readonly string DefaultPakPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "fdl.pak");

        // Cache loaded resources
        private static Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>();
        private static Dictionary<string, FdlPakEntry> _entries = new Dictionary<string, FdlPakEntry>();
        private static string _loadedPakPath = null;
        private static readonly object _lock = new object();

        #region PAK Format Definition

        /// <summary>
        /// PAK File Header (Fixed 64 bytes)
        /// </summary>
        public class FdlPakHeader
        {
            public uint Magic { get; set; }           // 4: Magic "FPAK"
            public uint Version { get; set; }         // 4: Version number
            public uint EntryCount { get; set; }      // 4: Number of file entries
            public uint EntryTableOffset { get; set; }// 4: Entry table offset
            public uint DataOffset { get; set; }      // 4: Data area offset
            public uint TotalSize { get; set; }       // 4: Total size
            public uint Checksum { get; set; }        // 4: Checksum
            public uint Flags { get; set; }           // 4: Flags (Compression, etc.)
            public byte[] Reserved { get; set; }      // 32: Reserved

            public const int SIZE = 64;

            public FdlPakHeader()
            {
                Magic = PAK_MAGIC;
                Version = PAK_VERSION;
                Reserved = new byte[32];
            }

            public byte[] ToBytes()
            {
                var data = new byte[SIZE];
                BitConverter.GetBytes(Magic).CopyTo(data, 0);
                BitConverter.GetBytes(Version).CopyTo(data, 4);
                BitConverter.GetBytes(EntryCount).CopyTo(data, 8);
                BitConverter.GetBytes(EntryTableOffset).CopyTo(data, 12);
                BitConverter.GetBytes(DataOffset).CopyTo(data, 16);
                BitConverter.GetBytes(TotalSize).CopyTo(data, 20);
                BitConverter.GetBytes(Checksum).CopyTo(data, 24);
                BitConverter.GetBytes(Flags).CopyTo(data, 28);
                Array.Copy(Reserved, 0, data, 32, 32);
                return data;
            }

            public static FdlPakHeader FromBytes(byte[] data)
            {
                if (data.Length < SIZE)
                    throw new InvalidDataException("Invalid PAK header size");

                return new FdlPakHeader
                {
                    Magic = BitConverter.ToUInt32(data, 0),
                    Version = BitConverter.ToUInt32(data, 4),
                    EntryCount = BitConverter.ToUInt32(data, 8),
                    EntryTableOffset = BitConverter.ToUInt32(data, 12),
                    DataOffset = BitConverter.ToUInt32(data, 16),
                    TotalSize = BitConverter.ToUInt32(data, 20),
                    Checksum = BitConverter.ToUInt32(data, 24),
                    Flags = BitConverter.ToUInt32(data, 28),
                    Reserved = data.Skip(32).Take(32).ToArray()
                };
            }
        }

        /// <summary>
        /// PAK File Entry (Fixed 256 bytes)
        /// </summary>
        public class FdlPakEntry
        {
            public string ChipName { get; set; }      // 32: Chip name
            public string DeviceName { get; set; }    // 64: Device name
            public string FileName { get; set; }      // 64: File name
            public uint DataOffset { get; set; }      // 4: Data offset
            public uint CompressedSize { get; set; }  // 4: Compressed size
            public uint OriginalSize { get; set; }    // 4: Original size
            public uint Checksum { get; set; }        // 4: CRC32 check
            public uint Flags { get; set; }           // 4: Flags (FDL1=1, FDL2=2, Compressed=0x100)
            public uint Fdl1Address { get; set; }     // 4: FDL1 load address
            public uint Fdl2Address { get; set; }     // 4: FDL2 load address
            public byte[] Reserved { get; set; }      // 68: Reserved

            public const int SIZE = 256;

            // Flags
            public const uint FLAG_FDL1 = 0x01;
            public const uint FLAG_FDL2 = 0x02;
            public const uint FLAG_COMPRESSED = 0x100;

            public bool IsFdl1 => (Flags & FLAG_FDL1) != 0;
            public bool IsFdl2 => (Flags & FLAG_FDL2) != 0;
            public bool IsCompressed => (Flags & FLAG_COMPRESSED) != 0;

            /// <summary>
            /// Get unique key (for indexing)
            /// </summary>
            public string Key => $"{ChipName}/{DeviceName}/{FileName}".ToLower();

            public FdlPakEntry()
            {
                Reserved = new byte[68];
            }

            public byte[] ToBytes()
            {
                var data = new byte[SIZE];
                WriteString(data, 0, ChipName, 32);
                WriteString(data, 32, DeviceName, 64);
                WriteString(data, 96, FileName, 64);
                BitConverter.GetBytes(DataOffset).CopyTo(data, 160);
                BitConverter.GetBytes(CompressedSize).CopyTo(data, 164);
                BitConverter.GetBytes(OriginalSize).CopyTo(data, 168);
                BitConverter.GetBytes(Checksum).CopyTo(data, 172);
                BitConverter.GetBytes(Flags).CopyTo(data, 176);
                BitConverter.GetBytes(Fdl1Address).CopyTo(data, 180);
                BitConverter.GetBytes(Fdl2Address).CopyTo(data, 184);
                Array.Copy(Reserved, 0, data, 188, 68);
                return data;
            }

            public static FdlPakEntry FromBytes(byte[] data)
            {
                if (data.Length < SIZE)
                    throw new InvalidDataException("Invalid PAK entry size");

                return new FdlPakEntry
                {
                    ChipName = ReadString(data, 0, 32),
                    DeviceName = ReadString(data, 32, 64),
                    FileName = ReadString(data, 96, 64),
                    DataOffset = BitConverter.ToUInt32(data, 160),
                    CompressedSize = BitConverter.ToUInt32(data, 164),
                    OriginalSize = BitConverter.ToUInt32(data, 168),
                    Checksum = BitConverter.ToUInt32(data, 172),
                    Flags = BitConverter.ToUInt32(data, 176),
                    Fdl1Address = BitConverter.ToUInt32(data, 180),
                    Fdl2Address = BitConverter.ToUInt32(data, 184),
                    Reserved = data.Skip(188).Take(68).ToArray()
                };
            }

            private static void WriteString(byte[] data, int offset, string value, int maxLen)
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? "");
                var len = Math.Min(bytes.Length, maxLen - 1);
                Array.Copy(bytes, 0, data, offset, len);
            }

            private static string ReadString(byte[] data, int offset, int maxLen)
            {
                int end = offset;
                while (end < offset + maxLen && data[end] != 0)
                    end++;
                return Encoding.UTF8.GetString(data, offset, end - offset);
            }
        }

        #endregion

        #region PAK Loading

        /// <summary>
        /// Load PAK resource pack
        /// </summary>
        public static bool LoadPak(string pakPath = null)
        {
            pakPath = pakPath ?? DefaultPakPath;

            if (!File.Exists(pakPath))
                return false;

            lock (_lock)
            {
                if (_loadedPakPath == pakPath)
                    return true;  // Already loaded

                try
                {
                    using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var br = new BinaryReader(fs))
                    {
                        // Read header
                        var headerData = br.ReadBytes(FdlPakHeader.SIZE);
                        var header = FdlPakHeader.FromBytes(headerData);

                        if (header.Magic != PAK_MAGIC)
                            throw new InvalidDataException("Invalid PAK magic");

                        // Read entry table
                        fs.Seek(header.EntryTableOffset, SeekOrigin.Begin);
                        _entries.Clear();
                        _cache.Clear();

                        for (int i = 0; i < header.EntryCount; i++)
                        {
                            var entryData = br.ReadBytes(FdlPakEntry.SIZE);
                            var entry = FdlPakEntry.FromBytes(entryData);
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
        /// Get FDL file data
        /// </summary>
        public static byte[] GetFdlData(string chipName, string deviceName, bool isFdl1)
        {
            var fileName = isFdl1 ? "fdl1" : "fdl2";
            return GetFdlData(chipName, deviceName, fileName);
        }

        /// <summary>
        /// Get FDL file data
        /// </summary>
        public static byte[] GetFdlData(string chipName, string deviceName, string fileName)
        {
            // Try multiple filename formats
            string[] tryNames = {
                fileName,
                fileName + ".bin",
                fileName + "-sign.bin",
                "fdl1-sign.bin",
                "fdl2-sign.bin",
                "fdl1.bin",
                "fdl2.bin"
            };

            foreach (var name in tryNames)
            {
                var key = $"{chipName}/{deviceName}/{name}".ToLower();
                var data = GetDataByKey(key);
                if (data != null)
                    return data;
            }

            // Try generic chip FDL
            foreach (var name in tryNames)
            {
                var key = $"{chipName}/generic/{name}".ToLower();
                var data = GetDataByKey(key);
                if (data != null)
                    return data;
            }

            return null;
        }

        /// <summary>
        /// Get data by key
        /// </summary>
        private static byte[] GetDataByKey(string key)
        {
            lock (_lock)
            {
                // Check cache
                if (_cache.TryGetValue(key, out var cached))
                    return cached;

                // Check entries
                if (!_entries.TryGetValue(key, out var entry))
                    return null;

                // Read from PAK
                if (string.IsNullOrEmpty(_loadedPakPath) || !File.Exists(_loadedPakPath))
                    return null;

                try
                {
                    using (var fs = new FileStream(_loadedPakPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(entry.DataOffset, SeekOrigin.Begin);
                        var compressedData = new byte[entry.CompressedSize];
                        fs.Read(compressedData, 0, (int)entry.CompressedSize);

                        byte[] data;
                        if (entry.IsCompressed)
                        {
                            data = Decompress(compressedData, (int)entry.OriginalSize);
                        }
                        else
                        {
                            data = compressedData;
                        }

                        // Verify checksum
                        if (CalculateCrc32(data) != entry.Checksum)
                            return null;

                        // Cache
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
        /// Get all chip names
        /// </summary>
        public static string[] GetChipNames()
        {
            lock (_lock)
            {
                return _entries.Values
                    .Select(e => e.ChipName)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get all device names for a chip
        /// </summary>
        public static string[] GetDeviceNames(string chipName)
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.DeviceName)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToArray();
            }
        }

        /// <summary>
        /// Get FDL entry info
        /// </summary>
        public static FdlPakEntry GetEntry(string chipName, string deviceName, bool isFdl1)
        {
            var fileName = isFdl1 ? "fdl1" : "fdl2";
            string[] tryNames = { fileName, fileName + ".bin", fileName + "-sign.bin" };

            lock (_lock)
            {
                foreach (var name in tryNames)
                {
                    var key = $"{chipName}/{deviceName}/{name}".ToLower();
                    if (_entries.TryGetValue(key, out var entry))
                        return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// Check if PAK is loaded
        /// </summary>
        public static bool IsLoaded => _loadedPakPath != null;

        /// <summary>
        /// Get number of loaded entries
        /// </summary>
        public static int EntryCount => _entries.Count;

        #endregion

        #region PAK Building

        /// <summary>
        /// Build PAK resource pack from directory
        /// </summary>
        /// <param name="sourceDir">Source directory (contains FDL files)</param>
        /// <param name="outputPath">Output PAK path</param>
        /// <param name="compress">Whether to compress</param>
        public static void BuildPak(string sourceDir, string outputPath, bool compress = true)
        {
            var entries = new List<FdlPakEntry>();
            var dataBlocks = new List<byte[]>();
            uint dataOffset = 0;

            // Traverse directory to collect FDL files
            foreach (var file in Directory.GetFiles(sourceDir, "*.bin", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(sourceDir.Length).TrimStart('\\', '/');
                var parts = relativePath.Split('\\', '/');

                if (parts.Length < 2)
                    continue;

                var fileName = Path.GetFileName(file);
                var deviceName = parts.Length >= 3 ? parts[parts.Length - 2] : "generic";
                var chipName = parts[0];

                // Read file
                var originalData = File.ReadAllBytes(file);
                var checksum = CalculateCrc32(originalData);

                // Compress
                byte[] compressedData;
                if (compress)
                {
                    compressedData = Compress(originalData);
                }
                else
                {
                    compressedData = originalData;
                }

                // Determine FDL type
                uint flags = compress ? FdlPakEntry.FLAG_COMPRESSED : 0;
                if (fileName.ToLower().Contains("fdl1"))
                    flags |= FdlPakEntry.FLAG_FDL1;
                else if (fileName.ToLower().Contains("fdl2"))
                    flags |= FdlPakEntry.FLAG_FDL2;

                var entry = new FdlPakEntry
                {
                    ChipName = chipName,
                    DeviceName = deviceName,
                    FileName = fileName,
                    DataOffset = dataOffset,
                    CompressedSize = (uint)compressedData.Length,
                    OriginalSize = (uint)originalData.Length,
                    Checksum = checksum,
                    Flags = flags
                };

                entries.Add(entry);
                dataBlocks.Add(compressedData);
                dataOffset += (uint)compressedData.Length;
            }

            // Calculate offsets
            uint entryTableOffset = (uint)FdlPakHeader.SIZE;
            uint dataStartOffset = entryTableOffset + (uint)(entries.Count * FdlPakEntry.SIZE);

            // Update entry data offsets
            uint currentOffset = dataStartOffset;
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].DataOffset = currentOffset;
                currentOffset += entries[i].CompressedSize;
            }

            // Write to file
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Write header
                var header = new FdlPakHeader
                {
                    EntryCount = (uint)entries.Count,
                    EntryTableOffset = entryTableOffset,
                    DataOffset = dataStartOffset,
                    TotalSize = currentOffset,
                    Flags = compress ? 1u : 0u
                };
                bw.Write(header.ToBytes());

                // Write entry table
                foreach (var entry in entries)
                {
                    bw.Write(entry.ToBytes());
                }

                // Write data
                foreach (var data in dataBlocks)
                {
                    bw.Write(data);
                }
            }
        }

        /// <summary>
        /// Extract PAK to directory
        /// </summary>
        public static void ExtractPak(string pakPath, string outputDir)
        {
            using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // Read header
                var headerData = br.ReadBytes(FdlPakHeader.SIZE);
                var header = FdlPakHeader.FromBytes(headerData);

                if (header.Magic != PAK_MAGIC)
                    throw new InvalidDataException("Invalid PAK magic");

                // Read entries
                fs.Seek(header.EntryTableOffset, SeekOrigin.Begin);
                var entries = new List<FdlPakEntry>();

                for (int i = 0; i < header.EntryCount; i++)
                {
                    var entryData = br.ReadBytes(FdlPakEntry.SIZE);
                    entries.Add(FdlPakEntry.FromBytes(entryData));
                }

                // Unpack each file
                foreach (var entry in entries)
                {
                    var outputPath = Path.Combine(outputDir, entry.ChipName, entry.DeviceName, entry.FileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    fs.Seek(entry.DataOffset, SeekOrigin.Begin);
                    var compressedData = br.ReadBytes((int)entry.CompressedSize);

                    byte[] data;
                    if (entry.IsCompressed)
                    {
                        data = Decompress(compressedData, (int)entry.OriginalSize);
                    }
                    else
                    {
                        data = compressedData;
                    }

                    File.WriteAllBytes(outputPath, data);
                }
            }
        }

        #endregion

        #region Compression/Decompression

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

        private static byte[] Decompress(byte[] data, int originalSize)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        #endregion

        #region CRC32

        private static uint[] _crc32Table;

        private static uint CalculateCrc32(byte[] data)
        {
            if (_crc32Table == null)
                InitCrc32Table();

            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc = _crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFF;
        }

        private static void InitCrc32Table()
        {
            _crc32Table = new uint[256];
            const uint polynomial = 0xEDB88320;

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                _crc32Table[i] = crc;
            }
        }

        #endregion

        #region Temporary File Extraction

        private static string _tempDir;

        /// <summary>
        /// Extract FDL to temporary file and return path
        /// </summary>
        public static string ExtractToTempFile(string chipName, string deviceName, bool isFdl1)
        {
            var data = GetFdlData(chipName, deviceName, isFdl1);
            if (data == null)
                return null;

            if (_tempDir == null)
            {
                _tempDir = Path.Combine(Path.GetTempPath(), "SprdFdl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
        public static void CleanupTempFiles()
        {
            if (_tempDir != null && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch { }
                _tempDir = null;
            }
        }

        #endregion
    }
}
