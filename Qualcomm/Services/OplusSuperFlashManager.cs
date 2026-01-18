using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using LoveAlways.Qualcomm.Common;
using LoveAlways.Qualcomm.Models;

namespace LoveAlways.Qualcomm.Services
{
    /// <summary>
    /// OPLUS (OPPO/Realme/OnePlus) Super 分区拆解写入管理器
    /// </summary>
    public class OplusSuperFlashManager
    {
        private readonly Action<string> _log;
        private readonly LpMetadataParser _lpParser;

        public OplusSuperFlashManager(Action<string> log)
        {
            _log = log;
            _lpParser = new LpMetadataParser();
        }

        public class FlashTask
        {
            public string PartitionName { get; set; }
            public string FilePath { get; set; }
            public long PhysicalSector { get; set; }
            public long SizeInBytes { get; set; }
        }

        /// <summary>
        /// 扫描固件目录，生成 Super 拆解写入任务列表
        /// </summary>
        public async Task<List<FlashTask>> PrepareSuperTasksAsync(string firmwareRoot, long superStartSector, int sectorSize, string activeSlot = "a", string nvId = "")
        {
            var tasks = new List<FlashTask>();
            
            // 1. 查找关键文件
            string imagesDir = Path.Combine(firmwareRoot, "IMAGES");
            string metaDir = Path.Combine(firmwareRoot, "META");
            
            if (!Directory.Exists(imagesDir)) imagesDir = firmwareRoot;
            
            // 优先查找带 NV_ID 的 Metadata: super_meta.{nvId}.raw
            string superMetaPath = null;
            if (!string.IsNullOrEmpty(nvId))
            {
                superMetaPath = Directory.GetFiles(imagesDir, $"super_meta.{nvId}.raw").FirstOrDefault();
            }
            
            if (string.IsNullOrEmpty(superMetaPath))
            {
                superMetaPath = Directory.GetFiles(imagesDir, "super_meta*.raw").FirstOrDefault();
                
                // [关键] 如果设备无法读取 NV_ID，则从固件包文件名自动提取
                if (!string.IsNullOrEmpty(superMetaPath) && string.IsNullOrEmpty(nvId))
                {
                    nvId = ExtractNvIdFromFilename(superMetaPath);
                }
            }

            // 优先查找带 NV_ID 的映射表: super_def.{nvId}.json
            string superDefPath = null;
            if (!string.IsNullOrEmpty(nvId))
            {
                superDefPath = Path.Combine(metaDir, $"super_def.{nvId}.json");
            }
            if (string.IsNullOrEmpty(superDefPath) || !File.Exists(superDefPath))
            {
                superDefPath = Path.Combine(metaDir, "super_def.json");
            }
            
            if (string.IsNullOrEmpty(superMetaPath) || !File.Exists(superMetaPath))
            {
                // 如果没有 super_meta.raw，尝试寻找 super.img 本身 (如果是完整镜像)
                string fullSuperPath = Path.Combine(imagesDir, "super.img");
                if (File.Exists(fullSuperPath))
                {
                    _log("发现全量 super.img");
                    tasks.Add(new FlashTask { 
                        PartitionName = "super", 
                        FilePath = fullSuperPath, 
                        PhysicalSector = superStartSector, 
                        SizeInBytes = new FileInfo(fullSuperPath).Length 
                    });
                    return tasks;
                }
                
                _log("未找到 super_meta.raw 或 super.img");
                return tasks;
            }

            // 2. 解析 LP Metadata
            byte[] metaData = File.ReadAllBytes(superMetaPath);
            var lpPartitions = _lpParser.ParseMetadata(metaData);
            _log(string.Format("解析 Super 布局: {0} 个逻辑卷{1}", lpPartitions.Count, string.IsNullOrEmpty(nvId) ? "" : string.Format(" (NV: {0})", nvId)));

            // 3. 读取映射
            Dictionary<string, string> nameToPathMap = LoadPartitionMapManual(superDefPath, imagesDir);

            // 4. 构建任务 - LP Metadata 写入 super+1 (主) 和 super+2 (备)
            tasks.Add(new FlashTask
            {
                PartitionName = "super",
                FilePath = superMetaPath,
                PhysicalSector = superStartSector + 1,
                SizeInBytes = metaData.Length
            });
            tasks.Add(new FlashTask
            {
                PartitionName = "super",
                FilePath = superMetaPath,
                PhysicalSector = superStartSector + 2,
                SizeInBytes = metaData.Length
            });

            string suffix = "_" + activeSlot.ToLower();
            foreach (var lp in lpPartitions)
            {
                // 跳过没有 LINEAR Extent 的分区或非当前槽位
                if (!lp.HasLinearExtent) continue;
                if ((lp.Name.EndsWith("_a") || lp.Name.EndsWith("_b")) && !lp.Name.EndsWith(suffix)) continue;

                string imgPath = FindImagePath(lp.Name, nameToPathMap, imagesDir, nvId);

                if (imgPath != null)
                {
                    long realSize = GetImageRealSize(imgPath);
                    long deviceSectorOffset = lp.GetDeviceSectorOffset(sectorSize);
                    if (deviceSectorOffset < 0) continue;
                    
                    long physicalSector = superStartSector + deviceSectorOffset;
                    
                    tasks.Add(new FlashTask
                    {
                        PartitionName = lp.Name,
                        FilePath = imgPath,
                        PhysicalSector = physicalSector,
                        SizeInBytes = realSize
                    });
                    _log(string.Format("  {0} -> 扇区 {1} ({2} MB)", lp.Name, physicalSector, realSize / 1024 / 1024));
                }
            }

            return tasks;
        }

        private Dictionary<string, string> LoadPartitionMapManual(string defPath, string imagesDir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(defPath)) return map;

            try
            {
                // 使用简单正则解析 JSON (避免引入 Newtonsoft.Json 依赖)
                string content = File.ReadAllText(defPath);
                
                // 寻找 partitions 数组内容
                var matches = Regex.Matches(content, "\"name\":\\s*\"(.*?)\".*?\"path\":\\s*\"(.*?)\"", RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    string name = m.Groups[1].Value;
                    string relPath = m.Groups[2].Value;
                    
                    string fullPath = Path.Combine(imagesDir, relPath.Replace("IMAGES/", ""));
                    if (File.Exists(fullPath)) map[name] = fullPath;
                }
            }
            catch { }
            return map;
        }

        private string FindImagePath(string lpName, Dictionary<string, string> map, string imagesDir, string nvId = "")
        {
            // 1. Map 优先 (如果 super_def.json 中有明确路径)
            if (map.TryGetValue(lpName, out string path)) return path;

            // 2. 尝试带 NV_ID 的文件名匹配: {lpName}.{nvId}.img 或 {baseName}.{nvId}.img
            if (!string.IsNullOrEmpty(nvId))
            {
                string nvPattern = string.Format("{0}.{1}.img", lpName, nvId);
                var nvFiles = Directory.GetFiles(imagesDir, nvPattern);
                if (nvFiles.Length > 0) return nvFiles[0];

                // 去掉槽位再找
                string baseName = lpName;
                if (baseName.EndsWith("_a") || baseName.EndsWith("_b"))
                    baseName = baseName.Substring(0, baseName.Length - 2);
                
                nvPattern = string.Format("{0}.{1}.img", baseName, nvId);
                nvFiles = Directory.GetFiles(imagesDir, nvPattern);
                if (nvFiles.Length > 0) return nvFiles[0];
            }

            // 3. 原有逻辑：去掉槽位名再找
            string searchName = lpName;
            if (searchName.EndsWith("_a") || searchName.EndsWith("_b"))
                searchName = searchName.Substring(0, searchName.Length - 2);

            if (map.TryGetValue(searchName, out path)) return path;

            // 4. 通用磁盘扫描
            string[] patterns = { searchName + ".img", searchName + ".*.img", lpName + ".img" };
            foreach (var pattern in patterns)
            {
                try {
                    var files = Directory.GetFiles(imagesDir, pattern);
                    if (files.Length > 0) return files[0];
                } catch { }
            }

            return null;
        }

        /// <summary>
        /// 获取镜像的实际数据大小（Sparse 镜像只计算有效数据）
        /// </summary>
        private long GetImageRealSize(string path)
        {
            if (SparseStream.IsSparseFile(path))
            {
                using (var ss = SparseStream.Open(path))
                {
                    // 返回实际数据大小，不含 DONT_CARE
                    return ss.GetRealDataSize();
                }
            }
            return new FileInfo(path).Length;
        }

        /// <summary>
        /// 获取镜像展开后的完整大小
        /// </summary>
        private long GetImageExpandedSize(string path)
        {
            if (SparseStream.IsSparseFile(path))
            {
                using (var ss = SparseStream.Open(path))
                {
                    return ss.Length;
                }
            }
            return new FileInfo(path).Length;
        }

        /// <summary>
        /// 从文件名中提取 NV_ID
        /// 例如: super_meta.10010111.raw -> 10010111
        /// </summary>
        private string ExtractNvIdFromFilename(string filePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath); // super_meta.10010111
                
                // 匹配格式: super_meta.{nvId} 或 super_def.{nvId}
                var match = Regex.Match(fileName, @"^super_(?:meta|def)\.(\d+)$");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
                
                // 备用匹配: 任意文件名中间的数字部分
                // 例如: system.10010111 -> 10010111
                var parts = fileName.Split('.');
                if (parts.Length >= 2)
                {
                    string potentialNvId = parts[parts.Length - 1];
                    // NV_ID 通常是 8 位或更长的数字
                    if (Regex.IsMatch(potentialNvId, @"^\d{6,}$"))
                    {
                        return potentialNvId;
                    }
                }
            }
            catch { }
            
            return null;
        }
    }
}
