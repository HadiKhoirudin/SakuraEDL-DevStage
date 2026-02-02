// ============================================================================
// LoveAlways - LZ4 解码器
// 纯 C# 实现，高效解压 EROFS 压缩数据
// ============================================================================

using System;
using System.Collections.Generic;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// 纯 C# LZ4 解码器
    /// 支持 LZ4 块格式解压 (用于 EROFS FLAT_COMPR 布局)
    /// </summary>
    public static class Lz4Decoder
    {
        private const int MIN_MATCH = 4;
        private const int ML_BITS = 4;
        private const int ML_MASK = (1 << ML_BITS) - 1;
        private const int RUN_BITS = 8 - ML_BITS;
        private const int RUN_MASK = (1 << RUN_BITS) - 1;

        /// <summary>
        /// 解压 LZ4 块数据
        /// </summary>
        /// <param name="source">压缩数据</param>
        /// <param name="destSize">预期解压后大小</param>
        /// <returns>解压后的数据</returns>
        public static byte[] Decompress(byte[] source, int destSize)
        {
            if (source == null || source.Length == 0)
                return null;

            try
            {
                return DecompressBlock(source, 0, source.Length, destSize);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解压 LZ4 块数据 (带偏移)
        /// </summary>
        public static byte[] Decompress(byte[] source, int srcOffset, int srcLength, int destSize)
        {
            if (source == null || srcOffset < 0 || srcLength <= 0 || srcOffset + srcLength > source.Length)
                return null;

            try
            {
                return DecompressBlock(source, srcOffset, srcLength, destSize);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 核心解压算法
        /// </summary>
        private static byte[] DecompressBlock(byte[] src, int srcOff, int srcLen, int destSize)
        {
            byte[] dest = new byte[destSize];
            int srcEnd = srcOff + srcLen;
            int srcPos = srcOff;
            int destPos = 0;

            while (srcPos < srcEnd && destPos < destSize)
            {
                // 读取 token
                byte token = src[srcPos++];

                // 解析 literal 长度 (高 4 位)
                int literalLen = (token >> ML_BITS) & RUN_MASK;
                if (literalLen == RUN_MASK)
                {
                    int s;
                    do
                    {
                        if (srcPos >= srcEnd)
                            break;
                        s = src[srcPos++];
                        literalLen += s;
                    } while (s == 255 && srcPos < srcEnd);
                }

                // 复制 literals
                if (literalLen > 0)
                {
                    if (srcPos + literalLen > srcEnd || destPos + literalLen > destSize)
                    {
                        // 复制尽可能多的数据
                        int copyLen = Math.Min(srcEnd - srcPos, Math.Min(literalLen, destSize - destPos));
                        if (copyLen > 0)
                        {
                            Array.Copy(src, srcPos, dest, destPos, copyLen);
                            srcPos += copyLen;
                            destPos += copyLen;
                        }
                        break;
                    }
                    Array.Copy(src, srcPos, dest, destPos, literalLen);
                    srcPos += literalLen;
                    destPos += literalLen;
                }

                // 检查是否到达末尾
                if (srcPos >= srcEnd)
                    break;

                // 读取 match offset (2 字节小端)
                if (srcPos + 2 > srcEnd)
                    break;

                int offset = src[srcPos] | (src[srcPos + 1] << 8);
                srcPos += 2;

                if (offset == 0)
                    break; // 无效偏移

                // 解析 match 长度 (低 4 位)
                int matchLen = (token & ML_MASK) + MIN_MATCH;
                if ((token & ML_MASK) == ML_MASK)
                {
                    int s;
                    do
                    {
                        if (srcPos >= srcEnd)
                            break;
                        s = src[srcPos++];
                        matchLen += s;
                    } while (s == 255 && srcPos < srcEnd);
                }

                // 执行 match 复制
                int matchPos = destPos - offset;
                if (matchPos < 0)
                    break; // 无效引用

                // 逐字节复制 (处理重叠情况)
                for (int i = 0; i < matchLen && destPos < destSize; i++)
                {
                    dest[destPos++] = dest[matchPos++];
                }
            }

            // 返回实际解压的数据
            if (destPos < destSize)
            {
                byte[] result = new byte[destPos];
                Array.Copy(dest, 0, result, 0, destPos);
                return result;
            }

            return dest;
        }

        /// <summary>
        /// 尝试解压并返回是否成功
        /// </summary>
        public static bool TryDecompress(byte[] source, int destSize, out byte[] result)
        {
            result = Decompress(source, destSize);
            return result != null && result.Length > 0;
        }

        /// <summary>
        /// 解压 EROFS 压缩块
        /// EROFS 使用特定的 LZ4 封装格式
        /// </summary>
        public static byte[] DecompressErofsBlock(byte[] compressedData, int uncompressedSize)
        {
            if (compressedData == null || compressedData.Length == 0)
                return null;

            // EROFS 可能使用原始 LZ4 块或带头的格式
            // 先尝试直接解压
            byte[] result = Decompress(compressedData, uncompressedSize);
            if (result != null && result.Length > 0)
                return result;

            // 尝试跳过可能的头部 (EROFS 压缩头)
            if (compressedData.Length > 4)
            {
                // 检查是否有 LZ4 Frame 魔数 (0x184D2204)
                uint magic = BitConverter.ToUInt32(compressedData, 0);
                if (magic == 0x184D2204)
                {
                    // LZ4 Frame 格式，需要解析帧头
                    return DecompressLz4Frame(compressedData);
                }
            }

            return null;
        }

        /// <summary>
        /// 解压 LZ4 Frame 格式 (带帧头)
        /// </summary>
        private static byte[] DecompressLz4Frame(byte[] data)
        {
            if (data == null || data.Length < 7)
                return null;

            try
            {
                int pos = 4; // 跳过魔数

                // 读取 FLG 字节
                byte flg = data[pos++];
                bool hasContentSize = (flg & 0x08) != 0;
                bool hasBlockChecksum = (flg & 0x10) != 0;
                bool hasContentChecksum = (flg & 0x04) != 0;

                // 读取 BD 字节
                byte bd = data[pos++];
                int blockMaxSize = GetBlockMaxSize((bd >> 4) & 0x07);

                // 跳过可选的 content size
                if (hasContentSize)
                    pos += 8;

                // 跳过 HC (header checksum)
                pos++;

                var output = new List<byte>();

                // 读取数据块
                while (pos + 4 <= data.Length)
                {
                    uint blockSize = BitConverter.ToUInt32(data, pos);
                    pos += 4;

                    if (blockSize == 0) // 结束标记
                        break;

                    bool isCompressed = (blockSize & 0x80000000) == 0;
                    int actualSize = (int)(blockSize & 0x7FFFFFFF);

                    if (pos + actualSize > data.Length)
                        break;

                    if (isCompressed)
                    {
                        byte[] decompressed = Decompress(data, pos, actualSize, blockMaxSize);
                        if (decompressed != null)
                            output.AddRange(decompressed);
                    }
                    else
                    {
                        // 未压缩块
                        for (int i = 0; i < actualSize; i++)
                            output.Add(data[pos + i]);
                    }

                    pos += actualSize;

                    // 跳过可选的 block checksum
                    if (hasBlockChecksum)
                        pos += 4;
                }

                return output.Count > 0 ? output.ToArray() : null;
            }
            catch
            {
                return null;
            }
        }

        private static int GetBlockMaxSize(int blockSizeId)
        {
            switch (blockSizeId)
            {
                case 4: return 64 * 1024;      // 64KB
                case 5: return 256 * 1024;     // 256KB
                case 6: return 1024 * 1024;    // 1MB
                case 7: return 4 * 1024 * 1024; // 4MB
                default: return 64 * 1024;
            }
        }
    }
}
