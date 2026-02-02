
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using LoveAlways.Qualcomm.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Services
{
    /// <summary>
    /// OPLUS (OPPO/Realme/OnePlus) Super partition disassembly and write manager
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
        /// Scan firmware directory and generate Super disassembly write task list
        /// </summary>
        public async Task<List<FlashTask>> PrepareSuperTasksAsync(string firmwareRoot, long superStartSector, int sectorSize, string activeSlot = "a", string nvId = "")
        {
            var tasks = new List<FlashTask>();

            // 1. Find critical files
            string imagesDir = Path.Combine(firmwareRoot, "IMAGES");
            string metaDir = Path.Combine(firmwareRoot, "META");

            if (!Directory.Exists(imagesDir)) imagesDir = firmwareRoot;

            // Prioritize Metadata with NV_ID: super_meta.{nvId}.raw
            string superMetaPath = null;
            if (!string.IsNullOrEmpty(nvId))
            {
                superMetaPath = Directory.GetFiles(imagesDir, $"super_meta.{nvId}.raw").FirstOrDefault();
            }

            if (string.IsNullOrEmpty(superMetaPath))
            {
                superMetaPath = Directory.GetFiles(imagesDir, "super_meta*.raw").FirstOrDefault();

                // [Critical] If device cannot read NV_ID, auto extract from firmware package filename
                if (!string.IsNullOrEmpty(superMetaPath) && string.IsNullOrEmpty(nvId))
                {
                    nvId = ExtractNvIdFromFilename(superMetaPath);
                }
            }

            // Prioritize Mapping table with NV_ID: super_def.{nvId}.json
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
                // If super_meta.raw not found, try searching for super.img itself (if it's a full image)
                string fullSuperPath = Path.Combine(imagesDir, "super.img");
                if (File.Exists(fullSuperPath))
                {
                    _log("Full super.img found");
                    tasks.Add(new FlashTask
                    {
                        PartitionName = "super",
                        FilePath = fullSuperPath,
                        PhysicalSector = superStartSector,
                        SizeInBytes = new FileInfo(fullSuperPath).Length
                    });
                    return tasks;
                }

                _log("super_meta.raw or super.img not found");
                return tasks;
            }

            // 2. Parse LP Metadata
            byte[] metaData = File.ReadAllBytes(superMetaPath);
            var lpPartitions = _lpParser.ParseMetadata(metaData);
            _log(string.Format("Parsed Super layout: {0} logical volumes{1}", lpPartitions.Count, string.IsNullOrEmpty(nvId) ? "" : string.Format(" (NV: {0})", nvId)));

            // 3. Read mapping
            Dictionary<string, string> nameToPathMap = LoadPartitionMapManual(superDefPath, imagesDir);

            // 4. Build tasks - LP Metadata written to super+1 (Main) and super+2 (Backup)
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
                // Skip partitions without LINEAR Extent or not for current slot
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
                    _log(string.Format("  {0} -> Sector {1} ({2} MB)", lp.Name, physicalSector, realSize / 1024 / 1024));
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
                // Use simple regex to parse JSON (Avoid Newtonsoft.Json dependency)
                string content = File.ReadAllText(defPath);

                // Find partitions array content
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
            // 1. Map priority (If explicit path exists in super_def.json)
            if (map.TryGetValue(lpName, out string path)) return path;

            // 2. Try NV_ID filename matching: {lpName}.{nvId}.img or {baseName}.{nvId}.img
            if (!string.IsNullOrEmpty(nvId))
            {
                string nvPattern = string.Format("{0}.{1}.img", lpName, nvId);
                var nvFiles = Directory.GetFiles(imagesDir, nvPattern);
                if (nvFiles.Length > 0) return nvFiles[0];

                // Remove slot and try again
                string baseName = lpName;
                if (baseName.EndsWith("_a") || baseName.EndsWith("_b"))
                    baseName = baseName.Substring(0, baseName.Length - 2);

                nvPattern = string.Format("{0}.{1}.img", baseName, nvId);
                nvFiles = Directory.GetFiles(imagesDir, nvPattern);
                if (nvFiles.Length > 0) return nvFiles[0];
            }

            // 3. Original logic: Remove slot name and try again
            string searchName = lpName;
            if (searchName.EndsWith("_a") || searchName.EndsWith("_b"))
                searchName = searchName.Substring(0, searchName.Length - 2);

            if (map.TryGetValue(searchName, out path)) return path;

            // 4. General disk scan
            string[] patterns = { searchName + ".img", searchName + ".*.img", lpName + ".img" };
            foreach (var pattern in patterns)
            {
                try
                {
                    var files = Directory.GetFiles(imagesDir, pattern);
                    if (files.Length > 0) return files[0];
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Get actual image data size (Sparse images only count valid data)
        /// </summary>
        private long GetImageRealSize(string path)
        {
            if (SparseStream.IsSparseFile(path))
            {
                using (var ss = SparseStream.Open(path))
                {
                    // Return actual data size, excluding DONT_CARE
                    return ss.GetRealDataSize();
                }
            }
            return new FileInfo(path).Length;
        }

        /// <summary>
        /// Get expanded image full size
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
        /// Extract NV_ID from filename
        /// Example: super_meta.10010111.raw -> 10010111
        /// </summary>
        private string ExtractNvIdFromFilename(string filePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath); // super_meta.10010111

                // Match format: super_meta.{nvId} or super_def.{nvId}
                var match = Regex.Match(fileName, @"^super_(?:meta|def)\.(\d+)$");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                // Fallback matching: Any numeric part in filename
                // Example: system.10010111 -> 10010111
                var parts = fileName.Split('.');
                if (parts.Length >= 2)
                {
                    string potentialNvId = parts[parts.Length - 1];
                    // NV_ID is usually 8 or more digits
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
