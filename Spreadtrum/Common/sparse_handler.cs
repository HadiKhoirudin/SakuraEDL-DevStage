// LoveAlways - Sparse Image Handler
// Android Sparse Image Handler for Spreadtrum
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.IO;

namespace LoveAlways.Spreadtrum.Common
{
    /// Sparse Image Handler
    /// Used to decompress Android Sparse format image files
    /// </summary>
    public class SparseHandler
    {
        // Sparse Magic: 0xED26FF3A
        public const uint SPARSE_MAGIC = 0xED26FF3A;

        // Chunk types
        public const ushort CHUNK_TYPE_RAW = 0xCAC1;
        public const ushort CHUNK_TYPE_FILL = 0xCAC2;
        public const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        public const ushort CHUNK_TYPE_CRC32 = 0xCAC4;

        private readonly Action<string> _log;

        public SparseHandler(Action<string> log = null)
        {
            _log = log;
        }

        /// Check if it's a Sparse Image
        /// </summary>
        public static bool IsSparseImage(byte[] header)
        {
            if (header == null || header.Length < 4)
                return false;

            uint magic = BitConverter.ToUInt32(header, 0);
            return magic == SPARSE_MAGIC;
        }

        /// Check if file is a Sparse Image
        /// </summary>
        public static bool IsSparseImage(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] header = new byte[4];
                    fs.Read(header, 0, 4);
                    return IsSparseImage(header);
                }
            }
            catch
            {
                return false;
            }
        }

        /// Decompress Sparse Image to Raw Image
        /// </summary>
        public void Decompress(string sparseFile, string outputFile, Action<long, long> progress = null)
        {
            using (var input = new FileStream(sparseFile, FileMode.Open, FileAccess.Read))
            using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            {
                Decompress(input, output, progress);
            }
        }

        /// Decompress Sparse Image stream
        /// </summary>
        public void Decompress(Stream input, Stream output, Action<long, long> progress = null)
        {
            using (var reader = new BinaryReader(input))
            {
                // Read Sparse Header
                var header = ReadSparseHeader(reader);

                Log("[Sparse] Version: {0}.{1}", header.MajorVersion, header.MinorVersion);
                Log("[Sparse] Block Size: {0}", header.BlockSize);
                Log("[Sparse] Total Blocks: {0}", header.TotalBlocks);
                Log("[Sparse] Total Chunk Count: {0}", header.TotalChunks);

                long totalSize = (long)header.TotalBlocks * header.BlockSize;
                long written = 0;

                // Process each Chunk
                for (int i = 0; i < header.TotalChunks; i++)
                {
                    var chunk = ReadChunkHeader(reader);

                    long chunkDataSize = (long)chunk.ChunkSize * header.BlockSize;

                    switch (chunk.ChunkType)
                    {
                        case CHUNK_TYPE_RAW:
                            // Raw data, copy directly
                            CopyData(reader, output, chunkDataSize);
                            written += chunkDataSize;
                            break;

                        case CHUNK_TYPE_FILL:
                            // Fill data
                            uint fillValue = reader.ReadUInt32();
                            WriteFillData(output, fillValue, chunkDataSize);
                            written += chunkDataSize;
                            break;

                        case CHUNK_TYPE_DONT_CARE:
                            // Don't care area, write zeros
                            WriteZeros(output, chunkDataSize);
                            written += chunkDataSize;
                            break;

                        case CHUNK_TYPE_CRC32:
                            // CRC32 checksum, skip
                            reader.ReadUInt32();
                            break;

                        default:
                            Log("[Sparse] Unknown Chunk Type: 0x{0:X4}", chunk.ChunkType);
                            break;
                    }

                    progress?.Invoke(written, totalSize);
                }

                Log("[Sparse] Decompression complete, output size: {0} bytes", written);
            }
        }

        /// Compress Raw Image to Sparse Image
        /// </summary>
        public void Compress(string rawFile, string outputFile, uint blockSize = 4096, Action<long, long> progress = null)
        {
            using (var input = new FileStream(rawFile, FileMode.Open, FileAccess.Read))
            using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            {
                Compress(input, output, blockSize, progress);
            }
        }

        /// Compress Raw Image stream
        /// </summary>
        public void Compress(Stream input, Stream output, uint blockSize = 4096, Action<long, long> progress = null)
        {
            long inputLength = input.Length;
            uint totalBlocks = (uint)((inputLength + blockSize - 1) / blockSize);

            // Temporarily store Chunks
            using (var chunkStream = new MemoryStream())
            using (var chunkWriter = new BinaryWriter(chunkStream))
            using (var reader = new BinaryReader(input))
            using (var writer = new BinaryWriter(output))
            {
                uint chunkCount = 0;
                byte[] blockBuffer = new byte[blockSize];
                byte[] zeroBlock = new byte[blockSize];
                long processed = 0;

                while (input.Position < inputLength)
                {
                    int read = reader.Read(blockBuffer, 0, (int)blockSize);
                    if (read == 0)
                        break;

                    // Check if all zeros
                    bool isZero = IsZeroBlock(blockBuffer, read);

                    // Check if fill block
                    uint fillValue;
                    bool isFill = IsFillBlock(blockBuffer, read, out fillValue);

                    if (isZero)
                    {
                        // DONT_CARE Chunk
                        WriteChunkHeader(chunkWriter, CHUNK_TYPE_DONT_CARE, 1, 0);
                        chunkCount++;
                    }
                    else if (isFill)
                    {
                        // FILL Chunk
                        WriteChunkHeader(chunkWriter, CHUNK_TYPE_FILL, 1, 4);
                        chunkWriter.Write(fillValue);
                        chunkCount++;
                    }
                    else
                    {
                        // RAW Chunk
                        WriteChunkHeader(chunkWriter, CHUNK_TYPE_RAW, 1, (uint)read);
                        chunkWriter.Write(blockBuffer, 0, read);
                        chunkCount++;
                    }

                    processed += read;
                    progress?.Invoke(processed, inputLength);
                }

                // Write Sparse Header
                WriteSparseHeader(writer, totalBlocks, chunkCount, blockSize);

                // Write Chunks
                chunkStream.Position = 0;
                chunkStream.CopyTo(output);

                Log("[Sparse] Compression complete, {0} blocks -> {1} Chunks", totalBlocks, chunkCount);
            }
        }

        #region Internal Methods

        private SparseHeader ReadSparseHeader(BinaryReader reader)
        {
            var header = new SparseHeader();

            header.Magic = reader.ReadUInt32();
            if (header.Magic != SPARSE_MAGIC)
                throw new InvalidDataException("Not a valid Sparse Image");

            header.MajorVersion = reader.ReadUInt16();
            header.MinorVersion = reader.ReadUInt16();
            header.FileHeaderSize = reader.ReadUInt16();
            header.ChunkHeaderSize = reader.ReadUInt16();
            header.BlockSize = reader.ReadUInt32();
            header.TotalBlocks = reader.ReadUInt32();
            header.TotalChunks = reader.ReadUInt32();
            header.ImageChecksum = reader.ReadUInt32();

            return header;
        }

        private ChunkHeader ReadChunkHeader(BinaryReader reader)
        {
            var chunk = new ChunkHeader();

            chunk.ChunkType = reader.ReadUInt16();
            chunk.Reserved = reader.ReadUInt16();
            chunk.ChunkSize = reader.ReadUInt32();
            chunk.TotalSize = reader.ReadUInt32();

            return chunk;
        }

        private void WriteSparseHeader(BinaryWriter writer, uint totalBlocks, uint totalChunks, uint blockSize)
        {
            writer.Write(SPARSE_MAGIC);
            writer.Write((ushort)1);        // Major version
            writer.Write((ushort)0);        // Minor version
            writer.Write((ushort)28);       // File header size
            writer.Write((ushort)12);       // Chunk header size
            writer.Write(blockSize);
            writer.Write(totalBlocks);
            writer.Write(totalChunks);
            writer.Write((uint)0);          // Image checksum
        }

        private void WriteChunkHeader(BinaryWriter writer, ushort chunkType, uint chunkSize, uint dataSize)
        {
            writer.Write(chunkType);
            writer.Write((ushort)0);        // Reserved
            writer.Write(chunkSize);
            writer.Write(12 + dataSize);    // Total size = header + data
        }

        private void CopyData(BinaryReader reader, Stream output, long size)
        {
            byte[] buffer = new byte[65536];
            long remaining = size;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = reader.Read(buffer, 0, toRead);
                if (read == 0)
                    break;

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private void WriteFillData(Stream output, uint value, long size)
        {
            byte[] fillBytes = BitConverter.GetBytes(value);
            byte[] buffer = new byte[4096];

            // Fill buffer
            for (int i = 0; i < buffer.Length; i += 4)
            {
                Array.Copy(fillBytes, 0, buffer, i, 4);
            }

            long remaining = size;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(buffer.Length, remaining);
                output.Write(buffer, 0, toWrite);
                remaining -= toWrite;
            }
        }

        private void WriteZeros(Stream output, long size)
        {
            byte[] zeros = new byte[65536];
            long remaining = size;

            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(zeros.Length, remaining);
                output.Write(zeros, 0, toWrite);
                remaining -= toWrite;
            }
        }

        private bool IsZeroBlock(byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (data[i] != 0)
                    return false;
            }
            return true;
        }

        private bool IsFillBlock(byte[] data, int length, out uint fillValue)
        {
            fillValue = 0;

            if (length < 4)
                return false;

            fillValue = BitConverter.ToUInt32(data, 0);

            for (int i = 4; i < length; i += 4)
            {
                if (i + 4 > length)
                    break;

                uint value = BitConverter.ToUInt32(data, i);
                if (value != fillValue)
                    return false;
            }

            return true;
        }

        private void Log(string format, params object[] args)
        {
            _log?.Invoke(string.Format(format, args));
        }

        #endregion
    }

    #region Data Structures

    /// <summary>
    /// Sparse Header
    /// </summary>
    internal class SparseHeader
    {
        public uint Magic { get; set; }
        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }
        public ushort FileHeaderSize { get; set; }
        public ushort ChunkHeaderSize { get; set; }
        public uint BlockSize { get; set; }
        public uint TotalBlocks { get; set; }
        public uint TotalChunks { get; set; }
        public uint ImageChecksum { get; set; }
    }

    /// <summary>
    /// Chunk Header
    /// </summary>
    internal class ChunkHeader
    {
        public ushort ChunkType { get; set; }
        public ushort Reserved { get; set; }
        public uint ChunkSize { get; set; }
        public uint TotalSize { get; set; }
    }

    #endregion
}
