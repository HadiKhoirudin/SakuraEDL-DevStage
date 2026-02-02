// ============================================================================
// LoveAlways - Spreadtrum XML Configuration Parser
// Spreadtrum/Unisoc XML Configuration Parser
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace LoveAlways.Spreadtrum.Common
{
    /// <summary>
    /// Spreadtrum XML Configuration Parser
    /// Parses XML configuration files within PAC packages
    /// </summary>
    public class XmlConfigParser
    {
        private readonly Action<string> _log;

        public XmlConfigParser(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        #region Main Parsing Methods

        /// <summary>
        /// Parse XML configuration data
        /// </summary>
        public SprdXmlConfig Parse(byte[] xmlData)
        {
            if (xmlData == null || xmlData.Length == 0)
                return null;

            try
            {
                // Remove BOM and null characters
                string xmlContent = CleanXmlContent(xmlData);

                var doc = XDocument.Parse(xmlContent);
                var config = new SprdXmlConfig();

                // Get root element
                var root = doc.Root;
                if (root == null)
                    return null;

                Log("[XML] Parsing configuration: {0}", root.Name.LocalName);

                // Parse different types of XML configurations
                if (root.Name.LocalName == "BMAConfig" || root.Name.LocalName == "BMFileWapper")
                {
                    // Standard flashing configuration
                    ParseBmaConfig(root, config);
                }
                else if (root.Name.LocalName == "Partition")
                {
                    // Partition table configuration
                    ParsePartitionConfig(root, config);
                }
                else if (root.Name.LocalName == "NVBackupRestore")
                {
                    // NV backup/restore configuration
                    ParseNvConfig(root, config);
                }
                else if (root.Name.LocalName == "ProductionConfig" || root.Name.LocalName == "Production")
                {
                    // Production configuration
                    ParseProductionConfig(root, config);
                }
                else
                {
                    // Attempt generic parsing
                    ParseGenericConfig(root, config);
                }

                return config;
            }
            catch (Exception ex)
            {
                Log("[XML] Parsing error: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Parse from file
        /// </summary>
        public SprdXmlConfig ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            byte[] data = File.ReadAllBytes(filePath);
            return Parse(data);
        }

        #endregion

        #region BMA Config Parsing

        /// <summary>
        /// Parse BMA flashing configuration
        /// </summary>
        private void ParseBmaConfig(XElement root, SprdXmlConfig config)
        {
            config.ConfigType = SprdXmlConfigType.BmaConfig;

            // Parse product information
            var product = root.Element("Product") ?? root.Element("ProductInfo");
            if (product != null)
            {
                config.ProductName = GetAttributeOrElement(product, "name") ??
                                     GetAttributeOrElement(product, "Name");
                config.Version = GetAttributeOrElement(product, "version") ??
                                GetAttributeOrElement(product, "Version");
                Log("[XML] Product: {0}, Version: {1}", config.ProductName, config.Version);
            }

            // Parse flashing scheme
            var scheme = root.Element("Scheme") ?? root.Element("DownloadScheme");
            if (scheme != null)
            {
                config.SchemeName = GetAttributeOrElement(scheme, "name");
                Log("[XML] Scheme: {0}", config.SchemeName);
            }

            // Parse file list
            ParseFileList(root, config);

            // Parse FDL configuration
            ParseFdlConfig(root, config);

            // Parse partition operations
            ParsePartitionOperations(root, config);

            // Parse erase configuration
            ParseEraseConfig(root, config);

            // Parse NV configuration
            ParseNvOperations(root, config);
        }

        /// <summary>
        /// Parse file list
        /// </summary>
        private void ParseFileList(XElement root, SprdXmlConfig config)
        {
            // Find file list node
            var fileNodes = root.Descendants("File")
                .Concat(root.Descendants("BMFile"))
                .Concat(root.Descendants("DownLoadFile"));

            foreach (var fileNode in fileNodes)
            {
                var fileInfo = new SprdXmlFileInfo
                {
                    ID = GetAttributeOrElement(fileNode, "ID") ??
                         GetAttributeOrElement(fileNode, "id"),
                    Name = GetAttributeOrElement(fileNode, "Name") ??
                           GetAttributeOrElement(fileNode, "name") ??
                           GetAttributeOrElement(fileNode, "PARTITION_NAME"),
                    FileName = GetAttributeOrElement(fileNode, "IDAlias") ??
                               GetAttributeOrElement(fileNode, "FileName") ??
                               GetAttributeOrElement(fileNode, "File"),
                    Type = ParseFileType(GetAttributeOrElement(fileNode, "Type") ??
                                        GetAttributeOrElement(fileNode, "type")),
                    Block = GetAttributeOrElement(fileNode, "Block") ??
                            GetAttributeOrElement(fileNode, "block"),
                    Flag = ParseFlag(GetAttributeOrElement(fileNode, "Flag") ??
                                    GetAttributeOrElement(fileNode, "flag")),
                    CheckFlag = ParseCheckFlag(GetAttributeOrElement(fileNode, "CheckFlag")),
                    IsSelected = ParseBool(GetAttributeOrElement(fileNode, "Selected") ??
                                          GetAttributeOrElement(fileNode, "Use"), true)
                };

                // Parse address
                string addrStr = GetAttributeOrElement(fileNode, "Base") ??
                                GetAttributeOrElement(fileNode, "Address") ??
                                GetAttributeOrElement(fileNode, "LoadAddr");
                if (!string.IsNullOrEmpty(addrStr))
                {
                    fileInfo.Address = ParseHexOrDecimal(addrStr);
                }

                // Parse size
                string sizeStr = GetAttributeOrElement(fileNode, "Size") ??
                                GetAttributeOrElement(fileNode, "Length");
                if (!string.IsNullOrEmpty(sizeStr))
                {
                    fileInfo.Size = ParseHexOrDecimal(sizeStr);
                }

                if (!string.IsNullOrEmpty(fileInfo.Name) || !string.IsNullOrEmpty(fileInfo.FileName))
                {
                    config.Files.Add(fileInfo);
                    Log("[XML] File: {0} -> {1}, Address: 0x{2:X}, Size: {3}",
                        fileInfo.Name, fileInfo.FileName, fileInfo.Address, FormatSize(fileInfo.Size));
                }
            }
        }

        /// <summary>
        /// Parse FDL configuration
        /// </summary>
        private void ParseFdlConfig(XElement root, SprdXmlConfig config)
        {
            // FDL1 configuration
            var fdl1 = root.Descendants("FDL1")
                .Concat(root.Descendants("FDL"))
                .FirstOrDefault();

            if (fdl1 != null)
            {
                config.Fdl1Config = new SprdFdlConfig
                {
                    FileName = GetAttributeOrElement(fdl1, "File") ??
                               GetAttributeOrElement(fdl1, "FileName") ??
                               fdl1.Value,
                    Address = ParseHexOrDecimal(GetAttributeOrElement(fdl1, "Base") ??
                                               GetAttributeOrElement(fdl1, "Address") ?? "0"),
                    BaudRate = ParseInt(GetAttributeOrElement(fdl1, "Baud") ??
                                       GetAttributeOrElement(fdl1, "BaudRate"), 115200)
                };
                Log("[XML] FDL1: {0} @ 0x{1:X}", config.Fdl1Config.FileName, config.Fdl1Config.Address);
            }

            // FDL2 configuration
            var fdl2 = root.Descendants("FDL2").FirstOrDefault();
            if (fdl2 != null)
            {
                config.Fdl2Config = new SprdFdlConfig
                {
                    FileName = GetAttributeOrElement(fdl2, "File") ??
                               GetAttributeOrElement(fdl2, "FileName") ??
                               fdl2.Value,
                    Address = ParseHexOrDecimal(GetAttributeOrElement(fdl2, "Base") ??
                                               GetAttributeOrElement(fdl2, "Address") ?? "0"),
                    BaudRate = ParseInt(GetAttributeOrElement(fdl2, "Baud") ??
                                       GetAttributeOrElement(fdl2, "BaudRate"), 921600)
                };
                Log("[XML] FDL2: {0} @ 0x{1:X}", config.Fdl2Config.FileName, config.Fdl2Config.Address);
            }
        }

        /// <summary>
        /// Parse partition operations
        /// </summary>
        private void ParsePartitionOperations(XElement root, SprdXmlConfig config)
        {
            var partOps = root.Descendants("PartitionOperation")
                .Concat(root.Descendants("Operation"));

            foreach (var op in partOps)
            {
                var operation = new SprdPartitionOperation
                {
                    PartitionName = GetAttributeOrElement(op, "Partition") ??
                                   GetAttributeOrElement(op, "Name"),
                    Operation = ParseOperationType(GetAttributeOrElement(op, "Op") ??
                                                  GetAttributeOrElement(op, "Operation") ??
                                                  GetAttributeOrElement(op, "Type")),
                    Priority = ParseInt(GetAttributeOrElement(op, "Priority"), 0),
                    IsEnabled = ParseBool(GetAttributeOrElement(op, "Enable"), true)
                };

                if (!string.IsNullOrEmpty(operation.PartitionName))
                {
                    config.PartitionOperations.Add(operation);
                }
            }

            // Sort by priority
            config.PartitionOperations = config.PartitionOperations
                .OrderBy(p => p.Priority)
                .ToList();
        }

        /// <summary>
        /// Parse erase configuration
        /// </summary>
        private void ParseEraseConfig(XElement root, SprdXmlConfig config)
        {
            var eraseNode = root.Element("Erase") ?? root.Element("EraseConfig");
            if (eraseNode != null)
            {
                config.EraseConfig = new SprdEraseConfig
                {
                    EraseAll = ParseBool(GetAttributeOrElement(eraseNode, "EraseAll"), false),
                    EraseUserData = ParseBool(GetAttributeOrElement(eraseNode, "EraseUserData"), false),
                    EraseNv = ParseBool(GetAttributeOrElement(eraseNode, "EraseNV"), false),
                    FormatFlash = ParseBool(GetAttributeOrElement(eraseNode, "Format"), false)
                };

                // Parse excluded partitions
                var excludeNode = eraseNode.Element("Exclude");
                if (excludeNode != null)
                {
                    config.EraseConfig.ExcludePartitions = excludeNode.Value?
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList() ?? new List<string>();
                }

                Log("[XML] Erase config: All={0}, UserData={1}, NV={2}",
                    config.EraseConfig.EraseAll, config.EraseConfig.EraseUserData, config.EraseConfig.EraseNv);
            }
        }

        /// <summary>
        /// Parse NV operation
        /// </summary>
        private void ParseNvOperations(XElement root, SprdXmlConfig config)
        {
            var nvNodes = root.Descendants("NV")
                .Concat(root.Descendants("NVItem"));

            foreach (var nv in nvNodes)
            {
                var nvOp = new SprdNvOperation
                {
                    ItemId = (ushort)ParseHexOrDecimal(GetAttributeOrElement(nv, "ID") ?? "0"),
                    Name = GetAttributeOrElement(nv, "Name"),
                    Operation = GetAttributeOrElement(nv, "Op") ?? "backup",
                    BackupFile = GetAttributeOrElement(nv, "BackupFile"),
                    RestoreFile = GetAttributeOrElement(nv, "RestoreFile")
                };

                if (nvOp.ItemId > 0 || !string.IsNullOrEmpty(nvOp.Name))
                {
                    config.NvOperations.Add(nvOp);
                }
            }
        }

        #endregion

        #region Partition Config Parsing

        /// <summary>
        /// Parse partition table configuration
        /// </summary>
        private void ParsePartitionConfig(XElement root, SprdXmlConfig config)
        {
            config.ConfigType = SprdXmlConfigType.PartitionTable;

            var partitions = root.Descendants("Part")
                .Concat(root.Descendants("Partition"))
                .Concat(root.Descendants("Entry"));

            foreach (var part in partitions)
            {
                var partInfo = new SprdXmlPartitionInfo
                {
                    Name = GetAttributeOrElement(part, "id") ??
                           GetAttributeOrElement(part, "Name") ??
                           GetAttributeOrElement(part, "name"),
                    Size = ParseHexOrDecimal(GetAttributeOrElement(part, "size") ??
                                            GetAttributeOrElement(part, "Size") ?? "0"),
                    Offset = ParseHexOrDecimal(GetAttributeOrElement(part, "offset") ??
                                              GetAttributeOrElement(part, "Offset") ?? "0"),
                    Type = GetAttributeOrElement(part, "type") ??
                           GetAttributeOrElement(part, "Type") ?? "unknown",
                    FileSystem = GetAttributeOrElement(part, "fs") ??
                                GetAttributeOrElement(part, "FileSystem"),
                    IsReadOnly = ParseBool(GetAttributeOrElement(part, "readonly"), false)
                };

                if (!string.IsNullOrEmpty(partInfo.Name))
                {
                    config.PartitionTable.Add(partInfo);
                    Log("[XML] Partition: {0}, Size: {1}, Offset: 0x{2:X}",
                        partInfo.Name, FormatSize(partInfo.Size), partInfo.Offset);
                }
            }
        }

        #endregion

        #region NV Config Parsing

        /// <summary>
        /// Parse NV backup/restore configuration
        /// </summary>
        private void ParseNvConfig(XElement root, SprdXmlConfig config)
        {
            config.ConfigType = SprdXmlConfigType.NvConfig;

            var nvItems = root.Descendants("Item")
                .Concat(root.Descendants("NVItem"));

            foreach (var item in nvItems)
            {
                var nvOp = new SprdNvOperation
                {
                    ItemId = (ushort)ParseHexOrDecimal(GetAttributeOrElement(item, "ID") ?? "0"),
                    Name = GetAttributeOrElement(item, "Name"),
                    Operation = GetAttributeOrElement(item, "Operation") ?? "backup",
                    Category = GetAttributeOrElement(item, "Category") ?? "system"
                };

                if (nvOp.ItemId > 0)
                {
                    config.NvOperations.Add(nvOp);
                }
            }
        }

        #endregion

        #region Production Config Parsing

        /// <summary>
        /// Parse production configuration
        /// </summary>
        private void ParseProductionConfig(XElement root, SprdXmlConfig config)
        {
            config.ConfigType = SprdXmlConfigType.ProductionConfig;

            // Parse production parameters
            config.ProductionSettings = new Dictionary<string, string>();

            foreach (var elem in root.Elements())
            {
                string key = elem.Name.LocalName;
                string value = elem.Value ?? GetAttributeOrElement(elem, "value");
                if (!string.IsNullOrEmpty(key))
                {
                    config.ProductionSettings[key] = value;
                }
            }

            // Also attempt to parse file list
            ParseFileList(root, config);
        }

        #endregion

        #region Generic Config Parsing

        /// <summary>
        /// <summary>
        /// Generic configuration parsing
        /// </summary>
        private void ParseGenericConfig(XElement root, SprdXmlConfig config)
        {
            config.ConfigType = SprdXmlConfigType.Generic;

            // Attempt to extract all useful information
            ParseFileList(root, config);
            ParseFdlConfig(root, config);
            ParsePartitionOperations(root, config);

            // Store raw XML for later analysis
            config.RawXml = root.ToString();
        }

        #endregion

        #region Helper methods

        private string CleanXmlContent(byte[] data)
        {
            // Skip BOM
            int start = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                start = 3;
            }
            else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                // UTF-16 LE BOM
                return System.Text.Encoding.Unicode.GetString(data, 2, data.Length - 2)
                    .Replace("\0", "");
            }
            else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                // UTF-16 BE BOM
                return System.Text.Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2)
                    .Replace("\0", "");
            }

            string content = System.Text.Encoding.UTF8.GetString(data, start, data.Length - start);

            // Remove null characters
            content = content.Replace("\0", "");

            // Remove invalid XML characters
            content = RemoveInvalidXmlChars(content);

            return content;
        }

        private string RemoveInvalidXmlChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (XmlConvert.IsXmlChar(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private string GetAttributeOrElement(XElement element, string name)
        {
            // Check attribute first
            var attr = element.Attribute(name);
            if (attr != null)
                return attr.Value;

            // Then check child element
            var child = element.Element(name);
            if (child != null)
                return child.Value;

            // Check again case-insensitively
            attr = element.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attr != null)
                return attr.Value;

            child = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return child?.Value;
        }

        private ulong ParseHexOrDecimal(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            value = value.Trim();

            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToUInt64(value.Substring(2), 16);
                }
                else if (value.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    // Try to parse as decimal, if it fails then try hexadecimal
                    if (ulong.TryParse(value, out ulong dec))
                        return dec;
                    return Convert.ToUInt64(value, 16);
                }
                return ulong.Parse(value);
            }
            catch
            {
                return 0;
            }
        }

        private int ParseInt(string value, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            if (int.TryParse(value, out int result))
                return result;

            return defaultValue;
        }

        private bool ParseBool(string value, bool defaultValue = false)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            value = value.Trim().ToLower();
            return value == "1" || value == "true" || value == "yes" || value == "on";
        }

        private SprdXmlFileType ParseFileType(string type)
        {
            if (string.IsNullOrEmpty(type))
                return SprdXmlFileType.Unknown;

            type = type.ToLower();

            if (type.Contains("fdl1") || type == "0")
                return SprdXmlFileType.FDL1;
            if (type.Contains("fdl2") || type == "1")
                return SprdXmlFileType.FDL2;
            if (type.Contains("code") || type.Contains("modem"))
                return SprdXmlFileType.Code;
            if (type.Contains("nv"))
                return SprdXmlFileType.NV;
            if (type.Contains("phase"))
                return SprdXmlFileType.PhaseCheck;
            if (type.Contains("user") || type.Contains("data"))
                return SprdXmlFileType.UserData;
            if (type.Contains("boot"))
                return SprdXmlFileType.Boot;
            if (type.Contains("system"))
                return SprdXmlFileType.System;
            if (type.Contains("recovery"))
                return SprdXmlFileType.Recovery;

            return SprdXmlFileType.Unknown;
        }

        private SprdXmlFileFlag ParseFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag))
                return SprdXmlFileFlag.None;

            int flagValue = ParseInt(flag, 0);
            return (SprdXmlFileFlag)flagValue;
        }

        private SprdXmlCheckFlag ParseCheckFlag(string checkFlag)
        {
            if (string.IsNullOrEmpty(checkFlag))
                return SprdXmlCheckFlag.None;

            int checkValue = ParseInt(checkFlag, 0);
            return (SprdXmlCheckFlag)checkValue;
        }

        private SprdOperationType ParseOperationType(string op)
        {
            if (string.IsNullOrEmpty(op))
                return SprdOperationType.Write;

            op = op.ToLower();

            if (op.Contains("erase"))
                return SprdOperationType.Erase;
            if (op.Contains("read") || op.Contains("backup"))
                return SprdOperationType.Read;
            if (op.Contains("verify"))
                return SprdOperationType.Verify;
            if (op.Contains("format"))
                return SprdOperationType.Format;

            return SprdOperationType.Write;
        }

        private string FormatSize(ulong size)
        {
            if (size >= 1024 * 1024 * 1024)
                return string.Format("{0:F1} GB", size / (1024.0 * 1024 * 1024));
            if (size >= 1024 * 1024)
                return string.Format("{0:F1} MB", size / (1024.0 * 1024));
            if (size >= 1024)
                return string.Format("{0:F1} KB", size / 1024.0);
            return string.Format("{0} B", size);
        }

        private void Log(string format, params object[] args)
        {
            _log?.Invoke(string.Format(format, args));
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// XML configuration type
    /// </summary>
    public enum SprdXmlConfigType
    {
        Unknown,
        BmaConfig,          // BMA flashing configuration
        PartitionTable,     // Partition table configuration
        NvConfig,           // NV configuration
        ProductionConfig,   // Production configuration
        Generic             // Generic configuration
    }

    /// <summary>
    /// File type
    /// </summary>
    public enum SprdXmlFileType
    {
        Unknown,
        FDL1,
        FDL2,
        Code,
        NV,
        PhaseCheck,
        UserData,
        Boot,
        System,
        Recovery,
        Bootloader,
        Modem
    }

    /// <summary>
    /// File flags
    /// </summary>
    [Flags]
    public enum SprdXmlFileFlag
    {
        None = 0,
        OmitIfNotExist = 1,
        EnableNvBackup = 2,
        EraseFirst = 4,
        VerifyAfterWrite = 8
    }

    /// <summary>
    /// Check flags
    /// </summary>
    [Flags]
    public enum SprdXmlCheckFlag
    {
        None = 0,
        Crc = 1,
        Md5 = 2,
        Sha1 = 4,
        Sha256 = 8
    }

    /// <summary>
    /// Operation type
    /// </summary>
    public enum SprdOperationType
    {
        Write,
        Erase,
        Read,
        Verify,
        Format
    }

    /// <summary>
    /// XML configuration information
    /// </summary>
    public class SprdXmlConfig
    {
        public SprdXmlConfigType ConfigType { get; set; }
        public string ProductName { get; set; }
        public string Version { get; set; }
        public string SchemeName { get; set; }
        public string RawXml { get; set; }

        // FDL configuration
        public SprdFdlConfig Fdl1Config { get; set; }
        public SprdFdlConfig Fdl2Config { get; set; }

        // File list
        public List<SprdXmlFileInfo> Files { get; set; } = new List<SprdXmlFileInfo>();

        // Partition table
        public List<SprdXmlPartitionInfo> PartitionTable { get; set; } = new List<SprdXmlPartitionInfo>();

        // Partition operations
        public List<SprdPartitionOperation> PartitionOperations { get; set; } = new List<SprdPartitionOperation>();

        // Erase configuration
        public SprdEraseConfig EraseConfig { get; set; }

        // NV operations
        public List<SprdNvOperation> NvOperations { get; set; } = new List<SprdNvOperation>();

        // Production settings
        public Dictionary<string, string> ProductionSettings { get; set; }

        /// <summary>
        /// Get flash order sorted by priority
        /// </summary>
        public List<SprdXmlFileInfo> GetFlashOrder()
        {
            // FDL1 -> FDL2 -> Other files
            var order = new List<SprdXmlFileInfo>();

            // Add FDL1
            var fdl1 = Files.FirstOrDefault(f => f.Type == SprdXmlFileType.FDL1);
            if (fdl1 != null) order.Add(fdl1);

            // Add FDL2
            var fdl2 = Files.FirstOrDefault(f => f.Type == SprdXmlFileType.FDL2);
            if (fdl2 != null) order.Add(fdl2);

            // Add other selected files
            foreach (var file in Files)
            {
                if (file.Type != SprdXmlFileType.FDL1 &&
                    file.Type != SprdXmlFileType.FDL2 &&
                    file.IsSelected)
                {
                    order.Add(file);
                }
            }

            return order;
        }
    }

    /// <summary>
    /// FDL configuration
    /// </summary>
    public class SprdFdlConfig
    {
        public string FileName { get; set; }
        public ulong Address { get; set; }
        public int BaudRate { get; set; }
    }

    /// <summary>
    /// XML file information
    /// </summary>
    public class SprdXmlFileInfo
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public SprdXmlFileType Type { get; set; }
        public string Block { get; set; }
        public ulong Address { get; set; }
        public ulong Size { get; set; }
        public SprdXmlFileFlag Flag { get; set; }
        public SprdXmlCheckFlag CheckFlag { get; set; }
        public bool IsSelected { get; set; } = true;
    }

    /// <summary>
    /// XML partition information
    /// </summary>
    public class SprdXmlPartitionInfo
    {
        public string Name { get; set; }
        public ulong Size { get; set; }
        public ulong Offset { get; set; }
        public string Type { get; set; }
        public string FileSystem { get; set; }
        public bool IsReadOnly { get; set; }
    }

    /// <summary>
    /// Partition operation
    /// </summary>
    public class SprdPartitionOperation
    {
        public string PartitionName { get; set; }
        public SprdOperationType Operation { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Erase configuration
    /// </summary>
    public class SprdEraseConfig
    {
        public bool EraseAll { get; set; }
        public bool EraseUserData { get; set; }
        public bool EraseNv { get; set; }
        public bool FormatFlash { get; set; }
        public List<string> ExcludePartitions { get; set; } = new List<string>();
    }

    /// <summary>
    /// NV operation
    /// </summary>
    public class SprdNvOperation
    {
        public ushort ItemId { get; set; }
        public string Name { get; set; }
        public string Operation { get; set; }
        public string Category { get; set; }
        public string BackupFile { get; set; }
        public string RestoreFile { get; set; }
    }

    #endregion
}
