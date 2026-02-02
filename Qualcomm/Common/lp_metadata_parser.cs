using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// Android Logical Partition (LP) Metadata Parser
    /// Used to parse super partition layout information (with cache)
    /// </summary>
    public class LpMetadataParser
    {
        private const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467; // gDla (OPUS Header)
        private const uint LP_METADATA_HEADER_MAGIC = 0x414c5030;   // ALP0

        // LP Metadata 使用的固定扇区大小
        public const int LP_SECTOR_SIZE = 512;

        // Static parse cache (results cached by data hash)
        private static readonly ConcurrentDictionary<string, List<LpPartitionInfo>> _parseCache
            = new ConcurrentDictionary<string, List<LpPartitionInfo>>();

        // 缓存大小限制
        private const int MAX_CACHE_ENTRIES = 10;

        public class LpPartitionInfo
        {
            public string Name { get; set; }
            public List<LpExtentInfo> Extents { get; set; } = new List<LpExtentInfo>();

            /// <summary>
            /// Total size (LP sectors, 512 bytes per sector)
            /// </summary>
            public long TotalSizeLpSectors => Extents.Sum(e => (long)e.NumSectors);

            /// <summary>
            /// Total size (bytes)
            /// </summary>
            public long TotalSizeBytes => TotalSizeLpSectors * LP_SECTOR_SIZE;

            /// <summary>
            /// Physical offset of the first extent (LP sectors, 512 bytes per sector)
            /// Only valid for LINEAR type
            /// </summary>
            public long FirstLpSectorOffset => GetFirstLinearOffset();

            private long GetFirstLinearOffset()
            {
                // Only return LINEAR type Extent offset
                foreach (var ext in Extents)
                {
                    if (ext.TargetType == 0) // LP_TARGET_TYPE_LINEAR
                        return (long)ext.TargetData;
                }
                return -1;
            }

            /// <summary>
            /// Calculate device sector offset (considering sector size conversion)
            /// </summary>
            /// <param name="deviceSectorSize">Device sector size (e.g., 4096)</param>
            /// <returns>Device sector offset</returns>
            public long GetDeviceSectorOffset(int deviceSectorSize)
            {
                long lpOffset = FirstLpSectorOffset;
                if (lpOffset < 0) return -1;

                // LP Metadata 使用 512B 扇区，转换为设备扇区
                long byteOffset = lpOffset * LP_SECTOR_SIZE;
                return byteOffset / deviceSectorSize;
            }

            /// <summary>
            /// Has valid LINEAR extent
            /// </summary>
            public bool HasLinearExtent => Extents.Any(e => e.TargetType == 0);
        }

        public class LpExtentInfo
        {
            public ulong NumSectors { get; set; }      // LP sector count (512B)
            public uint TargetType { get; set; }       // 0=LINEAR, 1=ZERO
            public ulong TargetData { get; set; }      // Physical offset (LP sectors)
            public uint TargetSource { get; set; }     // Block device index
        }

        /// <summary>
        /// Parse partition table from super_meta.raw or super partition header (with cache)
        /// </summary>
        public List<LpPartitionInfo> ParseMetadata(byte[] data)
        {
            // 计算数据哈希用于缓存
            string cacheKey = ComputeDataHash(data);

            // 检查缓存
            List<LpPartitionInfo> cached;
            if (_parseCache.TryGetValue(cacheKey, out cached))
            {
                // Return deep copy to prevent cache modification
                return DeepCopyPartitions(cached);
            }

            var partitions = new List<LpPartitionInfo>();

            // 1. Find ALP0 magic (possible multiple backups, take the first valid one)
            int headerOffset = FindAlp0Header(data);

            if (headerOffset == -1)
                throw new Exception("Could not find a valid LP Metadata Header (ALP0) in the data");

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                ms.Seek(headerOffset, SeekOrigin.Begin);

                // Read Header
                uint hMagic = br.ReadUInt32();
                ushort hMajor = br.ReadUInt16();
                ushort hMinor = br.ReadUInt16();
                uint hHeaderSize = br.ReadUInt32();
                byte[] hChecksum = br.ReadBytes(32);
                uint hTablesSize = br.ReadUInt32();
                byte[] hTablesChecksum = br.ReadBytes(32);

                uint hPartitionsOffset = br.ReadUInt32();
                uint hPartitionsNum = br.ReadUInt32();
                uint hPartitionsEntrySize = br.ReadUInt32();

                uint hExtentsOffset = br.ReadUInt32();
                uint hExtentsNum = br.ReadUInt32();
                uint hExtentsEntrySize = br.ReadUInt32();

                uint hGroupsOffset = br.ReadUInt32();
                uint hGroupsNum = br.ReadUInt32();
                uint hGroupsEntrySize = br.ReadUInt32();

                uint hBlockDevicesOffset = br.ReadUInt32();
                uint hBlockDevicesNum = br.ReadUInt32();
                uint hBlockDevicesEntrySize = br.ReadUInt32();

                // Tables base offset
                long tablesBase = headerOffset + hHeaderSize;

                // 1. Read Extents
                var allExtents = new List<LpExtentInfo>();
                ms.Seek(tablesBase + hExtentsOffset, SeekOrigin.Begin);
                for (int i = 0; i < hExtentsNum; i++)
                {
                    var ext = new LpExtentInfo
                    {
                        NumSectors = br.ReadUInt64(),
                        TargetType = br.ReadUInt32(),
                        TargetData = br.ReadUInt64(),
                        TargetSource = br.ReadUInt32()
                    };
                    allExtents.Add(ext);

                    // Skip remaining entry size if any
                    if (hExtentsEntrySize > 24)
                        ms.Seek(hExtentsEntrySize - 24, SeekOrigin.Current);
                }

                // 2. Read Partitions
                ms.Seek(tablesBase + hPartitionsOffset, SeekOrigin.Begin);
                for (int i = 0; i < hPartitionsNum; i++)
                {
                    byte[] nameBytes = br.ReadBytes(36);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                    byte[] guidBytes = br.ReadBytes(16); // Ignore for now
                    uint attributes = br.ReadUInt32();
                    uint firstExtentIndex = br.ReadUInt32();
                    uint numExtents = br.ReadUInt32();
                    uint groupIndex = br.ReadUInt32();

                    var lpPart = new LpPartitionInfo { Name = name };
                    for (uint j = 0; j < numExtents; j++)
                    {
                        if (firstExtentIndex + j < allExtents.Count)
                        {
                            lpPart.Extents.Add(allExtents[(int)(firstExtentIndex + j)]);
                        }
                    }
                    partitions.Add(lpPart);

                    // Skip remaining entry size
                    if (hPartitionsEntrySize > 64)
                        ms.Seek(hPartitionsEntrySize - 64, SeekOrigin.Current);
                }
            }

            // Store in cache (limit cache size)
            if (_parseCache.Count >= MAX_CACHE_ENTRIES)
            {
                // Simple strategy: Clear cache
                _parseCache.Clear();
            }
            _parseCache[cacheKey] = partitions;

            return DeepCopyPartitions(partitions);
        }

        /// <summary>
        /// Find ALP0 Header position (optimized version: only check common offsets)
        /// </summary>
        private static int FindAlp0Header(byte[] data)
        {
            // ALP0 magic bytes: 0x30 0x50 0x4C 0x41 ("0PLA" little-endian)
            // Common offset locations
            int[] commonOffsets = { 4096, 8192, 0x1000, 0x2000, 0x3000 };

            // First check common locations
            foreach (int offset in commonOffsets)
            {
                if (offset + 6 <= data.Length && IsAlp0Header(data, offset))
                    return offset;
            }

            // Byte-by-byte search (limit range to avoid full scan)
            int maxSearch = Math.Min(data.Length - 4, 0x10000); // Search up to 64KB
            for (int i = 0; i < maxSearch; i++)
            {
                if (IsAlp0Header(data, i))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Check if it is a valid ALP0 Header
        /// </summary>
        private static bool IsAlp0Header(byte[] data, int offset)
        {
            if (offset + 6 > data.Length) return false;

            // Check "0PLA" magic (ALP0 in little-endian)
            if (data[offset] != 0x30 || data[offset + 1] != 0x50 ||
                data[offset + 2] != 0x4c || data[offset + 3] != 0x41)
                return false;

            // Check major version == 10
            ushort major = BitConverter.ToUInt16(data, offset + 4);
            return major == 10;
        }

        /// <summary>
        /// Calculate data hash (for cache key)
        /// </summary>
        private static string ComputeDataHash(byte[] data)
        {
            // Only take the first 4KB to calculate hash (performance optimization)
            int hashLen = Math.Min(data.Length, 4096);
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(data, 0, hashLen);
                return BitConverter.ToString(hash).Replace("-", "") + "_" + data.Length;
            }
        }

        /// <summary>
        /// Deep copy partition list
        /// </summary>
        private static List<LpPartitionInfo> DeepCopyPartitions(List<LpPartitionInfo> source)
        {
            var result = new List<LpPartitionInfo>(source.Count);
            foreach (var p in source)
            {
                var copy = new LpPartitionInfo { Name = p.Name };
                foreach (var ext in p.Extents)
                {
                    copy.Extents.Add(new LpExtentInfo
                    {
                        NumSectors = ext.NumSectors,
                        TargetType = ext.TargetType,
                        TargetData = ext.TargetData,
                        TargetSource = ext.TargetSource
                    });
                }
                result.Add(copy);
            }
            return result;
        }

        /// <summary>
        /// Clear parse cache
        /// </summary>
        public static void ClearCache()
        {
            _parseCache.Clear();
        }
    }
}
