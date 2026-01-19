using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LoveAlways.Fastboot.Common
{
    /// <summary>
    /// Fastboot 刷机脚本解析器
    /// 支持解析 flash_all.bat 等脚本文件
    /// </summary>
    public class BatScriptParser
    {
        private readonly Action<string> _log;
        private string _baseDir;

        public BatScriptParser(string baseDir, Action<string> log = null)
        {
            _baseDir = baseDir;
            _log = log ?? (msg => { });
        }

        /// <summary>
        /// 刷机任务
        /// </summary>
        public class FlashTask
        {
            /// <summary>
            /// 分区名称
            /// </summary>
            public string PartitionName { get; set; }

            /// <summary>
            /// 镜像文件路径（相对或绝对）
            /// </summary>
            public string ImagePath { get; set; }

            /// <summary>
            /// 镜像文件名
            /// </summary>
            public string ImageFileName => Path.GetFileName(ImagePath ?? "");

            /// <summary>
            /// 操作类型 (flash/erase/set_active/reboot)
            /// </summary>
            public string Operation { get; set; } = "flash";

            /// <summary>
            /// 文件大小
            /// </summary>
            public long FileSize { get; set; }

            /// <summary>
            /// 文件大小格式化显示
            /// </summary>
            public string FileSizeFormatted
            {
                get
                {
                    if (FileSize <= 0) return "-";
                    if (FileSize >= 1024L * 1024 * 1024)
                        return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
                    if (FileSize >= 1024 * 1024)
                        return $"{FileSize / (1024.0 * 1024):F2} MB";
                    if (FileSize >= 1024)
                        return $"{FileSize / 1024.0:F2} KB";
                    return $"{FileSize} B";
                }
            }

            /// <summary>
            /// 是否存在镜像文件
            /// </summary>
            public bool ImageExists { get; set; }

            /// <summary>
            /// 额外参数（如 --disable-verity）
            /// </summary>
            public string ExtraArgs { get; set; }

            /// <summary>
            /// 原始命令行
            /// </summary>
            public string RawCommand { get; set; }

            /// <summary>
            /// 行号
            /// </summary>
            public int LineNumber { get; set; }

            public override string ToString()
            {
                return $"{Operation} {PartitionName} -> {ImageFileName}";
            }
        }

        /// <summary>
        /// 解析 bat 脚本文件
        /// </summary>
        public List<FlashTask> ParseBatScript(string batPath)
        {
            var tasks = new List<FlashTask>();

            if (!File.Exists(batPath))
            {
                _log($"[BatParser] 脚本文件不存在: {batPath}");
                return tasks;
            }

            _baseDir = Path.GetDirectoryName(batPath);
            string[] lines = File.ReadAllLines(batPath);

            // 正则表达式匹配 fastboot 命令
            // 支持格式:
            // fastboot %* flash partition_name path/to/file.img
            // fastboot %* flash partition_ab path/to/file.img
            // fastboot %* erase partition_name
            // fastboot %* set_active a
            // fastboot %* reboot

            // flash 命令正则
            var flashRegex = new Regex(
                @"fastboot\s+%\*\s+flash\s+(\S+)\s+(%~dp0)?([^\s|&]+)",
                RegexOptions.IgnoreCase);

            // erase 命令正则
            var eraseRegex = new Regex(
                @"fastboot\s+%\*\s+erase\s+(\S+)",
                RegexOptions.IgnoreCase);

            // set_active 命令正则
            var setActiveRegex = new Regex(
                @"fastboot\s+%\*\s+set_active\s+(\S+)",
                RegexOptions.IgnoreCase);

            // reboot 命令正则
            var rebootRegex = new Regex(
                @"fastboot\s+%\*\s+reboot(?:\s+(\S+))?",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // 跳过注释和空行
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("::") || line.StartsWith("REM "))
                    continue;

                // 解析 flash 命令
                var flashMatch = flashRegex.Match(line);
                if (flashMatch.Success)
                {
                    string partition = flashMatch.Groups[1].Value;
                    string imagePath = flashMatch.Groups[3].Value;

                    // 处理路径
                    imagePath = NormalizePath(imagePath);
                    string fullPath = ResolveFullPath(imagePath);

                    var task = new FlashTask
                    {
                        PartitionName = partition,
                        ImagePath = fullPath,
                        Operation = "flash",
                        RawCommand = line,
                        LineNumber = i + 1
                    };

                    // 检查文件是否存在并获取大小
                    if (File.Exists(fullPath))
                    {
                        task.ImageExists = true;
                        task.FileSize = new FileInfo(fullPath).Length;
                    }
                    else
                    {
                        task.ImageExists = false;
                    }

                    tasks.Add(task);
                    continue;
                }

                // 解析 erase 命令
                var eraseMatch = eraseRegex.Match(line);
                if (eraseMatch.Success)
                {
                    string partition = eraseMatch.Groups[1].Value;

                    tasks.Add(new FlashTask
                    {
                        PartitionName = partition,
                        Operation = "erase",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }

                // 解析 set_active 命令
                var setActiveMatch = setActiveRegex.Match(line);
                if (setActiveMatch.Success)
                {
                    string slot = setActiveMatch.Groups[1].Value;

                    tasks.Add(new FlashTask
                    {
                        PartitionName = $"slot_{slot}",
                        Operation = "set_active",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }

                // 解析 reboot 命令
                var rebootMatch = rebootRegex.Match(line);
                if (rebootMatch.Success)
                {
                    string target = rebootMatch.Groups[1].Success ? rebootMatch.Groups[1].Value : "system";

                    tasks.Add(new FlashTask
                    {
                        PartitionName = $"reboot_{target}",
                        Operation = "reboot",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }
            }

            _log($"[BatParser] 解析完成: {tasks.Count} 个任务");
            return tasks;
        }

        /// <summary>
        /// 解析 sh 脚本文件 (Linux 格式)
        /// </summary>
        public List<FlashTask> ParseShScript(string shPath)
        {
            var tasks = new List<FlashTask>();

            if (!File.Exists(shPath))
            {
                _log($"[BatParser] 脚本文件不存在: {shPath}");
                return tasks;
            }

            _baseDir = Path.GetDirectoryName(shPath);
            string[] lines = File.ReadAllLines(shPath);

            // sh 脚本格式:
            // fastboot $* flash partition_name "$DIR/images/file.img"
            // fastboot $* flash partition_name $DIR/images/file.img

            var flashRegex = new Regex(
                @"fastboot\s+\$\*?\s+flash\s+(\S+)\s+[""']?\$DIR/([^""'\s|&]+)[""']?",
                RegexOptions.IgnoreCase);

            var eraseRegex = new Regex(
                @"fastboot\s+\$\*?\s+erase\s+(\S+)",
                RegexOptions.IgnoreCase);

            var setActiveRegex = new Regex(
                @"fastboot\s+\$\*?\s+set_active\s+(\S+)",
                RegexOptions.IgnoreCase);

            var rebootRegex = new Regex(
                @"fastboot\s+\$\*?\s+reboot(?:\s+(\S+))?",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var flashMatch = flashRegex.Match(line);
                if (flashMatch.Success)
                {
                    string partition = flashMatch.Groups[1].Value;
                    string imagePath = flashMatch.Groups[2].Value;

                    string fullPath = Path.Combine(_baseDir, imagePath.Replace("/", "\\"));

                    var task = new FlashTask
                    {
                        PartitionName = partition,
                        ImagePath = fullPath,
                        Operation = "flash",
                        RawCommand = line,
                        LineNumber = i + 1
                    };

                    if (File.Exists(fullPath))
                    {
                        task.ImageExists = true;
                        task.FileSize = new FileInfo(fullPath).Length;
                    }

                    tasks.Add(task);
                    continue;
                }

                var eraseMatch = eraseRegex.Match(line);
                if (eraseMatch.Success)
                {
                    tasks.Add(new FlashTask
                    {
                        PartitionName = eraseMatch.Groups[1].Value,
                        Operation = "erase",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }

                var setActiveMatch = setActiveRegex.Match(line);
                if (setActiveMatch.Success)
                {
                    tasks.Add(new FlashTask
                    {
                        PartitionName = $"slot_{setActiveMatch.Groups[1].Value}",
                        Operation = "set_active",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }

                var rebootMatch = rebootRegex.Match(line);
                if (rebootMatch.Success)
                {
                    string target = rebootMatch.Groups[1].Success ? rebootMatch.Groups[1].Value : "system";
                    tasks.Add(new FlashTask
                    {
                        PartitionName = $"reboot_{target}",
                        Operation = "reboot",
                        RawCommand = line,
                        LineNumber = i + 1
                    });
                    continue;
                }
            }

            _log($"[BatParser] 解析完成: {tasks.Count} 个任务");
            return tasks;
        }

        /// <summary>
        /// 自动检测并解析脚本文件
        /// </summary>
        public List<FlashTask> ParseScript(string scriptPath)
        {
            string ext = Path.GetExtension(scriptPath).ToLowerInvariant();

            if (ext == ".bat" || ext == ".cmd")
            {
                return ParseBatScript(scriptPath);
            }
            else if (ext == ".sh")
            {
                return ParseShScript(scriptPath);
            }
            else
            {
                // 尝试检测文件内容
                string firstLine = "";
                try
                {
                    using (var sr = new StreamReader(scriptPath))
                    {
                        firstLine = sr.ReadLine() ?? "";
                    }
                }
                catch { }

                if (firstLine.StartsWith("#!/"))
                {
                    return ParseShScript(scriptPath);
                }
                else
                {
                    return ParseBatScript(scriptPath);
                }
            }
        }

        /// <summary>
        /// 标准化路径
        /// </summary>
        private string NormalizePath(string path)
        {
            // 移除引号
            path = path.Trim('"', '\'');
            
            // 将正斜杠转换为反斜杠
            path = path.Replace("/", "\\");

            return path;
        }

        /// <summary>
        /// 解析完整路径
        /// </summary>
        private string ResolveFullPath(string relativePath)
        {
            // 如果已经是绝对路径，直接返回
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            // 移除可能的 images\ 或 images/ 前缀，因为某些脚本可能已经包含
            relativePath = relativePath.TrimStart('\\', '/');

            // 组合基础目录
            return Path.Combine(_baseDir, relativePath);
        }

        /// <summary>
        /// 扫描目录查找刷机脚本
        /// </summary>
        public static List<string> FindFlashScripts(string directory)
        {
            var scripts = new List<string>();

            if (!Directory.Exists(directory))
                return scripts;

            // 常见的刷机脚本名称
            string[] scriptNames = new[]
            {
                "flash_all.bat",
                "flash_all.sh",
                "flash_all_lock.bat",
                "flash_all_lock.sh",
                "flash_all_except_storage.bat",
                "flash_all_except_storage.sh",
                "flash.bat",
                "flash.sh"
            };

            foreach (var name in scriptNames)
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    scripts.Add(path);
                }
            }

            return scripts;
        }

        /// <summary>
        /// 获取脚本类型描述
        /// </summary>
        public static string GetScriptDescription(string scriptPath)
        {
            string fileName = Path.GetFileName(scriptPath).ToLowerInvariant();

            if (fileName.Contains("except_storage"))
                return "完整刷机 (保留数据)";
            else if (fileName.Contains("lock"))
                return "完整刷机 + 锁定BL";
            else if (fileName.Contains("flash_all"))
                return "完整刷机 (清除数据)";
            else
                return "刷机脚本";
        }
    }
}
