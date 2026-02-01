
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Payload
{
    /// <summary>
    /// Payload Service
    /// Provides advanced functions such as payload.bin parsing, partition extraction, and direct flashing
    /// </summary>
    public class PayloadService : IDisposable
    {
        #region Fields
        
        private PayloadParser _parser;
        private string _currentPayloadPath;
        private bool _disposed;
        
        private readonly Action<string> _log;
        private readonly Action<string> _logDetail;
        private readonly Action<int, int> _progress;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether Payload is loaded
        /// </summary>
        public bool IsLoaded => _parser?.IsInitialized ?? false;
        
        /// <summary>
        /// Current Payload path
        /// </summary>
        public string CurrentPayloadPath => _currentPayloadPath;
        
        /// <summary>
        /// Partition list
        /// </summary>
        public IReadOnlyList<PayloadPartition> Partitions => _parser?.Partitions ?? new List<PayloadPartition>();
        
        /// <summary>
        /// File format version
        /// </summary>
        public ulong FileFormatVersion => _parser?.FileFormatVersion ?? 0;
        
        /// <summary>
        /// Block size
        /// </summary>
        public uint BlockSize => _parser?.BlockSize ?? 4096;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Extraction progress event
        /// </summary>
        public event EventHandler<PayloadExtractProgress> ExtractProgressChanged;
        
        #endregion
        
        #region Constructor
        
        public PayloadService(Action<string> log = null, Action<int, int> progress = null, Action<string> logDetail = null)
        {
            _log = log ?? (msg => { });
            _progress = progress;
            _logDetail = logDetail ?? (msg => { });
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Load Payload file
        /// </summary>
        public async Task<bool> LoadPayloadAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _log("File path cannot be empty");
                return false;
            }
            
            if (!File.Exists(filePath))
            {
                _log($"File does not exist: {filePath}");
                return false;
            }
            
            // Clean up previous parser
            _parser?.Dispose();
            _parser = new PayloadParser(_log, _logDetail);
            
            _log($"Loading Payload: {Path.GetFileName(filePath)}...");
            
            bool result = await _parser.LoadAsync(filePath, ct);
            
            if (result)
            {
                _currentPayloadPath = filePath;
                LogPayloadInfo();
            }
            
            return result;
        }
        
        /// <summary>
        /// Extract single partition
        /// </summary>
        public async Task<bool> ExtractPartitionAsync(string partitionName, string outputPath, CancellationToken ct = default)
        {
            if (!IsLoaded)
            {
                _log("Please load Payload file first");
                return false;
            }
            
            var progress = new Progress<PayloadExtractProgress>(p =>
            {
                ExtractProgressChanged?.Invoke(this, p);
                _progress?.Invoke((int)p.Percent, 100);
            });
            
            return await _parser.ExtractPartitionAsync(partitionName, outputPath, progress, ct);
        }
        
        /// <summary>
        /// Extract all partitions
        /// </summary>
        public async Task<int> ExtractAllPartitionsAsync(string outputDir, CancellationToken ct = default)
        {
            if (!IsLoaded)
            {
                _log("Please load Payload file first");
                return 0;
            }
            
            var progress = new Progress<PayloadExtractProgress>(p =>
            {
                ExtractProgressChanged?.Invoke(this, p);
                _progress?.Invoke((int)p.Percent, 100);
            });
            
            return await _parser.ExtractAllPartitionsAsync(outputDir, progress, ct);
        }
        
        /// <summary>
        /// Extract selected partitions
        /// </summary>
        public async Task<int> ExtractSelectedPartitionsAsync(IEnumerable<string> partitionNames, string outputDir, CancellationToken ct = default)
        {
            if (!IsLoaded)
            {
                _log("Please load Payload file first");
                return 0;
            }
            
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            
            var names = partitionNames.ToList();
            int successCount = 0;
            int total = names.Count;
            
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var name = names[i];
                var outputPath = Path.Combine(outputDir, $"{name}.img");
                
                _log($"Extracting {name} ({i + 1}/{total})...");
                
                if (await _parser.ExtractPartitionAsync(name, outputPath, null, ct))
                {
                    successCount++;
                }
                
                _progress?.Invoke(i + 1, total);
            }
            
            _log($"Extraction complete: {successCount}/{total} partitions");
            return successCount;
        }
        
        /// <summary>
        /// Get partition information
        /// </summary>
        public PayloadPartition GetPartitionInfo(string partitionName)
        {
            return Partitions.FirstOrDefault(p => 
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Check if partition exists
        /// </summary>
        public bool HasPartition(string partitionName)
        {
            return Partitions.Any(p => 
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Get all partition names
        /// </summary>
        public IEnumerable<string> GetPartitionNames()
        {
            return Partitions.Select(p => p.Name);
        }
        
        /// <summary>
        /// Get Payload summary information
        /// </summary>
        public PayloadSummary GetSummary()
        {
            if (!IsLoaded)
                return null;
            
            return new PayloadSummary
            {
                FilePath = _currentPayloadPath,
                FileName = Path.GetFileName(_currentPayloadPath),
                FileFormatVersion = FileFormatVersion,
                BlockSize = BlockSize,
                PartitionCount = Partitions.Count,
                TotalSize = (ulong)Partitions.Sum(p => (long)p.Size),
                TotalCompressedSize = (ulong)Partitions.Sum(p => (long)p.CompressedSize),
                Partitions = Partitions.ToList()
            };
        }
        
        /// <summary>
        /// Close Payload
        /// </summary>
        public void Close()
        {
            _parser?.Dispose();
            _parser = null;
            _currentPayloadPath = null;
        }
        
        #endregion
        
        #region Private Methods
        
        private void LogPayloadInfo()
        {
            _log($"[Payload] Format version: {FileFormatVersion}");
            _log($"[Payload] Block size: {BlockSize} bytes");
            _log($"[Payload] Partition count: {Partitions.Count}");
            
            // Output partition list
            _logDetail("Partition list:");
            foreach (var partition in Partitions)
            {
                _logDetail($"  - {partition.Name}: {partition.SizeFormatted} (Compressed: {partition.CompressedSizeFormatted})");
            }
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Payload Summary
    /// </summary>
    public class PayloadSummary
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public ulong FileFormatVersion { get; set; }
        public uint BlockSize { get; set; }
        public int PartitionCount { get; set; }
        public ulong TotalSize { get; set; }
        public ulong TotalCompressedSize { get; set; }
        public List<PayloadPartition> Partitions { get; set; }
        
        public string TotalSizeFormatted => FormatSize(TotalSize);
        public string TotalCompressedSizeFormatted => FormatSize(TotalCompressedSize);
        public double CompressionRatio => TotalSize > 0 ? (double)TotalCompressedSize / TotalSize : 1;
        
        private static string FormatSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:F2} {units[unitIndex]}";
        }
    }
}
