// LoveAlways - Spreadtrum PAC Firmware Package Parser
// Spreadtrum/Unisoc PAC Firmware Package Parser
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LoveAlways.Spreadtrum.Common
{
    /// PAC Firmware Package Parser
    /// Supports BP_R1.0.0 and BP_R2.0.1 formats
    /// </summary>
    public class PacParser
    {
        private readonly Action<string> _log;

        // PAC version
        public const string VERSION_BP_R1 = "BP_R1.0.0";
        public const string VERSION_BP_R2 = "BP_R2.0.1";

        public PacParser(Action<string> log = null)
        {
            _log = log;
        }

        /// Parse PAC file
        /// </summary>
        public PacInfo Parse(string pacFilePath)
        {
            if (!File.Exists(pacFilePath))
                throw new FileNotFoundException("PAC file does not exist", pacFilePath);

            using (var fs = new FileStream(pacFilePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Parse PAC header
                var header = ParseHeader(reader);
                
                // Verify file size
                var fileInfo = new FileInfo(pacFilePath);
                ulong expectedSize = ((ulong)header.HiSize << 32) + header.LoSize;
                if (expectedSize != (ulong)fileInfo.Length)
                {
                    Log("[PAC] Warning: File size mismatch (Expected: {0}, Actual: {1})", expectedSize, fileInfo.Length);
                }

                // Parse file entries
                reader.BaseStream.Seek(header.PartitionsListStart, SeekOrigin.Begin);
                var files = new List<PacFileEntry>();

                for (int i = 0; i < header.PartitionCount; i++)
                {
                    var entry = ParseFileEntry(reader, header.Version);
                    if (entry != null)
                    {
                        files.Add(entry);
                        Log("[PAC] File: {0}, Size: {1}, Offset: 0x{2:X}",
                            entry.FileName, FormatSize(entry.Size), entry.DataOffset);
                    }
                }

                return new PacInfo
                {
                    FilePath = pacFilePath,
                    Header = header,
                    Files = files
                };
            }
        }

        /// Parse PAC header
        /// </summary>
        private PacHeader ParseHeader(BinaryReader reader)
        {
            var header = new PacHeader();

            // Version (44 bytes, Unicode)
            header.Version = ReadUnicodeString(reader, 44);
            Log("[PAC] Version: {0}", header.Version);

            if (header.Version != VERSION_BP_R1 && header.Version != VERSION_BP_R2)
            {
                throw new NotSupportedException("Unsupported PAC version: " + header.Version);
            }

            // File size
            header.HiSize = reader.ReadUInt32();
            header.LoSize = reader.ReadUInt32();

            // Product name (512 bytes, Unicode)
            header.ProductName = ReadUnicodeString(reader, 512);
            Log("[PAC] Product Name: {0}", header.ProductName);

            // Firmware name (512 bytes, Unicode)
            header.FirmwareName = ReadUnicodeString(reader, 512);
            Log("[PAC] Firmware Name: {0}", header.FirmwareName);

            Log("[PAC] Partition Count: {0}, List Offset: 0x{1:X}", header.PartitionCount, header.PartitionsListStart);

            // Other fields
            header.Mode = reader.ReadUInt32();
            header.FlashType = reader.ReadUInt32();
            header.NandStrategy = reader.ReadUInt32();
            header.IsNvBackup = reader.ReadUInt32();
            header.NandPageType = reader.ReadUInt32();

            // Product alias (996 bytes, Unicode)
            header.ProductAlias = ReadUnicodeString(reader, 996);

            header.OmaDmProductFlag = reader.ReadUInt32();
            header.IsOmaDM = reader.ReadUInt32();
            header.IsPreload = reader.ReadUInt32();
            header.Reserved = reader.ReadUInt32();
            header.Magic = reader.ReadUInt32();
            header.Crc1 = reader.ReadUInt32();
            header.Crc2 = reader.ReadUInt32();

            return header;
        }

        /// Parse file entry
        /// </summary>
        private PacFileEntry ParseFileEntry(BinaryReader reader, string version)
        {
            var entry = new PacFileEntry();

            // Entry length
            entry.HeaderLength = reader.ReadUInt32();

            // Partition name (512 bytes, Unicode)
            entry.PartitionName = ReadUnicodeString(reader, 512);

            // File name (512 bytes, Unicode)
            entry.FileName = ReadUnicodeString(reader, 512);

            // Original file name (508 bytes, Unicode)
            entry.OriginalFileName = ReadUnicodeString(reader, 508);

            if (version == VERSION_BP_R1)
            {
                // BP_R1 format
                entry.HiDataOffset = reader.ReadUInt32();
                entry.HiSize = reader.ReadUInt32();
                reader.ReadUInt32(); // reserved1
                reader.ReadUInt32(); // reserved2
                entry.LoDataOffset = reader.ReadUInt32();
                entry.LoSize = reader.ReadUInt32();
                entry.FileFlag = reader.ReadUInt16();
                entry.CheckFlag = reader.ReadUInt16();
                reader.ReadUInt32(); // reserved3
                entry.CanOmitFlag = reader.ReadUInt32();
                entry.AddrNum = reader.ReadUInt32();
                entry.Address = reader.ReadUInt32();
                reader.ReadUInt32(); // reserved4
                reader.ReadBytes(996); // reserved data
            }
            else if (version == VERSION_BP_R2)
            {
                // BP_R2 format - using szPartitionInfo
                byte[] partitionInfo = reader.ReadBytes(24);
                
                // Parse partition info (Little-endian reversed)
                entry.HiSize = ParseReversedUInt32(partitionInfo, 0);
                entry.LoSize = ParseReversedUInt32(partitionInfo, 4);
                // bytes 8-15 contain extra info
                entry.HiDataOffset = ParseReversedUInt32(partitionInfo, 16);
                entry.LoDataOffset = ParseReversedUInt32(partitionInfo, 20);

                reader.ReadUInt32(); // reserved2
                entry.FileFlag = reader.ReadUInt16();
                entry.CheckFlag = reader.ReadUInt16();
                reader.ReadUInt32(); // reserved3
                entry.CanOmitFlag = reader.ReadUInt32();
                entry.AddrNum = reader.ReadUInt32();
                entry.Address = reader.ReadUInt32();
                reader.ReadBytes(996); // reserved data
            }

            // Calculate actual offset and size
            entry.DataOffset = CombineHiLo(entry.HiDataOffset, entry.LoDataOffset);
            entry.Size = CombineHiLo(entry.HiSize, entry.LoSize);

            // Determine type
            entry.Type = DetermineFileType(entry);

            return entry;
        }

        /// Extract file
        /// </summary>
        public void ExtractFile(string pacFilePath, PacFileEntry entry, string outputPath, Action<long, long> progress = null)
        {
            using (var fs = new FileStream(pacFilePath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek((long)entry.DataOffset, SeekOrigin.Begin);

                using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[65536];
                    long remaining = (long)entry.Size;
                    long written = 0;

                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int read = fs.Read(buffer, 0, toRead);
                        
                        if (read == 0)
                            break;

                        // Check if it's a Sparse Image
                        if (written == 0 && IsSparseImage(buffer))
                        {
                            entry.IsSparse = true;
                        }

                        output.Write(buffer, 0, read);
                        remaining -= read;
                        written += read;

                        progress?.Invoke(written, (long)entry.Size);
                    }
                }
            }

            Log("[PAC] Extraction complete: {0}", outputPath);
        }

        /// Extract all files
        /// </summary>
        public void ExtractAll(PacInfo pac, string outputDir, Action<int, int, string> progress = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            for (int i = 0; i < pac.Files.Count; i++)
            {
                var entry = pac.Files[i];
                
                if (string.IsNullOrEmpty(entry.FileName) || entry.Size == 0)
                    continue;

                string outputPath = Path.Combine(outputDir, entry.FileName);
                progress?.Invoke(i + 1, pac.Files.Count, entry.FileName);

                ExtractFile(pac.FilePath, entry, outputPath);
            }
        }

        /// Get FDL1 entry
        /// </summary>
        public PacFileEntry GetFdl1(PacInfo pac)
        {
            return pac.Files.Find(f => 
                f.PartitionName.Equals("FDL", StringComparison.OrdinalIgnoreCase) ||
                f.Type == PacFileType.FDL1);
        }

        /// Get FDL2 entry
        /// </summary>
        public PacFileEntry GetFdl2(PacInfo pac)
        {
            return pac.Files.Find(f => 
                f.PartitionName.Equals("FDL2", StringComparison.OrdinalIgnoreCase) ||
                f.Type == PacFileType.FDL2);
        }

        /// Parse and integrate XML configurations
        /// </summary>
        public void ParseXmlConfigs(PacInfo pac)
        {
            if (pac == null || pac.Files == null)
                return;

            var xmlParser = new XmlConfigParser(msg => _log?.Invoke(msg));

            // Find all XML files
            var xmlFiles = pac.Files.Where(f => 
                f.Type == PacFileType.XML || 
                f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var xmlFile in xmlFiles)
            {
                try
                {
                    // Read XML data
                    byte[] xmlData = ExtractFileData(pac.FilePath, xmlFile);
                    if (xmlData != null && xmlData.Length > 0)
                    {
                        var config = xmlParser.Parse(xmlData);
                        if (config != null)
                        {
                            config.ProductionSettings = config.ProductionSettings ?? new Dictionary<string, string>();
                            config.ProductionSettings["SourceFile"] = xmlFile.FileName;
                            
                            pac.AllXmlConfigs.Add(config);
                            Log("[PAC] Parse XML config: {0} ({1})", xmlFile.FileName, config.ConfigType);

                            // Set main config (prioritize BmaConfig)
                            if (pac.XmlConfig == null || config.ConfigType == SprdXmlConfigType.BmaConfig)
                            {
                                pac.XmlConfig = config;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("[PAC] XML parsing failed ({0}): {1}", xmlFile.FileName, ex.Message);
                }
            }

            // If there's XML configuration, update file attributes
            if (pac.XmlConfig != null)
            {
                UpdateFilesFromXmlConfig(pac);
            }
        }

        /// <summary>
        /// Update file info from XML config
        /// </summary>
        private void UpdateFilesFromXmlConfig(PacInfo pac)
        {
            if (pac.XmlConfig == null)
                return;

            // Update FDL configuration
            if (pac.XmlConfig.Fdl1Config != null)
            {
                var fdl1 = GetFdl1(pac);
                if (fdl1 != null && pac.XmlConfig.Fdl1Config.Address > 0)
                {
                    fdl1.Address = (uint)pac.XmlConfig.Fdl1Config.Address;
                    Log("[PAC] Update FDL1 address from XML: 0x{0:X}", fdl1.Address);
                }
            }

            if (pac.XmlConfig.Fdl2Config != null)
            {
                var fdl2 = GetFdl2(pac);
                if (fdl2 != null && pac.XmlConfig.Fdl2Config.Address > 0)
                {
                    fdl2.Address = (uint)pac.XmlConfig.Fdl2Config.Address;
                    Log("[PAC] Update FDL2 address from XML: 0x{0:X}", fdl2.Address);
                }
            }

            // Update file address
            foreach (var xmlFile in pac.XmlConfig.Files)
            {
                var pacFile = pac.Files.Find(f =>
                    f.PartitionName.Equals(xmlFile.Name, StringComparison.OrdinalIgnoreCase) ||
                    f.FileName.Equals(xmlFile.FileName, StringComparison.OrdinalIgnoreCase));

                if (pacFile != null && xmlFile.Address > 0)
                {
                    pacFile.Address = (uint)xmlFile.Address;
                }
            }
        }

        /// Extract file data to memory
        /// </summary>
        public byte[] ExtractFileData(string pacFilePath, PacFileEntry entry)
        {
            if (entry.Size == 0)
                return null;

            try
            {
                using (var fs = new FileStream(pacFilePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek((long)entry.DataOffset, SeekOrigin.Begin);
                    
                    byte[] data = new byte[entry.Size];
                    int read = fs.Read(data, 0, (int)entry.Size);
                    
                    if (read < (int)entry.Size)
                    {
                        Array.Resize(ref data, read);
                    }
                    
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log("[PAC] Failed to extract file data ({0}): {1}", entry.FileName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Get XML configuration files
        /// </summary>
        public List<PacFileEntry> GetXmlFiles(PacInfo pac)
        {
            return pac.Files.Where(f => 
                f.Type == PacFileType.XML || 
                f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        #region Helper Methods

        private string ReadUnicodeString(BinaryReader reader, int byteLength)
        {
            byte[] bytes = reader.ReadBytes(byteLength);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        private uint ParseReversedUInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length)
                return 0;

            // Reverse bytes
            byte[] reversed = new byte[4];
            reversed[0] = data[offset + 3];
            reversed[1] = data[offset + 2];
            reversed[2] = data[offset + 1];
            reversed[3] = data[offset];

            return BitConverter.ToUInt32(reversed, 0);
        }

        private ulong CombineHiLo(uint hi, uint lo)
        {
            if (hi > 2)
                return hi;
            if (lo > 2)
                return lo;
            return ((ulong)hi << 32) | lo;
        }

        private PacFileType DetermineFileType(PacFileEntry entry)
        {
            string name = entry.PartitionName.ToLower();
            string fileName = entry.FileName.ToLower();

            if (name == "fdl" || fileName.Contains("fdl1"))
                return PacFileType.FDL1;
            if (name == "fdl2" || fileName.Contains("fdl2"))
                return PacFileType.FDL2;
            if (fileName.EndsWith(".xml"))
                return PacFileType.XML;
            if (name.Contains("nv") || name.Contains("nvitem"))
                return PacFileType.NV;
            if (name.Contains("boot"))
                return PacFileType.Boot;
            if (name.Contains("system") || name.Contains("super"))
                return PacFileType.System;
            if (name.Contains("userdata"))
                return PacFileType.UserData;

            return PacFileType.Partition;
        }

        private bool IsSparseImage(byte[] header)
        {
            if (header.Length < 4)
                return false;

            // Sparse magic: 0xED26FF3A
            uint magic = BitConverter.ToUInt32(header, 0);
            return magic == 0xED26FF3A;
        }

        private string FormatSize(ulong size)
        {
            if (size >= 1024UL * 1024 * 1024)
                return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
            if (size >= 1024 * 1024)
                return string.Format("{0:F2} MB", size / (1024.0 * 1024));
            if (size >= 1024)
                return string.Format("{0:F2} KB", size / 1024.0);
            return string.Format("{0} B", size);
        }

        private void Log(string format, params object[] args)
        {
            _log?.Invoke(string.Format(format, args));
        }

        #endregion
    }

    #region Data Structures

    /// PAC Information
    /// </summary>
    public class PacInfo
    {
        public string FilePath { get; set; }
        public PacHeader Header { get; set; }
        public List<PacFileEntry> Files { get; set; }

        /// <summary>
        /// XML configuration (if PAC contains it)
        /// </summary>
        public SprdXmlConfig XmlConfig { get; set; }

        /// <summary>
        /// All XML configurations (may have multiple)
        /// </summary>
        public List<SprdXmlConfig> AllXmlConfigs { get; set; } = new List<SprdXmlConfig>();

        /// <summary>
        /// Get total size
        /// </summary>
        public ulong TotalSize => ((ulong)Header.HiSize << 32) + Header.LoSize;

        /// Get file list in flash order
        /// </summary>
        public List<PacFileEntry> GetFlashOrder()
        {
            var order = new List<PacFileEntry>();

            // 1. FDL1
            var fdl1 = Files.Find(f => f.Type == PacFileType.FDL1);
            if (fdl1 != null) order.Add(fdl1);

            // 2. FDL2
            var fdl2 = Files.Find(f => f.Type == PacFileType.FDL2);
            if (fdl2 != null) order.Add(fdl2);

            // 3. If there is an XML configuration, follow the configuration order
            if (XmlConfig != null && XmlConfig.Files.Count > 0)
            {
                foreach (var xmlFile in XmlConfig.Files)
                {
                    if (xmlFile.Type == SprdXmlFileType.FDL1 || xmlFile.Type == SprdXmlFileType.FDL2)
                        continue;

                    var pacFile = Files.Find(f => 
                        f.PartitionName.Equals(xmlFile.Name, StringComparison.OrdinalIgnoreCase) ||
                        f.FileName.Equals(xmlFile.FileName, StringComparison.OrdinalIgnoreCase));

                    if (pacFile != null && !order.Contains(pacFile))
                        order.Add(pacFile);
                }
            }

            // 4. 添加剩余文件
            foreach (var file in Files)
            {
                if (!order.Contains(file) && 
                    file.Type != PacFileType.FDL1 && 
                    file.Type != PacFileType.FDL2 &&
                    file.Type != PacFileType.XML &&
                    file.Size > 0)
                {
                    order.Add(file);
                }
            }

            return order;
        }
    }

    /// PAC Header
    /// </summary>
    public class PacHeader
    {
        public string Version { get; set; }
        public uint HiSize { get; set; }
        public uint LoSize { get; set; }
        public string ProductName { get; set; }
        public string FirmwareName { get; set; }
        public uint PartitionCount { get; set; }
        public uint PartitionsListStart { get; set; }
        public uint Mode { get; set; }
        public uint FlashType { get; set; }
        public uint NandStrategy { get; set; }
        public uint IsNvBackup { get; set; }
        public uint NandPageType { get; set; }
        public string ProductAlias { get; set; }
        public uint OmaDmProductFlag { get; set; }
        public uint IsOmaDM { get; set; }
        public uint IsPreload { get; set; }
        public uint Reserved { get; set; }
        public uint Magic { get; set; }
        public uint Crc1 { get; set; }
        public uint Crc2 { get; set; }
    }

    /// PAC File Entry
    /// </summary>
    public class PacFileEntry
    {
        public uint HeaderLength { get; set; }
        public string PartitionName { get; set; }
        public string FileName { get; set; }
        public string OriginalFileName { get; set; }
        public uint HiDataOffset { get; set; }
        public uint LoDataOffset { get; set; }
        public uint HiSize { get; set; }
        public uint LoSize { get; set; }
        public ushort FileFlag { get; set; }
        public ushort CheckFlag { get; set; }
        public uint CanOmitFlag { get; set; }
        public uint AddrNum { get; set; }
        public uint Address { get; set; }

        // Calculated values
        public ulong DataOffset { get; set; }
        public ulong Size { get; set; }
        public PacFileType Type { get; set; }
        public bool IsSparse { get; set; }

        public override string ToString()
        {
            return string.Format("{0} ({1}, {2} bytes)", PartitionName, FileName, Size);
        }
    }

    /// PAC File Type
    /// </summary>
    public enum PacFileType
    {
        Unknown,
        FDL1,
        FDL2,
        XML,
        NV,
        Boot,
        System,
        UserData,
        Partition
    }

    #endregion
}
