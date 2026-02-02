// ============================================================================
// LoveAlways - Spreadtrum Resource Loader
// Prioritizes loading from resource package (sprd_resources.pak), falls back to embedded resources
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.IO;
using System.Reflection;

namespace LoveAlways.Spreadtrum.Resources
{
    /// <summary>
    /// Spreadtrum module resource loader
    /// Loading priority: Resource Package (sprd_resources.pak) > Embedded Resources
    /// </summary>
    public static class SprdResourceLoader
    {
        private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
        private static SprdResourcePak _pak;
        private static bool _pakChecked;
        private static readonly object _lock = new object();

        // Resource package filename
        private const string PAK_FILENAME = "sprd_resources.pak";

        // Embedded resource name prefix
        private const string EMBEDDED_PREFIX = "LoveAlways.Spreadtrum.Resources.";

        /// <summary>
        /// Exploit payload filenames
        /// </summary>
        public static class ExploitPayloads
        {
            public const string Exploit_4ee8 = "exploit_4ee8.bin";
            public const string Exploit_65015f08 = "exploit_65015f08.bin";
            public const string Exploit_65015f48 = "exploit_65015f48.bin";
        }

        #region Resource Package Management

        /// <summary>
        /// Ensure resource package is loaded
        /// </summary>
        private static void EnsurePak()
        {
            if (_pakChecked) return;

            lock (_lock)
            {
                if (_pakChecked) return;

                string pakPath = GetPakPath();
                if (File.Exists(pakPath))
                {
                    try
                    {
                        _pak = new SprdResourcePak(pakPath);
                    }
                    catch
                    {
                        _pak = null;
                    }
                }
                _pakChecked = true;
            }
        }

        /// <summary>
        /// Get resource package path
        /// </summary>
        private static string GetPakPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PAK_FILENAME);
        }

        /// <summary>
        /// Check if resource package is available
        /// </summary>
        public static bool IsPakAvailable()
        {
            EnsurePak();
            return _pak != null;
        }

        /// <summary>
        /// Get resource package version
        /// </summary>
        public static int GetPakVersion()
        {
            EnsurePak();
            return _pak?.Version ?? 0;
        }

        /// <summary>
        /// Get number of resources in the package
        /// </summary>
        public static int GetPakResourceCount()
        {
            EnsurePak();
            return _pak?.Count ?? 0;
        }

        #endregion

        #region Exploit Loading

        /// <summary>
        /// Get corresponding exploit payload by FDL1 address
        /// </summary>
        /// <param name="fdl1Address">FDL1 load address</param>
        /// <returns>Exploit payload data, returns null if no match</returns>
        public static byte[] GetExploitPayload(uint fdl1Address)
        {
            string exploitName = GetExploitNameByAddress(fdl1Address);
            if (string.IsNullOrEmpty(exploitName))
                return null;

            string fileName = GetExploitFileName(exploitName);
            if (string.IsNullOrEmpty(fileName))
                return null;

            return LoadResource(fileName);
        }

        /// <summary>
        /// Get payload by exploit name
        /// </summary>
        /// <param name="exploitName">Example: "0x4ee8", "0x65015f08"</param>
        /// <returns>Exploit payload data</returns>
        public static byte[] GetExploitPayloadByName(string exploitName)
        {
            string fileName = GetExploitFileName(exploitName);
            if (string.IsNullOrEmpty(fileName))
                return null;

            return LoadResource(fileName);
        }

        /// <summary>
        /// Get exploit name by FDL1 address
        /// </summary>
        private static string GetExploitNameByAddress(uint fdl1Address)
        {
            // Based on Prepare_Exploit function logic in iReverse project
            if (fdl1Address == 0x5000 || fdl1Address == 0x00005000)
                return "0x4ee8";

            if (fdl1Address == 0x65000800)
                return "0x65015f08";

            if (fdl1Address == 0x65000000)
                return "0x65015f48";

            return null;
        }

        /// <summary>
        /// Get exploit filename
        /// </summary>
        private static string GetExploitFileName(string exploitName)
        {
            switch (exploitName?.ToLower())
            {
                case "0x4ee8":
                    return ExploitPayloads.Exploit_4ee8;
                case "0x65015f08":
                    return ExploitPayloads.Exploit_65015f08;
                case "0x65015f48":
                    return ExploitPayloads.Exploit_65015f48;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Check if exploit payload is available
        /// </summary>
        public static bool HasExploitPayload(uint fdl1Address)
        {
            return !string.IsNullOrEmpty(GetExploitNameByAddress(fdl1Address));
        }

        /// <summary>
        /// Check if exploit is available (Alias)
        /// </summary>
        public static bool HasExploitForAddress(uint fdl1Address)
        {
            return HasExploitPayload(fdl1Address);
        }

        /// <summary>
        /// Get exploit address ID
        /// </summary>
        public static string GetExploitAddressId(uint fdl1Address)
        {
            return GetExploitNameByAddress(fdl1Address);
        }

        #endregion

        #region General Resource Loading

        /// <summary>
        /// Load resource (Priority: Package, Fallback: Embedded)
        /// </summary>
        /// <param name="resourceName">Resource filename</param>
        /// <returns>Resource data</returns>
        public static byte[] LoadResource(string resourceName)
        {
            // 1. Try loading from resource package
            EnsurePak();
            if (_pak != null)
            {
                byte[] pakData = _pak.GetResource(resourceName);
                if (pakData != null)
                    return pakData;
            }

            // 2. Fallback: load from embedded resources
            return LoadEmbeddedResource(resourceName);
        }

        /// <summary>
        /// Load from embedded resource
        /// </summary>
        private static byte[] LoadEmbeddedResource(string resourceName)
        {
            string fullName = EMBEDDED_PREFIX + resourceName;

            try
            {
                using (Stream stream = _assembly.GetManifestResourceStream(fullName))
                {
                    if (stream == null)
                    {
                        // Try other possible name formats
                        foreach (string name in _assembly.GetManifestResourceNames())
                        {
                            if (name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                            {
                                using (Stream altStream = _assembly.GetManifestResourceStream(name))
                                {
                                    if (altStream != null)
                                    {
                                        return ReadAllBytes(altStream);
                                    }
                                }
                            }
                        }
                        return null;
                    }

                    return ReadAllBytes(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Read all bytes from stream
        /// </summary>
        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        #endregion

        #region Exploit Information

        /// <summary>
        /// Get all available exploit information
        /// </summary>
        public static ExploitInfo[] GetAvailableExploits()
        {
            return new ExploitInfo[]
            {
                new ExploitInfo
                {
                    Name = "0x4ee8",
                    FileName = ExploitPayloads.Exploit_4ee8,
                    Description = "SC77xx series BSL overflow vulnerability",
                    SupportedAddresses = new uint[] { 0x5000, 0x00005000 },
                    SupportedChips = "SC7731, SC7730, SC9830"
                },
                new ExploitInfo
                {
                    Name = "0x65015f08",
                    FileName = ExploitPayloads.Exploit_65015f08,
                    Description = "SC98xx/T series signature bypass vulnerability",
                    SupportedAddresses = new uint[] { 0x65000800 },
                    SupportedChips = "SC9863A, T610, T618"
                },
                new ExploitInfo
                {
                    Name = "0x65015f48",
                    FileName = ExploitPayloads.Exploit_65015f48,
                    Description = "SC98xx series signature bypass vulnerability (variant)",
                    SupportedAddresses = new uint[] { 0x65000000 },
                    SupportedChips = "SC9850, SC9860"
                }
            };
        }

        #endregion

        #region Temporary File Extraction

        /// <summary>
        /// Extract exploit payload to temporary directory
        /// </summary>
        /// <param name="exploitName">Exploit name</param>
        /// <returns>Extracted file path, returns null if failed</returns>
        public static string ExtractExploitToTemp(string exploitName)
        {
            byte[] payload = GetExploitPayloadByName(exploitName);
            if (payload == null)
                return null;

            string fileName = GetExploitFileName(exploitName);
            string tempPath = Path.Combine(Path.GetTempPath(), "LoveAlways_Sprd", fileName);

            try
            {
                string dir = Path.GetDirectoryName(tempPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(tempPath, payload);
                return tempPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cleanup temporarily extracted files
        /// </summary>
        public static void CleanupTemp()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "LoveAlways_Sprd");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #endregion

        #region Resource Package Tools

        /// <summary>
        /// Create resource package from specified directory
        /// </summary>
        /// <param name="sourceDir">Source directory</param>
        /// <param name="outputPath">Output path (Optional, default is sprd_resources.pak in app directory)</param>
        public static void CreateResourcePak(string sourceDir, string outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = GetPakPath();

            SprdResourcePak.CreatePakFromDirectory(sourceDir, outputPath);
        }

        /// <summary>
        /// Create resource package from embedded resources
        /// </summary>
        /// <param name="outputPath">Output path</param>
        public static void CreateResourcePakFromEmbedded(string outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = GetPakPath();

            var resources = new System.Collections.Generic.List<(string Name, byte[] Data, SprdResourcePak.ResourceType Type)>();

            // Add all exploit resources
            foreach (var exploit in GetAvailableExploits())
            {
                byte[] data = LoadEmbeddedResource(exploit.FileName);
                if (data != null)
                {
                    resources.Add((exploit.FileName, data, SprdResourcePak.ResourceType.Exploit));
                }
            }

            if (resources.Count > 0)
            {
                SprdResourcePak.CreatePak(outputPath, resources);
            }
        }

        #endregion
    }

    /// <summary>
    /// Exploit information structure
    /// </summary>
    public class ExploitInfo
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Description { get; set; }
        public uint[] SupportedAddresses { get; set; }
        public string SupportedChips { get; set; }
    }
}
