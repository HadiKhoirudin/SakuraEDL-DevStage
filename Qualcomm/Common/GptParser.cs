// ============================================================================
// LoveAlways - GPT 分区表解析器 (借鉴 gpttool 逻辑)
// GPT Partition Table Parser - Enhanced version based on gpttool
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 解析 GPT 分区表，支持自动扇区大小检测、CRC校验、槽位检测
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LoveAlways.Qualcomm.Models;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// GPT Header 信息
    /// </summary>
    public class GptHeaderInfo
    {
        public string Signature { get; set; }           // "EFI PART"
        public uint Revision { get; set; }              // 版本 (通常 0x00010000)
        public uint HeaderSize { get; set; }            // Header 大小 (通常 92)
        public uint HeaderCrc32 { get; set; }           // Header CRC32
        public ulong MyLba { get; set; }                // 当前 Header LBA
        public ulong AlternateLba { get; set; }         // 备份 Header LBA
        public ulong FirstUsableLba { get; set; }       // 第一个可用 LBA
        public ulong LastUsableLba { get; set; }        // 最后可用 LBA
        public string DiskGuid { get; set; }            // 磁盘 GUID
        public ulong PartitionEntryLba { get; set; }    // 分区条目起始 LBA
        public uint NumberOfPartitionEntries { get; set; }  // 分区条目数量
        public uint SizeOfPartitionEntry { get; set; }  // 每条目大小 (通常 128)
        public uint PartitionEntryCrc32 { get; set; }   // 分区条目 CRC32
        
        public bool IsValid { get; set; }
        public bool CrcValid { get; set; }
        public string GptType { get; set; }             // "gptmain" 或 "gptbackup"
        public int SectorSize { get; set; }             // 扇区大小 (512 或 4096)
    }

    /// <summary>
    /// 槽位信息
    /// </summary>
    public class SlotInfo
    {
        public string CurrentSlot { get; set; }         // "a", "b", "undefined", "nonexistent"
        public string OtherSlot { get; set; }
        public bool HasAbPartitions { get; set; }
    }

    /// <summary>
    /// GPT 解析结果
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
    /// GPT 分区表解析器 (借鉴 gpttool)
    /// </summary>
    public class GptParser
    {
        private readonly Action<string> _log;
        
        // GPT 签名
        private static readonly byte[] GPT_SIGNATURE = Encoding.ASCII.GetBytes("EFI PART");
        
        // A/B 分区属性标志
        private const int AB_FLAG_OFFSET = 6;
        private const int AB_PARTITION_ATTR_SLOT_ACTIVE = 0x1 << 2;

        public GptParser(Action<string> log = null)
        {
            _log = log ?? (s => { });
        }

        #region 主要解析方法

        /// <summary>
        /// 解析 GPT 数据
        /// </summary>
        public GptParseResult Parse(byte[] gptData, int lun, int defaultSectorSize = 4096)
        {
            var result = new GptParseResult { Lun = lun };

            try
            {
                if (gptData == null || gptData.Length < 512)
                {
                    result.ErrorMessage = "GPT 数据过小";
                    return result;
                }

                // 1. 查找 GPT Header 并自动检测扇区大小
                int headerOffset = FindGptHeader(gptData);
                if (headerOffset < 0)
                {
                    result.ErrorMessage = "未找到 GPT 签名";
                    return result;
                }

                // 2. 解析 GPT Header
                var header = ParseGptHeader(gptData, headerOffset, defaultSectorSize);
                if (!header.IsValid)
                {
                    result.ErrorMessage = "GPT Header 无效";
                    return result;
                }
                result.Header = header;

                // 3. 自动检测扇区大小 (参考 gpttool)
                // Disk_SecSize_b_Dec = HeaderArea_Start_InF_b_Dec / HeaderArea_Start_Sec_Dec
                if (header.MyLba > 0 && headerOffset > 0)
                {
                    int detectedSectorSize = headerOffset / (int)header.MyLba;
                    if (detectedSectorSize == 512 || detectedSectorSize == 4096)
                    {
                        header.SectorSize = detectedSectorSize;
                        _log(string.Format("[GPT] 自动检测扇区大小: {0} 字节 (Header偏移={1}, MyLBA={2})", 
                            detectedSectorSize, headerOffset, header.MyLba));
                    }
                    else
                    {
                        // 尝试根据分区条目 LBA 推断
                        if (header.PartitionEntryLba == 2)
                        {
                            // 标准情况: 分区条目紧跟 Header
                            header.SectorSize = defaultSectorSize;
                            _log(string.Format("[GPT] 使用默认扇区大小: {0} 字节", defaultSectorSize));
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
                    _log(string.Format("[GPT] MyLBA=0，使用默认扇区大小: {0} 字节", defaultSectorSize));
                }

                // 4. 验证 CRC (可选)
                header.CrcValid = VerifyCrc32(gptData, headerOffset, header);

                // 5. 解析分区条目
                result.Partitions = ParsePartitionEntries(gptData, headerOffset, header, lun);

                // 6. 检测 A/B 槽位
                result.SlotInfo = DetectSlot(result.Partitions);

                result.Success = true;
                _log(string.Format("[GPT] LUN{0}: 解析成功, {1} 个分区, 槽位: {2}, CRC: {3}",
                    lun, result.Partitions.Count, result.SlotInfo.CurrentSlot,
                    header.CrcValid ? "有效" : "无效"));
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _log(string.Format("[GPT] 解析异常: {0}", ex.Message));
            }

            return result;
        }

        #endregion

        #region GPT Header 解析

        /// <summary>
        /// 查找 GPT Header 位置
        /// </summary>
        private int FindGptHeader(byte[] data)
        {
            // 常见偏移位置
            int[] searchOffsets = { 4096, 512, 0, 4096 * 2, 512 * 2 };

            foreach (int offset in searchOffsets)
            {
                if (offset + 92 <= data.Length && MatchSignature(data, offset))
                {
                    _log(string.Format("[GPT] 在偏移 {0} 处找到 GPT Header", offset));
                    return offset;
                }
            }

            // 暴力搜索 (每 512 字节)
            for (int i = 0; i <= data.Length - 92; i += 512)
            {
                if (MatchSignature(data, i))
                {
                    _log(string.Format("[GPT] 暴力搜索: 在偏移 {0} 处找到 GPT Header", i));
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 匹配 GPT 签名
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
        /// 解析 GPT Header
        /// </summary>
        private GptHeaderInfo ParseGptHeader(byte[] data, int offset, int defaultSectorSize)
        {
            var header = new GptHeaderInfo
            {
                SectorSize = defaultSectorSize
            };

            try
            {
                // 签名 (0-8)
                header.Signature = Encoding.ASCII.GetString(data, offset, 8);
                if (header.Signature != "EFI PART")
                {
                    header.IsValid = false;
                    return header;
                }

                // 版本 (8-12)
                header.Revision = BitConverter.ToUInt32(data, offset + 8);

                // Header 大小 (12-16)
                header.HeaderSize = BitConverter.ToUInt32(data, offset + 12);

                // Header CRC32 (16-20)
                header.HeaderCrc32 = BitConverter.ToUInt32(data, offset + 16);

                // 保留 (20-24)

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

                // 判断 GPT 类型
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

        #region 分区条目解析

        /// <summary>
        /// 解析分区条目
        /// </summary>
        private List<PartitionInfo> ParsePartitionEntries(byte[] data, int headerOffset, GptHeaderInfo header, int lun)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                int sectorSize = header.SectorSize > 0 ? header.SectorSize : 4096;
                
                _log(string.Format("[GPT] LUN{0} 开始解析分区条目 (数据长度={1}, HeaderOffset={2}, SectorSize={3})", 
                    lun, data.Length, headerOffset, sectorSize));
                _log(string.Format("[GPT] Header信息: PartitionEntryLba={0}, NumberOfEntries={1}, EntrySize={2}, FirstUsableLba={3}",
                    header.PartitionEntryLba, header.NumberOfPartitionEntries, header.SizeOfPartitionEntry, header.FirstUsableLba));

                // 计算分区条目起始位置 - 尝试多种方式
                int entryOffset = -1;
                
                // 方式1: 使用 Header 中指定的 LBA
                if (header.PartitionEntryLba > 0)
                {
                    long calcOffset = (long)header.PartitionEntryLba * sectorSize;
                    if (calcOffset > 0 && calcOffset < data.Length - 128)
                    {
                        entryOffset = (int)calcOffset;
                        _log(string.Format("[GPT] 使用 PartitionEntryLba 计算: {0} * {1} = {2}", 
                            header.PartitionEntryLba, sectorSize, entryOffset));
                    }
                }
                
                // 方式2: Header 后紧跟分区条目
                if (entryOffset < 0 || entryOffset >= data.Length - 128)
                {
                    entryOffset = headerOffset + sectorSize;
                    _log(string.Format("[GPT] 使用相对位置: HeaderOffset({0}) + SectorSize({1}) = {2}", 
                        headerOffset, sectorSize, entryOffset));
                }
                
                // 方式3: 如果还是无效，尝试常见偏移
                if (entryOffset < 0 || entryOffset >= data.Length - 128)
                {
                    int[] commonOffsets = { 8192, 4096 * 2, 1024, 2048, 4096, 512 * 2 };
                    foreach (int tryOffset in commonOffsets)
                    {
                        if (tryOffset < data.Length - 128 && HasValidPartitionEntry(data, tryOffset))
                        {
                            entryOffset = tryOffset;
                            _log(string.Format("[GPT] 使用探测偏移: {0}", entryOffset));
                            break;
                        }
                    }
                }
                
                // 最终检查
                if (entryOffset < 0 || entryOffset >= data.Length - 128)
                {
                    _log(string.Format("[GPT] 无法确定有效的分区条目偏移, entryOffset={0}, dataLen={1}", entryOffset, data.Length));
                    return partitions;
                }

                int entrySize = (int)header.SizeOfPartitionEntry;
                if (entrySize <= 0 || entrySize > 512) entrySize = 128;

                // ========== 借鉴 gpttool 的计算方式 ==========
                // gpttool: ParEntry_Num = Sec2b(ParEntriesArea_Size_Sec_Dec) / ParEntry_Size_b_Dec
                // ParEntriesArea_Size = FirstUsableLba - PartitionEntryLba (对于 gptmain)
                
                int headerEntries = (int)header.NumberOfPartitionEntries;
                if (headerEntries <= 0) headerEntries = 128;
                
                // 计算实际可用的分区条目数 (gpttool 方式)
                int actualAvailableEntries = 0;
                if (header.FirstUsableLba > header.PartitionEntryLba && header.PartitionEntryLba > 0)
                {
                    // ParEntriesArea_Size = (FirstUsableLba - PartitionEntryLba) * SectorSize
                    long parEntriesAreaSize = (long)(header.FirstUsableLba - header.PartitionEntryLba) * sectorSize;
                    actualAvailableEntries = (int)(parEntriesAreaSize / entrySize);
                    _log(string.Format("[GPT] gpttool方式: ParEntriesArea=({0}-{1})*{2}={3}, 可用条目数={4}", 
                        header.FirstUsableLba, header.PartitionEntryLba, sectorSize, parEntriesAreaSize, actualAvailableEntries));
                }
                
                // 从数据长度计算可扫描的最大条目数
                int maxFromData = Math.Max(0, (data.Length - entryOffset) / entrySize);
                
                // 综合计算: 取 Header指定、实际可用、数据容量 中的最大值
                // 但不超过数据实际能容纳的数量和合理上限 256
                int maxEntries = Math.Max(headerEntries, actualAvailableEntries);
                maxEntries = Math.Min(maxEntries, maxFromData);  // 不能超过数据容量
                maxEntries = Math.Min(maxEntries, 256);          // 合理上限

                _log(string.Format("[GPT] 分区条目: 偏移={0}, 大小={1}, Header数量={2}, gpttool计算={3}, 数据容量={4}, 实际扫描={5}", 
                    entryOffset, entrySize, headerEntries, actualAvailableEntries, maxFromData, maxEntries));

                int parsedCount = 0;
                for (int i = 0; i < maxEntries; i++)
                {
                    int offset = entryOffset + i * entrySize;
                    if (offset + 128 > data.Length)
                    {
                        _log(string.Format("[GPT] 数据不足，已解析 {0} 个分区 (offset={1}, dataLen={2})", 
                            parsedCount, offset, data.Length));
                        break;
                    }

                    // 检查分区类型 GUID 是否为空
                    bool isEmpty = true;
                    for (int j = 0; j < 16; j++)
                    {
                        if (data[offset + j] != 0)
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                    if (isEmpty) continue;

                    // 解析分区条目
                    var partition = ParsePartitionEntry(data, offset, lun, sectorSize, i + 1);
                    if (partition != null && !string.IsNullOrWhiteSpace(partition.Name))
                    {
                        partitions.Add(partition);
                        parsedCount++;
                    }
                }
                
                _log(string.Format("[GPT] LUN{0} 解析完成: {1} 个有效分区", lun, parsedCount));
            }
            catch (Exception ex)
            {
                _log(string.Format("[GPT] 解析分区条目异常: {0}", ex.Message));
            }

            return partitions;
        }
        
        /// <summary>
        /// 检查是否有有效的分区条目
        /// </summary>
        private bool HasValidPartitionEntry(byte[] data, int offset)
        {
            if (offset + 128 > data.Length) return false;
            
            // 检查分区类型 GUID 是否为非空
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
            
            // 检查分区名称是否可读
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
        /// 解析单个分区条目
        /// </summary>
        private PartitionInfo ParsePartitionEntry(byte[] data, int offset, int lun, int sectorSize, int index)
        {
            try
            {
                // 分区类型 GUID (0-16)
                string typeGuid = FormatGuid(data, offset);

                // 分区唯一 GUID (16-32)
                string uniqueGuid = FormatGuid(data, offset + 16);

                // 起始 LBA (32-40)
                long startLba = BitConverter.ToInt64(data, offset + 32);

                // 结束 LBA (40-48)
                long endLba = BitConverter.ToInt64(data, offset + 40);

                // 属性 (48-56)
                ulong attributes = BitConverter.ToUInt64(data, offset + 48);

                // 分区名称 UTF-16LE (56-128)
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
                    Attributes = attributes
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region A/B 槽位检测

        /// <summary>
        /// 检测 A/B 槽位状态
        /// </summary>
        private SlotInfo DetectSlot(List<PartitionInfo> partitions)
        {
            var info = new SlotInfo
            {
                CurrentSlot = "nonexistent",
                OtherSlot = "nonexistent",
                HasAbPartitions = false
            };

            // 查找带 _a 或 _b 后缀的分区
            var abPartitions = partitions.Where(p =>
                p.Name.EndsWith("_a") || p.Name.EndsWith("_b")).ToList();

            if (abPartitions.Count == 0)
                return info;

            info.HasAbPartitions = true;
            info.CurrentSlot = "undefined";

            // 排除 vendor_boot 分区 (可能与整体槽位状态不一致)
            var checkPartitions = abPartitions.Where(p =>
                p.Name != "vendor_boot_a" && p.Name != "vendor_boot_b").ToList();

            int slotAActive = 0;
            int slotBActive = 0;

            foreach (var p in checkPartitions)
            {
                bool isActive = IsSlotActive(p.Attributes);
                if (p.Name.EndsWith("_a") && isActive)
                    slotAActive++;
                else if (p.Name.EndsWith("_b") && isActive)
                    slotBActive++;
            }

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

            return info;
        }

        /// <summary>
        /// 检查槽位是否激活
        /// </summary>
        private bool IsSlotActive(ulong attributes)
        {
            // 根据 gpttool 的逻辑
            byte flagByte = (byte)((attributes >> (AB_FLAG_OFFSET * 8)) & 0xFF);
            return (flagByte & AB_PARTITION_ATTR_SLOT_ACTIVE) == AB_PARTITION_ATTR_SLOT_ACTIVE;
        }

        #endregion

        #region CRC32 校验

        /// <summary>
        /// 验证 CRC32
        /// </summary>
        private bool VerifyCrc32(byte[] data, int headerOffset, GptHeaderInfo header)
        {
            try
            {
                // 计算 Header CRC (需要先将 CRC 字段置零)
                byte[] headerData = new byte[header.HeaderSize];
                Array.Copy(data, headerOffset, headerData, 0, (int)header.HeaderSize);
                
                // 将 CRC 字段置零
                headerData[16] = 0;
                headerData[17] = 0;
                headerData[18] = 0;
                headerData[19] = 0;

                uint calculatedCrc = CalculateCrc32(headerData);
                
                if (calculatedCrc != header.HeaderCrc32)
                {
                    _log(string.Format("[GPT] Header CRC 不匹配: 计算={0:X8}, 存储={1:X8}",
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
        /// CRC32 计算
        /// </summary>
        private uint CalculateCrc32(byte[] data)
        {
            uint[] crcTable = GenerateCrc32Table();
            uint crc = 0xFFFFFFFF;
            
            foreach (byte b in data)
            {
                byte index = (byte)((crc ^ b) & 0xFF);
                crc = (crc >> 8) ^ crcTable[index];
            }
            
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// 生成 CRC32 表
        /// </summary>
        private uint[] GenerateCrc32Table()
        {
            uint[] table = new uint[256];
            uint polynomial = 0xEDB88320;
            
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

        #region GUID 格式化

        /// <summary>
        /// 格式化 GUID (混合端序)
        /// </summary>
        private string FormatGuid(byte[] data, int offset)
        {
            // GPT GUID 格式: 前3部分小端序，后2部分大端序
            // 格式: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX
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
        /// 生成 rawprogram.xml 内容
        /// </summary>
        public string GenerateRawprogramXml(List<PartitionInfo> partitions, int sectorSize)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" ?>");
            sb.AppendLine("<data>");

            foreach (var p in partitions.OrderBy(x => x.Lun).ThenBy(x => x.StartSector))
            {
                long sizeKb = (p.NumSectors * sectorSize) / 1024;
                long startByte = p.StartSector * sectorSize;

                sb.AppendFormat("  <program SECTOR_SIZE_IN_BYTES=\"{0}\" " +
                    "file_sector_offset=\"0\" " +
                    "filename=\"{1}.img\" " +
                    "label=\"{1}\" " +
                    "num_partition_sectors=\"{2}\" " +
                    "partofsingleimage=\"false\" " +
                    "physical_partition_number=\"{3}\" " +
                    "readbackverify=\"false\" " +
                    "size_in_KB=\"{4}\" " +
                    "sparse=\"false\" " +
                    "start_byte_hex=\"0x{5:X}\" " +
                    "start_sector=\"{6}\" />\r\n",
                    sectorSize, p.Name, p.NumSectors, p.Lun, sizeKb, startByte, p.StartSector);
            }

            sb.AppendLine("</data>");
            return sb.ToString();
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
