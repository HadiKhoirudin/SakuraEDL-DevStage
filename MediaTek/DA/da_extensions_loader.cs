// ============================================================================
// LoveAlways - MediaTek Download Agent Extensions Loader
// MediaTek Download Agent Extensions Loader
// ============================================================================
// Load and upload DA Extensions to device from mtk-payloads project
// Reference: https://github.com/shomykohai/mtk-payloads
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.IO;
using System.Threading.Tasks;
using LoveAlways.MediaTek.Common;
using LoveAlways.MediaTek.Models;
using LoveAlways.MediaTek.Protocol;

namespace LoveAlways.MediaTek.DA
{
    /// <summary>
    /// DA Extensions Loader
    /// Load compiled Extensions binaries from mtk-payloads project
    /// </summary>
    public class DaExtensionsLoader
    {
        private readonly string _payloadBasePath;
        private readonly MtkLogger _log;

        #region Default Path Configuration

        /// <summary>XFlash (V5) Extensions filename</summary>
        public const string V5_EXTENSION_FILE = "da_x_ext.bin";
        
        /// <summary>XML (V6) Extensions filename</summary>
        public const string V6_EXTENSION_FILE = "da_xml_ext.bin";
        
        /// <summary>Default Payload path</summary>
        public const string DEFAULT_PAYLOAD_PATH = "Payloads";

        #endregion

        #region Constructor

        public DaExtensionsLoader(string payloadBasePath = null, MtkLogger logger = null)
        {
            _payloadBasePath = payloadBasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DEFAULT_PAYLOAD_PATH);
            _log = logger ?? MtkLog.Instance;
        }

        #endregion

        #region Load Extensions Binary

        /// <summary>
        /// Load corresponding Extensions binary based on device info
        /// </summary>
        public byte[] LoadExtension(ushort hwCode, MtkDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
                throw new ArgumentNullException(nameof(deviceInfo));

            var isV6 = deviceInfo.DaMode == 6;
            return LoadExtension(hwCode, isV6);
        }

        /// <summary>
        /// Load Extensions binary based on DA mode
        /// </summary>
        public byte[] LoadExtension(ushort hwCode, bool isV6)
        {
            _log.Info($"Loading DA Extensions (HW Code: 0x{hwCode:X4}, Mode: {(isV6 ? "V6/XML" : "V5/XFlash")})", LogCategory.Da);

            // Determine filename
            var fileName = isV6 ? V6_EXTENSION_FILE : V5_EXTENSION_FILE;
            var folderName = isV6 ? "da_xml" : "da_x";
            
            // Try multiple possible paths
            var possiblePaths = new[]
            {
                Path.Combine(_payloadBasePath, folderName, fileName),  // Payloads/da_x/da_x_ext.bin
                Path.Combine(_payloadBasePath, fileName),               // Payloads/da_x_ext.bin
                Path.Combine(".", folderName, fileName),                // ./da_x/da_x_ext.bin
                Path.Combine(".", fileName)                             // ./da_x_ext.bin
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var data = File.ReadAllBytes(path);
                        if (ValidateExtensionBinary(data))
                        {
                            _log.Success($"Loaded successfully: {path} ({data.Length} bytes)", LogCategory.Da);
                            return data;
                        }
                        else
                        {
                            _log.Warning($"Binary validation failed: {path}", LogCategory.Da);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Failed to read file: {path}", LogCategory.Da, ex);
                    }
                }
            }

            // No valid Extensions file found
            var searchedPaths = string.Join("\n  ", possiblePaths);
            var errorMsg = $"DA Extensions binary file not found\nSearch paths:\n  {searchedPaths}";
            _log.Error(errorMsg, LogCategory.Da);
            
            throw new FileNotFoundException(errorMsg);
        }

        /// <summary>
        /// Verify if Extensions binary is valid
        /// </summary>
        private bool ValidateExtensionBinary(byte[] binary)
        {
            if (binary == null || binary.Length < 0x100)
                return false;

            // TODO: Add more detailed validation
            // - Check ELF magic value
            // - Verify code segment
            // - Check entry point
            
            return true;
        }

        #endregion

        #region Load to Device

        /// <summary>
        /// Load Extensions to device
        /// </summary>
        public async Task<bool> LoadToDeviceAsync(
            IBromClient bromClient,
            ushort hwCode,
            MtkDeviceInfo deviceInfo,
            DaExtensionsConfig config = null)
        {
            if (bromClient == null)
                throw new ArgumentNullException(nameof(bromClient));

            _log.LogHeader("DA Extensions Loading Process");

            try
            {
                // 1. Check compatibility
                _log.Info("Checking device compatibility...", LogCategory.Da);
                if (!DaExtensionsCompatibility.SupportsExtensions(deviceInfo))
                {
                    _log.Error("Device does not support DA Extensions", LogCategory.Da);
                    return false;
                }
                _log.Success("Device compatibility check passed", LogCategory.Da);

                // 2. Load binary
                _log.Info("Loading Extensions binary...", LogCategory.Da);
                var binary = LoadExtension(hwCode, deviceInfo);

                // 3. Prepare config
                if (config == null)
                {
                    config = DaExtensionsHelper.GetRecommendedConfig(hwCode, deviceInfo);
                }
                config.ExtensionsBinary = binary;

                var loadAddr = config.GetLoadAddress();
                _log.Info($"Load address: 0x{loadAddr:X8} ({(config.UseLowMemoryAddress ? "Low Memory" : "Standard")})", LogCategory.Da);
                _log.Info($"Binary size: {binary.Length} bytes ({binary.Length / 1024.0:F2} KB)", LogCategory.Da);

                // 4. Upload to device
                _log.Info("Uploading Extensions to device...", LogCategory.Da);
                
                // TODO: Actual upload logic needs to be completed according to BromClient implementation
                // Example interface provided here
                /*
                await bromClient.SendBootTo(loadAddr, binary);
                */
                
                _log.Warning("Extensions upload function pending implementation (requires boot_to command support)", LogCategory.Da);
                _log.Info("Tip: Need to patch DA1 using Carbonara exploit first, then load Extensions via boot_to", LogCategory.Exploit);

                _log.LogSeparator();
                _log.Success("Extensions configuration preparation complete", LogCategory.Da);
                
                return true;
            }
            catch (Exception ex)
            {
                _log.Critical("Extensions loading failed", LogCategory.Da, ex);
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Check if Payload file exists
        /// </summary>
        public bool CheckPayloadExists(bool isV6)
        {
            var fileName = isV6 ? V6_EXTENSION_FILE : V5_EXTENSION_FILE;
            var folderName = isV6 ? "da_xml" : "da_x";
            
            var path1 = Path.Combine(_payloadBasePath, folderName, fileName);
            var path2 = Path.Combine(_payloadBasePath, fileName);
            
            return File.Exists(path1) || File.Exists(path2);
        }

        /// <summary>
        /// Get Payload information
        /// </summary>
        public void PrintPayloadInfo()
        {
            _log.LogHeader("DA Extensions Payload Info");
            
            _log.Info($"Payload base path: {_payloadBasePath}", LogCategory.Da);
            
            // V5 Extensions
            var v5Exists = CheckPayloadExists(false);
            _log.LogDeviceInfo("V5/XFlash Extensions", v5Exists ? "✓ Installed" : "✗ Not Found", LogCategory.Da);
            
            // V6 Extensions
            var v6Exists = CheckPayloadExists(true);
            _log.LogDeviceInfo("V6/XML Extensions", v6Exists ? "✓ Installed" : "✗ Not Found", LogCategory.Da);
            
            if (!v5Exists && !v6Exists)
            {
                _log.LogSeparator('-', 60);
                _log.Warning("No Extensions Payloads found", LogCategory.Da);
                _log.Info("Please obtain them from:", LogCategory.Da);
                _log.Info("  https://github.com/shomykohai/mtk-payloads", LogCategory.Da);
                _log.Info("", LogCategory.Da);
                _log.Info("Installation method:", LogCategory.Da);
                _log.Info($"  1. Clone repository: git clone https://github.com/shomykohai/mtk-payloads", LogCategory.Da);
                _log.Info($"  2. Build: cd mtk-payloads && ./build_all.sh", LogCategory.Da);
                _log.Info($"  3. Copy to: {_payloadBasePath}", LogCategory.Da);
            }
            
            _log.LogSeparator();
        }

        /// <summary>
        /// Create default Extensions configuration
        /// </summary>
        public DaExtensionsConfig CreateDefaultConfig(ushort hwCode, MtkDeviceInfo deviceInfo)
        {
            var config = DaExtensionsHelper.GetRecommendedConfig(hwCode, deviceInfo);
            
            // Automatically load binary
            try
            {
                config.ExtensionsBinary = LoadExtension(hwCode, deviceInfo);
            }
            catch (Exception ex)
            {
                _log.Warning($"Unable to load Extensions binary: {ex.Message}", LogCategory.Da);
            }
            
            return config;
        }

        #endregion
    }

    /// <summary>
    /// BROM client interface (for Extensions loading)
    /// </summary>
    public interface IBromClient
    {
        /// <summary>Send boot_to command to load code to specified address</summary>
        Task SendBootTo(uint address, byte[] data);
        
        /// <summary>Send DA command</summary>
        Task SendDaCommand(uint command, byte[] data = null);
        
        /// <summary>Receive DA response</summary>
        Task<byte[]> ReceiveDaResponse(int length);
    }
}
