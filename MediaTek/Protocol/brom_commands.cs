// ============================================================================
// LoveAlways - MediaTek BROM Command Definitions
// MediaTek Boot ROM (BROM) Protocol Commands
// ============================================================================
// Reference: mtkclient project mtk_preloader.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;

namespace LoveAlways.MediaTek.Protocol
{
    /// <summary>
    /// BROM Handshake Sequence
    /// </summary>
    public static class BromHandshake
    {
        /// <summary>Handshake send byte sequence (0xA0, 0x0A, 0x50, 0x05)</summary>
        public static readonly byte[] SendSequence = { 0xA0, 0x0A, 0x50, 0x05 };

        /// <summary>Handshake expected response sequence (0x5F, 0xF5, 0xAF, 0xFA)</summary>
        public static readonly byte[] ExpectedResponse = { 0x5F, 0xF5, 0xAF, 0xFA };

        /// <summary>Single byte handshake send</summary>
        public const byte HANDSHAKE_SEND = 0xA0;

        /// <summary>Single byte handshake response</summary>
        public const byte HANDSHAKE_RESPONSE = 0x5F;
    }

    /// <summary>
    /// BROM Command Byte Definitions (Only including actually used commands)
    /// Reference: mtkclient/Library/mtk_preloader.py Cmd enum
    /// </summary>
    public static class BromCommands
    {
        // ========================================
        // Memory Read/Write Commands
        // ========================================

        /// <summary>Read 32-bit (0xD1)</summary>
        public const byte CMD_READ32 = 0xD1;

        /// <summary>Write 16-bit (0xD2)</summary>
        public const byte CMD_WRITE16 = 0xD2;

        /// <summary>Write 32-bit (0xD4)</summary>
        public const byte CMD_WRITE32 = 0xD4;

        // ========================================
        // DA Operation Commands
        // ========================================

        /// <summary>Jump to DA (0xD5)</summary>
        public const byte CMD_JUMP_DA = 0xD5;

        /// <summary>Send DA (0xD7)</summary>
        public const byte CMD_SEND_DA = 0xD7;

        /// <summary>Get target config (0xD8)</summary>
        public const byte CMD_GET_TARGET_CONFIG = 0xD8;

        /// <summary>Send environment prepare (0xD9)</summary>
        public const byte CMD_SEND_ENV_PREPARE = 0xD9;

        // ========================================
        // Security/Authentication Commands
        // ========================================

        /// <summary>Send certificate / Exploit Payload (0xE0)</summary>
        public const byte CMD_SEND_CERT = 0xE0;

        /// <summary>Get ME ID (0xE1)</summary>
        public const byte CMD_GET_ME_ID = 0xE1;

        /// <summary>SLA Auth (0xE3)</summary>
        public const byte CMD_SLA = 0xE3;

        /// <summary>Get SOC ID (0xE7)</summary>
        public const byte CMD_GET_SOC_ID = 0xE7;

        // ========================================
        // Version Info Commands
        // ========================================

        /// <summary>Get hardware/software version (0xFC)</summary>
        public const byte CMD_GET_HW_SW_VER = 0xFC;

        /// <summary>Get hardware code (0xFD)</summary>
        public const byte CMD_GET_HW_CODE = 0xFD;

        /// <summary>Get BL version (0xFE)</summary>
        public const byte CMD_GET_BL_VER = 0xFE;

        /// <summary>Get version (0xFF)</summary>
        public const byte CMD_GET_VERSION = 0xFF;
    }

    /// <summary>
    /// BROM Response Codes
    /// </summary>
    public static class BromResponse
    {
        /// <summary>Acknowledgment (0x5A)</summary>
        public const byte ACK = 0x5A;

        /// <summary>Negative Acknowledgment (0xA5)</summary>
        public const byte NACK = 0xA5;
    }

    /// <summary>
    /// BROM Status Codes
    /// </summary>
    public enum BromStatus : ushort
    {
        /// <summary>Success</summary>
        Success = 0x0000,

        /// <summary>Auth/SLA Authentication Required (0x0010) - Common in Preloader mode</summary>
        AuthRequired = 0x0010,

        /// <summary>Preloader mode requires Auth (0x0011)</summary>
        PreloaderAuth = 0x0011,

        /// <summary>SLA Authentication Required</summary>
        SlaRequired = 0x1D0D,

        /// <summary>SLA Not Needed</summary>
        SlaNotNeeded = 0x1D0C,

        /// <summary>DAA Security Error</summary>
        DaaSecurityError = 0x7017,

        /// <summary>DAA Signature Verification Error</summary>
        DaaSignatureError = 0x7015,

        /// <summary>Unsupported Command</summary>
        UnsupportedCmd = 0x0001,

        /// <summary>General Error</summary>
        Error = 0x0002,
    }

    /// <summary>
    /// Target Configuration Flags
    /// </summary>
    [Flags]
    public enum TargetConfigFlags : uint
    {
        /// <summary>No flags</summary>
        None = 0x00,

        /// <summary>Secure Boot Enabled</summary>
        SbcEnabled = 0x01,

        /// <summary>SLA Enabled</summary>
        SlaEnabled = 0x02,

        /// <summary>DAA Enabled</summary>
        DaaEnabled = 0x04,
    }

    /// <summary>
    /// DA Mode
    /// </summary>
    public enum DaMode : int
    {
        /// <summary>Legacy DA Mode</summary>
        Legacy = 3,

        /// <summary>XFlash DA Mode</summary>
        XFlash = 5,

        /// <summary>XML DA Mode (V6)</summary>
        Xml = 6,
    }

    /// <summary>
    /// MTK Device State
    /// </summary>
    public enum MtkDeviceState
    {
        /// <summary>Disconnected</summary>
        Disconnected,

        /// <summary>In Handshake</summary>
        Handshaking,

        /// <summary>Connected (BROM Mode)</summary>
        Brom,

        /// <summary>Preloader Mode</summary>
        Preloader,

        /// <summary>DA1 Loaded</summary>
        Da1Loaded,

        /// <summary>DA2 Loaded</summary>
        Da2Loaded,

        /// <summary>Error State</summary>
        Error,
    }

    /// <summary>
    /// MTK USB VID/PID
    /// </summary>
    public static class MtkUsbIds
    {
        public const int VID_MTK = 0x0E8D;
        public const int PID_PRELOADER = 0x0003;
        public const int PID_BOOTROM = 0x2000;

        /// <summary>Check if device is an MTK Download Mode device</summary>
        public static bool IsDownloadMode(int vid, int pid)
        {
            return vid == VID_MTK && (pid == PID_PRELOADER || pid == PID_BOOTROM);
        }
    }

    /// <summary>
    /// BROM Error Code Helper
    /// </summary>
    public static class BromErrorHelper
    {
        /// <summary>Get error message</summary>
        public static string GetErrorMessage(ushort status)
        {
            return status switch
            {
                0x0000 => "Success",
                0x0001 => "Invalid Command",
                0x0002 => "Checksum Error",
                0x1D0C => "SLA Authentication not needed",
                0x1D0D => "SLA Authentication required",
                0x7015 => "DAA Signature verification failed",
                0x7017 => "DAA Security Error (DAA protection enabled on device)",
                _ => $"Unknown Error (0x{status:X4})"
            };
        }

        /// <summary>Check if status is success</summary>
        public static bool IsSuccess(ushort status)
        {
            // Explicit success status
            if (status == 0x0000 || status == 0x1D0C)
                return true;

            // DAA status - Return success to let upper layer handle reconnection
            if (status == 0x7017 || status == 0x7015)
                return true;

            // Other values less than 0x100 are considered successful
            if (status < 0x100 && status > 0x000E)
                return true;

            return false;
        }

        /// <summary>Check if SLA authentication is required</summary>
        public static bool NeedsSla(ushort status)
        {
            return status == (ushort)BromStatus.SlaRequired;
        }
    }
}
