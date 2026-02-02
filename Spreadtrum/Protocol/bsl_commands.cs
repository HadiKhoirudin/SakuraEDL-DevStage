// ============================================================================
// LoveAlways - Spreadtrum BSL/FDL Command Definitions
// Spreadtrum/Unisoc BSL (Boot Second Loader) Commands
// ============================================================================

namespace LoveAlways.Spreadtrum.Protocol
{
    /// <summary>
    /// BSL Command Type (Reference spd_cmd.h)
    /// </summary>
    public enum BslCommand : byte
    {
        // ========================================
        // Connection/Handshake Commands
        // ========================================

        /// <summary>Connect command</summary>
        BSL_CMD_CONNECT = 0x00,

        // ========================================
        // Data Transfer Commands
        // ========================================


        /// <summary>Start data transfer (Send address and size)</summary>
        BSL_CMD_START_DATA = 0x01,

        /// <summary>Data block transfer</summary>
        BSL_CMD_MIDST_DATA = 0x02,

        /// <summary>End data transfer</summary>
        BSL_CMD_END_DATA = 0x03,

        /// <summary>Execute downloaded code</summary>
        BSL_CMD_EXEC_DATA = 0x04,

        /// <summary>Normal reboot / Reset device</summary>
        BSL_CMD_NORMAL_RESET = 0x05,
        BSL_CMD_RESET = 0x05,  // Alias

        /// <summary>Read flash content</summary>
        BSL_CMD_READ_FLASH = 0x06,

        /// <summary>Read chip type</summary>
        BSL_CMD_READ_CHIP_TYPE = 0x07,

        /// <summary>Read NV item</summary>
        BSL_CMD_READ_NVITEM = 0x08,

        /// <summary>Change baud rate / Set baud rate</summary>
        BSL_CMD_CHANGE_BAUD = 0x09,
        BSL_CMD_SET_BAUD = 0x09,  // Alias

        /// <summary>Erase Flash</summary>
        BSL_CMD_ERASE_FLASH = 0x0A,

        /// <summary>Repartition NAND Flash</summary>
        BSL_CMD_REPARTITION = 0x0B,

        /// <summary>Read Flash type</summary>
        BSL_CMD_READ_FLASH_TYPE = 0x0C,

        /// <summary>Read Flash information</summary>
        BSL_CMD_READ_FLASH_INFO = 0x0D,

        /// <summary>Read NOR Flash sector size</summary>
        BSL_CMD_READ_SECTOR_SIZE = 0x0F,

        // ========================================
        // Partition Read Commands (Correct Values!)
        // ========================================

        /// <summary>Read Flash Start</summary>
        BSL_CMD_READ_START = 0x10,

        /// <summary>Read Flash Midst Data</summary>
        BSL_CMD_READ_MIDST = 0x11,

        /// <summary>Read Flash End</summary>
        BSL_CMD_READ_END = 0x12,

        // ========================================
        // Device Control Commands
        // ========================================

        /// <summary>Keep charging</summary>
        BSL_CMD_KEEP_CHARGE = 0x13,

        /// <summary>Set extended table</summary>
        BSL_CMD_EXTTABLE = 0x14,

        /// <summary>Read Flash UID</summary>
        BSL_CMD_READ_FLASH_UID = 0x15,

        /// <summary>Read Soft SIM EID</summary>
        BSL_CMD_READ_SOFTSIM_EID = 0x16,

        /// <summary>Power off</summary>
        BSL_CMD_POWER_OFF = 0x17,

        /// <summary>Check Root</summary>
        BSL_CMD_CHECK_ROOT = 0x19,

        /// <summary>Read chip UID</summary>
        BSL_CMD_READ_CHIP_UID = 0x1A,

        /// <summary>Enable Flash Write</summary>
        BSL_CMD_ENABLE_WRITE_FLASH = 0x1B,

        /// <summary>Enable Secure Boot / Read Version (used for version info)</summary>
        BSL_CMD_ENABLE_SECUREBOOT = 0x1C,
        BSL_CMD_READ_VERSION = 0x1C,  // Alias, some FDL use this for reading version        
        /// <summary>Identify start</summary>
        BSL_CMD_IDENTIFY_START = 0x1D,

        /// <summary>Identify end</summary>
        BSL_CMD_IDENTIFY_END = 0x1E,

        /// <summary>Read CU Ref</summary>
        BSL_CMD_READ_CU_REF = 0x1F,

        /// <summary>Read Ref info</summary>
        BSL_CMD_READ_REFINFO = 0x20,

        /// <summary>Disable transcode (Required for FDL2)</summary>
        BSL_CMD_DISABLE_TRANSCODE = 0x21,

        /// <summary>Write NV item</summary>
        BSL_CMD_WRITE_NVITEM = 0x22,

        /// <summary>Write Date/Time to miscdata</summary>
        BSL_CMD_WRITE_DATETIME = 0x22,

        /// <summary>Custom Dummy</summary>
        BSL_CMD_CUST_DUMMY = 0x23,

        /// <summary>Read RF Transceiver Type</summary>
        BSL_CMD_READ_RF_TRANSCEIVER_TYPE = 0x24,

        /// <summary>Set debug information</summary>
        BSL_CMD_SET_DEBUGINFO = 0x25,

        /// <summary>DDR Check</summary>
        BSL_CMD_DDR_CHECK = 0x26,

        /// <summary>Self refresh</summary>
        BSL_CMD_SELF_REFRESH = 0x27,

        /// <summary>Enable raw data writing (for 0x31 and 0x33)</summary>
        BSL_CMD_WRITE_RAW_DATA_ENABLE = 0x28,

        /// <summary>Read NAND block information</summary>
        BSL_CMD_READ_NAND_BLOCK_INFO = 0x29,

        /// <summary>Set first mode</summary>
        BSL_CMD_SET_FIRST_MODE = 0x2A,

        /// <summary>Read partition list</summary>
        BSL_CMD_READ_PARTITION = 0x2D,

        /// <summary>Unlock</summary>
        BSL_CMD_UNLOCK = 0x30,

        /// <summary>Read public key / raw data packet</summary>
        BSL_CMD_READ_PUBKEY = 0x31,
        BSL_CMD_DLOAD_RAW_START = 0x31,

        /// <summary>Write flush data / Send signature</summary>
        BSL_CMD_WRITE_FLUSH_DATA = 0x32,
        BSL_CMD_SEND_SIGNATURE = 0x32,

        /// <summary>Full raw file</summary>
        BSL_CMD_DLOAD_RAW_START2 = 0x33,

        /// <summary>Read log</summary>
        BSL_CMD_READ_LOG = 0x35,

        /// <summary>Read eFuse</summary>
        BSL_CMD_READ_EFUSE = 0x60,

        /// <summary>Baud rate detection (Internal use) - Sends multiple 0x7E</summary>
        BSL_CMD_CHECK_BAUD = 0x7E,

        /// <summary>End flash process</summary>
        BSL_CMD_END_PROCESS = 0x7F,

        // ========================================
        // Response Types
        // ========================================

        /// <summary>ACK Response</summary>
        BSL_REP_ACK = 0x80,

        /// <summary>Version Response</summary>
        BSL_REP_VER = 0x81,

        /// <summary>Invalid Command</summary>
        BSL_REP_INVALID_CMD = 0x82,

        /// <summary>Unknown Command</summary>
        BSL_REP_UNKNOWN_CMD = 0x83,

        /// <summary>Operation Failed</summary>
        BSL_REP_OPERATION_FAILED = 0x84,

        /// <summary>Unsupported Baud Rate</summary>
        BSL_REP_NOT_SUPPORT_BAUDRATE = 0x85,

        /// <summary>Download Not Started</summary>
        BSL_REP_DOWN_NOT_START = 0x86,

        /// <summary>Repeated Start Download</summary>
        BSL_REP_DOWN_MULTI_START = 0x87,

        /// <summary>Download Ended Early</summary>
        BSL_REP_DOWN_EARLY_END = 0x88,

        /// <summary>Download Target Address Error</summary>
        BSL_REP_DOWN_DEST_ERROR = 0x89,

        /// <summary>Download Size Error</summary>
        BSL_REP_DOWN_SIZE_ERROR = 0x8A,

        /// <summary>Verify Error (Data verification failed)</summary>
        BSL_REP_VERIFY_ERROR = 0x8B,

        /// <summary>Not Verified</summary>
        BSL_REP_NOT_VERIFY = 0x8C,

        /// <summary>Not Enough Memory</summary>
        BSL_PHONE_NOT_ENOUGH_MEMORY = 0x8D,

        /// <summary>Wait Input Timeout</summary>
        BSL_PHONE_WAIT_INPUT_TIMEOUT = 0x8E,

        /// <summary>Operation Succeed (Internal)</summary>
        BSL_PHONE_SUCCEED = 0x8F,

        /// <summary>Valid Baud Rate</summary>
        BSL_PHONE_VALID_BAUDRATE = 0x90,

        /// <summary>Repeat Continue</summary>
        BSL_PHONE_REPEAT_CONTINUE = 0x91,

        /// <summary>Repeat Break</summary>
        BSL_PHONE_REPEAT_BREAK = 0x92,

        /// <summary>Read Flash Response / Data Response</summary>
        BSL_REP_READ_FLASH = 0x93,
        BSL_REP_DATA = 0x93,

        /// <summary>Chip Type Response</summary>
        BSL_REP_CHIP_TYPE = 0x94,

        /// <summary>NV Item Response</summary>
        BSL_REP_READ_NVITEM = 0x95,

        /// <summary>Incompatible Partition</summary>
        BSL_REP_INCOMPATIBLE_PARTITION = 0x96,

        /// <summary>Signature Verification Failed</summary>
        BSL_REP_SIGN_VERIFY_ERROR = 0xA6,

        /// <summary>Check Root True</summary>
        BSL_REP_CHECK_ROOT_TRUE = 0xA7,

        /// <summary>Chip UID Response</summary>
        BSL_REP_READ_CHIP_UID = 0xAB,

        /// <summary>Partition Table Response</summary>
        BSL_REP_PARTITION = 0xBA,

        /// <summary>Log Response</summary>
        BSL_REP_READ_LOG = 0xBB,

        /// <summary>Unsupported Command</summary>
        BSL_REP_UNSUPPORTED_COMMAND = 0xFE,

        /// <summary>Log Output</summary>
        BSL_REP_LOG = 0xFF,

        // ========================================
        // Compatibility Definitions
        // ========================================

        /// <summary>Flash Info Response</summary>
        BSL_REP_FLASH_INFO = 0x92,
    }

    /// <summary>
    /// BSL Error Code
    /// </summary>
    public enum BslError : ushort
    {
        SUCCESS = 0x0000,
        VERIFY_ERROR = 0x0001,
        CHECKSUM_ERROR = 0x0002,
        PACKET_ERROR = 0x0003,
        SIZE_ERROR = 0x0004,
        WAIT_TIMEOUT = 0x0005,
        DEVICE_ERROR = 0x0006,
        WRITE_ERROR = 0x0007,
        READ_ERROR = 0x0008,
        ERASE_ERROR = 0x0009,
        FLASH_ERROR = 0x000A,
        UNSUPPORTED = 0x000B,
        INVALID_CMD = 0x000C,
        SECURITY_ERROR = 0x000D,
        UNLOCK_ERROR = 0x000E,
    }

    /// <summary>
    /// FDL Stage
    /// </summary>
    public enum FdlStage
    {
        /// <summary>Not Loaded</summary>
        None,

        /// <summary>FDL1 - Stage 1 Bootloader</summary>
        FDL1,

        /// <summary>FDL2 - Stage 2 Flashing</summary>
        FDL2
    }

    /// <summary>
    /// Spreadtrum Device State
    /// </summary>
    public enum SprdDeviceState
    {
        /// <summary>Disconnected</summary>
        Disconnected,

        /// <summary>Connected (ROM Mode)</summary>
        Connected,

        /// <summary>FDL1 Loaded</summary>
        Fdl1Loaded,

        /// <summary>FDL2 Loaded (Flashable)</summary>
        Fdl2Loaded,

        /// <summary>Error State</summary>
        Error
    }

    /// <summary>
    /// Spreadtrum Chip Platform (Consolidated from spd_dump, SPRDClientCore, iReverse and other open source projects)
    /// </summary>
    public static class SprdPlatform
    {
        // ========== SC6xxx Feature Phone Series ==========
        public const uint SC6500 = 0x6500;
        public const uint SC6530 = 0x6530;
        public const uint SC6531 = 0x6531;
        public const uint SC6531E = 0x6531;
        public const uint SC6531EFM = 0x65310001;
        public const uint SC6531DA = 0x65310002;
        public const uint SC6531H = 0x65310003;
        public const uint SC6533G = 0x6533;
        public const uint SC6533GF = 0x65330001;
        public const uint SC6600 = 0x6600;
        public const uint SC6600L = 0x66000001;
        public const uint SC6610 = 0x6610;
        public const uint SC6620 = 0x6620;
        public const uint SC6800H = 0x6800;

        // ========== SC77xx Series (Legacy 3G/4G) ==========
        public const uint SC7701 = 0x7701;
        public const uint SC7702 = 0x7702;
        public const uint SC7710 = 0x7710;
        public const uint SC7715 = 0x7715;
        public const uint SC7715A = 0x77150001;
        public const uint SC7720 = 0x7720;
        public const uint SC7727S = 0x7727;
        public const uint SC7730 = 0x7730;
        public const uint SC7730A = 0x77300001;
        public const uint SC7730S = 0x77300002;
        public const uint SC7731 = 0x7731;
        public const uint SC7731C = 0x77310001;
        public const uint SC7731E = 0x77310002;
        public const uint SC7731G = 0x77310003;
        public const uint SC7731GF = 0x77310004;
        public const uint SC7731EF = 0x77310005;

        // ========== SC85xx Series ==========
        public const uint SC8521E = 0x8521;
        public const uint SC8541 = 0x8541;
        public const uint SC8541E = 0x8541;
        public const uint SC8541EF = 0x85410001;
        public const uint SC8551 = 0x8551;
        public const uint SC8551E = 0x85510001;
        public const uint SC8581 = 0x8581;
        public const uint SC8581A = 0x85810001;

        // ========== SC96xx/SC98xx Series ==========
        public const uint SC9600 = 0x9600;
        public const uint SC9610 = 0x9610;
        public const uint SC9620 = 0x9620;
        public const uint SC9630 = 0x9630;
        public const uint SC9820 = 0x9820;
        public const uint SC9820A = 0x98200001;
        public const uint SC9820E = 0x98200002;
        public const uint SC9830 = 0x9830;
        public const uint SC9830A = 0x98300001;
        public const uint SC9830I = 0x98300002;
        public const uint SC9830IA = 0x98300003;
        public const uint SC9832 = 0x9832;
        public const uint SC9832A = 0x98320001;
        public const uint SC9832E = 0x98320002;
        public const uint SC9832EP = 0x98320003;
        public const uint SC9850 = 0x9850;
        public const uint SC9850KA = 0x98500001;
        public const uint SC9850KH = 0x98500002;
        public const uint SC9850S = 0x98500003;
        public const uint SC9853I = 0x9853;
        public const uint SC9860 = 0x9860;
        public const uint SC9860G = 0x98600001;
        public const uint SC9860GV = 0x98600002;
        public const uint SC9861 = 0x9861;
        public const uint SC9863 = 0x9863;
        public const uint SC9863A = 0x98630001;

        // ========== Unisoc T Series (4G) ==========
        public const uint T310 = 0x0310;
        public const uint T606 = 0x0606;
        public const uint T610 = 0x0610;
        public const uint T612 = 0x0612;
        public const uint T616 = 0x0616;
        public const uint T618 = 0x0618;
        public const uint T700 = 0x0700;
        public const uint T760 = 0x0760;
        public const uint T770 = 0x0770;
        public const uint T820 = 0x0820;
        public const uint T900 = 0x0900;

        // ========== Unisoc T Series (5G) ==========
        public const uint T740 = 0x0740;
        public const uint T750 = 0x0750;
        public const uint T765 = 0x0765;
        public const uint T7510 = 0x7510;
        public const uint T7520 = 0x7520;
        public const uint T7525 = 0x7525;
        public const uint T7530 = 0x7530;
        public const uint T7560 = 0x7560;
        public const uint T7570 = 0x7570;
        public const uint T8000 = 0x8000;
        public const uint T8200 = 0x8200;

        // ========== UMS Series (Unisoc Mobile SOC) ==========
        public const uint UMS312 = 0x0312;      // T310 Variant
        public const uint UMS512 = 0x0512;      // T618 Variant
        public const uint UMS9230 = 0x9230;     // T606 Variant
        public const uint UMS9620 = 0x96200001; // T740 Variant (Distinguish from SC9620)
        public const uint UMS9621 = 0x96210001;

        // ========== UIS Series (Unisoc IoT SOC) ==========
        public const uint UIS7862 = 0x78620001;
        public const uint UIS7863 = 0x78630001;
        public const uint UIS7870 = 0x78700001;
        public const uint UIS7885 = 0x78850001;
        public const uint UIS8581 = 0x85810002; // Distinguish from SC8581
        public const uint UIS8910DM = 0x89100001;

        // ========== T1xx Series (4G Feature Phone) - Reference spreadtrum_flash ==========
        public const uint T107 = 0x0107;      // UMS9107
        public const uint T117 = 0x0117;      // UMS9117 (Most common)
        public const uint T127 = 0x0127;      // UMS9127
        public const uint UMS9107 = 0x9107;   // T107 Alias
        public const uint UMS9117 = 0x9117;   // T117 Alias
        public const uint UMS9127 = 0x9127;   // T127 Alias

        // ========== W Series (Feature Phone/IoT) ==========
        public const uint W117 = 0x01170001;  // Use different ID to avoid conflict with T117
        public const uint W217 = 0x0217;
        public const uint W307 = 0x0307;

        // ========== UWS 系列 (Wearable) ==========
        public const uint UWS6121 = 0x6121;
        public const uint UWS6122 = 0x6122;
        public const uint UWS6131 = 0x6131;
        public const uint UWS6152 = 0x6152;     // Chip asked by user

        // ========== S Series (Tablet/IoT) ==========
        public const uint S9863A1H10 = 0x9863;

        // ========== Special Chip IDs ==========
        public const uint CHIP_UNKNOWN = 0x0000;
        public const uint CHIP_SC6800H = 0x6800;
        public const uint CHIP_SC8800G = 0x8800;

        /// <summary>
        /// Get platform name
        /// </summary>
        public static string GetPlatformName(uint chipId)
        {
            // Handle chip IDs with sub-models (High 16 bits are the base ID)
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;

            switch (chipId)
            {
                // ========== SC6xxx Feature Phone Series ==========
                case SC6500: return "SC6500";
                case SC6530: return "SC6530";
                case SC6531E: return "SC6531E";
                case SC6531EFM: return "SC6531E-FM";
                case SC6531DA: return "SC6531DA";
                case SC6531H: return "SC6531H";
                case SC6533G: return "SC6533G";
                case SC6533GF: return "SC6533GF";
                case SC6600: return "SC6600";
                case SC6600L: return "SC6600L";
                case SC6610: return "SC6610";
                case SC6620: return "SC6620";
                case SC6800H: return "SC6800H";

                // ========== SC77xx Series ==========
                case SC7701: return "SC7701";
                case SC7702: return "SC7702";
                case SC7710: return "SC7710";
                case SC7715: return "SC7715";
                case SC7715A: return "SC7715A";
                case SC7720: return "SC7720";
                case SC7727S: return "SC7727S";
                case SC7730: return "SC7730";
                case SC7730A: return "SC7730A";
                case SC7730S: return "SC7730S";
                case SC7731: return "SC7731";
                case SC7731C: return "SC7731C";
                case SC7731E: return "SC7731E";
                case SC7731G: return "SC7731G";
                case SC7731GF: return "SC7731GF";
                case SC7731EF: return "SC7731EF";

                // ========== SC85xx Series ==========
                case SC8521E: return "SC8521E";
                case SC8541E: return "SC8541E";
                case SC8541EF: return "SC8541EF";
                case SC8551: return "SC8551";
                case SC8551E: return "SC8551E";
                case SC8581: return "SC8581";
                case SC8581A: return "SC8581A";

                // ========== SC96xx/SC98xx Series ==========
                case SC9600: return "SC9600";
                case SC9610: return "SC9610";
                case SC9620: return "SC9620";
                case SC9630: return "SC9630";
                case SC9820: return "SC9820";
                case SC9820A: return "SC9820A";
                case SC9820E: return "SC9820E";
                case SC9830: return "SC9830";
                case SC9830A: return "SC9830A";
                case SC9830I: return "SC9830i";
                case SC9830IA: return "SC9830iA";
                case SC9832: return "SC9832";
                case SC9832A: return "SC9832A";
                case SC9832E: return "SC9832E";
                case SC9832EP: return "SC9832EP";
                case SC9850: return "SC9850";
                case SC9850KA: return "SC9850KA";
                case SC9850KH: return "SC9850KH";
                case SC9850S: return "SC9850S";
                case SC9853I: return "SC9853i";
                case SC9860: return "SC9860";
                case SC9860G: return "SC9860G";
                case SC9860GV: return "SC9860GV";
                case SC9861: return "SC9861";
                case SC9863: return "SC9863A";
                case SC9863A: return "SC9863A";

                // ========== Unisoc T Series (4G) ==========
                case T310: return "T310";
                case T606: return "T606";
                case T610: return "T610";
                case T612: return "T612";
                case T616: return "T616";
                case T618: return "T618";
                case T700: return "T700";
                case T760: return "T760";
                case T770: return "T770";
                case T820: return "T820";
                case T900: return "T900";

                // ========== Unisoc T Series (5G) ==========
                case T740: return "T740 (5G)";
                case T750: return "T750 (5G)";
                case T765: return "T765 (5G)";
                case T7510: return "T7510 (5G)";
                case T7520: return "T7520 (5G)";
                case T7525: return "T7525 (5G)";
                case T7530: return "T7530 (5G)";
                case T7560: return "T7560 (5G)";
                case T7570: return "T7570 (5G)";
                case T8000: return "T8000 (5G)";
                case T8200: return "T8200 (5G)";

                // ========== Unisoc T1xx Series (4G Feature Phone) ==========
                case T107: return "T107/UMS9107 (4G Feature Phone)";
                case T117: return "T117/UMS9117 (4G Feature Phone)";
                case T127: return "T127/UMS9127 (4G Feature Phone)";
                case UMS9107: return "UMS9107 (T107)";
                case UMS9117: return "UMS9117 (T117)";
                case UMS9127: return "UMS9127 (T127)";

                // ========== UMS Series ==========
                case UMS312: return "UMS312 (T310)";
                case UMS512: return "UMS512 (T618)";
                case UMS9230: return "UMS9230 (T606)";
                case UMS9620: return "UMS9620 (T740)";
                case UMS9621: return "UMS9621";

                // ========== UIS Series ==========
                case UIS7862: return "UIS7862";
                case UIS7863: return "UIS7863";
                case UIS7870: return "UIS7870";
                case UIS7885: return "UIS7885";
                case UIS8581: return "UIS8581";
                case UIS8910DM: return "UIS8910DM";

                // ========== W Series ==========
                case W117: return "W117";
                case W217: return "W217";
                case W307: return "W307";

                // ========== UWS Series ==========
                case UWS6121: return "UWS6121";
                case UWS6122: return "UWS6122";
                case UWS6131: return "UWS6131";
                case UWS6152: return "UWS6152";

                default:
                    // Attempt to match Base ID
                    if (baseId == 0x6531) return string.Format("SC6531 (0x{0:X})", chipId);
                    if (baseId == 0x6533) return string.Format("SC6533 (0x{0:X})", chipId);
                    if (baseId == 0x7731) return string.Format("SC7731 (0x{0:X})", chipId);
                    if (baseId == 0x7730) return string.Format("SC7730 (0x{0:X})", chipId);
                    if (baseId == 0x8541) return string.Format("SC8541 (0x{0:X})", chipId);
                    if (baseId == 0x9820) return string.Format("SC9820 (0x{0:X})", chipId);
                    if (baseId == 0x9830) return string.Format("SC9830 (0x{0:X})", chipId);
                    if (baseId == 0x9832) return string.Format("SC9832 (0x{0:X})", chipId);
                    if (baseId == 0x9850) return string.Format("SC9850 (0x{0:X})", chipId);
                    if (baseId == 0x9860) return string.Format("SC9860 (0x{0:X})", chipId);
                    if (baseId == 0x9863) return string.Format("SC9863 (0x{0:X})", chipId);
                    return string.Format("Unknown (0x{0:X})", chipId);
            }
        }

        /// <summary>
        /// Get FDL1 default load address
        /// Reference: spd_dump.c, SPRDClientCore, iReverse projects
        /// </summary>
        public static uint GetFdl1Address(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;

            switch (baseId)
            {
                // ========== New platforms requiring Exploit (0x65000800) ==========
                case 0x9863:  // SC9863A
                case 0x8581:  // SC8581A
                case 0x9853:  // SC9853i
                    return 0x65000800;

                // ========== SC9850/SC9860/SC9861 Series (0x65000000) ==========
                case 0x9850:  // SC9850K
                case 0x9860:  // SC9860G
                case 0x9861:  // SC9861
                    return 0x65000000;

                // ========== T6xx/T7xx Series requiring Exploit (0x65000800) ==========
                case 0x0610:  // T610
                case 0x0612:  // T612
                case 0x0616:  // T616
                case 0x0618:  // T618
                case 0x0512:  // UMS512
                case 0x0700:  // T700
                case 0x0760:  // T760
                case 0x0770:  // T770
                    return 0x65000800;

                // ========== Standard New Platforms (0x5500) ==========
                case 0x8521:  // SC8521E
                case 0x8541:  // SC8541E
                case 0x8551:  // SC8551
                case 0x9832:  // SC9832E
                case 0x0310:  // T310
                case 0x0312:  // UMS312
                case 0x0606:  // T606
                case 0x9230:  // UMS9230
                case 0x0820:  // T820
                case 0x0900:  // T900
                case 0x0740:  // T740
                case 0x0750:  // T750
                case 0x0765:  // T765
                case 0x7510:  // T7510
                case 0x7520:  // T7520
                case 0x7525:  // T7525
                case 0x7530:  // T7530
                case 0x7560:  // T7560
                case 0x7570:  // T7570
                case 0x8000:  // T8000
                case 0x8200:  // T8200
                    return 0x5500;

                // ========== Feature Phone Platforms (0x40004000) ==========
                case 0x6500:  // SC6500
                case 0x6530:  // SC6530
                case 0x6531:  // SC6531E
                case 0x6533:  // SC6533G
                case 0x6600:  // SC6600
                case 0x6610:  // SC6610
                case 0x6620:  // SC6620
                case 0x6800:  // SC6800H
                    return 0x40004000;  // Feature phone FDL1 address

                // ========== W Series Feature Phones (0x40004000) ==========
                // Note: W117 is actually T117, using T1xx series address
                case 0x0217:  // W217
                case 0x0307:  // W307
                    return 0x40004000;

                // ========== T1xx 4G Feature Phones (0x6200) - Reference spreadtrum_flash ==========
                case 0x0107:  // T107/UMS9107
                case 0x0117:  // T117/UMS9117 (also called W117)
                case 0x0127:  // T127/UMS9127
                case 0x9107:  // UMS9107
                case 0x9117:  // UMS9117
                case 0x9127:  // UMS9127
                    return 0x6200;

                // ========== UWS Wearable Series (0x5500) ==========
                case 0x6121:  // UWS6121
                case 0x6122:  // UWS6122
                case 0x6131:  // UWS6131
                case 0x6152:  // UWS6152
                    return 0x5500;

                // ========== UIS IoT Series (0x5500) ==========
                case 0x7862:  // UIS7862
                case 0x7863:  // UIS7863
                case 0x7870:  // UIS7870
                case 0x7885:  // UIS7885
                case 0x8910:  // UIS8910DM
                    return 0x5500;

                // ========== Legacy Platforms (0x5000) ==========
                case 0x7701:  // SC7701
                case 0x7702:  // SC7702
                case 0x7710:  // SC7710
                case 0x7715:  // SC7715
                case 0x7720:  // SC7720
                case 0x7727:  // SC7727S
                case 0x7730:  // SC7730
                case 0x7731:  // SC7731
                case 0x9600:  // SC9600
                case 0x9610:  // SC9610
                case 0x9620:  // SC9620
                case 0x9630:  // SC9630
                case 0x9820:  // SC9820
                case 0x9830:  // SC9830
                default:
                    return 0x5000;  // Legacy platform
            }
        }

        /// <summary>
        /// Get FDL2 default load address
        /// Reference: spd_dump.c, SPRDClientCore, iReverse projects
        /// </summary>
        public static uint GetFdl2Address(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;

            switch (baseId)
            {
                // ========== SC8541E / SC9832E / SC9863A / T6xx 系列 (0x9EFFFE00) ==========
                case 0x8521:  // SC8521E
                case 0x8541:  // SC8541E
                case 0x8551:  // SC8551
                case 0x8581:  // SC8581A
                case 0x9832:  // SC9832E
                case 0x9853:  // SC9853i
                case 0x9863:  // SC9863A
                case 0x0310:  // T310
                case 0x0312:  // UMS312
                case 0x0606:  // T606
                case 0x9230:  // UMS9230
                case 0x0610:  // T610
                case 0x0612:  // T612
                case 0x0616:  // T616
                case 0x0618:  // T618
                case 0x0512:  // UMS512
                    return 0x9EFFFE00;

                // ========== T7xx 需要 Exploit 系列 (0xB4FFFE00) ==========
                case 0x0700:  // T700
                case 0x0760:  // T760
                case 0x0770:  // T770
                    return 0xB4FFFE00;

                // ========== T8xx / T9xx / 5G Series (0x9F000000) ==========
                case 0x0820:  // T820
                case 0x0900:  // T900
                case 0x0740:  // T740
                case 0x0750:  // T750
                case 0x0765:  // T765
                case 0x7510:  // T7510
                case 0x7520:  // T7520
                case 0x7525:  // T7525
                case 0x7530:  // T7530
                case 0x7560:  // T7560
                case 0x7570:  // T7570
                case 0x8000:  // T8000
                case 0x8200:  // T8200
                    return 0x9F000000;

                // ========== SC9850/SC9860/SC9861 Series (0x8C800000) ==========
                case 0x9850:  // SC9850K
                case 0x9860:  // SC9860G
                case 0x9861:  // SC9861
                    return 0x8C800000;

                // ========== Legacy Platforms SC77xx / SC98xx (0x8A800000) ==========
                case 0x7701:  // SC7701
                case 0x7702:  // SC7702
                case 0x7710:  // SC7710
                case 0x7715:  // SC7715
                case 0x7720:  // SC7720
                case 0x7727:  // SC7727S
                case 0x7730:  // SC7730
                case 0x7731:  // SC7731
                case 0x9600:  // SC9600
                case 0x9610:  // SC9610
                case 0x9630:  // SC9630
                case 0x9820:  // SC9820
                case 0x9830:  // SC9830
                    return 0x8A800000;

                // ========== Feature Phone Platforms (0x14000000) ==========
                case 0x6500:  // SC6500
                case 0x6530:  // SC6530
                case 0x6531:  // SC6531E
                case 0x6533:  // SC6533G
                case 0x6600:  // SC6600
                case 0x6610:  // SC6610
                case 0x6620:  // SC6620
                case 0x6800:  // SC6800H
                // Note: W117 is actually T117, using T1xx series address
                case 0x0217:  // W217
                case 0x0307:  // W307
                    return 0x14000000;

                // ========== T1xx 4G Feature Phones (0x80100000) - Reference spreadtrum_flash ==========
                case 0x0107:  // T107/UMS9107
                case 0x0117:  // T117/UMS9117 (also called W117)
                case 0x0127:  // T127/UMS9127
                case 0x9107:  // UMS9107
                case 0x9117:  // UMS9117
                case 0x9127:  // UMS9127
                    return 0x80100000;

                // ========== UWS Wearable Series (0x9EFFFE00) ==========
                case 0x6121:  // UWS6121
                case 0x6122:  // UWS6122
                case 0x6131:  // UWS6131
                case 0x6152:  // UWS6152
                    return 0x9EFFFE00;

                // ========== UIS IoT Series (0x9EFFFE00) ==========
                case 0x7862:  // UIS7862
                case 0x7863:  // UIS7863
                case 0x7870:  // UIS7870
                case 0x7885:  // UIS7885
                case 0x8910:  // UIS8910DM
                    return 0x9EFFFE00;

                default:
                    return 0x9EFFFE00;  // Default address (Applicable to most new platforms)
            }
        }

        /// <summary>
        /// Get exec_addr (Used for signature bypass)
        /// Reference: custom_exec_no_verify mechanism in spd_dump
        /// These addresses are the addresses of the verify function in BROM.
        /// Bypass verification by writing code that returns success.
        /// </summary>
        public static uint GetExecAddress(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;

            switch (baseId)
            {
                // ========== SC9863A Series ==========
                // Reference: spd_dump exec_addr 0x65012f48
                case 0x9863:  // SC9863A
                    return 0x65012f48;

                // ========== T6xx Series (UMS512) ==========
                // These chips use similar BROM, same exec_addr
                case 0x0610:  // T610
                case 0x0612:  // T612
                case 0x0616:  // T616
                case 0x0618:  // T618
                case 0x0512:  // UMS512
                    return 0x65012f48;  // Same as SC9863A

                // ========== SC9853i ==========
                case 0x9853:  // SC9853i
                    return 0x65012f48;

                // ========== SC8581A ==========
                case 0x8581:  // SC8581A
                    return 0x65012f48;

                // ========== SC9850/SC9860 Series ==========
                // These older chips may use different addresses
                case 0x9850:  // SC9850K
                case 0x9860:  // SC9860G
                case 0x9861:  // SC9861
                    return 0x65012000;  // Need verification

                // ========== T7xx Series (Requires Exploit) ==========
                // Verified: T760 uses exec_addr 0x65012f48
                case 0x0700:  // T700
                case 0x0760:  // T760 ✓ Verified
                case 0x0770:  // T770
                    return 0x65012f48;

                // ========== 5G Series ==========
                case 0x0740:  // T740
                case 0x0750:  // T750
                case 0x0765:  // T765
                case 0x7510:  // T7510
                case 0x7520:  // T7520
                case 0x7525:  // T7525
                case 0x7530:  // T7530
                case 0x7560:  // T7560
                case 0x7570:  // T7570
                case 0x8000:  // T8000
                case 0x8200:  // T8200
                    return 0;  // 5G platform may not need exec bypass

                // ========== Feature Phone Platforms (Not Required) ==========
                case 0x6500:  // SC6500
                case 0x6530:  // SC6530
                case 0x6531:  // SC6531E
                case 0x6533:  // SC6533G
                case 0x6600:  // SC6600
                case 0x6610:  // SC6610
                case 0x6620:  // SC6620
                case 0x6800:  // SC6800H
                    return 0;  // Feature phone does not need exec bypass

                // ========== T1xx 4G Feature Phones ==========
                case 0x0107:  // T107
                case 0x0117:  // T117
                case 0x0127:  // T127
                case 0x9107:  // UMS9107
                case 0x9117:  // UMS9117
                case 0x9127:  // UMS9127
                    return 0;  // 4G Feature phone usually not required

                // ========== Legacy Platforms (Not Required) ==========
                case 0x7701:  // SC7701
                case 0x7702:  // SC7702
                case 0x7710:  // SC7710
                case 0x7715:  // SC7715
                case 0x7720:  // SC7720
                case 0x7727:  // SC7727S
                case 0x7730:  // SC7730
                case 0x7731:  // SC7731
                case 0x9600:  // SC9600
                case 0x9610:  // SC9610
                case 0x9620:  // SC9620
                case 0x9630:  // SC9630
                case 0x9820:  // SC9820
                case 0x9830:  // SC9830
                case 0x9832:  // SC9832E
                    return 0;  // Legacy platform not required

                // ========== UWS Wearable ==========
                case 0x6121:  // UWS6121
                case 0x6122:  // UWS6122
                case 0x6131:  // UWS6131
                case 0x6152:  // UWS6152
                    return 0;  // Wearable platform not required

                default:
                    // For unknown chips, if FDL1 address is 0x65000800, then it may need exec bypass
                    if (GetFdl1Address(chipId) == 0x65000800)
                    {
                        return 0x65012f48;  // Default attempt
                    }
                    return 0;  // Default not using exec bypass
            }
        }

        /// <summary>
        /// Check if chip needs exec_no_verify bypass
        /// </summary>
        public static bool NeedsExecBypass(uint chipId)
        {
            return GetExecAddress(chipId) > 0;
        }

        /// <summary>
        /// Determine if it is a 5G platform based on chip ID
        /// </summary>
        public static bool Is5GPlatform(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;
            switch (baseId)
            {
                case 0x0740:  // T740
                case 0x0750:  // T750
                case 0x0765:  // T765
                case 0x7510:  // T7510
                case 0x7520:  // T7520
                case 0x7525:  // T7525
                case 0x7530:  // T7530
                case 0x7560:  // T7560
                case 0x7570:  // T7570
                case 0x8000:  // T8000
                case 0x8200:  // T8200
                case 0x9620:  // UMS9620
                case 0x9621:  // UMS9621
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determine storage type based on chip ID
        /// </summary>
        public static string GetStorageType(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;

            switch (baseId)
            {
                // ========== UFS 存储 (高端 5G 平台) ==========
                case 0x0820:  // T820
                case 0x0900:  // T900
                case 0x0740:  // T740
                case 0x0750:  // T750
                case 0x0765:  // T765
                case 0x7510:  // T7510
                case 0x7520:  // T7520
                case 0x7525:  // T7525
                case 0x7530:  // T7530
                case 0x7560:  // T7560
                case 0x7570:  // T7570
                case 0x8000:  // T8000
                case 0x8200:  // T8200
                case 0x9620:  // UMS9620
                case 0x9621:  // UMS9621
                    return "UFS";

                // ========== NOR/NAND 存储 (功能机) ==========
                case 0x6500:  // SC6500
                case 0x6530:  // SC6530
                case 0x6531:  // SC6531E
                case 0x6533:  // SC6533G
                case 0x6600:  // SC6600
                case 0x6610:  // SC6610
                case 0x6620:  // SC6620
                case 0x6800:  // SC6800H
                case 0x0117:  // W117
                case 0x0217:  // W217
                case 0x0307:  // W307
                    return "NOR/NAND";

                // ========== SPI NOR (可穿戴设备) ==========
                case 0x6121:  // UWS6121
                case 0x6122:  // UWS6122
                case 0x6131:  // UWS6131
                case 0x6152:  // UWS6152
                    return "SPI NOR";

                // ========== eMMC 存储 (默认) ==========
                default:
                    return "eMMC";
            }
        }

        /// <summary>
        /// Determine if it is a feature phone platform
        /// </summary>
        public static bool IsFeaturePhonePlatform(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;
            switch (baseId)
            {
                case 0x6500:  // SC6500
                case 0x6530:  // SC6530
                case 0x6531:  // SC6531E
                case 0x6533:  // SC6533G
                case 0x6600:  // SC6600
                case 0x6610:  // SC6610
                case 0x6620:  // SC6620
                case 0x6800:  // SC6800H
                case 0x0117:  // W117
                case 0x0217:  // W217
                case 0x0307:  // W307
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determine if it is a wearable platform
        /// </summary>
        public static bool IsWearablePlatform(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;
            switch (baseId)
            {
                case 0x6121:  // UWS6121
                case 0x6122:  // UWS6122
                case 0x6131:  // UWS6131
                case 0x6152:  // UWS6152
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determine if it is an IoT platform
        /// </summary>
        public static bool IsIoTPlatform(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;
            switch (baseId)
            {
                case 0x7862:  // UIS7862
                case 0x7863:  // UIS7863
                case 0x7870:  // UIS7870
                case 0x7885:  // UIS7885
                case 0x8910:  // UIS8910DM
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determine if exploit is required to download FDL
        /// </summary>
        public static bool RequiresExploit(uint chipId)
        {
            uint baseId = chipId > 0xFFFF ? (chipId >> 16) : chipId;
            switch (baseId)
            {
                case 0x9850:  // SC9850K
                case 0x9853:  // SC9853i
                case 0x9860:  // SC9860G
                case 0x9861:  // SC9861
                case 0x9863:  // SC9863A
                case 0x8581:  // SC8581A
                case 0x0610:  // T610
                case 0x0612:  // T612
                case 0x0616:  // T616
                case 0x0618:  // T618
                case 0x0512:  // UMS512
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Spreadtrum USB VID/PID
    /// </summary>
    public static class SprdUsbIds
    {
        // Spreadtrum/Unisoc VID
        public const int VID_SPRD = 0x1782;
        public const int VID_UNISOC = 0x1782;

        // Download mode PID (Standard)
        public const int PID_DOWNLOAD = 0x4D00;      // Standard download mode
        public const int PID_DOWNLOAD_2 = 0x4D01;    // Download mode variant
        public const int PID_DOWNLOAD_3 = 0x4D02;    // Download mode variant 2
        public const int PID_DOWNLOAD_4 = 0x4D03;    // Download mode variant 3
        public const int PID_U2S_DIAG = 0x4D00;      // U2S Diag (SPRD U2S Diag)

        // Download mode PID (New platform)
        public const int PID_UMS_DOWNLOAD = 0x5000;  // UMS series download mode
        public const int PID_UWS_DOWNLOAD = 0x5001;  // UWS series download mode
        public const int PID_T606_DOWNLOAD = 0x5002; // T606 download mode

        // Diagnostic mode PID
        public const int PID_DIAG = 0x4D10;          // Standard diagnostic mode
        public const int PID_DIAG_2 = 0x4D11;        // Diagnostic mode variant
        public const int PID_DIAG_3 = 0x4D14;        // Diagnostic mode variant 2

        // ADB mode PID
        public const int PID_ADB = 0x4D12;           // ADB mode
        public const int PID_ADB_2 = 0x4D13;         // ADB mode variant

        // MTP mode PID
        public const int PID_MTP = 0x4D15;           // MTP mode
        public const int PID_MTP_2 = 0x4D16;         // MTP mode variant

        // CDC/ACM mode PID
        public const int PID_CDC = 0x4D20;           // CDC mode
        public const int PID_ACM = 0x4D21;           // ACM mode
        public const int PID_SERIAL = 0x4D22;        // Serial mode

        // Special PID
        public const int PID_RNDIS = 0x4D30;         // RNDIS network mode
        public const int PID_FASTBOOT = 0x4D40;      // Fastboot mode

        // ========== Other manufacturers using Spreadtrum chips ==========

        // Samsung
        public const int VID_SAMSUNG = 0x04E8;
        public const int PID_SAMSUNG_SPRD = 0x685D;  // Samsung Spreadtrum download
        public const int PID_SAMSUNG_SPRD_2 = 0x685C; // Samsung Spreadtrum download 2
        public const int PID_SAMSUNG_DIAG = 0x6860;  // Samsung diagnostic
        public const int PID_SAMSUNG_DIAG_2 = 0x6862; // Samsung diagnostic 2

        // Huawei
        public const int VID_HUAWEI = 0x12D1;
        public const int PID_HUAWEI_DOWNLOAD = 0x1001;
        public const int PID_HUAWEI_DOWNLOAD_2 = 0x1035;
        public const int PID_HUAWEI_DOWNLOAD_3 = 0x1C05;

        // ZTE
        public const int VID_ZTE = 0x19D2;
        public const int PID_ZTE_DOWNLOAD = 0x0016;
        public const int PID_ZTE_DOWNLOAD_2 = 0x0034;
        public const int PID_ZTE_DOWNLOAD_3 = 0x1403;
        public const int PID_ZTE_DIAG = 0x0117;
        public const int PID_ZTE_DIAG_2 = 0x0076;

        // Alcatel/TCL
        public const int VID_ALCATEL = 0x1BBB;
        public const int PID_ALCATEL_DOWNLOAD = 0x0536;
        public const int PID_ALCATEL_DOWNLOAD_2 = 0x0530;
        public const int PID_ALCATEL_DOWNLOAD_3 = 0x0510;

        // Lenovo
        public const int VID_LENOVO = 0x17EF;
        public const int PID_LENOVO_DOWNLOAD = 0x7890;

        // Realme/OPPO
        public const int VID_REALME = 0x22D9;
        public const int PID_REALME_DOWNLOAD = 0x2762;
        public const int PID_REALME_DOWNLOAD_2 = 0x2763;
        public const int PID_REALME_DOWNLOAD_3 = 0x2764;

        // Xiaomi (Partial use of Spreadtrum)
        public const int VID_XIAOMI = 0x2717;
        public const int PID_XIAOMI_DOWNLOAD = 0xFF48;

        // Nokia
        public const int VID_NOKIA = 0x0421;
        public const int PID_NOKIA_DOWNLOAD = 0x0600;
        public const int PID_NOKIA_DOWNLOAD_2 = 0x0601;
        public const int PID_NOKIA_DOWNLOAD_3 = 0x0602;

        // Infinix/Tecno/Itel (Transsion)
        public const int VID_TRANSSION = 0x2A47;
        public const int PID_TRANSSION_DOWNLOAD = 0x2012;
        public const int VID_TRANSSION_2 = 0x1782;

        /// <summary>
        /// Check if it is a Spreadtrum device VID
        /// </summary>
        public static bool IsSprdVid(int vid)
        {
            return vid == VID_SPRD ||
                   vid == VID_SAMSUNG ||
                   vid == VID_HUAWEI ||
                   vid == VID_ZTE ||
                   vid == VID_ALCATEL ||
                   vid == VID_LENOVO ||
                   vid == VID_REALME ||
                   vid == VID_XIAOMI ||
                   vid == VID_NOKIA;
        }

        /// <summary>
        /// Check if it is a download mode PID
        /// </summary>
        public static bool IsDownloadPid(int pid)
        {
            return pid == PID_DOWNLOAD ||
                   pid == PID_DOWNLOAD_2 ||
                   pid == PID_DOWNLOAD_3 ||
                   pid == PID_DOWNLOAD_4 ||
                   pid == PID_U2S_DIAG ||
                   pid == PID_UMS_DOWNLOAD ||
                   pid == PID_UWS_DOWNLOAD ||
                   pid == PID_T606_DOWNLOAD ||
                   pid == PID_CDC ||
                   pid == PID_ACM ||
                   pid == PID_SERIAL ||
                   pid == PID_SAMSUNG_SPRD ||
                   pid == PID_SAMSUNG_SPRD_2 ||
                   pid == PID_HUAWEI_DOWNLOAD ||
                   pid == PID_HUAWEI_DOWNLOAD_2 ||
                   pid == PID_HUAWEI_DOWNLOAD_3 ||
                   pid == PID_ZTE_DOWNLOAD ||
                   pid == PID_ZTE_DOWNLOAD_2 ||
                   pid == PID_ZTE_DOWNLOAD_3 ||
                   pid == PID_ALCATEL_DOWNLOAD ||
                   pid == PID_ALCATEL_DOWNLOAD_2 ||
                   pid == PID_ALCATEL_DOWNLOAD_3 ||
                   pid == PID_LENOVO_DOWNLOAD ||
                   pid == PID_REALME_DOWNLOAD ||
                   pid == PID_REALME_DOWNLOAD_2 ||
                   pid == PID_REALME_DOWNLOAD_3 ||
                   pid == PID_XIAOMI_DOWNLOAD ||
                   pid == PID_NOKIA_DOWNLOAD ||
                   pid == PID_NOKIA_DOWNLOAD_2 ||
                   pid == PID_NOKIA_DOWNLOAD_3 ||
                   pid == PID_TRANSSION_DOWNLOAD;
        }

        /// <summary>
        /// Check if it is a diagnostic mode PID
        /// </summary>
        public static bool IsDiagPid(int pid)
        {
            return pid == PID_DIAG ||
                   pid == PID_DIAG_2 ||
                   pid == PID_DIAG_3 ||
                   pid == PID_SAMSUNG_DIAG ||
                   pid == PID_SAMSUNG_DIAG_2 ||
                   pid == PID_ZTE_DIAG ||
                   pid == PID_ZTE_DIAG_2;
        }
    }
}
