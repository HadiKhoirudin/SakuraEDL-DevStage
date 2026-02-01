// ============================================================================
// LoveAlways - EDL Loader Database
// [Deprecated] Local PAK resources have been replaced by cloud automatic matching
// This file is kept only for offline fallback compatibility
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LoveAlways.Qualcomm.Database
{
    /// <summary>
    /// EDL Loader Local Database (Deprecated)
    /// Now uses CloudLoaderService for cloud automatic matching
    /// </summary>
    [Obsolete("Use CloudLoaderService for cloud automatic matching")]
    public static class EdlLoaderDatabase
    {
        public class LoaderInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Brand { get; set; }
            public string Chip { get; set; }
            public bool IsCommon { get; set; }
            /// <summary>
            /// Authentication Mode: "none" = No auth needed, "oneplus" = OnePlus authentication
            /// </summary>
            public string AuthMode { get; set; }
        }

        private static Dictionary<string, LoaderInfo> _database;
        private static readonly object _lock = new object();

        public static Dictionary<string, LoaderInfo> Database
        {
            get
            {
                if (_database == null)
                {
                    lock (_lock)
                    {
                        if (_database == null)
                            _database = InitDatabase();
                    }
                }
                return _database;
            }
        }

        [Obsolete("Use CloudLoaderService")]
        public static string[] GetBrands()
        {
            return new string[0]; // No longer using local database
        }
 
        [Obsolete("Use CloudLoaderService")]
        public static LoaderInfo[] GetByBrand(string brand)
        {
            return new LoaderInfo[0]; // No longer using local database
        }
 
        [Obsolete("Use CloudLoaderService")]
        public static LoaderInfo[] GetByChip(string chip)
        {
            return new LoaderInfo[0]; // No longer using local database
        }
 
        [Obsolete("Use CloudLoaderService")]
        public static byte[] LoadLoader(string id)
        {
            // No longer loading from local PAK, return null
            return null;
        }
 
        [Obsolete("Use CloudLoaderService")]
        public static bool IsPakAvailable()
        {
            // Always return false, forcing use of cloud matching
            return false;
        }

        private static Dictionary<string, LoaderInfo> InitDatabase()
        {
            var db = new Dictionary<string, LoaderInfo>(StringComparer.OrdinalIgnoreCase);

            // === 360 ===
            db["360_360N6Lite"] = new LoaderInfo { Id = "360_360N6Lite", Name = "360 360N6Lite", Brand = "360", Chip = "", IsCommon = false, AuthMode = "none" };
            db["360_360N7Pro"] = new LoaderInfo { Id = "360_360N7Pro", Name = "360 360N7Pro", Brand = "360", Chip = "", IsCommon = false, AuthMode = "none" };

            // === BBK ===
            db["BBK_660"] = new LoaderInfo { Id = "BBK_660", Name = "BBK 660 (Generic)", Brand = "BBK", Chip = "660", IsCommon = true, AuthMode = "none" };
            db["BBK_730G"] = new LoaderInfo { Id = "BBK_730G", Name = "BBK 730G (Generic)", Brand = "BBK", Chip = "730G", IsCommon = true, AuthMode = "none" };

            // === BlackShark ===
            db["BlackShark_BlackShark1"] = new LoaderInfo { Id = "BlackShark_BlackShark1", Name = "BlackShark BlackShark1", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark2"] = new LoaderInfo { Id = "BlackShark_BlackShark2", Name = "BlackShark BlackShark2", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark2Pro"] = new LoaderInfo { Id = "BlackShark_BlackShark2Pro", Name = "BlackShark BlackShark2Pro", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark3"] = new LoaderInfo { Id = "BlackShark_BlackShark3", Name = "BlackShark BlackShark3", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark3Pro"] = new LoaderInfo { Id = "BlackShark_BlackShark3Pro", Name = "BlackShark BlackShark3Pro", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark3S"] = new LoaderInfo { Id = "BlackShark_BlackShark3S", Name = "BlackShark BlackShark3S", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackShark4"] = new LoaderInfo { Id = "BlackShark_BlackShark4", Name = "BlackShark BlackShark4", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };
            db["BlackShark_BlackSharkHelo"] = new LoaderInfo { Id = "BlackShark_BlackSharkHelo", Name = "BlackShark BlackSharkHelo", Brand = "BlackShark", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Huawei ===
            db["Huawei_410"] = new LoaderInfo { Id = "Huawei_410", Name = "Huawei 410 (Generic)", Brand = "Huawei", Chip = "410", IsCommon = true, AuthMode = "none" };
            db["Huawei_425"] = new LoaderInfo { Id = "Huawei_425", Name = "Huawei 425 (Generic)", Brand = "Huawei", Chip = "425", IsCommon = true, AuthMode = "none" };
            db["Huawei_430"] = new LoaderInfo { Id = "Huawei_430", Name = "Huawei 430 (Generic)", Brand = "Huawei", Chip = "430", IsCommon = true, AuthMode = "none" };
            db["Huawei_430_2"] = new LoaderInfo { Id = "Huawei_430_2", Name = "Huawei 430 (Generic)", Brand = "Huawei", Chip = "430", IsCommon = true, AuthMode = "none" };
            db["Huawei_435"] = new LoaderInfo { Id = "Huawei_435", Name = "Huawei 435 (Generic)", Brand = "Huawei", Chip = "435", IsCommon = true, AuthMode = "none" };
            db["Huawei_450"] = new LoaderInfo { Id = "Huawei_450", Name = "Huawei 450 (Generic)", Brand = "Huawei", Chip = "450", IsCommon = true, AuthMode = "none" };
            db["Huawei_480"] = new LoaderInfo { Id = "Huawei_480", Name = "Huawei 480 (Generic)", Brand = "Huawei", Chip = "480", IsCommon = true, AuthMode = "none" };
            db["Huawei_625"] = new LoaderInfo { Id = "Huawei_625", Name = "Huawei 625 (Generic)", Brand = "Huawei", Chip = "625", IsCommon = true, AuthMode = "none" };
            db["Huawei_632"] = new LoaderInfo { Id = "Huawei_632", Name = "Huawei 632 (Generic)", Brand = "Huawei", Chip = "632", IsCommon = true, AuthMode = "none" };
            db["Huawei_636"] = new LoaderInfo { Id = "Huawei_636", Name = "Huawei 636 (Generic)", Brand = "Huawei", Chip = "636", IsCommon = true, AuthMode = "none" };
            db["Huawei_660"] = new LoaderInfo { Id = "Huawei_660", Name = "Huawei 660 (Generic)", Brand = "Huawei", Chip = "660", IsCommon = true, AuthMode = "none" };
            db["Huawei_662"] = new LoaderInfo { Id = "Huawei_662", Name = "Huawei 662 (Generic)", Brand = "Huawei", Chip = "662", IsCommon = true, AuthMode = "none" };
            db["Huawei_680"] = new LoaderInfo { Id = "Huawei_680", Name = "Huawei 680 (Generic)", Brand = "Huawei", Chip = "680", IsCommon = true, AuthMode = "none" };
            db["Huawei_680_2"] = new LoaderInfo { Id = "Huawei_680_2", Name = "Huawei 680 (Generic)", Brand = "Huawei", Chip = "680", IsCommon = true, AuthMode = "none" };
            db["Huawei_690"] = new LoaderInfo { Id = "Huawei_690", Name = "Huawei 690 (Generic)", Brand = "Huawei", Chip = "690", IsCommon = true, AuthMode = "none" };
            db["Huawei_690_2"] = new LoaderInfo { Id = "Huawei_690_2", Name = "Huawei 690 (Generic)", Brand = "Huawei", Chip = "690", IsCommon = true, AuthMode = "none" };
            db["Huawei_695"] = new LoaderInfo { Id = "Huawei_695", Name = "Huawei 695 (Generic)", Brand = "Huawei", Chip = "695", IsCommon = true, AuthMode = "none" };
            db["Huawei_695_2"] = new LoaderInfo { Id = "Huawei_695_2", Name = "Huawei 695 (Generic)", Brand = "Huawei", Chip = "695", IsCommon = true, AuthMode = "none" };
            db["Huawei_778G"] = new LoaderInfo { Id = "Huawei_778G", Name = "Huawei 778G (Generic)", Brand = "Huawei", Chip = "778G", IsCommon = true, AuthMode = "none" };
            db["Huawei_778G_2"] = new LoaderInfo { Id = "Huawei_778G_2", Name = "Huawei 778G V2 (Generic)", Brand = "Huawei", Chip = "778G", IsCommon = true, AuthMode = "none" };
            db["Huawei_8+Gen1"] = new LoaderInfo { Id = "Huawei_8+Gen1", Name = "Huawei 8+Gen1 (Generic)", Brand = "Huawei", Chip = "8+Gen1", IsCommon = true, AuthMode = "none" };
            db["Huawei_8+Gen1_2"] = new LoaderInfo { Id = "Huawei_8+Gen1_2", Name = "Huawei 8+Gen1 (Generic)", Brand = "Huawei", Chip = "8+Gen1", IsCommon = true, AuthMode = "none" };
            db["Huawei_870"] = new LoaderInfo { Id = "Huawei_870", Name = "Huawei 870 (Generic)", Brand = "Huawei", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["Huawei_870_2"] = new LoaderInfo { Id = "Huawei_870_2", Name = "Huawei 870 (Generic)", Brand = "Huawei", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["Huawei_888"] = new LoaderInfo { Id = "Huawei_888", Name = "Huawei 888 (Generic)", Brand = "Huawei", Chip = "888", IsCommon = true, AuthMode = "none" };
            db["Huawei_888_2"] = new LoaderInfo { Id = "Huawei_888_2", Name = "Huawei 888 V2 (Generic)", Brand = "Huawei", Chip = "888", IsCommon = true, AuthMode = "none" };
            db["Huawei_8Gen1"] = new LoaderInfo { Id = "Huawei_8Gen1", Name = "Huawei 8Gen1 (Generic)", Brand = "Huawei", Chip = "8Gen1", IsCommon = true, AuthMode = "none" };
            db["Huawei_8Gen1_2"] = new LoaderInfo { Id = "Huawei_8Gen1_2", Name = "Huawei 8Gen1 (Generic)", Brand = "Huawei", Chip = "8Gen1", IsCommon = true, AuthMode = "none" };
            db["Huawei_Common_615616"] = new LoaderInfo { Id = "Huawei_Common_615616", Name = "Huawei Common_615616 (Generic)", Brand = "Huawei", Chip = "", IsCommon = true, AuthMode = "none" };
            db["Huawei_Huawei_Enjoy6S"] = new LoaderInfo { Id = "Huawei_Huawei_Enjoy6S", Name = "Huawei Huawei_Enjoy6S", Brand = "Huawei", Chip = "", IsCommon = false, AuthMode = "none" };

            // === LG ===
            db["LG_765G"] = new LoaderInfo { Id = "LG_765G", Name = "LG 765G (Generic)", Brand = "LG", Chip = "765G", IsCommon = true, AuthMode = "none" };
            db["LG_835"] = new LoaderInfo { Id = "LG_835", Name = "LG 835 (Generic)", Brand = "LG", Chip = "835", IsCommon = true, AuthMode = "none" };
            db["LG_845"] = new LoaderInfo { Id = "LG_845", Name = "LG 845 (Generic)", Brand = "LG", Chip = "845", IsCommon = true, AuthMode = "none" };
            db["LG_855"] = new LoaderInfo { Id = "LG_855", Name = "LG 855 (Generic)", Brand = "LG", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["LG_865"] = new LoaderInfo { Id = "LG_865", Name = "LG 865 (Generic)", Brand = "LG", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["LG_LGG6"] = new LoaderInfo { Id = "LG_LGG6", Name = "LG LGG6", Brand = "LG", Chip = "", IsCommon = false, AuthMode = "none" };
            db["LG_LGG6_H872"] = new LoaderInfo { Id = "LG_LGG6_H872", Name = "LG LGG6-H872", Brand = "LG", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Lenovo ===
            db["Lenovo_Legion_2Pro"] = new LoaderInfo { Id = "Lenovo_Legion_2Pro", Name = "Lenovo Legion_2Pro", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Pro"] = new LoaderInfo { Id = "Lenovo_Legion_Pro", Name = "Lenovo Legion_Pro", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y70"] = new LoaderInfo { Id = "Lenovo_Legion_Y70", Name = "Lenovo Legion_Y70", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y700_2022"] = new LoaderInfo { Id = "Lenovo_Legion_Y700_2022", Name = "Lenovo Legion_Y700-2022", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y700_2023"] = new LoaderInfo { Id = "Lenovo_Legion_Y700_2023", Name = "Lenovo Legion_Y700-2023", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y700_2025"] = new LoaderInfo { Id = "Lenovo_Legion_Y700_2025", Name = "Lenovo Legion_Y700-2025", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y700_4"] = new LoaderInfo { Id = "Lenovo_Legion_Y700_4", Name = "Lenovo Legion_Y700-4", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Legion_Y90"] = new LoaderInfo { Id = "Lenovo_Legion_Y90", Name = "Lenovo Legion_Y90", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Lenovo_K5Note"] = new LoaderInfo { Id = "Lenovo_Lenovo_K5Note", Name = "Lenovo Lenovo_K5Note", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Lenovo_TabK11Plus_ddr"] = new LoaderInfo { Id = "Lenovo_Lenovo_TabK11Plus_ddr", Name = "Lenovo Lenovo_TabK11Plus_ddr", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Lenovo_TabK11Plus_lite"] = new LoaderInfo { Id = "Lenovo_Lenovo_TabK11Plus_lite", Name = "Lenovo Lenovo_TabK11Plus_lite", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_Lenovo_YOGAPadProAI"] = new LoaderInfo { Id = "Lenovo_Lenovo_YOGAPadProAI", Name = "Lenovo Lenovo_YOGAPadProAI", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_2020"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_2020", Name = "Lenovo XiaoxinPad_2020", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_2022"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_2022", Name = "Lenovo XiaoxinPad_2022", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_2024"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_2024", Name = "Lenovo XiaoxinPad_2024", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Plus"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Plus", Name = "Lenovo XiaoxinPad_Plus", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Pro12.6"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Pro12.6", Name = "Lenovo XiaoxinPad_Pro12.6", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Pro12.7"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Pro12.7", Name = "Lenovo XiaoxinPad_Pro12.7", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Pro2020"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Pro2020", Name = "Lenovo XiaoxinPad_Pro2020", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Pro2021"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Pro2021", Name = "Lenovo XiaoxinPad_Pro2021", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_Pro2022Snapdragon"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_Pro2022Snapdragon", Name = "Lenovo XiaoxinPad_Pro2022Snapdragon", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_XiaoxinPad_ProGT"] = new LoaderInfo { Id = "Lenovo_XiaoxinPad_ProGT", Name = "Lenovo XiaoxinPad_ProGT", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Lenovo_ZUK_Z2"] = new LoaderInfo { Id = "Lenovo_ZUK_Z2", Name = "Lenovo ZUK_Z2", Brand = "Lenovo", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Meizu ===
            db["Meizu_Meizu16SBeforeFlyme9"] = new LoaderInfo { Id = "Meizu_Meizu16SBeforeFlyme9", Name = "Meizu Meizu16SBeforeFlyme9", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16SFlyme9"] = new LoaderInfo { Id = "Meizu_Meizu16SFlyme9", Name = "Meizu Meizu16SFlyme9", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16SProBeforeFlyme9"] = new LoaderInfo { Id = "Meizu_Meizu16SProBeforeFlyme9", Name = "Meizu Meizu16SProBeforeFlyme9", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16SProFlyme9"] = new LoaderInfo { Id = "Meizu_Meizu16SProFlyme9", Name = "Meizu Meizu16SProFlyme9", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16T"] = new LoaderInfo { Id = "Meizu_Meizu16T", Name = "Meizu Meizu16T", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16X"] = new LoaderInfo { Id = "Meizu_Meizu16X", Name = "Meizu Meizu16X", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16XS"] = new LoaderInfo { Id = "Meizu_Meizu16XS", Name = "Meizu Meizu16XS", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16th"] = new LoaderInfo { Id = "Meizu_Meizu16th", Name = "Meizu Meizu16th", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu16thPlus"] = new LoaderInfo { Id = "Meizu_Meizu16thPlus", Name = "Meizu Meizu16thPlus", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu17"] = new LoaderInfo { Id = "Meizu_Meizu17", Name = "Meizu Meizu17", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu17Pro"] = new LoaderInfo { Id = "Meizu_Meizu17Pro", Name = "Meizu Meizu17Pro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu18"] = new LoaderInfo { Id = "Meizu_Meizu18", Name = "Meizu Meizu18", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu18Pro"] = new LoaderInfo { Id = "Meizu_Meizu18Pro", Name = "Meizu Meizu18Pro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu18S"] = new LoaderInfo { Id = "Meizu_Meizu18S", Name = "Meizu Meizu18S", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu18SPro"] = new LoaderInfo { Id = "Meizu_Meizu18SPro", Name = "Meizu Meizu18SPro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu18X"] = new LoaderInfo { Id = "Meizu_Meizu18X", Name = "Meizu Meizu18X", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu20"] = new LoaderInfo { Id = "Meizu_Meizu20", Name = "Meizu Meizu20", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu20Pro"] = new LoaderInfo { Id = "Meizu_Meizu20Pro", Name = "Meizu Meizu20Pro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu21"] = new LoaderInfo { Id = "Meizu_Meizu21", Name = "Meizu Meizu21", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu21Note"] = new LoaderInfo { Id = "Meizu_Meizu21Note", Name = "Meizu Meizu21Note", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_Meizu21Pro"] = new LoaderInfo { Id = "Meizu_Meizu21Pro", Name = "Meizu Meizu21Pro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_MeizuLucky08"] = new LoaderInfo { Id = "Meizu_MeizuLucky08", Name = "Meizu MeizuLucky08", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_MeizuM15"] = new LoaderInfo { Id = "Meizu_MeizuM15", Name = "Meizu MeizuM15", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_MeizuNote16Pro"] = new LoaderInfo { Id = "Meizu_MeizuNote16Pro", Name = "Meizu MeizuNote16Pro", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_MeizuNote8"] = new LoaderInfo { Id = "Meizu_MeizuNote8", Name = "Meizu MeizuNote8", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Meizu_MeizuX8"] = new LoaderInfo { Id = "Meizu_MeizuX8", Name = "Meizu MeizuX8", Brand = "Meizu", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Nothing ===
            db["Nothing_Nothing1"] = new LoaderInfo { Id = "Nothing_Nothing1", Name = "Nothing Nothing1", Brand = "Nothing", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Nothing_Nothing2"] = new LoaderInfo { Id = "Nothing_Nothing2", Name = "Nothing Nothing2", Brand = "Nothing", Chip = "", IsCommon = false, AuthMode = "none" };

            // === OPLUS ===
            db["OPLUS_665"] = new LoaderInfo { Id = "OPLUS_665", Name = "OPLUS 665 (Generic)", Brand = "OPLUS", Chip = "665", IsCommon = true, AuthMode = "none" };
            db["OPLUS_665_2"] = new LoaderInfo { Id = "OPLUS_665_2", Name = "OPLUS 665 (Generic)", Brand = "OPLUS", Chip = "665", IsCommon = true, AuthMode = "none" };
            db["OPLUS_710"] = new LoaderInfo { Id = "OPLUS_710", Name = "OPLUS 710 (Generic)", Brand = "OPLUS", Chip = "710", IsCommon = true, AuthMode = "none" };
            db["OPLUS_710_2"] = new LoaderInfo { Id = "OPLUS_710_2", Name = "OPLUS 710 (Generic)", Brand = "OPLUS", Chip = "710", IsCommon = true, AuthMode = "none" };
            db["OPLUS_765G"] = new LoaderInfo { Id = "OPLUS_765G", Name = "OPLUS 765G (Generic)", Brand = "OPLUS", Chip = "765G", IsCommon = true, AuthMode = "none" };
            db["OPLUS_765G_2"] = new LoaderInfo { Id = "OPLUS_765G_2", Name = "OPLUS 765G V2 (Generic)", Brand = "OPLUS", Chip = "765G", IsCommon = true, AuthMode = "none" };
            db["OPLUS_778G"] = new LoaderInfo { Id = "OPLUS_778G", Name = "OPLUS 778G (Generic)", Brand = "OPLUS", Chip = "778G", IsCommon = true, AuthMode = "none" };
            db["OPLUS_855"] = new LoaderInfo { Id = "OPLUS_855", Name = "OPLUS 855 (Generic)", Brand = "OPLUS", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["OPLUS_865"] = new LoaderInfo { Id = "OPLUS_865", Name = "OPLUS 865 (Generic)", Brand = "OPLUS", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["OPLUS_865_2"] = new LoaderInfo { Id = "OPLUS_865_2", Name = "OPLUS 865 (Generic)", Brand = "OPLUS", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["OPLUS_870"] = new LoaderInfo { Id = "OPLUS_870", Name = "OPLUS 870 (Generic)", Brand = "OPLUS", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["OPLUS_870_2"] = new LoaderInfo { Id = "OPLUS_870_2", Name = "OPLUS 870 (Generic)", Brand = "OPLUS", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["OPLUS_OnePlus_1"] = new LoaderInfo { Id = "OPLUS_OnePlus_1", Name = "OPLUS OnePlus_1", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_OnePlus_2"] = new LoaderInfo { Id = "OPLUS_OnePlus_2", Name = "OPLUS OnePlus_2", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_OnePlus_3"] = new LoaderInfo { Id = "OPLUS_OnePlus_3", Name = "OPLUS OnePlus_3", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_OnePlus_3T_ddr"] = new LoaderInfo { Id = "OPLUS_OnePlus_3T_ddr", Name = "OPLUS OnePlus_3T-ddr", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_OnePlus_3T_lite"] = new LoaderInfo { Id = "OPLUS_OnePlus_3T_lite", Name = "OPLUS OnePlus_3T-lite", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_5"] = new LoaderInfo { Id = "OPLUS_oneplus_5", Name = "OPLUS oneplus_5", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_6"] = new LoaderInfo { Id = "OPLUS_oneplus_6", Name = "OPLUS oneplus_6", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_7"] = new LoaderInfo { Id = "OPLUS_oneplus_7", Name = "OPLUS oneplus_7", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_8"] = new LoaderInfo { Id = "OPLUS_oneplus_8", Name = "OPLUS oneplus_8", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_9_870"] = new LoaderInfo { Id = "OPLUS_oneplus_9_870", Name = "OPLUS oneplus_9_870 (870)", Brand = "OPLUS", Chip = "870", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_9_888"] = new LoaderInfo { Id = "OPLUS_oneplus_9_888", Name = "OPLUS oneplus_9_888 (888)", Brand = "OPLUS", Chip = "888", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_n10"] = new LoaderInfo { Id = "OPLUS_oneplus_n10", Name = "OPLUS oneplus_n10", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_n100"] = new LoaderInfo { Id = "OPLUS_oneplus_n100", Name = "OPLUS oneplus_n100", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_nord"] = new LoaderInfo { Id = "OPLUS_oneplus_nord", Name = "OPLUS oneplus_nord", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };
            db["OPLUS_oneplus_nordn200"] = new LoaderInfo { Id = "OPLUS_oneplus_nordn200", Name = "OPLUS oneplus_nordn200", Brand = "OPLUS", Chip = "", IsCommon = false, AuthMode = "oneplus" };

            // === ROG ===
            db["ROG_ROG2"] = new LoaderInfo { Id = "ROG_ROG2", Name = "ROG ROG2", Brand = "ROG", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ROG_ROG5"] = new LoaderInfo { Id = "ROG_ROG5", Name = "ROG ROG5", Brand = "ROG", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ROG_ROG6"] = new LoaderInfo { Id = "ROG_ROG6", Name = "ROG ROG6", Brand = "ROG", Chip = "", IsCommon = false, AuthMode = "none" };

            // === ROYOLE ===
            db["ROYOLE_FlexPai2"] = new LoaderInfo { Id = "ROYOLE_FlexPai2", Name = "ROYOLE FlexPai2", Brand = "ROYOLE", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Samsung ===
            db["Samsung_Galaxy_A02s"] = new LoaderInfo { Id = "Samsung_Galaxy_A02s", Name = "Samsung Galaxy_A02s", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_A11"] = new LoaderInfo { Id = "Samsung_Galaxy_A11", Name = "Samsung Galaxy_A11", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_A52"] = new LoaderInfo { Id = "Samsung_Galaxy_A52", Name = "Samsung Galaxy_A52", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_A70"] = new LoaderInfo { Id = "Samsung_Galaxy_A70", Name = "Samsung Galaxy_A70", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_A72"] = new LoaderInfo { Id = "Samsung_Galaxy_A72", Name = "Samsung Galaxy_A72", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_J4Plus"] = new LoaderInfo { Id = "Samsung_Galaxy_J4Plus", Name = "Samsung Galaxy_J4Plus", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_J6Plus"] = new LoaderInfo { Id = "Samsung_Galaxy_J6Plus", Name = "Samsung Galaxy_J6Plus", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Samsung_Galaxy_M11"] = new LoaderInfo { Id = "Samsung_Galaxy_M11", Name = "Samsung Galaxy_M11", Brand = "Samsung", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Smartisan ===
            db["Smartisan_Nut_1"] = new LoaderInfo { Id = "Smartisan_Nut_1", Name = "Smartisan Nut_1", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_3"] = new LoaderInfo { Id = "Smartisan_Nut_3", Name = "Smartisan Nut_3", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_Pro2"] = new LoaderInfo { Id = "Smartisan_Nut_Pro2", Name = "Smartisan Nut_Pro2", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_Pro2S"] = new LoaderInfo { Id = "Smartisan_Nut_Pro2S", Name = "Smartisan Nut_Pro2S", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_Pro2SE"] = new LoaderInfo { Id = "Smartisan_Nut_Pro2SE", Name = "Smartisan Nut_Pro2SE", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_Pro3"] = new LoaderInfo { Id = "Smartisan_Nut_Pro3", Name = "Smartisan Nut_Pro3", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_R1"] = new LoaderInfo { Id = "Smartisan_Nut_R1", Name = "Smartisan Nut_R1", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Nut_R2"] = new LoaderInfo { Id = "Smartisan_Nut_R2", Name = "Smartisan Nut_R2", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Smartisan_M1L_ddr"] = new LoaderInfo { Id = "Smartisan_Smartisan_M1L_ddr", Name = "Smartisan Smartisan_M1L-ddr", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Smartisan_M1L_lite"] = new LoaderInfo { Id = "Smartisan_Smartisan_M1L_lite", Name = "Smartisan Smartisan_M1L-lite", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Smartisan_M1_ddr"] = new LoaderInfo { Id = "Smartisan_Smartisan_M1_ddr", Name = "Smartisan Smartisan_M1-ddr", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Smartisan_Smartisan_M1_lite"] = new LoaderInfo { Id = "Smartisan_Smartisan_M1_lite", Name = "Smartisan Smartisan_M1-lite", Brand = "Smartisan", Chip = "", IsCommon = false, AuthMode = "none" };

            // === XTC ===
            db["XTC_XTCWatch_Z10"] = new LoaderInfo { Id = "XTC_XTCWatch_Z10", Name = "XTC XTCWatch_Z10", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z2Y"] = new LoaderInfo { Id = "XTC_XTCWatch_Z2Y", Name = "XTC XTCWatch_Z2Y", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z3"] = new LoaderInfo { Id = "XTC_XTCWatch_Z3", Name = "XTC XTCWatch_Z3", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z5A"] = new LoaderInfo { Id = "XTC_XTCWatch_Z5A", Name = "XTC XTCWatch_Z5A", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z5Pro"] = new LoaderInfo { Id = "XTC_XTCWatch_Z5Pro", Name = "XTC XTCWatch_Z5Pro", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z5Q"] = new LoaderInfo { Id = "XTC_XTCWatch_Z5Q", Name = "XTC XTCWatch_Z5Q", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z6"] = new LoaderInfo { Id = "XTC_XTCWatch_Z6", Name = "XTC XTCWatch_Z6", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z6DFB"] = new LoaderInfo { Id = "XTC_XTCWatch_Z6DFB", Name = "XTC XTCWatch_Z6DFB", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z7"] = new LoaderInfo { Id = "XTC_XTCWatch_Z7", Name = "XTC XTCWatch_Z7", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z7A"] = new LoaderInfo { Id = "XTC_XTCWatch_Z7A", Name = "XTC XTCWatch_Z7A", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };
            db["XTC_XTCWatch_Z8"] = new LoaderInfo { Id = "XTC_XTCWatch_Z8", Name = "XTC XTCWatch_Z8", Brand = "XTC", Chip = "", IsCommon = false, AuthMode = "none" };

            // === Xiaomi ===
            db["Xiaomi_439"] = new LoaderInfo { Id = "Xiaomi_439", Name = "Xiaomi 439 (Generic)", Brand = "Xiaomi", Chip = "439", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_625"] = new LoaderInfo { Id = "Xiaomi_625", Name = "Xiaomi 625 (Generic)", Brand = "Xiaomi", Chip = "625", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_632"] = new LoaderInfo { Id = "Xiaomi_632", Name = "Xiaomi 632 (Generic)", Brand = "Xiaomi", Chip = "632", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_636"] = new LoaderInfo { Id = "Xiaomi_636", Name = "Xiaomi 636 (Generic)", Brand = "Xiaomi", Chip = "636", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_660"] = new LoaderInfo { Id = "Xiaomi_660", Name = "Xiaomi 660 (Generic)", Brand = "Xiaomi", Chip = "660", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_662"] = new LoaderInfo { Id = "Xiaomi_662", Name = "Xiaomi 662 (Generic)", Brand = "Xiaomi", Chip = "662", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_665"] = new LoaderInfo { Id = "Xiaomi_665", Name = "Xiaomi 665 (Generic)", Brand = "Xiaomi", Chip = "665", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_675"] = new LoaderInfo { Id = "Xiaomi_675", Name = "Xiaomi 675 (Generic)", Brand = "Xiaomi", Chip = "675", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_680"] = new LoaderInfo { Id = "Xiaomi_680", Name = "Xiaomi 680 (Generic)", Brand = "Xiaomi", Chip = "680", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_710"] = new LoaderInfo { Id = "Xiaomi_710", Name = "Xiaomi 710 (Generic)", Brand = "Xiaomi", Chip = "710", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_710_2"] = new LoaderInfo { Id = "Xiaomi_710_2", Name = "Xiaomi 710 (Generic)", Brand = "Xiaomi", Chip = "710", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_730G"] = new LoaderInfo { Id = "Xiaomi_730G", Name = "Xiaomi 730G (Generic)", Brand = "Xiaomi", Chip = "730G", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_778G"] = new LoaderInfo { Id = "Xiaomi_778G", Name = "Xiaomi 778G (Generic)", Brand = "Xiaomi", Chip = "778G", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_835"] = new LoaderInfo { Id = "Xiaomi_835", Name = "Xiaomi 835 (Generic)", Brand = "Xiaomi", Chip = "835", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_845"] = new LoaderInfo { Id = "Xiaomi_845", Name = "Xiaomi 845 (Generic)", Brand = "Xiaomi", Chip = "845", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_845_2"] = new LoaderInfo { Id = "Xiaomi_845_2", Name = "Xiaomi 845 (Generic)", Brand = "Xiaomi", Chip = "845", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_855"] = new LoaderInfo { Id = "Xiaomi_855", Name = "Xiaomi 855 (Generic)", Brand = "Xiaomi", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_865"] = new LoaderInfo { Id = "Xiaomi_865", Name = "Xiaomi 865 (Generic)", Brand = "Xiaomi", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_865_2"] = new LoaderInfo { Id = "Xiaomi_865_2", Name = "Xiaomi 865 (Generic)", Brand = "Xiaomi", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_870"] = new LoaderInfo { Id = "Xiaomi_870", Name = "Xiaomi 870 (Generic)", Brand = "Xiaomi", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_870_2"] = new LoaderInfo { Id = "Xiaomi_870_2", Name = "Xiaomi 870 (Generic)", Brand = "Xiaomi", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_888"] = new LoaderInfo { Id = "Xiaomi_888", Name = "Xiaomi 888 (Generic)", Brand = "Xiaomi", Chip = "888", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_888_2"] = new LoaderInfo { Id = "Xiaomi_888_2", Name = "Xiaomi 888 (Generic)", Brand = "Xiaomi", Chip = "888", IsCommon = true, AuthMode = "none" };
            db["Xiaomi_Xiaomi5"] = new LoaderInfo { Id = "Xiaomi_Xiaomi5", Name = "Xiaomi Xiaomi5", Brand = "Xiaomi", Chip = "", IsCommon = false, AuthMode = "none" };
            db["Xiaomi_XiaomiCommon712"] = new LoaderInfo { Id = "Xiaomi_XiaomiCommon712", Name = "Xiaomi XiaomiCommon712 (Generic)", Brand = "Xiaomi", Chip = "", IsCommon = true, AuthMode = "none" };

            // === ZTE ===
            db["ZTE_765G"] = new LoaderInfo { Id = "ZTE_765G", Name = "ZTE 765G (Generic)", Brand = "ZTE", Chip = "765G", IsCommon = true, AuthMode = "none" };
            db["ZTE_768G"] = new LoaderInfo { Id = "ZTE_768G", Name = "ZTE 768G (Generic)", Brand = "ZTE", Chip = "768G", IsCommon = true, AuthMode = "none" };
            db["ZTE_845"] = new LoaderInfo { Id = "ZTE_845", Name = "ZTE 845 (Generic)", Brand = "ZTE", Chip = "845", IsCommon = true, AuthMode = "none" };
            db["ZTE_855"] = new LoaderInfo { Id = "ZTE_855", Name = "ZTE 855 (Generic)", Brand = "ZTE", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["ZTE_855_2"] = new LoaderInfo { Id = "ZTE_855_2", Name = "ZTE 855 (Generic)", Brand = "ZTE", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["ZTE_865"] = new LoaderInfo { Id = "ZTE_865", Name = "ZTE 865 (Generic)", Brand = "ZTE", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["ZTE_865_2"] = new LoaderInfo { Id = "ZTE_865_2", Name = "ZTE 865 (Generic)", Brand = "ZTE", Chip = "865", IsCommon = true, AuthMode = "none" };
            db["ZTE_870"] = new LoaderInfo { Id = "ZTE_870", Name = "ZTE 870 (Generic)", Brand = "ZTE", Chip = "870", IsCommon = true, AuthMode = "none" };
            db["ZTE_888"] = new LoaderInfo { Id = "ZTE_888", Name = "ZTE 888 (Generic)", Brand = "ZTE", Chip = "888", IsCommon = true, AuthMode = "none" };
            db["ZTE_8Gen1"] = new LoaderInfo { Id = "ZTE_8Gen1", Name = "ZTE 8Gen1 (Generic)", Brand = "ZTE", Chip = "8Gen1", IsCommon = true, AuthMode = "none" };
            db["ZTE_8Gen2"] = new LoaderInfo { Id = "ZTE_8Gen2", Name = "ZTE 8Gen2 (Generic)", Brand = "ZTE", Chip = "8Gen2", IsCommon = true, AuthMode = "none" };
            db["ZTE_Nubia_Play"] = new LoaderInfo { Id = "ZTE_Nubia_Play", Name = "ZTE Nubia_Play", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_Nubia_RedMagic"] = new LoaderInfo { Id = "ZTE_Nubia_RedMagic", Name = "ZTE Nubia_RedMagic", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_Nubia_X"] = new LoaderInfo { Id = "ZTE_Nubia_X", Name = "ZTE Nubia_X", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_Nubia_Z17"] = new LoaderInfo { Id = "ZTE_Nubia_Z17", Name = "ZTE Nubia_Z17", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_Nubia_Z18"] = new LoaderInfo { Id = "ZTE_Nubia_Z18", Name = "ZTE Nubia_Z18", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_Nubia_mini5G"] = new LoaderInfo { Id = "ZTE_Nubia_mini5G", Name = "ZTE Nubia_mini5G", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_RedMagic_7S"] = new LoaderInfo { Id = "ZTE_RedMagic_7S", Name = "ZTE RedMagic_7S", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_RedMagic_7SPro"] = new LoaderInfo { Id = "ZTE_RedMagic_7SPro", Name = "ZTE RedMagic_7SPro", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_RedMagic_Mars"] = new LoaderInfo { Id = "ZTE_RedMagic_Mars", Name = "ZTE RedMagic_Mars", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8+Pad"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8+Pad", Name = "ZTE ZTEFamily_8+Pad (8+Gen1)", Brand = "ZTE", Chip = "8+Gen1", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_888"] = new LoaderInfo { Id = "ZTE_ZTEFamily_888", Name = "ZTE ZTEFamily_888 (888)", Brand = "ZTE", Chip = "888", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8Elite"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8Elite", Name = "ZTE ZTEFamily_8Elite (8Elite)", Brand = "ZTE", Chip = "8Elite", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8ElitePad"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8ElitePad", Name = "ZTE ZTEFamily_8ElitePad (8Elite)", Brand = "ZTE", Chip = "8Elite", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8Gen1"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8Gen1", Name = "ZTE ZTEFamily_8Gen1 (8Gen1)", Brand = "ZTE", Chip = "8Gen1", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8Gen2Pad"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8Gen2Pad", Name = "ZTE ZTEFamily_8Gen2Pad (8Gen2)", Brand = "ZTE", Chip = "8Gen2", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8Gen3Pad"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8Gen3Pad", Name = "ZTE ZTEFamily_8Gen3Pad (8Gen3)", Brand = "ZTE", Chip = "8Gen3", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTEFamily_8Gen3Phone"] = new LoaderInfo { Id = "ZTE_ZTEFamily_8Gen3Phone", Name = "ZTE ZTEFamily_8Gen3Phone (8Gen3)", Brand = "ZTE", Chip = "8Gen3", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTE_Axon7"] = new LoaderInfo { Id = "ZTE_ZTE_Axon7", Name = "ZTE ZTE_Axon7", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };
            db["ZTE_ZTE_Axon7MAX"] = new LoaderInfo { Id = "ZTE_ZTE_Axon7MAX", Name = "ZTE ZTE_Axon7MAX", Brand = "ZTE", Chip = "", IsCommon = false, AuthMode = "none" };

            // === vivo ===
            db["vivo_425"] = new LoaderInfo { Id = "vivo_425", Name = "vivo 425 (Generic)", Brand = "vivo", Chip = "425", IsCommon = true, AuthMode = "none" };
            db["vivo_439"] = new LoaderInfo { Id = "vivo_439", Name = "vivo 439 (Generic)", Brand = "vivo", Chip = "439", IsCommon = true, AuthMode = "none" };
            db["vivo_665"] = new LoaderInfo { Id = "vivo_665", Name = "vivo 665 (Generic)", Brand = "vivo", Chip = "665", IsCommon = true, AuthMode = "none" };
            db["vivo_675"] = new LoaderInfo { Id = "vivo_675", Name = "vivo 675 (Generic)", Brand = "vivo", Chip = "675", IsCommon = true, AuthMode = "none" };
            db["vivo_720G"] = new LoaderInfo { Id = "vivo_720G", Name = "vivo 720G (Generic)", Brand = "vivo", Chip = "720G", IsCommon = true, AuthMode = "none" };
            db["vivo_855"] = new LoaderInfo { Id = "vivo_855", Name = "vivo 855 (Generic)", Brand = "vivo", Chip = "855", IsCommon = true, AuthMode = "none" };
            db["vivo_iQOO_Neo3"] = new LoaderInfo { Id = "vivo_iQOO_Neo3", Name = "vivo iQOO_Neo3", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_iQOO_U1"] = new LoaderInfo { Id = "vivo_iQOO_U1", Name = "vivo iQOO_U1", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_V11"] = new LoaderInfo { Id = "vivo_vivo_V11", Name = "vivo vivo_V11", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_V11Pro"] = new LoaderInfo { Id = "vivo_vivo_V11Pro", Name = "vivo vivo_V11Pro", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_V21eM2"] = new LoaderInfo { Id = "vivo_vivo_V21eM2", Name = "vivo vivo_V21eM2", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_V9"] = new LoaderInfo { Id = "vivo_vivo_V9", Name = "vivo vivo_V9", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_V9Youth"] = new LoaderInfo { Id = "vivo_vivo_V9Youth", Name = "vivo vivo_V9Youth", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_X7"] = new LoaderInfo { Id = "vivo_vivo_X7", Name = "vivo vivo_X7", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_X7Plus"] = new LoaderInfo { Id = "vivo_vivo_X7Plus", Name = "vivo vivo_X7Plus", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_X9"] = new LoaderInfo { Id = "vivo_vivo_X9", Name = "vivo vivo_X9", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_X9Plus"] = new LoaderInfo { Id = "vivo_vivo_X9Plus", Name = "vivo vivo_X9Plus", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_X9S"] = new LoaderInfo { Id = "vivo_vivo_X9S", Name = "vivo vivo_X9S", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_XPlay5A"] = new LoaderInfo { Id = "vivo_vivo_XPlay5A", Name = "vivo vivo_XPlay5A", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y20_2020"] = new LoaderInfo { Id = "vivo_vivo_Y20_2020", Name = "vivo vivo_Y20-2020", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y51"] = new LoaderInfo { Id = "vivo_vivo_Y51", Name = "vivo vivo_Y51", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y53"] = new LoaderInfo { Id = "vivo_vivo_Y53", Name = "vivo vivo_Y53", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y55"] = new LoaderInfo { Id = "vivo_vivo_Y55", Name = "vivo vivo_Y55", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y65"] = new LoaderInfo { Id = "vivo_vivo_Y65", Name = "vivo vivo_Y65", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y71"] = new LoaderInfo { Id = "vivo_vivo_Y71", Name = "vivo vivo_Y71", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Y79"] = new LoaderInfo { Id = "vivo_vivo_Y79", Name = "vivo vivo_Y79", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Z1"] = new LoaderInfo { Id = "vivo_vivo_Z1", Name = "vivo vivo_Z1", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };
            db["vivo_vivo_Z3"] = new LoaderInfo { Id = "vivo_vivo_Z3", Name = "vivo vivo_Z3", Brand = "vivo", Chip = "", IsCommon = false, AuthMode = "none" };


            return db;
        }
    }

    internal class EdlLoaderPak
    {
        private readonly string _pakPath;
        private readonly Dictionary<string, (long offset, int compSize, int origSize)> _index;

        public EdlLoaderPak(string pakPath)
        {
            _pakPath = pakPath;
            _index = new Dictionary<string, (long, int, int)>(StringComparer.OrdinalIgnoreCase);
            LoadIndex();
        }

        private void LoadIndex()
        {
            using (var fs = new FileStream(_pakPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                byte[] magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "EDLP")
                    throw new InvalidDataException("Invalid EDL PAK file");
                uint version = br.ReadUInt32();
                uint count = br.ReadUInt32();

                for (int i = 0; i < count; i++)
                {
                    byte[] idBytes = br.ReadBytes(64);
                    string id = Encoding.UTF8.GetString(idBytes).TrimEnd('\0');
                    long offset = br.ReadInt64();
                    int compSize = br.ReadInt32();
                    int origSize = br.ReadInt32();
                    _index[id] = (offset, compSize, origSize);
                }
            }
        }

        public byte[] GetLoader(string id)
        {
            if (!_index.TryGetValue(id, out var entry))
                return null;

            using (var fs = new FileStream(_pakPath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(entry.offset, SeekOrigin.Begin);
                byte[] compressed = new byte[entry.compSize];
                fs.Read(compressed, 0, entry.compSize);

                using (var input = new MemoryStream(compressed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
        }
    }
}
