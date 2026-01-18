using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// Android Logical Partition (LP) Metadata Parser
    /// 用于解析 super 分区的布局信息
    /// </summary>
    public class LpMetadataParser
    {
        private const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467; // gDla (OPUS Header)
        private const uint LP_METADATA_HEADER_MAGIC = 0x414c5030;   // ALP0

        // LP Metadata 使用的固定扇区大小
        public const int LP_SECTOR_SIZE = 512;

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
        /// 从 super_meta.raw 或 super 分区头部解析分区表
        /// </summary>
        public List<LpPartitionInfo> ParseMetadata(byte[] data)
        {
            var partitions = new List<LpPartitionInfo>();
            
            // 1. 查找 ALP0 魔数 (可能会有多个备份，取第一个有效的)
            int headerOffset = -1;
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == 0x30 && data[i + 1] == 0x50 && data[i + 2] == 0x4c && data[i + 3] == 0x41)
                {
                    // 检查是否为 Header (Major version 应为 10)
                    ushort major = BitConverter.ToUInt16(data, i + 4);
                    if (major == 10)
                    {
                        headerOffset = i;
                        break;
                    }
                }
            }

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

            return partitions;
        }
    }
}
