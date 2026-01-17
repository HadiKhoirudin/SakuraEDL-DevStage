// ============================================================================
// LoveAlways - 高通芯片数据库
// Qualcomm Chipset Database - 设备识别信息库
// ============================================================================
// 模块: Qualcomm.Database
// 功能: 包含 MSM ID、芯片名称、厂商信息和硬件规格
// ============================================================================

using System.Collections.Generic;

namespace LoveAlways.Qualcomm.Database
{
    /// <summary>
    /// 存储类型枚举
    /// </summary>
    public enum MemoryType
    {
        Unknown = -1,
        Nand = 0,
        Emmc = 1,
        Ufs = 2,
        Spinor = 3
    }

    /// <summary>
    /// 高通芯片数据库
    /// </summary>
    public static class QualcommDatabase
    {
        // OEM ID -> 厂商名称
        public static readonly Dictionary<ushort, string> VendorIds = new Dictionary<ushort, string>
        {
            { 0x0000, "Qualcomm" },
            { 0x0001, "Foxconn/Sony" },
            { 0x0004, "ZTE" },
            { 0x0011, "Smartisan" },
            { 0x0015, "Huawei" },
            { 0x0017, "Lenovo" },
            { 0x0040, "Lenovo" },
            { 0x0020, "Samsung" },
            { 0x0029, "Asus" },
            { 0x0030, "Haier" },
            { 0x0031, "LG" },
            { 0x0035, "Foxconn/Nokia" },
            { 0x0042, "Alcatel" },
            { 0x0045, "Nokia" },
            { 0x0048, "YuLong" },
            { 0x0051, "Oppo/OnePlus" },
            { 0x0072, "Xiaomi" },
            { 0x0073, "Vivo" },
            { 0x0130, "GlocalMe" },
            { 0x0139, "Lyf" },
            { 0x0168, "Motorola" },
            { 0x01B0, "Motorola" },
            { 0x0208, "Motorola" },
            { 0x0228, "Motorola" },
            { 0x2A96, "Micromax" },
            { 0x1043, "Asus" },
            { 0x1111, "Asus" },
            { 0x143A, "Asus" },
            { 0x1978, "Blackphone" },
            { 0x2A70, "Oxygen" },
        };

        // HWID -> 芯片名称
        public static readonly Dictionary<uint, string> MsmIds = new Dictionary<uint, string>
        {
            // Snapdragon 2xx
            { 0x009600E1, "MSM8909 (Snapdragon 210)" },
            
            // Snapdragon 4xx
            { 0x007050E1, "MSM8916 (Snapdragon 410)" },
            { 0x009720E1, "MSM8952 (Snapdragon 617)" },
            { 0x000460E1, "MSM8953 (Snapdragon 625)" },
            { 0x0004F0E1, "MSM8937 (Snapdragon 430)" },
            { 0x0006B0E1, "MSM8940 (Snapdragon 435)" },
            { 0x0009A0E1, "SDM450 (Snapdragon 450)" },
            { 0x000BE0E1, "SDM429 (Snapdragon 429)" },
            { 0x000BF0E1, "SDM439 (Snapdragon 439)" },
            
            // Snapdragon 6xx
            { 0x009900E1, "MSM8976 (Snapdragon 652)" },
            { 0x0009B0E1, "MSM8956 (Snapdragon 650)" },
            { 0x000AC0E1, "SDM630 (Snapdragon 630)" },
            { 0x000CC0E1, "SDM636 (Snapdragon 636)" },
            { 0x0008C0E1, "SDM660 (Snapdragon 660)" },
            { 0x000BA0E1, "SDM632 (Snapdragon 632)" },
            { 0x000950E1, "SM6150 (Snapdragon 675)" },
            
            // Snapdragon 7xx
            { 0x000910E1, "SDM670 (Snapdragon 670)" },
            { 0x000DB0E1, "SDM710 (Snapdragon 710)" },
            { 0x000E70E1, "SM7150 (Snapdragon 730)" },
            { 0x0011E0E1, "SM7250 (Snapdragon 765G)" },
            { 0x001920E1, "SM7325 (Snapdragon 778G)" },
            { 0x001630E1, "SM7350 (Snapdragon 780G)" },
            
            // Snapdragon 8xx (旗舰)
            { 0x007B00E1, "MSM8974 (Snapdragon 800)" },
            { 0x009400E1, "MSM8994 (Snapdragon 810)" },
            { 0x009690E1, "MSM8992 (Snapdragon 808)" },
            { 0x009470E1, "MSM8996 (Snapdragon 820)" },
            { 0x0005F0E1, "MSM8996Pro (Snapdragon 821)" },
            { 0x0005E0E1, "MSM8998 (Snapdragon 835)" },
            { 0x0008B0E1, "SDM845 (Snapdragon 845)" },
            { 0x000A50E1, "SM8150 (Snapdragon 855)" },
            { 0x000A60E1, "SM8150p (Snapdragon 855+)" },
            { 0x000C30E1, "SM8250 (Snapdragon 865)" },
            { 0x000CE0E1, "SM8250 (Snapdragon 865)" },
            { 0x001350E1, "SM8350 (Snapdragon 888)" },
            { 0x001620E1, "SM8450 (Snapdragon 8 Gen 1)" },
            { 0x001900E1, "SM8475 (Snapdragon 8+ Gen 1)" },
            { 0x001CA0E1, "SM8550 (Snapdragon 8 Gen 2)" },
            { 0x0022A0E1, "SM8650 (Snapdragon 8 Gen 3)" },
            { 0x002280E1, "SM8650-AB (Snapdragon 8 Gen 3)" },
            { 0x0028C0E1, "SM8750 (Snapdragon 8 Elite)" },
            { 0x0028C0E2, "SM8750-AB (Snapdragon 8 Elite)" },
            
            // 其他
            { 0x0014A0E1, "SC8280X (Snapdragon 8cx Gen 3)" },
            { 0x000B70E1, "SDM850 (Snapdragon 850)" },
            { 0x000B80E1, "SC8180X (Snapdragon 8cx)" },
        };

        // PK Hash 前缀 -> 厂商
        public static readonly Dictionary<string, string> PkHashVendorPrefix = new Dictionary<string, string>
        {
            // OPPO
            { "2be76cee", "OPPO" },
            { "d8e3b5a8", "OPPO" },
            { "2acf3a85", "OPPO" },
            { "d53f19d2", "OPPO" },
            { "13d7a19a", "OPPO" },
            { "08239eab", "OPPO" },
            { "daedb40c", "OPPO" },
            { "f10bd691", "OPPO" },
            
            // OnePlus
            { "7c15a98d", "OnePlus" },
            { "a26bc257", "OnePlus" },
            { "3cceb55b", "OnePlus" },
            { "24de7daf", "OnePlus" },
            { "3e18a198", "OnePlus" },
            { "6519c91c", "OnePlus" },
            { "8aabc662", "OnePlus" },
            { "267bac27", "OnePlus" },
            { "a469caf8", "OnePlus" },
            
            // Xiaomi
            { "57158eaf", "Xiaomi" },
            { "355d47f9", "Xiaomi" },
            { "a7b8b825", "Xiaomi" },
            { "1c845b80", "Xiaomi" },
            { "58b4add1", "Xiaomi" },
            { "dd0cba2f", "Xiaomi" },
            { "1bebe386", "Xiaomi" },
            
            // Vivo
            { "60ba997f", "Vivo" },
            { "2c0a52ff", "Vivo" },
            { "2e8bd2f5", "Vivo" },
            
            // Samsung
            { "6e1f1dfa", "Samsung" },
            { "893ed73f", "Samsung" },
            { "79f3c689", "Samsung" },
            { "b2f2bb07", "Samsung" },
            { "7dad1baf", "Samsung" },
            { "4dcefbb1", "Samsung" },
            
            // Motorola
            { "628be3f4", "Motorola" },
            { "99cbafe8", "Motorola" },
            { "140f82e9", "Motorola" },
            { "09108969", "Motorola" },
            
            // Lenovo
            { "5cb51521", "Lenovo" },
            { "99c8c13e", "Lenovo" },
            { "1be87f7c", "Lenovo" },
            { "a5984742", "Lenovo" },
            
            // ZTE
            { "168d0bad", "ZTE" },
            { "07cb63f6", "ZTE" },
            { "6ab694e7", "ZTE" },
            
            // Asus
            { "18000eb7", "Asus" },
            { "1e5d0b2a", "Asus" },
            { "872011aa", "Asus" },
            { "b965addf", "Asus" },
            
            // Nokia
            { "7fe240dd", "Nokia" },
            { "441e29fd", "Nokia" },
            
            // Huawei
            { "6bc36951", "Huawei" },
            { "5ef1d112", "Huawei" },
            
            // LG
            { "1030cd12", "LG" },
            { "2cf7619a", "LG" },
            
            // Nothing
            { "6a4ee8e1", "Nothing" },
            
            // BlackShark
            { "acb46529", "BlackShark" },
            { "423e32d3", "BlackShark" },
            
            // Qualcomm
            { "cc3153a8", "Qualcomm" },
            { "7be49b72", "Qualcomm" },
            { "afca69d4", "Qualcomm" },
        };

        /// <summary>
        /// 获取芯片名称
        /// </summary>
        public static string GetChipName(uint hwId)
        {
            string name;
            if (MsmIds.TryGetValue(hwId, out name))
                return name;

            // 尝试简化的 HWID
            uint simplifiedId = hwId & 0x00FFFFFF;
            if (MsmIds.TryGetValue(simplifiedId, out name))
                return name;

            return "Unknown";
        }

        /// <summary>
        /// 获取厂商名称
        /// </summary>
        public static string GetVendorName(ushort oemId)
        {
            string name;
            if (VendorIds.TryGetValue(oemId, out name))
                return name;
            return string.Format("Unknown (0x{0:X4})", oemId);
        }

        /// <summary>
        /// 根据 PK Hash 获取厂商
        /// </summary>
        public static string GetVendorByPkHash(string pkHash)
        {
            if (string.IsNullOrEmpty(pkHash) || pkHash.Length < 8)
                return "Unknown";

            string prefix = pkHash.ToLowerInvariant().Substring(0, 8);
            string vendor;
            if (PkHashVendorPrefix.TryGetValue(prefix, out vendor))
                return vendor;

            return "Unknown";
        }

        /// <summary>
        /// 获取 PK Hash 详细信息
        /// </summary>
        public static string GetPkHashInfo(string pkHash)
        {
            if (string.IsNullOrEmpty(pkHash))
                return "Unknown";

            string vendor = GetVendorByPkHash(pkHash);
            if (vendor != "Unknown")
                return vendor + " SecBoot";

            // 检查是否为空 Hash (无安全启动)
            if (pkHash.StartsWith("0000000000"))
                return "No SecBoot (Unlocked)";

            return "Custom OEM";
        }

        /// <summary>
        /// 判断是否需要 VIP 认证
        /// </summary>
        public static bool RequiresVipAuth(string pkHash)
        {
            string vendor = GetVendorByPkHash(pkHash);
            return vendor == "OPPO" || vendor == "OnePlus" || vendor == "Realme";
        }

        /// <summary>
        /// 获取存储类型
        /// </summary>
        public static MemoryType GetMemoryType(string chipName)
        {
            if (string.IsNullOrEmpty(chipName))
                return MemoryType.Ufs;

            // UFS 设备
            if (chipName.StartsWith("SM8") || chipName.StartsWith("SC8"))
                return MemoryType.Ufs;

            // eMMC 设备
            if (chipName.Contains("MSM891") || chipName.Contains("MSM890") ||
                chipName.Contains("SDM4") || chipName.Contains("SDM6"))
                return MemoryType.Emmc;

            return MemoryType.Ufs;
        }
    }

    /// <summary>
    /// 芯片详细信息
    /// </summary>
    public class QualcommChipInfo
    {
        public string SerialHex { get; set; }
        public uint SerialDec { get; set; }
        public string HwIdHex { get; set; }
        public uint MsmId { get; set; }
        public ushort OemId { get; set; }
        public string ChipName { get; set; }
        public string Vendor { get; set; }
        public string PkHash { get; set; }
        public string PkHashInfo { get; set; }

        public QualcommChipInfo()
        {
            SerialHex = "";
            HwIdHex = "";
            ChipName = "Unknown";
            Vendor = "Unknown";
            PkHash = "";
            PkHashInfo = "";
        }
    }
}
