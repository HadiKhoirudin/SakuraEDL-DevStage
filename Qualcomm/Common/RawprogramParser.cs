// ============================================================================
// LoveAlways - Rawprogram XML 解析器
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
        public long FileOffset { get; set; }
        public bool IsSparse { get; set; }
        public TaskType Type { get; set; }

        public FlashTask()
        {
            Label = "";
            Filename = "";
            FilePath = "";
            SectorSize = 4096;
            Type = TaskType.Program;
        }

        public long Size { get { return NumSectors * SectorSize; } }

        public string FormattedSize
        {
            get
            {
                long size = Size;
                if (size >= 1024L * 1024 * 1024) return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
                if (size >= 1024 * 1024) return string.Format("{0:F2} MB", size / (1024.0 * 1024));
                if (size >= 1024) return string.Format("{0:F2} KB", size / 1024.0);
                return string.Format("{0} B", size);
            }
        }
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
    }

    public class FlashPackageInfo
    {
        public string PackagePath { get; set; }
        public List<FlashTask> Tasks { get; set; }
        public List<string> RawprogramFiles { get; set; }
        public List<string> PatchFiles { get; set; }
        public List<PatchEntry> PatchEntries { get; set; }
        public string ProgrammerPath { get; set; }
        public int MaxLun { get; set; }

        public FlashPackageInfo()
        {
            PackagePath = "";
            Tasks = new List<FlashTask>();
            RawprogramFiles = new List<string>();
            PatchFiles = new List<string>();
            PatchEntries = new List<PatchEntry>();
            ProgrammerPath = "";
        }

        public int TotalTasks { get { return Tasks.Count; } }
        public long TotalSize { get { return Tasks.Sum(t => t.Size); } }
        public bool HasPatches { get { return PatchFiles.Count > 0 || PatchEntries.Count > 0; } }
    }

    public class RawprogramParser
    {
        private readonly Action<string> _log;
        private readonly string _basePath;

        private static readonly HashSet<string> SensitivePartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ssd", "persist", "frp", "config", "limits", "modemst1", "modemst2", "fsc", "fsg",
            "devinfo", "secdata", "splash", "xbl", "xbl_config", "abl", "hyp", "tz", "rpm", "pmic",
            "keymaster", "cmnlib", "cmnlib64", "devcfg", "qupfw", "uefisecapp", "apdp", "msadp", "dip", "storsec"
        };

        public RawprogramParser(string basePath, Action<string> log = null)
        {
            _basePath = basePath;
            _log = log ?? delegate { };
        }

        public FlashPackageInfo LoadPackage()
        {
            var info = new FlashPackageInfo { PackagePath = _basePath };

            var rawprogramFiles = Directory.GetFiles(_basePath, "rawprogram*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f).ToList();

            if (rawprogramFiles.Count == 0)
            {
                _log("[RawprogramParser] 未找到 rawprogram*.xml 文件");
                return info;
            }

            info.RawprogramFiles.AddRange(rawprogramFiles);
            info.ProgrammerPath = FindProgrammer();

            foreach (var file in rawprogramFiles)
            {
                var tasks = ParseRawprogramXml(file);
                info.Tasks.AddRange(tasks);
            }

            // 自动查找并加载 patch 文件
            var patchFiles = Directory.GetFiles(_basePath, "patch*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f).ToList();
            
            foreach (var file in patchFiles)
            {
                info.PatchFiles.Add(file);
                var patches = ParsePatchXml(file);
                info.PatchEntries.AddRange(patches);
            }

            info.MaxLun = info.Tasks.Count > 0 ? info.Tasks.Max(t => t.Lun) : 0;
            _log(string.Format("[RawprogramParser] 加载 {0} 个任务, {1} 个补丁", info.TotalTasks, info.PatchEntries.Count));

            return info;
        }

        public List<FlashTask> ParseRawprogramXml(string filePath)
        {
            var tasks = new List<FlashTask>();

            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null) return tasks;

                foreach (var elem in root.Elements("program"))
                {
                    string filename = GetAttr(elem, "filename", "");
                    if (string.IsNullOrWhiteSpace(filename) || filename.StartsWith("0:")) continue;

                    var task = new FlashTask
                    {
                        Type = TaskType.Program,
                        Label = GetAttr(elem, "label", Path.GetFileNameWithoutExtension(filename)),
                        Filename = filename,
                        FilePath = FindFile(filename, filePath),
                        Lun = GetIntAttr(elem, "physical_partition_number", 0),
                        StartSector = GetLongAttr(elem, "start_sector", 0),
                        NumSectors = GetLongAttr(elem, "num_partition_sectors", 0),
                        SectorSize = GetIntAttr(elem, "SECTOR_SIZE_IN_BYTES", 4096),
                        IsSparse = GetAttr(elem, "sparse", "false").ToLowerInvariant() == "true"
                    };

                    tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[RawprogramParser] 解析失败: {0}", ex.Message));
            }

            return tasks;
        }

        /// <summary>
        /// 解析 Patch XML 文件
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
                    
                    // 跳过空补丁
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
                
                _log(string.Format("[RawprogramParser] 从 {0} 解析 {1} 个补丁", Path.GetFileName(filePath), patches.Count));
            }
            catch (Exception ex)
            {
                _log(string.Format("[RawprogramParser] 解析 Patch 失败: {0}", ex.Message));
            }

            return patches;
        }

        public string FindProgrammer()
        {
            var patterns = new[] { "prog_*.mbn", "prog_*.elf", "programmer*.mbn", "firehose*.mbn" };
            foreach (var pattern in patterns)
            {
                var files = Directory.GetFiles(_basePath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            return "";
        }

        private string FindFile(string filename, string xmlPath)
        {
            string xmlDir = Path.GetDirectoryName(xmlPath);
            string path = Path.Combine(xmlDir, filename);
            if (File.Exists(path)) return path;

            path = Path.Combine(_basePath, filename);
            if (File.Exists(path)) return path;

            var files = Directory.GetFiles(_basePath, filename, SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];

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

        private static string GetAttr(XElement elem, string name, string defaultValue)
        {
            var attr = elem.Attribute(name);
            return attr != null ? attr.Value : defaultValue;
        }

        private static int GetIntAttr(XElement elem, string name, int defaultValue)
        {
            var attr = elem.Attribute(name);
            if (attr == null) return defaultValue;
            int result;
            return int.TryParse(attr.Value, out result) ? result : defaultValue;
        }

        private static long GetLongAttr(XElement elem, string name, long defaultValue)
        {
            var attr = elem.Attribute(name);
            if (attr == null) return defaultValue;
            string value = attr.Value;
            long result;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result) ? result : defaultValue;
            return long.TryParse(value, out result) ? result : defaultValue;
        }
    }
}
