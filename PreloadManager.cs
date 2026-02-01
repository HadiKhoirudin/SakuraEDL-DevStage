using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Database;
using OPFlashTool.Services;

namespace LoveAlways
{
    /// <summary>
    /// Preload Manager - Optimized version, supports lazy loading to reduce memory usage
    /// EDL Loader has been changed to cloud auto-match, no longer preloads local PAK
    /// </summary>
    public static class PreloadManager
    {
        // Preload Status
        public static bool IsPreloadComplete { get; private set; } = false;
        public static string CurrentStatus { get; private set; } = "Preparing...";
        public static int Progress { get; private set; } = 0;

        // Lazy loaded data - load on demand
        private static List<string> _edlLoaderItems = null;
        private static List<string> _vipLoaderItems = null;
        private static bool _edlLoaderItemsLoaded = false;
        private static bool _vipLoaderItemsLoaded = false;
        private static readonly object _loaderLock = new object();

        /// <summary>
        /// EDL Loader List (Deprecated, use cloud match)
        /// </summary>
        [Obsolete("Use cloud auto-match")]
        public static List<string> EdlLoaderItems
        {
            get
            {
                // Returns empty list, EDL Loader now uses cloud match
                return new List<string>();
            }
        }
        
        /// <summary>
        /// VIP Loader List (Still loaded from local PAK)
        /// </summary>
        public static List<string> VipLoaderItems
        {
            get
            {
                if (!_vipLoaderItemsLoaded)
                {
                    lock (_loaderLock)
                    {
                        if (!_vipLoaderItemsLoaded)
                        {
                            _vipLoaderItems = BuildVipLoaderItems();
                            _vipLoaderItemsLoaded = true;
                        }
                    }
                }
                return _vipLoaderItems;
            }
        }

        private static string _systemInfo = null;
        public static string SystemInfo
        {
            get
            {
                if (_systemInfo == null)
                {
                    try 
                    { 
                        // Use Task.Run to avoid deadlock
                        _systemInfo = Task.Run(async () => 
                            await WindowsInfo.GetSystemInfoAsync().ConfigureAwait(false)
                        ).GetAwaiter().GetResult(); 
                    }
                    catch (Exception ex)
                    { 
                        System.Diagnostics.Debug.WriteLine($"[PreloadManager] Failed to get system info: {ex.Message}");
                        _systemInfo = "Unknown"; 
                    }
                }
                return _systemInfo;
            }
        }

        private static bool? _edlPakAvailable = null;
        
        /// <summary>
        /// Whether EDL PAK is available (Deprecated, use cloud match)
        /// </summary>
        [Obsolete("Use cloud auto-match")]
        public static bool EdlPakAvailable
        {
            get
            {
                // Always return false, force use cloud match
                return false;
            }
        }
        
        private static bool? _vipPakAvailable = null;
        public static bool VipPakAvailable
        {
            get
            {
                if (!_vipPakAvailable.HasValue)
                {
                    _vipPakAvailable = ChimeraSignDatabase.IsLoaderPackAvailable();
                }
                return _vipPakAvailable.Value;
            }
        }

        // Preload Task
        private static Task _preloadTask = null;

        // Whether to enable lazy loading mode (use PerformanceConfig)
        private static bool EnableLazyLoading => Common.PerformanceConfig.EnableLazyLoading;

        /// <summary>
        /// Start Preload (Called in SplashForm)
        /// Optimized version: Only load necessary resources, others on demand
        /// EDL Loader changed to cloud auto-match, no longer preloaded
        /// </summary>
        public static void StartPreload()
        {
            if (_preloadTask != null) return;

            _preloadTask = Task.Run(async () =>
            {
                try
                {
                    // Phase 0: Extract embedded tool files (Required)
                    CurrentStatus = "Extracting tool files...";
                    Progress = 10;
                    EmbeddedResourceExtractor.ExtractAll();
                    Progress = 30;

                    // Phase 1: Check VIP PAK (Fast) - EDL changed to cloud match
                    CurrentStatus = "Checking resource packs...";
                    Progress = 40;
                    _vipPakAvailable = ChimeraSignDatabase.IsLoaderPackAvailable();
                    Progress = 50;

                    // Lazy loading mode: skip preloading system info
                    if (!EnableLazyLoading)
                    {
                        // Phase 2: Preload VIP Loader list (if available)
                        if (_vipPakAvailable.Value)
                        {
                            CurrentStatus = "Loading VIP boot database...";
                            Progress = 60;
                            _vipLoaderItems = BuildVipLoaderItems();
                            _vipLoaderItemsLoaded = true;
                        }
                        Progress = 70;

                        // Phase 3: Preload system info
                        CurrentStatus = "Getting system info...";
                        Progress = 80;
                        try { _systemInfo = await WindowsInfo.GetSystemInfoAsync(); }
                        catch { _systemInfo = "Unknown"; }
                    }
                    
                    Progress = 90;

                    // Phase 4: Prewarm common types (Lightweight)
                    CurrentStatus = "Initializing components...";
                    PrewarmTypesLight();

                    // Complete
                    Progress = 100;
                    IsPreloadComplete = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Preload failed: {ex.Message}");
                    Progress = 100;
                    IsPreloadComplete = true;
                }
            });
        }

        /// <summary>
        /// Release cache to reduce memory usage
        /// </summary>
        public static void ClearCache()
        {
            lock (_loaderLock)
            {
                _edlLoaderItems?.Clear();
                _edlLoaderItems = null;
                _edlLoaderItemsLoaded = false;
                
                _vipLoaderItems?.Clear();
                _vipLoaderItems = null;
                _vipLoaderItemsLoaded = false;
            }
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        /// <summary>
        /// Wait for preload to complete
        /// </summary>
        public static async Task WaitForPreloadAsync()
        {
            if (_preloadTask != null)
            {
                await _preloadTask;
            }
        }

        /// <summary>
        /// Build EDL Loader list items (Deprecated, use cloud match)
        /// </summary>
        [Obsolete("Use cloud auto-match")]
        private static List<string> BuildEdlLoaderItems()
        {
            // No longer builds local PAK list, EDL Loader uses cloud match
            return new List<string>();
        }
        
        /// <summary>
        /// Build VIP Loader list items (OPLUS signature authentication devices)
        /// </summary>
        private static List<string> BuildVipLoaderItems()
        {
            var items = new List<string>();

            try
            {
                if (!ChimeraSignDatabase.IsLoaderPackAvailable())
                    return items;

                // Get all VIP platforms
                var platforms = ChimeraSignDatabase.GetSupportedPlatforms();
                if (platforms == null || platforms.Length == 0)
                    return items;

                items.Add("─── VIP Signed Devices ───");
                
                foreach (var platform in platforms)
                {
                    if (ChimeraSignDatabase.TryGet(platform, out var signData))
                    {
                        items.Add($"[VIP] {signData.Name} ({platform})");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VIP Loader build failed: {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// Get Brand Display Name (Internationalized)
        /// </summary>
        private static string GetBrandDisplayName(string brand)
        {
            switch (brand.ToLower())
            {
                case "huawei": return "Huawei/Honor";
                case "zte": return "ZTE/Nubia/RedMagic";
                case "xiaomi": return "Xiaomi/Redmi";
                case "blackshark": return "BlackShark";
                case "vivo": return "Vivo/iQOO";
                case "meizu": return "Meizu";
                case "lenovo": return "Lenovo/Motorola";
                case "samsung": return "Samsung";
                case "nothing": return "Nothing";
                case "rog": return "ASUS ROG";
                case "lg": return "LG";
                case "smartisan": return "Smartisan";
                case "xtc": return "XTC";
                case "360": return "360";
                case "bbk": return "BBK";
                case "royole": return "Royole";
                case "oplus": return "OPPO/OnePlus/Realme";
                default: return brand;
            }
        }

        /// <summary>
        /// Prewarm common types to avoid JIT compilation delay during first use
        /// </summary>
        private static void PrewarmTypes()
        {
            try
            {
                // 预热 UI 相关类型
                var _ = typeof(AntdUI.Select);
                var __ = typeof(Sunny.UI.UIButton);
                var ___ = typeof(System.Windows.Forms.ListView);

                // 预热 IO 相关
                var ____ = typeof(System.IO.FileStream);
                var _____ = typeof(System.IO.MemoryStream);

                // 预热网络相关
                var ______ = typeof(System.Net.Http.HttpClient);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreloadManager] Type prewarm failed (Non-critical): {ex.Message}");
            }
        }

        /// <summary>
        /// Lightweight prewarm - only prewarm core types to reduce memory usage
        /// </summary>
        private static void PrewarmTypesLight()
        {
            try
            {
                // 仅预热最核心的 IO 类型
                var _ = typeof(System.IO.FileStream);
                var __ = typeof(System.Windows.Forms.ListView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreloadManager] Lightweight prewarm failed (Non-critical): {ex.Message}");
            }
        }
    }
}
