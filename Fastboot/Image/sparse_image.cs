
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LoveAlways.Fastboot.Image
{
    /// <summary>
    /// Android Sparse Image Parser
    /// Based on AOSP libsparse implementation
    /// 
    /// Sparse Image Format:
    /// - Header (28 bytes)
    /// - Chunk[] 
    ///   - Chunk Header (12 bytes)
    ///   - Chunk Data (variable)
    /// </summary>
    public class SparseImage : IDisposable
    {
        // Sparse magic number
        public const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;
        
        // Chunk types
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
        /// Whether it's a Sparse image
        /// </summary>
        public bool IsSparse => _isSparse;
        
        /// <summary>
        /// Original file size (after decompression)
        /// </summary>
        public long OriginalSize => _isSparse ? (long)_header.TotalBlocks * _header.BlockSize : _stream.Length;
        
        /// <summary>
        /// Sparse file size
        /// </summary>
        public long SparseSize => _stream.Length;
        
        /// <summary>
        /// Block size
        /// </summary>
        public uint BlockSize => _isSparse ? _header.BlockSize : 4096;
        
        /// <summary>
        /// Total blocks
        /// </summary>
        public uint TotalBlocks => _isSparse ? _header.TotalBlocks : (uint)((_stream.Length + BlockSize - 1) / BlockSize);
        
        /// <summary>
        /// Number of chunks
        /// </summary>
        public int ChunkCount => _chunks?.Count ?? 0;
        
        /// <summary>
        /// Sparse Header
        /// </summary>
        public SparseHeader Header => _header;
        
        /// <summary>
        /// All Chunks
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
            
            // Read magic number
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
            
            // Read full header
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
            
            // Skip additional header data
            if (_header.FileHeaderSize > 28)
            {
                _stream.Position = _header.FileHeaderSize;
            }
            
            // Parse all chunks
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
                
                // Calculate data size
                uint dataSize = chunk.TotalSize - _header.ChunkHeaderSize;
                chunk.DataSize = dataSize;
                
                // Skip data part
                _stream.Position += dataSize;
                
                _chunks.Add(chunk);
            }
        }
        
        /// <summary>
        /// Convert Sparse image to raw data stream
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
        /// Split into multiple Sparse chunks for transfer
        /// Sparse image will be resparsed into multiple independent Sparse files
        /// </summary>
        /// <param name="maxSize">Maximum size for each chunk</param>
        public IEnumerable<SparseChunkData> SplitForTransfer(long maxSize)
        {
            if (!_isSparse)
            {
                // Non-Sparse image, chunk directly                _stream.Position = 0;
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
                // Sparse image: If smaller than maxSize, send the entire file directly
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
                    // Sparse image is too large, needs resparse
                    // Group chunks, each group generates an independent Sparse file
                    foreach (var sparseChunk in ResparseSplitTransfer(maxSize))
                    {
                        yield return sparseChunk;
                    }
                }
            }
        }
        
        /// <summary>
        /// Resparse: Split large Sparse image into multiple small Sparse images
        /// Optimize memory usage: Allocate only necessary memory each time
        /// </summary>
        private IEnumerable<SparseChunkData> ResparseSplitTransfer(long maxSize)
        {
            // Calculate how much data each slice can hold (reserve header space)
            int headerSize = _header.FileHeaderSize;
            int chunkHeaderSize = _header.ChunkHeaderSize;
            
            // Group chunks - calculate group info first to avoid saving large amounts of data
            var groups = new List<List<int>>();
            var currentGroup = new List<int>();
            long currentGroupSize = headerSize;
            
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                long chunkTotalSize = chunk.TotalSize;
                
                // If a single chunk exceeds maxSize, it needs to be handled separately
                if (chunkTotalSize + headerSize > maxSize && currentGroup.Count == 0)
                {
                    // Single chunk too large, separate into a group alone                    currentGroup.Add(i);
                    groups.Add(currentGroup);
                    currentGroup = new List<int>();
                    currentGroupSize = headerSize;
                    continue;
                }
                
                if (currentGroup.Count > 0 && currentGroupSize + chunkTotalSize > maxSize)
                {
                    // Current group is full, start a new group                    groups.Add(currentGroup);
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
            
            // Generate independent Sparse files for each group
            int totalGroups = groups.Count;
            
            for (int groupIndex = 0; groupIndex < totalGroups; groupIndex++)
            {
                var group = groups[groupIndex];
                
                // Calculate total size of this group
                long groupDataSize = headerSize;
                uint groupTotalBlocks = 0;
                foreach (int idx in group)
                {
                    groupDataSize += _chunks[idx].TotalSize;
                    groupTotalBlocks += _chunks[idx].ChunkBlocks;
                }
                
                // Pre-allocate buffer
                byte[] sparseData = new byte[groupDataSize];
                int writeOffset = 0;
                
                // Write Sparse header (28 bytes)
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
                
                // Read each chunk data directly to the pre-allocated buffer
                foreach (int idx in group)
                {
                    var chunk = _chunks[idx];
                    
                    // Locate trunk data (including header)                    _stream.Position = chunk.DataOffset - chunkHeaderSize;
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
                
                // Explicitly release reference to help GC
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
    /// Chunk data for transfer
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
    /// Sparse to Raw stream converter
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
            // Simplified implementation: traverse chunks to find data at corresponding position
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
