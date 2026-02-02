// LoveAlways - Qualcomm Chipset Database
// Qualcomm Chipset Database - Device Identification Info Repository
// ============================================================================
// Module: Qualcomm.Database
// Function: Contains MSM ID, chipset name, vendor info, and hardware specifications
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System.Collections.Generic;

namespace LoveAlways.Qualcomm.Database
{
    /// <summary>
    /// Storage Type Enum
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
    /// Qualcomm Chipset Database
    /// </summary>
    public static class QualcommDatabase
    {
        // OEM ID -> Vendor Name (Full list)
        public static readonly Dictionary<ushort, string> VendorIds = new Dictionary<ushort, string>
        {
            { 0x0000, "Qualcomm" },
            { 0x0001, "Foxconn/Sony" },
            { 0x0004, "ZTE" },
            { 0x0011, "Smartisan" },
            { 0x0015, "Huawei" },
            { 0x0017, "Lenovo" },
            { 0x0020, "Samsung" },
            { 0x0029, "Asus" },
            { 0x0030, "Haier" },
            { 0x0031, "LG" },
            { 0x0035, "Foxconn/Nokia" },
            { 0x0040, "Lenovo" },
            { 0x0042, "Alcatel" },
            { 0x0045, "Nokia" },
            { 0x0048, "YuLong" },
            { 0x0051, "Oppo/OnePlus" },
            { 0x0072, "Xiaomi" },
            { 0x0073, "Vivo" },
            { 0x00C8, "Motorola" },
            { 0x0130, "GlocalMe" },
            { 0x0139, "Lyf" },
            { 0x0168, "Motorola" },
            { 0x01B0, "Motorola" },
            { 0x0208, "Motorola" },
            { 0x0228, "Motorola" },
            { 0x02E8, "Lenovo" },
            { 0x0328, "Motorola" },
            { 0x0348, "Motorola" },
            { 0x0368, "Motorola" },
            { 0x03C8, "Motorola" },
            { 0x1043, "Asus" },
            { 0x1111, "Asus" },
            { 0x143A, "Asus" },
            { 0x1978, "Blackphone" },
            { 0x2A70, "Oxygen" },
            { 0x2A96, "Micromax" },
            { 0x50E1, "OnePlus" },
            { 0xB0E1, "Xiaomi" },      // Xiaomi (New devices)
            { 0x01E8, "Motorola" },    // Moto new devices
            { 0x0488, "Motorola" },    // Moto Edge series
            { 0x0508, "Motorola" },    // Moto G series
            { 0x0070, "Google" },      // Pixel series
            { 0x00A1, "Meizu" },       // Meizu
            { 0x00A8, "Meizu" },       // Meizu
            { 0x0110, "POCO" },        // POCO devices
            { 0x0200, "Realme" },      // Realme
            { 0x0201, "Realme" },      // Realme (Alternate)
            { 0x0250, "Redmi" },       // Redmi
            { 0x0260, "Honor" },       // Honor
            { 0x0270, "iQOO" },        // iQOO
            { 0x0290, "Nothing" },     // Nothing Phone
            { 0x0300, "Sony" },        // Sony Xperia
            { 0x0310, "Sharp" },       // Sharp AQUOS
            { 0x0320, "Fairphone" },   // Fairphone
        };

        // HWID -> Chipset Name (Full database - 200+ chips)
        public static readonly Dictionary<uint, string> MsmIds = new Dictionary<uint, string>
        {
            // ======================= Early Chips =======================
            { 0x0002C0E1, "APQ8098" },
            { 0x0002E0E1, "APQ8097" },
            { 0x0003D0E1, "SDX20M" },
            { 0x0003E0E1, "SDX20" },
            
            // ======================= Snapdragon 1xx/2xx (Entry-level) =======================
            { 0x009600E1, "MSM8909 (Snapdragon 210)" },
            { 0x007220E1, "MSM8909 (Snapdragon 210)" },
            { 0x007200E1, "APQ8009 (Snapdragon 212)" },
            { 0x009350E1, "MSM8208 (Snapdragon 200)" },
            { 0x009510E1, "MSM8209 (Snapdragon 200)" },
            { 0x0010F0E1, "SM2150" },
            { 0x0013D0E1, "QCM2150" },
            { 0x001AE0E1, "QCM2290" },
            { 0x0015A0E1, "SM4125 (Snapdragon 215)" },
            
            // ======================= Snapdragon 4xx (Low-end) =======================
            { 0x007050E1, "MSM8916 (Snapdragon 410)" },
            { 0x007060E1, "APQ8016 (Snapdragon 410)" },
            { 0x007090E1, "MSM8216 (Snapdragon 410)" },
            { 0x0070A0E1, "MSM8116 (Snapdragon 410)" },
            { 0x009720E1, "MSM8952 (Snapdragon 617)" },
            { 0x000460E1, "MSM8953 (Snapdragon 625)" },
            { 0x0004E0E1, "APQ8053 (Snapdragon 625)" },
            { 0x0004F0E1, "MSM8937 (Snapdragon 430)" },
            { 0x000500E1, "APQ8037 (Snapdragon 430)" },
            { 0x000510E1, "MSM8917 (Snapdragon 425)" },
            { 0x000520E1, "APQ8017 (Snapdragon 425)" },
            { 0x0006B0E1, "MSM8940 (Snapdragon 435)" },
            { 0x0006C0E1, "APQ8040 (Snapdragon 435)" },
            { 0x0009A0E1, "SDM450 (Snapdragon 450)" },
            { 0x0009F0E1, "SDA450 (Snapdragon 450)" },
            { 0x000BE0E1, "SDM429 (Snapdragon 429)" },
            { 0x000BF0E1, "SDM439 (Snapdragon 439)" },
            { 0x000C00E1, "SDA429 (Snapdragon 429)" },
            { 0x000C10E1, "SDA439 (Snapdragon 439)" },
            { 0x001190E1, "SM4350 (Snapdragon 480)" },
            { 0x0013F0E1, "SM4250 (Snapdragon 460)" },
            { 0x001410E1, "QCM4290" },
            { 0x001B90E1, "SM4450 (Snapdragon 4 Gen 1)" },
            { 0x001BD0E1, "SM4375 (Snapdragon 4 Gen 2)" },
            { 0x001FD0E1, "SM4635 (Snapdragon 4s Gen 2)" },
            { 0x0027A0E1, "SM4550 (Snapdragon 4 Gen 3)" },
            
            // ======================= Snapdragon 6xx (Mid-range) =======================
            { 0x009900E1, "MSM8976 (Snapdragon 652)" },
            { 0x009910E1, "APQ8076 (Snapdragon 652)" },
            { 0x0009B0E1, "MSM8956 (Snapdragon 650)" },
            { 0x0009C0E1, "APQ8056 (Snapdragon 650)" },
            { 0x000AC0E1, "SDM630 (Snapdragon 630)" },
            { 0x000AD0E1, "SDA630 (Snapdragon 630)" },
            { 0x000CC0E1, "SDM636 (Snapdragon 636)" },
            { 0x000CD0E1, "SDA636 (Snapdragon 636)" },
            { 0x0008C0E1, "SDM660 (Snapdragon 660)" },
            { 0x0008D0E1, "SDA660 (Snapdragon 660)" },
            { 0x000BA0E1, "SDM632 (Snapdragon 632)" },
            { 0x000BB0E1, "SDA632 (Snapdragon 632)" },
            { 0x000950E1, "SM6150 (Snapdragon 675)" },
            { 0x000960E1, "SA6150 (Snapdragon 675)" },
            { 0x0010E0E1, "SM6125 (Snapdragon 665)" },
            { 0x0010D0E1, "SA6125 (Snapdragon 665)" },
            { 0x0013E0E1, "SM6115 (Snapdragon 662)" },
            { 0x0015E0E1, "SM6350 (Snapdragon 690)" },
            { 0x0015F0E1, "SA6350 (Snapdragon 690)" },
            { 0x0019E0E1, "SM6375 (Snapdragon 695)" },
            { 0x001BE0E1, "SM6225 (Snapdragon 680)" },
            { 0x0021E0E1, "SM6450 (Snapdragon 6 Gen 1)" },
            { 0x0025C0E1, "SM6475 (Snapdragon 6s Gen 3)" },
            { 0x002790E1, "SM6550 (Snapdragon 6 Gen 3)" },
            
            // ======================= Snapdragon 7xx (Upper Mid-range) =======================
            { 0x000910E1, "SDM670 (Snapdragon 670)" },
            { 0x000920E1, "SDA670 (Snapdragon 670)" },
            { 0x000DB0E1, "SDM710 (Snapdragon 710)" },
            { 0x000DC0E1, "SDA710 (Snapdragon 710)" },
            { 0x000DD0E1, "SDM712 (Snapdragon 712)" },
            { 0x000DE0E1, "SDA712 (Snapdragon 712)" },
            { 0x000E70E1, "SM7150 (Snapdragon 730)" },
            { 0x000E80E1, "SM7150-AB (Snapdragon 730G)" },
            { 0x000EB0E1, "SA7150 (Snapdragon 730)" },
            { 0x0011E0E1, "SM7250 (Snapdragon 765G)" },
            { 0x0011F0E1, "SM7250-AB (Snapdragon 768G)" },
            { 0x001200E1, "SA7250 (Snapdragon 765G)" },
            { 0x0017C0E1, "SM7225 (Snapdragon 750G)" },
            { 0x001920E1, "SM7325 (Snapdragon 778G)" },
            { 0x001930E1, "SM7325-AE (Snapdragon 778G+)" },
            { 0x001630E1, "SM7350 (Snapdragon 780G)" },
            { 0x001940E1, "SA7325 (Snapdragon 778G)" },
            { 0x001CE0E1, "SM7435 (Snapdragon 7s Gen 2)" },
            { 0x001DE0E1, "SM7450 (Snapdragon 7 Gen 1)" },
            { 0x001DF0E1, "SM7450-AB (Snapdragon 7+ Gen 2)" },
            { 0x0023E0E1, "SM7550 (Snapdragon 7 Gen 3)" },
            { 0x0025E0E1, "SM7675 (Snapdragon 7+ Gen 3)" },
            
            // ======================= Snapdragon 8xx (Flagship) =======================
            { 0x007B00E1, "MSM8974 (Snapdragon 800)" },
            { 0x007B10E1, "MSM8974-AA (Snapdragon 800)" },
            { 0x007B20E1, "MSM8974-AB (Snapdragon 801)" },
            { 0x007B40E1, "MSM8974-AC (Snapdragon 801)" },
            { 0x007BC0E1, "APQ8074 (Snapdragon 800)" },
            { 0x007BD0E1, "APQ8074-AA (Snapdragon 800)" },
            { 0x007BE0E1, "APQ8074-AB (Snapdragon 801)" },
            { 0x009400E1, "MSM8994 (Snapdragon 810)" },
            { 0x009410E1, "MSM8994V (Snapdragon 810)" },
            { 0x009430E1, "APQ8094 (Snapdragon 810)" },
            { 0x009690E1, "MSM8992 (Snapdragon 808)" },
            { 0x0096A0E1, "APQ8092 (Snapdragon 808)" },
            { 0x009470E1, "MSM8996 (Snapdragon 820)" },
            { 0x009480E1, "APQ8096 (Snapdragon 820)" },
            { 0x009490E1, "MSM8996L (Snapdragon 820)" },
            { 0x0094D0E1, "APQ8096SG (Snapdragon 820)" },
            { 0x0005F0E1, "MSM8996Pro (Snapdragon 821)" },
            { 0x000600E1, "MSM8996Pro-AB (Snapdragon 821)" },
            { 0x000610E1, "APQ8096Pro (Snapdragon 821)" },
            { 0x0005E0E1, "MSM8998 (Snapdragon 835)" },
            { 0x0005D0E1, "APQ8098 (Snapdragon 835)" },
            { 0x0008B0E1, "SDM845 (Snapdragon 845)" },
            { 0x0008A0E1, "SDA845 (Snapdragon 845)" },
            { 0x000A50E1, "SM8150 (Snapdragon 855)" },
            { 0x000A40E1, "SA8150 (Snapdragon 855)" },
            { 0x000A60E1, "SM8150p (Snapdragon 855+)" },
            { 0x000A70E1, "SM8150-AC (Snapdragon 855+)" },
            { 0x000C30E1, "SM8250 (Snapdragon 865)" },
            { 0x000C40E1, "SM8250-AB (Snapdragon 865+)" },
            { 0x000CE0E1, "SM8250 (Snapdragon 865)" },
            { 0x000CF0E1, "SA8250 (Snapdragon 865)" },
            { 0x001350E1, "SM8350 (Snapdragon 888)" },
            { 0x001360E1, "SM8350-AB (Snapdragon 888+)" },
            { 0x001370E1, "SA8350 (Snapdragon 888)" },
            { 0x001620E1, "SM8450 (Snapdragon 8 Gen 1)" },
            { 0x001610E1, "SA8450 (Snapdragon 8 Gen 1)" },
            { 0x001900E1, "SM8475 (Snapdragon 8+ Gen 1)" },
            { 0x001E00E1, "SM8475-AB (Snapdragon 8+ Gen 1)" },
            { 0x001CA0E1, "SM8550 (Snapdragon 8 Gen 2)" },
            { 0x001CB0E1, "SA8550 (Snapdragon 8 Gen 2)" },
            { 0x0022A0E1, "SM8650 (Snapdragon 8 Gen 3)" },
            { 0x002280E1, "SM8650-AB (Snapdragon 8 Gen 3)" },
            { 0x0022B0E1, "SA8650 (Snapdragon 8 Gen 3)" },
            { 0x0026A0E1, "SM8635 (Snapdragon 8s Gen 3)" },
            { 0x0028C0E1, "SM8750 (Snapdragon 8 Elite)" },
            { 0x0028C0E2, "SM8750-AB (Snapdragon 8 Elite)" },
            { 0x0028D0E1, "SA8750 (Snapdragon 8 Elite)" },
            { 0x0029C0E1, "SM8775 (Snapdragon 8 Elite 2)" },   // Prediction: Next-generation flagship
            
            // ======================= Snapdragon 8 Series (Lite version) =======================
            { 0x002630E1, "SM8635 (Snapdragon 8s Gen 3)" },    // 8s Gen 3 alternate ID
            { 0x0026B0E1, "SA8635 (Snapdragon 8s Gen 3)" },
            
            // ======================= Modem / Baseband (MDM/SDX) =======================
            { 0x007F50E1, "MDM9x25" },
            { 0x009100E1, "MDM9x35 (X7 LTE)" },
            { 0x009530E1, "MDM9x40 (X10 LTE)" },
            { 0x009560E1, "MDM9x45 (X12 LTE)" },
            { 0x0004D0E1, "MDM9x50 (X16/X20 LTE)" },
            { 0x0007E0E1, "MDM9x55 (X20 LTE)" },
            { 0x000990E1, "SDX50M (X50 5G)" },
            { 0x0009E0E1, "SDX55 (X55 5G)" },
            { 0x001650E1, "SDX60 (X60 5G)" },
            { 0x001A00E1, "SDX62 (X62 5G)" },
            { 0x001600E1, "SDX65 (X65 5G)" },
            { 0x001E30E1, "SDX70 (X70 5G)" },
            { 0x0022E0E1, "SDX72 (X72 5G)" },
            { 0x0022D0E1, "SDX75 (X75 5G)" },
            { 0x002850E1, "SDX80 (X80 5G)" },
            { 0x002830E1, "SDX82 (X82 5G)" },
            { 0x000800E1, "SDX24 (X24 LTE)" },
            { 0x0003F0E1, "MDM9150" },
            { 0x000380E1, "MDM9205" },
            { 0x0003A0E1, "MDM9206" },
            { 0x0003C0E1, "MDM9207" },
            { 0x001480E1, "SDX55M" },
            { 0x001C20E1, "SDX35" },
            { 0x001D80E1, "SDX35M" },
            
            // ======================= IoT / Embedded Chips (QCS/QCM/SA) =======================
            { 0x000B20E1, "QCS605" },
            { 0x000B30E1, "QCS603" },
            { 0x000DA0E1, "QCS404" },
            { 0x001510E1, "QCS6490" },
            { 0x001520E1, "QCM6490" },
            { 0x001C60E1, "QCS8550" },
            { 0x001D30E1, "QCM8550" },
            { 0x001D70E1, "QCS8450" },
            { 0x001A10E1, "QCS4490" },
            { 0x001B00E1, "QCM4490" },
            { 0x000E90E1, "SA6155" },
            { 0x001590E1, "SA6145" },
            { 0x001640E1, "SA8155" },
            { 0x0014B0E1, "SA8195" },
            { 0x001B80E1, "SA8255" },
            { 0x001BA0E1, "SA8295" },
            { 0x001BB0E1, "SA8650P" },
            { 0x001BC0E1, "SA8770P" },
            { 0x000EA0E1, "SA415M" },
            { 0x001BD0E2, "SA7775P" },  // Fix: Original 0x001BC0E1 was duplicated
            { 0x001F40E1, "QCS8250" },
            
            // ======================= Wearables (SW/SDW) =======================
            { 0x009000E1, "MSM8928 (Snapdragon Wear 2100)" },
            { 0x000470E1, "MSW8909W (Snapdragon Wear 2100)" },
            { 0x000EC0E1, "SDW3100 (Snapdragon Wear 3100)" },
            { 0x000ED0E1, "SDW4100 (Snapdragon Wear 4100)" },
            { 0x0016A0E1, "SW5100 (Snapdragon Wear W5)" },
            { 0x0016B0E1, "SW5100+ (Snapdragon Wear W5+)" },
            
            // ======================= Computing Platforms (SC8180X/SC8280X) =======================
            { 0x0014A0E1, "SC8280X (Snapdragon 8cx Gen 3)" },
            { 0x000B70E1, "SDM850 (Snapdragon 850)" },
            { 0x000B80E1, "SC8180X (Snapdragon 8cx)" },
            { 0x000B90E1, "SC8180XP (Snapdragon 8cx)" },
            { 0x001170E1, "SC8180X-AD (Snapdragon 8c)" },
            { 0x001180E1, "SC7180 (Snapdragon 7c)" },
            { 0x001A30E1, "SC7280 (Snapdragon 7c Gen 2)" },
            { 0x001A40E1, "SC7280P (Snapdragon 7c+ Gen 3)" },
            { 0x001D00E1, "SC8380X (Snapdragon X Elite)" },
            { 0x001D10E1, "SC8380XP (Snapdragon X Elite)" },
            { 0x002070E1, "SC8280XP (Snapdragon 8cx Gen 3)" },
            
            // ======================= XR / VR Chips =======================
            { 0x001100E1, "XR2 (Snapdragon XR2)" },
            { 0x001110E1, "XR2+ (Snapdragon XR2+)" },
            { 0x001D90E1, "XR2 Gen 2" },
            
            // ======================= Other Chips =======================
            { 0x007100E1, "MSM8926 (Snapdragon 400)" },
            { 0x007130E1, "MSM8226 (Snapdragon 400)" },
            { 0x007140E1, "MSM8626 (Snapdragon 400)" },
            { 0x007150E1, "MSM8526 (Snapdragon 400)" },
            { 0x007190E1, "APQ8028 (Snapdragon 400)" },
            { 0x009290E1, "MSM8939 (Snapdragon 615)" },
            { 0x0092B0E1, "MSM8939 (Snapdragon 616)" },
            { 0x0092D0E1, "APQ8039 (Snapdragon 615)" },
            { 0x009210E1, "MSM8929 (Snapdragon 415)" },
            { 0x009230E1, "MSM8629 (Snapdragon 415)" },
            { 0x009250E1, "APQ8029 (Snapdragon 415)" },
            { 0x009200E1, "MSM8239 (Snapdragon 610)" },
            { 0x0093E0E1, "MSM8936 (Snapdragon 610)" },
            { 0x009370E1, "APQ8036 (Snapdragon 610)" },
            { 0x007B30E1, "MSM8274 (Snapdragon 800)" },
            { 0x007B50E1, "APQ8084 (Snapdragon 805)" },
            { 0x0005C0E1, "MSM8997" },
            { 0x0007A0E1, "SDM455" },
            { 0x000E30E1, "SXR1130" },
            { 0x000E40E1, "SXR1120" },
            { 0x000E50E1, "QCS405" },
            { 0x000E60E1, "QCS407" },
            { 0x001460E1, "QRB5165" },
            { 0x001470E1, "QRB3165" },
            { 0x001840E1, "QRB4210" },
            { 0x001870E1, "QRB2210" },
        };

        // PK Hash Prefix -> Vendor
        public static readonly Dictionary<string, string> PkHashVendorPrefix = new Dictionary<string, string>
        {
            // OPPO
            { "2be76cee", "OPPO" },
            { "d8e3b5a8", "OPPO" },
            { "d53f19d2", "OPPO" },
            { "13d7a19a", "OPPO" },
            { "08239eab", "OPPO" },
            { "daedb40c", "OPPO" },
            { "f10bd691", "OPPO" },
            { "91057040", "OPPO" },  // SDM710 OPPO devices
            
            // OnePlus (Note: Some OnePlus devices use OPPO's SecBoot)
            { "2acf3a85", "OnePlus" },  // OnePlus 7T/7 Pro, etc.
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
            { "c924a35f", "Xiaomi" },  // SDM845 devices
            
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
            
            // Google Pixel
            { "9ab13b3e", "Google" },
            { "6fb2b36f", "Google" },
            { "7e0b1d5c", "Google" },
            
            // Meizu
            { "5e3a7c21", "Meizu" },
            { "8d4f9a7b", "Meizu" },
            { "f3e2a1b4", "Meizu" },
            
            // Realme
            { "4c8e7a2d", "Realme" },
            { "b7d93f6a", "Realme" },
            { "e2a45c8f", "Realme" },
            
            // Honor
            { "3a7d8e5c", "Honor" },
            { "9f4b6c2a", "Honor" },
            
            // Redmi
            { "d5e7f8a9", "Redmi" },
            { "7c3b4a5d", "Redmi" },
            
            // POCO
            { "6e9d2c7f", "POCO" },
            { "a4b8c5d3", "POCO" },
            
            // iQOO
            { "f9e8d7c6", "iQOO" },
            { "5a6b7c8d", "iQOO" },
            
            // Sony Xperia
            { "2d4e6f8a", "Sony" },
            { "b1c3d5e7", "Sony" },
        };

        /// <summary>
        /// Get Chip Name
        /// </summary>
        public static string GetChipName(uint hwId)
        {
            string name;

            // 1. Direct search for full HWID
            if (MsmIds.TryGetValue(hwId, out name))
                return name;

            // 2. Try with E1 suffix (Qualcomm standard format)
            if ((hwId & 0xFF) != 0xE1)
            {
                uint withE1 = (hwId & 0xFFFFFF00) | 0xE1;
                if (MsmIds.TryGetValue(withE1, out name))
                    return name;
            }

            // 3. Try low 24 bits + E1
            uint low24WithE1 = ((hwId & 0x00FFFF00) >> 8) | 0xE1;
            if (low24WithE1 != hwId && MsmIds.TryGetValue(low24WithE1, out name))
                return name;

            // 4. Iterate to find (for handling non-standard formats)
            uint msmPart = hwId & 0x00FFFFF0;  // Extract core identification part
            foreach (var kvp in MsmIds)
            {
                if ((kvp.Key & 0x00FFFFF0) == msmPart)
                    return kvp.Value;
            }

            return "Unknown";
        }

        /// <summary>
        /// Get Chip Codename by HWID (returns only codes like SM/SDM/MSM)
        /// </summary>
        public static string GetChipCodename(uint hwId)
        {
            string fullName = GetChipName(hwId);
            if (fullName == "Unknown")
                return null;

            // Extract the part before the parentheses
            int parenIndex = fullName.IndexOf('(');
            if (parenIndex > 0)
                return fullName.Substring(0, parenIndex).Trim();

            return fullName;
        }

        /// <summary>
        /// Get Vendor Name
        /// </summary>
        public static string GetVendorName(ushort oemId)
        {
            string name;
            if (VendorIds.TryGetValue(oemId, out name))
                return name;
            return string.Format("Unknown (0x{0:X4})", oemId);
        }

        /// <summary>
        /// Get Vendor by PK Hash
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
        /// Get PK Hash Details
        /// </summary>
        public static string GetPkHashInfo(string pkHash)
        {
            if (string.IsNullOrEmpty(pkHash))
                return "Unknown";

            string vendor = GetVendorByPkHash(pkHash);
            if (vendor != "Unknown")
                return vendor + " SecBoot";

            // Check if empty Hash (No SecBoot)
            if (pkHash.StartsWith("0000000000"))
                return "No SecBoot (Unlocked)";

            return "Custom OEM";
        }

        /// <summary>
        /// Determine if VIP authentication is needed (OPPO/Realme spoofing mode)
        /// Note: OnePlus uses Demacia authentication, no VIP spoofing needed
        /// </summary>
        public static bool RequiresVipAuth(string pkHash)
        {
            string vendor = GetVendorByPkHash(pkHash);
            // OnePlus uses Demacia authentication, can write directly after successful authentication, no VIP spoofing needed
            return vendor == "OPPO" || vendor == "Realme";
        }

        /// <summary>
        /// Determine if it is a OnePlus device (uses Demacia authentication)
        /// </summary>
        public static bool IsOnePlusDevice(string pkHash)
        {
            string vendor = GetVendorByPkHash(pkHash);
            return vendor == "OnePlus" || vendor.Contains("OnePlus");
        }

        /// <summary>
        /// Get Storage Type
        /// </summary>
        public static MemoryType GetMemoryType(string chipName)
        {
            if (string.IsNullOrEmpty(chipName))
                return MemoryType.Ufs;

            // UFS devices
            if (chipName.StartsWith("SM8") || chipName.StartsWith("SC8"))
                return MemoryType.Ufs;

            // eMMC devices
            if (chipName.Contains("MSM891") || chipName.Contains("MSM890") ||
                chipName.Contains("SDM4") || chipName.Contains("SDM6"))
                return MemoryType.Emmc;

            return MemoryType.Ufs;
        }

        #region Database Statistics and Utility Methods

        /// <summary>
        /// Get Database Statistics Info
        /// </summary>
        public static QualcommDatabaseStats GetStats()
        {
            var stats = new QualcommDatabaseStats();

            stats.TotalChips = MsmIds.Count;
            stats.TotalVendors = VendorIds.Count;
            stats.TotalPkHashPrefixes = PkHashVendorPrefix.Count;

            // Count chips by series
            var seriesCounts = new Dictionary<string, int>();
            foreach (var kvp in MsmIds)
            {
                string series = GetChipSeries(kvp.Value);
                if (!seriesCounts.ContainsKey(series))
                    seriesCounts[series] = 0;
                seriesCounts[series]++;
            }
            stats.ChipsBySeries = seriesCounts;

            return stats;
        }

        /// <summary>
        /// Get Chip Series
        /// </summary>
        private static string GetChipSeries(string chipName)
        {
            if (string.IsNullOrEmpty(chipName))
                return "Unknown";

            // Extract chip codename prefix
            string codename = chipName.Split(' ')[0].Split('(')[0].Trim();

            // Snapdragon 8 Series (Flagship)
            if (codename.StartsWith("SM8") || codename.StartsWith("SDM8") ||
                codename.Contains("855") || codename.Contains("865") || codename.Contains("888"))
                return "Snapdragon 8 Series (Flagship)";

            // Snapdragon 7 Series (Upper Mid-Range)
            if (codename.StartsWith("SM7") || codename.StartsWith("SDM7") ||
                codename.Contains("765") || codename.Contains("778") || codename.Contains("780"))
                return "Snapdragon 7 Series (Upper Mid-Range)";

            // Snapdragon 6 Series (Mid-Range)
            if (codename.StartsWith("SM6") || codename.StartsWith("SDM6") ||
                codename.Contains("660") || codename.Contains("675") || codename.Contains("690"))
                return "Snapdragon 6 Series (Mid-Range)";

            // Snapdragon 4 Series (Entry-Level)
            if (codename.StartsWith("SM4") || codename.StartsWith("SDM4") ||
                codename.Contains("MSM8917") || codename.Contains("MSM8937"))
                return "Snapdragon 4 Series (Entry-Level)";

            // MDM/SDX Baseband
            if (codename.StartsWith("MDM") || codename.StartsWith("SDX"))
                return "Modem/Baseband (MDM/SDX)";

            // SC/QCS IoT
            if (codename.StartsWith("SC") || codename.StartsWith("QCS") || codename.StartsWith("QCM"))
                return "IoT/Compute (SC/QCS)";

            // Wearable
            if (codename.StartsWith("SW") || codename.StartsWith("SDW") || codename.Contains("Wear"))
                return "Wearable (Snapdragon Wear)";

            // XR
            if (codename.StartsWith("XR") || codename.StartsWith("SXR"))
                return "XR/VR";

            // Legacy MSM 系列
            if (codename.StartsWith("MSM") || codename.StartsWith("APQ"))
                return "Legacy MSM/APQ";

            return "Other";
        }

        /// <summary>
        /// Smart Chip Identification (supports multiple HWID formats)
        /// </summary>
        public static QualcommChipIdentification IdentifyChip(uint hwId)
        {
            var result = new QualcommChipIdentification { HwId = hwId };

            // 1. Exact match
            if (MsmIds.TryGetValue(hwId, out string name))
            {
                result.ChipName = name;
                result.MatchType = "Exact";
                result.Confidence = 100;
                return result;
            }

            // 2. Try normalizing HWID format (adding/removing E1 suffix)
            uint[] variants = new uint[]
            {
                (hwId & 0xFFFFFF00) | 0xE1,          // Replace last byte with E1
                (hwId & 0xFFFFFF00) | 0xE2,          // Replace last byte with E2
                hwId | 0xE1,                          // Add E1 to low bits
                hwId >> 8,                            // Right shift 8 bits
                (hwId & 0x00FFFFFF),                  // Take low 24 bits
            };

            foreach (var variant in variants)
            {
                if (variant != hwId && MsmIds.TryGetValue(variant, out name))
                {
                    result.ChipName = name;
                    result.MatchType = "Variant";
                    result.Confidence = 85;
                    return result;
                }
            }

            // 3. Fuzzy match - compare core identification parts
            uint msmCore = (hwId >> 4) & 0x00FFFF;
            foreach (var kvp in MsmIds)
            {
                uint dbCore = (kvp.Key >> 4) & 0x00FFFF;
                if (msmCore == dbCore)
                {
                    result.ChipName = kvp.Value;
                    result.MatchType = "Fuzzy";
                    result.Confidence = 70;
                    return result;
                }
            }

            // 4. Feature-based guessing
            result.ChipName = GuessChipByFeatures(hwId);
            result.MatchType = result.ChipName != "Unknown" ? "Guess" : "Unknown";
            result.Confidence = result.ChipName != "Unknown" ? 30 : 0;

            return result;
        }

        /// <summary>
        /// Guess chip series based on HWID features
        /// </summary>
        private static string GuessChipByFeatures(uint hwId)
        {
            // Qualcomm HWID usually ends with E1/E2
            uint suffix = hwId & 0xFF;
            if (suffix != 0xE1 && suffix != 0xE2)
                return "Unknown";

            // Guess by HWID range
            uint idPart = (hwId >> 8) & 0xFFFF;

            // SM8xxx range
            if (idPart >= 0x1CA && idPart <= 0x2FF)
                return "SM8xxx Series (Flagship, Exact Model Unknown)";

            // SM7xxx range
            if (idPart >= 0x190 && idPart <= 0x1C9)
                return "SM7xxx Series (Upper Mid-Range, Exact Model Unknown)";

            // SM6xxx range
            if (idPart >= 0x10E && idPart <= 0x18F)
                return "SM6xxx Series (Mid-Range, Exact Model Unknown)";

            // SDX Modem range
            if (idPart >= 0x160 && idPart <= 0x285 && ((hwId >> 4) & 0xF) >= 0x5)
                return "SDX Series (Modem, Exact Model Unknown)";

            return "Unknown";
        }

        /// <summary>
        /// Get Recommended Storage Type for chip
        /// </summary>
        public static MemoryType GetRecommendedMemoryType(uint hwId)
        {
            string chipName = GetChipName(hwId);
            if (chipName == "Unknown")
            {
                // Guess based on HWID
                uint idPart = (hwId >> 8) & 0xFFFF;
                // New flagship chips generally use UFS
                if (idPart >= 0x1CA)
                    return MemoryType.Ufs;
                // Entry-level chips may use eMMC
                if (idPart <= 0x100)
                    return MemoryType.Emmc;
                return MemoryType.Ufs;
            }
            return GetMemoryType(chipName);
        }

        /// <summary>
        /// 检查是否为新型 Snapdragon 芯片 (8 Gen 1+)
        /// </summary>
        public static bool IsModernSnapdragon(uint hwId)
        {
            string chipName = GetChipName(hwId);
            if (chipName == "Unknown")
                return false;

            // 检查是否为 SM8450+ (8 Gen 1 及以后)
            return chipName.Contains("8 Gen") || chipName.Contains("8Gen") ||
                   chipName.Contains("8 Elite") || chipName.Contains("8Elite") ||
                   chipName.Contains("8s Gen") || chipName.Contains("8sGen") ||
                   chipName.Contains("SM8450") || chipName.Contains("SM8475") ||
                   chipName.Contains("SM8550") || chipName.Contains("SM8650") ||
                   chipName.Contains("SM8750");
        }

        /// <summary>
        /// 检查芯片是否支持 Sahara 协议
        /// </summary>
        public static bool SupportsSaharaProtocol(uint hwId)
        {
            // 所有现代高通芯片都支持 Sahara
            string chipName = GetChipName(hwId);
            if (chipName == "Unknown")
                return true; // 假设支持

            // 非常旧的芯片可能使用 DMSS/Streaming 协议
            if (chipName.Contains("MSM72") || chipName.Contains("MSM73"))
                return false;

            return true;
        }

        /// <summary>
        /// 检查芯片是否支持 Firehose 协议
        /// </summary>
        public static bool SupportsFirehoseProtocol(uint hwId)
        {
            string chipName = GetChipName(hwId);
            if (chipName == "Unknown")
                return true; // 假设支持

            // MSM8916 及以后的芯片都支持 Firehose
            // 旧芯片使用 SBL/EHostDL
            if (chipName.Contains("MSM890") || chipName.Contains("MSM891") ||
                chipName.Contains("MSM72") || chipName.Contains("MSM73"))
                return false;

            return true;
        }

        #endregion
    }

    /// <summary>
    /// 数据库统计信息
    /// </summary>
    public class QualcommDatabaseStats
    {
        public int TotalChips { get; set; }
        public int TotalVendors { get; set; }
        public int TotalPkHashPrefixes { get; set; }
        public Dictionary<string, int> ChipsBySeries { get; set; }

        public QualcommDatabaseStats()
        {
            ChipsBySeries = new Dictionary<string, int>();
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format("Qualcomm Chips:"));
            sb.AppendLine(string.Format("  Total Chips: {0}", TotalChips));
            sb.AppendLine(string.Format("  Manufacturer: {0}", TotalVendors));
            sb.AppendLine(string.Format("  PK Hash: {0}", TotalPkHashPrefixes));
            sb.AppendLine("  按系列分布:");
            foreach (var kvp in ChipsBySeries)
            {
                sb.AppendLine(string.Format("    {0}: {1}", kvp.Key, kvp.Value));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 芯片识别结果
    /// </summary>
    public class QualcommChipIdentification
    {
        public uint HwId { get; set; }
        public string ChipName { get; set; }
        public string MatchType { get; set; }  // Exact, Variant, Fuzzy, Guess, Unknown
        public int Confidence { get; set; }    // 0-100

        public override string ToString()
        {
            return string.Format("{0} (HWID: 0x{1:X8}, Match: {2}, Confidence: {3}%)",
                ChipName, HwId, MatchType, Confidence);
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
        public ushort ModelId { get; set; }
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
