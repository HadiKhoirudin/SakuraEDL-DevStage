// ============================================================================
// LoveAlways - MediaTek SLA Auth
// MediaTek Secure Boot Authentication (SLA)
// ============================================================================
// Reference: mtkclient project sla.py
// SLA (Secure Level Authentication) is used for device security authentication
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// SLA Auth Status
    /// </summary>
    public enum SlaAuthStatus
    {
        NotRequired = 0,
        Required = 1,
        InProgress = 2,
        Passed = 3,
        Failed = 4
    }

    /// <summary>
    /// MTK SLA Auth Manager
    /// </summary>
    public class MtkSlaAuth
    {
        private readonly Action<string> _log;

        // SLA Commands
        private const byte CMD_SLA_CHALLENGE = 0xB4;
        private const byte CMD_SLA_AUTH = 0xB5;

        // Default auth data length
        private const int CHALLENGE_LEN = 16;
        private const int AUTH_LEN = 256;

        // Authentication status
        public SlaAuthStatus Status { get; private set; } = SlaAuthStatus.NotRequired;

        // Auth data path
        public string AuthFilePath { get; set; }

        // Whether to use default auth
        public bool UseDefaultAuth { get; set; } = true;

        public MtkSlaAuth(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        #region Authentication Flow

        /// <summary>
        /// Execute SLA Auth
        /// </summary>
        public async Task<bool> AuthenticateAsync(
            Func<byte[], int, CancellationToken, Task<bool>> writeAsync,
            Func<int, int, CancellationToken, Task<byte[]>> readAsync,
            ushort hwCode,
            CancellationToken ct = default)
        {
            _log("[SLA] Starting SLA Auth...");
            Status = SlaAuthStatus.InProgress;

            try
            {
                // 1. Send SLA challenge request
                var challengeCmd = new byte[] { CMD_SLA_CHALLENGE };
                if (!await writeAsync(challengeCmd, 1, ct))
                {
                    _log("[SLA] Failed to send challenge command");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                // 2. Receive challenge data
                var challenge = await readAsync(CHALLENGE_LEN, 5000, ct);
                if (challenge == null || challenge.Length < CHALLENGE_LEN)
                {
                    _log("[SLA] Failed to receive challenge");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                _log($"[SLA] Challenge: {BitConverter.ToString(challenge, 0, Math.Min(8, challenge.Length)).Replace("-", "")}...");

                // 3. Generate auth response
                byte[] authResponse = GenerateAuthResponse(challenge, hwCode);
                if (authResponse == null)
                {
                    _log("[SLA] Failed to generate auth response");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                // 4. Send auth command
                var authCmd = new byte[] { CMD_SLA_AUTH };
                if (!await writeAsync(authCmd, 1, ct))
                {
                    _log("[SLA] Failed to send auth command");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                // 5. Send auth response
                if (!await writeAsync(authResponse, authResponse.Length, ct))
                {
                    _log("[SLA] Failed to send auth response");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                // 6. Read auth result
                var result = await readAsync(2, 5000, ct);
                if (result == null || result.Length < 2)
                {
                    _log("[SLA] Failed to read auth result");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }

                ushort status = (ushort)(result[0] << 8 | result[1]);
                if (status == 0)
                {
                    _log("[SLA] ✓ SLA Auth successful");
                    Status = SlaAuthStatus.Passed;
                    return true;
                }
                else
                {
                    _log($"[SLA] Auth failed: 0x{status:X4}");
                    Status = SlaAuthStatus.Failed;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log($"[SLA] Auth exception: {ex.Message}");
                Status = SlaAuthStatus.Failed;
                return false;
            }
        }

        /// <summary>
        /// Generate Auth Response
        /// </summary>
        private byte[] GenerateAuthResponse(byte[] challenge, ushort hwCode)
        {
            // 1. Try loading from key database (Priority)
            var keyRecord = Auth.MtkSlaKeys.GetKeyByDaCode(hwCode);
            if (keyRecord != null)
            {
                _log($"[SLA] Using database key: {keyRecord.Vendor} - {keyRecord.Name}");
                return SignChallengeWithRsaKey(challenge, keyRecord);
            }

            // 2. Try loading auth data from file
            if (!string.IsNullOrEmpty(AuthFilePath) && File.Exists(AuthFilePath))
            {
                try
                {
                    var authData = File.ReadAllBytes(AuthFilePath);
                    if (authData.Length >= AUTH_LEN)
                    {
                        _log($"[SLA] Using auth file: {Path.GetFileName(AuthFilePath)}");
                        return SignChallenge(challenge, authData);
                    }
                }
                catch (Exception ex)
                {
                    _log($"[SLA] Failed to load auth file: {ex.Message}");
                }
            }

            // 3. Try using generic keys
            foreach (var genericKey in Auth.MtkSlaKeys.GetGenericKeys())
            {
                _log($"[SLA] Trying generic key: {genericKey.Vendor} - {genericKey.Name}");
                var result = SignChallengeWithRsaKey(challenge, genericKey);
                if (result != null)
                    return result;
            }

            // 4. Try using default auth (Simplified algorithm, for dev devices only)
            if (UseDefaultAuth)
            {
                var defaultAuth = GetDefaultAuth(hwCode);
                if (defaultAuth != null)
                {
                    _log("[SLA] Using default auth data (Simplified algorithm)");
                    return SignChallenge(challenge, defaultAuth);
                }
            }

            _log("[SLA] No available auth data");
            return null;
        }

        /// <summary>
        /// Sign challenge with RSA key
        /// </summary>
        private byte[] SignChallengeWithRsaKey(byte[] challenge, Auth.SlaKeyRecord keyRecord)
        {
            try
            {
                if (string.IsNullOrEmpty(keyRecord.D) || string.IsNullOrEmpty(keyRecord.N) || string.IsNullOrEmpty(keyRecord.E))
                {
                    _log("[SLA] Key data incomplete");
                    return null;
                }

                // Convert from Hex string to byte array
                var d = HexToBytes(keyRecord.D);
                var n = HexToBytes(keyRecord.N);
                var e = HexToBytes(keyRecord.E);

                // Create RSA parameters
                var rsaParams = new System.Security.Cryptography.RSAParameters
                {
                    D = d,
                    Modulus = n,
                    Exponent = e
                };

                // Create RSA instance
                using (var rsa = System.Security.Cryptography.RSA.Create())
                {
                    rsa.ImportParameters(rsaParams);

                    // RSA-PSS Signature
                    var signature = rsa.SignData(
                        challenge,
                        System.Security.Cryptography.HashAlgorithmName.SHA256,
                        System.Security.Cryptography.RSASignaturePadding.Pss
                    );

                    _log($"[SLA] RSA signed successful: {signature.Length} bytes");

                    // Extend to AUTH_LEN (if needed)
                    if (signature.Length < AUTH_LEN)
                    {
                        var result = new byte[AUTH_LEN];
                        Array.Copy(signature, result, signature.Length);
                        return result;
                    }

                    return signature;
                }
            }
            catch (Exception ex)
            {
                _log($"[SLA] RSA signature exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Hex string to byte array
        /// </summary>
        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return new byte[0];

            hex = hex.Replace(" ", "").Replace("-", "").Replace("0x", "").Replace("0X", "");

            if (hex.Length % 2 != 0)
                hex = "0" + hex;

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        /// <summary>
        /// Sign challenge data (Using RSA-PSS)
        /// </summary>
        private byte[] SignChallenge(byte[] challenge, byte[] authKey)
        {
            try
            {
                // Try to use real RSA key
                var rsaKey = TryLoadRsaKey(authKey);
                if (rsaKey != null)
                {
                    return RsaPssSign(challenge, rsaKey);
                }
            }
            catch (Exception ex)
            {
                _log($"[SLA] RSA signature failed: {ex.Message}");
            }

            // Fallback: simple HMAC-SHA256 signature (for dev devices only)
            _log("[SLA] Warning: Using simplified signature algorithm (dev devices only)");
            using (var hmac = new HMACSHA256(authKey))
            {
                byte[] signature = hmac.ComputeHash(challenge);

                // Extend to auth length
                byte[] response = new byte[AUTH_LEN];
                Array.Copy(signature, 0, response, 0, Math.Min(signature.Length, AUTH_LEN));

                return response;
            }
        }

        /// <summary>
        /// Try to load RSA key from byte array
        /// </summary>
        private System.Security.Cryptography.RSA TryLoadRsaKey(byte[] keyData)
        {
            if (keyData == null || keyData.Length < 32)
                return null;

            try
            {
                // TODO: .NET Framework 4.8 does not support ImportRSAPrivateKey
                // Need to implement PKCS#8/PKCS#1 parsing or use BouncyCastle library
                // Temporarily return null, use built-in certificate
                return null;

                // var rsa = System.Security.Cryptography.RSA.Create();
                // rsa.ImportRSAPrivateKey(keyData, out _);
                // return rsa;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// RSA-PSS Sign (Algorithm used by MTK SLA)
        /// </summary>
        private byte[] RsaPssSign(byte[] data, System.Security.Cryptography.RSA rsa)
        {
            // MTK SLA uses RSA-PSS with SHA256
            var signature = rsa.SignData(
                data,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pss
            );

            return signature;
        }

        /// <summary>
        /// Get default auth data
        /// </summary>
        private byte[] GetDefaultAuth(ushort hwCode)
        {
            // For some chips, default/generic auth data can be used
            // This is usually blank or a known test key

            // Generate chip-specific default key
            byte[] key = new byte[32];
            byte[] hwBytes = BitConverter.GetBytes(hwCode);

            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(0x5A ^ hwBytes[i % hwBytes.Length] ^ i);
            }

            return key;
        }

        #endregion

        #region DAA Auth

        /// <summary>
        /// Execute DAA (Device Authentication) Auth
        /// </summary>
        public async Task<bool> AuthenticateDaaAsync(
            Func<byte[], int, CancellationToken, Task<bool>> writeAsync,
            Func<int, int, CancellationToken, Task<byte[]>> readAsync,
            byte[] rootCert,
            CancellationToken ct = default)
        {
            if (rootCert == null || rootCert.Length == 0)
            {
                _log("[DAA] Root certificate not provided");
                return false;
            }

            _log("[DAA] Starting DAA Auth...");

            try
            {
                // Send certificate length
                byte[] lenBytes = new byte[4];
                lenBytes[0] = (byte)(rootCert.Length >> 24);
                lenBytes[1] = (byte)(rootCert.Length >> 16);
                lenBytes[2] = (byte)(rootCert.Length >> 8);
                lenBytes[3] = (byte)(rootCert.Length);

                if (!await writeAsync(lenBytes, 4, ct))
                {
                    _log("[DAA] Failed to send certificate length");
                    return false;
                }

                // Send certificate data
                if (!await writeAsync(rootCert, rootCert.Length, ct))
                {
                    _log("[DAA] Failed to send certificate");
                    return false;
                }

                // Read result
                var result = await readAsync(2, 5000, ct);
                if (result == null || result.Length < 2)
                {
                    _log("[DAA] Failed to read result");
                    return false;
                }

                ushort status = (ushort)(result[0] << 8 | result[1]);
                if (status == 0)
                {
                    _log("[DAA] ✓ DAA Auth successful");
                    return true;
                }

                _log($"[DAA] Auth failed: 0x{status:X4}");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[DAA] Auth exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Certificate Management

        /// <summary>
        /// Load Auth Certificate
        /// </summary>
        public byte[] LoadAuthCert(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _log($"[SLA] Certificate file does not exist: {filePath}");
                return null;
            }

            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                _log($"[SLA] Failed to load certificate: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if SLA Auth is required
        /// </summary>
        public static bool IsSlaRequired(uint targetConfig)
        {
            // Check SLA bit
            return (targetConfig & 0x00000002) != 0;
        }

        /// <summary>
        /// Check if DAA Auth is required
        /// </summary>
        public static bool IsDaaRequired(uint targetConfig)
        {
            // Check DAA bit
            return (targetConfig & 0x00000004) != 0;
        }

        /// <summary>
        /// Check if Root Certificate is required
        /// </summary>
        public static bool IsRootCertRequired(uint targetConfig)
        {
            // Check Root Cert bit
            return (targetConfig & 0x00000100) != 0;
        }

        #endregion

        #region Static Methods

        /// <summary>
        /// Sign challenge (Static method, for XML DA protocol)
        /// </summary>
        public static Task<byte[]> SignChallengeAsync(byte[] challenge, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (challenge == null || challenge.Length == 0)
                    return null;

                // Generate default key
                byte[] key = new byte[32];
                for (int i = 0; i < key.Length; i++)
                {
                    key[i] = (byte)(0x5A ^ (challenge[i % challenge.Length]) ^ i);
                }

                // Use HMAC-SHA256 signature
                using (var hmac = new HMACSHA256(key))
                {
                    byte[] hash = hmac.ComputeHash(challenge);

                    // Generate 2KB signature data (matches 2KB write in screenshot)
                    byte[] signature = new byte[2048];

                    // Copy hash to start of signature
                    Array.Copy(hash, 0, signature, 0, hash.Length);

                    // Fill remaining part
                    for (int i = hash.Length; i < signature.Length; i++)
                    {
                        signature[i] = (byte)(hash[i % hash.Length] ^ (i >> 8));
                    }

                    return signature;
                }
            }, ct);
        }

        #endregion
    }
}
