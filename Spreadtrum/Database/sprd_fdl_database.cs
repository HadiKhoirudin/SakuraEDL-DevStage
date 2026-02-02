// ============================================================================
// LoveAlways - 展讯 FDL 数据库
// 包含芯片信息、FDL 地址、设备 FDL 映射
// ============================================================================

using LoveAlways.Spreadtrum.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoveAlways.Spreadtrum.Database
{
    /// <summary>
    /// 芯片信息
    /// </summary>
    public class SprdChipInfo
    {
        public uint ChipId { get; set; }
        public string ChipName { get; set; }
        public string DisplayName { get; set; }
        public string Series { get; set; }
        public uint Fdl1Address { get; set; }
        public uint Fdl2Address { get; set; }
        public bool HasExploit { get; set; }
        public string ExploitId { get; set; }
        public string StorageType { get; set; }

        public string Fdl1AddressHex => $"0x{Fdl1Address:X8}";
        public string Fdl2AddressHex => $"0x{Fdl2Address:X8}";
    }

    /// <summary>
    /// 设备 FDL 信息
    /// </summary>
    public class SprdDeviceFdl
    {
        public string ChipName { get; set; }
        public string DeviceName { get; set; }
        public string Brand { get; set; }
        public string Fdl1FileName { get; set; }
        public string Fdl2FileName { get; set; }
        public string RelativePath { get; set; }
        public long Fdl1Size { get; set; }
        public long Fdl2Size { get; set; }
    }

    /// <summary>
    /// 展讯 FDL 数据库
    /// </summary>
    public static class SprdFdlDatabase
    {
        private static List<SprdChipInfo> _chips;
        private static List<SprdDeviceFdl> _deviceFdls;
        private static readonly object _lock = new object();

        /// <summary>
        /// 所有芯片信息
        /// </summary>
        public static List<SprdChipInfo> Chips
        {
            get
            {
                EnsureInitialized();
                return _chips;
            }
        }

        /// <summary>
        /// 所有设备 FDL
        /// </summary>
        public static List<SprdDeviceFdl> DeviceFdls
        {
            get
            {
                EnsureInitialized();
                return _deviceFdls;
            }
        }

        private static void EnsureInitialized()
        {
            if (_chips == null)
            {
                lock (_lock)
                {
                    if (_chips == null)
                    {
                        InitializeDatabase();
                    }
                }
            }
        }

        private static void InitializeDatabase()
        {
            _chips = new List<SprdChipInfo>();
            _deviceFdls = new List<SprdDeviceFdl>();

            // ==================== 芯片数据库 ====================

            // SC77xx 系列 (旧款)
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7731,
                ChipName = "SC7731E",
                DisplayName = "SC7731E (4核 1.3GHz)",
                Series = "SC77xx",
                Fdl1Address = 0x00005000,
                Fdl2Address = 0x8A800000,
                HasExploit = true,
                ExploitId = "0x4ee8",
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7730,
                ChipName = "SC7730",
                DisplayName = "SC7730 (4核)",
                Series = "SC77xx",
                Fdl1Address = 0x00005000,
                Fdl2Address = 0x8A800000,
                HasExploit = true,
                ExploitId = "0x4ee8",
                StorageType = "eMMC"
            });

            // SC98xx / SC85xx 系列
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9832,
                ChipName = "SC9832E",
                DisplayName = "SC9832E (4核 A53)",
                Series = "SC98xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x8541,
                ChipName = "SC8541E",
                DisplayName = "SC8541E (4核 A53 LTE)",
                Series = "SC85xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9863,
                ChipName = "SC9863A",
                DisplayName = "SC9863A (8核 A55)",
                Series = "SC98xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x8581,
                ChipName = "SC8581A",
                DisplayName = "SC8581A (8核 A55)",
                Series = "SC85xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9850,
                ChipName = "SC9850K",
                DisplayName = "SC9850K (4核 A53)",
                Series = "SC98xx",
                Fdl1Address = 0x65000000,
                Fdl2Address = 0x8C800000,
                HasExploit = true,
                ExploitId = "0x65015f48",
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9860,
                ChipName = "SC9860G",
                DisplayName = "SC9860G (8核 A53)",
                Series = "SC98xx",
                Fdl1Address = 0x65000000,
                Fdl2Address = 0x8C800000,
                HasExploit = true,
                ExploitId = "0x65015f48",
                StorageType = "UFS"
            });

            // T 系列 (4G)
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0310,
                ChipName = "T310",
                DisplayName = "Tiger T310 (4核 A55)",
                Series = "T3xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0606,
                ChipName = "T606",
                DisplayName = "Tiger T606 (8核 A55)",
                Series = "T6xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0610,
                ChipName = "T610",
                DisplayName = "Tiger T610 (8核 A75+A55)",
                Series = "T6xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0618,
                ChipName = "T618",
                DisplayName = "Tiger T618 (8核 A75+A55)",
                Series = "T6xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC/UFS"
            });

            // T 系列 (5G)
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0740,
                ChipName = "T740",
                DisplayName = "Tanggula T740 (5G)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7520,
                ChipName = "T7520",
                DisplayName = "Tanggula T7520 (5G 旗舰)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "UFS"
            });

            // UMS 系列
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0312,
                ChipName = "UMS312",
                DisplayName = "UMS312 (T310 变体)",
                Series = "UMS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0512,
                ChipName = "UMS512",
                DisplayName = "UMS512 (T618 变体)",
                Series = "UMS",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9230,
                ChipName = "UMS9230",
                DisplayName = "UMS9230 (T606 变体)",
                Series = "UMS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            // 功能机芯片
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x6531,
                ChipName = "SC6531E",
                DisplayName = "SC6531E (功能机)",
                Series = "SC65xx",
                Fdl1Address = 0x40004000,
                Fdl2Address = 0x14000000,
                HasExploit = false,
                StorageType = "NOR Flash"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x6533,
                ChipName = "SC6533G",
                DisplayName = "SC6533G (功能机 4G)",
                Series = "SC65xx",
                Fdl1Address = 0x40004000,
                Fdl2Address = 0x14000000,
                HasExploit = false,
                StorageType = "NOR Flash"
            });

            // ========== 新增: T 系列 4G (更多型号) ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0612,
                ChipName = "T612",
                DisplayName = "Tiger T612 (8核 A75+A55)",
                Series = "T6xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0616,
                ChipName = "T616",
                DisplayName = "Tiger T616 (8核 A75+A55)",
                Series = "T6xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC/UFS"
            });

            // ========== T7xx 系列需要 Exploit (已验证 T760) ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0700,
                ChipName = "T700",
                DisplayName = "Tiger T700 (8核 A76+A55)",
                Series = "T7xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0xB4FFFE00,
                HasExploit = true,
                ExploitId = "0x65012f48",
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0760,
                ChipName = "T760",
                DisplayName = "Tiger T760 (8核 A76+A55) ✓已验证",
                Series = "T7xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0xB4FFFE00,
                HasExploit = true,
                ExploitId = "0x65012f48",
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0770,
                ChipName = "T770",
                DisplayName = "Tiger T770 (8核 A76+A55)",
                Series = "T7xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0xB4FFFE00,
                HasExploit = true,
                ExploitId = "0x65012f48",
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0820,
                ChipName = "T820",
                DisplayName = "Tiger T820 (8核 A78+A55)",
                Series = "T8xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            // ========== 新增: T 系列 5G (更多型号) ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0750,
                ChipName = "T750",
                DisplayName = "Tanggula T750 (5G)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0765,
                ChipName = "T765",
                DisplayName = "Tanggula T765 (5G)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7510,
                ChipName = "T7510",
                DisplayName = "Tanggula T7510 (5G)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7525,
                ChipName = "T7525",
                DisplayName = "Tanggula T7525 (5G)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x7530,
                ChipName = "T7530",
                DisplayName = "Tanggula T7530 (5G 旗舰)",
                Series = "T7xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            // ========== 新增: SC98xx 系列 (更多型号) ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9853,
                ChipName = "SC9853i",
                DisplayName = "SC9853i (8核 Intel)",
                Series = "SC98xx",
                Fdl1Address = 0x65000800,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = true,
                ExploitId = "0x65015f08",
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9830,
                ChipName = "SC9830",
                DisplayName = "SC9830 (4核 A7)",
                Series = "SC98xx",
                Fdl1Address = 0x00005000,
                Fdl2Address = 0x8A800000,
                HasExploit = true,
                ExploitId = "0x4ee8",
                StorageType = "eMMC"
            });

            // ========== 新增: UWS 可穿戴系列 ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x6152,
                ChipName = "UWS6152",
                DisplayName = "UWS6152 (可穿戴)",
                Series = "UWS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "SPI NOR"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x6121,
                ChipName = "UWS6121",
                DisplayName = "UWS6121 (可穿戴)",
                Series = "UWS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "SPI NOR"
            });

            // ========== 新增: T1xx 系列 (4G 功能机) - 参考 spreadtrum_flash ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0117,
                ChipName = "T117",
                DisplayName = "T117/UMS9117 (4G 功能机)",
                Series = "T1xx",
                Fdl1Address = 0x6200,
                Fdl2Address = 0x80100000,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0107,
                ChipName = "T107",
                DisplayName = "T107/UMS9107 (4G 功能机)",
                Series = "T1xx",
                Fdl1Address = 0x6200,
                Fdl2Address = 0x80100000,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0127,
                ChipName = "T127",
                DisplayName = "T127/UMS9127 (4G 功能机)",
                Series = "T1xx",
                Fdl1Address = 0x6200,
                Fdl2Address = 0x80100000,
                HasExploit = false,
                StorageType = "eMMC"
            });

            // ========== 新增: W 系列功能机 ==========
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0217,
                ChipName = "W217",
                DisplayName = "W217 (功能机 4G)",
                Series = "W2xx",
                Fdl1Address = 0x40004000,
                Fdl2Address = 0x14000000,
                HasExploit = false,
                StorageType = "NOR Flash"
            });

            // ========== 新增: 2024-2025 新芯片 ==========

            // T8xx 系列 (高端 4G/5G)
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0830,
                ChipName = "T830",
                DisplayName = "Tiger T830 (8核 A78+A55 5G)",
                Series = "T8xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0860,
                ChipName = "T860",
                DisplayName = "Tiger T860 (5G 旗舰)",
                Series = "T8xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            // UMS 新系列
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9620,
                ChipName = "UMS9620",
                DisplayName = "UMS9620 (T760 变体)",
                Series = "UMS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "eMMC/UFS"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x9820,
                ChipName = "UMS9820",
                DisplayName = "UMS9820 (T820 变体)",
                Series = "UMS",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9F000000,
                HasExploit = false,
                StorageType = "UFS"
            });

            // T3xx 更多型号
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0320,
                ChipName = "T320",
                DisplayName = "Tiger T320 (4核 A55 增强)",
                Series = "T3xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            // T4xx 系列 (新中端)
            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0403,
                ChipName = "T403",
                DisplayName = "Tiger T403 (6核 A55)",
                Series = "T4xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            _chips.Add(new SprdChipInfo
            {
                ChipId = 0x0430,
                ChipName = "T430",
                DisplayName = "Tiger T430 (8核 A55)",
                Series = "T4xx",
                Fdl1Address = 0x00005500,
                Fdl2Address = 0x9EFFFE00,
                HasExploit = false,
                StorageType = "eMMC"
            });

            // ==================== 设备 FDL 数据库 ====================
            // 从收集的资源中添加

            // SC8541E / SC9832E 设备
            AddDeviceFdl("SC8541E", "A23-Pro-L5006C", "Samsung", "fdl1-sign.a895ce65.bin", "fdl2-sign.55e57e05.bin");
            AddDeviceFdl("SC8541E", "A23R", "Samsung", "fdl1-sign.8e0f601f.bin", "fdl2-sign.489679bb.bin");
            AddDeviceFdl("SC8541E", "A23S-A511LQ", "Samsung", "fdl1-sign.adfffa13.bin", "fdl2-sign.154ce568.bin");
            AddDeviceFdl("SC8541E", "A27-A551L", "Samsung", "fdl1-sign.e74a133d.bin", "fdl2-sign.e5f8436f.bin");
            AddDeviceFdl("SC8541E", "A60-A662L", "ZTE", "fdl1-sign.36bcce83.bin", "fdl2-sign.e91bdb0b.bin");
            AddDeviceFdl("SC8541E", "A662L", "ZTE", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC8541E", "BL50", "Blackview", "fdl1-sign.e74a133d.bin", "fdl2-sign.e27b68ae.bin");
            AddDeviceFdl("SC8541E", "BL51", "Blackview", "fdl1-sign.e74a133d.bin", "fdl2-sign.a47bae86.bin");
            AddDeviceFdl("SC8541E", "Bold-T0040TT", "Bold", "fdl1-sign.47a50341.bin", "fdl2-sign.5f15280e.bin");
            AddDeviceFdl("SC8541E", "Bold-T0060TT", "Bold", "fdl1-sign.47a50341.bin", "fdl2-sign.76550926.bin");
            AddDeviceFdl("SC8541E", "L6006", "Other", "fdl1-sign.d92195b3.bin", "fdl2-sign.25b90be9.bin");

            // SC9863A / SC8581A 设备
            AddDeviceFdl("SC9863A", "BL50-Pro", "Blackview", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Blade-V10-Vita", "ZTE", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Hot-10i", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "RMX3231", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Vision-2-P681L", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Vision-2S-P651L", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");

            // SC7731E 设备
            AddDeviceFdl("SC7731E", "A33-Plus-A509W", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "A58-A661W", "ZTE", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "Nitro-55R", "Lava", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "S16-W6502", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");

            // UMS512 设备 (Realme)
            AddDeviceFdl("UMS512", "RMX3261", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS512", "RMX3263", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS512", "RMX3269", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");

            // UMS9230 / T606 设备 (Realme)
            AddDeviceFdl("UMS9230", "RMX3501", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS9230", "RMX3506", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS9230", "RMX3511", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS9230", "RMX3624", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UMS9230", "RMX3627", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== T610/T612/T616/T618 设备 ==========
            AddDeviceFdl("T610", "Hot-11-X662", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T610", "Hot-11S", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T610", "Note-11", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T612", "RMX3760", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T612", "Note-12-X663", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T616", "RMX3560", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T616", "Note-12-Pro", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T618", "Tab-8-X", "Lenovo", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T618", "RMX3085", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T618", "Pad-5", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== T700/T760/T770 设备 ==========
            AddDeviceFdl("T700", "GT-2-Pro", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T760", "Note-30-5G", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T770", "11T-Pro", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== T820 设备 ==========
            AddDeviceFdl("T820", "GT-5-Pro", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("T820", "V30", "Vivo", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== SC9863A 更多设备 ==========
            AddDeviceFdl("SC9863A", "Smart-5-P561L", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Smart-7-S663L", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Hot-9-X655", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Hot-10-Play", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "C21Y", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "C25Y", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "A03s", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "A04s", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Nokia-C01-Plus", "Nokia", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC9863A", "Nokia-C20", "Nokia", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== SC8541E 更多设备 ==========
            AddDeviceFdl("SC8541E", "A04e", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC8541E", "A05", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC8541E", "A24", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC8541E", "Note-12-VIP", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC8541E", "C35", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== SC7731E 更多设备 ==========
            AddDeviceFdl("SC7731E", "A02s", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "A03-Core", "Samsung", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "S15-Pro", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "A25-W6501", "Itel", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("SC7731E", "Smart-6", "Infinix", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== SC6531E 功能机 ==========
            AddDeviceFdl("SC6531E", "2720-Flip", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6531E", "105-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6531E", "110-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6531E", "it2163", "Itel", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6531E", "it2173", "Itel", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6531E", "B310", "Samsung", "fdl1.bin", "fdl2.bin");

            // ========== SC6533G 功能机 4G ==========
            AddDeviceFdl("SC6533G", "2760-Flip", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6533G", "225-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6533G", "6300-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6533G", "8000-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("SC6533G", "Keypad-40", "TCL", "fdl1.bin", "fdl2.bin");

            // ========== UWS6152 可穿戴 ==========
            AddDeviceFdl("UWS6152", "Watch-S1", "Xiaomi", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UWS6152", "Watch-2-Pro", "Realme", "fdl1-sign.bin", "fdl2-sign.bin");
            AddDeviceFdl("UWS6152", "Band-7", "Honor", "fdl1-sign.bin", "fdl2-sign.bin");

            // ========== W217 功能机 4G ==========
            AddDeviceFdl("W217", "Z20", "Lava", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("W217", "it5262", "Itel", "fdl1.bin", "fdl2.bin");

            // ========== T117/T107/T127 4G 功能机 (参考 spreadtrum_flash) ==========
            AddDeviceFdl("T117", "220-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "230-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "235-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "400-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "it5625", "Itel", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "it5626", "Itel", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "Z10", "Lava", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "Z30", "Lava", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "MT6820", "Micromax", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T117", "KG365", "Karbonn", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T107", "105-Dual-SIM", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T107", "it5029", "Itel", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T127", "150-4G", "Nokia", "fdl1.bin", "fdl2.bin");
            AddDeviceFdl("T127", "110-4G-2022", "Nokia", "fdl1.bin", "fdl2.bin");

            // ========== SC6530/SC6531DA 旧功能机 ==========
            AddDeviceFdl("SC6530", "Generic-SC6530", "Other", "nor_fdl.bin", "");
            AddDeviceFdl("SC6531DA", "Generic-SC6531DA", "Other", "nor_fdl.bin", "");
        }

        private static void AddDeviceFdl(string chipName, string deviceName, string brand, string fdl1, string fdl2)
        {
            _deviceFdls.Add(new SprdDeviceFdl
            {
                ChipName = chipName,
                DeviceName = deviceName,
                Brand = brand,
                Fdl1FileName = fdl1,
                Fdl2FileName = fdl2
            });
        }

        /// <summary>
        /// 获取所有芯片名称
        /// </summary>
        public static string[] GetChipNames()
        {
            return Chips.Select(c => c.ChipName).ToArray();
        }

        /// <summary>
        /// 获取芯片的所有设备
        /// </summary>
        public static string[] GetDeviceNames(string chipName)
        {
            return DeviceFdls
                .Where(d => d.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.DeviceName)
                .ToArray();
        }

        /// <summary>
        /// 根据芯片名称获取信息
        /// </summary>
        public static SprdChipInfo GetChipByName(string chipName)
        {
            return Chips.FirstOrDefault(c =>
                c.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据芯片 ID 获取信息
        /// </summary>
        public static SprdChipInfo GetChipById(uint chipId)
        {
            return Chips.FirstOrDefault(c => c.ChipId == chipId || c.ChipId == (chipId & 0xFFFF));
        }

        /// <summary>
        /// 获取芯片的设备 FDL 列表
        /// </summary>
        public static List<SprdDeviceFdl> GetDeviceFdlsByChip(string chipName)
        {
            return DeviceFdls
                .Where(d => d.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// 获取指定芯片和设备的 FDL 信息
        /// </summary>
        public static SprdDeviceFdl GetDeviceFdl(string chipName, string deviceName)
        {
            return DeviceFdls.FirstOrDefault(d =>
                d.ChipName.Equals(chipName, StringComparison.OrdinalIgnoreCase) &&
                d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 搜索设备
        /// </summary>
        public static List<SprdDeviceFdl> SearchDevices(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return DeviceFdls;

            return DeviceFdls
                .Where(d => d.DeviceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           d.Brand.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           d.ChipName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>
        /// 获取有 Exploit 的芯片
        /// </summary>
        public static List<SprdChipInfo> GetExploitableChips()
        {
            return Chips.Where(c => c.HasExploit).ToList();
        }

        /// <summary>
        /// 按系列分组获取芯片
        /// </summary>
        public static Dictionary<string, List<SprdChipInfo>> GetChipsBySeries()
        {
            return Chips.GroupBy(c => c.Series)
                       .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// 获取 FDL 文件的完整路径
        /// 优先级: 1. 统一 PAK (sprd.pak) -> 2. FDL PAK (fdl.pak) -> 3. 本地文件
        /// </summary>
        public static string GetFdlPath(SprdDeviceFdl device, bool isFdl1)
        {
            // 1. 优先从统一资源包加载
            var pakPath = GetFdlPathFromUnifiedPak(device.ChipName, device.DeviceName, isFdl1);
            if (pakPath != null)
                return pakPath;

            // 2. 尝试 FDL 专用资源包
            pakPath = GetFdlPathFromFdlPak(device.ChipName, device.DeviceName, isFdl1);
            if (pakPath != null)
                return pakPath;

            // 3. 回退到本地文件
            return GetFdlPathFromLocal(device, isFdl1);
        }

        /// <summary>
        /// 从统一资源包获取 FDL (sprd.pak)
        /// </summary>
        private static string GetFdlPathFromUnifiedPak(string chipName, string deviceName, bool isFdl1)
        {
            // 加载统一资源包
            if (!SprdPakManager.IsLoaded)
            {
                SprdPakManager.LoadPak();
            }

            if (!SprdPakManager.IsLoaded)
                return null;

            return SprdPakManager.ExtractFdlToTemp(chipName, deviceName, isFdl1);
        }

        /// <summary>
        /// 从 FDL 资源包获取 (fdl.pak) - 向后兼容
        /// </summary>
        private static string GetFdlPathFromFdlPak(string chipName, string deviceName, bool isFdl1)
        {
            if (!FdlPakManager.IsLoaded)
            {
                FdlPakManager.LoadPak();
            }

            if (!FdlPakManager.IsLoaded)
                return null;

            return FdlPakManager.ExtractToTempFile(chipName, deviceName, isFdl1);
        }

        /// <summary>
        /// 直接从 PAK 获取 FDL 数据 (不提取文件)
        /// </summary>
        public static byte[] GetFdlDataFromPak(string chipName, string deviceName, bool isFdl1)
        {
            // 1. 统一资源包
            if (!SprdPakManager.IsLoaded)
                SprdPakManager.LoadPak();

            if (SprdPakManager.IsLoaded)
            {
                var data = SprdPakManager.GetFdlData(chipName, deviceName, isFdl1);
                if (data != null)
                    return data;
            }

            // 2. FDL 资源包
            if (!FdlPakManager.IsLoaded)
                FdlPakManager.LoadPak();

            if (FdlPakManager.IsLoaded)
                return FdlPakManager.GetFdlData(chipName, deviceName, isFdl1);

            return null;
        }

        /// <summary>
        /// 从本地文件系统获取 FDL 路径
        /// </summary>
        private static string GetFdlPathFromLocal(SprdDeviceFdl device, bool isFdl1)
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "sprd_fdls");

            // 根据芯片类型确定子目录
            string chipDir = GetChipDirectory(device.ChipName);
            if (string.IsNullOrEmpty(chipDir))
                return null;

            string fileName = isFdl1 ? device.Fdl1FileName : device.Fdl2FileName;
            string path = Path.Combine(baseDir, chipDir, device.DeviceName, fileName);

            // 如果精确路径不存在，尝试搜索
            if (!File.Exists(path))
            {
                string searchDir = Path.Combine(baseDir, chipDir, device.DeviceName);
                if (Directory.Exists(searchDir))
                {
                    string pattern = isFdl1 ? "fdl1*.bin" : "fdl2*.bin";
                    var files = Directory.GetFiles(searchDir, pattern);
                    if (files.Length > 0)
                        return files[0];
                }

                // 尝试在芯片目录下直接搜索
                searchDir = Path.Combine(baseDir, chipDir);
                if (Directory.Exists(searchDir))
                {
                    string pattern = isFdl1 ? "fdl1*.bin" : "fdl2*.bin";
                    var files = Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories);
                    if (files.Length > 0)
                        return files[0];
                }
            }

            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// 获取芯片对应的 FDL 目录
        /// </summary>
        private static string GetChipDirectory(string chipName)
        {
            switch (chipName.ToUpper())
            {
                // SC85xx / SC98xx 系列
                case "SC8541E":
                case "SC9832E":
                    return @"sc_sp_sl\98xx_85xx\9832E_8541E";
                case "SC9863A":
                case "SC8581A":
                    return @"sc_sp_sl\98xx_85xx\9863A_8581A";
                case "SC9850K":
                case "SC9860G":
                    return @"sc_sp_sl\98xx_85xx\9850_9860";
                case "SC9853I":
                    return @"sc_sp_sl\98xx_85xx\9853i";
                case "SC9830":
                case "SC9830A":
                    return @"sc_sp_sl\98xx_85xx\9830";

                // SC77xx 系列
                case "SC7731E":
                case "SC7731":
                case "SC7731G":
                    return @"sc_sp_sl\old\7731e";
                case "SC7730":
                    return @"sc_sp_sl\old\7730";

                // T 系列 4G
                case "T310":
                    return @"t_series\t310";
                case "T606":
                    return @"t_series\t606";
                case "T610":
                    return @"t_series\t610";
                case "T612":
                    return @"t_series\t612";
                case "T616":
                    return @"t_series\t616";
                case "T618":
                    return @"t_series\t618";
                case "T700":
                    return @"t_series\t700";
                case "T760":
                    return @"t_series\t760";
                case "T770":
                    return @"t_series\t770";
                case "T820":
                    return @"t_series\t820";

                // T 系列 5G
                case "T740":
                case "T750":
                case "T765":
                    return @"t_series\5g\t7xx";
                case "T7510":
                case "T7520":
                case "T7525":
                case "T7530":
                    return @"t_series\5g\t75xx";

                // UMS 系列
                case "UMS312":
                    return @"ums\ums312";
                case "UMS512":
                    return @"ums\ums512";
                case "UMS9230":
                    return @"ums\ums9230";

                // T1xx 系列 (4G 功能机) - 参考 spreadtrum_flash
                case "T107":
                case "T117":
                case "T127":
                case "UMS9107":
                case "UMS9117":
                case "UMS9127":
                    return @"t_series\t1xx";

                // 功能机系列
                case "SC6531E":
                case "SC6531":
                case "SC6531DA":
                case "SC6530":
                    return @"feature_phone\6531e";
                case "SC6533G":
                    return @"feature_phone\6533g";
                case "W117":
                case "W217":
                    return @"feature_phone\w_series";

                // 可穿戴系列
                case "UWS6121":
                case "UWS6152":
                    return @"wearable\uws";

                default:
                    // 通用搜索路径
                    return chipName.ToLower();
            }
        }

        /// <summary>
        /// 获取芯片的通用 FDL 文件 (不指定设备)
        /// </summary>
        public static string GetGenericFdlPath(string chipName, bool isFdl1)
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SprdResources", "sprd_fdls");
            string chipDir = GetChipDirectory(chipName);

            if (string.IsNullOrEmpty(chipDir))
                return null;

            string searchDir = Path.Combine(baseDir, chipDir);
            if (!Directory.Exists(searchDir))
                return null;

            string pattern = isFdl1 ? "fdl1*.bin" : "fdl2*.bin";
            var files = Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories);

            // 优先返回签名版本
            var signedFile = files.FirstOrDefault(f => f.Contains("-sign"));
            if (signedFile != null)
                return signedFile;

            return files.FirstOrDefault();
        }

        /// <summary>
        /// 检查芯片是否有可用的 FDL 文件
        /// </summary>
        public static bool HasFdlFiles(string chipName)
        {
            return GetGenericFdlPath(chipName, true) != null && GetGenericFdlPath(chipName, false) != null;
        }

        #region 芯片识别

        /// <summary>
        /// 从芯片 ID 智能识别芯片信息
        /// </summary>
        /// <param name="chipId">芯片 ID (从设备读取)</param>
        /// <returns>芯片信息，或 null</returns>
        public static SprdChipInfo IdentifyChip(uint chipId)
        {
            // 1. 精确匹配
            var exact = GetChipById(chipId);
            if (exact != null)
                return exact;

            // 2. 尝试常见的 ID 变体
            uint[] variants = new uint[]
            {
                chipId & 0xFFFF,           // 低 16 位
                chipId >> 16,              // 高 16 位
                (chipId & 0xFF00) >> 8,    // 中间字节
                chipId & 0x0FFF            // 低 12 位 (T 系列常见)
            };

            foreach (var variant in variants)
            {
                var chip = GetChipById(variant);
                if (chip != null)
                    return chip;
            }

            // 3. 按系列推测
            return GuessChipBySeries(chipId);
        }

        /// <summary>
        /// 按系列推测芯片信息
        /// </summary>
        private static SprdChipInfo GuessChipBySeries(uint chipId)
        {
            // T 系列 (0x03xx ~ 0x08xx)
            if (chipId >= 0x0300 && chipId < 0x0900)
            {
                int series = (int)((chipId >> 8) & 0x0F);
                string seriesName = series switch
                {
                    3 => "T3xx",
                    4 => "T4xx",
                    6 => "T6xx",
                    7 => "T7xx",
                    8 => "T8xx",
                    _ => $"T{series}xx"
                };

                return new SprdChipInfo
                {
                    ChipId = chipId,
                    ChipName = $"Unknown-T{chipId:X3}",
                    DisplayName = $"未知 T 系列芯片 (0x{chipId:X})",
                    Series = seriesName,
                    Fdl1Address = 0x00005500,
                    Fdl2Address = 0x9EFFFE00,
                    HasExploit = false,
                    StorageType = "eMMC/UFS"
                };
            }

            // SC98xx 系列
            if (chipId >= 0x9800 && chipId < 0x9900)
            {
                return new SprdChipInfo
                {
                    ChipId = chipId,
                    ChipName = $"SC{chipId:X4}",
                    DisplayName = $"SC{chipId:X4} (未知型号)",
                    Series = "SC98xx",
                    Fdl1Address = 0x65000800,
                    Fdl2Address = 0x9EFFFE00,
                    HasExploit = false,
                    StorageType = "eMMC"
                };
            }

            // SC77xx 系列
            if (chipId >= 0x7700 && chipId < 0x7800)
            {
                return new SprdChipInfo
                {
                    ChipId = chipId,
                    ChipName = $"SC{chipId:X4}",
                    DisplayName = $"SC{chipId:X4} (旧款芯片)",
                    Series = "SC77xx",
                    Fdl1Address = 0x00005000,
                    Fdl2Address = 0x8A800000,
                    HasExploit = true,  // 旧款芯片通常有漏洞
                    ExploitId = "0x4ee8",
                    StorageType = "eMMC"
                };
            }

            // SC65xx 功能机系列
            if (chipId >= 0x6500 && chipId < 0x6600)
            {
                return new SprdChipInfo
                {
                    ChipId = chipId,
                    ChipName = $"SC{chipId:X4}",
                    DisplayName = $"SC{chipId:X4} (功能机芯片)",
                    Series = "SC65xx",
                    Fdl1Address = 0x40004000,
                    Fdl2Address = 0x14000000,
                    HasExploit = false,
                    StorageType = "NOR Flash"
                };
            }

            // UMS 系列
            if (chipId >= 0x9100 && chipId < 0x9700)
            {
                return new SprdChipInfo
                {
                    ChipId = chipId,
                    ChipName = $"UMS{chipId:X4}",
                    DisplayName = $"UMS{chipId:X4} (未知型号)",
                    Series = "UMS",
                    Fdl1Address = 0x00005500,
                    Fdl2Address = 0x9EFFFE00,
                    HasExploit = false,
                    StorageType = "eMMC"
                };
            }

            return null;
        }

        /// <summary>
        /// 获取芯片的推荐配置
        /// </summary>
        public static (uint fdl1Addr, uint fdl2Addr, string storageType) GetRecommendedConfig(uint chipId)
        {
            var chip = IdentifyChip(chipId);
            if (chip != null)
            {
                return (chip.Fdl1Address, chip.Fdl2Address, chip.StorageType);
            }

            // 默认配置 (适用于较新芯片)
            return (0x00005500, 0x9EFFFE00, "eMMC");
        }

        /// <summary>
        /// 获取数据库统计信息
        /// </summary>
        public static SprdDatabaseStats GetStats()
        {
            EnsureInitialized();
            return new SprdDatabaseStats
            {
                TotalChips = _chips.Count,
                TotalDevices = _deviceFdls.Count,
                ChipsWithExploit = _chips.Count(c => c.HasExploit),
                SeriesCounts = _chips.GroupBy(c => c.Series)
                                      .ToDictionary(g => g.Key, g => g.Count()),
                BrandCounts = _deviceFdls.GroupBy(d => d.Brand)
                                          .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        #endregion
    }

    /// <summary>
    /// 数据库统计信息
    /// </summary>
    public class SprdDatabaseStats
    {
        public int TotalChips { get; set; }
        public int TotalDevices { get; set; }
        public int ChipsWithExploit { get; set; }
        public Dictionary<string, int> SeriesCounts { get; set; }
        public Dictionary<string, int> BrandCounts { get; set; }

        public override string ToString()
        {
            return $"展讯数据库: {TotalChips} 芯片, {TotalDevices} 设备, {ChipsWithExploit} 有漏洞";
        }
    }
}
