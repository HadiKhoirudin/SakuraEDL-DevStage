// ============================================================================
// LoveAlways - MediaTek EMI (External Memory Interface) Configuration
// MediaTek DRAM Initialization Configuration
// ============================================================================
// Reference: mtkclient project emi_config.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using LoveAlways.MediaTek.Models;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// DRAM Type
    /// </summary>
    public enum DramType
    {
        Unknown = 0,
        LPDDR2 = 2,
        LPDDR3 = 3,
        LPDDR4 = 4,
        LPDDR4X = 5,
        LPDDR5 = 6
    }

    /// <summary>
    /// EMI Settings Structure (Base Version)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EmiSettings
    {
        public uint EmiCona;           // EMI_CONA
        public uint EmiConb;           // EMI_CONB
        public uint EmiConc;           // EMI_CONC
        public uint EmiCond;           // EMI_COND
        public uint EmiCone;           // EMI_CONE
        public uint EmiConf;           // EMI_CONF
        public uint EmiCong;           // EMI_CONG
        public uint EmiConh;           // EMI_CONH
        public uint DramcActim;        // DRAMC_ACTIM
        public uint DramcGddr3Ctl1;    // DRAMC_GDDR3CTL1
        public uint DramcConf1;        // DRAMC_CONF1
        public uint DramcDdr2Ctl;      // DRAMC_DDR2CTL
        public uint DramcTest2_3;      // DRAMC_TEST2_3
        public uint DramcConf2;        // DRAMC_CONF2
        public uint DramcPd_ctrl;      // DRAMC_PD_CTRL
        public uint DramcPadctl3;      // DRAMC_PADCTL3
        public uint DramcDqodly;       // DRAMC_DQODLY
        public uint DramcAddr_Output_Dly; // DRAMC_ADDR_OUTPUT_DLY
        public uint DramcClk_Output_Dly;  // DRAMC_CLK_OUTPUT_DLY
        public uint DramcActim1;       // DRAMC_ACTIM1
        public uint DramcMisctl0;      // DRAMC_MISCTL0
        public uint DramcActim05t;     // DRAMC_ACTIM05T
        public uint DramcRkcfg;        // DRAMC_RKCFG
        public uint DramcDrvctl0;      // DRAMC_DRVCTL0
        public uint DramcDrvctl1;      // DRAMC_DRVCTL1
        public uint DramcDdr2Ctl2;     // DRAMC_DDR2CTL2
        
        public DramType DramType;      // DRAM Type
        public uint DramRank;          // DRAM Rank
        public uint DramDensity;       // DRAM Density
    }

    /// <summary>
    /// EMI Settings V2 (LPDDR4/4X Version)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EmiSettingsV2
    {
        public uint EmiCona;
        public uint EmiConf;
        public uint EmiConb;
        public uint EmiConh;
        public uint EmiConh2;
        public uint ChnEmiCona;
        public uint ChnEmiConc;
        public uint ChnEmiCond;
        public uint DramcActim0;
        public uint DramcActim1;
        public uint DramcDdr2Ctl;
        public uint DramcConf1;
        public uint DramcConf2;
        public uint DramcPadctl3;
        public uint DramcPdCtrl;
        public uint DramcRkcfg;
        public uint DramcMisctl0;
        public uint DramcActim5;
        public uint DramcOdtctl;
        public uint DramcActim05t;
        public uint DramcDqodly;
        public uint DramcAddrOutputDly;
        public uint DramcClkOutputDly;
        public uint DramcTxdqsctl;
        public uint DramcSelphDqs0;
        public uint DramcSelphDqs1;
        public uint DramcTxRankctl;
        public uint DramcPipe;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        
        public DramType DramType;
        public uint DramRank;
        public uint DramDensity;
    }

    /// <summary>
    /// EMI Configuration Record
    /// </summary>
    public class EmiConfigRecord
    {
        /// <summary>Chip HW Code</summary>
        public ushort HwCode { get; set; }
        
        /// <summary>Chip Name</summary>
        public string ChipName { get; set; }
        
        /// <summary>EMI configuration data</summary>
        public byte[] ConfigData { get; set; }
        
        /// <summary>Configuration length</summary>
        public int ConfigLength => ConfigData?.Length ?? 0;
        
        /// <summary>Whether EMI configuration is required</summary>
        public bool Required { get; set; }
        
        /// <summary>EMI settings version</summary>
        public int Version { get; set; } = 1;
        
        /// <summary>DRAM type</summary>
        public DramType DramType { get; set; } = DramType.LPDDR4;
        
        /// <summary>DRAM size (MB)</summary>
        public uint DramSizeMB { get; set; }
        
        /// <summary>DRAM rank count</summary>
        public int DramRanks { get; set; } = 2;
    }

    /// <summary>
    /// MTK EMI Configuration Database
    /// </summary>
    public static class MtkEmiConfig
    {
        private static readonly Dictionary<ushort, EmiConfigRecord> _configs = new Dictionary<ushort, EmiConfigRecord>();
        
        // Mapping of whether a chip requires EMI configuration
        private static readonly Dictionary<ushort, bool> _requiresEmi = new Dictionary<ushort, bool>();
        
        // DRAM type mapping
        private static readonly Dictionary<ushort, DramType> _dramTypes = new Dictionary<ushort, DramType>();

        static MtkEmiConfig()
        {
            InitializeConfigs();
        }

        private static void InitializeConfigs()
        {
            // ═══════════════════════════════════════════════════════════════
            // EMI Configuration Requirements Mapping
            // Reference: mtkclient emi_config.py
            // ═══════════════════════════════════════════════════════════════
            
            // Legacy chips - Requires EMI configuration
            SetChipEmiRequirement(0x6572, true, DramType.LPDDR2);   // MT6572
            SetChipEmiRequirement(0x6582, true, DramType.LPDDR2);   // MT6582
            SetChipEmiRequirement(0x6580, true, DramType.LPDDR3);   // MT6580
            SetChipEmiRequirement(0x6592, true, DramType.LPDDR3);   // MT6592
            SetChipEmiRequirement(0x6595, true, DramType.LPDDR3);   // MT6595
            SetChipEmiRequirement(0x0321, true, DramType.LPDDR3);   // MT6735
            SetChipEmiRequirement(0x0335, true, DramType.LPDDR3);   // MT6737
            SetChipEmiRequirement(0x0326, true, DramType.LPDDR3);   // MT6755
            SetChipEmiRequirement(0x0601, true, DramType.LPDDR3);   // MT6757
            SetChipEmiRequirement(0x0279, true, DramType.LPDDR3);   // MT6797
            
            // Mid-range chips - Requires EMI configuration
            SetChipEmiRequirement(0x0562, true, DramType.LPDDR4);   // MT6761
            SetChipEmiRequirement(0x0707, true, DramType.LPDDR4);   // MT6762
            SetChipEmiRequirement(0x0690, true, DramType.LPDDR4);   // MT6763
            SetChipEmiRequirement(0x0717, true, DramType.LPDDR4);   // MT6765
            SetChipEmiRequirement(0x0725, true, DramType.LPDDR4);   // MT6765 Variant
            SetChipEmiRequirement(0x0551, true, DramType.LPDDR4);   // MT6768
            SetChipEmiRequirement(0x0688, true, DramType.LPDDR4);   // MT6771
            SetChipEmiRequirement(0x0507, true, DramType.LPDDR4X);  // MT6779
            SetChipEmiRequirement(0x0588, true, DramType.LPDDR4X);  // MT6785
            SetChipEmiRequirement(0x0699, true, DramType.LPDDR4);   // MT6739
            
            // Modern chips - Requires EMI configuration (V2)
            SetChipEmiRequirement(0x0813, true, DramType.LPDDR4X);  // MT6833
            SetChipEmiRequirement(0x0600, true, DramType.LPDDR4X);  // MT6853
            SetChipEmiRequirement(0x0788, true, DramType.LPDDR4X);  // MT6873
            SetChipEmiRequirement(0x0766, true, DramType.LPDDR4X);  // MT6877
            SetChipEmiRequirement(0x0886, true, DramType.LPDDR4X);  // MT6885
            SetChipEmiRequirement(0x0989, true, DramType.LPDDR4X);  // MT6891
            SetChipEmiRequirement(0x0816, true, DramType.LPDDR4X);  // MT6893
            SetChipEmiRequirement(0x0996, true, DramType.LPDDR5);   // MT6895
            SetChipEmiRequirement(0x0900, true, DramType.LPDDR5);   // MT6983
            SetChipEmiRequirement(0x0930, true, DramType.LPDDR5);   // MT6985
            SetChipEmiRequirement(0x0950, true, DramType.LPDDR5);   // MT6989
            
            // Tablet chips
            SetChipEmiRequirement(0x8127, true, DramType.LPDDR3);   // MT8127
            SetChipEmiRequirement(0x8163, true, DramType.LPDDR3);   // MT8163
            SetChipEmiRequirement(0x8167, true, DramType.LPDDR4);   // MT8167
            SetChipEmiRequirement(0x8168, true, DramType.LPDDR4);   // MT8168
            SetChipEmiRequirement(0x8173, true, DramType.LPDDR3);   // MT8173
            SetChipEmiRequirement(0x8183, true, DramType.LPDDR4X);  // MT8183
            SetChipEmiRequirement(0x8195, true, DramType.LPDDR5);   // MT8195
        }
        
        private static void SetChipEmiRequirement(ushort hwCode, bool required, DramType dramType)
        {
            _requiresEmi[hwCode] = required;
            _dramTypes[hwCode] = dramType;
        }

        /// <summary>
        /// Get EMI configuration
        /// </summary>
        public static EmiConfigRecord GetConfig(ushort hwCode)
        {
            if (_configs.TryGetValue(hwCode, out var config))
                return config;
            
            // Generate default configuration
            var dramType = GetDramType(hwCode);
            return new EmiConfigRecord
            {
                HwCode = hwCode,
                ChipName = $"MT{hwCode:X4}",
                Required = IsRequired(hwCode),
                ConfigData = new byte[0],
                DramType = dramType,
                Version = dramType >= DramType.LPDDR4 ? 2 : 1
            };
        }

        /// <summary>
        /// Check if EMI configuration is required
        /// </summary>
        public static bool IsRequired(ushort hwCode)
        {
            if (_requiresEmi.TryGetValue(hwCode, out var required))
                return required;
            return false;
        }
        
        /// <summary>
        /// Get chip DRAM type
        /// </summary>
        public static DramType GetDramType(ushort hwCode)
        {
            if (_dramTypes.TryGetValue(hwCode, out var dramType))
                return dramType;
            return DramType.LPDDR4;  // LPDDR4 default
        }
        
        /// <summary>
        /// Get EMI settings version
        /// </summary>
        public static int GetEmiVersion(ushort hwCode)
        {
            var dramType = GetDramType(hwCode);
            return dramType >= DramType.LPDDR4 ? 2 : 1;
        }

        /// <summary>
        /// Load EMI configuration from file
        /// </summary>
        public static byte[] LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;
            
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Extract EMI configuration from Preloader
        /// </summary>
        public static byte[] ExtractFromPreloader(byte[] preloaderData)
        {
            if (preloaderData == null || preloaderData.Length < 0x1000)
                return null;
            
            // EMI configuration signature
            byte[] signature = new byte[] { 0x4D, 0x45, 0x4D, 0x49 };  // "MEMI"
            
            for (int i = 0; i < preloaderData.Length - 256; i += 4)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (preloaderData[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }
                
                if (found)
                {
                    // Found EMI configuration header
                    int configSize = BitConverter.ToInt32(preloaderData, i + 4);
                    if (configSize > 0 && configSize < 0x1000)
                    {
                        byte[] config = new byte[configSize];
                        Array.Copy(preloaderData, i + 8, config, 0, configSize);
                        return config;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Generate default EMI configuration (Generic LPDDR config)
        /// </summary>
        public static byte[] GenerateDefaultConfig(ushort hwCode)
        {
            var dramType = GetDramType(hwCode);
            var version = GetEmiVersion(hwCode);
            
            if (version == 2)
            {
                // LPDDR4/4X/5 Config
                var settings = new EmiSettingsV2
                {
                    EmiCona = 0x00000000,
                    EmiConf = 0x00000000,
                    EmiConb = 0x00000000,
                    EmiConh = 0x00000000,
                    DramType = dramType,
                    DramRank = 2,
                    DramDensity = 0x1000  // 4GB default
                };
                
                return StructToBytes(settings);
            }
            else
            {
                // LPDDR2/3 Config
                var settings = new EmiSettings
                {
                    EmiCona = 0x00000000,
                    EmiConb = 0x00000000,
                    DramType = dramType,
                    DramRank = 2,
                    DramDensity = 0x0400  // 1GB default
                };
                
                return StructToBytes(settings);
            }
        }

        /// <summary>
        /// Verify EMI configuration validity
        /// </summary>
        public static bool ValidateConfig(byte[] configData)
        {
            if (configData == null || configData.Length == 0)
                return false;
            
            // EMI configuration is usually 4-byte aligned
            if (configData.Length % 4 != 0)
                return false;
            
            // Minimum length check (at least 4 register configurations)
            if (configData.Length < 16)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Convert structure to byte array
        /// </summary>
        private static byte[] StructToBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(structure, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
        }
        
        /// <summary>
        /// Convert byte array to structure
        /// </summary>
        public static T BytesToStruct<T>(byte[] arr) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            if (arr.Length < size)
                throw new ArgumentException("Insufficient data length");
            
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(arr, 0, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        
        /// <summary>
        /// Get list of chips supporting EMI configuration
        /// </summary>
        public static IReadOnlyList<ushort> GetSupportedChips()
        {
            return new List<ushort>(_requiresEmi.Keys).AsReadOnly();
        }
        
        /// <summary>
        /// Get statistical information
        /// </summary>
        public static string GetStats()
        {
            int total = _requiresEmi.Count;
            int lpddr2 = 0, lpddr3 = 0, lpddr4 = 0, lpddr4x = 0, lpddr5 = 0;
            
            foreach (var kv in _dramTypes)
            {
                switch (kv.Value)
                {
                    case DramType.LPDDR2: lpddr2++; break;
                    case DramType.LPDDR3: lpddr3++; break;
                    case DramType.LPDDR4: lpddr4++; break;
                    case DramType.LPDDR4X: lpddr4x++; break;
                    case DramType.LPDDR5: lpddr5++; break;
                }
            }
            
            return $"EMI Configuration Statistics:\n" +
                   $"  Total chips: {total}\n" +
                   $"  LPDDR2: {lpddr2}\n" +
                   $"  LPDDR3: {lpddr3}\n" +
                   $"  LPDDR4: {lpddr4}\n" +
                   $"  LPDDR4X: {lpddr4x}\n" +
                   $"  LPDDR5: {lpddr5}";
        }
    }
}
