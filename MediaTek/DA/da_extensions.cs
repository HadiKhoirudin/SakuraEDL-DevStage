// ============================================================================
// LoveAlways - MediaTek DA Extensions Support Framework
// MediaTek Download Agent Extensions Support Framework
// ============================================================================
// Reference: Penumbra documentation https://shomy.is-a.dev/penumbra/Mediatek/Common/DA/DA-Extensions
// DA Extensions developed by bkerler, used to remove manufacturer DA restrictions, restore RPMB/register access, etc.
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using LoveAlways.MediaTek.Models;

namespace LoveAlways.MediaTek.DA
{
    /// <summary>
    /// DA Extensions Configuration
    /// </summary>
    public class DaExtensionsConfig
    {
        /// <summary>
        /// Standard load address (DRAM space)
        /// </summary>
        public const uint STANDARD_LOAD_ADDR = 0x68000000;
        
        /// <summary>
        /// Low memory device load address (XFlash protocol)
        /// Reference: https://github.com/bkerler/mtkclient/pull/1563
        /// </summary>
        public const uint LOW_MEM_LOAD_ADDR = 0x4FFF0000;
        
        /// <summary>
        /// DA2 usual load address
        /// </summary>
        public const uint DA2_LOAD_ADDR = 0x40000000;
        
        /// <summary>
        /// DA1 usual load address range
        /// </summary>
        public const uint DA1_MEM_START = 0x00200000;
        public const uint DA1_MEM_END = 0x00300000;

        /// <summary>
        /// Whether to use low memory address
        /// </summary>
        public bool UseLowMemoryAddress { get; set; }

        /// <summary>
        /// Extensions binary data
        /// </summary>
        public byte[] ExtensionsBinary { get; set; }

        /// <summary>
        /// Get load address
        /// </summary>
        public uint GetLoadAddress()
        {
            return UseLowMemoryAddress ? LOW_MEM_LOAD_ADDR : STANDARD_LOAD_ADDR;
        }
    }

    /// <summary>
    /// XFlash (V5) DA Extensions Commands
    /// Command range: 0x0F0000 - 0x0FFFFF
    /// </summary>
    public static class XFlashExtensionCommands
    {
        public const uint CMD_RANGE_START = 0x0F0000;
        public const uint CMD_RANGE_END   = 0x0FFFFF;

        // RPMB operations
        public const uint CMD_READ_RPMB   = 0x0F0001;
        public const uint CMD_WRITE_RPMB  = 0x0F0002;

        // Register access
        public const uint CMD_READ_REG    = 0x0F0003;
        public const uint CMD_WRITE_REG   = 0x0F0004;
        public const uint CMD_READ_REG16  = 0x0F0005;
        public const uint CMD_WRITE_REG16 = 0x0F0006;

        // SEJ (Security Engine) operations
        public const uint CMD_SEJ_DECRYPT = 0x0F0007;
        public const uint CMD_SEJ_ENCRYPT = 0x0F0008;

        // Memory operations
        public const uint CMD_READ_MEM    = 0x0F0009;
        public const uint CMD_WRITE_MEM   = 0x0F000A;

        /// <summary>
        /// Check if command is an Extensions command
        /// </summary>
        public static bool IsExtensionCommand(uint command)
        {
            return command >= CMD_RANGE_START && command <= CMD_RANGE_END;
        }
    }

    /// <summary>
    /// XML (V6) DA Extensions Commands
    /// Command strings using XML protocol
    /// </summary>
    public static class XmlExtensionCommands
    {
        // RPMB operations
        public const string CMD_READ_RPMB = "CMD:READ-RPMB";
        public const string CMD_WRITE_RPMB = "CMD:WRITE-RPMB";

        // Register access
        public const string CMD_READ_REG = "CMD:READ-REGISTER";
        public const string CMD_WRITE_REG = "CMD:WRITE-REGISTER";

        // SEJ operations
        public const string CMD_SEJ = "CMD:SEJ-OPERATION";

        // Memory operations
        public const string CMD_READ_MEM = "CMD:READ-MEMORY";
        public const string CMD_WRITE_MEM = "CMD:WRITE-MEMORY";
    }

    /// <summary>
    /// DA Extensions Compatibility Detection
    /// </summary>
    public static class DaExtensionsCompatibility
    {
        /// <summary>
        /// Check if device supports DA Extensions
        /// 
        /// Requirements:
        /// 1. Must be able to load patched DA (at least custom DA2)
        /// 2. Carbonara exploit not patched (devices after 2024 may have been patched)
        /// 3. Device does not have strict DA validation enabled
        /// </summary>
        public static bool SupportsExtensions(MtkDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
                return false;

            // Check if it is V5/V6 DA (Extensions only support these two)
            var daMode = deviceInfo.DaMode;
            if (daMode != 5 && daMode != 6)  // 5=XFlash, 6=XML
                return false;

            // TODO: Add Carbonara patch detection
            // if (IsCarbonaraPatched(deviceInfo))
            //     return false;

            // Devices after 2024 may not support it
            // Needs more detailed chip/date detection here
            return true;
        }

        /// <summary>
        /// Check if DA has been Carbonara-patched (affects Extensions loading)
        /// Reference: Penumbra docs - boot_to hardcoded address patched after 2024
        /// </summary>
        public static bool IsCarbonaraPatched(byte[] da2Data)
        {
            if (da2Data == null || da2Data.Length < 0x1000)
                return true;  // Conservative policy: assume patched if cannot confirm

            // Check if it contains hardcoded 0x40000000 address (patched feature)
            // Patched DA2 will force use 0x40000000 as boot_to address
            byte[] hardcodedAddr = { 0x00, 0x00, 0x00, 0x40 };  // 0x40000000 LE
            
            int count = 0;
            for (int i = 0; i < da2Data.Length - 4; i++)
            {
                if (da2Data[i] == hardcodedAddr[0] &&
                    da2Data[i + 1] == hardcodedAddr[1] &&
                    da2Data[i + 2] == hardcodedAddr[2] &&
                    da2Data[i + 3] == hardcodedAddr[3])
                {
                    count++;
                }
            }

            // If multiple hardcoded addresses appear, it might be a patched DA
            return count > 3;
        }

        /// <summary>
        /// Determine if device is a low memory device
        /// Low memory devices need special Extensions load address
        /// </summary>
        public static bool IsLowMemoryDevice(ushort hwCode)
        {
            // Usually entry-level chips (like MT6739, MT6761, etc.) have small memory
            return hwCode switch
            {
                0x0699 => true,  // MT6739
                0x0562 => true,  // MT6761
                0x0707 => true,  // MT6762
                _ => false
            };
        }
    }

    /// <summary>
    /// DA Extensions Status
    /// </summary>
    public enum ExtensionsStatus
    {
        /// <summary>Not loaded</summary>
        NotLoaded,
        
        /// <summary>Loading</summary>
        Loading,
        
        /// <summary>Loaded</summary>
        Loaded,
        
        /// <summary>Not supported</summary>
        NotSupported,
        
        /// <summary>Load failed</summary>
        LoadFailed
    }

    /// <summary>
    /// DA Extensions Manager (Interface definition)
    /// </summary>
    public interface IDaExtensionsManager
    {
        /// <summary>Current Extensions status</summary>
        ExtensionsStatus Status { get; }

        /// <summary>Check if Extensions are supported</summary>
        bool IsSupported();

        /// <summary>Load Extensions to device</summary>
        bool LoadExtensions(DaExtensionsConfig config);

        /// <summary>Unload Extensions</summary>
        void UnloadExtensions();

        /// <summary>Read RPMB</summary>
        byte[] ReadRpmb(uint address, uint length);

        /// <summary>Write RPMB</summary>
        bool WriteRpmb(uint address, byte[] data);

        /// <summary>Read register</summary>
        uint ReadRegister(uint address);

        /// <summary>Write register</summary>
        bool WriteRegister(uint address, uint value);

        /// <summary>SEJ decrypt</summary>
        byte[] SejDecrypt(byte[] data);

        /// <summary>SEJ encrypt</summary>
        byte[] SejEncrypt(byte[] data);
    }

    /// <summary>
    /// DA Extensions Utility Class
    /// </summary>
    public static class DaExtensionsHelper
    {
        /// <summary>
        /// Get recommended Extensions configuration
        /// </summary>
        public static DaExtensionsConfig GetRecommendedConfig(ushort hwCode, MtkDeviceInfo deviceInfo)
        {
            var config = new DaExtensionsConfig
            {
                UseLowMemoryAddress = DaExtensionsCompatibility.IsLowMemoryDevice(hwCode),
            };
            // ExtensionsBinary needs to be loaded externally

            return config;
        }

        /// <summary>
        /// Verify if Extensions binary is valid
        /// </summary>
        public static bool ValidateExtensionsBinary(byte[] binary)
        {
            if (binary == null || binary.Length < 0x1000)
                return false;

            // TODO: Add more detailed validation logic
            // For example: check ELF header, magic values, etc.

            return true;
        }

        /// <summary>
        /// Get Extensions version information
        /// </summary>
        public static string GetExtensionsVersion()
        {
            // TODO: Extract version info from Extensions binary
            return "1.0.0";
        }
    }

    /// <summary>
    /// DA Extensions Exception
    /// </summary>
    public class DaExtensionsException : Exception
    {
        public DaExtensionsException(string message) : base(message) { }
        public DaExtensionsException(string message, Exception innerException) : base(message, innerException) { }
    }
}
