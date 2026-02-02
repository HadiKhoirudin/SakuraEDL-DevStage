// ============================================================================
// LoveAlways - MediaTek Error Code Parser and Formatter
// MediaTek Error Code Parser and Formatter
// ============================================================================
// Reference: Penumbra project https://shomy.is-a.dev/penumbra/
// Structured parsing of XFlash (V5) and XML (V6) protocol error codes
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System.Collections.Generic;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// Error Severity Level
    /// </summary>
    public enum ErrorSeverity
    {
        Success = 0x00,      // 0x00000000
        Info = 0x40,         // 0x40000000
        Warning = 0x80,      // 0x80000000
        Error = 0xC0         // 0xC0000000
    }

    /// <summary>
    /// Error Domain (Component)
    /// </summary>
    public enum ErrorDomain
    {
        Common = 1,          // General error
        Security = 2,        // Security related
        Library = 3,         // Library/function error
        Device = 4,          // Device/hardware error
        Host = 5,            // Host-side error
        Brom = 6,            // BROM error
        Da = 7,              // DA error
        Preloader = 8        // Preloader error
    }

    /// <summary>
    /// MTK Error Code Parser and Formatter
    /// 
    /// Error code structure (32-bit):
    /// - Bits 31-30: Severity (Success=00, Info=01, Warning=10, Error=11)
    /// - Bits 29-24: Reserved
    /// - Bits 23-16: Error Domain (1-8)
    /// - Bits 15-0:  Error Code
    /// 
    /// Example: 0xC0070004
    ///   0xC0000000 (Error) | 0x00070000 (DA Domain) | 0x0004 (Code 4)
    ///   => Error | DA | DA_HASH_MISMATCH
    /// </summary>
    public static class MtkErrorCodes
    {
        #region Error Code Masks

        private const uint SEVERITY_MASK = 0xC0000000;
        private const uint DOMAIN_MASK = 0x00FF0000;
        private const uint CODE_MASK = 0x0000FFFF;

        public const uint SEVERITY_SUCCESS = 0x00000000;
        public const uint SEVERITY_INFO = 0x40000000;
        public const uint SEVERITY_WARNING = 0x80000000;
        public const uint SEVERITY_ERROR = 0xC0000000;

        private const int DOMAIN_SHIFT = 16;

        #endregion

        #region Error Code Definitions

        /// <summary>
        /// Common error codes and their descriptions
        /// Source: mtkclient project Library/error.py (ErrorCodes_XFlash, verified real data)
        /// </summary>
        public static readonly Dictionary<uint, string> CommonErrors = new Dictionary<uint, string>
        {
            // Success
            { 0x00000000, "OK - Success" },
            
            // XFlash Common error (0xC001xxxx)
            { 0xC0010001, "Error - Error" },
            { 0xC0010002, "Abort - Abort" },
            { 0xC0010003, "Unsupported command - Unsupported command" },
            { 0xC0010004, "Unsupported ctrl code - Unsupported control code" },
            { 0xC0010005, "Protocol error - Protocol error" },
            { 0xC0010006, "Protocol buffer overflow - Protocol buffer overflow" },
            { 0xC0010007, "Insufficient buffer - Insufficient buffer" },
            { 0xC0010008, "USB SCAN error - USB scan error" },
            { 0xC0010009, "Invalid hsession - Invalid session handle" },
            { 0xC001000A, "Invalid session - Invalid session" },
            { 0xC001000B, "Invalid stage - Invalid stage" },
            { 0xC001000C, "Not implemented - Not implemented" },
            { 0xC001000D, "File not found - File not found" },
            { 0xC001000E, "Open file error - Open file error" },
            { 0xC001000F, "Write file error - Write file error" },
            { 0xC0010010, "Read file error - Read file error" },
            { 0xC0010011, "Create File error / Unsupported Version - Create file error / Unsupported Version" },
            
            // Security error (0xC002xxxx)
            { 0xC0020001, "Rom info not found - ROM info not found" },
            { 0xC0020002, "Cust name not found - Customer name not found" },
            { 0xC0020003, "Device not supported - Device not supported" },
            { 0xC0020004, "DL forbidden - Download forbidden" },
            { 0xC0020005, "Img too large - Image too large" },
            { 0xC0020006, "PL verify fail - Preloader verification failed" },
            { 0xC0020007, "Image verify fail - Image verification failed" },
            { 0xC0020008, "Hash operation fail - Hash operation failed" },
            { 0xC0020009, "Hash binding check fail - Hash binding check failed" },
            { 0xC002000A, "Invalid buf - Invalid buffer" },
            { 0xC002000B, "Binding hash not available - Binding hash not available" },
            { 0xC002000C, "Write data not allowed - Write data not allowed" },
            { 0xC002000D, "Format not allowed - Format not allowed" },
            { 0xC002000E, "SV5 public key auth failed - SV5 public key auth failed" },
            { 0xC002000F, "SV5 hash verify failed - SV5 hash verify failed" },
            { 0xC0020010, "SV5 RSA OP failed - SV5 RSA operation failed" },
            { 0xC0020011, "SV5 RSA verify failed - SV5 RSA verification failed" },
            { 0xC0020012, "SV5 GFH not found - SV5 GFH not found" },
            { 0xC0020013, "Cert1 invalid - Certificate 1 invalid" },
            { 0xC0020014, "Cert2 invalid - Certificate 2 invalid" },
            { 0xC0020015, "Imghdr invalid - Image header invalid" },
            { 0xC0020016, "Sig size invalid - Signature size invalid" },
            { 0xC0020017, "RSA pss op fail - RSA-PSS operation failed" },
            { 0xC0020018, "Cert auth failed - Certificate authentication failed" },
            { 0xC002002D, "Anti rollback violation - Anti rollback violation" },
            { 0xC002002E, "SECCFG not found - SECCFG not found" },
            { 0xC002002F, "SECCFG magic incorrect - SECCFG magic incorrect" },
            { 0xC0020030, "SECCFG invalid - SECCFG invalid" },
            { 0xC0020049, "Remote Security policy disabled - Remote security policy disabled" },
            { 0xC002004C, "DA Anti-Rollback error - DA Anti-Rollback error" },
            { 0xC0020053, "DA version Anti-Rollback error - DA version Anti-Rollback error" },
            { 0xC002005C, "Lockstate seccfg fail - Lock state SECCFG failed" },
            { 0xC002005D, "Lockstate custom fail - Lock state custom failed" },
            { 0xC002005E, "Lockstate inconsistent - Lock state inconsistent" },
            
            // Library error (0xC003xxxx)
            { 0xC0030001, "Scatter file invalid - Scatter file invalid" },
            { 0xC0030002, "DA file invalid - DA file invalid" },
            { 0xC0030003, "DA selection error - DA selection error" },
            { 0xC0030004, "Preloader invalid - Preloader invalid" },
            { 0xC0030005, "EMI hdr invalid - EMI header invalid" },
            { 0xC0030006, "Storage mismatch - Storage mismatch" },
            { 0xC0030007, "Invalid parameters - Invalid parameters" },
            { 0xC0030008, "Invalid GPT - GPT invalid" },
            { 0xC0030009, "Invalid PMT - PMT invalid" },
            { 0xC003000A, "Layout changed - Layout changed" },
            { 0xC003000B, "Invalid format param - Invalid format parameter" },
            { 0xC003000C, "Unknown storage section type - Unknown storage section type" },
            { 0xC003000D, "Unknown scatter field - Unknown scatter field" },
            { 0xC003000E, "Partition tbl doesn't exist - Partition table does not exist" },
            { 0xC003000F, "Scatter hw chip id mismatch - Scatter hardware chip ID mismatch" },
            { 0xC0030010, "SEC cert file not found - Security certificate file not found" },
            { 0xC0030011, "SEC auth file not found - Security authentication file not found" },
            { 0xC0030012, "SEC auth file needed - Security authentication file needed" },
            
            // Device error (0xC004xxxx)
            { 0xC0040001, "Unsupported operation - Unsupported operation" },
            { 0xC0040002, "Thread error - Thread error" },
            { 0xC0040003, "Checksum error - Checksum error" },
            { 0xC0040004, "Unknown sparse - Unknown sparse image" },
            { 0xC0040005, "Unknown sparse chunk type - Unknown sparse chunk type" },
            { 0xC0040006, "Partition not found - Partition not found" },
            { 0xC0040007, "Read parttbl failed - Read partition table failed" },
            { 0xC0040008, "Exceeded max partition number - Exceeded maximum partition number" },
            { 0xC0040009, "Unknown storage type - Unknown storage type" },
            { 0xC004000A, "Dram Test failed - DRAM test failed" },
            { 0xC004000B, "Exceed available range - Exceeded available range" },
            { 0xC004000C, "Write sparse image failed - Write sparse image failed" },
            { 0xC0040030, "MMC error - MMC error" },
            { 0xC0040040, "Nand error - Nand error" },
            { 0xC0040041, "Nand in progress - Nand operation in progress" },
            { 0xC0040042, "Nand timeout - Nand timeout" },
            { 0xC0040043, "Nand bad block - Nand bad block" },
            { 0xC0040044, "Nand erase failed - Nand erase failed" },
            { 0xC0040045, "Nand page program failed - Nand page program failed" },
            { 0xC0040050, "EMI setting version error - EMI setting version error" },
            { 0xC0040060, "UFS error - UFS error" },
            { 0xC0040100, "DA OTP not supported - DA OTP not supported" },
            { 0xC0040102, "DA OTP lock failed - DA OTP lock failed" },
            { 0xC0040200, "EFUSE unknown error - EFUSE unknown error" },
            { 0xC0040201, "EFUSE write timeout without verify - EFUSE write timeout (unverified)" },
            { 0xC0040202, "EFUSE blown - EFUSE already blown" },
            { 0xC0040203, "EFUSE revert bit - EFUSE revert bit" },
            { 0xC0040204, "EFUSE blown partly - EFUSE partly blown" },
            { 0xC0040206, "EFUSE value is not zero - EFUSE value is non-zero" },
            { 0xC0040209, "EFUSE blow error - EFUSE blow error" },
            
            // Host error (0xC005xxxx)
            { 0xC0050001, "Device ctrl exception - Device control exception" },
            { 0xC0050002, "Shutdown Cmd exception - Shutdown command exception" },
            { 0xC0050003, "Download exception - Download exception" },
            { 0xC0050004, "Upload exception - Upload exception" },
            { 0xC0050005, "Ext Ram exception - External RAM exception" },
            { 0xC0050008, "Write data exception - Write data exception" },
            { 0xC0050009, "Format exception - Format exception" },
            
            // BROM error (0xC006xxxx)
            { 0xC0060001, "Brom start cmd/connect not preloader failed - BROM start command failed" },
            { 0xC0060002, "Brom get bbchip hw ver failed - BROM get chip version failed" },
            { 0xC0060003, "Brom cmd send da failed - BROM send DA failed" },
            { 0xC0060004, "Brom cmd jump da failed - BROM jump DA failed" },
            { 0xC0060005, "Brom cmd failed - BROM command failed" },
            { 0xC0060006, "Brom stage callback failed - BROM stage callback failed" },
            
            // DA error (0xC007xxxx)
            { 0xC0070001, "DA Version mismatch - DA version mismatch" },
            { 0xC0070002, "DA not found - DA not found" },
            { 0xC0070003, "DA section not found - DA section not found" },
            { 0xC0070004, "DA hash mismatch - DA hash mismatch (Carbonara expected)" },
            { 0xC0070005, "DA exceed max num - DA exceeds maximum count" },

            // Progress Reports (Info level 0x4004xxxx)
            { 0x40040004, "PROGRESS_REPORT - Operation in progress" },
            { 0x40040005, "PROGRESS_DONE - Operation completed" },
            
            // Special error codes
            { 0x00005A5B, "DA_IN_BLACKLIST - DA in blacklist" },
        };

        #endregion

        #region Parsing and Formatting

        /// <summary>
        /// Parse error code
        /// </summary>
        public static (ErrorSeverity severity, ErrorDomain domain, ushort code) ParseErrorCode(uint errorCode)
        {
            var severityBits = (errorCode & SEVERITY_MASK) >> 30;
            var severity = severityBits switch
            {
                0 => ErrorSeverity.Success,
                1 => ErrorSeverity.Info,
                2 => ErrorSeverity.Warning,
                3 => ErrorSeverity.Error,
                _ => ErrorSeverity.Error
            };

            var domainCode = (errorCode & DOMAIN_MASK) >> DOMAIN_SHIFT;
            var domain = (ErrorDomain)(domainCode);

            var code = (ushort)(errorCode & CODE_MASK);

            return (severity, domain, code);
        }

        /// <summary>
        /// Format error code as readable string
        /// </summary>
        public static string FormatError(uint errorCode)
        {
            // Check if it is a known error
            if (CommonErrors.TryGetValue(errorCode, out var knownError))
            {
                return $"0x{errorCode:X8}: {knownError}";
            }

            // Parse unknown error
            var (severity, domain, code) = ParseErrorCode(errorCode);

            var severityStr = severity switch
            {
                ErrorSeverity.Success => "Success",
                ErrorSeverity.Info => "Info",
                ErrorSeverity.Warning => "Warning",
                ErrorSeverity.Error => "Error",
                _ => "Unknown"
            };

            var domainStr = domain switch
            {
                ErrorDomain.Common => "Common",
                ErrorDomain.Security => "Security",
                ErrorDomain.Library => "Library",
                ErrorDomain.Device => "Device",
                ErrorDomain.Host => "Host",
                ErrorDomain.Brom => "BROM",
                ErrorDomain.Da => "DA",
                ErrorDomain.Preloader => "Preloader",
                _ => $"Domain({(int)domain})"
            };

            return $"0x{errorCode:X8}: {severityStr} | {domainStr} | Code 0x{code:X4}";
        }

        /// <summary>
        /// Check if it is an error (Error level)
        /// </summary>
        public static bool IsError(uint errorCode)
        {
            return (errorCode & SEVERITY_MASK) == SEVERITY_ERROR;
        }

        /// <summary>
        /// Check if it is success
        /// </summary>
        public static bool IsSuccess(uint errorCode)
        {
            return errorCode == 0x00000000;
        }

        /// <summary>
        /// Check if it is a progress report
        /// </summary>
        public static bool IsProgressReport(uint errorCode)
        {
            return errorCode == 0x40040004 || errorCode == 0x40040005;
        }

        /// <summary>
        /// Check if it is a DA hash mismatch error
        /// </summary>
        public static bool IsDaHashMismatch(uint errorCode)
        {
            return errorCode == 0xC0070004;
        }

        /// <summary>
        /// Construct error code
        /// </summary>
        public static uint MakeErrorCode(ErrorSeverity severity, ErrorDomain domain, ushort code)
        {
            uint severityBits = severity switch
            {
                ErrorSeverity.Success => 0,
                ErrorSeverity.Info => 1,
                ErrorSeverity.Warning => 2,
                ErrorSeverity.Error => 3,
                _ => 3
            };

            return (severityBits << 30) | ((uint)domain << DOMAIN_SHIFT) | code;
        }

        #endregion

        #region Detailed Descriptions

        /// <summary>
        /// Get detailed description of error (including suggestions)
        /// </summary>
        public static string GetDetailedDescription(uint errorCode)
        {
            return errorCode switch
            {
                0xC0070004 => @"DA_HASH_MISMATCH (0xC0070004)
    Reason: DA signature/hash verification failed
    Possibilities:
    1. DA file modified but not signed
    2. Device has DAA (Download Agent Authorization) enabled
    3. Wrong DA file version used
    4. Carbonara exploit first attempt (expected behavior)
    Suggestions:
    - Confirm if device supports unsigned DA
    - Check if Kamakiri/Carbonara exploit is needed
    - Verify DA file integrity",

                0xC0020003 => @"SECURITY_SLA_REQUIRED (0xC0020003)
    Reason: Device requires SLA (Secure Level Authentication)
    Possibilities:
    1. Preloader or BROM requires RSA signature authentication
    2. Device has Secure Boot enabled
    Suggestions:
    - Provide correct SLA key for authentication
    - Check for available authentication certificates",

                0xC0020004 => @"SECURITY_DAA_REQUIRED (0xC0020004)
    Reason: Device requires DAA (Download Agent Authorization)
    Possibilities:
    1. DA1 requires signature verification to load
    2. Device Secure Boot enabled
    Suggestions:
    - Use Kamakiri exploit to temporarily disable DAA
    - Use manufacturer signed DA file",

                0xC0060003 => @"BROM_HANDSHAKE_FAIL (0xC0060003)
    Reason: BROM handshake failed
    Possibilities:
    1. Device not in BROM mode
    2. USB connection unstable
    3. Driver issues
    Suggestions:
    - Confirm device is in BROM mode (short TP after power off)
    - Check USB connection and drivers
    - Try changing USB port",

                _ => FormatError(errorCode)
            };
        }

        #endregion
    }
}
