// ============================================================================
// LoveAlways - MediaTek Chip Database
// MediaTek Chip Information Database
// ============================================================================
// Reference: mtkclient project brom_config.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LoveAlways.MediaTek.Database
{
    /// <summary>
    /// MTK Chip Information Record
    /// </summary>
    public class MtkChipRecord
    {
        public ushort HwCode { get; set; }
        public string ChipName { get; set; }
        public string Description { get; set; }
        public uint WatchdogAddr { get; set; }
        public uint UartAddr { get; set; }
        public uint BromPayloadAddr { get; set; }
        public uint DaPayloadAddr { get; set; }
        public uint? CqDmaBase { get; set; }
        public int DaMode { get; set; } = (int)Protocol.DaMode.Xml;
        public uint Var1 { get; set; }
        public bool SupportsXFlash { get; set; }
        public bool Is64Bit { get; set; }
        public bool HasExploit { get; set; }
        public string ExploitType { get; set; }

        // V6 protocol related
        /// <summary>
        /// Whether BROM has been patched (kamakiri/linecode BROM exploits would be invalid)
        /// </summary>
        public bool BromPatched { get; set; }

        /// <summary>
        /// Whether V6 Loader is required (Preloader mode)
        /// </summary>
        public bool RequiresLoader { get; set; }

        /// <summary>
        /// Loader filename (if required)
        /// </summary>
        public string LoaderName { get; set; }

        /// <summary>
        /// SoC version (used to distinguish versions of the same chip)
        /// </summary>
        public ushort SocVer { get; set; }

        /// <summary>
        /// Chip code name (used for Loader matching)
        /// </summary>
        public string Codename { get; set; }
    }

    /// <summary>
    /// MTK Chip Database
    /// </summary>
    public static class MtkChipDatabase
    {
        private static readonly Dictionary<ushort, MtkChipRecord> _chips = new Dictionary<ushort, MtkChipRecord>();

        // HW Code alias mapping in Preloader mode
        // Some chips report different HW Codes in Preloader mode
        private static readonly Dictionary<ushort, ushort> _preloaderAliases = new Dictionary<ushort, ushort>
        {
            // Preloader HW Code => BROM HW Code
            { 0x1236, 0x0950 },   // MT6989 Preloader => MT6989 BROM
            { 0x0951, 0x0950 },   // MT6989 Alt => MT6989 BROM
            { 0x1172, 0x0996 },   // MT6895 Dimensity 8200 => MT6895 (Needs confirmation)
            { 0x0959, 0x0766 },   // MT6877 Preloader => MT6877 BROM
        };

        static MtkChipDatabase()
        {
            InitializeDatabase();
        }

        /// <summary>
        /// Initialize chip database
        /// </summary>
        private static void InitializeDatabase()
        {
            // MT6261 Series (Feature phones)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6261,
                ChipName = "MT6261",
                Description = "Feature phone chip",
                WatchdogAddr = 0xA0030000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false
            });

            // MT6572/MT6582 Series
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6572,
                ChipName = "MT6572",
                Description = "Dual-core smartphone chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x6582,
                ChipName = "MT6582",
                Description = "Quad-core smartphone chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false
            });

            // MT6735/MT6737 Series
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0321,
                ChipName = "MT6735",
                Description = "64-bit quad-core chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x0335,
                ChipName = "MT6737",
                Description = "64-bit quad-core chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true
            });

            // MT6755/MT6757 Series (Helio P10/P20)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0326,
                ChipName = "MT6755",
                Description = "Helio P10",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x0601,
                ChipName = "MT6757",
                Description = "Helio P20",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true
            });

            // MT6761/MT6762/MT6763 Series (Helio A/P22/P23)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0562,
                ChipName = "MT6761",
                Description = "Helio A22",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x0707,
                ChipName = "MT6762",
                Description = "Helio P22",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x0690,
                ChipName = "MT6763",
                Description = "Helio P23",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6765 (Helio P35)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0717,
                ChipName = "MT6765",
                Description = "Helio P35",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            AddChip(new MtkChipRecord
            {
                HwCode = 0x0725,
                ChipName = "MT6765",
                Description = "Helio P35 (Variant)",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6768 (Helio G85)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0551,
                ChipName = "MT6768",
                Description = "Helio G85",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6771 (Helio P60)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0688,
                ChipName = "MT6771",
                Description = "Helio P60",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6779 (Helio P90)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0507,
                ChipName = "MT6779",
                Description = "Helio P90",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6785 (Helio G90/G95)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0588,
                ChipName = "MT6785",
                Description = "Helio G90/G95",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6797 (Helio X20/X25)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0279,
                ChipName = "MT6797",
                Description = "Helio X20/X25",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true
            });

            // MT6739
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0699,
                ChipName = "MT6739",
                Description = "Entry-level 4G chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6833 (Dimensity 700)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0813,
                ChipName = "MT6833",
                Description = "Dimensity 700",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6853 (Dimensity 720)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0600,
                ChipName = "MT6853",
                Description = "Dimensity 720",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6873 (Dimensity 800)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0788,
                ChipName = "MT6873",
                Description = "Dimensity 800/820",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6877 (Dimensity 900) - BROM mode
            // Reference Config.xml: DA1Address=2097152 (0x200000)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0766,
                ChipName = "MT6765",
                Description = "Helio P35/G35",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                HasExploit = true
            });

            // MT6877 (Dimensity 900) - Preloader mode HW Code
            // Reference Config.xml: DA1Address=2097152 (0x200000)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0959,
                ChipName = "MT6877",
                Description = "Dimensity 900 (Preloader)",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,   // Config.xml: 0x200000
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6885 (Dimensity 1000)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0886,
                ChipName = "MT6885",
                Description = "Dimensity 1000/1000+",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6891 (Dimensity 1100)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0989,
                ChipName = "MT6891",
                Description = "Dimensity 1100",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6893 (Dimensity 1200)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0816,
                ChipName = "MT6893",
                Description = "Dimensity 1200",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6895 (Dimensity 8000)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0996,
                ChipName = "MT6895",
                Description = "Dimensity 8000/8100",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // MT6895 (Dimensity 8200) - HW Code 0x1172
            // Note: previously mislabeled as MT6983, actual test HW Code 0x1172 = MT6895
            AddChip(new MtkChipRecord
            {
                HwCode = 0x1172,
                ChipName = "MT6895",
                Description = "Dimensity 8200",
                WatchdogAddr = 0x1C007000,  // Corrected based on screenshot
                UartAddr = 0x11001000,      // Corrected based on screenshot
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,   // Per screenshot: 0x201000
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Var1 = 0x0A,                // Per screenshot: Var1 = A
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "Carbonara"
            });

            // ═══════════════════════════════════════════════════════════════
            // MT6989 (Dimensity 9300) - iQOO Z9 Turbo, VIVO, etc.
            // Supports ALLINONE-SIGNATURE exploit
            // ChimeraTool logs confirm DA1 address: 0x02000000
            // ═══════════════════════════════════════════════════════════════
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0950,
                ChipName = "MT6989",
                Description = "Dimensity 9300",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x02000000,  // DA1 address (ChimeraTool confirmed)
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Var1 = 0x0A,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature"
            });

            // MT6989 Preloader mode HW Code - 0x1236 (Confirmed by testing)
            // VIVO devices report this HW Code in Preloader mode
            // ChimeraTool logs confirm DA1 address: 0x02000000
            AddChip(new MtkChipRecord
            {
                HwCode = 0x1236,
                ChipName = "MT6989",
                Description = "Dimensity 9300 (Preloader)",
                WatchdogAddr = 0x1C007000,  // Might be inaccessible in Preloader mode
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x02000000,  // DA1 address (ChimeraTool confirmed)
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Var1 = 0x0A,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature"  // Use DA2 level exploit
            });

            // MT6989 other possible Preloader HW Codes
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0951,
                ChipName = "MT6989",
                Description = "Dimensity 9300 (Preloader Alt)",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x02000000,  // DA1 address
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Var1 = 0x0A,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature"
            });

            // MT6983 (Dimensity 9000) - Might also support ALLINONE-SIGNATURE
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0900,
                ChipName = "MT6983",
                Description = "Dimensity 9000",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x40000000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature"
            });

            // MT6985 (Dimensity 9200) - Might also support ALLINONE-SIGNATURE
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0930,
                ChipName = "MT6985",
                Description = "Dimensity 9200",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x40000000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature",
                BromPatched = true,
                RequiresLoader = true,
                Codename = "rubens"
            });

            // ═══════════════════════════════════════════════════════════════
            // V6 New chips (BROM patched, requires V6 Loader)
            // Reference: mtkclient brom_config.py
            // These chips require Preloader mode + signed Loader
            // ═══════════════════════════════════════════════════════════════

            // ═══════════════════════════════════════════════════════════════
            // Note: HW Codes below conflict with existing chips, needs device validation
            // Temporarily commented out to avoid overwriting verified chip configurations
            // ═══════════════════════════════════════════════════════════════

            // MT6781 (Helio G96) - HW Code 0x0788 conflicts with MT6873
            // MT6789 (Helio G99) - HW Code 0x0813 conflicts with MT6833
            // MT6855 (Dimensity 930) - HW Code 0x0886 conflicts with MT6885
            // MT6879 (Dimensity 920) - HW Code 0x0959 conflicts with MT6877 Preloader
            // MT6886 (Dimensity 7050) - HW Code 0x0996 conflicts with MT6895
            //
            // TODO: Real device validation needed for these chips' HW Codes
            // These might be aliases for Preloader mode, need to add to _preloaderAliases

            // MT6897 (Dimensity 8300) - V6 protocol
            // TODO: Needs verification of real HW Code, 0x0950 conflicts with MT6989
            // Temporarily commented out, waiting for device validation
            /*
            AddChip(new MtkChipRecord
            {
                HwCode = 0x????,  // To be verified
                ChipName = "MT6897",
                Description = "Dimensity 8300",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x02000000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                BromPatched = true,
                RequiresLoader = true,
                HasExploit = true,
                ExploitType = "AllinoneSignature",
                Codename = "mt6897"
            });
            */

            // MT8168, MT8183, MT8185 - HW Code conflict, temporarily commented out
            // MT8168/MT8183's 0x0699 conflicts with MT6739
            // MT8185's 0x0717 conflicts with MT6765
            // TODO: Real HW Code validation needed

            // MT8188 - V6 protocol
            // TODO: Needs verification of real HW Code, 0x0950 conflicts with MT6989
            // Temporarily commented out, waiting for device validation
            /*
            AddChip(new MtkChipRecord
            {
                HwCode = 0x????,  // To be verified
                ChipName = "MT8188",
                Description = "IoT/Tablet flagship chip",
                WatchdogAddr = 0x1C007000,
                UartAddr = 0x11001000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x40000000,
                CqDmaBase = 0x10212000,
                DaMode = (int)DaMode.Xml,
                Is64Bit = true,
                BromPatched = true,
                RequiresLoader = true,
                Codename = "mt8188"
            });
            */

            // MT8195, MT6769, MT6769V, MT6769Z, MT6750 - HW Code conflict, temporarily commented out
            // MT8195's 0x0816 conflicts with MT6893
            // MT6769's 0x0707 conflicts with MT6762 (might be different names for the same chip)
            // MT6769V's 0x0725 conflicts with MT6765 variant
            // MT6769Z's 0x0562 conflicts with MT6761
            // MT6750's 0x0326 conflicts with MT6755
            // TODO: Verify if these are different market names for the same chip

            // MT6580 (Entry-level chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6580,
                ChipName = "MT6580",
                Description = "Entry-level quad-core chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false,
                Codename = "mt6580"
            });

            // MT6592 (Octa-core chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6592,
                ChipName = "MT6592",
                Description = "Octa-core chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false,
                Codename = "mt6592"
            });

            // MT6595 (First 64-bit chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6595,
                ChipName = "MT6595",
                Description = "First 64-bit chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = true,
                Codename = "mt6595"
            });

            // MT6752, MT6753, MT6732 - HW Code conflict, temporarily commented out
            // MT6752's 0x0321 conflicts with MT6735
            // MT6753's 0x0337 is independent, can be kept
            // MT6732's 0x0335 conflicts with MT6737

            // MT6753 (MT6752 Enhanced) - Independent HW Code
            AddChip(new MtkChipRecord
            {
                HwCode = 0x0337,
                ChipName = "MT6753",
                Description = "MT6752 Enhanced Edition",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                Codename = "mt6753"
            });

            // MT6570 (Entry-level chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x6570,
                ChipName = "MT6570",
                Description = "Entry-level chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false,
                Codename = "mt6570"
            });

            // MT8127 (Tablet chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x8127,
                ChipName = "MT8127",
                Description = "Tablet chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = false,
                Codename = "mt8127"
            });

            // MT8163 (Tablet chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x8163,
                ChipName = "MT8163",
                Description = "Tablet chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.Legacy,
                Is64Bit = true,
                Codename = "mt8163"
            });

            // MT8167 (Tablet chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x8167,
                ChipName = "MT8167",
                Description = "Tablet chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                Codename = "mt8167"
            });

            // MT8173 (Chromebook chip)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x8173,
                ChipName = "MT8173",
                Description = "Chromebook chip",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                Codename = "mt8173"
            });

            // MT8176 (MT8173 Enhanced Edition)
            AddChip(new MtkChipRecord
            {
                HwCode = 0x8176,
                ChipName = "MT8176",
                Description = "MT8173 Enhanced Edition",
                WatchdogAddr = 0x10007000,
                UartAddr = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x200000,
                DaMode = (int)DaMode.XFlash,
                Is64Bit = true,
                Codename = "mt8176"
            });

            // ═══════════════════════════════════════════════════════════════
            // Added Preloader HW Code alias mapping (some chips have different HW Codes in different modes)
            // ═══════════════════════════════════════════════════════════════

            // TODO: Add more verified chips
            // Chip information source:
            // 1. mtkclient project brom_config.py (Verified)
            // 2. Real device test data
            // 3. Official technical documentation
            //
            // Note: Do not add unverified chips or guessed HW Codes
        }


        /// <summary>
        /// Add chip record
        /// </summary>
        private static void AddChip(MtkChipRecord chip)
        {
            _chips[chip.HwCode] = chip;
        }

        /// <summary>
        /// Get chip information by HW Code
        /// </summary>
        public static MtkChipRecord GetChip(ushort hwCode)
        {
            // Direct lookup first
            if (_chips.TryGetValue(hwCode, out var chip))
                return chip;

            // Check Preloader alias
            if (_preloaderAliases.TryGetValue(hwCode, out var bromHwCode))
            {
                if (_chips.TryGetValue(bromHwCode, out chip))
                    return chip;
            }

            return null;
        }

        /// <summary>
        /// Get chip information by HW Code (including Preloader mode detection)
        /// </summary>
        public static (MtkChipRecord chip, bool isPreloaderAlias) GetChipWithAlias(ushort hwCode)
        {
            // Direct lookup first
            if (_chips.TryGetValue(hwCode, out var chip))
                return (chip, false);

            // Check Preloader alias
            if (_preloaderAliases.TryGetValue(hwCode, out var bromHwCode))
            {
                if (_chips.TryGetValue(bromHwCode, out chip))
                    return (chip, true);
            }

            return (null, false);
        }

        /// <summary>
        /// Get chip name
        /// </summary>
        public static string GetChipName(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip?.ChipName ?? $"MT{hwCode:X4}";
        }

        /// <summary>
        /// Get all supported chips
        /// </summary>
        public static IReadOnlyList<MtkChipRecord> GetAllChips()
        {
            return _chips.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Get chips with exploit support
        /// </summary>
        public static IReadOnlyList<MtkChipRecord> GetExploitableChips()
        {
            return _chips.Values.Where(c => c.HasExploit).ToList().AsReadOnly();
        }

        /// <summary>
        /// Get chips by exploit type
        /// </summary>
        /// <param name="exploitType">Exploit type: Carbonara, AllinoneSignature, Kamakiri2</param>
        public static IReadOnlyList<MtkChipRecord> GetChipsByExploitType(string exploitType)
        {
            return _chips.Values
                .Where(c => c.HasExploit &&
                       string.Equals(c.ExploitType, exploitType, StringComparison.OrdinalIgnoreCase))
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Get chips supporting ALLINONE-SIGNATURE exploit
        /// </summary>
        public static IReadOnlyList<MtkChipRecord> GetAllinoneSignatureChips()
        {
            return GetChipsByExploitType("AllinoneSignature");
        }

        /// <summary>
        /// Check if chip supports ALLINONE-SIGNATURE exploit
        /// </summary>
        public static bool IsAllinoneSignatureSupported(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip != null &&
                   chip.HasExploit &&
                   string.Equals(chip.ExploitType, "AllinoneSignature", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get chip exploit type
        /// </summary>
        public static string GetExploitType(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip?.ExploitType ?? "None";
        }

        /// <summary>
        /// Convert to MtkChipInfo
        /// </summary>
        public static MtkChipInfo ToChipInfo(MtkChipRecord record)
        {
            if (record == null) return null;

            return new MtkChipInfo
            {
                HwCode = record.HwCode,
                ChipName = record.ChipName,
                Description = record.Description,
                WatchdogAddr = record.WatchdogAddr,
                UartAddr = record.UartAddr,
                BromPayloadAddr = record.BromPayloadAddr,
                DaPayloadAddr = record.DaPayloadAddr,
                CqDmaBase = record.CqDmaBase,
                DaMode = record.DaMode,
                SupportsXFlash = record.SupportsXFlash,
                Is64Bit = record.Is64Bit,
                // V6 New fields
                BromPatched = record.BromPatched,
                RequiresLoader = record.RequiresLoader,
                LoaderName = record.LoaderName,
                Codename = record.Codename,
                ExploitType = record.ExploitType,
                HasExploit = record.HasExploit
            };
        }

        #region V6 Protocol Methods

        /// <summary>
        /// Check if chip requires V6 Loader
        /// </summary>
        public static bool RequiresV6Loader(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip?.RequiresLoader ?? false;
        }

        /// <summary>
        /// Check if BROM has been patched
        /// </summary>
        public static bool IsBromPatched(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip?.BromPatched ?? false;
        }

        /// <summary>
        /// Get list of chips requiring V6 Loader
        /// </summary>
        public static IReadOnlyList<MtkChipRecord> GetV6LoaderChips()
        {
            return _chips.Values.Where(c => c.RequiresLoader).ToList().AsReadOnly();
        }

        /// <summary>
        /// Get list of BROM patched chips
        /// </summary>
        public static IReadOnlyList<MtkChipRecord> GetBromPatchedChips()
        {
            return _chips.Values.Where(c => c.BromPatched).ToList().AsReadOnly();
        }

        /// <summary>
        /// Get chip loader filename
        /// </summary>
        public static string GetLoaderName(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            if (chip == null) return null;

            // Return designated loader name if available
            if (!string.IsNullOrEmpty(chip.LoaderName))
                return chip.LoaderName;

            // Otherwise generate default name based on chip name
            return $"{chip.ChipName.ToLower()}_loader.bin";
        }

        /// <summary>
        /// Get chip codename
        /// </summary>
        public static string GetCodename(ushort hwCode)
        {
            var chip = GetChip(hwCode);
            return chip?.Codename ?? $"mt{hwCode:x4}";
        }

        #endregion

        #region Statistics Methods

        /// <summary>
        /// Get database statistics
        /// </summary>
        public static MtkDatabaseStats GetStats()
        {
            var stats = new MtkDatabaseStats
            {
                TotalChips = _chips.Count,
                ExploitableChips = _chips.Values.Count(c => c.HasExploit),
                V6LoaderChips = _chips.Values.Count(c => c.RequiresLoader),
                BromPatchedChips = _chips.Values.Count(c => c.BromPatched),
                CarbonaraChips = _chips.Values.Count(c => c.ExploitType == "Carbonara"),
                AllinoneSignatureChips = _chips.Values.Count(c => c.ExploitType == "AllinoneSignature"),
                LegacyChips = _chips.Values.Count(c => c.DaMode == (int)DaMode.Legacy),
                XmlChips = _chips.Values.Count(c => c.DaMode == (int)DaMode.Xml),
                XFlashChips = _chips.Values.Count(c => c.DaMode == (int)DaMode.XFlash)
            };
            return stats;
        }

        #endregion
    }

    /// <summary>
    /// Chip Database Statistics
    /// </summary>
    public class MtkDatabaseStats
    {
        public int TotalChips { get; set; }
        public int ExploitableChips { get; set; }
        public int V6LoaderChips { get; set; }
        public int BromPatchedChips { get; set; }
        public int CarbonaraChips { get; set; }
        public int AllinoneSignatureChips { get; set; }
        public int LegacyChips { get; set; }
        public int XmlChips { get; set; }
        public int XFlashChips { get; set; }

        public override string ToString()
        {
            return $"MTK Chip Database Statistics:\n" +
                   $"  Total Chips: {TotalChips}\n" +
                   $"  Exploitable: {ExploitableChips}\n" +
                   $"  - Carbonara: {CarbonaraChips}\n" +
                   $"  - AllinoneSignature: {AllinoneSignatureChips}\n" +
                   $"  Requires V6 Loader: {V6LoaderChips}\n" +
                   $"  BROM Patched: {BromPatchedChips}\n" +
                   $"  Protocol Distribution: Legacy={LegacyChips}, XML={XmlChips}, XFlash={XFlashChips}";
        }
    }
}
