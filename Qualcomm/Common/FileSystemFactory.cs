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
    }
}
