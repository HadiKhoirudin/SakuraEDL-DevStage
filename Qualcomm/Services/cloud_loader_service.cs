// ============================================================================
// CloudLoaderService - cloud Loader Automatic matching服务
// 替代Local PAK 资源，SupportAutomaticdownload和缓存
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Services
{
    public class CloudLoaderService
    {
        #region Singleton
        private static CloudLoaderService _instance;
        private static readonly object _lock = new object();

        public static CloudLoaderService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new CloudLoaderService();
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Configuration

        // API addressConfig
        private const string API_BASE_DEV = "http://localhost:8081/api";
        private const string API_BASE_PROD = "https://www.xiriacg.top/api";

        // 当前Use的 API address
        public string ApiBase { get; set; } = API_BASE_PROD;

        // Local缓存Table of contents
        public string CacheDirectory { get; set; }

        // yesnoEnablecloud matching
        public bool EnableCloudMatch { get; set; } = true;

        // yesnoEnableLocal缓存
        public bool EnableCache { get; set; } = true;

        // timeoutTime (Second)
        public int TimeoutSeconds { get; set; } = 15;

        #endregion

        #region Fields

        private readonly HttpClient _httpClient;
        private Action<string> _log;
        private Action<string> _logDetail;

        #endregion

        #region Constructor

        private CloudLoaderService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MultiFlashTool/2.0");

            // default缓存Table of contents
            CacheDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "cache",
                "loaders"
            );
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Settingslog回调
        /// </summary>
        public void SetLogger(Action<string> log, Action<string> logDetail = null)
        {
            _log = log;
            _logDetail = logDetail;
        }

        /// <summary>
        /// 根据device informationAutomatic matching Loader
        /// </summary>
        public async Task<LoaderResult> MatchLoaderAsync(
            string msmId,
            string pkHash = null,
            string oemId = null,
            string storageType = "ufs")
        {
            if (!EnableCloudMatch)
            {
                Log("Cloud matching is disabled.");
                return null;
            }

            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

                // 1. Check Local缓存
                if (EnableCache && !string.IsNullOrEmpty(pkHash))
                {
                    var cached = LoadFromCache(pkHash);
                    if (cached != null)
                    {
                        Log(string.Format("Use local cache: {0}", cached.Filename));
                        return cached;
                    }
                }

                // 2. 调用cloud API match
                Log("Matching in the cloud Loader...");
                LogDetail(string.Format("MSM ID: {0}, PK Hash: {1}...", msmId, pkHash != null && pkHash.Length >= 16 ? pkHash.Substring(0, 16) : pkHash));

                // 构建Please  JSON
                var json = BuildMatchRequestJson(msmId, pkHash, oemId, storageType);

                Debug.WriteLine($"\n{json}\n");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ApiBase + "/loaders/match", content);
                var resultJson = await response.Content.ReadAsStringAsync();

                // checkresponse

                Debug.WriteLine($"\n{resultJson}\n");

                int code = ParseJsonInt(resultJson, "code");
                if (code != 0)
                {
                    string message = ParseJsonString(resultJson, "message");
                    Log(string.Format("Cloud no matches: {0}", message ?? "UnknownError"));
                    return null;
                }

                // check loader 数据
                string loaderJson = ExtractJsonObject(resultJson, "loader");
                if (string.IsNullOrEmpty(loaderJson))
                {
                    Log("Cloud no matches: No loader data");
                    return null;
                }

                int loaderId = ParseJsonInt(loaderJson, "id");
                string filename = ParseJsonString(loaderJson, "filename");
                string vendor = ParseJsonString(loaderJson, "vendor");
                string chip = ParseJsonString(loaderJson, "chip");
                string authType = ParseJsonString(loaderJson, "auth_type");
                string loaderStorageType = ParseJsonString(loaderJson, "storage_type");
                string hwId = ParseJsonString(loaderJson, "hw_id");

                // checkmatch信息
                string dataJson = ExtractJsonObject(resultJson, "data");
                int score = ParseJsonInt(dataJson, "score");
                string matchType = ParseJsonString(dataJson, "match_type");

                Log(string.Format("cloud matching successfull: {0}", filename));
                LogDetail(string.Format("Manufacturer: {0}, Chip: {1}, auth: {2}", vendor, chip, authType));
                LogDetail(string.Format("Matching Type: {0}, Confidence: {1}%", matchType, score));

                // 3. download Loader file
                byte[] loaderData = null;
                if (loaderId > 0)
                {
                    loaderData = await DownloadLoaderAsync(loaderId);
                }

                var loaderResult = new LoaderResult
                {
                    Id = loaderId,
                    Filename = filename,
                    Vendor = vendor,
                    Chip = chip,
                    AuthType = authType,
                    StorageType = loaderStorageType,
                    HwId = hwId,
                    PkHash = pkHash,
                    MatchType = matchType,
                    Confidence = score,
                    Data = loaderData
                };

                // 4. Keep to缓存
                if (EnableCache && loaderData != null && !string.IsNullOrEmpty(pkHash))
                {
                    SaveToCache(pkHash, loaderResult);
                }

                return loaderResult;
            }
            catch (TaskCanceledException)
            {
                Log("cloud matching timeout");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Log(string.Format("cloud matching fail: {0}", ex.Message));
                return null;
            }
            catch (Exception ex)
            {
                Log(string.Format("cloud matching abnormal: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// fromclouddownload Loader file
        /// </summary>
        public async Task<byte[]> DownloadLoaderAsync(int loaderId)
        {
            try
            {
                Debug.WriteLine(string.Format("\nDownloading Loader (ID: {0})...\n", loaderId));

                Log(string.Format("Downloading Loader (ID: {0})...", loaderId));

                var response = await _httpClient.GetAsync(string.Format("{0}/loaders/{1}/download", ApiBase, loaderId));

                if (!response.IsSuccessStatusCode)
                {
                    Log(string.Format("download fail: HTTP {0}", (int)response.StatusCode));
                    return null;
                }

                var data = await response.Content.ReadAsByteArrayAsync();
                Log(string.Format("Download finished: {0} KB", data.Length / 1024));

                return data;
            }
            catch (Exception ex)
            {
                Log(string.Format("Download abnormal: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 上报devicelog (异步，not阻塞主流程)
        /// </summary>
        public void ReportDeviceLog(
            string msmId,
            string pkHash,
            string oemId,
            string storageType,
            string matchResult)
        {
            if (!EnableCloudMatch) return;

            Task.Run(async () =>
            {
                try
                {
                    var json = BuildDeviceLogJson(msmId, pkHash, oemId, storageType, matchResult);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    //await _httpClient.PostAsync(ApiBase + "/device-logs", content);
                }
                catch
                {
                    // 静默 fail，not影响主流程
                }
            });
        }

        /// <summary>
        /// 清除Local缓存
        /// </summary>
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Log("Cache cleared");
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("Clear cache fail: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Get缓存Size
        /// </summary>
        public long GetCacheSize()
        {
            if (!Directory.Exists(CacheDirectory))
                return 0;

            long size = 0;
            foreach (var file in Directory.GetFiles(CacheDirectory, "*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
            return size;
        }

        #endregion

        #region Private Methods - JSON Helpers

        private string BuildMatchRequestJson(string msmId, string pkHash, string oemId, string storageType)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"msm_id\":\"{0}\"", EscapeJson(msmId ?? ""));
            if (!string.IsNullOrEmpty(pkHash))
                sb.AppendFormat(",\"pk_hash\":\"{0}\"", EscapeJson(pkHash));
            if (!string.IsNullOrEmpty(oemId))
                sb.AppendFormat(",\"oem_id\":\"{0}\"", EscapeJson(oemId));
            sb.AppendFormat(",\"storage_type\":\"{0}\"", EscapeJson(storageType ?? "ufs"));
            sb.Append(",\"client_version\":\"MultiFlashTool/2.0\"");
            sb.Append("}");
            return sb.ToString();
        }

        private string BuildDeviceLogJson(string msmId, string pkHash, string oemId, string storageType, string matchResult)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"platform\":\"qualcomm\"");
            sb.AppendFormat(",\"msm_id\":\"{0}\"", EscapeJson(msmId ?? ""));
            if (!string.IsNullOrEmpty(pkHash))
                sb.AppendFormat(",\"pk_hash\":\"{0}\"", EscapeJson(pkHash));
            if (!string.IsNullOrEmpty(oemId))
                sb.AppendFormat(",\"oem_id\":\"{0}\"", EscapeJson(oemId));
            sb.AppendFormat(",\"storage_type\":\"{0}\"", EscapeJson(storageType ?? "ufs"));
            sb.AppendFormat(",\"match_result\":\"{0}\"", EscapeJson(matchResult ?? ""));
            sb.Append(",\"client_version\":\"MultiFlashTool/2.0\"");
            sb.Append("}");
            return sb.ToString();
        }

        private string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string ParseJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var pattern = string.Format("\"{0}\"\\s*:\\s*\"([^\"]*)\"", Regex.Escape(key));
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private int ParseJsonInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            var pattern = string.Format("\"{0}\"\\s*:\\s*(-?\\d+)", Regex.Escape(key));
            var match = Regex.Match(json, pattern);
            if (match.Success)
            {
                int result;
                if (int.TryParse(match.Groups[1].Value, out result))
                    return result;
            }
            return 0;
        }

        private string ExtractJsonObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var pattern = string.Format("\"{0}\"\\s*:\\s*\\{{", Regex.Escape(key));
            var match = Regex.Match(json, pattern);
            if (!match.Success) return null;

            int start = match.Index + match.Length - 1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
                if (depth == 0)
                    return json.Substring(start, i - start + 1);
            }
            return null;
        }

        #endregion

        #region Private Methods - Cache

        private LoaderResult LoadFromCache(string pkHash)
        {
            try
            {
                var cacheFile = GetCachePath(pkHash);
                var metaFile = cacheFile + ".meta";

                if (!File.Exists(cacheFile) || !File.Exists(metaFile))
                    return null;

                var metaJson = File.ReadAllText(metaFile);
                var result = new LoaderResult
                {
                    Id = ParseJsonInt(metaJson, "Id"),
                    Filename = ParseJsonString(metaJson, "Filename"),
                    Vendor = ParseJsonString(metaJson, "Vendor"),
                    Chip = ParseJsonString(metaJson, "Chip"),
                    AuthType = ParseJsonString(metaJson, "AuthType"),
                    StorageType = ParseJsonString(metaJson, "StorageType"),
                    HwId = ParseJsonString(metaJson, "HwId"),
                    PkHash = ParseJsonString(metaJson, "PkHash"),
                    MatchType = ParseJsonString(metaJson, "MatchType"),
                    Confidence = ParseJsonInt(metaJson, "Confidence"),
                    Data = File.ReadAllBytes(cacheFile)
                };

                return result;
            }
            catch
            {
                return null;
            }
        }

        private void SaveToCache(string pkHash, LoaderResult result)
        {
            try
            {
                var cacheFile = GetCachePath(pkHash);
                var metaFile = cacheFile + ".meta";

                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));

                // Keep Loader 数据
                if (result.Data != null)
                {
                    File.WriteAllBytes(cacheFile, result.Data);
                }

                // Keep元数据
                var sb = new StringBuilder();
                sb.Append("{");
                sb.AppendFormat("\"Id\":{0}", result.Id);
                sb.AppendFormat(",\"Filename\":\"{0}\"", EscapeJson(result.Filename ?? ""));
                sb.AppendFormat(",\"Vendor\":\"{0}\"", EscapeJson(result.Vendor ?? ""));
                sb.AppendFormat(",\"Chip\":\"{0}\"", EscapeJson(result.Chip ?? ""));
                sb.AppendFormat(",\"AuthType\":\"{0}\"", EscapeJson(result.AuthType ?? ""));
                sb.AppendFormat(",\"StorageType\":\"{0}\"", EscapeJson(result.StorageType ?? ""));
                sb.AppendFormat(",\"HwId\":\"{0}\"", EscapeJson(result.HwId ?? ""));
                sb.AppendFormat(",\"PkHash\":\"{0}\"", EscapeJson(result.PkHash ?? ""));
                sb.AppendFormat(",\"MatchType\":\"{0}\"", EscapeJson(result.MatchType ?? ""));
                sb.AppendFormat(",\"Confidence\":{0}", result.Confidence);
                sb.Append("}");

                File.WriteAllText(metaFile, sb.ToString());

                LogDetail(string.Format("Cached: {0}", result.Filename));
            }
            catch (Exception ex)
            {
                LogDetail(string.Format("Keep cache fail: {0}", ex.Message));
            }
        }

        private string GetCachePath(string pkHash)
        {
            // Use PK Hash 前 8 位作为子Table of contents
            var subDir = pkHash.Length >= 8 ? pkHash.Substring(0, 8) : pkHash;
            return Path.Combine(CacheDirectory, subDir, pkHash + ".bin");
        }

        private void Log(string message)
        {
            if (_log != null)
                _log(message);
        }

        private void LogDetail(string message)
        {
            if (_logDetail != null)
                _logDetail(message);
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Loader matchresult
    /// </summary>
    public class LoaderResult
    {
        public int Id { get; set; }
        public string Filename { get; set; }
        public string Vendor { get; set; }
        public string Chip { get; set; }
        public string AuthType { get; set; }
        public string StorageType { get; set; }
        public string HwId { get; set; }
        public string PkHash { get; set; }
        public string MatchType { get; set; }
        public int Confidence { get; set; }
        public byte[] Data { get; set; }
    }

    #endregion
}
