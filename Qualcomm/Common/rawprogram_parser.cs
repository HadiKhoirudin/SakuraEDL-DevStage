// ============================================================================
// LoveAlways - Rawprogram XML Parser (optimized version)
// Supports: rawprogram*.xml, patch*.xml, erase, zeroout, negative sector, slot-aware
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LoveAlways.Qualcomm.Common
{
    public class FlashTask
    {
        public string Label { get; set; }
        public string Filename { get; set; }
        public string FilePath { get; set; }
        public int Lun { get; set; }
        public long StartSector { get; set; }
        public long NumSectors { get; set; }
        public int SectorSize { get; set; }
        public long FileOffset { get; set; }        // file_sector_offset * SectorSize
        public long FileSectorOffset { get; set; }  // File offset in sectors
        public bool IsSparse { get; set; }
        public bool ReadBackVerify { get; set; }
        public TaskType Type { get; set; }
        public string PartiGuid { get; set; }       // Partition GUID
        public int Priority { get; set; }           // Write priority (smaller is earlier)

        public FlashTask()
        {
            Label = "";
            Filename = "";
            FilePath = "";
            PartiGuid = "";
            SectorSize = 4096;
            Type = TaskType.Program;
            Priority = 100;
        }

        public long Size { get { return NumSectors * SectorSize; } }
        public long ActualFileSize { get; set; }    // Actual file size (for Sparse)

        public string FormattedSize
        {
            get
            {
                long size = ActualFileSize > 0 ? ActualFileSize : Size;
                if (size >= 1024L * 1024 * 1024) return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
                if (size >= 1024 * 1024) return string.Format("{0:F2} MB", size / (1024.0 * 1024));
                if (size >= 1024) return string.Format("{0:F0} KB", size / 1024.0);
                return string.Format("{0} B", size);
            }
        }

        /// <summary>
        /// Whether negative sector resolution is needed
        /// </summary>
        public bool NeedsNegativeSectorResolve { get { return StartSector < 0; } }
    }

    public enum TaskType { Program, Patch, Erase, Zeroout }

    public class PatchEntry
    {
        public int Lun { get; set; }
        public long StartSector { get; set; }
        public int ByteOffset { get; set; }
        public int SizeInBytes { get; set; }
        public string Value { get; set; }
        public string What { get; set; }
        public string Filename { get; set; }

        public PatchEntry()
        {
            Value = "";
            What = "";
            Filename = "";
        }

        /// <summary>
        /// Whether negative sector resolution is needed
        /// </summary>
        public bool NeedsNegativeSectorResolve { get { return StartSector < 0; } }
    }

    public class FlashPackageInfo
    {
        public string PackagePath { get; set; }
        public List<FlashTask> Tasks { get; set; }
        public List<string> RawprogramFiles { get; set; }
        public List<string> PatchFiles { get; set; }
        public List<PatchEntry> PatchEntries { get; set; }
        public string ProgrammerPath { get; set; }
        public string DigestPath { get; set; }      // VIP Digest file
        public string SignaturePath { get; set; }   // VIP Signature file
        public int MaxLun { get; set; }
        public string DetectedSlot { get; set; }    // Detected slot (a/b)

        public FlashPackageInfo()
        {
            PackagePath = "";
            Tasks = new List<FlashTask>();
            RawprogramFiles = new List<string>();
            PatchFiles = new List<string>();
            PatchEntries = new List<PatchEntry>();
            ProgrammerPath = "";
            DigestPath = "";
            SignaturePath = "";
            DetectedSlot = "";
        }

        public int TotalTasks { get { return Tasks.Count; } }
        public long TotalSize { get { return Tasks.Sum(t => t.ActualFileSize > 0 ? t.ActualFileSize : t.Size); } }
        public bool HasPatches { get { return PatchFiles.Count > 0 || PatchEntries.Count > 0; } }
        public bool HasVipAuth { get { return !string.IsNullOrEmpty(DigestPath) && !string.IsNullOrEmpty(SignaturePath); } }

        /// <summary>
        /// Sort tasks by priority (GPT first, then by LUN + StartSector)
        /// </summary>
        public List<FlashTask> GetSortedTasks()
        {
            return Tasks.OrderBy(t => t.Priority)
                        .ThenBy(t => t.Lun)
                        .ThenBy(t => t.StartSector)
                        .ToList();
        }
    }

    public class RawprogramParser
    {
        private readonly Action<string> _log;
        private readonly string _basePath;
        private Dictionary<string, string> _fileCache;  // Filename -> Full path cache

        private static readonly HashSet<string> SensitivePartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ssd", "persist", "frp", "config", "limits", "modemst1", "modemst2", "fsc", "fsg",
            "devinfo", "secdata", "splash", "xbl", "xbl_config", "abl", "hyp", "tz", "rpm", "pmic",
            "keymaster", "cmnlib", "cmnlib64", "devcfg", "qupfw", "uefisecapp", "apdp", "msadp", "dip", "storsec"
        };

        // GPT related partitions (need to be written first)
        private static readonly HashSet<string> GptPartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PrimaryGPT", "BackupGPT", "gpt_main0", "gpt_main1", "gpt_main2", "gpt_main3", "gpt_main4", "gpt_main5",
            "gpt_backup0", "gpt_backup1", "gpt_backup2", "gpt_backup3", "gpt_backup4", "gpt_backup5"
        };

        public RawprogramParser(string basePath, Action<string> log = null)
        {
            _basePath = basePath;
            _log = log ?? delegate { };
            _fileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public FlashPackageInfo LoadPackage()
        {
            var info = new FlashPackageInfo { PackagePath = _basePath };

            // Pre-build file cache
            BuildFileCache();

            // Find rawprogram files (supports multiple naming conventions)
            var rawprogramFiles = FindRawprogramFiles();

            if (rawprogramFiles.Count == 0)
            {
                _log("[RawprogramParser] rawprogram*.xml file not found");
                return info;
            }

            info.RawprogramFiles.AddRange(rawprogramFiles);
            info.ProgrammerPath = FindProgrammer();

            // Find VIP authentication files
            info.DigestPath = FindVipFile("Digest", "digest");
            info.SignaturePath = FindVipFile("Sign", "signature", "Signature");

            // Parse all rawprogram files
            foreach (var file in rawprogramFiles)
            {
                _log(string.Format("[RawprogramParser] Parsing: {0}", Path.GetFileName(file)));
                var tasks = ParseRawprogramXml(file);

                foreach (var task in tasks)
                {
                    // Deduplicate (by LUN + StartSector + Label)
                    if (!info.Tasks.Any(t => t.Lun == task.Lun && t.StartSector == task.StartSector && t.Label == task.Label))
                    {
                        // Set priority
                        if (GptPartitions.Contains(task.Label) || task.Label.StartsWith("gpt_"))
                        {
                            task.Priority = task.Label.Contains("Primary") || task.Label.Contains("main") ? 1 : 2;
                        }
                        else if (task.Label.Equals("xbl", StringComparison.OrdinalIgnoreCase) ||
                                 task.Label.Equals("abl", StringComparison.OrdinalIgnoreCase))
                        {
                            task.Priority = 10;
                        }

                        info.Tasks.Add(task);
                    }
                }
            }

            // Detect slot
            info.DetectedSlot = DetectSlotFromTasks(info.Tasks);

            // Load patch files
            var patchFiles = Directory.GetFiles(_basePath, "patch*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f).ToList();

            foreach (var file in patchFiles)
            {
                info.PatchFiles.Add(file);
                var patches = ParsePatchXml(file);
                info.PatchEntries.AddRange(patches);
            }

            info.MaxLun = info.Tasks.Count > 0 ? info.Tasks.Max(t => t.Lun) : 0;

            _log(string.Format("[RawprogramParser] Load completed: {0} tasks, {1} patches, slot: {2}",
                info.TotalTasks, info.PatchEntries.Count,
                string.IsNullOrEmpty(info.DetectedSlot) ? "Unknown" : info.DetectedSlot));

            return info;
        }

        // Search depth limit
        private const int MAX_SEARCH_DEPTH = 5;
        private const int MAX_FILES_TO_CACHE = 10000;

        /// <summary>
        /// Pre-build file cache (accelerate search, limit depth)
        /// </summary>
        private void BuildFileCache()
        {
            _fileCache.Clear();
            try
            {
                BuildFileCacheRecursive(_basePath, 0);
            }
            catch { }
        }

        /// <summary>
        /// Recursively build file cache (with depth limit)
        /// </summary>
        private void BuildFileCacheRecursive(string dir, int depth)
        {
            if (depth > MAX_SEARCH_DEPTH || _fileCache.Count >= MAX_FILES_TO_CACHE)
                return;

            try
            {
                // Add files in current directory
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (_fileCache.Count >= MAX_FILES_TO_CACHE)
                        return;

                    string name = Path.GetFileName(file);
                    if (!_fileCache.ContainsKey(name))
                        _fileCache[name] = file;
                }

                // Recursively search subdirectories
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    // Skip hidden directories and common irrelevant directories
                    string dirName = Path.GetFileName(subDir);
                    if (dirName.StartsWith(".") ||
                        dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("__pycache__", StringComparison.OrdinalIgnoreCase))
                        continue;

                    BuildFileCacheRecursive(subDir, depth + 1);
                }
            }
            catch { }
        }

        /// <summary>
        /// Find rawprogram files (supports multiple naming formats)
        /// </summary>
        private List<string> FindRawprogramFiles()
        {
            var files = new List<string>();
            var patterns = new[] { "rawprogram*.xml", "rawprogram_*.xml", "rawprogram?.xml" };

            foreach (var pattern in patterns)
            {
                try
                {
                    var found = Directory.GetFiles(_basePath, pattern, SearchOption.AllDirectories);
                    foreach (var f in found)
                    {
                        if (!files.Contains(f, StringComparer.OrdinalIgnoreCase))
                            files.Add(f);
                    }
                }
                catch { }
            }

            // Sort by LUN number (rawprogram0.xml, rawprogram1.xml, ...)
            return files.OrderBy(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f);
                int num;
                var numStr = new string(name.Where(char.IsDigit).ToArray());
                return int.TryParse(numStr, out num) ? num : 999;
            }).ToList();
        }

        /// <summary>
        /// Find VIP authentication file
        /// </summary>
        private string FindVipFile(params string[] keywords)
        {
            foreach (var kv in _fileCache)
            {
                string name = kv.Key;
                foreach (var keyword in keywords)
                {
                    if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (name.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                        {
                            return kv.Value;
                        }
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Detect slot from task list
        /// </summary>
        private string DetectSlotFromTasks(List<FlashTask> tasks)
        {
            int slotA = 0, slotB = 0;
            foreach (var task in tasks)
            {
                if (task.Label.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
                    task.Filename.Contains("_a."))
                    slotA++;
                else if (task.Label.EndsWith("_b", StringComparison.OrdinalIgnoreCase) ||
                         task.Filename.Contains("_b."))
                    slotB++;
            }

            if (slotA > 0 && slotB == 0) return "a";
            if (slotB > 0 && slotA == 0) return "b";
            if (slotA > slotB) return "a";
            if (slotB > slotA) return "b";
            return "";
        }

        public List<FlashTask> ParseRawprogramXml(string filePath)
        {
            var tasks = new List<FlashTask>();

            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null) return tasks;

                // Parse <program> element
                foreach (var elem in root.Elements("program"))
                {
                    var task = ParseProgramElement(elem, filePath);
                    if (task != null)
                        tasks.Add(task);
                }

                // Parse <erase> element
                foreach (var elem in root.Elements("erase"))
                {
                    var task = ParseEraseElement(elem);
                    if (task != null)
                        tasks.Add(task);
                }

                // Parse <zeroout> element
                foreach (var elem in root.Elements("zeroout"))
                {
                    var task = ParseZerooutElement(elem);
                    if (task != null)
                        tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[RawprogramParser] Parse failed: {0}", ex.Message));
            }

            return tasks;
        }

        /// <summary>
        /// Parse program element
        /// </summary>
        private FlashTask ParseProgramElement(XElement elem, string xmlPath)
        {
            string filename = GetAttr(elem, "filename", "");
            string label = GetAttr(elem, "label", "");

            // Skip virtual filenames starting with 0:
            if (!string.IsNullOrEmpty(filename) && filename.StartsWith("0:"))
                return null;

            // Skip entries with empty filename and no label
            if (string.IsNullOrEmpty(filename) && string.IsNullOrEmpty(label))
                return null;

            var task = new FlashTask
            {
                Type = string.IsNullOrEmpty(filename) ? TaskType.Zeroout : TaskType.Program,
                Label = !string.IsNullOrEmpty(label) ? label : Path.GetFileNameWithoutExtension(filename),
                Filename = filename,
                FilePath = !string.IsNullOrEmpty(filename) ? FindFile(filename, xmlPath) : "",
                Lun = GetIntAttr(elem, "physical_partition_number", 0),
                StartSector = GetLongAttr(elem, "start_sector", 0),
                NumSectors = GetLongAttr(elem, "num_partition_sectors", 0),
                SectorSize = GetIntAttr(elem, "SECTOR_SIZE_IN_BYTES", 4096),
                FileSectorOffset = GetLongAttr(elem, "file_sector_offset", 0),
                IsSparse = GetAttr(elem, "sparse", "false").ToLowerInvariant() == "true",
                ReadBackVerify = GetAttr(elem, "read_back_verify", "false").ToLowerInvariant() == "true",
                PartiGuid = GetAttr(elem, "partofsingleimage", "")
            };

            task.FileOffset = task.FileSectorOffset * task.SectorSize;

            // Calculate actual file size
            if (!string.IsNullOrEmpty(task.FilePath) && File.Exists(task.FilePath))
            {
                try
                {
                    if (SparseStream.IsSparseFile(task.FilePath))
                    {
                        using (var ss = SparseStream.Open(task.FilePath))
                        {
                            task.ActualFileSize = ss.GetRealDataSize();
                            task.IsSparse = true;
                        }
                    }
                    else
                    {
                        task.ActualFileSize = new FileInfo(task.FilePath).Length;
                    }
                }
                catch { }
            }

            // When NumSectors is 0, try other calculation methods
            if (task.NumSectors == 0)
            {
                // Calculate from size_in_KB
                double sizeInKb;
                if (double.TryParse(GetAttr(elem, "size_in_KB", "0"), out sizeInKb) && sizeInKb > 0)
                {
                    task.NumSectors = (long)(sizeInKb * 1024 / task.SectorSize);
                }
                // Calculate from file size
                else if (task.ActualFileSize > 0)
                {
                    task.NumSectors = (task.ActualFileSize + task.SectorSize - 1) / task.SectorSize;
                }
                // GPT default size
                else if (task.Label == "PrimaryGPT" && task.StartSector == 0)
                {
                    task.NumSectors = 6;
                }
            }

            return task;
        }

        /// <summary>
        /// Parse erase element
        /// </summary>
        private FlashTask ParseEraseElement(XElement elem)
        {
            string label = GetAttr(elem, "label", "");
            if (string.IsNullOrEmpty(label))
                label = "erase_" + GetIntAttr(elem, "physical_partition_number", 0);

            return new FlashTask
            {
                Type = TaskType.Erase,
                Label = label,
                Lun = GetIntAttr(elem, "physical_partition_number", 0),
                StartSector = GetLongAttr(elem, "start_sector", 0),
                NumSectors = GetLongAttr(elem, "num_partition_sectors", 0),
                SectorSize = GetIntAttr(elem, "SECTOR_SIZE_IN_BYTES", 4096),
                Priority = 50  // erase medium priority
            };
        }

        /// <summary>
        /// Parse zeroout element
        /// </summary>
        private FlashTask ParseZerooutElement(XElement elem)
        {
            string label = GetAttr(elem, "label", "");
            if (string.IsNullOrEmpty(label))
                label = "zeroout_" + GetIntAttr(elem, "physical_partition_number", 0);

            return new FlashTask
            {
                Type = TaskType.Zeroout,
                Label = label,
                Lun = GetIntAttr(elem, "physical_partition_number", 0),
                StartSector = GetLongAttr(elem, "start_sector", 0),
                NumSectors = GetLongAttr(elem, "num_partition_sectors", 0),
                SectorSize = GetIntAttr(elem, "SECTOR_SIZE_IN_BYTES", 4096),
                Priority = 60
            };
        }

        /// <summary>
        /// Parse Patch XML file
        /// </summary>
        public List<PatchEntry> ParsePatchXml(string filePath)
        {
            var patches = new List<PatchEntry>();

            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null) return patches;

                foreach (var elem in root.Elements("patch"))
                {
                    string what = GetAttr(elem, "what", "");
                    string value = GetAttr(elem, "value", "");

                    // Skip empty patches
                    if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(what))
                        continue;

                    var patch = new PatchEntry
                    {
                        Lun = GetIntAttr(elem, "physical_partition_number", 0),
                        StartSector = GetLongAttr(elem, "start_sector", 0),
                        ByteOffset = GetIntAttr(elem, "byte_offset", 0),
                        SizeInBytes = GetIntAttr(elem, "size_in_bytes", 0),
                        Value = value,
                        What = what,
                        Filename = GetAttr(elem, "filename", "")
                    };

                    patches.Add(patch);
                }

                if (patches.Count > 0)
                    _log(string.Format("[RawprogramParser] {0}: {1} patches", Path.GetFileName(filePath), patches.Count));
            }
            catch (Exception ex)
            {
                _log(string.Format("[RawprogramParser] Parse Patch failed: {0}", ex.Message));
            }

            return patches;
        }

        public string FindProgrammer()
        {
            // Search by priority
            var patterns = new[] {
                "prog_ufs_*.mbn", "prog_ufs_*.elf", "prog_ufs_*.melf",   // UFS
                "prog_emmc_*.mbn", "prog_emmc_*.elf", "prog_emmc_*.melf", // eMMC
                "prog_*.mbn", "prog_*.elf", "prog_*.melf",               // Generic
                "programmer*.mbn", "programmer*.elf", "programmer*.melf",
                "firehose*.mbn", "firehose*.elf", "firehose*.melf",
                "*firehose*.mbn", "*firehose*.elf", "*firehose*.melf"
            };

            foreach (var pattern in patterns)
            {
                try
                {
                    var files = Directory.GetFiles(_basePath, pattern, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        // Prioritize returning DDR version
                        var ddrFile = files.FirstOrDefault(f => f.IndexOf("ddr", StringComparison.OrdinalIgnoreCase) >= 0);
                        return ddrFile ?? files[0];
                    }
                }
                catch { }
            }
            return "";
        }

        private string FindFile(string filename, string xmlPath)
        {
            // 1. Search from cache
            string cached;
            if (_fileCache.TryGetValue(filename, out cached))
                return cached;

            // 2. XML same directory
            string xmlDir = Path.GetDirectoryName(xmlPath);
            string path = Path.Combine(xmlDir, filename);
            if (File.Exists(path))
            {
                _fileCache[filename] = path;
                return path;
            }

            // 3. Base directory
            path = Path.Combine(_basePath, filename);
            if (File.Exists(path))
            {
                _fileCache[filename] = path;
                return path;
            }

            // 4. Slot variant (system_a.img -> system.img)
            if (filename.Contains("_a.") || filename.Contains("_b."))
            {
                string altName = filename.Replace("_a.", ".").Replace("_b.", ".");
                if (_fileCache.TryGetValue(altName, out cached))
                {
                    _fileCache[filename] = cached;
                    return cached;
                }
            }

            // 5. Deep search (already in cache)
            return "";
        }

        public static bool IsSensitivePartition(string name)
        {
            return SensitivePartitions.Contains(name);
        }

        public static List<FlashTask> FilterSensitivePartitions(List<FlashTask> tasks)
        {
            return tasks.Where(t => !SensitivePartitions.Contains(t.Label)).ToList();
        }

        /// <summary>
        /// Get absolute physical offset of partition (bytes)
        /// </summary>
        public static long GetAbsoluteOffset(FlashTask task)
        {
            if (task == null) return -1;
            return task.StartSector * task.SectorSize;
        }

        private static string GetAttr(XElement elem, string name, string defaultValue)
        {
            var attr = elem.Attribute(name);
            return attr != null ? attr.Value : defaultValue;
        }

        private static int GetIntAttr(XElement elem, string name, int defaultValue)
        {
            var attr = elem.Attribute(name);
            if (attr == null) return defaultValue;

            string value = attr.Value;
            int result;

            // Handle hexadecimal
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result) ? result : defaultValue;

            return int.TryParse(value, out result) ? result : defaultValue;
        }

        private static long GetLongAttr(XElement elem, string name, long defaultValue)
        {
            var attr = elem.Attribute(name);
            if (attr == null) return defaultValue;
            string value = attr.Value;
            long result;

            // Handle NUM_DISK_SECTORS-N formula
            if (value.Contains("NUM_DISK_SECTORS"))
            {
                if (value.Contains("-"))
                {
                    string offsetStr = value.Split('-')[1].TrimEnd('.');
                    if (long.TryParse(offsetStr, out result))
                        return -result; // Negative number means counting from the end
                }
                return -1;
            }

            // Handle hexadecimal
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result) ? result : defaultValue;

            // 移除尾随点号 (如 "5.")
            if (value.EndsWith("."))
                value = value.Substring(0, value.Length - 1);

            return long.TryParse(value, out result) ? result : defaultValue;
        }
    }
}
