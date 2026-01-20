using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Database;
using OPFlashTool.Services;

namespace LoveAlways
{
    /// <summary>
    /// 预加载管理器 - 在启动动画期间后台预加载模块
    /// </summary>
    public static class PreloadManager
    {
        // 预加载状态
        public static bool IsPreloadComplete { get; private set; } = false;
        public static string CurrentStatus { get; private set; } = "准备中...";
        public static int Progress { get; private set; } = 0;

        // 预加载数据
        public static List<string> EdlLoaderItems { get; private set; } = null;
        public static string SystemInfo { get; private set; } = null;
        public static bool EdlPakAvailable { get; private set; } = false;

        // 预加载任务
        private static Task _preloadTask = null;

        /// <summary>
        /// 启动预加载（在 SplashForm 中调用）
        /// </summary>
        public static void StartPreload()
        {
            if (_preloadTask != null) return;

            _preloadTask = Task.Run(async () =>
            {
                try
                {
                    // 阶段1: 检查 EDL PAK
                    CurrentStatus = "检查资源包...";
                    Progress = 10;
                    await Task.Delay(50); // 让状态有时间更新
                    EdlPakAvailable = EdlLoaderDatabase.IsPakAvailable();

                    // 阶段2: 预加载 EDL Loader 列表
                    if (EdlPakAvailable)
                    {
                        CurrentStatus = "加载 EDL 引导数据库...";
                        Progress = 20;
                        EdlLoaderItems = BuildEdlLoaderItems();
                    }
                    Progress = 50;

                    // 阶段3: 预加载系统信息
                    CurrentStatus = "获取系统信息...";
                    Progress = 60;
                    try
                    {
                        SystemInfo = await WindowsInfo.GetSystemInfoAsync();
                    }
                    catch { SystemInfo = "未知"; }
                    Progress = 80;

                    // 阶段4: 预热常用类型
                    CurrentStatus = "初始化组件...";
                    Progress = 90;
                    PrewarmTypes();

                    // 完成
                    CurrentStatus = "加载完成";
                    Progress = 100;
                    IsPreloadComplete = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"预加载失败: {ex.Message}");
                    CurrentStatus = "加载完成";
                    Progress = 100;
                    IsPreloadComplete = true;
                }
            });
        }

        /// <summary>
        /// 等待预加载完成
        /// </summary>
        public static async Task WaitForPreloadAsync()
        {
            if (_preloadTask != null)
            {
                await _preloadTask;
            }
        }

        /// <summary>
        /// 构建 EDL Loader 列表项
        /// </summary>
        private static List<string> BuildEdlLoaderItems()
        {
            var items = new List<string>(300);

            try
            {
                if (!EdlLoaderDatabase.IsPakAvailable())
                    return items;

                var brands = EdlLoaderDatabase.GetBrands();
                if (brands.Length == 0)
                    return items;

                foreach (var brand in brands)
                {
                    var loaders = EdlLoaderDatabase.GetByBrand(brand);
                    if (loaders.Length == 0) continue;

                    string brandName = GetBrandDisplayName(brand);
                    items.Add($"─── {brandName} ({loaders.Length}) ───");

                    // 先添加通用 loader
                    foreach (var loader in loaders)
                    {
                        if (loader.IsCommon)
                        {
                            string chip = string.IsNullOrEmpty(loader.Chip) ? "" : $" {loader.Chip}";
                            items.Add($"[{brand}]{chip} (通用)");
                        }
                    }

                    // 再添加专用 loader
                    foreach (var loader in loaders)
                    {
                        if (!loader.IsCommon)
                        {
                            string shortName = loader.Name.Replace($"{brand} ", "");
                            items.Add($"[{brand}] {shortName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EDL Loader 构建失败: {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// 获取品牌中文显示名
        /// </summary>
        private static string GetBrandDisplayName(string brand)
        {
            switch (brand.ToLower())
            {
                case "huawei": return "华为/荣耀";
                case "zte": return "中兴/努比亚/红魔";
                case "xiaomi": return "小米/Redmi";
                case "blackshark": return "黑鲨";
                case "vivo": return "vivo/iQOO";
                case "meizu": return "魅族";
                case "lenovo": return "联想/摩托罗拉";
                case "samsung": return "三星";
                case "nothing": return "Nothing";
                case "rog": return "华硕ROG";
                case "lg": return "LG";
                case "smartisan": return "锤子";
                case "xtc": return "小天才";
                case "360": return "360";
                case "bbk": return "BBK";
                case "royole": return "柔宇";
                case "oplus": return "OPPO/OnePlus/Realme";
                default: return brand;
            }
        }

        /// <summary>
        /// 预热常用类型，避免首次使用时 JIT 编译延迟
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
            catch { }
        }
    }
}
