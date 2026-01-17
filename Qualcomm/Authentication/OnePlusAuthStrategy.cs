// ============================================================================
// LoveAlways - OnePlus 动态认证策略
// 支持 OnePlus 5/5T/6/6T/7/7Pro/7T/8/8Pro/8T/9/9Pro/Nord/N10/N100
// ============================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Protocol;

namespace LoveAlways.Qualcomm.Authentication
{
    public class OnePlusAuthStrategy : IAuthStrategy
    {
        private readonly Action<string> _log;
        private string _serial = "123456";
        private string _projId = "";
        private int _version = 1;

        public string Name { get { return "OnePlus (Demacia/SetProjModel)"; } }

        // 设备配置: projId -> (version, cm, paramMode)
        private static readonly Dictionary<string, Tuple<int, string, int>> DeviceConfigs = new Dictionary<string, Tuple<int, string, int>>
        {
            // OP5-7T 系列 (Version 1)
            { "16859", Tuple.Create(1, (string)null, 0) },  // OP5
            { "17801", Tuple.Create(1, (string)null, 0) },  // OP5T
            { "17819", Tuple.Create(1, (string)null, 0) },  // OP6
            { "18801", Tuple.Create(1, (string)null, 0) },  // OP6T
            { "18811", Tuple.Create(1, (string)null, 0) },  // OP6T T-Mo
            { "18857", Tuple.Create(1, (string)null, 0) },  // OP7
            { "18821", Tuple.Create(1, (string)null, 0) },  // OP7 Pro
            { "18825", Tuple.Create(1, (string)null, 0) },  // OP7 Pro 5G
            { "18827", Tuple.Create(1, (string)null, 0) },  // OP7 Pro 5G EE
            { "18831", Tuple.Create(1, (string)null, 0) },  // OP7 Pro T-Mo
            { "18865", Tuple.Create(1, (string)null, 0) },  // OP7T
            { "19801", Tuple.Create(1, (string)null, 0) },  // OP7T Pro
            { "19861", Tuple.Create(1, (string)null, 0) },  // OP7T Pro 5G
            { "19863", Tuple.Create(1, (string)null, 0) },  // OP7T T-Mo

            // OP8/9 系列 (Version 2)
            { "19821", Tuple.Create(2, "0cffee8a", 0) },  // OP8
            { "19855", Tuple.Create(2, "6d9215b4", 0) },  // OP8 T-Mo
            { "19811", Tuple.Create(2, "40217c07", 0) },  // OP8 Pro
            { "19805", Tuple.Create(2, "1a5ec176", 0) },  // OP8T
            { "20801", Tuple.Create(2, "eacf50e7", 0) },  // Nord
            { "19815", Tuple.Create(2, "9c151c7f", 0) },  // OP9 Pro
            { "19825", Tuple.Create(2, "0898dcd6", 0) },  // OP9

            // N10/N100 系列 (Version 3)
            { "20885", Tuple.Create(3, "3a403a71", 1) },  // N10 5G Metro
            { "20886", Tuple.Create(3, "b8bd9e39", 1) },  // N10 5G Global
            { "20888", Tuple.Create(3, "142f1bd7", 1) },  // N10 5G TMO
            { "20880", Tuple.Create(3, "6ccf5913", 1) },  // N100 Metro
            { "20881", Tuple.Create(3, "fa9ff378", 1) },  // N100 Global
        };

        private static readonly byte[] AesKeyPrefix1 = { 0x10, 0x45, 0x63, 0x87, 0xE3, 0x7E, 0x23, 0x71 };
        private static readonly byte[] AesKeySuffix1 = { 0xA2, 0xD4, 0xA0, 0x74, 0x0F, 0xD3, 0x28, 0x96 };
        private static readonly byte[] AesIv1 = { 0x9D, 0x61, 0x4A, 0x1E, 0xAC, 0x81, 0xC9, 0xB2, 0xD3, 0x76, 0xD7, 0x49, 0x31, 0x03, 0x63, 0x79 };
        private static readonly byte[] AesKeyPrefixDemacia = { 0x01, 0x63, 0xA0, 0xD1, 0xFD, 0xE2, 0x67, 0x11 };
        private static readonly byte[] AesKeySuffixDemacia = { 0x48, 0x27, 0xC2, 0x08, 0xFB, 0xB0, 0xE6, 0xF0 };
        private static readonly byte[] AesIvDemacia = { 0x96, 0xE0, 0x79, 0x0C, 0xAE, 0x2B, 0xB4, 0xAF, 0x68, 0x4C, 0x36, 0xCB, 0x0B, 0xEC, 0x49, 0xCE };
        private static readonly byte[] AesKeyPrefixV3 = { 0x46, 0xA5, 0x97, 0x30, 0xBB, 0x0D, 0x41, 0xE8 };
        private static readonly byte[] AesIvV3 = { 0xDC, 0x91, 0x0D, 0x88, 0xE3, 0xC6, 0xEE, 0x65, 0xF0, 0xC7, 0x44, 0xB4, 0x02, 0x30, 0xCE, 0x40 };

        private const string ProdKeyOld = "b2fad511325185e5";
        private const string ProdKeyNew = "7016147d58e8c038";
        private const string RandomPostfixV1 = "8MwDdWXZO7sj0PF3";
        private const string RandomPostfixV3 = "c75oVnz8yUgLZObh";

        public OnePlusAuthStrategy(Action<string> log = null)
        {
            _log = log ?? delegate { };
        }

        public async Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default(CancellationToken))
        {
            _log("[OnePlus] 开始认证流程...");

            // 获取序列号
            if (!string.IsNullOrEmpty(client.ChipSerial))
            {
                string serialHex = client.ChipSerial.Replace("0x", "");
                uint s;
                if (uint.TryParse(serialHex, System.Globalization.NumberStyles.HexNumber, null, out s))
                    _serial = s.ToString();
            }

            _log(string.Format("[OnePlus] 序列号: {0}", _serial));

            // 尝试获取 projId
            await ReadProjIdAsync(client, ct);

            if (string.IsNullOrEmpty(_projId))
            {
                _log("[OnePlus] 无法获取 projid，使用默认值 18821");
                _projId = "18821";
            }

            // 尝试主 projId
            if (await TryAuthenticateWithProjIdAsync(client, _projId, ct))
                return true;

            // 尝试备选 projId
            var alternatives = GetAlternativeProjIds(_projId);
            foreach (var altProjId in alternatives)
            {
                if (ct.IsCancellationRequested) break;
                _log(string.Format("[OnePlus] 尝试备选 projid: {0}", altProjId));
                if (await TryAuthenticateWithProjIdAsync(client, altProjId, ct))
                {
                    _projId = altProjId;
                    return true;
                }
            }

            _log("[OnePlus] ❌ 所有认证尝试均失败");
            return false;
        }

        private async Task ReadProjIdAsync(FirehoseClient client, CancellationToken ct)
        {
            try
            {
                _log("[OnePlus] 正在读取 ProjId...");
                string response = await client.SendRawXmlAsync("<?xml version=\"1.0\" ?><data><getprjversion /></data>", ct);
                if (!string.IsNullOrEmpty(response))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(response, @"(?:prjversion|PrjVersion|projid)=""(\d+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string val = match.Groups[1].Value;
                        if (val.Length >= 5)
                        {
                            _projId = val;
                            _log(string.Format("[OnePlus] ✓ 获取到 ProjId: {0}", _projId));
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log("[OnePlus] getprjversion 异常: " + ex.Message);
            }

            // 猜测逻辑
            string hwid = (client.ChipHwId ?? "").ToLower();
            string pkHash = (client.ChipPkHash ?? "").ToLower();

            if (pkHash.StartsWith("2acf3a85") || pkHash.StartsWith("8aabc662") || hwid.Contains("e1500a00") || hwid.Contains("000a50e1"))
            {
                _projId = "18821"; // OP7 Pro
                _log("[OnePlus] 猜测设备为 OP7 Pro (SM8150)");
            }
            else if (pkHash.StartsWith("c0c66e27") || hwid.Contains("e1b00800") || hwid.Contains("0008b0e1"))
            {
                _projId = "18801"; // OP6T
                _log("[OnePlus] 猜测设备为 OP6T (SDM845)");
            }
        }

        private List<string> GetAlternativeProjIds(string primaryProjId)
        {
            var alts = new List<string>();
            if (primaryProjId == "18821" || primaryProjId == "18825" || primaryProjId == "18827" || primaryProjId == "18831")
            {
                alts.AddRange(new[] { "18821", "18825", "18827", "18831", "18865", "19801" });
            }
            else if (primaryProjId == "18865" || primaryProjId == "19801" || primaryProjId == "19863" || primaryProjId == "19861")
            {
                alts.AddRange(new[] { "18865", "19801", "19863", "19861", "18821", "18825" });
            }
            else if (primaryProjId == "18801" || primaryProjId == "18811" || primaryProjId == "17819" || primaryProjId == "17801")
            {
                alts.AddRange(new[] { "18801", "18811", "17819", "17801" });
            }
            alts.Remove(primaryProjId);
            return alts;
        }

        private async Task<bool> TryAuthenticateWithProjIdAsync(FirehoseClient client, string projId, CancellationToken ct)
        {
            Tuple<int, string, int> config;
            if (!DeviceConfigs.TryGetValue(projId, out config))
                config = Tuple.Create(1, (string)null, 0);

            _version = config.Item1;
            string modelId = config.Item2 ?? projId;

            _log(string.Format("[OnePlus] 尝试: ProjId={0}, 算法=V{1}", projId, _version));

            try
            {
                if (_version == 3)
                    return await AuthenticateV3Async(client, modelId, ct);
                else
                    return await AuthenticateV1V2Async(client, modelId, ct);
            }
            catch (Exception ex)
            {
                _log("[OnePlus] 认证出错: " + ex.Message);
                return false;
            }
        }

        private async Task<bool> AuthenticateV1V2Async(FirehoseClient client, string modelId, CancellationToken ct)
        {
            string pk = GenerateRandomPk();
            string prodKey = _projId == "18825" || _projId == "18801" ? ProdKeyOld : ProdKeyNew;

            // 1. Demacia
            _log("[OnePlus] Step 1: demacia 验证...");
            var demacia = GenerateDemaciaToken(pk);
            string demCmd = string.Format("demacia token=\"{0}\" pk=\"{1}\"", demacia.Item2, demacia.Item1);
            string demResp = await client.SendRawXmlAsync(demCmd, ct);
            
            if (!string.IsNullOrEmpty(demResp) && demResp.Contains("verify_res=\"0\""))
                _log("[OnePlus] ✓ demacia 验证成功");

            // 2. SetProjModel
            _log(string.Format("[OnePlus] Step 2: setprojmodel (model={0})...", modelId));
            var proj = GenerateSetProjModelToken(modelId, pk, prodKey);
            string projCmd = string.Format("setprojmodel token=\"{0}\" pk=\"{1}\"", proj.Item2, proj.Item1);
            string resp = await client.SendRawXmlAsync(projCmd, ct);

            bool ok = !string.IsNullOrEmpty(resp) && (resp.Contains("model_check=\"0\"") || resp.Contains("ACK"));
            if (ok) _log("[OnePlus]  认证成功！设备已解锁。");
            return ok;
        }

        private async Task<bool> AuthenticateV3Async(FirehoseClient client, string modelId, CancellationToken ct)
        {
            _log("[OnePlus] Step 1: 获取设备时间戳...");
            string startResp = await client.SendRawXmlAsync("setprocstart", ct);
            string timestamp = ExtractAttribute(startResp, "device_timestamp");
            if (string.IsNullOrEmpty(timestamp)) return false;

            _log(string.Format("[OnePlus] Step 2: setswprojmodel (model={0})...", modelId));
            string pk = GenerateRandomPk();
            string prodKey = _projId == "18825" || _projId == "18801" ? ProdKeyOld : ProdKeyNew;

            var sw = GenerateSetSwProjModelToken(modelId, pk, prodKey, timestamp);
            string swCmd = string.Format("setswprojmodel token=\"{0}\" pk=\"{1}\"", sw.Item2, sw.Item1);
            string resp = await client.SendRawXmlAsync(swCmd, ct);

            bool ok = !string.IsNullOrEmpty(resp) && (resp.Contains("model_check=\"0\"") || resp.Contains("ACK"));
            if (ok) _log("[OnePlus]  认证成功！设备已解锁。");
            return ok;
        }

        #region 加密助手

        private static string GenerateRandomPk()
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var random = new Random();
            var sb = new StringBuilder(16);
            for (int i = 0; i < 16; i++) sb.Append(chars[random.Next(chars.Length)]);
            return sb.ToString();
        }

        private Tuple<string, string> GenerateDemaciaToken(string pk)
        {
            string serial = _serial.PadLeft(10, '0');
            string hashSource = "2e7006834dafe8ad" + serial + "a6674c6b039707ff";
            byte[] hashBytes = ComputeSha256(Encoding.UTF8.GetBytes(hashSource));

            byte[] data = new byte[256];
            Encoding.ASCII.GetBytes("907heavyworkload").CopyTo(data, 0);
            hashBytes.CopyTo(data, 16);

            return Tuple.Create(pk, EncryptAesCbc(data, pk, true));
        }

        private Tuple<string, string> GenerateSetProjModelToken(string modelId, string pk, string prodKey)
        {
            string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string h1 = prodKey + modelId + RandomPostfixV1;
            string modelHash = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h1))).ToUpper();

            string version = "guacamoles_21_O.22_191107";
            string h2 = "c4b95538c57df231" + modelId + "0" + _serial + version + ts + modelHash + "5b0217457e49381b";
            string secret = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h2))).ToUpper();

            string dataStr = string.Format("{0},{1},{2},{3},0,{4},{5},{6}", modelId, RandomPostfixV1, modelHash, version, _serial, ts, secret);
            byte[] padded = new byte[256];
            Encoding.UTF8.GetBytes(dataStr).CopyTo(padded, 0);

            return Tuple.Create(pk, EncryptAesCbc(padded, pk, false));
        }

        private Tuple<string, string> GenerateSetSwProjModelToken(string modelId, string pk, string prodKey, string deviceTs)
        {
            string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string h1 = prodKey + modelId + RandomPostfixV3;
            string modelHash = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h1))).ToUpper();

            string version = "billie8_14_E.01_201028";
            string h2 = prodKey + modelId + _serial + version + ts + modelHash + "8f7359c8a2951e8c";
            string secret = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h2))).ToUpper();

            string deviceId = modelId;
            try { deviceId = Convert.ToInt32(modelId, 16).ToString(); } catch { }

            string dataStr = string.Format("{0},{1},{2},0,0,{3},{4},{5},{6},{7}", modelId, RandomPostfixV3, modelHash, version, _serial, deviceId, ts, secret);
            byte[] padded = new byte[512];
            Encoding.UTF8.GetBytes(dataStr).CopyTo(padded, 0);

            return Tuple.Create(pk, EncryptAesCbcV3(padded, pk, deviceTs));
        }

        private static string EncryptAesCbc(byte[] data, string pk, bool isDemacia)
        {
            byte[] keyPrefix = isDemacia ? AesKeyPrefixDemacia : AesKeyPrefix1;
            byte[] keySuffix = isDemacia ? AesKeySuffixDemacia : AesKeySuffix1;
            byte[] iv = isDemacia ? AesIvDemacia : AesIv1;

            byte[] key = new byte[32];
            keyPrefix.CopyTo(key, 0);
            Encoding.UTF8.GetBytes(pk).CopyTo(key, 8);
            keySuffix.CopyTo(key, 24);

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            byte[] encrypted = aes.CreateEncryptor().TransformFinalBlock(data, 0, data.Length);
            return BytesToHex(encrypted).ToUpper();
        }

        private static string EncryptAesCbcV3(byte[] data, string pk, string deviceTs)
        {
            byte[] key = new byte[32];
            AesKeyPrefixV3.CopyTo(key, 0);
            Encoding.UTF8.GetBytes(pk).CopyTo(key, 8);
            BitConverter.GetBytes(long.Parse(deviceTs)).CopyTo(key, 24);

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = AesIvV3;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            byte[] encrypted = aes.CreateEncryptor().TransformFinalBlock(data, 0, data.Length);
            return BytesToHex(encrypted).ToUpper();
        }

        private static byte[] ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string ExtractAttribute(string xml, string attrName)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            int start = xml.IndexOf(attrName + "=\"");
            if (start < 0) return null;
            start += attrName.Length + 2;
            int end = xml.IndexOf("\"", start);
            return end < 0 ? null : xml.Substring(start, end - start);
        }

        #endregion
    }
}
