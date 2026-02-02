// ============================================================================
// LoveAlways - Spreadtrum Resource Package Reader
// Load resources like Exploit/FDL from external resource package (sprd_resources.pak)
// Supports SPAK v1 format
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LoveAlways.Spreadtrum.Resources
{
    /// <summary>
    /// Spreadtrum Resource Package Reader (SPAK Format)
    /// 
    /// SPAK v1 format:
    /// Header: Magic(4 "SPAK") + Version(4) + Count(4)
    /// Entry: Name(64) + Offset(8) + CompSize(4) + OrigSize(4) + Type(4) + Reserved(4)
    /// Data: GZip compressed resource data
    /// </summary>
    public class SprdResourcePak : IDisposable
    {
        private const string MAGIC = "SPAK";
        private const int CURRENT_VERSION = 1;
        private const int ENTRY_NAME_SIZE = 64;
        private const int ENTRY_SIZE = 88; // 64 + 8 + 4 + 4 + 4 + 4

        private readonly string _pakPath;
        private readonly Dictionary<string, PakEntry> _index;
        private FileStream _fileStream;
        private bool _disposed;

        /// <summary>
        /// Resource package version
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// Resource entry count
        /// </summary>
        public int Count => _index.Count;

        /// <summary>
        /// Resource type
        /// </summary>
        public enum ResourceType : uint
        {
            Unknown = 0,
            Exploit = 1,        // Exploit payload
            Fdl1 = 2,           // FDL1 file
            Fdl2 = 3,           // FDL2 file
            ChipData = 4,       // Chip data
            Config = 5,         // Configuration file
            Script = 6          // Script file
        }

        private struct PakEntry
        {
            public string Name;
            public long Offset;
            public int CompressedSize;
            public int OriginalSize;
            public ResourceType Type;
        }

        /// <summary>
        /// Create resource package reader
        /// </summary>
        public SprdResourcePak(string pakPath)
        {
            _pakPath = pakPath;
            _index = new Dictionary<string, PakEntry>(StringComparer.OrdinalIgnoreCase);
            LoadIndex();
        }

        /// <summary>
        /// Load resource package index
        /// </summary>
        private void LoadIndex()
        {
            _fileStream = new FileStream(_pakPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using (var br = new BinaryReader(_fileStream, Encoding.UTF8, true))
            {
                // Read magic number
                byte[] magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != MAGIC)
                    throw new InvalidDataException("Invalid SPAK file");

                // Version
                Version = (int)br.ReadUInt32();
                if (Version > CURRENT_VERSION)
                    throw new InvalidDataException($"Unsupported SPAK version: {Version}");

                // Entry count
                uint count = br.ReadUInt32();

                // Read index
                for (int i = 0; i < count; i++)
                {
                    byte[] nameBytes = br.ReadBytes(ENTRY_NAME_SIZE);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    var entry = new PakEntry
                    {
                        Name = name,
                        Offset = br.ReadInt64(),
                        CompressedSize = br.ReadInt32(),
                        OriginalSize = br.ReadInt32(),
                        Type = (ResourceType)br.ReadUInt32()
                    };

                    br.ReadInt32(); // Reserved

                    _index[name] = entry;
                }
            }
        }

        /// <summary>
        /// Get resource data
        /// </summary>
        public byte[] GetResource(string name)
        {
            if (!_index.TryGetValue(name, out var entry))
                return null;

            return ReadAndDecompress(entry.Offset, entry.CompressedSize, entry.OriginalSize);
        }

        /// <summary>
        /// Get all resource names of specified type
        /// </summary>
        public string[] GetResourcesByType(ResourceType type)
        {
            var names = new List<string>();
            foreach (var kvp in _index)
            {
                if (kvp.Value.Type == type)
                    names.Add(kvp.Key);
            }
            return names.ToArray();
        }

        /// <summary>
        /// Get all Exploit resource names
        /// </summary>
        public string[] GetExploitNames()
        {
            return GetResourcesByType(ResourceType.Exploit);
        }

        /// <summary>
        /// Check if resource exists
        /// </summary>
        public bool HasResource(string name)
        {
            return _index.ContainsKey(name);
        }

        /// <summary>
        /// Get resource type
        /// </summary>
        public ResourceType GetResourceType(string name)
        {
            if (_index.TryGetValue(name, out var entry))
                return entry.Type;
            return ResourceType.Unknown;
        }

        /// <summary>
        /// Get all resource names
        /// </summary>
        public string[] GetAllResourceNames()
        {
            var names = new string[_index.Count];
            _index.Keys.CopyTo(names, 0);
            return names;
        }

        /// <summary>
        /// Read and decompress data
        /// </summary>
        private byte[] ReadAndDecompress(long offset, int compSize, int origSize)
        {
            lock (_fileStream)
            {
                _fileStream.Seek(offset, SeekOrigin.Begin);
                byte[] compressed = new byte[compSize];
                _fileStream.Read(compressed, 0, compSize);

                // If compressed size is same as original, it's not compressed
                if (compSize == origSize)
                    return compressed;

                // GZip decompression
                using (var input = new MemoryStream(compressed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileStream?.Dispose();
                }
                _disposed = true;
            }
        }

        ~SprdResourcePak()
        {
            Dispose(false);
        }

        #region Static Packaging Methods

        /// <summary>
        /// Create resource package
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <param name="resources">Resource list (Name, Data, Type)</param>
        public static void CreatePak(string outputPath, List<(string Name, byte[] Data, ResourceType Type)> resources)
        {
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Write header
                bw.Write(Encoding.ASCII.GetBytes(MAGIC));
                bw.Write((uint)CURRENT_VERSION);
                bw.Write((uint)resources.Count);

                // Calculate data start offset
                long headerSize = 12; // Magic(4) + Version(4) + Count(4)
                long indexSize = resources.Count * ENTRY_SIZE;
                long dataOffset = headerSize + indexSize;

                // Prepare compressed data and index
                var compressedData = new List<byte[]>();
                var entries = new List<PakEntry>();

                foreach (var res in resources)
                {
                    byte[] compressed = Compress(res.Data);
                    compressedData.Add(compressed);

                    entries.Add(new PakEntry
                    {
                        Name = res.Name,
                        Offset = dataOffset,
                        CompressedSize = compressed.Length,
                        OriginalSize = res.Data.Length,
                        Type = res.Type
                    });

                    dataOffset += compressed.Length;
                }

                // Write index
                foreach (var entry in entries)
                {
                    byte[] nameBytes = new byte[ENTRY_NAME_SIZE];
                    byte[] nameUtf8 = Encoding.UTF8.GetBytes(entry.Name);
                    Array.Copy(nameUtf8, nameBytes, Math.Min(nameUtf8.Length, ENTRY_NAME_SIZE - 1));
                    bw.Write(nameBytes);

                    bw.Write(entry.Offset);
                    bw.Write(entry.CompressedSize);
                    bw.Write(entry.OriginalSize);
                    bw.Write((uint)entry.Type);
                    bw.Write((uint)0); // Reserved
                }

                // Write data
                foreach (var data in compressedData)
                {
                    bw.Write(data);
                }
            }
        }

        /// <summary>
        /// GZip compression
        /// </summary>
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

        /// <summary>
        /// Create resource package from directory
        /// </summary>
        /// <param name="sourceDir">Source directory</param>
        /// <param name="outputPath">Output file path</param>
        public static void CreatePakFromDirectory(string sourceDir, string outputPath)
        {
            var resources = new List<(string Name, byte[] Data, ResourceType Type)>();

            foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                byte[] data = File.ReadAllBytes(file);
                ResourceType type = InferResourceType(name);

                resources.Add((name, data, type));
            }

            CreatePak(outputPath, resources);
        }

        /// <summary>
        /// Infer resource type based on file name
        /// </summary>
        private static ResourceType InferResourceType(string fileName)
        {
            string lower = fileName.ToLower();

            if (lower.StartsWith("exploit_") || lower.Contains("exploit"))
                return ResourceType.Exploit;
            if (lower.StartsWith("fdl1") || lower.Contains("fdl1"))
                return ResourceType.Fdl1;
            if (lower.StartsWith("fdl2") || lower.Contains("fdl2"))
                return ResourceType.Fdl2;
            if (lower.EndsWith(".json") || lower.EndsWith(".xml") || lower.EndsWith(".ini"))
                return ResourceType.Config;
            if (lower.EndsWith(".bat") || lower.EndsWith(".sh") || lower.EndsWith(".ps1"))
                return ResourceType.Script;

            return ResourceType.Unknown;
        }

        #endregion
    }

    /// <summary>
    /// Resource package info
    /// </summary>
    public class SprdPakResourceInfo
    {
        public string Name { get; set; }
        public int OriginalSize { get; set; }
        public int CompressedSize { get; set; }
        public SprdResourcePak.ResourceType Type { get; set; }
        public double CompressionRatio => CompressedSize > 0 ? (double)OriginalSize / CompressedSize : 1.0;
    }
}
