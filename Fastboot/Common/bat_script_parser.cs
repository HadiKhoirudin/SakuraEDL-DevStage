
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LoveAlways.Fastboot.Common
{
    /// <summary>
    /// Fastboot Flashing Script Parser
    /// Supports parsing script files such as flash_all.bat
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
        /// Flashing Task
        /// </summary>
        public class FlashTask
        {
            /// <summary>
            /// Partition name
            /// </summary>
            public string PartitionName { get; set; }

            /// <summary>
            /// Image file path (relative or absolute)
            /// </summary>
            public string ImagePath { get; set; }

            /// <summary>
            /// Image filename
            /// </summary>
            public string ImageFileName => Path.GetFileName(ImagePath ?? "");

            /// <summary>
            /// Operation type (flash/erase/set_active/reboot)
            /// </summary>
            public string Operation { get; set; } = "flash";

            /// <summary>
            /// File size
            /// </summary>
            public long FileSize { get; set; }

            /// <summary>
            /// Formatted file size display
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
            /// Whether the image file exists
            /// </summary>
            public bool ImageExists { get; set; }

            /// <summary>
            /// Extra arguments (e.g., --disable-verity)
            /// </summary>
            public string ExtraArgs { get; set; }

            /// <summary>
            /// Raw command line
            /// </summary>
            public string RawCommand { get; set; }

            /// <summary>
            /// Line number
            /// </summary>
            public int LineNumber { get; set; }

            public override string ToString()
            {
                return $"{Operation} {PartitionName} -> {ImageFileName}";
            }
        }

        /// <summary>
        /// Parse bat script file
        /// </summary>
        public List<FlashTask> ParseBatScript(string batPath)
        {
            var tasks = new List<FlashTask>();

            if (!File.Exists(batPath))
            {
                _log($"[BatParser] Script file does not exist: {batPath}");
                return tasks;
            }

            _baseDir = Path.GetDirectoryName(batPath);
            string[] lines = File.ReadAllLines(batPath);

            // Regex matching fastboot commands
            // Supported formats:
            // fastboot %* flash partition_name path/to/file.img
            // fastboot %* flash partition_ab path/to/file.img
            // fastboot %* erase partition_name
            // fastboot %* set_active a
            // fastboot %* reboot

            // flash command regex
            var flashRegex = new Regex(
                @"fastboot\s+%\*\s+flash\s+(\S+)\s+(%~dp0)?([^\s|&]+)",
                RegexOptions.IgnoreCase);

            // erase command regex
            var eraseRegex = new Regex(
                @"fastboot\s+%\*\s+erase\s+(\S+)",
                RegexOptions.IgnoreCase);

            // set_active command regex
            var setActiveRegex = new Regex(
                @"fastboot\s+%\*\s+set_active\s+(\S+)",
                RegexOptions.IgnoreCase);

            // reboot command regex
            var rebootRegex = new Regex(
                @"fastboot\s+%\*\s+reboot(?:\s+(\S+))?",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("::") || line.StartsWith("REM "))
                    continue;

                // Parse flash command
                var flashMatch = flashRegex.Match(line);
                if (flashMatch.Success)
                {
                    string partition = flashMatch.Groups[1].Value;
                    string imagePath = flashMatch.Groups[3].Value;

                    // Handle path
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

                    // Check if file exists and get size
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

                // Parse erase command
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

                // Parse set_active command
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

                // Parse reboot command
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

            _log($"[BatParser] Parse complete: {tasks.Count} tasks");
            return tasks;
        }

        /// <summary>
        /// Parse sh script file (Linux format)
        /// </summary>
        public List<FlashTask> ParseShScript(string shPath)
        {
            var tasks = new List<FlashTask>();

            if (!File.Exists(shPath))
            {
                _log($"[BatParser] Script file does not exist: {shPath}");
                return tasks;
            }

            _baseDir = Path.GetDirectoryName(shPath);
            string[] lines = File.ReadAllLines(shPath);

            // sh script format:
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

            _log($"[BatParser] Parse complete: {tasks.Count} tasks");
            return tasks;
        }

        /// <summary>
        /// Automatically detect and parse script file
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
                // Try to detect file content
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
        /// Normalize path
        /// </summary>
        private string NormalizePath(string path)
        {
            // Remove quotes
            path = path.Trim('"', '\'');
            
            // Convert forward slashes to backward slashes
            path = path.Replace("/", "\\");

            return path;
        }

        /// <summary>
        /// Resolve full path
        /// </summary>
        private string ResolveFullPath(string relativePath)
        {
            // If already an absolute path, return directly            if (Path.IsPathRooted(relativePath))
                return relativePath;

            // Remove possible images\ or images/ prefix, as some scripts may already contain them
            relativePath = relativePath.TrimStart('\\', '/');

            // Combine base directory
            return Path.Combine(_baseDir, relativePath);
        }

        /// <summary>
        /// Scan directory for flashing scripts
        /// </summary>
        public static List<string> FindFlashScripts(string directory)
        {
            var scripts = new List<string>();

            if (!Directory.Exists(directory))
                return scripts;

            // Common flashing script names
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
        /// Get script type description
        /// </summary>
        public static string GetScriptDescription(string scriptPath)
        {
            string fileName = Path.GetFileName(scriptPath).ToLowerInvariant();

            if (fileName.Contains("except_storage"))
                return "Full Flash (Keep Data)";
            else if (fileName.Contains("lock"))
                return "Full Flash + Lock BL";
            else if (fileName.Contains("flash_all"))
                return "Full Flash (Wipe Data)";
            else
                return "Flashing Script";
        }
    }
}
