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
                    _log("[OPLUS] 发现全量 super.img，将执行标准写入");
                    tasks.Add(new FlashTask { 
                        PartitionName = "super", 
                        FilePath = fullSuperPath, 
                        PhysicalSector = superStartSector, 
                        SizeInBytes = new FileInfo(fullSuperPath).Length 
                    });
                    return tasks;
                }
                
                _log("[OPLUS] 未找到 super_meta.raw 或 super.img");
                return tasks;
            }

            _log(string.Format("[OPLUS] 深度分析布局: {0} (NV_ID: {1})", Path.GetFileName(superMetaPath), string.IsNullOrEmpty(nvId) ? "默认" : nvId));

            // 2. 解析 LP Metadata
            byte[] metaData = File.ReadAllBytes(superMetaPath);
            var lpPartitions = _lpParser.ParseMetadata(metaData);
            _log(string.Format("[OPLUS] LP Metadata 解析成功，共 {0} 个逻辑卷", lpPartitions.Count));

            // 3. 读取映射
            Dictionary<string, string> nameToPathMap = LoadPartitionMapManual(superDefPath, imagesDir);

            // 4. 构建任务
            // [官方协议] LP Metadata 写入 super 分区内偏移 1 扇区（主副本）和 2 扇区（备份副本）
            // 主副本 (Primary) - super + 1 扇区
            tasks.Add(new FlashTask
            {
                PartitionName = "super",  // 官方使用 label = super
                FilePath = superMetaPath,
                PhysicalSector = superStartSector + 1,  // 偏移 1 扇区
                SizeInBytes = metaData.Length
            });
            _log(string.Format("[OPLUS] LP Metadata 主副本将写入: 扇区 {0} (super+1)", superStartSector + 1));
            
            // 备份副本 (Backup) - super + 2 扇区
            tasks.Add(new FlashTask
            {
                PartitionName = "super",  // 官方使用 label = super  
                FilePath = superMetaPath,
                PhysicalSector = superStartSector + 2,  // 偏移 2 扇区
                SizeInBytes = metaData.Length
            });
            _log(string.Format("[OPLUS] LP Metadata 备份副本将写入: 扇区 {0} (super+2)", superStartSector + 2));

            string suffix = "_" + activeSlot.ToLower();
            foreach (var lp in lpPartitions)
            {
                // 跳过没有 LINEAR Extent 的分区（如空分区或 ZERO 类型）
                if (!lp.HasLinearExtent) 
                {
                    _log(string.Format("[OPLUS] 跳过非 LINEAR 分区: {0}", lp.Name));
                    continue;
                }

                // 跳过非当前槽位的分区
                if ((lp.Name.EndsWith("_a") || lp.Name.EndsWith("_b")) && !lp.Name.EndsWith(suffix))
                {
                    continue;
                }

                string imgPath = FindImagePath(lp.Name, nameToPathMap, imagesDir, nvId);

                if (imgPath != null)
                {
                    long realSize = GetImageRealSize(imgPath);
                    
                    // [关键修复] 正确计算设备扇区偏移
                    // LP Metadata 使用 512B 扇区，设备可能使用 4096B 扇区
                    long deviceSectorOffset = lp.GetDeviceSectorOffset(sectorSize);
                    if (deviceSectorOffset < 0)
                    {
                        _log(string.Format("[OPLUS] 警告: {0} 无法计算偏移，跳过", lp.Name));
                        continue;
                    }
                    
                    long physicalSector = superStartSector + deviceSectorOffset;
                    long offsetInBytes = deviceSectorOffset * sectorSize;
                    
                    // 验证展开大小不超过分区容量
                    long lpCapacityBytes = lp.TotalSizeBytes;
                    if (realSize > lpCapacityBytes && lpCapacityBytes > 0)
                    {
                        _log(string.Format("[OPLUS] 警告: {0} 镜像大小 ({1} MB) 超过分区容量 ({2} MB)", 
                            lp.Name, realSize / 1024 / 1024, lpCapacityBytes / 1024 / 1024));
                    }
                    
                    tasks.Add(new FlashTask
                    {
                        PartitionName = lp.Name,
                        FilePath = imgPath,
                        PhysicalSector = physicalSector,
                        SizeInBytes = realSize
                    });
                    _log(string.Format("[OPLUS] 映射逻辑卷: {0,-15} -> {1,-25} | 物理扇区: {2} | 偏移: 0x{3:X10} ({4} MB)", 
                        lp.Name, Path.GetFileName(imgPath), physicalSector, offsetInBytes, realSize / 1024 / 1024));
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

        private long GetImageRealSize(string path)
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
    }
}
