// ============================================================================
// LoveAlways - 文件系统工厂
// 统一接口访问 EXT4/EROFS 文件系统
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 自动检测文件系统类型并创建相应解析器
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// 文件系统类型
    /// </summary>
    public enum FileSystemType
    {
        Unknown,
        Ext4,
        Erofs,
        Sparse  // Sparse 格式 (需要先展开)
    }

    /// <summary>
    /// 通用文件系统接口
    /// </summary>
    public interface IFileSystemParser : IDisposable
    {
        /// <summary>
        /// 文件系统类型
        /// </summary>
        FileSystemType Type { get; }

        /// <summary>
        /// 是否有效
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// 块大小
        /// </summary>
        int BlockSize { get; }

        /// <summary>
        /// 卷名
        /// </summary>
        string VolumeName { get; }

        /// <summary>
        /// 读取文本文件
        /// </summary>
        string ReadTextFile(string path);

        /// <summary>
        /// 读取 build.prop
        /// </summary>
        Dictionary<string, string> ReadBuildProp(string path = null);

        /// <summary>
        /// 文件是否存在
        /// </summary>
        bool FileExists(string path);

        /// <summary>
        /// 列出目录
        /// </summary>
        List<string> ListDirectory(string path);
    }

    /// <summary>
    /// EXT4 文件系统适配器
    /// </summary>
    public class Ext4FileSystemAdapter : IFileSystemParser
    {
        private readonly Ext4Parser _parser;
        private readonly Stream _stream;
        private bool _disposed;

        public FileSystemType Type => FileSystemType.Ext4;
        public bool IsValid => _parser.IsValid;
        public int BlockSize => _parser.BlockSize;
        public string VolumeName => _parser.VolumeName;

        public Ext4FileSystemAdapter(Stream stream, Action<string> log = null)
        {
            _stream = stream;
            _parser = new Ext4Parser(stream, log);
        }

        public string ReadTextFile(string path)
        {
            return _parser.ReadTextFile(path);
        }

        public Dictionary<string, string> ReadBuildProp(string path = null)
        {
            return _parser.ReadBuildProp(path ?? "/system/build.prop");
        }

        public bool FileExists(string path)
        {
            return _parser.FindFile(path).HasValue;
        }

        public List<string> ListDirectory(string path)
        {
            var result = new List<string>();
            uint? dirInode = _parser.FindFile(path);
            if (!dirInode.HasValue)
                return result;

            var entries = _parser.ReadDirectory(dirInode.Value);
            foreach (var entry in entries)
            {
                if (entry.Item1 != "." && entry.Item1 != "..")
                    result.Add(entry.Item1);
            }
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // 不关闭传入的 Stream
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// EROFS 文件系统适配器
    /// </summary>
    public class ErofsFileSystemAdapter : IFileSystemParser
    {
        private readonly ErofsParser _parser;
        private readonly Stream _stream;
        private bool _disposed;

        public FileSystemType Type => FileSystemType.Erofs;
        public bool IsValid => _parser.IsValid;
        public int BlockSize => _parser.BlockSize;
        public string VolumeName => _parser.VolumeName;

        public ErofsFileSystemAdapter(Stream stream, Action<string> log = null)
        {
            _stream = stream;
            _parser = new ErofsParser(stream, log);
        }

        public string ReadTextFile(string path)
        {
            return _parser.ReadTextFile(path);
        }

        public Dictionary<string, string> ReadBuildProp(string path = null)
        {
            return _parser.ReadBuildProp(path ?? "/system/build.prop");
        }

        public bool FileExists(string path)
        {
            return _parser.FindFile(path).HasValue;
        }

        public List<string> ListDirectory(string path)
        {
            var result = new List<string>();
            ulong? dirNid = _parser.FindFile(path);
            if (!dirNid.HasValue)
                return result;

            var entries = _parser.ReadDirectory(dirNid.Value);
            foreach (var entry in entries)
            {
                if (entry.Item1 != "." && entry.Item1 != "..")
                    result.Add(entry.Item1);
            }
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 文件系统工厂
    /// </summary>
    public static class FileSystemFactory
    {
        /// <summary>
        /// 检测文件系统类型
        /// </summary>
        public static FileSystemType DetectType(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
                return FileSystemType.Unknown;

            // 检查 Sparse
            if (SparseStream.IsSparseStream(stream))
                return FileSystemType.Sparse;

            // 检查 EROFS (先检查，因为偏移相同)
            if (ErofsParser.IsErofs(stream))
                return FileSystemType.Erofs;

            // 检查 EXT4
            if (Ext4Parser.IsExt4(stream))
                return FileSystemType.Ext4;

            return FileSystemType.Unknown;
        }

        /// <summary>
        /// 检测文件类型
        /// </summary>
        public static FileSystemType DetectType(string filePath)
        {
            if (!File.Exists(filePath))
                return FileSystemType.Unknown;

            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    return DetectType(fs);
                }
            }
            catch
            {
                return FileSystemType.Unknown;
            }
        }

        /// <summary>
        /// 创建文件系统解析器
        /// </summary>
        public static IFileSystemParser Create(Stream stream, Action<string> log = null)
        {
            var type = DetectType(stream);

            switch (type)
            {
                case FileSystemType.Ext4:
                    return new Ext4FileSystemAdapter(stream, log);

                case FileSystemType.Erofs:
                    return new ErofsFileSystemAdapter(stream, log);

                case FileSystemType.Sparse:
                    // 展开 Sparse 后再检测
                    var sparse = new SparseStream(stream, true, log);
                    if (!sparse.IsValid)
                        return null;
                    return Create(sparse, log);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 从文件创建解析器
        /// </summary>
        public static IFileSystemParser CreateFromFile(string filePath, Action<string> log = null)
        {
            if (!File.Exists(filePath))
                return null;

            var stream = File.OpenRead(filePath);
            var parser = Create(stream, log);

            if (parser == null)
            {
                stream.Dispose();
                return null;
            }

            return parser;
        }

        /// <summary>
        /// 从字节数据创建解析器
        /// </summary>
        public static IFileSystemParser CreateFromBytes(byte[] data, Action<string> log = null)
        {
            if (data == null || data.Length == 0)
                return null;

            var stream = new MemoryStream(data);
            return Create(stream, log);
        }

        /// <summary>
        /// 快速读取 build.prop
        /// </summary>
        public static Dictionary<string, string> QuickReadBuildProp(Stream stream, Action<string> log = null)
        {
            using (var parser = Create(stream, log))
            {
                if (parser == null || !parser.IsValid)
                    return new Dictionary<string, string>();

                return parser.ReadBuildProp();
            }
        }

        /// <summary>
        /// 快速读取 build.prop (从文件)
        /// </summary>
        public static Dictionary<string, string> QuickReadBuildProp(string filePath, Action<string> log = null)
        {
            using (var parser = CreateFromFile(filePath, log))
            {
                if (parser == null || !parser.IsValid)
                    return new Dictionary<string, string>();

                return parser.ReadBuildProp();
            }
        }

        /// <summary>
        /// 快速读取 build.prop (从字节)
        /// </summary>
        public static Dictionary<string, string> QuickReadBuildProp(byte[] data, Action<string> log = null)
        {
            using (var parser = CreateFromBytes(data, log))
            {
                if (parser == null || !parser.IsValid)
                    return new Dictionary<string, string>();

                return parser.ReadBuildProp();
            }
        }

        /// <summary>
        /// 检测字节数据的文件系统类型
        /// </summary>
        public static FileSystemType DetectTypeFromHeader(byte[] header)
        {
            if (header == null || header.Length < 2048)
                return FileSystemType.Unknown;

            // 检查 Sparse (魔数在偏移 0)
            if (header.Length >= 4)
            {
                uint sparseMagic = BitConverter.ToUInt32(header, 0);
                if (sparseMagic == 0xED26FF3A)
                    return FileSystemType.Sparse;
            }

            // 检查 EROFS (魔数在偏移 1024)
            if (header.Length >= 1028)
            {
                uint erofsMagic = BitConverter.ToUInt32(header, 1024);
                if (erofsMagic == 0xE0F5E1E2)
                    return FileSystemType.Erofs;
            }

            // 检查 EXT4 (魔数在偏移 1024 + 56)
            if (header.Length >= 1082)
            {
                ushort ext4Magic = BitConverter.ToUInt16(header, 1024 + 56);
                if (ext4Magic == 0xEF53)
                    return FileSystemType.Ext4;
            }

            return FileSystemType.Unknown;
        }

        /// <summary>
        /// 获取文件系统名称
        /// </summary>
        public static string GetFileSystemName(FileSystemType type)
        {
            switch (type)
            {
                case FileSystemType.Ext4: return "EXT4";
                case FileSystemType.Erofs: return "EROFS";
                case FileSystemType.Sparse: return "Sparse";
                default: return "Unknown";
            }
        }
    }

    /// <summary>
    /// 设备文件系统读取器 - 支持从设备委托读取
    /// </summary>
    public class DeviceFileSystemReader : IDisposable
    {
        /// <summary>
        /// 设备读取委托类型
        /// </summary>
        public delegate byte[] ReadDelegate(long offset, int size);

        private readonly ReadDelegate _read;
        private readonly Action<string> _log;
        private readonly long _baseOffset;
        private FileSystemType _fsType;
        private bool _isValid;

        // EXT4 参数
        private uint _ext4BlockSize;
        private uint _ext4InodeSize;
        private uint _ext4InodesPerGroup;
        private long _ext4InodeTableBlock;
        private uint _ext4FirstDataBlock;
        private uint _ext4BlocksPerGroup;
        private bool _ext4HasExtents;
        private bool _ext4Is64Bit;
        private long _ext4BgdtOffset;
        private int _ext4BgdSize;

        // EROFS 参数
        private uint _erofsBlockSize;
        private uint _erofsMetaBlkAddr;
        private ushort _erofsRootNid;

        public bool IsValid => _isValid;
        public FileSystemType Type => _fsType;

        /// <summary>
        /// 从设备读取委托创建文件系统读取器
        /// </summary>
        /// <param name="read">读取委托 (从分区起始开始)</param>
        /// <param name="baseOffset">基础偏移 (如果在 super 分区内)</param>
        /// <param name="log">日志回调</param>
        public DeviceFileSystemReader(ReadDelegate read, long baseOffset = 0, Action<string> log = null)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _baseOffset = baseOffset;
            _log = log ?? delegate { };
            _isValid = Initialize();
        }

        /// <summary>
        /// 初始化文件系统参数
        /// </summary>
        private bool Initialize()
        {
            // 读取头部数据
            byte[] header = _read(_baseOffset, 4096);
            if (header == null || header.Length < 2048)
                return false;

            _fsType = FileSystemFactory.DetectTypeFromHeader(header);

            switch (_fsType)
            {
                case FileSystemType.Ext4:
                    return InitializeExt4(header);
                case FileSystemType.Erofs:
                    return InitializeErofs(header);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 初始化 EXT4 参数
        /// </summary>
        private bool InitializeExt4(byte[] header)
        {
            try
            {
                // 验证 magic
                ushort magic = BitConverter.ToUInt16(header, 1024 + 0x38);
                if (magic != 0xEF53)
                    return false;

                uint sLogBlockSize = BitConverter.ToUInt32(header, 1024 + 0x18);
                _ext4BlockSize = 1024u << (int)sLogBlockSize;
                _ext4InodesPerGroup = BitConverter.ToUInt32(header, 1024 + 0x28);
                _ext4InodeSize = BitConverter.ToUInt16(header, 1024 + 0x58);
                _ext4FirstDataBlock = BitConverter.ToUInt32(header, 1024 + 0x14);
                _ext4BlocksPerGroup = BitConverter.ToUInt32(header, 1024 + 0x20);
                uint featureIncompat = BitConverter.ToUInt32(header, 1024 + 0x60);

                _ext4HasExtents = (featureIncompat & 0x40) != 0;
                _ext4Is64Bit = (featureIncompat & 0x80) != 0;
                _ext4BgdSize = _ext4Is64Bit ? 64 : 32;
                _ext4BgdtOffset = (_ext4FirstDataBlock + 1) * _ext4BlockSize;

                // 读取第一个块组描述符
                byte[] bgd = _read(_baseOffset + _ext4BgdtOffset, _ext4BgdSize);
                if (bgd == null || bgd.Length < _ext4BgdSize)
                    return false;

                uint bgInodeTableLo = BitConverter.ToUInt32(bgd, 0x08);
                uint bgInodeTableHi = _ext4Is64Bit ? BitConverter.ToUInt32(bgd, 0x28) : 0;
                _ext4InodeTableBlock = bgInodeTableLo | ((long)bgInodeTableHi << 32);

                _log(string.Format("[EXT4] 初始化成功 - BlockSize={0}, InodeSize={1}", _ext4BlockSize, _ext4InodeSize));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 初始化 EROFS 参数
        /// </summary>
        private bool InitializeErofs(byte[] header)
        {
            try
            {
                uint magic = BitConverter.ToUInt32(header, 1024);
                if (magic != 0xE0F5E1E2)
                    return false;

                byte blkSzBits = header[1024 + 0x0C];
                _erofsBlockSize = 1u << blkSzBits;
                _erofsRootNid = BitConverter.ToUInt16(header, 1024 + 0x0E);
                _erofsMetaBlkAddr = BitConverter.ToUInt32(header, 1024 + 0x28);

                _log(string.Format("[EROFS] 初始化成功 - BlockSize={0}, RootNid={1}", _erofsBlockSize, _erofsRootNid));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取 build.prop 文件
        /// </summary>
        public Dictionary<string, string> ReadBuildProp()
        {
            string content = ReadTextFile("/build.prop");
            if (string.IsNullOrEmpty(content))
                content = ReadTextFile("/system/build.prop");
            if (string.IsNullOrEmpty(content))
                content = ReadTextFile("/etc/build.prop");

            return ParseBuildPropContent(content);
        }

        /// <summary>
        /// 读取文本文件
        /// </summary>
        public string ReadTextFile(string path)
        {
            if (!_isValid || string.IsNullOrEmpty(path))
                return null;

            byte[] data = null;
            switch (_fsType)
            {
                case FileSystemType.Ext4:
                    data = ReadExt4File(path);
                    break;
                case FileSystemType.Erofs:
                    data = ReadErofsFile(path);
                    break;
            }

            if (data == null || data.Length == 0)
                return null;

            return Encoding.UTF8.GetString(data).TrimEnd('\0');
        }

        /// <summary>
        /// 从 EXT4 读取文件
        /// </summary>
        private byte[] ReadExt4File(string path)
        {
            // 从根目录开始查找
            string[] parts = path.Trim('/').Split('/');
            uint currentInode = 2; // EXT4_ROOT_INO

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                var entries = ReadExt4Directory(currentInode);
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

            return ReadExt4FileByInode(currentInode);
        }

        /// <summary>
        /// 读取 EXT4 目录
        /// </summary>
        private List<Tuple<string, uint, byte>> ReadExt4Directory(uint inodeNum)
        {
            var entries = new List<Tuple<string, uint, byte>>();

            byte[] dirData = ReadExt4InodeData(inodeNum);
            if (dirData == null || dirData.Length < 12)
                return entries;

            int offset = 0;
            while (offset + 8 <= dirData.Length)
            {
                uint inode = BitConverter.ToUInt32(dirData, offset);
                ushort recLen = BitConverter.ToUInt16(dirData, offset + 4);
                byte nameLen = dirData[offset + 6];
                byte fileType = dirData[offset + 7];

                if (recLen < 8 || recLen > dirData.Length - offset)
                    break;

                if (inode != 0 && nameLen > 0 && offset + 8 + nameLen <= dirData.Length)
                {
                    string name = Encoding.UTF8.GetString(dirData, offset + 8, nameLen);
                    if (name != "." && name != "..")
                        entries.Add(Tuple.Create(name, inode, fileType));
                }

                offset += recLen;
            }

            return entries;
        }

        /// <summary>
        /// 读取 EXT4 Inode 数据
        /// </summary>
        private byte[] ReadExt4InodeData(uint inodeNum)
        {
            byte[] inode = ReadExt4Inode(inodeNum);
            if (inode == null || inode.Length < 128)
                return null;

            uint iSizeLo = BitConverter.ToUInt32(inode, 0x04);
            uint iFlags = BitConverter.ToUInt32(inode, 0x20);
            bool useExtents = (iFlags & 0x80000) != 0;

            int maxSize = (int)Math.Min(iSizeLo, 4 * 1024 * 1024);

            if (useExtents)
            {
                return ReadExt4ExtentData(inode, maxSize);
            }
            else
            {
                uint block0 = BitConverter.ToUInt32(inode, 0x28);
                if (block0 > 0)
                {
                    return _read(_baseOffset + (long)block0 * _ext4BlockSize, maxSize);
                }
            }

            return null;
        }

        /// <summary>
        /// 读取 EXT4 Inode
        /// </summary>
        private byte[] ReadExt4Inode(uint inodeNum)
        {
            uint localIndex = (inodeNum - 1) % _ext4InodesPerGroup;
            long inodeOffset = _ext4InodeTableBlock * _ext4BlockSize + localIndex * _ext4InodeSize;
            return _read(_baseOffset + inodeOffset, (int)_ext4InodeSize);
        }

        /// <summary>
        /// 读取 EXT4 文件内容 (按 Inode)
        /// </summary>
        private byte[] ReadExt4FileByInode(uint inodeNum)
        {
            byte[] inode = ReadExt4Inode(inodeNum);
            if (inode == null || inode.Length < 128)
                return null;

            ushort iMode = BitConverter.ToUInt16(inode, 0x00);
            if ((iMode & 0xF000) != 0x8000) // 不是普通文件
                return null;

            return ReadExt4InodeData(inodeNum);
        }

        /// <summary>
        /// 读取 EXT4 Extent 数据
        /// </summary>
        private byte[] ReadExt4ExtentData(byte[] inode, int maxSize)
        {
            if (inode == null || inode.Length < 0x28 + 12)
                return null;

            ushort ehMagic = BitConverter.ToUInt16(inode, 0x28);
            if (ehMagic != 0xF30A)
                return null;

            ushort ehEntries = BitConverter.ToUInt16(inode, 0x28 + 2);
            ushort ehDepth = BitConverter.ToUInt16(inode, 0x28 + 6);

            if (ehDepth != 0) // 暂不支持多层 extent
                return null;

            var result = new List<byte>();
            for (int i = 0; i < ehEntries && result.Count < maxSize; i++)
            {
                int entryOffset = 0x28 + 12 + i * 12;
                if (entryOffset + 12 > inode.Length)
                    break;

                ushort eeLen = BitConverter.ToUInt16(inode, entryOffset + 4);
                ushort eeStartHi = BitConverter.ToUInt16(inode, entryOffset + 6);
                uint eeStartLo = BitConverter.ToUInt32(inode, entryOffset + 8);

                int actualLen = eeLen & 0x7FFF;
                if (actualLen == 0) continue;

                long physBlock = eeStartLo | ((long)eeStartHi << 32);
                int readSize = Math.Min((int)(actualLen * _ext4BlockSize), maxSize - result.Count);

                byte[] data = _read(_baseOffset + physBlock * _ext4BlockSize, readSize);
                if (data != null)
                    result.AddRange(data);
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// 从 EROFS 读取文件
        /// </summary>
        private byte[] ReadErofsFile(string path)
        {
            string[] parts = path.Trim('/').Split('/');
            ulong currentNid = _erofsRootNid;

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                var entries = ReadErofsDirectory(currentNid);
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

            return ReadErofsFileByNid(currentNid);
        }

        /// <summary>
        /// 读取 EROFS 目录
        /// </summary>
        private List<Tuple<string, ulong, byte>> ReadErofsDirectory(ulong nid)
        {
            var entries = new List<Tuple<string, ulong, byte>>();

            byte[] inode = ReadErofsInode(nid);
            if (inode == null || inode.Length < 32)
                return entries;

            ushort format = BitConverter.ToUInt16(inode, 0);
            bool isExtended = (format & 1) == 1;
            byte dataLayout = (byte)((format >> 1) & 0x7);
            ushort mode = BitConverter.ToUInt16(inode, 0x04);

            if ((mode & 0xF000) != 0x4000) // 不是目录
                return entries;

            long dirSize = isExtended ? BitConverter.ToInt64(inode, 0x08) : BitConverter.ToUInt32(inode, 0x08);
            byte[] dirData = ReadErofsInodeData(nid, inode, (int)Math.Min(dirSize, _erofsBlockSize * 4));

            if (dirData == null || dirData.Length < 12)
                return entries;

            // 解析 EROFS 目录项
            ushort firstNameOff = BitConverter.ToUInt16(dirData, 8);
            if (firstNameOff == 0 || firstNameOff > dirData.Length)
                return entries;

            int direntCount = firstNameOff / 12;
            var dirents = new List<Tuple<ulong, ushort, byte>>();

            for (int i = 0; i < direntCount && i * 12 + 12 <= dirData.Length; i++)
            {
                ulong entryNid = BitConverter.ToUInt64(dirData, i * 12);
                ushort nameOff = BitConverter.ToUInt16(dirData, i * 12 + 8);
                byte fileType = dirData[i * 12 + 10];
                dirents.Add(Tuple.Create(entryNid, nameOff, fileType));
            }

            for (int i = 0; i < dirents.Count; i++)
            {
                var d = dirents[i];
                int nameEnd = (i + 1 < dirents.Count) ? dirents[i + 1].Item2 : dirData.Length;
                if (d.Item2 >= dirData.Length) continue;

                int nameLen = 0;
                for (int j = 0; j < nameEnd - d.Item2 && d.Item2 + j < dirData.Length; j++)
                {
                    if (dirData[d.Item2 + j] == 0) break;
                    nameLen++;
                }

                if (nameLen > 0)
                {
                    string name = Encoding.UTF8.GetString(dirData, d.Item2, nameLen);
                    entries.Add(Tuple.Create(name, d.Item1, d.Item3));
                }
            }

            return entries;
        }

        /// <summary>
        /// 读取 EROFS Inode
        /// </summary>
        private byte[] ReadErofsInode(ulong nid)
        {
            long offset = (long)_erofsMetaBlkAddr * _erofsBlockSize + (long)nid * 32;
            return _read(_baseOffset + offset, 64);
        }

        /// <summary>
        /// 读取 EROFS Inode 数据
        /// </summary>
        private byte[] ReadErofsInodeData(ulong nid, byte[] inode, int maxSize)
        {
            if (inode == null || inode.Length < 32)
                return null;

            ushort format = BitConverter.ToUInt16(inode, 0);
            bool isExtended = (format & 1) == 1;
            byte dataLayout = (byte)((format >> 1) & 0x7);
            uint rawBlkAddr = BitConverter.ToUInt32(inode, 0x10);
            int inodeSize = isExtended ? 64 : 32;
            ushort xattrCount = BitConverter.ToUInt16(inode, 0x02);
            int xattrSize = xattrCount > 0 ? 12 + (xattrCount - 1) * 4 : 0;
            xattrSize = (xattrSize + 3) & ~3; // 4字节对齐
            int inlineDataOffset = inodeSize + xattrSize;

            if (dataLayout == 2) // FLAT_INLINE
            {
                long inodeOffset = (long)_erofsMetaBlkAddr * _erofsBlockSize + (long)nid * 32;
                int totalSize = inlineDataOffset + maxSize;
                byte[] data = _read(_baseOffset + inodeOffset, totalSize);
                if (data != null && data.Length > inlineDataOffset)
                {
                    int dataLen = Math.Min(maxSize, data.Length - inlineDataOffset);
                    byte[] result = new byte[dataLen];
                    Array.Copy(data, inlineDataOffset, result, 0, dataLen);
                    return result;
                }
            }
            else if (dataLayout == 0) // FLAT_PLAIN
            {
                long dataOffset = (long)rawBlkAddr * _erofsBlockSize;
                return _read(_baseOffset + dataOffset, maxSize);
            }
            else if (dataLayout == 1 || dataLayout == 3) // FLAT_COMPR / FLAT_COMPR_FULL (LZ4压缩)
            {
                return ReadErofsCompressedData(nid, inode, maxSize, isExtended, inlineDataOffset, rawBlkAddr);
            }

            return null;
        }

        /// <summary>
        /// 读取 EROFS 压缩数据 (LZ4)
        /// </summary>
        private byte[] ReadErofsCompressedData(ulong nid, byte[] inode, int maxSize, bool isExtended, int inlineDataOffset, uint rawBlkAddr)
        {
            try
            {
                // 获取文件原始大小
                long fileSize = isExtended ? BitConverter.ToInt64(inode, 0x08) : BitConverter.ToUInt32(inode, 0x08);
                if (fileSize <= 0 || fileSize > maxSize)
                    fileSize = maxSize;

                // EROFS 压缩使用 cluster 概念
                // 简化实现: 直接读取压缩数据块并尝试解压

                // 方法1: 如果 rawBlkAddr 有效，从该位置读取压缩数据
                if (rawBlkAddr > 0 && rawBlkAddr != 0xFFFFFFFF)
                {
                    long dataOffset = (long)rawBlkAddr * _erofsBlockSize;

                    // 读取足够的压缩数据 (通常压缩率约 30-50%)
                    int compressedReadSize = (int)Math.Min(fileSize, _erofsBlockSize * 4);
                    byte[] compressedData = _read(_baseOffset + dataOffset, compressedReadSize);

                    if (compressedData != null && compressedData.Length > 0)
                    {
                        // 尝试 LZ4 解压
                        byte[] decompressed = Lz4Decoder.DecompressErofsBlock(compressedData, (int)fileSize);
                        if (decompressed != null && decompressed.Length > 0)
                        {
                            _log(string.Format("[EROFS] LZ4 解压成功: {0} -> {1} bytes",
                                compressedData.Length, decompressed.Length));
                            return decompressed;
                        }

                        // 如果 LZ4 解压失败，尝试直接返回数据 (可能是未压缩的)
                        if (compressedData.Length >= fileSize)
                        {
                            byte[] result = new byte[fileSize];
                            Array.Copy(compressedData, 0, result, 0, (int)fileSize);
                            return result;
                        }
                    }
                }

                // 方法2: 读取压缩索引 (z_erofs_map_blocks 简化版)
                long inodeOffset = (long)_erofsMetaBlkAddr * _erofsBlockSize + (long)nid * 32;
                byte[] fullInode = _read(_baseOffset + inodeOffset, inlineDataOffset + 256);

                if (fullInode != null && fullInode.Length > inlineDataOffset)
                {
                    // 检查内联压缩数据
                    int availableData = fullInode.Length - inlineDataOffset;
                    if (availableData > 0)
                    {
                        byte[] inlineData = new byte[availableData];
                        Array.Copy(fullInode, inlineDataOffset, inlineData, 0, availableData);

                        // 尝试解压内联数据
                        byte[] decompressed = Lz4Decoder.DecompressErofsBlock(inlineData, (int)fileSize);
                        if (decompressed != null && decompressed.Length > 0)
                            return decompressed;
                    }
                }

                _log(string.Format("[EROFS] 压缩数据读取失败 (NID: {0})", nid));
            }
            catch (Exception ex)
            {
                _log(string.Format("[EROFS] 解压异常: {0}", ex.Message));
            }

            return null;
        }

        /// <summary>
        /// 读取 EROFS 文件 (按 Nid)
        /// </summary>
        private byte[] ReadErofsFileByNid(ulong nid)
        {
            byte[] inode = ReadErofsInode(nid);
            if (inode == null || inode.Length < 32)
                return null;

            ushort format = BitConverter.ToUInt16(inode, 0);
            bool isExtended = (format & 1) == 1;
            ushort mode = BitConverter.ToUInt16(inode, 0x04);

            if ((mode & 0xF000) != 0x8000) // 不是普通文件
                return null;

            long fileSize = isExtended ? BitConverter.ToInt64(inode, 0x08) : BitConverter.ToUInt32(inode, 0x08);
            return ReadErofsInodeData(nid, inode, (int)Math.Min(fileSize, 1024 * 1024));
        }

        /// <summary>
        /// 解析 build.prop 内容
        /// </summary>
        private Dictionary<string, string> ParseBuildPropContent(string content)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        public void Dispose()
        {
            // 无需释放，委托由调用者管理
        }
    }
}
