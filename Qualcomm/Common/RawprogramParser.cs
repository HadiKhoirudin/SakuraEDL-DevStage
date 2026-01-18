// ============================================================================
// LoveAlways - Rawprogram XML 解析器 (优化版)
// 支持: rawprogram*.xml, patch*.xml, erase, zeroout, 负扇区, 槽位感知
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
        public long FileSectorOffset { get; set; }  // 文件偏移扇区数
        public bool IsSparse { get; set; }
        public bool ReadBackVerify { get; set; }
        public TaskType Type { get; set; }
        public string PartiGuid { get; set; }       // 分区 GUID
        public int Priority { get; set; }           // 写入优先级 (越小越先)

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
        public long ActualFileSize { get; set; }    // 实际文件大小 (用于 Sparse)

        public string FormattedSize
        {
            get
            {
                long size = ActualFileSize > 0 ? ActualFileSize : Size;
                if (size >= 1024L * 1024 * 1024) return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
                if (size >= 1024 * 1024) return string.Format("{0:F2} MB", size / (1024.0 * 1024));
                if (size >= 1024) return string.Format("{0:F2} KB", size / 1024.0);
                return string.Format("{0} B", size);
            }
        }

        /// <summary>
        /// 是否需要负扇区解析
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
        /// 是否需要负扇区解析
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
        public string DigestPath { get; set; }      // VIP Digest 文件
        public string SignaturePath { get; set; }   // VIP Signature 文件
        public int MaxLun { get; set; }
        public string DetectedSlot { get; set; }    // 检测到的槽位 (a/b)

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
        /// 按优先级排序任务 (GPT 最先, 然后按 LUN + StartSector)
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
        private Dictionary<string, string> _fileCache;  // 文件名 -> 完整路径 缓存

        private static readonly HashSet<string> SensitivePartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ssd", "persist", "frp", "config", "limits", "modemst1", "modemst2", "fsc", "fsg",
            "devinfo", "secdata", "splash", "xbl", "xbl_config", "abl", "hyp", "tz", "rpm", "pmic",
            "keymaster", "cmnlib", "cmnlib64", "devcfg", "qupfw", "uefisecapp", "apdp", "msadp", "dip", "storsec"
        };

        // GPT 相关分区 (需要优先写入)
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

            // 预建文件缓存
            BuildFileCache();

            // 查找 rawprogram 文件 (支持多种命名)
            var rawprogramFiles = FindRawprogramFiles();

            if (rawprogramFiles.Count == 0)
            {
                _log("[RawprogramParser] 未找到 rawprogram*.xml 文件");
                return info;
            }

            info.RawprogramFiles.AddRange(rawprogramFiles);
            info.ProgrammerPath = FindProgrammer();

            // 查找 VIP 认证文件
            info.DigestPath = FindVipFile("Digest", "digest");
            info.SignaturePath = FindVipFile("Sign", "signature", "Signature");

            // 解析所有 rawprogram 文件
            foreach (var file in rawprogramFiles)
            {
                _log(string.Format("[RawprogramParser] 解析: {0}", Path.GetFileName(file)));
                var tasks = ParseRawprogramXml(file);
                
                foreach (var task in tasks)
                {
                    // 去重 (按 LUN + StartSector + Label)
                    if (!info.Tasks.Any(t => t.Lun == task.Lun && t.StartSector == task.StartSector && t.Label == task.Label))
                    {
                        // 设置优先级
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

            // 检测槽位
            info.DetectedSlot = DetectSlotFromTasks(info.Tasks);

            // 加载 patch 文件
            var patchFiles = Directory.GetFiles(_basePath, "patch*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f).ToList();
            
            foreach (var file in patchFiles)
            {
                info.PatchFiles.Add(file);
                var patches = ParsePatchXml(file);
                info.PatchEntries.AddRange(patches);
            }

            info.MaxLun = info.Tasks.Count > 0 ? info.Tasks.Max(t => t.Lun) : 0;
            
            _log(string.Format("[RawprogramParser] 加载完成: {0} 个任务, {1} 个补丁, 槽位: {2}", 
                info.TotalTasks, info.PatchEntries.Count, 
                string.IsNullOrEmpty(info.DetectedSlot) ? "未知" : info.DetectedSlot));

            return info;
        }

        // 搜索深度限制
        private const int MAX_SEARCH_DEPTH = 5;
        private const int MAX_FILES_TO_CACHE = 10000;

        /// <summary>
        /// 预建文件缓存 (加速查找, 限制深度)
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
        /// 递归构建文件缓存 (带深度限制)
        /// </summary>
        private void BuildFileCacheRecursive(string dir, int depth)
        {
            if (depth > MAX_SEARCH_DEPTH || _fileCache.Count >= MAX_FILES_TO_CACHE)
                return;

            try
            {
                // 添加当前目录的文件
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (_fileCache.Count >= MAX_FILES_TO_CACHE)
                        return;
                        
                    string name = Path.GetFileName(file);
                    if (!_fileCache.ContainsKey(name))
                        _fileCache[name] = file;
                }

                // 递归子目录
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    // 跳过隐藏目录和常见无关目录
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
        /// 查找 rawprogram 文件 (支持多种命名格式)
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

            // 按 LUN 数字排序 (rawprogram0.xml, rawprogram1.xml, ...)
            return files.OrderBy(f => {
                string name = Path.GetFileNameWithoutExtension(f);
                int num;
                var numStr = new string(name.Where(char.IsDigit).ToArray());
                return int.TryParse(numStr, out num) ? num : 999;
            }).ToList();
        }

        /// <summary>
        /// 查找 VIP 认证文件
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
        /// 从任务列表检测槽位
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

                // 解析 <program> 元素
                foreach (var elem in root.Elements("program"))
                {
                    var task = ParseProgramElement(elem, filePath);
                    if (task != null)
                        tasks.Add(task);
                }

                // 解析 <erase> 元素
                foreach (var elem in root.Elements("erase"))
                {
                    var task = ParseEraseElement(elem);
                    if (task != null)
                        tasks.Add(task);
                }

                // 解析 <zeroout> 元素
                foreach (var elem in root.Elements("zeroout"))
                {
                    var task = ParseZerooutElement(elem);
                    if (task != null)
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
        /// 解析 program 元素
        /// </summary>
        private FlashTask ParseProgramElement(XElement elem, string xmlPath)
        {
            string filename = GetAttr(elem, "filename", "");
            string label = GetAttr(elem, "label", "");
            
            // 跳过 0: 开头的虚拟文件名
            if (!string.IsNullOrEmpty(filename) && filename.StartsWith("0:"))
                return null;

            // 跳过空文件名且无 label 的条目
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

            // 计算实际文件大小
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

            // NumSectors 为 0 时尝试其他方式计算
            if (task.NumSectors == 0)
            {
                // 从 size_in_KB 计算
                double sizeInKb;
                if (double.TryParse(GetAttr(elem, "size_in_KB", "0"), out sizeInKb) && sizeInKb > 0)
                {
                    task.NumSectors = (long)(sizeInKb * 1024 / task.SectorSize);
                }
                // 从文件大小计算
                else if (task.ActualFileSize > 0)
                {
                    task.NumSectors = (task.ActualFileSize + task.SectorSize - 1) / task.SectorSize;
                }
                // GPT 默认大小
                else if (task.Label == "PrimaryGPT" && task.StartSector == 0)
                {
                    task.NumSectors = 6;
                }
            }

            return task;
        }

        /// <summary>
        /// 解析 erase 元素
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
                Priority = 50  // erase 中等优先级
            };
        }

        /// <summary>
        /// 解析 zeroout 元素
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
                
                if (patches.Count > 0)
                    _log(string.Format("[RawprogramParser] {0}: {1} 个补丁", Path.GetFileName(filePath), patches.Count));
            }
            catch (Exception ex)
            {
                _log(string.Format("[RawprogramParser] 解析 Patch 失败: {0}", ex.Message));
            }

            return patches;
        }

        public string FindProgrammer()
        {
            // 按优先级搜索
            var patterns = new[] { 
                "prog_ufs_*.mbn", "prog_ufs_*.elf", "prog_ufs_*.melf",   // UFS
                "prog_emmc_*.mbn", "prog_emmc_*.elf", "prog_emmc_*.melf", // eMMC
                "prog_*.mbn", "prog_*.elf", "prog_*.melf",               // 通用
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
                        // 优先返回 DDR 版本
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
            // 1. 从缓存查找
            string cached;
            if (_fileCache.TryGetValue(filename, out cached))
                return cached;

            // 2. XML 同目录
            string xmlDir = Path.GetDirectoryName(xmlPath);
            string path = Path.Combine(xmlDir, filename);
            if (File.Exists(path))
            {
                _fileCache[filename] = path;
                return path;
            }

            // 3. 基础目录
            path = Path.Combine(_basePath, filename);
            if (File.Exists(path))
            {
                _fileCache[filename] = path;
                return path;
            }

            // 4. 槽位变体 (system_a.img -> system.img)
            if (filename.Contains("_a.") || filename.Contains("_b."))
            {
                string altName = filename.Replace("_a.", ".").Replace("_b.", ".");
                if (_fileCache.TryGetValue(altName, out cached))
                {
                    _fileCache[filename] = cached;
                    return cached;
                }
            }

            // 5. 深度搜索 (已在缓存中)
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
        /// 获取分区的绝对物理偏移 (字节)
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
            
            // 处理十六进制
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

            // 处理 NUM_DISK_SECTORS-N 公式
            if (value.Contains("NUM_DISK_SECTORS"))
            {
                if (value.Contains("-"))
                {
                    string offsetStr = value.Split('-')[1].TrimEnd('.');
                    if (long.TryParse(offsetStr, out result))
                        return -result; // 负数表示从末尾倒数
                }
                return -1; 
            }

            // 处理十六进制
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result) ? result : defaultValue;
            
            // 移除尾随点号 (如 "5.")
            if (value.EndsWith("."))
                value = value.Substring(0, value.Length - 1);

            return long.TryParse(value, out result) ? result : defaultValue;
        }
    }
}
