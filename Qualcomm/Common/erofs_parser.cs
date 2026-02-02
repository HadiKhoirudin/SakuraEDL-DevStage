// ============================================================================
// LoveAlways - EROFS 文件系统解析器
// 基于 erofs_extract 项目移植 - 纯 C# 实现
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 解析 EROFS (Enhanced Read-Only File System) 结构
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// EROFS SuperBlock 结构 (128 字节)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ErofsSuperBlock
    {
        public uint magic;              // 0xE0F5E1E2
        public uint checksum;           // CRC32
        public uint feature_compat;     // 兼容特性
        public byte blkszbits;          // 块大小 = 1 << blkszbits
        public byte sb_extslots;        // SuperBlock 扩展槽数
        public ushort root_nid;         // 根目录节点号
        public ulong inos;              // Inode 总数
        public ulong build_time;        // 构建时间
        public uint build_time_nsec;    // 纳秒
        public uint blocks;             // 块数
        public uint meta_blkaddr;       // 元数据区起始块
        public uint xattr_blkaddr;      // xattr 区起始块

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] uuid;             // UUID

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] volume_name;      // 卷名

        public uint feature_incompat;   // 不兼容特性
        public ushort available_compr_algs; // 可用压缩算法
        public ushort extra_devices;    // 额外设备数
        public ushort devt_slotoff;     // 设备槽偏移
        public byte dirblkbits;         // 目录块大小位
        public byte xattr_prefix_count; // xattr 前缀数
        public uint xattr_prefix_start; // xattr 前缀起始
        public ulong packed_nid;        // 打包 Inode 节点号
        public byte xattr_filter_reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 23)]
        public byte[] reserved2;
    }

    /// <summary>
    /// EROFS Inode (紧凑版, 32 字节)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ErofsInodeCompact
    {
        public ushort i_format;         // 格式标志
        public ushort i_xattr_icount;   // xattr 数量
        public ushort i_mode;           // 文件模式
        public ushort i_nlink;          // 链接数
        public uint i_size;             // 文件大小
        public uint i_reserved;         // 保留
        public uint i_u;                // 块地址/压缩块数/设备号
        public uint i_ino;              // Inode 号
        public ushort i_uid;            // UID
        public ushort i_gid;            // GID
        public uint i_reserved2;        // 保留
    }

    /// <summary>
    /// EROFS Inode (扩展版, 64 字节)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ErofsInodeExtended
    {
        public ushort i_format;
        public ushort i_xattr_icount;
        public ushort i_mode;
        public ushort i_reserved;
        public ulong i_size;
        public uint i_u;
        public uint i_ino;
        public uint i_uid;
        public uint i_gid;
        public ulong i_mtime;
        public uint i_mtime_nsec;
        public uint i_nlink;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] i_reserved2;
    }

    /// <summary>
    /// EROFS 目录项
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ErofsDirEntry
    {
        public ulong nid;               // 节点号
        public ushort nameoff;          // 名称偏移
        public byte file_type;          // 文件类型
        public byte reserved;           // 保留
    }

    /// <summary>
    /// EROFS 文件类型
    /// </summary>
    public enum ErofsFileType : byte
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
    /// EROFS 数据布局
    /// </summary>
    public enum ErofsDataLayout
    {
        FlatPlain = 0,          // 未压缩平坦
        CompressedFull = 1,     // 压缩 (非紧凑索引)
        FlatInline = 2,         // 未压缩 + 内联
        CompressedCompact = 3,  // 压缩 (紧凑索引)
        ChunkBased = 4          // 块分片
    }

    /// <summary>
    /// EROFS 压缩算法
    /// </summary>
    public enum ErofsCompressionType
    {
        Lz4 = 0,
        Lzma = 1,
        Deflate = 2,
        Zstd = 3
    }

    /// <summary>
    /// EROFS 文件系统解析器
    /// </summary>
    public class ErofsParser
    {
        // EROFS 魔数
        public const uint EROFS_SUPER_MAGIC = 0xE0F5E1E2;
        public const int SUPERBLOCK_OFFSET = 1024;

        private readonly Stream _stream;
        private readonly Action<string> _log;
        private ErofsSuperBlock _superBlock;
        private int _blockSize;
        private bool _isValid;

        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid => _isValid;

        /// <summary>
        /// 块大小
        /// </summary>
        public int BlockSize => _blockSize;

        /// <summary>
        /// 卷名
        /// </summary>
        public string VolumeName
        {
            get
            {
                if (_superBlock.volume_name == null) return "";
                return Encoding.UTF8.GetString(_superBlock.volume_name).TrimEnd('\0');
            }
        }

        /// <summary>
        /// UUID
        /// </summary>
        public string Uuid
        {
            get
            {
                if (_superBlock.uuid == null) return "";
                return new Guid(_superBlock.uuid).ToString();
            }
        }

        /// <summary>
        /// 构建时间
        /// </summary>
        public DateTime BuildTime
        {
            get
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(_superBlock.build_time);
            }
        }

        /// <summary>
        /// 根节点 ID
        /// </summary>
        public ushort RootNid => _superBlock.root_nid;

        /// <summary>
        /// 从流创建 EROFS 解析器
        /// </summary>
        public ErofsParser(Stream stream, Action<string> log = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _log = log ?? delegate { };
            _isValid = ParseSuperBlock();
        }

        /// <summary>
        /// 检查是否为 EROFS 文件系统
        /// </summary>
        public static bool IsErofs(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
                return false;
            if (stream.Length < SUPERBLOCK_OFFSET + 4)
                return false;

            long pos = stream.Position;
            try
            {
                stream.Seek(SUPERBLOCK_OFFSET, SeekOrigin.Begin);
                byte[] magic = new byte[4];
                stream.Read(magic, 0, 4);
                uint magicValue = BitConverter.ToUInt32(magic, 0);
                return magicValue == EROFS_SUPER_MAGIC;
            }
            finally
            {
                stream.Seek(pos, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// 检查文件是否为 EROFS 镜像
        /// </summary>
        public static bool IsErofsFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    return IsErofs(fs);
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
                byte[] sbData = new byte[128];
                int read = _stream.Read(sbData, 0, 128);
                if (read < 128)
                    return false;

                _superBlock = BytesToStruct<ErofsSuperBlock>(sbData);

                if (_superBlock.magic != EROFS_SUPER_MAGIC)
                {
                    _log("[EROFS] 无效魔数: 0x" + _superBlock.magic.ToString("X8"));
                    return false;
                }

                _blockSize = 1 << _superBlock.blkszbits;

                _log(string.Format("[EROFS] 解析成功 - 块大小: {0}, 卷名: {1}, 构建时间: {2}",
                    _blockSize, VolumeName, BuildTime.ToString("yyyy-MM-dd HH:mm:ss")));

                return true;
            }
            catch (Exception ex)
            {
                _log("[EROFS] 解析失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 计算 Inode 偏移
        /// </summary>
        private long GetInodeOffset(ulong nid)
        {
            return (long)_superBlock.meta_blkaddr * _blockSize + (long)nid * 32;
        }

        /// <summary>
        /// 读取 Inode (紧凑版)
        /// </summary>
        public ErofsInodeCompact? ReadInodeCompact(ulong nid)
        {
            if (!_isValid)
                return null;

            try
            {
                long offset = GetInodeOffset(nid);
                _stream.Seek(offset, SeekOrigin.Begin);
                byte[] data = new byte[32];
                _stream.Read(data, 0, 32);
                return BytesToStruct<ErofsInodeCompact>(data);
            }
            catch (Exception ex)
            {
                _log("[EROFS] 读取 Inode " + nid + " 失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 获取 Inode 数据布局
        /// </summary>
        public ErofsDataLayout GetDataLayout(ushort i_format)
        {
            return (ErofsDataLayout)((i_format >> 1) & 0x7);
        }

        /// <summary>
        /// 是否为扩展 Inode
        /// </summary>
        public bool IsExtendedInode(ushort i_format)
        {
            return (i_format & 0x1) == 1;
        }

        /// <summary>
        /// 读取目录内容
        /// </summary>
        public List<Tuple<string, ulong, ErofsFileType>> ReadDirectory(ulong nid)
        {
            var entries = new List<Tuple<string, ulong, ErofsFileType>>();

            var inode = ReadInodeCompact(nid);
            if (!inode.HasValue)
                return entries;

            var layout = GetDataLayout(inode.Value.i_format);
            bool isDir = (inode.Value.i_mode & 0xF000) == 0x4000;

            if (!isDir)
                return entries;

            try
            {
                byte[] dirData = ReadInodeData(nid, inode.Value);
                if (dirData == null || dirData.Length < 12)
                    return entries;

                // 解析目录项
                int offset = 0;
                while (offset + 12 <= dirData.Length)
                {
                    ulong entryNid = BitConverter.ToUInt64(dirData, offset);
                    ushort nameoff = BitConverter.ToUInt16(dirData, offset + 8);
                    byte fileType = dirData[offset + 10];

                    if (entryNid == 0 && nameoff == 0)
                        break;

                    // 读取名称
                    int nextNameoff;
                    if (offset + 12 + 12 <= dirData.Length)
                    {
                        nextNameoff = BitConverter.ToUInt16(dirData, offset + 12 + 8);
                        if (nextNameoff <= nameoff)
                            nextNameoff = dirData.Length;
                    }
                    else
                    {
                        nextNameoff = dirData.Length;
                    }

                    int nameLen = Math.Min(nextNameoff - nameoff, 255);
                    if (nameoff + nameLen <= dirData.Length && nameLen > 0)
                    {
                        string name = Encoding.UTF8.GetString(dirData, nameoff, nameLen).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(name))
                        {
                            entries.Add(Tuple.Create(name, entryNid, (ErofsFileType)fileType));
                        }
                    }

                    offset += 12;
                }
            }
            catch (Exception ex)
            {
                _log("[EROFS] 读取目录失败: " + ex.Message);
            }

            return entries;
        }

        /// <summary>
        /// 读取 Inode 数据 (支持所有数据布局)
        /// </summary>
        private byte[] ReadInodeData(ulong nid, ErofsInodeCompact inode)
        {
            var layout = GetDataLayout(inode.i_format);
            int size = (int)inode.i_size;

            if (size <= 0 || size > 64 * 1024 * 1024)
                return null;

            try
            {
                switch (layout)
                {
                    case ErofsDataLayout.FlatPlain:
                        return ReadFlatPlainData(inode, size);

                    case ErofsDataLayout.FlatInline:
                        return ReadFlatInlineData(nid, inode, size);

                    case ErofsDataLayout.ChunkBased:
                        return ReadChunkBasedData(nid, inode, size);

                    case ErofsDataLayout.CompressedFull:
                    case ErofsDataLayout.CompressedCompact:
                        // 压缩数据需要解压，当前只返回原始数据用于分析
                        _log("[EROFS] 压缩数据布局，尝试读取原始数据...");
                        return ReadCompressedDataRaw(nid, inode, size);

                    default:
                        _log("[EROFS] 不支持的数据布局: " + layout);
                        return null;
                }
            }
            catch (Exception ex)
            {
                _log("[EROFS] 读取数据失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 读取 Chunk-Based 数据
        /// </summary>
        private byte[] ReadChunkBasedData(ulong nid, ErofsInodeCompact inode, int size)
        {
            try
            {
                long inodeOffset = GetInodeOffset(nid);
                int inodeSize = IsExtendedInode(inode.i_format) ? 64 : 32;
                int xattrSize = inode.i_xattr_icount > 0 ? 12 + (inode.i_xattr_icount - 1) * 4 : 0;
                int chunkIndexOffset = inodeSize + xattrSize;

                // 计算 chunk 数量
                int chunkBits = _superBlock.blkszbits; // 通常与块大小相同
                int chunkSize = 1 << chunkBits;
                int chunkCount = (size + chunkSize - 1) / chunkSize;

                var result = new MemoryStream();

                for (int i = 0; i < chunkCount && result.Length < size; i++)
                {
                    // 读取 chunk 索引 (8 字节)
                    _stream.Seek(inodeOffset + chunkIndexOffset + i * 8, SeekOrigin.Begin);
                    byte[] chunkIdx = new byte[8];
                    _stream.Read(chunkIdx, 0, 8);

                    uint blkAddr = BitConverter.ToUInt32(chunkIdx, 0);
                    // uint deviceId = BitConverter.ToUInt16(chunkIdx, 4);
                    // ushort reserved = BitConverter.ToUInt16(chunkIdx, 6);

                    if (blkAddr == 0xFFFFFFFF)
                    {
                        // Hole chunk - 填充零
                        int toWrite = Math.Min(chunkSize, size - (int)result.Length);
                        result.Write(new byte[toWrite], 0, toWrite);
                    }
                    else
                    {
                        // 读取实际数据
                        long dataOffset = (long)blkAddr * _blockSize;
                        _stream.Seek(dataOffset, SeekOrigin.Begin);
                        int toRead = Math.Min(chunkSize, size - (int)result.Length);
                        byte[] data = new byte[toRead];
                        _stream.Read(data, 0, toRead);
                        result.Write(data, 0, toRead);
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 读取压缩数据的原始块 (不解压)
        /// </summary>
        private byte[] ReadCompressedDataRaw(ulong nid, ErofsInodeCompact inode, int size)
        {
            try
            {
                // 对于压缩数据，我们尝试读取 inode.i_u 指向的数据块
                // 这可能是压缩后的数据，但对于 build.prop 这样的小文件
                // 有时候数据是内联的或者只有少量压缩块

                long dataOffset = (long)inode.i_u * _blockSize;
                int readSize = Math.Min(size * 2, 128 * 1024); // 读取更多以覆盖压缩开销

                _stream.Seek(dataOffset, SeekOrigin.Begin);
                byte[] data = new byte[readSize];
                int actualRead = _stream.Read(data, 0, readSize);

                if (actualRead > 0)
                {
                    // 尝试在原始数据中查找 build.prop 内容
                    string content = System.Text.Encoding.UTF8.GetString(data, 0, actualRead);

                    // 如果能找到 ro. 开头的属性，说明数据可能未压缩或部分可读
                    if (content.Contains("ro.") || content.Contains("build.prop"))
                    {
                        return data;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 读取平坦数据
        /// </summary>
        private byte[] ReadFlatPlainData(ErofsInodeCompact inode, int size)
        {
            long dataOffset = (long)inode.i_u * _blockSize;
            _stream.Seek(dataOffset, SeekOrigin.Begin);
            byte[] data = new byte[size];
            _stream.Read(data, 0, size);
            return data;
        }

        /// <summary>
        /// 读取内联数据
        /// </summary>
        private byte[] ReadFlatInlineData(ulong nid, ErofsInodeCompact inode, int size)
        {
            // 内联数据紧跟 Inode 之后
            long inodeOffset = GetInodeOffset(nid);
            int inodeSize = IsExtendedInode(inode.i_format) ? 64 : 32;

            // xattr 大小
            int xattrSize = 0;
            if (inode.i_xattr_icount > 0)
            {
                xattrSize = 12 + (inode.i_xattr_icount - 1) * 4;
            }

            // 对齐到 4 字节
            int alignedSize = (inodeSize + xattrSize + 3) & ~3;

            long dataOffset = inodeOffset + alignedSize;

            // 检查内联部分
            int tailSize = _blockSize - (int)(dataOffset % _blockSize);

            if (size <= tailSize)
            {
                // 完全内联
                _stream.Seek(dataOffset, SeekOrigin.Begin);
                byte[] data = new byte[size];
                _stream.Read(data, 0, size);
                return data;
            }
            else
            {
                // 部分内联 + 外部块
                byte[] data = new byte[size];

                // 读取内联部分
                _stream.Seek(dataOffset, SeekOrigin.Begin);
                _stream.Read(data, 0, tailSize);

                // 读取外部块
                long blockAddr = (long)inode.i_u * _blockSize;
                _stream.Seek(blockAddr, SeekOrigin.Begin);
                _stream.Read(data, tailSize, size - tailSize);

                return data;
            }
        }

        /// <summary>
        /// 查找文件
        /// </summary>
        public ulong? FindFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string[] parts = path.Trim('/').Split('/');
            ulong currentNid = _superBlock.root_nid;

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                var entries = ReadDirectory(currentNid);
                bool found = false;

                foreach (var entry in entries)
                {
                    if (entry.Item1.Equals(part, StringComparison.OrdinalIgnoreCase))
                    {
                        currentNid = entry.Item2;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return null;
            }

            return currentNid;
        }

        /// <summary>
        /// 读取文本文件
        /// </summary>
        public string ReadTextFile(string path, Encoding encoding = null)
        {
            ulong? nid = FindFile(path);
            if (!nid.HasValue)
                return null;

            var inode = ReadInodeCompact(nid.Value);
            if (!inode.HasValue)
                return null;

            byte[] content = ReadInodeData(nid.Value, inode.Value);
            if (content == null)
                return null;

            return (encoding ?? Encoding.UTF8).GetString(content).TrimEnd('\0');
        }

        /// <summary>
        /// 读取 build.prop
        /// </summary>
        public Dictionary<string, string> ReadBuildProp(string path = "/system/build.prop")
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string content = ReadTextFile(path);
            if (string.IsNullOrEmpty(content))
            {
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

        #region 辅助方法

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
