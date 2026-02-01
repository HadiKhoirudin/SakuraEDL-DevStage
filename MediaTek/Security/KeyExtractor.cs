// ============================================================================
// LoveAlways - MediaTek Key Extractor
// Extract device key info (seccfg, efuse, rpmb keys)
// ============================================================================
// Reference: mtkclient keys.py, seccfg parser
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LoveAlways.MediaTek.Security
{
    /// <summary>
    /// Key Type
    /// </summary>
    public enum KeyType
    {
        Unknown,
        MeId,           // ME ID (Mobile Equipment ID)
        SocId,          // SoC ID
        PrdKey,         // Production Key
        RpmbKey,        // RPMB Key
        FdeKey,         // Full Disk Encryption Key
        SeccfgKey,      // Seccfg Encryption Key
        HrId,           // Hardware Root ID
        PlatformKey,    // Platform Key
        OemKey,         // OEM Key
        DaaKey          // DAA Key
    }

    /// <summary>
    /// Extracted Key Information
    /// </summary>
    public class ExtractedKey
    {
        public KeyType Type { get; set; }
        public string Name { get; set; }
        public byte[] Data { get; set; }
        public int Length => Data?.Length ?? 0;
        public string HexString => Data != null ? BitConverter.ToString(Data).Replace("-", "") : "";
        
        public override string ToString()
        {
            return $"{Name}: {HexString} ({Length} bytes)";
        }
    }

    /// <summary>
    /// Seccfg Partition Structure
    /// </summary>
    public class SeccfgData
    {
        /// <summary>Magic Number</summary>
        public uint Magic { get; set; }
        
        /// <summary>Version</summary>
        public uint Version { get; set; }
        
        /// <summary>Lock State (0=Unlocked, 1=Locked)</summary>
        public uint LockState { get; set; }
        
        /// <summary>Critical Lock State</summary>
        public uint CriticalLockState { get; set; }
        
        /// <summary>SBC Flag</summary>
        public uint SbcFlag { get; set; }
        
        /// <summary>Anti-rollback Version</summary>
        public uint AntiRollbackVersion { get; set; }
        
        /// <summary>Encrypted Data</summary>
        public byte[] EncryptedData { get; set; }
        
        /// <summary>Hash</summary>
        public byte[] Hash { get; set; }
        
        /// <summary>Whether Unlocked</summary>
        public bool IsUnlocked => LockState == 0;
        
        /// <summary>Raw Data</summary>
        public byte[] RawData { get; set; }
    }

    /// <summary>
    /// eFuse Data
    /// </summary>
    public class EfuseData
    {
        /// <summary>Secure Boot Status</summary>
        public bool SecureBootEnabled { get; set; }
        
        /// <summary>SLA Status</summary>
        public bool SlaEnabled { get; set; }
        
        /// <summary>DAA Status</summary>
        public bool DaaEnabled { get; set; }
        
        /// <summary>SBC Status</summary>
        public bool SbcEnabled { get; set; }
        
        /// <summary>Root Key Hash</summary>
        public byte[] RootKeyHash { get; set; }
        
        /// <summary>Anti-rollback Version</summary>
        public uint AntiRollbackVersion { get; set; }
        
        /// <summary>Raw eFuse Data</summary>
        public byte[] RawData { get; set; }
    }

    /// <summary>
    /// MediaTek Key Extractor
    /// </summary>
    public static class KeyExtractor
    {
        // Seccfg Magic Numbers
        private const uint SECCFG_MAGIC = 0x53454343;  // "SECC"
        private const uint SECCFG_MAGIC_V2 = 0x4D4D4D01;  // MTK V2
        private const uint SECCFG_MAGIC_V3 = 0x53454346;  // "SECF"
        
        // Default Key (Used for unencrypted seccfg)
        private static readonly byte[] DefaultKey = new byte[16]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        /// Parse Seccfg partition data
        /// </summary>
        public static SeccfgData ParseSeccfg(byte[] data)
        {
            if (data == null || data.Length < 64)
                return null;

            var seccfg = new SeccfgData
            {
                RawData = data
            };

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                // Read Magic Number
                seccfg.Magic = br.ReadUInt32();
                
                // Validate Magic Number
                if (seccfg.Magic != SECCFG_MAGIC && 
                    seccfg.Magic != SECCFG_MAGIC_V2 && 
                    seccfg.Magic != SECCFG_MAGIC_V3)
                {
                    // Try parsing as unencrypted format
                    ms.Position = 0;
                    return ParseSeccfgUnencrypted(data);
                }
                
                // Version
                seccfg.Version = br.ReadUInt32();
                
                // Parse based on version
                if (seccfg.Magic == SECCFG_MAGIC_V3)
                {
                    // V3 Format
                    ParseSeccfgV3(br, seccfg);
                }
                else if (seccfg.Magic == SECCFG_MAGIC_V2)
                {
                    // V2 Format
                    ParseSeccfgV2(br, seccfg);
                }
                else
                {
                    // V1 Format
                    ParseSeccfgV1(br, seccfg);
                }
            }

            return seccfg;
        }

        private static void ParseSeccfgV1(BinaryReader br, SeccfgData seccfg)
        {
            // Offset 8: Lock State
            seccfg.LockState = br.ReadUInt32();
            seccfg.CriticalLockState = br.ReadUInt32();
            seccfg.SbcFlag = br.ReadUInt32();
            
            // Skip reserved fields
            br.ReadBytes(20);
            
            // Hash (32 bytes)
            seccfg.Hash = br.ReadBytes(32);
        }

        private static void ParseSeccfgV2(BinaryReader br, SeccfgData seccfg)
        {
            // V2 Format has more fields
            seccfg.LockState = br.ReadUInt32();
            seccfg.CriticalLockState = br.ReadUInt32();
            seccfg.SbcFlag = br.ReadUInt32();
            seccfg.AntiRollbackVersion = br.ReadUInt32();
            
            // Reserved fields
            br.ReadBytes(16);
            
            // Hash (32 bytes)
            seccfg.Hash = br.ReadBytes(32);
            
            // Encrypted Data (if exists)
            if (br.BaseStream.Position < br.BaseStream.Length - 64)
            {
                int remaining = (int)(br.BaseStream.Length - br.BaseStream.Position);
                seccfg.EncryptedData = br.ReadBytes(remaining);
            }
        }

        private static void ParseSeccfgV3(BinaryReader br, SeccfgData seccfg)
        {
            // V3 Format
            uint flags = br.ReadUInt32();
            seccfg.LockState = flags & 0x01;
            seccfg.CriticalLockState = (flags >> 1) & 0x01;
            seccfg.SbcFlag = (flags >> 2) & 0x01;
            
            seccfg.AntiRollbackVersion = br.ReadUInt32();
            
            // Reserved fields
            br.ReadBytes(24);
            
            // Hash (32 bytes SHA256)
            seccfg.Hash = br.ReadBytes(32);
        }

        private static SeccfgData ParseSeccfgUnencrypted(byte[] data)
        {
            // Try parsing unencrypted seccfg
            var seccfg = new SeccfgData
            {
                RawData = data,
                Magic = 0,
                Version = 0
            };
            
            // Search for lock state markers
            // Usually at fixed offset positions
            if (data.Length >= 8)
            {
                seccfg.LockState = BitConverter.ToUInt32(data, 4);
            }
            
            return seccfg;
        }

        /// <summary>
        /// Parse eFuse data
        /// </summary>
        public static EfuseData ParseEfuse(byte[] data)
        {
            if (data == null || data.Length < 32)
                return null;

            var efuse = new EfuseData
            {
                RawData = data
            };

            // eFuse layout depends on specific chip
            // This is a generic parsing
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                // Security config is usually in the first 4 bytes
                uint secConfig = br.ReadUInt32();
                
                efuse.SecureBootEnabled = (secConfig & 0x01) != 0;
                efuse.SlaEnabled = (secConfig & 0x02) != 0;
                efuse.DaaEnabled = (secConfig & 0x04) != 0;
                efuse.SbcEnabled = (secConfig & 0x08) != 0;
                
                // Anti-rollback version
                efuse.AntiRollbackVersion = br.ReadUInt32();
                
                // Root Key Hash (if exists)
                if (data.Length >= 40)
                {
                    br.ReadBytes(8);  // Skip reserved fields
                    efuse.RootKeyHash = br.ReadBytes(32);
                }
            }

            return efuse;
        }

        /// <summary>
        /// Derive Key from ME ID and SoC ID
        /// </summary>
        public static byte[] DeriveKey(byte[] meId, byte[] socId)
        {
            if (meId == null || socId == null)
                return null;

            // Combine ME ID and SoC ID
            var combined = new byte[meId.Length + socId.Length];
            Array.Copy(meId, 0, combined, 0, meId.Length);
            Array.Copy(socId, 0, combined, meId.Length, socId.Length);

            // Use SHA256 to derive key
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(combined);
            }
        }

        /// <summary>
        /// Generate RPMB Key
        /// </summary>
        public static byte[] GenerateRpmbKey(byte[] meId, byte[] socId, byte[] hwId = null)
        {
            // RPMB Key is usually derived from device unique identifier
            var baseKey = DeriveKey(meId, socId);
            
            if (hwId != null && hwId.Length > 0)
            {
                // If HW ID exists, derive further
                using (var sha256 = SHA256.Create())
                {
                    var combined = new byte[baseKey.Length + hwId.Length];
                    Array.Copy(baseKey, 0, combined, 0, baseKey.Length);
                    Array.Copy(hwId, 0, combined, baseKey.Length, hwId.Length);
                    return sha256.ComputeHash(combined).Take(32).ToArray();
                }
            }
            
            return baseKey.Take(32).ToArray();
        }

        /// <summary>
        /// Decrypt Seccfg Data
        /// </summary>
        public static byte[] DecryptSeccfg(byte[] encryptedData, byte[] key)
        {
            if (encryptedData == null || key == null)
                return null;

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key.Take(16).ToArray();
                    aes.IV = new byte[16];  // Zero IV

                    using (var decryptor = aes.CreateDecryptor())
                    using (var ms = new MemoryStream())
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(encryptedData, 0, encryptedData.Length);
                        cs.FlushFinalBlock();
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extract all available keys
        /// </summary>
        public static List<ExtractedKey> ExtractAllKeys(
            byte[] seccfgData = null,
            byte[] efuseData = null,
            byte[] meId = null,
            byte[] socId = null)
        {
            var keys = new List<ExtractedKey>();

            // ME ID
            if (meId != null && meId.Length > 0)
            {
                keys.Add(new ExtractedKey
                {
                    Type = KeyType.MeId,
                    Name = "ME ID",
                    Data = meId
                });
            }

            // SoC ID
            if (socId != null && socId.Length > 0)
            {
                keys.Add(new ExtractedKey
                {
                    Type = KeyType.SocId,
                    Name = "SoC ID",
                    Data = socId
                });
            }

            // Derived Key
            if (meId != null && socId != null)
            {
                var derivedKey = DeriveKey(meId, socId);
                if (derivedKey != null)
                {
                    keys.Add(new ExtractedKey
                    {
                        Type = KeyType.PrdKey,
                        Name = "Derived Key (ME+SoC)",
                        Data = derivedKey
                    });

                    // RPMB Key
                    var rpmbKey = GenerateRpmbKey(meId, socId);
                    keys.Add(new ExtractedKey
                    {
                        Type = KeyType.RpmbKey,
                        Name = "RPMB Key",
                        Data = rpmbKey
                    });
                }
            }

            // Seccfg-related
            if (seccfgData != null)
            {
                var seccfg = ParseSeccfg(seccfgData);
                if (seccfg != null && seccfg.Hash != null)
                {
                    keys.Add(new ExtractedKey
                    {
                        Type = KeyType.SeccfgKey,
                        Name = "Seccfg Hash",
                        Data = seccfg.Hash
                    });
                }
            }

            // eFuse-related
            if (efuseData != null)
            {
                var efuse = ParseEfuse(efuseData);
                if (efuse != null && efuse.RootKeyHash != null)
                {
                    keys.Add(new ExtractedKey
                    {
                        Type = KeyType.HrId,
                        Name = "Root Key Hash",
                        Data = efuse.RootKeyHash
                    });
                }
            }

            return keys;
        }

        /// <summary>
        /// Verify Seccfg Integrity
        /// </summary>
        public static bool VerifySeccfgIntegrity(SeccfgData seccfg)
        {
            if (seccfg == null || seccfg.RawData == null || seccfg.Hash == null)
                return false;

            // Calculate data hash (excluding hash field itself)
            using (var sha256 = SHA256.Create())
            {
                // Find position of hash field
                int hashOffset = Array.IndexOf(seccfg.RawData, seccfg.Hash[0]);
                if (hashOffset < 0)
                    return false;

                // Calculate hash of the first half
                var toHash = new byte[hashOffset];
                Array.Copy(seccfg.RawData, 0, toHash, 0, hashOffset);
                
                var calculated = sha256.ComputeHash(toHash);
                
                // Compare
                return calculated.Take(32).SequenceEqual(seccfg.Hash);
            }
        }

        /// <summary>
        /// Generate Unlocked Seccfg
        /// </summary>
        public static byte[] GenerateUnlockedSeccfg(SeccfgData original)
        {
            if (original == null || original.RawData == null)
                return null;

            var unlocked = (byte[])original.RawData.Clone();

            // Modify Lock State
            // This depends on specific seccfg format
            if (original.Magic == SECCFG_MAGIC_V3)
            {
                // V3: Flags at offset 8
                unlocked[8] = 0x00;  // Clear lock bit
            }
            else
            {
                // V1/V2: Lock state at offset 8
                unlocked[8] = 0x00;
                unlocked[9] = 0x00;
                unlocked[10] = 0x00;
                unlocked[11] = 0x00;
            }

            // Recalculate Hash (if needed)
            // Note: Valid hash cannot be generated without correct key
            // Here just modify data, actual usage requires correct signature

            return unlocked;
        }

        /// <summary>
        /// Export keys to file
        /// </summary>
        public static bool ExportKeys(List<ExtractedKey> keys, string outputPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# MediaTek Device Keys");
                sb.AppendLine($"# Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                foreach (var key in keys)
                {
                    sb.AppendLine($"[{key.Type}]");
                    sb.AppendLine($"Name={key.Name}");
                    sb.AppendLine($"Length={key.Length}");
                    sb.AppendLine($"Hex={key.HexString}");
                    sb.AppendLine();
                }

                File.WriteAllText(outputPath, sb.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get Key Extractor Description
        /// </summary>
        public static string GetDescription()
        {
            return @"MediaTek Key Extractor
================================================
Function:
  - Parse seccfg partition (Lock State, Security Configuration)
  - Parse eFuse data (Secure Boot Status, Anti-rollback Version)
  - Derive keys from ME ID and SoC ID
  - Generate RPMB Key
  - Verify and modify seccfg integrity

Supported Formats:
  - Seccfg V1/V2/V3
  - eFuse General Format

Note:
  - Modifying seccfg requires correct signature key
  - RPMB Key derivation algorithm may vary by manufacturer
  - Some operations may brick the device, please operate with caution";
        }
    }
}
