// ============================================================================
// LoveAlways - Xiaomi Authentication Strategy
// Xiaomi MiAuth - Supports Xiaomi device authorization bypass
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Protocol;

namespace LoveAlways.Qualcomm.Authentication
{
    public class XiaomiAuthStrategy : IAuthStrategy
    {
        private readonly Action<string> _log;

        public string Name { get { return "Xiaomi (MiAuth Bypass)"; } }

        /// <summary>
        /// Triggered when an authentication token is required (Base64 format starting with VQ)
        /// </summary>
        public event Action<string> OnAuthTokenRequired;

        /// <summary>
        /// Last obtained authorization token
        /// </summary>
        public string LastAuthToken { get; private set; }
 
        // Predefined signatures (edlclient signature library)
        private static readonly string[] AuthSignsBase64 = new[]
        {
            "k246jlc8rQfBZ2RLYSF4Ndha1P3bfYQKK3IlQy/NoTp8GSz6l57RZRfmlwsbB99sUW/sgfaWj89//dvDl6Fiwso" +
            "+XXYSSqF2nxshZLObdpMLTMZ1GffzOYd2d/ToryWChoK8v05ZOlfn4wUyaZJT4LHMXZ0NVUryvUbVbxjW5SkLpKDKwkMfnxnEwaOddmT" +
            "/q0ip4RpVk4aBmDW4TfVnXnDSX9tRI+ewQP4hEI8K5tfZ0mfyycYa0FTGhJPcTTP3TQzy1Krc1DAVLbZ8IqGBrW13YWN" +
            "/cMvaiEzcETNyA4N3kOaEXKWodnkwucJv2nEnJWTKNHY9NS9f5Cq3OPs4pQ==",
            
            "vzXWATo51hZr4Dh+a5sA/Q4JYoP4Ee3oFZSGbPZ2tBsaMupn" +
            "+6tPbZDkXJRLUzAqHaMtlPMKaOHrEWZysCkgCJqpOPkUZNaSbEKpPQ6uiOVJpJwA" +
            "/PmxuJ72inzSPevriMAdhQrNUqgyu4ATTEsOKnoUIuJTDBmzCeuh/34SOjTdO4Pc+s3ORfMD0TX+WImeUx4c9xVdSL/xirPl" +
            "/BouhfuwFd4qPPyO5RqkU/fevEoJWGHaFjfI302c9k7EpfRUhq1z+wNpZblOHuj0B3/7VOkK8KtSvwLkmVF" +
            "/t9ECiry6G5iVGEOyqMlktNlIAbr2MMYXn6b4Y3GDCkhPJ5LUkQ=="
        };

        public XiaomiAuthStrategy(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        public async Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default(CancellationToken))
        {
            _log("[MiAuth] Attempting Xiaomi authorization bypass...");
            LastAuthToken = null;

            try
            {
                // 1. Try predefined signatures
                int index = 1;
                foreach (var base64 in AuthSignsBase64)
                {
                    if (ct.IsCancellationRequested) break;

                    _log(string.Format("[MiAuth] Trying signature library #{0}...", index));
                    
                    // Send sig command request
                    string sigCmd = "<?xml version=\"1.0\" ?><data><sig TargetName=\"sig\" size_in_bytes=\"256\" verbose=\"1\"/></data>";
                    var sigResp = await client.SendRawXmlAsync(sigCmd, ct);
                    
                    if (sigResp == null || sigResp.Contains("NAK"))
                    {
                        index++;
                        continue;
                    }

                    // Send binary signature
                    byte[] data = Convert.FromBase64String(base64);
                    var authResp = await client.SendRawBytesAndGetResponseAsync(data, ct);

                    if (authResp != null && (authResp.ToLower().Contains("authenticated") || authResp.Contains("ACK")))
                    {
                        await Task.Delay(200, ct);
                        if (await client.PingAsync(ct))
                        {
                            _log("[MiAuth] ✅ Bypass successful! Device unlocked.");
                            return true;
                        }
                    }
                    index++;
                }

                _log("[MiAuth] Built-in signatures invalid, obtaining authorization token...");

                // 2. Get Challenge Token (Base64 format starting with VQ)
                string token = await GetAuthTokenAsync(client, ct);

                if (!string.IsNullOrEmpty(token))
                {
                    LastAuthToken = token;
                    _log(string.Format("[MiAuth] Authorization token: {0}", token));
                    _log("[MiAuth] 💡 Please copy the token for online authorization or official application.");
                    
                    // Trigger event to notify UI to display authorization window
                    OnAuthTokenRequired?.Invoke(token);
                }
                else
                {
                    _log("[MiAuth] ❌ Unable to obtain authorization token.");
                }

                return false;
            }
            catch (Exception ex)
            {
                _log("[MiAuth] Exception: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Get Xiaomi authorization token (Base64 format starting with VQ)
        /// </summary>
        public async Task<string> GetAuthTokenAsync(FirehoseClient client, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                // Send request to get Challenge
                string reqCmd = "<?xml version=\"1.0\" ?><data><sig TargetName=\"req\" /></data>";
                string response = await client.SendRawXmlAsync(reqCmd, ct);
                
                if (string.IsNullOrEmpty(response))
                    return null;

                // Parse value attribute (contains raw Token data)
                string rawValue = ExtractAttribute(response, "value");
                if (string.IsNullOrEmpty(rawValue))
                    return null;

                // If already starts with VQ, return directly
                if (rawValue.StartsWith("VQ"))
                    return rawValue;

                // Try to parse as hex and convert to Base64
                byte[] tokenBytes = HexToBytes(rawValue);
                if (tokenBytes != null && tokenBytes.Length > 0)
                {
                    string base64Token = Convert.ToBase64String(tokenBytes);
                    // Xiaomi Token usually starts with VQ
                    if (base64Token.StartsWith("VQ"))
                        return base64Token;
                    return base64Token;
                }

                return rawValue;
            }
            catch (Exception ex)
            {
                _log("[MiAuth] Get token exception: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Perform authentication using signature (used after online authorization)
        /// </summary>
        public async Task<bool> AuthenticateWithSignatureAsync(FirehoseClient client, string signatureBase64, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                _log("[MiAuth] Using online signature for authentication...");

                // Send sig command to prepare for receiving signature
                string sigCmd = "<?xml version=\"1.0\" ?><data><sig TargetName=\"sig\" size_in_bytes=\"256\" verbose=\"1\"/></data>";
                var sigResp = await client.SendRawXmlAsync(sigCmd, ct);

                if (sigResp == null || sigResp.Contains("NAK"))
                {
                    _log("[MiAuth] Device rejected signature request");
                    return false;
                }

                // Send signature data
                byte[] signatureData = Convert.FromBase64String(signatureBase64);
                var authResp = await client.SendRawBytesAndGetResponseAsync(signatureData, ct);

                if (authResp != null && (authResp.ToLower().Contains("authenticated") || authResp.Contains("ACK")))
                {
                    await Task.Delay(200, ct);
                    if (await client.PingAsync(ct))
                    {
                        _log("[MiAuth] ✅ Online authorization successful! Device unlocked.");
                        return true;
                    }
                }

                _log("[MiAuth] ❌ Signature verification failed");
                return false;
            }
            catch (Exception ex)
            {
                _log("[MiAuth] Signature authentication exception: " + ex.Message);
                return false;
            }
        }

        private string ExtractAttribute(string xml, string attrName)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            
            string pattern1 = attrName + "=\"";
            int start = xml.IndexOf(pattern1);
            if (start < 0) return null;
            
            start += pattern1.Length;
            int end = xml.IndexOf("\"", start);
            if (end < 0) return null;
            
            return xml.Substring(start, end - start);
        }

        private byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            
            // Remove potential prefix and spaces
            hex = hex.Replace(" ", "").Replace("0x", "").Replace("0X", "");
            
            if (hex.Length % 2 != 0) return null;
            
            try
            {
                byte[] bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            catch
            {
                return null;
            }
        }
    }
}
