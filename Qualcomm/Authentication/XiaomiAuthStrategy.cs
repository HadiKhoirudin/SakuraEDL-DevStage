// ============================================================================
// LoveAlways - 小米认证策略
// Xiaomi MiAuth - 支持小米设备免授权绕过
// ============================================================================

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

        // 预置签名 (edlclient 签名库)
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
            _log("[MiAuth] 正在尝试小米免授权绕过...");

            try
            {
                // 1. 尝试预置签名
                int index = 1;
                foreach (var base64 in AuthSignsBase64)
                {
                    if (ct.IsCancellationRequested) break;

                    _log(string.Format("[MiAuth] 尝试签名库 #{0}...", index));
                    
                    // 发送 sig 命令请求
                    string sigCmd = "<?xml version=\"1.0\" ?><data><sig TargetName=\"sig\" size_in_bytes=\"256\" verbose=\"1\"/></data>";
                    var sigResp = await client.SendRawXmlAsync(sigCmd, ct);
                    
                    if (sigResp == null || sigResp.Contains("NAK"))
                    {
                        index++;
                        continue;
                    }

                    // 发送二进制签名
                    byte[] data = Convert.FromBase64String(base64);
                    var authResp = await client.SendRawBytesAndGetResponseAsync(data, ct);

                    if (authResp != null && (authResp.ToLower().Contains("authenticated") || authResp.Contains("ACK")))
                    {
                        await Task.Delay(200, ct);
                        if (await client.PingAsync(ct))
                        {
                            _log("[MiAuth] ✅ 绕过成功！设备已解锁。");
                            return true;
                        }
                    }
                    index++;
                }

                _log("[MiAuth] 内置签名无效，尝试获取 Challenge (Token)...");

                // 2. 尝试获取 Challenge
                string token = await client.SendXmlCommandWithAttributeResponseAsync(
                    "<?xml version=\"1.0\" ?><data><sig TargetName=\"req\" /></data>", "value", 10, ct);

                if (!string.IsNullOrEmpty(token))
                {
                    _log(string.Format("[MiAuth] 获取到 Token: {0}...", token.Substring(0, Math.Min(32, token.Length))));
                    _log("[MiAuth] 💡 该设备需要官方账号授权，或使用在线服务。");
                }
                else
                {
                    _log("[MiAuth] ❌ 无法获取 Challenge，认证失败。");
                }

                return false;
            }
            catch (Exception ex)
            {
                _log("[MiAuth] 异常: " + ex.Message);
                return false;
            }
        }
    }
}
