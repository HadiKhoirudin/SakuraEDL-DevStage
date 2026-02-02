// ============================================================================
// LoveAlways - Android Sparse 镜像透明读取流
// SparseStream - 将 Sparse 格式镜像转换为 Raw 流
// ============================================================================
// 模块: Qualcomm.Common
// 功能: 透明读取 Android Sparse 格式镜像，支持随机访问
// ============================================================================
//
// Sparse 格式说明:
// - Magic: 0xED26FF3A
// - Header: 28 字节
// - Chunk 类型:
//   - RAW (0xCAC1): 原始数据
//   - FILL (0xCAC2): 填充数据 (4字节值重复)
//   - DONT_CARE (0xCAC3): 零填充
//   - CRC32 (0xCAC4): 校验和
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// Sparse 镜像透明读取流
    /// 将 Sparse 格式转换为可随机访问的 Raw 流
    /// </summary>
    public class SparseStream : Stream
    {
        // Sparse 常量
        private const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;
        private const ushort CHUNK_TYPE_RAW = 0xCAC1;
        private const ushort CHUNK_TYPE_FILL = 0xCAC2;
        private const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        private const ushort CHUNK_TYPE_CRC32 = 0xCAC4;
        private const int SPARSE_HEADER_SIZE = 28;

        private readonly Stream _baseStream;
        private readonly bool _leaveOpen;
        private readonly Action<string> _log;

        // Header 信息
        private ushort _majorVersion;
        private ushort _minorVersion;
        private ushort _fileHeaderSize;
        private ushort _chunkHeaderSize;
        private uint _blockSize;
        private uint _totalBlocks;
        private uint _totalChunks;

        // Chunk 索引
        private List<ChunkInfo> _chunkIndex;

        // 状态
        private long _position;
        private long _expandedLength;
        private bool _isValid;
        private bool _disposed;

        /// <summary>
        /// 检查流是否为 Sparse 格式
        /// </summary>
        public static bool IsSparseStream(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
                return false;

            if (stream.Length < SPARSE_HEADER_SIZE)
                return false;

            long originalPos = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                byte[] magicBytes = new byte[4];
                stream.Read(magicBytes, 0, 4);
                uint magic = BitConverter.ToUInt32(magicBytes, 0);
                return magic == SPARSE_HEADER_MAGIC;
            }
            finally
            {
                stream.Seek(originalPos, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// 检查文件是否为 Sparse 格式
        /// </summary>
        public static bool IsSparseFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    return IsSparseStream(fs);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从文件打开 SparseStream
        /// </summary>
        public static SparseStream Open(string filePath, Action<string> log = null)
        {
            var fs = File.OpenRead(filePath);
            return new SparseStream(fs, false, log);
        }

        /// <summary>
        /// 获取实际有数据的块数量（RAW + FILL chunks）
        /// </summary>
        public int GetRealDataChunkCount()
        {
            if (_chunkIndex == null) return 0;
            int count = 0;
            foreach (var chunk in _chunkIndex)
            {
                if (chunk.Type == CHUNK_TYPE_RAW || chunk.Type == CHUNK_TYPE_FILL)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 获取实际有数据的总字节数（不含 DONT_CARE）
        /// </summary>
        public long GetRealDataSize()
        {
            if (_chunkIndex == null) return 0;
            long size = 0;
            foreach (var chunk in _chunkIndex)
            {
                if (chunk.Type == CHUNK_TYPE_RAW || chunk.Type == CHUNK_TYPE_FILL)
                    size += chunk.OutputSize;
            }
            return size;
        }

        /// <summary>
        /// 获取有数据块的列表（用于智能写入）
        /// 返回: (起始偏移, 长度) 列表
        /// </summary>
        public List<Tuple<long, long>> GetDataRanges()
        {
            var ranges = new List<Tuple<long, long>>();
            if (_chunkIndex == null) return ranges;

            foreach (var chunk in _chunkIndex)
            {
                if (chunk.Type == CHUNK_TYPE_RAW || chunk.Type == CHUNK_TYPE_FILL)
                {
                    ranges.Add(Tuple.Create(chunk.OutputOffset, chunk.OutputSize));
                }
            }
            return ranges;
        }

        /// <summary>
        /// 检查指定位置是否有实际数据（非 DONT_CARE）
        /// </summary>
        public bool HasDataAt(long position)
        {
            var chunk = FindChunk(position);
            if (chunk == null) return false;
            return chunk.Type == CHUNK_TYPE_RAW || chunk.Type == CHUNK_TYPE_FILL;
        }

        /// <summary>
        /// 创建 SparseStream
        /// </summary>
        public SparseStream(Stream baseStream, bool leaveOpen = false, Action<string> log = null)
        {
            if (baseStream == null)
                throw new ArgumentNullException("baseStream");

            _baseStream = baseStream;
            _leaveOpen = leaveOpen;
            _log = log ?? delegate { };

            if (!_baseStream.CanRead)
                throw new ArgumentException("Base stream must be readable");
            if (!_baseStream.CanSeek)
                throw new ArgumentException("Base stream must be seekable");

            _chunkIndex = new List<ChunkInfo>();
            _isValid = ParseHeader();
        }

        /// <summary>
        /// 是否有效的 Sparse 镜像
        /// </summary>
        public bool IsValid { get { return _isValid; } }

        /// <summary>
        /// 展开后的大小
        /// </summary>
        public long ExpandedSize { get { return _expandedLength; } }

        /// <summary>
        /// Sparse 版本
        /// </summary>
        public string Version { get { return string.Format("{0}.{1}", _majorVersion, _minorVersion); } }

        /// <summary>
        /// 块大小
        /// </summary>
        public uint BlockSize { get { return _blockSize; } }

        /// <summary>
        /// 总块数
        /// </summary>
        public uint TotalBlocks { get { return _totalBlocks; } }

        /// <summary>
        /// Chunk 数量
        /// </summary>
        public uint TotalChunks { get { return _totalChunks; } }

        #region Stream Implementation

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }

        public override long Length { get { return _expandedLength; } }

        public override long Position
        {
            get { return _position; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");
                _position = value;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_isValid)
                throw new InvalidOperationException("Invalid sparse image");
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (offset + count > buffer.Length)
                throw new ArgumentException("Buffer too small");

            if (_position >= _expandedLength)
                return 0;

            int totalRead = 0;
            long remaining = Math.Min(count, _expandedLength - _position);

            while (remaining > 0)
            {
                var chunk = FindChunk(_position);
                if (chunk == null)
                {
                    int zeroCount = (int)Math.Min(remaining, _blockSize);
                    Array.Clear(buffer, offset + totalRead, zeroCount);
                    totalRead += zeroCount;
                    _position += zeroCount;
                    remaining -= zeroCount;
                    continue;
                }

                long chunkOffset = _position - chunk.OutputOffset;
                long chunkRemaining = chunk.OutputSize - chunkOffset;
                int toRead = (int)Math.Min(remaining, chunkRemaining);

                int bytesRead = ReadFromChunk(chunk, chunkOffset, buffer, offset + totalRead, toRead);

                totalRead += bytesRead;
                _position += bytesRead;
                remaining -= bytesRead;

                if (bytesRead == 0)
                    break;
            }

            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPos = offset;
                    break;
                case SeekOrigin.Current:
                    newPos = _position + offset;
                    break;
                case SeekOrigin.End:
                    newPos = _expandedLength + offset;
                    break;
                default:
                    throw new ArgumentException("Invalid seek origin");
            }

            if (newPos < 0)
                throw new IOException("Seek position is negative");

            _position = newPos;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region Sparse Parsing

        private bool ParseHeader()
        {
            try
            {
                _baseStream.Seek(0, SeekOrigin.Begin);
                using (var reader = new BinaryReader(_baseStream, Encoding.UTF8, true))
                {
                    uint magic = reader.ReadUInt32();
                    if (magic != SPARSE_HEADER_MAGIC)
                    {
                        _log(string.Format("[Sparse] 不是 Sparse 镜像 (magic: 0x{0:X8})", magic));
                        return false;
                    }

                    _majorVersion = reader.ReadUInt16();
                    _minorVersion = reader.ReadUInt16();
                    _fileHeaderSize = reader.ReadUInt16();
                    _chunkHeaderSize = reader.ReadUInt16();
                    _blockSize = reader.ReadUInt32();
                    _totalBlocks = reader.ReadUInt32();
                    _totalChunks = reader.ReadUInt32();
                    uint checksum = reader.ReadUInt32();

                    _expandedLength = (long)_totalBlocks * _blockSize;

                    // Sparse 详细信息已由调用者（FirehoseClient）处理，这里不重复输出

                    if (_fileHeaderSize > SPARSE_HEADER_SIZE)
                    {
                        _baseStream.Seek(_fileHeaderSize - SPARSE_HEADER_SIZE, SeekOrigin.Current);
                    }

                    BuildChunkIndex();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Sparse] 解析失败: {0}", ex.Message));
                return false;
            }
        }

        private void BuildChunkIndex()
        {
            _chunkIndex = new List<ChunkInfo>();

            _baseStream.Seek(_fileHeaderSize, SeekOrigin.Begin);
            using (var reader = new BinaryReader(_baseStream, Encoding.UTF8, true))
            {
                long currentOutputOffset = 0;

                for (uint i = 0; i < _totalChunks; i++)
                {
                    try
                    {
                        ushort chunkType = reader.ReadUInt16();
                        ushort reserved = reader.ReadUInt16();
                        uint chunkBlocks = reader.ReadUInt32();
                        uint totalSize = reader.ReadUInt32();

                        uint dataSize = totalSize - (uint)_chunkHeaderSize;
                        long dataOffset = _baseStream.Position;
                        long outputSize = (long)chunkBlocks * _blockSize;

                        var chunk = new ChunkInfo
                        {
                            Index = i,
                            Type = chunkType,
                            OutputOffset = currentOutputOffset,
                            OutputSize = outputSize,
                            DataOffset = dataOffset,
                            DataSize = dataSize,
                            ChunkBlocks = chunkBlocks
                        };

                        if (chunkType != CHUNK_TYPE_CRC32)
                        {
                            _chunkIndex.Add(chunk);
                            currentOutputOffset += outputSize;
                        }

                        if (dataSize > 0)
                        {
                            _baseStream.Seek(dataSize, SeekOrigin.Current);
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            // 索引完成，不输出日志
        }

        private ChunkInfo FindChunk(long position)
        {
            if (_chunkIndex == null || _chunkIndex.Count == 0)
                return null;

            int left = 0, right = _chunkIndex.Count - 1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                var chunk = _chunkIndex[mid];

                if (position < chunk.OutputOffset)
                {
                    right = mid - 1;
                }
                else if (position >= chunk.OutputOffset + chunk.OutputSize)
                {
                    left = mid + 1;
                }
                else
                {
                    return chunk;
                }
            }

            return null;
        }

        private int ReadFromChunk(ChunkInfo chunk, long chunkOffset, byte[] buffer, int bufferOffset, int count)
        {
            switch (chunk.Type)
            {
                case CHUNK_TYPE_RAW:
                    _baseStream.Seek(chunk.DataOffset + chunkOffset, SeekOrigin.Begin);
                    return _baseStream.Read(buffer, bufferOffset, count);

                case CHUNK_TYPE_FILL:
                    _baseStream.Seek(chunk.DataOffset, SeekOrigin.Begin);
                    byte[] fillValue = new byte[4];
                    _baseStream.Read(fillValue, 0, 4);

                    int fillOffset = (int)(chunkOffset % 4);
                    for (int i = 0; i < count; i++)
                    {
                        buffer[bufferOffset + i] = fillValue[(fillOffset + i) % 4];
                    }
                    return count;

                case CHUNK_TYPE_DONT_CARE:
                    Array.Clear(buffer, bufferOffset, count);
                    return count;

                default:
                    Array.Clear(buffer, bufferOffset, count);
                    return count;
            }
        }

        #endregion

        #region Conversion Methods

        /// <summary>
        /// 将 Sparse 镜像转换为 Raw 镜像文件
        /// </summary>
        public bool ConvertToRaw(string outputPath, IProgress<double> progress = null)
        {
            if (!_isValid)
                return false;

            try
            {
                _position = 0;
                long totalWritten = 0;

                using (var outputStream = File.Create(outputPath))
                {
                    byte[] buffer = new byte[_blockSize * 64];

                    while (_position < _expandedLength)
                    {
                        int bytesRead = Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                            break;

                        outputStream.Write(buffer, 0, bytesRead);
                        totalWritten += bytesRead;

                        if (progress != null)
                            progress.Report((double)totalWritten / _expandedLength);
                    }
                }

                _log(string.Format("[Sparse] 转换完成: {0:F1} MB", totalWritten / (1024.0 * 1024.0)));
                return true;
            }
            catch (Exception ex)
            {
                _log(string.Format("[Sparse] 转换失败: {0}", ex.Message));
                return false;
            }
        }

        // 最大内存转换限制 (256 MB)
        private const long MAX_TO_RAW_BYTES_SIZE = 256L * 1024 * 1024;

        /// <summary>
        /// 将 Sparse 镜像转换为内存中的 Raw 数据
        /// 警告: 仅适用于小型镜像 (最大 256 MB)
        /// </summary>
        /// <param name="maxSize">可选的最大大小限制 (默认 256 MB)</param>
        /// <returns>Raw 数据，超过限制或失败返回 null</returns>
        public byte[] ToRawBytes(long maxSize = MAX_TO_RAW_BYTES_SIZE)
        {
            if (!_isValid)
                return null;

            // 安全检查: 防止内存溢出
            if (_expandedLength > maxSize || _expandedLength > int.MaxValue)
            {
                if (_log != null)
                    _log(string.Format("[Sparse] ToRawBytes 拒绝: 大小 {0:F1} MB 超过限制 {1:F1} MB",
                        _expandedLength / (1024.0 * 1024.0), maxSize / (1024.0 * 1024.0)));
                return null;
            }

            try
            {
                byte[] result = new byte[_expandedLength];
                _position = 0;

                int offset = 0;
                while (offset < result.Length)
                {
                    int bytesRead = Read(result, offset, result.Length - offset);
                    if (bytesRead == 0)
                        break;
                    offset += bytesRead;
                }

                return result;
            }
            catch (OutOfMemoryException)
            {
                if (_log != null)
                    _log("[Sparse] ToRawBytes 内存不足");
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && !_leaveOpen)
                {
                    if (_baseStream != null)
                        _baseStream.Dispose();
                }
                if (_chunkIndex != null)
                    _chunkIndex.Clear();
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Chunk 信息
        /// </summary>
        private class ChunkInfo
        {
            public uint Index { get; set; }
            public ushort Type { get; set; }
            public long OutputOffset { get; set; }
            public long OutputSize { get; set; }
            public long DataOffset { get; set; }
            public uint DataSize { get; set; }
            public uint ChunkBlocks { get; set; }
        }
    }
}
