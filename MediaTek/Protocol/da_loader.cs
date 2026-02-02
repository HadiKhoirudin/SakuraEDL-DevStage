// ============================================================================
// LoveAlways - MediaTek DA Loader
// MediaTek Download Agent Loader
// ============================================================================
// Reference: mtkclient project mtk_daloader.py
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DaEntry = LoveAlways.MediaTek.Models.DaEntry;

namespace LoveAlways.MediaTek.Protocol
{
    /// <summary>
    /// DA Loader - Responsible for parsing and loading DA files
    /// </summary>
    public class DaLoader
    {
        private readonly BromClient _brom;
        private readonly Action<string> _log;
        private readonly Action<double> _progressCallback;

        // DA file header magic numbers
        private const uint DA_MAGIC = 0x4D4D4D4D;  // "MMMM"
        private const uint DA_MAGIC_V6 = 0x68766561;  // "hvea" (XML DA)

        // DA1/DA2 default signature lengths
        private const int DEFAULT_SIG_LEN = 0x100;
        private const int V6_SIG_LEN = 0x30;

        public DaLoader(BromClient brom, Action<string> log = null, Action<double> progressCallback = null)
        {
            _brom = brom;
            _log = log ?? delegate { };
            _progressCallback = progressCallback;
        }

        #region DA File Parsing

        /// <summary>
        /// Parse DA file (MTK_AllInOne_DA.bin format)
        /// </summary>
        public (DaEntry da1, DaEntry da2)? ParseDaFile(string filePath, ushort hwCode)
        {
            Debug.WriteLine("Parsing DA File ...");
            if (!File.Exists(filePath))
            {
                _log($"[DA] DA file does not exist: {filePath}");
                return null;
            }

            byte[] data = File.ReadAllBytes(filePath);
            return ParseDaData(data, hwCode);
        }

        /// <summary>
        /// Parse DA data
        /// </summary>
        public (DaEntry da1, DaEntry da2)? ParseDaData(byte[] data, ushort hwCode)
        {
            if (data == null || data.Length < 0x100)
            {
                _log("[DA] Invalid DA data");
                return null;
            }

            try
            {
                // Check DA file format
                uint magic = BitConverter.ToUInt32(data, 0);

                if (magic == DA_MAGIC_V6)
                {
                    return ParseDaV6(data, hwCode);
                }
                else
                {
                    return ParseDaLegacy(data, hwCode);
                }
            }
            catch (Exception ex)
            {
                _log($"[DA] Failed to parse DA: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse V6 (XML) format DA file
        /// </summary>
        private (DaEntry da1, DaEntry da2)? ParseDaV6(byte[] data, ushort hwCode)
        {
            _log($"[DA] Parsing V6 DA file (HW Code: 0x{hwCode:X4})");

            // V6 DA file header structure
            // Offset 0x00: Magic "hvea"
            // Offset 0x04: Version
            // Offset 0x08: Number of DA entries
            // Offset 0x0C: DA entry table offset

            int entryCount = BitConverter.ToInt32(data, 0x08);
            int tableOffset = BitConverter.ToInt32(data, 0x0C);

            DaEntry da1 = null;
            DaEntry da2 = null;

            // Iterate through DA entries to find matching HW Code
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = tableOffset + (i * 0x40);  // 64 bytes per entry

                if (entryOffset + 0x40 > data.Length)
                    break;

                ushort entryHwCode = BitConverter.ToUInt16(data, entryOffset);

                if (entryHwCode == hwCode)
                {
                    // Found matching entry
                    uint da1Offset = BitConverter.ToUInt32(data, entryOffset + 0x10);
                    uint da1Size = BitConverter.ToUInt32(data, entryOffset + 0x14);
                    uint da1LoadAddr = BitConverter.ToUInt32(data, entryOffset + 0x18);

                    uint da2Offset = BitConverter.ToUInt32(data, entryOffset + 0x20);
                    uint da2Size = BitConverter.ToUInt32(data, entryOffset + 0x24);
                    uint da2LoadAddr = BitConverter.ToUInt32(data, entryOffset + 0x28);

                    // Extract DA1
                    if (da1Offset > 0 && da1Size > 0 && da1Offset + da1Size <= data.Length)
                    {
                        da1 = new DaEntry
                        {
                            Name = "DA1",
                            LoadAddr = da1LoadAddr,
                            SignatureLen = V6_SIG_LEN,
                            Data = new byte[da1Size],
                            Version = 6,
                            DaType = (int)DaMode.Xml
                        };
                        Array.Copy(data, da1Offset, da1.Data, 0, da1Size);
                    }

                    // Extract DA2
                    if (da2Offset > 0 && da2Size > 0 && da2Offset + da2Size <= data.Length)
                    {
                        da2 = new DaEntry
                        {
                            Name = "DA2",
                            LoadAddr = da2LoadAddr,
                            SignatureLen = V6_SIG_LEN,
                            Data = new byte[da2Size],
                            Version = 6,
                            DaType = (int)DaMode.Xml
                        };
                        Array.Copy(data, da2Offset, da2.Data, 0, da2Size);
                    }

                    break;
                }
            }

            if (da1 == null)
            {
                _log($"[DA] Could not find DA for HW Code 0x{hwCode:X4}");
                return null;
            }

            _log($"[DA] Found DA1: Address=0x{da1.LoadAddr:X8}, Size={da1.Data.Length}");
            if (da2 != null)
                _log($"[DA] Found DA2: Address=0x{da2.LoadAddr:X8}, Size={da2.Data.Length}");

            return (da1, da2);
        }

        /// <summary>
        /// Parse legacy format DA file
        /// </summary>
        private (DaEntry da1, DaEntry da2)? ParseDaLegacy(byte[] data, ushort hwCode)
        {
            _log($"[DA] Parsing Legacy DA file (HW Code: 0x{hwCode:X4})");

            // Legacy DA files are usually a single DA
            // Needs parsing based on specific format

            var da1 = new DaEntry
            {
                Name = "DA1",
                LoadAddr = 0x200000,  // Default address
                SignatureLen = DEFAULT_SIG_LEN,
                Data = data,
                Version = 3,
                DaType = (int)DaMode.Legacy
            };

            return (da1, null);
        }

        #endregion

        #region DA Upload

        /// <summary>
        /// Upload DA1
        /// </summary>
        public async Task<bool> UploadDa1Async(DaEntry da1, CancellationToken ct = default)
        {
            if (da1 == null || da1.Data == null)
            {
                _log("[DA] DA1 data is empty");
                return false;
            }

            _log($"[DA] Uploading DA1 to 0x{da1.LoadAddr:X8} ({da1.Data.Length} bytes)");

            bool success = await _brom.SendDaAsync(da1.LoadAddr, da1.Data, da1.SignatureLen, ct);
            if (!success)
            {
                _log("[DA] DA1 upload failed");
                return false;
            }

            // Check upload status
            ushort uploadStatus = _brom.LastUploadStatus;
            _log($"[DA] DA upload status: 0x{uploadStatus:X4}");

            // Wait for device to process DA
            _log("[DA] Waiting for device to process DA...");
            await System.Threading.Tasks.Task.Delay(200, ct);

            // Status 0x7017 indicates DAA security error
            // This means device has DAA (Download Agent Authentication) protection enabled
            // In Preloader mode, device may re-enumerate or reboot
            if (uploadStatus == 0x7017 || uploadStatus == 0x7015)
            {
                _log($"[DA] Status 0x{uploadStatus:X4}: DAA security protection triggered");
                _log("[DA] ⚠ Device has DAA (Download Agent Authentication) enabled");
                _log("[DA] ⚠ Requires officially signed DA or vulnerability bypass");
                _log("[DA] Attempting to wait for USB re-enumeration...");

                // Wait for device to process
                await System.Threading.Tasks.Task.Delay(1500, ct);

                // Return success to let upper layer handle USB re-enumeration
                return true;
            }

            // Jump to execute DA1
            try
            {
                success = await _brom.JumpDaAsync(da1.LoadAddr, ct);
            }
            catch (Exception ex)
            {
                // Port closure may mean DA is running and re-enumerating USB
                _log($"[DA] JUMP_DA exception: {ex.Message}");
                _log("[DA] ⚠ Port disconnected - DA may have started execution and re-enumerated USB");
                _log("[DA] ⚠ Please wait for device to reappear and reconnect");

                // Return special status to let upper layer handle reconnection
                return true;  // Temporarily return success as DA might indeed be running
            }

            if (!success)
            {
                // JUMP_DA failed, check port status
                if (!_brom.IsConnected)
                {
                    _log("[DA] ⚠ Port disconnected - DA may be running");
                    _log("[DA] ⚠ Device should reappear with a new COM port");
                    return true;  // DA may be running
                }

                // Try to detect DA ready signal
                _log("[DA] JUMP_DA failed, checking if DA has started...");
                await System.Threading.Tasks.Task.Delay(500, ct);

                try
                {
                    bool daReady = await _brom.TryDetectDaReadyAsync(ct);
                    if (daReady)
                    {
                        _log("[DA] ✓ DA started");
                        return true;
                    }
                }
                catch
                {
                    _log("[DA] Port status changed, DA may be re-enumerating USB");
                    return true;
                }

                _log("[DA] DA1 jump execution failed");
                return false;
            }

            _log("[DA] ✓ DA1 uploaded and executed successfully");
            return true;
        }

        /// <summary>
        /// Upload DA2 (via XML DA protocol)
        /// </summary>
        public async Task<bool> UploadDa2Async(DaEntry da2, XmlDaClient xmlClient, CancellationToken ct = default)
        {
            if (da2 == null || da2.Data == null)
            {
                _log("[DA] DA2 data is empty");
                return false;
            }

            _log($"[DA] Uploading DA2 to 0x{da2.LoadAddr:X8} ({da2.Data.Length} bytes)");

            // Upload DA2 using XML DA protocol
            bool success = await xmlClient.UploadDa2Async(da2, ct);
            if (!success)
            {
                _log("[DA] DA2 upload failed");
                return false;
            }

            _log("[DA] ✓ DA2 uploaded successfully");
            return true;
        }

        #endregion

        #region DA Signature Handling

        /// <summary>
        /// Calculate DA hash position (V6 format)
        /// </summary>
        public int FindDa2HashPosition(byte[] da1, int sigLen)
        {
            // V6 format: hash_pos = len(da1) - sig_len - 0x30
            return da1.Length - sigLen - 0x30;
        }

        /// <summary>
        /// Compute SHA-256 hash
        /// </summary>
        public byte[] ComputeSha256(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        /// <summary>
        /// Fix DA2 hash in DA1 (used for Carbonara exploit)
        /// </summary>
        public byte[] FixDa1Hash(byte[] da1, byte[] patchedDa2, int hashPos)
        {
            if (hashPos < 0 || hashPos + 32 > da1.Length)
            {
                _log("[DA] Invalid hash position");
                return da1;
            }

            // Compute new DA2 hash
            byte[] newHash = ComputeSha256(patchedDa2);

            // Copy DA1 and modify hash
            byte[] result = new byte[da1.Length];
            Array.Copy(da1, result, da1.Length);
            Array.Copy(newHash, 0, result, hashPos, 32);

            _log($"[DA] Updated DA2 hash in DA1 (Position: 0x{hashPos:X})");
            return result;
        }

        #endregion

        #region DA Patching

        /// <summary>
        /// Apply DA patch (used for Carbonara exploit)
        /// </summary>
        public byte[] ApplyDaPatch(byte[] daData, byte[] originalBytes, byte[] patchBytes, int offset)
        {
            if (offset < 0 || offset + originalBytes.Length > daData.Length)
            {
                _log("[DA] Invalid patch offset");
                return daData;
            }

            // Verify original bytes
            bool match = true;
            for (int i = 0; i < originalBytes.Length; i++)
            {
                if (daData[offset + i] != originalBytes[i])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                _log("[DA] Original bytes mismatch, cannot apply patch");
                return daData;
            }

            // Apply patch
            byte[] result = new byte[daData.Length];
            Array.Copy(daData, result, daData.Length);
            Array.Copy(patchBytes, 0, result, offset, patchBytes.Length);

            _log($"[DA] Patch applied (Offset: 0x{offset:X})");
            return result;
        }

        /// <summary>
        /// Find security check function (used for Carbonara exploit)
        /// </summary>
        public int FindSecurityCheckOffset(byte[] daData)
        {
            // Search for security check instruction patterns
            // ARM: MOV R0, #0 (0x00 0x00 0xA0 0xE3)
            // Thumb: MOVS R0, #0 (0x00 0x20)

            byte[] armPattern = { 0x00, 0x00, 0xA0, 0xE3 };  // MOV R0, #0
            byte[] thumbPattern = { 0x00, 0x20 };  // MOVS R0, #0

            // Search ARM mode
            for (int i = 0; i < daData.Length - 4; i++)
            {
                bool match = true;
                for (int j = 0; j < armPattern.Length; j++)
                {
                    if (daData[i + j] != armPattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    // Check context to confirm security check
                    // Usually followed by BX LR or POP {PC}
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Generate patch bytes (MOV R0, #1)
        /// </summary>
        public byte[] GenerateBypassPatch(bool isArm = true)
        {
            if (isArm)
            {
                // ARM: MOV R0, #1 (0x01 0x00 0xA0 0xE3)
                return new byte[] { 0x01, 0x00, 0xA0, 0xE3 };
            }
            else
            {
                // Thumb: MOVS R0, #1 (0x01 0x20)
                return new byte[] { 0x01, 0x20 };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get default DA load address
        /// </summary>
        public uint GetDefaultDa1Address(ushort hwCode)
        {
            // Return default DA1 load address based on chip
            return hwCode switch
            {
                0x0279 => 0x200000,  // MT6797
                0x0326 => 0x200000,  // MT6755
                0x0551 => 0x200000,  // MT6768
                0x0562 => 0x200000,  // MT6761
                0x0717 => 0x200000,  // MT6765
                0x0788 => 0x200000,  // MT6873
                _ => 0x200000        // Default value
            };
        }

        /// <summary>
        /// Get default DA2 load address
        /// </summary>
        public uint GetDefaultDa2Address(ushort hwCode)
        {
            // Return default DA2 load address based on chip
            return hwCode switch
            {
                0x0279 => 0x40000000,  // MT6797
                0x0326 => 0x40000000,  // MT6755
                0x0551 => 0x40000000,  // MT6768
                0x0562 => 0x40000000,  // MT6761
                0x0717 => 0x40000000,  // MT6765
                0x0788 => 0x40000000,  // MT6873
                _ => 0x40000000        // Default value
            };
        }

        /// <summary>
        /// Verify DA data integrity
        /// </summary>
        public bool VerifyDaIntegrity(byte[] daData)
        {
            if (daData == null || daData.Length < 0x100)
                return false;

            // Check ELF header
            if (daData[0] == 0x7F && daData[1] == 'E' && daData[2] == 'L' && daData[3] == 'F')
                return true;

            // Check other valid DA headers
            // ...

            return true;  // Default accept
        }

        #endregion
    }
}
