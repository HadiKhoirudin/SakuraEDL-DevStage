using System;
using System.IO;
using System.Reflection;

namespace LoveAlways
{
    /// <summary>
    /// Embedded Resource Extractor - Extracts embedded ADB/Fastboot tools to the execution directory
    /// </summary>
    public static class EmbeddedResourceExtractor
    {
        private static bool _extracted = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// List of resource files to extract
        /// </summary>
        private static readonly string[] EmbeddedFiles = new string[]
        {
            "adb.exe",
            "fastboot.exe",
            "AdbWinApi.dll",
            "AdbWinUsbApi.dll"
        };

        /// <summary>
        /// Extract all embedded tool files to the program directory
        /// </summary>
        public static void ExtractAll()
        {
            if (_extracted) return;

            lock (_lock)
            {
                if (_extracted) return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var assembly = Assembly.GetExecutingAssembly();

                foreach (var fileName in EmbeddedFiles)
                {
                    try
                    {
                        string targetPath = Path.Combine(baseDir, fileName);

                        // 如果文件已存在且不是旧版本，跳过
                        if (File.Exists(targetPath))
                        {
                            // 检查文件是否被锁定或正在使用
                            try
                            {
                                using (var fs = File.Open(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
                                {
                                    // 文件可访问，检查是否需要更新
                                }
                            }
                            catch
                            {
                                // 文件被锁定，跳过
                                continue;
                            }
                        }

                        // 尝试从嵌入式资源提取
                        ExtractResource(assembly, fileName, targetPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Extraction of {fileName} failed: {ex.Message}");
                    }
                }

                _extracted = true;
            }
        }

        /// <summary>
        /// 从嵌入式资源提取单个文件
        /// </summary>
        private static void ExtractResource(Assembly assembly, string fileName, string targetPath)
        {
            // 资源名称格式: {命名空间}.Resources.{文件名}
            string resourceName = $"LoveAlways.Resources.{fileName.Replace("-", "_")}";

            // 尝试不同的资源名称格式
            string[] possibleNames = new string[]
            {
                resourceName,
                $"LoveAlways.{fileName}",
                $"LoveAlways.Tools.{fileName}",
                fileName
            };

            Stream resourceStream = null;
            foreach (var name in possibleNames)
            {
                resourceStream = assembly.GetManifestResourceStream(name);
                if (resourceStream != null) break;
            }

            // 如果找不到嵌入资源，尝试从源目录复制
            if (resourceStream == null)
            {
                // 列出所有可用资源以便调试
                var allResources = assembly.GetManifestResourceNames();
                System.Diagnostics.Debug.WriteLine($"可用资源: {string.Join(", ", allResources)}");

                // If file already exists in current directory, no need to extract
                if (File.Exists(targetPath))
                    return;

                return;
            }

            try
            {
                using (resourceStream)
                using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                {
                    resourceStream.CopyTo(fileStream);
                }

                System.Diagnostics.Debug.WriteLine($"Extracted: {fileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get tool file path (ensures extracted)
        /// </summary>
        public static string GetToolPath(string toolName)
        {
            ExtractAll();
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, toolName);
        }

        /// <summary>
        /// Check if tool is available
        /// </summary>
        public static bool IsToolAvailable(string toolName)
        {
            string path = GetToolPath(toolName);
            return File.Exists(path);
        }
    }
}
