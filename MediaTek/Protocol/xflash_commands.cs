// ============================================================================
// LoveAlways - MediaTek XFlash Protocol Command Definitions
// Reference: mtkclient/Library/DA/xflash/xflash_param.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

namespace LoveAlways.MediaTek.Protocol
{
    /// <summary>
    /// XFlash Protocol Command Codes
    /// </summary>
    public static class XFlashCmd
    {
        // Protocol Magic Number
        public const uint MAGIC = 0xFEEEEEEF;
        public const uint SYNC_SIGNAL = 0x434E5953;  // "SYNC"

        // ===== Data Operation Commands (0x01xxxx) =====
        public const uint UNKNOWN = 0x010000;
        public const uint DOWNLOAD = 0x010001;
        public const uint UPLOAD = 0x010002;
        public const uint FORMAT = 0x010003;
        public const uint WRITE_DATA = 0x010004;
        public const uint READ_DATA = 0x010005;
        public const uint FORMAT_PARTITION = 0x010006;
        public const uint SHUTDOWN = 0x010007;
        public const uint BOOT_TO = 0x010008;
        public const uint DEVICE_CTRL = 0x010009;
        public const uint INIT_EXT_RAM = 0x01000A;
        public const uint SWITCH_USB_SPEED = 0x01000B;
        public const uint READ_OTP_ZONE = 0x01000C;
        public const uint WRITE_OTP_ZONE = 0x01000D;
        public const uint WRITE_EFUSE = 0x01000E;
        public const uint READ_EFUSE = 0x01000F;
        public const uint NAND_BMT_REMARK = 0x010010;
        public const uint SETUP_ENVIRONMENT = 0x010100;
        public const uint SETUP_HW_INIT_PARAMS = 0x010101;

        // ===== Setting Commands (0x02xxxx) =====
        public const uint SET_BMT_PERCENTAGE = 0x020001;
        public const uint SET_BATTERY_OPT = 0x020002;
        public const uint SET_CHECKSUM_LEVEL = 0x020003;
        public const uint SET_RESET_KEY = 0x020004;
        public const uint SET_HOST_INFO = 0x020005;
        public const uint SET_META_BOOT_MODE = 0x020006;
        public const uint SET_EMMC_HWRESET_PIN = 0x020007;
        public const uint SET_GENERATE_GPX = 0x020008;
        public const uint SET_REGISTER_VALUE = 0x020009;
        public const uint SET_EXTERNAL_SIG = 0x02000A;
        public const uint SET_REMOTE_SEC_POLICY = 0x02000B;
        public const uint SET_ALL_IN_ONE_SIG = 0x02000C;
        public const uint SET_RSC_INFO = 0x02000D;
        public const uint SET_UPDATE_FW = 0x020010;
        public const uint SET_UFS_CONFIG = 0x020011;

        // ===== Information Retrieval Commands (0x04xxxx) =====
        public const uint GET_EMMC_INFO = 0x040001;
        public const uint GET_NAND_INFO = 0x040002;
        public const uint GET_NOR_INFO = 0x040003;
        public const uint GET_UFS_INFO = 0x040004;
        public const uint GET_DA_VERSION = 0x040005;
        public const uint GET_EXPIRE_DATA = 0x040006;
        public const uint GET_PACKET_LENGTH = 0x040007;
        public const uint GET_RANDOM_ID = 0x040008;
        public const uint GET_PARTITION_TBL_CATA = 0x040009;
        public const uint GET_CONNECTION_AGENT = 0x04000A;
        public const uint GET_USB_SPEED = 0x04000B;
        public const uint GET_RAM_INFO = 0x04000C;
        public const uint GET_CHIP_ID = 0x04000D;
        public const uint GET_OTP_LOCK_STATUS = 0x04000E;
        public const uint GET_BATTERY_VOLTAGE = 0x04000F;
        public const uint GET_RPMB_STATUS = 0x040010;
        public const uint GET_EXPIRE_DATE = 0x040011;
        public const uint GET_DRAM_TYPE = 0x040012;
        public const uint GET_DEV_FW_INFO = 0x040013;
        public const uint GET_HRID = 0x040014;
        public const uint GET_ERROR_DETAIL = 0x040015;
        public const uint SLA_ENABLED_STATUS = 0x040016;

        // ===== Lifecycle Commands (0x08xxxx) =====
        public const uint START_DL_INFO = 0x080001;
        public const uint END_DL_INFO = 0x080002;
        public const uint ACT_LOCK_OTP_ZONE = 0x080003;
        public const uint DISABLE_EMMC_HWRESET_PIN = 0x080004;
        public const uint CC_OPTIONAL_DOWNLOAD_ACT = 0x080005;
        public const uint DA_STOR_LIFE_CYCLE_CHECK = 0x080007;

        // ===== Control Commands (0x0Exxxx) =====
        public const uint UNKNOWN_CTRL_CODE = 0x0E0000;
        public const uint CTRL_STORAGE_TEST = 0x0E0001;
        public const uint CTRL_RAM_TEST = 0x0E0002;
        public const uint DEVICE_CTRL_READ_REGISTER = 0x0E0003;

        /// <summary>
        /// Get command name
        /// </summary>
        public static string GetCommandName(uint cmd)
        {
            return cmd switch
            {
                DOWNLOAD => "DOWNLOAD",
                UPLOAD => "UPLOAD",
                FORMAT => "FORMAT",
                WRITE_DATA => "WRITE_DATA",
                READ_DATA => "READ_DATA",
                FORMAT_PARTITION => "FORMAT_PARTITION",
                SHUTDOWN => "SHUTDOWN",
                BOOT_TO => "BOOT_TO",
                GET_EMMC_INFO => "GET_EMMC_INFO",
                GET_NAND_INFO => "GET_NAND_INFO",
                GET_UFS_INFO => "GET_UFS_INFO",
                GET_DA_VERSION => "GET_DA_VERSION",
                GET_PACKET_LENGTH => "GET_PACKET_LENGTH",
                GET_PARTITION_TBL_CATA => "GET_PARTITION_TBL",
                GET_RAM_INFO => "GET_RAM_INFO",
                GET_CHIP_ID => "GET_CHIP_ID",
                SET_CHECKSUM_LEVEL => "SET_CHECKSUM_LEVEL",
                _ => $"0x{cmd:X6}"
            };
        }
    }

    /// <summary>
    /// Checksum Algorithms
    /// </summary>
    public enum ChecksumAlgorithm : uint
    {
        None = 0,
        CRC32 = 1,
        MD5 = 2
    }

    /// <summary>
    /// Data Types
    /// </summary>
    public enum XFlashDataType : uint
    {
        ProtocolFlow = 0,
        ProtocolResponse = 1,
        ProtocolRaw = 2,
        Message = 2
    }

    /// <summary>
    /// Storage Types
    /// </summary>
    public enum StorageType : uint
    {
        Unknown = 0,
        EMMC = 1,
        NAND = 2,
        NOR = 3,
        UFS = 4
    }

    /// <summary>
    /// EMMC Partition Types
    /// </summary>
    public enum EmmcPartitionType : uint
    {
        User = 0,
        Boot1 = 1,
        Boot2 = 2,
        RPMB = 3,
        GP1 = 4,
        GP2 = 5,
        GP3 = 6,
        GP4 = 7
    }

    /// <summary>
    /// XFlash Error Codes
    /// </summary>
    public static class XFlashError
    {
        public const int OK = 0;
        public const int FAIL = -1;
        public const int TIMEOUT = -2;
        public const int CHECKSUM_ERROR = -3;
        public const int INVALID_PARAM = -4;
        public const int STORAGE_ERROR = -5;
        public const int DA_NOT_READY = -6;
        public const int PARTITION_NOT_FOUND = -7;
        public const int AUTH_FAILED = -8;

        public static string GetErrorMessage(int error)
        {
            return error switch
            {
                OK => "Success",
                FAIL => "Operation failed",
                TIMEOUT => "Timeout",
                CHECKSUM_ERROR => "Checksum error",
                INVALID_PARAM => "Invalid parameter",
                STORAGE_ERROR => "Storage error",
                DA_NOT_READY => "DA not ready",
                PARTITION_NOT_FOUND => "Partition not found",
                AUTH_FAILED => "Authentication failed",
                _ => $"Unknown error ({error})"
            };
        }
    }
}
