// ============================================================================
// LoveAlways - EXT4 文件系统解析器
// 基于 SharpExt4 项目移植 - 纯 C# 实现
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 解析 EXT2/3/4 文件系统结构，提取文件 (如 build.prop)
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// EXT4 SuperBlock 结构 (1024 字节)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Ext4SuperBlock
    {
        public uint s_inodes_count;           // Inode 总数
        public uint s_blocks_count_lo;        // 块总数 (低32位)
        public uint s_r_blocks_count_lo;      // 保留块数 (低32位)
        public uint s_free_blocks_count_lo;   // 空闲块数 (低32位)
        public uint s_free_inodes_count;      // 空闲 Inode 数
        public uint s_first_data_block;       // 第一个数据块
        public uint s_log_block_size;         // 块大小 = 1024 << s_log_block_size
        public uint s_log_cluster_size;       // 簇大小
        public uint s_blocks_per_group;       // 每组块数
        public uint s_clusters_per_group;     // 每组簇数
        public uint s_inodes_per_group;       // 每组 Inode 数
        public uint s_mtime;                  // 挂载时间
        public uint s_wtime;                  // 写入时间
        public ushort s_mnt_count;            // 挂载次数
        public short s_max_mnt_count;         // 最大挂载次数
        public ushort s_magic;                // 魔数 0xEF53
        public ushort s_state;                // 文件系统状态
        public ushort s_errors;               // 错误处理方式
        public ushort s_minor_rev_level;      // 次版本号
        public uint s_lastcheck;              // 最后检查时间
        public uint s_checkinterval;          // 检查间隔
        public uint s_creator_os;             // 创建操作系统
        public uint s_rev_level;              // 版本级别
        public ushort s_def_resuid;           // 默认保留 UID
        public ushort s_def_resgid;           // 默认保留 GID

        // EXT4 扩展字段
        public uint s_first_ino;              // 第一个非保留 Inode
        public ushort s_inode_size;           // Inode 大小
        public ushort s_block_group_nr;       // 此 SuperBlock 所在块组
        public uint s_feature_compat;         // 兼容特性
        public uint s_feature_incompat;       // 不兼容特性
        public uint s_feature_ro_compat;      // 只读兼容特性

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] s_uuid;                 // 128位 UUID

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] s_volume_name;          // 卷标

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] s_last_mounted;         // 最后挂载路径

        public uint s_algorithm_usage_bitmap; // 压缩算法位图

        // 更多字段省略...
    }

    /// <summary>
    /// EXT4 Inode 结构 (128/256 字节)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Ext4Inode
    {
        public ushort i_mode;                 // 文件模式
        public ushort i_uid;                  // 所有者 UID (低16位)
        public uint i_size_lo;                // 文件大小 (低32位)
        public uint i_atime;                  // 访问时间
        public uint i_ctime;                  // 创建时间
        public uint i_mtime;                  // 修改时间
        public uint i_dtime;                  // 删除时间
        public ushort i_gid;                  // 所有者 GID (低16位)
        public ushort i_links_count;          // 硬链接数
        public uint i_blocks_lo;              // 块数 (低32位)
        public uint i_flags;                  // 文件标志
        public uint i_osd1;                   // OS 相关字段1

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
        public byte[] i_block;                // 块指针 (15 * 4 字节)

        public uint i_generation;             // 文件版本
        public uint i_file_acl_lo;            // ACL (低32位)
        public uint i_size_high;              // 文件大小 (高32位)
        public uint i_obso_faddr;             // 废弃字段

        // EXT4 扩展
        public ushort i_blocks_high;          // 块数 (高16位)
        public ushort i_file_acl_high;        // ACL (高16位)
        public ushort i_uid_high;             // UID (高16位)
        public ushort i_gid_high;             // GID (高16位)
        public ushort i_checksum_lo;          // Inode 校验和 (低16位)
        public ushort i_reserved;             // 保留
    }

    /// <summary>
    /// EXT4 目录项结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Ext4DirEntry
    {
        public uint inode;                    // Inode 号
        public ushort rec_len;                // 记录长度
        public byte name_len;                 // 名称长度
        public byte file_type;                // 文件类型
        // 后跟 name_len 字节的文件名
    }

    /// <summary>
    /// EXT4 文件类型
    /// </summary>
    public enum Ext4FileType : byte
    {
        Unknown = 0,
        RegularFile = 1,
        Directory = 2,
        CharDevice = 3,
        BlockDevice = 4,
        Fifo = 5,
        Socket = 6,
        SymbolicLink = 7
    }

    /// <summary>
    /// EXT4 文件系统解析器
    /// </summary>
    public class Ext4Parser
    {
        // EXT4 魔数
        public const ushort EXT4_SUPER_MAGIC = 0xEF53;
        public const int SUPERBLOCK_OFFSET = 1024;
        public const int SUPERBLOCK_SIZE = 1024;

        // Inode 特殊值
        public const uint EXT4_ROOT_INO = 2;

        private readonly Stream _stream;
        private readonly Action<string> _log;
        private Ext4SuperBlock _superBlock;
        private int _blockSize;
        private int _inodeSize;
        private bool _isValid;

        /// <summary>
        /// 是否有效的 EXT4 文件系统
        /// </summary>
        public bool IsValid => _isValid;

        /// <summary>
        /// 块大小
        /// </summary>
        public int BlockSize => _blockSize;

        /// <summary>
        /// 卷标
        /// </summary>
        public string VolumeName
        {
            get
            {
                if (_superBlock.s_volume_name == null) return "";
                return Encoding.UTF8.GetString(_superBlock.s_volume_name).TrimEnd('\0');
            }
        }

        /// <summary>
        /// UUID
        /// </summary>
        public string Uuid
        {
            get
            {
                if (_superBlock.s_uuid == null) return "";
                return new Guid(_superBlock.s_uuid).ToString();
            }
        }

        /// <summary>
        /// 从流创建 EXT4 解析器
        /// </summary>
        public Ext4Parser(Stream stream, Action<string> log = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _log = log ?? delegate { };
            _isValid = ParseSuperBlock();
        }

        /// <summary>
        /// 检查是否为 EXT4 文件系统
        /// </summary>
        public static bool IsExt4(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
                return false;
            if (stream.Length < SUPERBLOCK_OFFSET + 2)
                return false;

            long pos = stream.Position;
            try
            {
                stream.Seek(SUPERBLOCK_OFFSET + 56, SeekOrigin.Begin); // s_magic 偏移
                byte[] magic = new byte[2];
                stream.Read(magic, 0, 2);
                ushort magicValue = BitConverter.ToUInt16(magic, 0);
                return magicValue == EXT4_SUPER_MAGIC;
            }
            finally
            {
                stream.Seek(pos, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// 检查文件是否为 EXT4 镜像
        /// </summary>
        public static bool IsExt4File(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    return IsExt4(fs);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析 SuperBlock
        /// </summary>
        private bool ParseSuperBlock()
        {
            try
            {
                _stream.Seek(SUPERBLOCK_OFFSET, SeekOrigin.Begin);
                byte[] sbData = new byte[SUPERBLOCK_SIZE];
                int read = _stream.Read(sbData, 0, SUPERBLOCK_SIZE);
                if (read < 264) // 最小需要的字节数
                    return false;

                _superBlock = BytesToStruct<Ext4SuperBlock>(sbData);

                if (_superBlock.s_magic != EXT4_SUPER_MAGIC)
                {
                    _log("[EXT4] 无效魔数: 0x" + _superBlock.s_magic.ToString("X4"));
                    return false;
                }

                _blockSize = 1024 << (int)_superBlock.s_log_block_size;
                _inodeSize = _superBlock.s_inode_size > 0 ? _superBlock.s_inode_size : 128;

                _log(string.Format("[EXT4] 解析成功 - 块大小: {0}, Inode大小: {1}, 卷标: {2}",
                    _blockSize, _inodeSize, VolumeName));

                return true;
            }
            catch (Exception ex)
            {
                _log("[EXT4] 解析失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 读取 Inode
        /// </summary>
        public Ext4Inode? ReadInode(uint inodeNum)
        {
            if (!_isValid || inodeNum < 1)
                return null;

            try
            {
                // 计算 Inode 所在块组
                uint blockGroup = (inodeNum - 1) / _superBlock.s_inodes_per_group;
                uint localIndex = (inodeNum - 1) % _superBlock.s_inodes_per_group;

                // 块组描述符表位于 SuperBlock 后的第一个完整块
                long bgdtOffset = _blockSize >= 2048 ? _blockSize : 2048;

                // 读取块组描述符 (简化: 假设 32 字节描述符)
                _stream.Seek(bgdtOffset + blockGroup * 32, SeekOrigin.Begin);
                byte[] bgd = new byte[32];
                _stream.Read(bgd, 0, 32);

                // Inode 表块地址 (偏移 8)
                uint inodeTableBlock = BitConverter.ToUInt32(bgd, 8);

                // 计算 Inode 位置
                long inodeOffset = (long)inodeTableBlock * _blockSize + localIndex * _inodeSize;

                _stream.Seek(inodeOffset, SeekOrigin.Begin);
                byte[] inodeData = new byte[_inodeSize];
                _stream.Read(inodeData, 0, Math.Min(_inodeSize, 128));

                return BytesToStruct<Ext4Inode>(inodeData);
            }
            catch (Exception ex)
            {
                _log("[EXT4] 读取 Inode " + inodeNum + " 失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 读取目录内容
        /// </summary>
        public List<Tuple<string, uint, Ext4FileType>> ReadDirectory(uint inodeNum)
        {
            var entries = new List<Tuple<string, uint, Ext4FileType>>();

            var inode = ReadInode(inodeNum);
            if (!inode.HasValue)
                return entries;

            try
            {
                // 使用 extent 或直接块
                uint flags = inode.Value.i_flags;
                bool useExtents = (flags & 0x80000) != 0; // EXT4_EXTENTS_FL

                byte[] dirData;
                if (useExtents)
                {
                    dirData = ReadExtentData(inode.Value);
                }
                else
                {
                    dirData = ReadDirectBlockData(inode.Value);
                }

                if (dirData == null || dirData.Length == 0)
                    return entries;

                // 解析目录项
                int offset = 0;
                while (offset < dirData.Length - 8)
                {
                    uint entryInode = BitConverter.ToUInt32(dirData, offset);
                    ushort recLen = BitConverter.ToUInt16(dirData, offset + 4);
                    byte nameLen = dirData[offset + 6];
                    byte fileType = dirData[offset + 7];

                    if (recLen == 0 || recLen > 4096)
                        break;

                    if (entryInode != 0 && nameLen > 0 && offset + 8 + nameLen <= dirData.Length)
                    {
                        string name = Encoding.UTF8.GetString(dirData, offset + 8, nameLen);
                        entries.Add(Tuple.Create(name, entryInode, (Ext4FileType)fileType));
                    }

                    offset += recLen;
                }
            }
            catch (Exception ex)
            {
                _log("[EXT4] 读取目录失败: " + ex.Message);
            }

            return entries;
        }

        /// <summary>
        /// 读取文件内容
        /// </summary>
        public byte[] ReadFileContent(uint inodeNum, int maxSize = 1024 * 1024)
        {
            var inode = ReadInode(inodeNum);
            if (!inode.HasValue)
                return null;

            long fileSize = inode.Value.i_size_lo | ((long)inode.Value.i_size_high << 32);
            if (fileSize > maxSize)
                fileSize = maxSize;

            try
            {
                uint flags = inode.Value.i_flags;
                bool useExtents = (flags & 0x80000) != 0;

                if (useExtents)
                {
                    return ReadExtentData(inode.Value, (int)fileSize);
                }
                else
                {
                    return ReadDirectBlockData(inode.Value, (int)fileSize);
                }
            }
            catch (Exception ex)
            {
                _log("[EXT4] 读取文件失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 查找文件 (支持路径如 /etc/build.prop)
        /// </summary>
        public uint? FindFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string[] parts = path.Trim('/').Split('/');
            uint currentInode = EXT4_ROOT_INO;

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                var entries = ReadDirectory(currentInode);
                bool found = false;

                foreach (var entry in entries)
                {
                    if (entry.Item1.Equals(part, StringComparison.OrdinalIgnoreCase))
                    {
                        currentInode = entry.Item2;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return null;
            }

            return currentInode;
        }

        /// <summary>
        /// 读取文本文件
        /// </summary>
        public string ReadTextFile(string path, Encoding encoding = null)
        {
            uint? inode = FindFile(path);
            if (!inode.HasValue)
                return null;

            byte[] content = ReadFileContent(inode.Value);
            if (content == null)
                return null;

            return (encoding ?? Encoding.UTF8).GetString(content).TrimEnd('\0');
        }

        /// <summary>
        /// 读取 build.prop 文件
        /// </summary>
        public Dictionary<string, string> ReadBuildProp(string path = "/system/build.prop")
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string content = ReadTextFile(path);
            if (string.IsNullOrEmpty(content))
            {
                // 尝试其他路径
                string[] altPaths = { "/build.prop", "/etc/build.prop", "/vendor/build.prop" };
                foreach (var altPath in altPaths)
                {
                    content = ReadTextFile(altPath);
                    if (!string.IsNullOrEmpty(content))
                        break;
                }
            }

            if (string.IsNullOrEmpty(content))
                return props;

            foreach (var line in content.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                int eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = trimmed.Substring(0, eqIndex).Trim();
                    string value = trimmed.Substring(eqIndex + 1).Trim();
                    props[key] = value;
                }
            }

            return props;
        }

        #region 私有方法

        /// <summary>
        /// 读取 Extent 数据
        /// </summary>
        private byte[] ReadExtentData(Ext4Inode inode, int maxSize = 0)
        {
            // 简化实现: 只处理单层 Extent
            if (inode.i_block == null || inode.i_block.Length < 12)
                return null;

            // Extent Header
            ushort magic = BitConverter.ToUInt16(inode.i_block, 0);
            if (magic != 0xF30A) // EXT4_EXT_MAGIC
                return ReadDirectBlockData(inode, maxSize);

            ushort entries = BitConverter.ToUInt16(inode.i_block, 2);
            // ushort max = BitConverter.ToUInt16(inode.i_block, 4);
            ushort depth = BitConverter.ToUInt16(inode.i_block, 6);

            if (depth != 0 || entries == 0)
                return null; // 暂不支持多层 Extent

            long fileSize = inode.i_size_lo | ((long)inode.i_size_high << 32);
            if (maxSize > 0 && maxSize < fileSize)
                fileSize = maxSize;

            var result = new MemoryStream();

            // 解析 Extent 条目 (每个 12 字节, 从偏移 12 开始)
            for (int i = 0; i < entries && result.Length < fileSize; i++)
            {
                int extOffset = 12 + i * 12;
                if (extOffset + 12 > inode.i_block.Length)
                    break;

                // uint ee_block = BitConverter.ToUInt32(inode.i_block, extOffset);
                ushort ee_len = BitConverter.ToUInt16(inode.i_block, extOffset + 4);
                ushort ee_start_hi = BitConverter.ToUInt16(inode.i_block, extOffset + 6);
                uint ee_start_lo = BitConverter.ToUInt32(inode.i_block, extOffset + 8);

                long startBlock = ee_start_lo | ((long)ee_start_hi << 32);
                int blocks = ee_len > 32768 ? ee_len - 32768 : ee_len;

                _stream.Seek(startBlock * _blockSize, SeekOrigin.Begin);
                for (int b = 0; b < blocks && result.Length < fileSize; b++)
                {
                    int toRead = (int)Math.Min(_blockSize, fileSize - result.Length);
                    byte[] blockData = new byte[toRead];
                    _stream.Read(blockData, 0, toRead);
                    result.Write(blockData, 0, toRead);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// 读取直接块数据 (完整支持间接块)
        /// </summary>
        private byte[] ReadDirectBlockData(Ext4Inode inode, int maxSize = 0)
        {
            if (inode.i_block == null || inode.i_block.Length < 60)
                return null;

            long fileSize = inode.i_size_lo | ((long)inode.i_size_high << 32);
            if (maxSize > 0 && maxSize < fileSize)
                fileSize = maxSize;

            var result = new MemoryStream();
            int pointersPerBlock = _blockSize / 4; // 每个块能容纳的指针数

            // 直接块 (12个, 偏移 0-47)
            for (int i = 0; i < 12 && result.Length < fileSize; i++)
            {
                uint blockNum = BitConverter.ToUInt32(inode.i_block, i * 4);
                if (blockNum == 0)
                    continue;

                ReadBlockToStream(blockNum, result, ref fileSize);
            }

            if (result.Length >= fileSize)
                return result.ToArray();

            // 一级间接块 (偏移 48)
            uint indirectBlock = BitConverter.ToUInt32(inode.i_block, 48);
            if (indirectBlock != 0)
            {
                ReadIndirectBlocks(indirectBlock, 1, result, ref fileSize, pointersPerBlock);
            }

            if (result.Length >= fileSize)
                return result.ToArray();

            // 二级间接块 (偏移 52)
            uint doubleIndirectBlock = BitConverter.ToUInt32(inode.i_block, 52);
            if (doubleIndirectBlock != 0)
            {
                ReadIndirectBlocks(doubleIndirectBlock, 2, result, ref fileSize, pointersPerBlock);
            }

            if (result.Length >= fileSize)
                return result.ToArray();

            // 三级间接块 (偏移 56)
            uint tripleIndirectBlock = BitConverter.ToUInt32(inode.i_block, 56);
            if (tripleIndirectBlock != 0)
            {
                ReadIndirectBlocks(tripleIndirectBlock, 3, result, ref fileSize, pointersPerBlock);
            }

            return result.ToArray();
        }

        /// <summary>
        /// 读取单个块到流
        /// </summary>
        private void ReadBlockToStream(uint blockNum, MemoryStream result, ref long remainingSize)
        {
            if (blockNum == 0 || result.Length >= remainingSize)
                return;

            _stream.Seek((long)blockNum * _blockSize, SeekOrigin.Begin);
            int toRead = (int)Math.Min(_blockSize, remainingSize - result.Length);
            byte[] blockData = new byte[toRead];
            _stream.Read(blockData, 0, toRead);
            result.Write(blockData, 0, toRead);
        }

        /// <summary>
        /// 递归读取间接块
        /// </summary>
        private void ReadIndirectBlocks(uint blockNum, int level, MemoryStream result, ref long remainingSize, int pointersPerBlock)
        {
            if (blockNum == 0 || result.Length >= remainingSize || level < 1)
                return;

            // 读取指针块
            _stream.Seek((long)blockNum * _blockSize, SeekOrigin.Begin);
            byte[] pointerBlock = new byte[_blockSize];
            _stream.Read(pointerBlock, 0, _blockSize);

            for (int i = 0; i < pointersPerBlock && result.Length < remainingSize; i++)
            {
                uint nextBlock = BitConverter.ToUInt32(pointerBlock, i * 4);
                if (nextBlock == 0)
                    continue;

                if (level == 1)
                {
                    // 直接数据块
                    ReadBlockToStream(nextBlock, result, ref remainingSize);
                }
                else
                {
                    // 递归处理下一层间接块
                    ReadIndirectBlocks(nextBlock, level - 1, result, ref remainingSize, pointersPerBlock);
                }
            }
        }

        /// <summary>
        /// 字节数组转结构体
        /// </summary>
        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        #endregion
    }
}
