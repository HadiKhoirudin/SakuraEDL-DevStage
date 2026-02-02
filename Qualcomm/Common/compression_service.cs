// ============================================================================
// LoveAlways - 统一压缩服务
// 支持 7z、ZIP、LZMA、LZ4 等多种格式
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace LoveAlways.Qualcomm.Common
{
    /// <summary>
    /// 统一压缩解压服务
    /// 支持：7z, ZIP, LZMA, LZ4, GZIP 等格式
    /// </summary>
    public class CompressionService : IDisposable
    {
        #region Fields

        private readonly Action<string> _log;
        private string _7zExePath;
        private bool _disposed;

        #endregion

        #region Constructor

        public CompressionService(Action<string> log = null)
        {
            _log = log ?? (msg => { });
            Find7zExecutable();
        }

        #endregion

        #region Archive Detection

        /// <summary>
        /// 压缩格式类型
        /// </summary>
        public enum ArchiveFormat
        {
            Unknown,
            SevenZip,   // 7z
            Zip,        // ZIP
            Rar,        // RAR
            Tar,        // TAR
            GZip,       // GZIP
            Lzma,       // LZMA
            Lz4,        // LZ4
            Xz,         // XZ
            Bz2,        // BZIP2
            Zstd        // Zstandard
        }

        /// <summary>
        /// 根据魔数检测压缩格式
        /// </summary>
        public static ArchiveFormat DetectFormat(byte[] header)
        {
            if (header == null || header.Length < 4)
                return ArchiveFormat.Unknown;

            // 7z: 37 7A BC AF 27 1C
            if (header.Length >= 6 &&
                header[0] == 0x37 && header[1] == 0x7A &&
                header[2] == 0xBC && header[3] == 0xAF &&
                header[4] == 0x27 && header[5] == 0x1C)
                return ArchiveFormat.SevenZip;

            // ZIP: 50 4B 03 04
            if (header[0] == 0x50 && header[1] == 0x4B &&
                header[2] == 0x03 && header[3] == 0x04)
                return ArchiveFormat.Zip;

            // RAR: 52 61 72 21 1A 07
            if (header.Length >= 6 &&
                header[0] == 0x52 && header[1] == 0x61 &&
                header[2] == 0x72 && header[3] == 0x21)
                return ArchiveFormat.Rar;

            // GZIP: 1F 8B
            if (header[0] == 0x1F && header[1] == 0x8B)
                return ArchiveFormat.GZip;

            // XZ: FD 37 7A 58 5A 00
            if (header.Length >= 6 &&
                header[0] == 0xFD && header[1] == 0x37 &&
                header[2] == 0x7A && header[3] == 0x58 &&
                header[4] == 0x5A && header[5] == 0x00)
                return ArchiveFormat.Xz;

            // BZ2: 42 5A 68
            if (header.Length >= 3 &&
                header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
                return ArchiveFormat.Bz2;

            // LZ4 Frame: 04 22 4D 18
            if (header[0] == 0x04 && header[1] == 0x22 &&
                header[2] == 0x4D && header[3] == 0x18)
                return ArchiveFormat.Lz4;

            // Zstd: 28 B5 2F FD
            if (header[0] == 0x28 && header[1] == 0xB5 &&
                header[2] == 0x2F && header[3] == 0xFD)
                return ArchiveFormat.Zstd;

            return ArchiveFormat.Unknown;
        }

        /// <summary>
        /// 检测文件压缩格式
        /// </summary>
        public static ArchiveFormat DetectFileFormat(string filePath)
        {
            if (!File.Exists(filePath))
                return ArchiveFormat.Unknown;

            try
            {
                byte[] header = new byte[16];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Read(header, 0, header.Length);
                }
                return DetectFormat(header);
            }
            catch (IOException)
            {
                // 文件被占用，返回未知格式
                return ArchiveFormat.Unknown;
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限访问
                return ArchiveFormat.Unknown;
            }
        }

        #endregion

        #region 7-Zip Integration

        /// <summary>
        /// 查找 7z 可执行文件
        /// </summary>
        private void Find7zExecutable()
        {
            // 1. 检查程序目录
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] possiblePaths = new[]
            {
                Path.Combine(exeDir, "7z.exe"),
                Path.Combine(exeDir, "7za.exe"),
                Path.Combine(exeDir, "7zr.exe"),
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _7zExePath = path;
                    _log($"[压缩服务] 找到 7-Zip: {path}");
                    return;
                }
            }

            // 2. 检查 PATH 环境变量
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "7z.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadLine();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        _7zExePath = output;
                        _log($"[压缩服务] 找到 7-Zip: {output}");
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // where 命令可能在某些环境下不可用，忽略此错误
            }

            _log("[压缩服务] 警告: 未找到 7-Zip，部分功能可能不可用");
        }

        /// <summary>
        /// 使用 7z 解压文件
        /// </summary>
        public bool Extract7z(string archivePath, string outputDir, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(_7zExePath))
            {
                _log("[压缩服务] 错误: 7-Zip 不可用");
                return false;
            }

            if (!File.Exists(archivePath))
            {
                _log($"[压缩服务] 错误: 文件不存在 {archivePath}");
                return false;
            }

            try
            {
                Directory.CreateDirectory(outputDir);

                var psi = new ProcessStartInfo
                {
                    FileName = _7zExePath,
                    Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y -bsp1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string lastLine = "";
                    while (!proc.StandardOutput.EndOfStream)
                    {
                        string line = proc.StandardOutput.ReadLine();
                        if (!string.IsNullOrEmpty(line))
                        {
                            lastLine = line;
                            // 解析进度 (7z 输出格式: "  5% - filename")
                            if (line.Contains("%"))
                            {
                                int percentIdx = line.IndexOf('%');
                                if (percentIdx > 0)
                                {
                                    string percentStr = line.Substring(0, percentIdx).Trim();
                                    if (int.TryParse(percentStr, out int percent))
                                    {
                                        progress?.Report(percent / 100.0);
                                    }
                                }
                            }
                        }
                    }

                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                    {
                        _log($"[压缩服务] 解压完成: {Path.GetFileName(archivePath)}");
                        progress?.Report(1.0);
                        return true;
                    }
                    else
                    {
                        string error = proc.StandardError.ReadToEnd();
                        _log($"[压缩服务] 解压失败: {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] 解压异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用 7z 列出压缩包内容
        /// </summary>
        public List<ArchiveEntry> List7zContents(string archivePath)
        {
            var entries = new List<ArchiveEntry>();

            if (string.IsNullOrEmpty(_7zExePath) || !File.Exists(archivePath))
                return entries;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _7zExePath,
                    Arguments = $"l \"{archivePath}\" -slt",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    ArchiveEntry current = null;
                    while (!proc.StandardOutput.EndOfStream)
                    {
                        string line = proc.StandardOutput.ReadLine();
                        if (line.StartsWith("Path = "))
                        {
                            if (current != null)
                                entries.Add(current);
                            current = new ArchiveEntry { Path = line.Substring(7) };
                        }
                        else if (current != null)
                        {
                            if (line.StartsWith("Size = "))
                            {
                                long size;
                                if (long.TryParse(line.Substring(7), out size))
                                    current.Size = size;
                            }
                            else if (line.StartsWith("Packed Size = "))
                            {
                                long compressedSize;
                                if (long.TryParse(line.Substring(14), out compressedSize))
                                    current.CompressedSize = compressedSize;
                            }
                            else if (line.StartsWith("Folder = "))
                                current.IsDirectory = line.Substring(9) == "+";
                        }
                    }
                    if (current != null)
                        entries.Add(current);

                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] 列表异常: {ex.Message}");
            }

            return entries;
        }

        /// <summary>
        /// 使用 7z 解压单个文件
        /// </summary>
        public byte[] ExtractSingleFile(string archivePath, string entryPath)
        {
            if (string.IsNullOrEmpty(_7zExePath) || !File.Exists(archivePath))
                return null;

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _7zExePath,
                        Arguments = $"x \"{archivePath}\" -o\"{tempDir}\" \"{entryPath}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit();

                        if (proc.ExitCode == 0)
                        {
                            string extractedFile = Path.Combine(tempDir, entryPath);
                            if (File.Exists(extractedFile))
                            {
                                return File.ReadAllBytes(extractedFile);
                            }
                        }
                    }
                }
                finally
                {
                    // 清理临时目录 (失败也不影响主要功能)
                    try { Directory.Delete(tempDir, true); }
                    catch (IOException) { /* 目录被占用，稍后会被系统清理 */ }
                    catch (UnauthorizedAccessException) { /* 无权限删除 */ }
                }
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] 提取异常: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region ZIP (Built-in .NET)

        /// <summary>
        /// 使用 .NET 内置功能解压 ZIP
        /// </summary>
        public bool ExtractZip(string zipPath, string outputDir, IProgress<double> progress = null)
        {
            if (!File.Exists(zipPath))
            {
                _log($"[压缩服务] 错误: 文件不存在 {zipPath}");
                return false;
            }

            try
            {
                Directory.CreateDirectory(outputDir);

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    int total = archive.Entries.Count;
                    int current = 0;

                    foreach (var entry in archive.Entries)
                    {
                        string destPath = Path.Combine(outputDir, entry.FullName);

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, true);
                        }

                        current++;
                        progress?.Report((double)current / total);
                    }
                }

                _log($"[压缩服务] ZIP 解压完成: {Path.GetFileName(zipPath)}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] ZIP 解压失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建 ZIP 压缩包
        /// </summary>
        public bool CreateZip(string zipPath, string sourceDir, IProgress<double> progress = null)
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, false);
                _log($"[压缩服务] ZIP 创建完成: {Path.GetFileName(zipPath)}");
                progress?.Report(1.0);
                return true;
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] ZIP 创建失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region LZMA

        /// <summary>
        /// 解压 LZMA 数据
        /// </summary>
        public byte[] DecompressLzma(byte[] compressedData, long uncompressedSize)
        {
            return LzmaDecoder.Decompress(compressedData, uncompressedSize);
        }

        /// <summary>
        /// 解压 LZMA2 数据
        /// </summary>
        public byte[] DecompressLzma2(byte[] compressedData, long uncompressedSize)
        {
            return LzmaDecoder.DecompressLzma2(compressedData, uncompressedSize);
        }

        /// <summary>
        /// 自动检测并解压 LZMA/LZMA2
        /// </summary>
        public byte[] DecompressLzmaAuto(byte[] compressedData, long uncompressedSize)
        {
            return LzmaDecoder.AutoDecompress(compressedData, uncompressedSize);
        }

        #endregion

        #region LZ4

        /// <summary>
        /// 解压 LZ4 数据
        /// </summary>
        public byte[] DecompressLz4(byte[] compressedData, int uncompressedSize)
        {
            return Lz4Decoder.Decompress(compressedData, uncompressedSize);
        }

        /// <summary>
        /// 解压 LZ4 Frame 格式
        /// </summary>
        /// <param name="compressedData">压缩数据</param>
        /// <param name="maxUncompressedSize">最大未压缩大小 (默认 64MB)</param>
        public byte[] DecompressLz4Frame(byte[] compressedData, int maxUncompressedSize = 64 * 1024 * 1024)
        {
            // 安全限制: 未压缩大小不超过指定最大值
            // LZ4 典型压缩比约 2-3 倍，但某些情况下可能更高
            int estimatedSize = Math.Min(compressedData.Length * 8, maxUncompressedSize);
            return Lz4Decoder.DecompressErofsBlock(compressedData, estimatedSize);
        }

        #endregion

        #region GZIP

        /// <summary>
        /// 解压 GZIP 数据
        /// </summary>
        public byte[] DecompressGZip(byte[] compressedData)
        {
            try
            {
                using (var input = new MemoryStream(compressedData))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] GZIP 解压失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 压缩为 GZIP
        /// </summary>
        public byte[] CompressGZip(byte[] data)
        {
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    {
                        gzip.Write(data, 0, data.Length);
                    }
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                _log($"[压缩服务] GZIP 压缩失败: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Auto Decompress

        /// <summary>
        /// 自动检测格式并解压
        /// </summary>
        public bool AutoExtract(string archivePath, string outputDir, IProgress<double> progress = null)
        {
            var format = DetectFileFormat(archivePath);
            _log($"[压缩服务] 检测到格式: {format}");

            switch (format)
            {
                case ArchiveFormat.Zip:
                    return ExtractZip(archivePath, outputDir, progress);

                case ArchiveFormat.SevenZip:
                case ArchiveFormat.Rar:
                case ArchiveFormat.Xz:
                case ArchiveFormat.Bz2:
                case ArchiveFormat.Tar:
                    return Extract7z(archivePath, outputDir, progress);

                case ArchiveFormat.GZip:
                    // GZIP 通常是单文件压缩，需要特殊处理
                    try
                    {
                        byte[] data = File.ReadAllBytes(archivePath);
                        byte[] decompressed = DecompressGZip(data);
                        if (decompressed != null)
                        {
                            Directory.CreateDirectory(outputDir);
                            string outputFile = Path.Combine(outputDir,
                                Path.GetFileNameWithoutExtension(archivePath));
                            File.WriteAllBytes(outputFile, decompressed);
                            progress?.Report(1.0);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[压缩服务] GZIP 解压失败: {ex.Message}");
                    }
                    return false;

                default:
                    _log("[压缩服务] 警告: 未知格式，尝试使用 7z");
                    return Extract7z(archivePath, outputDir, progress);
            }
        }

        /// <summary>
        /// 自动检测并解压字节数组
        /// </summary>
        public byte[] AutoDecompress(byte[] compressedData, long expectedSize = 0)
        {
            if (compressedData == null || compressedData.Length < 4)
                return null;

            var format = DetectFormat(compressedData);

            // 安全的大小估计: 最大 64MB，避免内存溢出
            const int MAX_ESTIMATED_SIZE = 64 * 1024 * 1024;
            int safeEstimatedSize = expectedSize > 0
                ? (int)Math.Min(expectedSize, MAX_ESTIMATED_SIZE)
                : Math.Min(compressedData.Length * 8, MAX_ESTIMATED_SIZE);

            switch (format)
            {
                case ArchiveFormat.GZip:
                    return DecompressGZip(compressedData);

                case ArchiveFormat.Lz4:
                    return DecompressLz4(compressedData, safeEstimatedSize);

                case ArchiveFormat.Lzma:
                    return DecompressLzma(compressedData, expectedSize);

                default:
                    // 尝试各种方式
                    byte[] result = null;

                    // 尝试 LZ4
                    if (expectedSize > 0)
                    {
                        result = DecompressLz4(compressedData, (int)expectedSize);
                        if (result != null && result.Length > 0)
                            return result;
                    }

                    // 尝试 LZMA
                    if (expectedSize > 0)
                    {
                        result = DecompressLzmaAuto(compressedData, expectedSize);
                        if (result != null && result.Length > 0)
                            return result;
                    }

                    // 尝试 GZIP
                    result = DecompressGZip(compressedData);
                    if (result != null && result.Length > 0)
                        return result;

                    return null;
            }
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// 压缩包条目信息
        /// </summary>
        public class ArchiveEntry
        {
            public string Path { get; set; }
            public long Size { get; set; }
            public long CompressedSize { get; set; }
            public bool IsDirectory { get; set; }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }
}
