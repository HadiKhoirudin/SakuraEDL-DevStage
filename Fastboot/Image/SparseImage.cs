using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LoveAlways.Fastboot.Image
{
    /// <summary>
    /// Android Sparse 镜像解析器
    /// 基于 AOSP libsparse 实现
    /// 
    /// Sparse 镜像格式：
    /// - Header (28 bytes)
    /// - Chunk[] 
    ///   - Chunk Header (12 bytes)
    ///   - Chunk Data (variable)
    /// </summary>
    public class SparseImage : IDisposable
    {
        // Sparse 魔数
        public const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;
        
        // Chunk 类型
        public const ushort CHUNK_TYPE_RAW = 0xCAC1;
        public const ushort CHUNK_TYPE_FILL = 0xCAC2;
        public const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        public const ushort CHUNK_TYPE_CRC32 = 0xCAC4;
        
        private Stream _stream;
        private SparseHeader _header;
        private List<SparseChunk> _chunks;
        private bool _isSparse;
        private bool _disposed;
        
        /// <summary>
        /// 是否是 Sparse 镜像
        /// </summary>
        public bool IsSparse => _isSparse;
        
        /// <summary>
        /// 原始文件大小（解压后）
        /// </summary>
        public long OriginalSize => _isSparse ? (long)_header.TotalBlocks * _header.BlockSize : _stream.Length;
        
        /// <summary>
        /// Sparse 文件大小
        /// </summary>
        public long SparseSize => _stream.Length;
        
        /// <summary>
        /// 块大小
        /// </summary>
        public uint BlockSize => _isSparse ? _header.BlockSize : 4096;
        
        /// <summary>
        /// 总块数
        /// </summary>
        public uint TotalBlocks => _isSparse ? _header.TotalBlocks : (uint)((_stream.Length + BlockSize - 1) / BlockSize);
        
        /// <summary>
        /// Chunk 数量
        /// </summary>
        public int ChunkCount => _chunks?.Count ?? 0;
        
        /// <summary>
        /// Sparse Header
        /// </summary>
        public SparseHeader Header => _header;
        
        /// <summary>
        /// 所有 Chunks
        /// </summary>
        public IReadOnlyList<SparseChunk> Chunks => _chunks;
        
        public SparseImage(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _chunks = new List<SparseChunk>();
            
            ParseHeader();
        }
        
        public SparseImage(string filePath)
            : this(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
        }
        
        private void ParseHeader()
        {
            _stream.Position = 0;
            
            // 读取魔数
            byte[] magicBytes = new byte[4];
            if (_stream.Read(magicBytes, 0, 4) != 4)
            {
                _isSparse = false;
                return;
            }
            
            uint magic = BitConverter.ToUInt32(magicBytes, 0);
            if (magic != SPARSE_HEADER_MAGIC)
            {
                _isSparse = false;
                return;
            }
            
            _isSparse = true;
            _stream.Position = 0;
            
            // 读取完整 header
            byte[] headerBytes = new byte[28];
            _stream.Read(headerBytes, 0, 28);
            
            _header = new SparseHeader
            {
                Magic = BitConverter.ToUInt32(headerBytes, 0),
                MajorVersion = BitConverter.ToUInt16(headerBytes, 4),
                MinorVersion = BitConverter.ToUInt16(headerBytes, 6),
                FileHeaderSize = BitConverter.ToUInt16(headerBytes, 8),
                ChunkHeaderSize = BitConverter.ToUInt16(headerBytes, 10),
                BlockSize = BitConverter.ToUInt32(headerBytes, 12),
                TotalBlocks = BitConverter.ToUInt32(headerBytes, 16),
                TotalChunks = BitConverter.ToUInt32(headerBytes, 20),
                ImageChecksum = BitConverter.ToUInt32(headerBytes, 24)
            };
            
            // 跳过额外的 header 数据
            if (_header.FileHeaderSize > 28)
            {
                _stream.Position = _header.FileHeaderSize;
            }
            
            // 解析所有 chunks
            ParseChunks();
        }
        
        private void ParseChunks()
        {
            _chunks.Clear();
            
            for (uint i = 0; i < _header.TotalChunks; i++)
            {
                byte[] chunkHeader = new byte[12];
                if (_stream.Read(chunkHeader, 0, 12) != 12)
                    break;
                
                var chunk = new SparseChunk
                {
                    Type = BitConverter.ToUInt16(chunkHeader, 0),
                    Reserved = BitConverter.ToUInt16(chunkHeader, 2),
                    ChunkBlocks = BitConverter.ToUInt32(chunkHeader, 4),
                    TotalSize = BitConverter.ToUInt32(chunkHeader, 8),
                    DataOffset = _stream.Position
                };
                
                // 计算数据大小
                uint dataSize = chunk.TotalSize - _header.ChunkHeaderSize;
                chunk.DataSize = dataSize;
                
                // 跳过数据部分
                _stream.Position += dataSize;
                
                _chunks.Add(chunk);
            }
        }
        
        /// <summary>
        /// 将 Sparse 镜像转换为原始数据流
        /// </summary>
        public Stream ToRawStream()
        {
            if (!_isSparse)
            {
                _stream.Position = 0;
                return _stream;
            }
            
            return new SparseToRawStream(this, _stream);
        }
        
        /// <summary>
        /// 分割为多个 Sparse 块用于传输
        /// Sparse 镜像会被 resparse 成多个独立的 Sparse 文件
        /// </summary>
        /// <param name="maxSize">每块最大大小</param>
        public IEnumerable<SparseChunkData> SplitForTransfer(long maxSize)
        {
            if (!_isSparse)
            {
                // 非 Sparse 镜像，直接分块
                _stream.Position = 0;
                long remaining = _stream.Length;
                int chunkIndex = 0;
                
                while (remaining > 0)
                {
                    int chunkSize = (int)Math.Min(remaining, maxSize);
                    byte[] data = new byte[chunkSize];
                    _stream.Read(data, 0, chunkSize);
                    
                    yield return new SparseChunkData
                    {
                        Index = chunkIndex++,
                        TotalChunks = (int)((_stream.Length + maxSize - 1) / maxSize),
                        Data = data,
                        Size = chunkSize
                    };
                    
                    remaining -= chunkSize;
                }
            }
            else
            {
                // Sparse 镜像：如果小于 maxSize，直接发送整个文件
                if (_stream.Length <= maxSize)
                {
                    _stream.Position = 0;
                    byte[] data = new byte[_stream.Length];
                    _stream.Read(data, 0, data.Length);
                    
                    yield return new SparseChunkData
                    {
                        Index = 0,
                        TotalChunks = 1,
                        Data = data,
                        Size = data.Length
                    };
                }
                else
                {
                    // Sparse 镜像太大，需要 resparse
                    // 将 chunks 分组，每组生成一个独立的 Sparse 文件
                    foreach (var sparseChunk in ResparseSplitTransfer(maxSize))
                    {
                        yield return sparseChunk;
                    }
                }
            }
        }
        
        /// <summary>
        /// Resparse：将大的 Sparse 镜像分割成多个小的 Sparse 镜像
        /// 优化内存使用：每次只分配必要的内存
        /// </summary>
        private IEnumerable<SparseChunkData> ResparseSplitTransfer(long maxSize)
        {
            // 计算每个分片可以容纳多少数据（预留 header 空间）
            int headerSize = _header.FileHeaderSize;
            int chunkHeaderSize = _header.ChunkHeaderSize;
            
            // 分组 chunks - 先计算分组信息，避免保存大量数据
            var groups = new List<List<int>>();
            var currentGroup = new List<int>();
            long currentGroupSize = headerSize;
            
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                long chunkTotalSize = chunk.TotalSize;
                
                // 如果单个 chunk 超过 maxSize，需要单独处理
                if (chunkTotalSize + headerSize > maxSize && currentGroup.Count == 0)
                {
                    // 单个 chunk 太大，单独作为一组
                    currentGroup.Add(i);
                    groups.Add(currentGroup);
                    currentGroup = new List<int>();
                    currentGroupSize = headerSize;
                    continue;
                }
                
                if (currentGroup.Count > 0 && currentGroupSize + chunkTotalSize > maxSize)
                {
                    // 当前组已满，开始新组
                    groups.Add(currentGroup);
                    currentGroup = new List<int>();
                    currentGroupSize = headerSize;
                }
                
                currentGroup.Add(i);
                currentGroupSize += chunkTotalSize;
            }
            
            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }
            
            // 为每个组生成独立的 Sparse 文件
            int totalGroups = groups.Count;
            
            for (int groupIndex = 0; groupIndex < totalGroups; groupIndex++)
            {
                var group = groups[groupIndex];
                
                // 计算此组的总大小
                long groupDataSize = headerSize;
                uint groupTotalBlocks = 0;
                foreach (int idx in group)
                {
                    groupDataSize += _chunks[idx].TotalSize;
                    groupTotalBlocks += _chunks[idx].ChunkBlocks;
                }
                
                // 预分配缓冲区
                byte[] sparseData = new byte[groupDataSize];
                int writeOffset = 0;
                
                // 写入 Sparse header (28 bytes)
                Buffer.BlockCopy(BitConverter.GetBytes(SPARSE_HEADER_MAGIC), 0, sparseData, writeOffset, 4);
                writeOffset += 4;
                Buffer.BlockCopy(BitConverter.GetBytes(_header.MajorVersion), 0, sparseData, writeOffset, 2);
                writeOffset += 2;
                Buffer.BlockCopy(BitConverter.GetBytes(_header.MinorVersion), 0, sparseData, writeOffset, 2);
                writeOffset += 2;
                Buffer.BlockCopy(BitConverter.GetBytes(_header.FileHeaderSize), 0, sparseData, writeOffset, 2);
                writeOffset += 2;
                Buffer.BlockCopy(BitConverter.GetBytes(_header.ChunkHeaderSize), 0, sparseData, writeOffset, 2);
                writeOffset += 2;
                Buffer.BlockCopy(BitConverter.GetBytes(_header.BlockSize), 0, sparseData, writeOffset, 4);
                writeOffset += 4;
                Buffer.BlockCopy(BitConverter.GetBytes(groupTotalBlocks), 0, sparseData, writeOffset, 4);
                writeOffset += 4;
                Buffer.BlockCopy(BitConverter.GetBytes((uint)group.Count), 0, sparseData, writeOffset, 4);
                writeOffset += 4;
                Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, sparseData, writeOffset, 4); // checksum = 0
                writeOffset += 4;
                
                // 直接读取每个 chunk 数据到预分配的缓冲区
                foreach (int idx in group)
                {
                    var chunk = _chunks[idx];
                    
                    // 定位到 chunk 数据（包含 header）
                    _stream.Position = chunk.DataOffset - chunkHeaderSize;
                    _stream.Read(sparseData, writeOffset, (int)chunk.TotalSize);
                    writeOffset += (int)chunk.TotalSize;
                }
                
                yield return new SparseChunkData
                {
                    Index = groupIndex,
                    TotalChunks = totalGroups,
                    Data = sparseData,
                    Size = (int)groupDataSize
                };
                
                // 显式释放引用，帮助 GC
                sparseData = null;
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// Sparse Header
    /// </summary>
    public struct SparseHeader
    {
        public uint Magic;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ushort FileHeaderSize;
        public ushort ChunkHeaderSize;
        public uint BlockSize;
        public uint TotalBlocks;
        public uint TotalChunks;
        public uint ImageChecksum;
    }
    
    /// <summary>
    /// Sparse Chunk
    /// </summary>
    public class SparseChunk
    {
        public ushort Type;
        public ushort Reserved;
        public uint ChunkBlocks;
        public uint TotalSize;
        public uint DataSize;
        public long DataOffset;
        
        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case SparseImage.CHUNK_TYPE_RAW: return "RAW";
                    case SparseImage.CHUNK_TYPE_FILL: return "FILL";
                    case SparseImage.CHUNK_TYPE_DONT_CARE: return "DONT_CARE";
                    case SparseImage.CHUNK_TYPE_CRC32: return "CRC32";
                    default: return $"UNKNOWN({Type:X4})";
                }
            }
        }
    }
    
    /// <summary>
    /// 用于传输的 Chunk 数据
    /// </summary>
    public class SparseChunkData
    {
        public int Index;
        public int TotalChunks;
        public byte[] Data;
        public int Size;
        public ushort ChunkType;
        public uint ChunkBlocks;
    }
    
    /// <summary>
    /// Sparse 到 Raw 的流转换器
    /// </summary>
    internal class SparseToRawStream : Stream
    {
        private readonly SparseImage _sparse;
        private readonly Stream _source;
        private long _position;
        private readonly long _length;
        
        public SparseToRawStream(SparseImage sparse, Stream source)
        {
            _sparse = sparse;
            _source = source;
            _length = sparse.OriginalSize;
        }
        
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => _position = value;
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            // 简化实现：遍历 chunks 找到对应位置的数据
            int totalRead = 0;
            long currentBlockOffset = 0;
            
            foreach (var chunk in _sparse.Chunks)
            {
                long chunkStartOffset = currentBlockOffset * _sparse.BlockSize;
                long chunkEndOffset = (currentBlockOffset + chunk.ChunkBlocks) * _sparse.BlockSize;
                
                if (_position >= chunkStartOffset && _position < chunkEndOffset)
                {
                    long posInChunk = _position - chunkStartOffset;
                    int toRead = (int)Math.Min(count - totalRead, chunkEndOffset - _position);
                    
                    switch (chunk.Type)
                    {
                        case SparseImage.CHUNK_TYPE_RAW:
                            _source.Position = chunk.DataOffset + posInChunk;
                            int read = _source.Read(buffer, offset + totalRead, toRead);
                            totalRead += read;
                            _position += read;
                            break;
                            
                        case SparseImage.CHUNK_TYPE_FILL:
                            _source.Position = chunk.DataOffset;
                            byte[] fillValue = new byte[4];
                            _source.Read(fillValue, 0, 4);
                            for (int i = 0; i < toRead; i++)
                            {
                                buffer[offset + totalRead + i] = fillValue[i % 4];
                            }
                            totalRead += toRead;
                            _position += toRead;
                            break;
                            
                        case SparseImage.CHUNK_TYPE_DONT_CARE:
                            Array.Clear(buffer, offset + totalRead, toRead);
                            totalRead += toRead;
                            _position += toRead;
                            break;
                    }
                    
                    if (totalRead >= count)
                        break;
                }
                
                currentBlockOffset += chunk.ChunkBlocks;
            }
            
            return totalRead;
        }
        
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: _position = offset; break;
                case SeekOrigin.Current: _position += offset; break;
                case SeekOrigin.End: _position = _length + offset; break;
            }
            return _position;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
