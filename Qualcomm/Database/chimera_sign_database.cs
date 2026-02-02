// ============================================================================
// LoveAlways - Chimera Signature Database
// Loader/Digest/Signature are all loaded from the resource pack (firehose.pak)
// Only supports CPAK v2 format (Loader + Digest + Signature)
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LoveAlways.Qualcomm.Database
{
    /// <summary>
    /// Chimera Signature Database
    /// </summary>
    public static class ChimeraSignDatabase
    {
        /// <summary>
        /// Signature Data Item
        /// </summary>
        public class SignatureData
        {
            public string Platform { get; set; }
            public string Name { get; set; }

            /// <summary>
            /// Load Digest from resource pack
            /// </summary>
            public byte[] Digest
            {
                get { return LoadDigest(Platform); }
            }

            /// <summary>
            /// Load Signature from resource pack
            /// </summary>
            public byte[] Signature
            {
                get { return LoadSignature(Platform); }
            }

            public int DigestSize { get { return Digest != null ? Digest.Length : 0; } }
            public int SignatureSize { get { return Signature != null ? Signature.Length : 0; } }
        }

        private static Dictionary<string, SignatureData> _database;
        private static readonly object _lock = new object();
        private static FirehosePak _pak;
        private static string _tempDir;

        public static Dictionary<string, SignatureData> Database
        {
            get
            {
                if (_database == null)
                {
                    lock (_lock)
                    {
                        if (_database == null)
                            _database = InitDatabase();
                    }
                }
                return _database;
            }
        }

        public static string[] GetSupportedPlatforms()
        {
            var platforms = new List<string>(Database.Keys);
            platforms.Sort();
            return platforms.ToArray();
        }

        public static bool IsSupported(string platform)
        {
            return Database.ContainsKey(platform);
        }

        public static SignatureData Get(string platform)
        {
            SignatureData data;
            Database.TryGetValue(platform, out data);
            return data;
        }

        public static bool TryGet(string platform, out SignatureData data)
        {
            return Database.TryGetValue(platform, out data);
        }

        private static void EnsurePak()
        {
            if (_pak == null)
            {
                string pakPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firehose.pak");
                if (File.Exists(pakPath))
                    _pak = new FirehosePak(pakPath);
            }
        }

        /// <summary>
        /// Load Loader from resource pack
        /// </summary>
        public static byte[] LoadLoader(string platform)
        {
            EnsurePak();
            return _pak != null ? _pak.GetLoader(platform) : null;
        }

        /// <summary>
        /// Load Digest from resource pack
        /// </summary>
        public static byte[] LoadDigest(string platform)
        {
            EnsurePak();
            return _pak != null ? _pak.GetDigest(platform) : null;
        }

        /// <summary>
        /// Load Signature from resource pack
        /// </summary>
        public static byte[] LoadSignature(string platform)
        {
            EnsurePak();
            return _pak != null ? _pak.GetSignature(platform) : null;
        }

        /// <summary>
        /// Get Digest file path (extracted from resource pack to temp directory)
        /// </summary>
        public static string GetDigestPath(string platform)
        {
            byte[] data = LoadDigest(platform);
            if (data == null) return null;
            return ExtractToTemp(platform, "digest", data);
        }

        /// <summary>
        /// Get Signature file path (extracted from resource pack to temp directory)
        /// </summary>
        public static string GetSignaturePath(string platform)
        {
            byte[] data = LoadSignature(platform);
            if (data == null) return null;
            return ExtractToTemp(platform, "signature", data);
        }

        private static string ExtractToTemp(string platform, string type, byte[] data)
        {
            try
            {
                if (_tempDir == null)
                    _tempDir = Path.Combine(Path.GetTempPath(), "LoveAlways_VipAuth");

                if (!Directory.Exists(_tempDir))
                    Directory.CreateDirectory(_tempDir);

                string path = Path.Combine(_tempDir, string.Format("{0}_{1}.bin", platform, type));
                File.WriteAllBytes(path, data);
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if resource pack exists
        /// </summary>
        public static bool IsLoaderPackAvailable()
        {
            string pakPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firehose.pak");
            return File.Exists(pakPath);
        }

        /// <summary>
        /// Check if VIP authentication data is available (resource pack loaded and is v2 format)
        /// </summary>
        public static bool IsVipAuthPackAvailable()
        {
            EnsurePak();
            return _pak != null && _pak.Version == 2;
        }

        /// <summary>
        /// Check if the specified platform has VIP authentication data
        /// </summary>
        public static bool HasVipAuthData(string platform)
        {
            EnsurePak();
            return _pak != null && _pak.HasVipAuth(platform);
        }

        private static Dictionary<string, SignatureData> InitDatabase()
        {
            var db = new Dictionary<string, SignatureData>(StringComparer.OrdinalIgnoreCase);

            // Platform info (Loader/Digest/Signature are all loaded from firehose.pak)
            db["SM6115"] = new SignatureData { Platform = "SM6115", Name = "Snapdragon 460" };
            db["SM6225"] = new SignatureData { Platform = "SM6225", Name = "Snapdragon 480" };
            db["SM6375"] = new SignatureData { Platform = "SM6375", Name = "Snapdragon 695/6SGen3" };
            db["SM7325"] = new SignatureData { Platform = "SM7325", Name = "Snapdragon 6G1/7SG2" };
            db["SM7450"] = new SignatureData { Platform = "SM7450", Name = "Snapdragon 7Gen1" };
            db["SM7475"] = new SignatureData { Platform = "SM7475", Name = "Snapdragon 7+Gen2" };
            db["SM7550"] = new SignatureData { Platform = "SM7550", Name = "Snapdragon 7+Gen3" };
            db["SM8350"] = new SignatureData { Platform = "SM8350", Name = "Snapdragon 888/888+" };
            db["SM8450"] = new SignatureData { Platform = "SM8450", Name = "Snapdragon 8Gen1" };
            db["SM8475"] = new SignatureData { Platform = "SM8475", Name = "Snapdragon 8+Gen1" };
            db["SM8550_1"] = new SignatureData { Platform = "SM8550_1", Name = "Snapdragon 8Gen2 V2.6" };
            db["SM8550_2"] = new SignatureData { Platform = "SM8550_2", Name = "Snapdragon 8Gen2 V2.7" };
            db["SM8650"] = new SignatureData { Platform = "SM8650", Name = "Snapdragon 8Gen3" };
            db["SM8750"] = new SignatureData { Platform = "SM8750", Name = "Snapdragon 8Elite" };
            db["SM8735"] = new SignatureData { Platform = "SM8735", Name = "Snapdragon 8SGen4" };

            return db;
        }
    }

    /// <summary>
    /// Firehose Resource Pack Reader (CPAK v2 only)
    /// 
    /// CPAK v2 format (Loader + Digest + Signature):
    /// Header: Magic(4) + Version(4) + Count(4)
    /// Entry: Platform(32) + LoaderOffset(8) + LoaderCompSize(4) + LoaderOrigSize(4) 
    ///        + DigestOffset(8) + DigestCompSize(4) + DigestOrigSize(4)
    ///        + SigOffset(8) + SigSize(4)
    /// </summary>
    internal class FirehosePak
    {
        private readonly string _pakPath;
        private readonly Dictionary<string, PakEntry> _index;
        public int Version { get; private set; }

        private struct PakEntry
        {
            // Loader
            public long LoaderOffset;
            public int LoaderCompSize;
            public int LoaderOrigSize;
            // Digest
            public long DigestOffset;
            public int DigestCompSize;
            public int DigestOrigSize;
            // Signature
            public long SignatureOffset;
            public int SignatureSize;
        }

        public FirehosePak(string pakPath)
        {
            _pakPath = pakPath;
            _index = new Dictionary<string, PakEntry>(StringComparer.OrdinalIgnoreCase);
            LoadIndex();
        }

        private void LoadIndex()
        {
            using (var fs = new FileStream(_pakPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // Magic
                byte[] magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "CPAK")
                    throw new InvalidDataException("Invalid PAK file");

                // Version (v2 supported only)
                Version = (int)br.ReadUInt32();
                if (Version != 2)
                    throw new InvalidDataException("Only CPAK v2 format is supported, current version: " + Version);

                // Count
                uint count = br.ReadUInt32();

                // Index entries (v2 format: Loader + Digest + Signature)
                for (int i = 0; i < count; i++)
                {
                    byte[] platformBytes = br.ReadBytes(32);
                    string platform = Encoding.UTF8.GetString(platformBytes).TrimEnd('\0');

                    var entry = new PakEntry();

                    // Loader
                    entry.LoaderOffset = br.ReadInt64();
                    entry.LoaderCompSize = br.ReadInt32();
                    entry.LoaderOrigSize = br.ReadInt32();

                    // Digest
                    entry.DigestOffset = br.ReadInt64();
                    entry.DigestCompSize = br.ReadInt32();
                    entry.DigestOrigSize = br.ReadInt32();

                    // Signature
                    entry.SignatureOffset = br.ReadInt64();
                    entry.SignatureSize = br.ReadInt32();

                    _index[platform] = entry;
                }
            }
        }

        public byte[] GetLoader(string platform)
        {
            if (!_index.TryGetValue(platform, out var entry))
                return null;

            if (entry.LoaderCompSize == 0)
                return null;

            return ReadAndDecompress(entry.LoaderOffset, entry.LoaderCompSize);
        }

        public byte[] GetDigest(string platform)
        {
            if (!_index.TryGetValue(platform, out var entry))
                return null;

            if (entry.DigestCompSize == 0)
                return null;

            return ReadAndDecompress(entry.DigestOffset, entry.DigestCompSize);
        }

        public byte[] GetSignature(string platform)
        {
            if (!_index.TryGetValue(platform, out var entry))
                return null;

            if (entry.SignatureSize == 0)
                return null;

            // Signature is not compressed, read directly
            using (var fs = new FileStream(_pakPath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(entry.SignatureOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.SignatureSize];
                fs.Read(data, 0, entry.SignatureSize);
                return data;
            }
        }

        private byte[] ReadAndDecompress(long offset, int compSize)
        {
            using (var fs = new FileStream(_pakPath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] compressed = new byte[compSize];
                fs.Read(compressed, 0, compSize);

                // GZip 解压
                using (var input = new MemoryStream(compressed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        public bool HasLoader(string platform)
        {
            return _index.TryGetValue(platform, out var entry) && entry.LoaderCompSize > 0;
        }

        public bool HasVipAuth(string platform)
        {
            return _index.TryGetValue(platform, out var entry) && entry.DigestCompSize > 0 && entry.SignatureSize > 0;
        }
    }
}
