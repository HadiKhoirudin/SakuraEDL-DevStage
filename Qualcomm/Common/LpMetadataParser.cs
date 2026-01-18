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
    /// 用于解析 super 分区的布局信息 (带缓存)
    /// </summary>
    public class LpMetadataParser
    {
        private const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467; // gDla (OPUS Header)
        private const uint LP_METADATA_HEADER_MAGIC = 0x414c5030;   // ALP0

        // LP Metadata 使用的固定扇区大小
        public const int LP_SECTOR_SIZE = 512;

        // 静态解析缓存 (按数据哈希缓存结果)
        private static readonly ConcurrentDictionary<string, List<LpPartitionInfo>> _parseCache 
            = new ConcurrentDictionary<string, List<LpPartitionInfo>>();

        // 缓存大小限制
        private const int MAX_CACHE_ENTRIES = 10;

        public class LpPartitionInfo
        {
            public string Name { get; set; }
            public List<LpExtentInfo> Extents { get; set; } = new List<LpExtentInfo>();
            
            /// <summary>
            /// 总大小（LP 扇区数，每扇区 512 字节）
            /// </summary>
            public long TotalSizeLpSectors => Extents.Sum(e => (long)e.NumSectors);
            
            /// <summary>
            /// 总大小（字节）
            /// </summary>
            public long TotalSizeBytes => TotalSizeLpSectors * LP_SECTOR_SIZE;
            
            /// <summary>
            /// 第一个 Extent 的物理偏移（LP 扇区，512 字节/扇区）
            /// 仅对 LINEAR 类型有效
            /// </summary>
            public long FirstLpSectorOffset => GetFirstLinearOffset();
            
            private long GetFirstLinearOffset()
            {
                // 只返回 LINEAR 类型的 Extent 偏移
                foreach (var ext in Extents)
                {
                    if (ext.TargetType == 0) // LP_TARGET_TYPE_LINEAR
                        return (long)ext.TargetData;
                }
                return -1;
            }
            
            /// <summary>
            /// 计算设备扇区偏移（考虑扇区大小转换）
            /// </summary>
            /// <param name="deviceSectorSize">设备扇区大小（如 4096）</param>
            /// <returns>设备扇区偏移</returns>
            public long GetDeviceSectorOffset(int deviceSectorSize)
            {
                long lpOffset = FirstLpSectorOffset;
                if (lpOffset < 0) return -1;
                
                // LP Metadata 使用 512B 扇区，转换为设备扇区
                long byteOffset = lpOffset * LP_SECTOR_SIZE;
                return byteOffset / deviceSectorSize;
            }
            
            /// <summary>
            /// 是否有有效的 LINEAR Extent
            /// </summary>
            public bool HasLinearExtent => Extents.Any(e => e.TargetType == 0);
        }

        public class LpExtentInfo
        {
            public ulong NumSectors { get; set; }      // LP 扇区数（512B）
            public uint TargetType { get; set; }       // 0=LINEAR, 1=ZERO
            public ulong TargetData { get; set; }      // 物理偏移（LP 扇区）
            public uint TargetSource { get; set; }     // 块设备索引
        }

        /// <summary>
        /// 从 super_meta.raw 或 super 分区头部解析分区表 (带缓存)
        /// </summary>
        public List<LpPartitionInfo> ParseMetadata(byte[] data)
        {
            // 计算数据哈希用于缓存
            string cacheKey = ComputeDataHash(data);
            
            // 检查缓存
            List<LpPartitionInfo> cached;
            if (_parseCache.TryGetValue(cacheKey, out cached))
            {
                // 返回深拷贝，防止缓存被修改
                return DeepCopyPartitions(cached);
            }
            
            var partitions = new List<LpPartitionInfo>();
            
            // 1. 查找 ALP0 魔数 (可能会有多个备份，取第一个有效的)
            int headerOffset = FindAlp0Header(data);

            if (headerOffset == -1)
                throw new Exception("未能在数据中找到有效的 LP Metadata Header (ALP0)");

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

            // 存入缓存 (限制缓存大小)
            if (_parseCache.Count >= MAX_CACHE_ENTRIES)
            {
                // 简单策略: 清空缓存
                _parseCache.Clear();
            }
            _parseCache[cacheKey] = partitions;

            return DeepCopyPartitions(partitions);
        }

        /// <summary>
        /// 查找 ALP0 Header 位置 (优化版: 只检查常见偏移)
        /// </summary>
        private static int FindAlp0Header(byte[] data)
        {
            // ALP0 magic bytes: 0x30 0x50 0x4C 0x41 ("0PLA" little-endian)
            // 常见偏移位置
            int[] commonOffsets = { 4096, 8192, 0x1000, 0x2000, 0x3000 };
            
            // 先检查常见位置
            foreach (int offset in commonOffsets)
            {
                if (offset + 6 <= data.Length && IsAlp0Header(data, offset))
                    return offset;
            }
            
            // 逐字节搜索 (限制范围避免全量扫描)
            int maxSearch = Math.Min(data.Length - 4, 0x10000); // 最多搜索 64KB
            for (int i = 0; i < maxSearch; i++)
            {
                if (IsAlp0Header(data, i))
                    return i;
            }
            
            return -1;
        }

        /// <summary>
        /// 检查是否为有效的 ALP0 Header
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
        /// 计算数据哈希 (用于缓存键)
        /// </summary>
        private static string ComputeDataHash(byte[] data)
        {
            // 只取前 4KB 计算哈希 (性能优化)
            int hashLen = Math.Min(data.Length, 4096);
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(data, 0, hashLen);
                return BitConverter.ToString(hash).Replace("-", "") + "_" + data.Length;
            }
        }

        /// <summary>
        /// 深拷贝分区列表
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
        /// 清除解析缓存
        /// </summary>
        public static void ClearCache()
        {
            _parseCache.Clear();
        }
    }
}
