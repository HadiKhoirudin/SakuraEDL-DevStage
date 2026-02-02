
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Text;

namespace LoveAlways.Fastboot.Protocol
{
    /// <summary>
    /// Fastboot Protocol Definition
    /// Based on Google AOSP platform/system/core/fastboot source analysis
    /// 
    /// Protocol Format:
    /// - Command: ASCII string, maximum 4096 bytes
    /// - Response: 4-byte prefix + optional data
    ///   - "OKAY" - Command successful
    ///   - "FAIL" - Command failed, followed by error message
    ///   - "DATA" - Ready to receive data, followed by 8-byte hex length
    ///   - "INFO" - Information message, followed by text
    /// </summary>
    public static class FastbootProtocol
    {
        // Protocol constants (according to Official Google README.md)
        public const int MAX_COMMAND_LENGTH = 4096;
        public const int MAX_RESPONSE_LENGTH = 256;
        public const int RESPONSE_PREFIX_LENGTH = 4;
        public const int DEFAULT_TIMEOUT_MS = 30000;
        public const int DATA_TIMEOUT_MS = 60000;
        public const string PROTOCOL_VERSION = "0.4";

        // USB protocol constants
        public const int USB_CLASS_FASTBOOT = 0xFF;
        public const int USB_SUBCLASS_FASTBOOT = 0x42;
        public const int USB_PROTOCOL_FASTBOOT = 0x03;

        #region Vendor USB Vendor IDs

        public const int USB_VID_GOOGLE = 0x18D1;       // Google / Pixel
        public const int USB_VID_XIAOMI = 0x2717;       // Xiaomi
        public const int USB_VID_OPPO = 0x22D9;         // OPPO
        public const int USB_VID_ONEPLUS = 0x2A70;      // OnePlus
        public const int USB_VID_QUALCOMM = 0x05C6;     // Qualcomm
        public const int USB_VID_SAMSUNG = 0x04E8;      // Samsung
        public const int USB_VID_HUAWEI = 0x12D1;       // Huawei
        public const int USB_VID_MOTOROLA = 0x22B8;     // Motorola
        public const int USB_VID_SONY = 0x0FCE;         // Sony
        public const int USB_VID_LG = 0x1004;           // LG
        public const int USB_VID_HTC = 0x0BB4;          // HTC
        public const int USB_VID_ASUS = 0x0B05;         // ASUS
        public const int USB_VID_LENOVO = 0x17EF;       // Lenovo
        public const int USB_VID_VIVO = 0x2D95;         // VIVO
        public const int USB_VID_MEIZU = 0x2A45;        // Meizu
        public const int USB_VID_ZTE = 0x19D2;          // ZTE / Nubia
        public const int USB_VID_REALME = 0x22D9;       // Realme (Same as OPPO)
        public const int USB_VID_NOTHING = 0x2970;      // Nothing Phone
        public const int USB_VID_FAIRPHONE = 0x2AE5;    // Fairphone
        public const int USB_VID_ESSENTIAL = 0x2E17;    // Essential
        public const int USB_VID_NVIDIA = 0x0955;       // NVIDIA Shield
        public const int USB_VID_MTK = 0x0E8D;          // MediaTek MTK

        // List of all supported Vendor IDs
        public static readonly int[] SUPPORTED_VENDOR_IDS = {
            USB_VID_GOOGLE, USB_VID_XIAOMI, USB_VID_OPPO, USB_VID_ONEPLUS,
            USB_VID_QUALCOMM, USB_VID_SAMSUNG, USB_VID_HUAWEI, USB_VID_MOTOROLA,
            USB_VID_SONY, USB_VID_LG, USB_VID_HTC, USB_VID_ASUS, USB_VID_LENOVO,
            USB_VID_VIVO, USB_VID_MEIZU, USB_VID_ZTE, USB_VID_NOTHING,
            USB_VID_FAIRPHONE, USB_VID_ESSENTIAL, USB_VID_NVIDIA, USB_VID_MTK
        };

        #endregion

        // Response prefixes
        public const string RESPONSE_OKAY = "OKAY";
        public const string RESPONSE_FAIL = "FAIL";
        public const string RESPONSE_DATA = "DATA";
        public const string RESPONSE_INFO = "INFO";
        public const string RESPONSE_TEXT = "TEXT";

        #region Official Google Standard Commands (Must Support)

        // Basic commands
        public const string CMD_GETVAR = "getvar";              // Query variable
        public const string CMD_DOWNLOAD = "download";          // Download data to device memory
        public const string CMD_UPLOAD = "upload";              // Upload data from device
        public const string CMD_FLASH = "flash";                // Flash partition
        public const string CMD_ERASE = "erase";                // Erase partition
        public const string CMD_BOOT = "boot";                  // Boot from memory
        public const string CMD_CONTINUE = "continue";          // Continue boot process

        // Reboot commands
        public const string CMD_REBOOT = "reboot";
        public const string CMD_REBOOT_BOOTLOADER = "reboot-bootloader";
        public const string CMD_REBOOT_FASTBOOT = "reboot-fastboot";     // Reboot to fastbootd
        public const string CMD_REBOOT_RECOVERY = "reboot-recovery";
        public const string CMD_REBOOT_EDL = "reboot-edl";               // Reboot to EDL mode
        public const string CMD_POWERDOWN = "powerdown";                 // Power off

        // A/B slot commands
        public const string CMD_SET_ACTIVE = "set_active";               // Set active slot

        // Unlock/lock commands
        public const string CMD_FLASHING_UNLOCK = "flashing unlock";
        public const string CMD_FLASHING_LOCK = "flashing lock";
        public const string CMD_FLASHING_UNLOCK_CRITICAL = "flashing unlock_critical";
        public const string CMD_FLASHING_LOCK_CRITICAL = "flashing lock_critical";
        public const string CMD_FLASHING_GET_UNLOCK_ABILITY = "flashing get_unlock_ability";

        // Dynamic partition commands (Android 10+)
        public const string CMD_UPDATE_SUPER = "update-super";
        public const string CMD_CREATE_LOGICAL_PARTITION = "create-logical-partition";
        public const string CMD_DELETE_LOGICAL_PARTITION = "delete-logical-partition";
        public const string CMD_RESIZE_LOGICAL_PARTITION = "resize-logical-partition";
        public const string CMD_WIPE_SUPER = "wipe-super";

        // GSI/Snapshot commands
        public const string CMD_GSI = "gsi";
        public const string CMD_GSI_WIPE = "gsi wipe";
        public const string CMD_GSI_DISABLE = "gsi disable";
        public const string CMD_GSI_STATUS = "gsi status";
        public const string CMD_SNAPSHOT_UPDATE = "snapshot-update";
        public const string CMD_SNAPSHOT_UPDATE_CANCEL = "snapshot-update cancel";
        public const string CMD_SNAPSHOT_UPDATE_MERGE = "snapshot-update merge";

        // Data retrieval commands
        public const string CMD_FETCH = "fetch";                // Fetch partition data from device

        // OEM common commands
        public const string CMD_OEM = "oem";

        #endregion

        #region Vendor Specific Commands (OEM Commands)

        // ========== Xiaomi/Redmi ==========
        public const string OEM_XIAOMI_DEVICE_INFO = "oem device-info";
        public const string OEM_XIAOMI_REBOOT_EDL = "oem edl";
        public const string OEM_XIAOMI_LOCK = "oem lock";
        public const string OEM_XIAOMI_UNLOCK = "oem unlock";
        public const string OEM_XIAOMI_LKSTATE = "oem lks";
        public const string OEM_XIAOMI_GET_TOKEN = "oem get_token";
        public const string OEM_XIAOMI_WRITE_PERSIST = "oem write_persist";
        public const string OEM_XIAOMI_BATTERY = "oem battery";
        public const string OEM_XIAOMI_REBOOT_FTMW = "oem ftmw";           // Factory mode
        public const string OEM_XIAOMI_CDMS = "oem cdms";

        // ========== OnePlus ==========
        public const string OEM_ONEPLUS_DEVICE_INFO = "oem device-info";
        public const string OEM_ONEPLUS_UNLOCK = "oem unlock";
        public const string OEM_ONEPLUS_LOCK = "oem lock";
        public const string OEM_ONEPLUS_ENABLE_DM_VERITY = "oem enable_dm_verity";
        public const string OEM_ONEPLUS_DISABLE_DM_VERITY = "oem disable_dm_verity";
        public const string OEM_ONEPLUS_SN = "oem sn";                     // Get serial number
        public const string OEM_ONEPLUS_4K = "oem 4k-video-supported";
        public const string OEM_ONEPLUS_REBOOT_FTMW = "oem ftmw";

        // ========== OPPO/Realme ==========
        public const string OEM_OPPO_DEVICE_INFO = "oem device-info";
        public const string OEM_OPPO_UNLOCK = "oem unlock";
        public const string OEM_OPPO_LOCK = "oem lock";
        public const string OEM_OPPO_GET_UNLOCK_CODE = "oem get-unlock-code";
        public const string OEM_OPPO_RW_FLAG = "oem rw_flag";
        public const string OEM_OPPO_DM_VERITY = "oem dm-verity";

        // ========== Samsung - Odin mode differs, partially supported ==========
        public const string OEM_SAMSUNG_UNLOCK = "oem unlock";
        public const string OEM_SAMSUNG_FRPRESET = "oem frpreset";         // FRP Reset

        // ========== Huawei ==========
        public const string OEM_HUAWEI_UNLOCK = "oem unlock";
        public const string OEM_HUAWEI_GET_IDENTIFIER = "oem get-identifier-token";
        public const string OEM_HUAWEI_CHECK_ROOTINFO = "oem check-rootinfo";

        // ========== Motorola ==========
        public const string OEM_MOTO_UNLOCK = "oem unlock";
        public const string OEM_MOTO_LOCK = "oem lock";
        public const string OEM_MOTO_GET_UNLOCK_DATA = "oem get_unlock_data";
        public const string OEM_MOTO_BP_TOOLS_ON = "oem bp_tools_on";
        public const string OEM_MOTO_CONFIG_CARRIER = "oem config carrier";

        // ========== Sony ==========
        public const string OEM_SONY_UNLOCK = "oem unlock";
        public const string OEM_SONY_GET_KEY = "oem key";
        public const string OEM_SONY_TA_BACKUP = "oem ta_backup";

        // Qualcomm Generic
        public const string OEM_QC_DEVICE_INFO = "oem device-info";
        public const string OEM_QC_ENABLE_CHARGER_SCREEN = "oem enable-charger-screen";
        public const string OEM_QC_DISABLE_CHARGER_SCREEN = "oem disable-charger-screen";
        public const string OEM_QC_OFF_MODE_CHARGE = "oem off-mode-charge";
        public const string OEM_QC_SELECT_DISPLAY_PANEL = "oem select-display-panel";

        // ========== MTK MediaTek Universal ==========
        public const string OEM_MTK_REBOOT_META = "oem reboot-meta";
        public const string OEM_MTK_LOG_ENABLE = "oem log_enable";
        public const string OEM_MTK_P2U = "oem p2u";

        // ========== Google Pixel ==========
        public const string OEM_PIXEL_UNLOCK = "flashing unlock";          // Pixel uses standard commands
        public const string OEM_PIXEL_LOCK = "flashing lock";
        public const string OEM_PIXEL_GET_UNLOCK_ABILITY = "flashing get_unlock_ability";
        public const string OEM_PIXEL_OFF_MODE_CHARGE = "oem off-mode-charge";

        #endregion

        #region Standard Variable Names (getvar)

        // Protocol/version info
        public const string VAR_VERSION = "version";                       // Protocol version (0.4)
        public const string VAR_VERSION_BOOTLOADER = "version-bootloader";
        public const string VAR_VERSION_BASEBAND = "version-baseband";
        public const string VAR_VERSION_OS = "version-os";
        public const string VAR_VERSION_VNDK = "version-vndk";

        // Device info
        public const string VAR_PRODUCT = "product";
        public const string VAR_SERIALNO = "serialno";
        public const string VAR_VARIANT = "variant";
        public const string VAR_HW_REVISION = "hw-revision";

        // Security status
        public const string VAR_SECURE = "secure";
        public const string VAR_UNLOCKED = "unlocked";
        public const string VAR_DEVICE_STATE = "device-state";             // locked/unlocked

        // Capacity limits
        public const string VAR_MAX_DOWNLOAD_SIZE = "max-download-size";
        public const string VAR_MAX_FETCH_SIZE = "max-fetch-size";

        // A/B slots
        public const string VAR_CURRENT_SLOT = "current-slot";
        public const string VAR_SLOT_COUNT = "slot-count";
        public const string VAR_HAS_SLOT = "has-slot";
        public const string VAR_SLOT_SUCCESSFUL = "slot-successful";
        public const string VAR_SLOT_UNBOOTABLE = "slot-unbootable";
        public const string VAR_SLOT_RETRY_COUNT = "slot-retry-count";

        // Partition info
        public const string VAR_PARTITION_SIZE = "partition-size";
        public const string VAR_PARTITION_TYPE = "partition-type";
        public const string VAR_IS_LOGICAL = "is-logical";

        // Fastbootd / Dynamic partitions
        public const string VAR_IS_USERSPACE = "is-userspace";
        public const string VAR_SUPER_PARTITION_NAME = "super-partition-name";
        public const string VAR_SNAPSHOT_UPDATE_STATUS = "snapshot-update-status";

        // Battery info
        public const string VAR_BATTERY_VOLTAGE = "battery-voltage";
        public const string VAR_BATTERY_SOC_OK = "battery-soc-ok";
        public const string VAR_CHARGER_SCREEN_ENABLED = "charger-screen-enabled";
        public const string VAR_OFF_MODE_CHARGE = "off-mode-charge";

        // Common
        public const string VAR_ALL = "all";                               // Get all variables

        #endregion

        #region Vendor Specific Variables

        // Xiaomi
        public const string VAR_XIAOMI_ANTI = "anti";
        public const string VAR_XIAOMI_TOKEN = "token";
        public const string VAR_XIAOMI_PRODUCT_TYPE = "product_type";

        // OnePlus
        public const string VAR_ONEPLUS_BUILD_TYPE = "build-type";
        public const string VAR_ONEPLUS_CARRIER = "carrier";

        // Huawei
        public const string VAR_HUAWEI_IDENTIFIER_TOKEN = "identifier-token";

        // Qualcomm
        public const string VAR_QC_SECURESTATE = "securestate";

        #endregion

        /// <summary>
        /// Build command bytes
        /// </summary>
        public static byte[] BuildCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                throw new ArgumentNullException(nameof(command));

            if (command.Length > MAX_COMMAND_LENGTH)
                throw new ArgumentException($"Command length exceeds {MAX_COMMAND_LENGTH} bytes");

            return Encoding.ASCII.GetBytes(command);
        }

        /// <summary>
        /// Build command with arguments
        /// </summary>
        public static byte[] BuildCommand(string command, string argument)
        {
            return BuildCommand($"{command}:{argument}");
        }

        /// <summary>
        /// Build download command (specify data size)
        /// </summary>
        public static byte[] BuildDownloadCommand(long size)
        {
            // Format: download:XXXXXXXX (8-digit hex)
            return BuildCommand($"{CMD_DOWNLOAD}:{size:x8}");
        }

        /// <summary>
        /// Parse response
        /// </summary>
        public static FastbootResponse ParseResponse(byte[] data, int length)
        {
            if (data == null || length < RESPONSE_PREFIX_LENGTH)
            {
                return new FastbootResponse
                {
                    Type = ResponseType.Unknown,
                    RawData = data,
                    Message = "Invalid response data"
                };
            }

            string response = Encoding.ASCII.GetString(data, 0, length);
            string prefix = response.Substring(0, RESPONSE_PREFIX_LENGTH);
            string payload = length > RESPONSE_PREFIX_LENGTH
                ? response.Substring(RESPONSE_PREFIX_LENGTH)
                : string.Empty;

            var result = new FastbootResponse
            {
                RawData = data,
                RawString = response,
                Message = payload
            };

            switch (prefix)
            {
                case RESPONSE_OKAY:
                    result.Type = ResponseType.Okay; // Command successful
                    break;

                case RESPONSE_FAIL:
                    result.Type = ResponseType.Fail; // Command failed
                    break;

                case RESPONSE_DATA:
                    result.Type = ResponseType.Data; // Ready to receive data
                    // Parse data length (8-digit hex)
                    if (payload.Length >= 8)
                    {
                        try
                        {
                            result.DataSize = Convert.ToInt64(payload.Substring(0, 8), 16);
                        }
                        catch { }
                    }
                    break;

                case RESPONSE_INFO:
                    result.Type = ResponseType.Info; // Info message
                    break;

                case RESPONSE_TEXT:
                    result.Type = ResponseType.Text; // Text message
                    break;

                default:
                    result.Type = ResponseType.Unknown;
                    result.Message = response;
                    break;
            }

            return result;
        }
    }

    /// <summary>
    /// Response Type
    /// </summary>
    public enum ResponseType
    {
        Unknown,
        Okay,       // Command successful
        Fail,       // Command failed
        Data,       // Ready to receive data
        Info,       // Info message
        Text        // Text message
    }

    /// <summary>
    /// Fastboot Response
    /// </summary>
    public class FastbootResponse
    {
        public ResponseType Type { get; set; }
        public string Message { get; set; }
        public string RawString { get; set; }
        public byte[] RawData { get; set; }
        public long DataSize { get; set; }

        public bool IsSuccess => Type == ResponseType.Okay;
        public bool IsFail => Type == ResponseType.Fail;
        public bool IsData => Type == ResponseType.Data;
        public bool IsInfo => Type == ResponseType.Info;

        public override string ToString()
        {
            return $"[{Type}] {Message}";
        }
    }
}
