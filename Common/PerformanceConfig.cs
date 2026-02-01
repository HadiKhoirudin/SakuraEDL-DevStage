// ============================================================================
// LoveAlways - Performance Configuration Manager
// Performance Configuration - Used to optimize the running experience on low-end computers
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Configuration;

namespace LoveAlways.Common
{
    /// <summary>
    /// Performance Configuration Manager - Unified performance configuration management
    /// </summary>
    public static class PerformanceConfig
    {
        private static bool? _lowPerformanceMode;
        private static int? _maxLogEntries;
        private static int? _uiRefreshInterval;
        private static bool? _enableDoubleBuffering;
        private static bool? _enableLazyLoading;

        /// <summary>
        /// Low Performance Mode - Reduce animation effects and refresh frequency
        /// </summary>
        public static bool LowPerformanceMode
        {
            get
            {
                if (!_lowPerformanceMode.HasValue)
                {
                    _lowPerformanceMode = GetBoolSetting("LowPerformanceMode", false);
                }
                return _lowPerformanceMode.Value;
            }
        }

        /// <summary>
        /// Maximum Log Entries
        /// </summary>
        public static int MaxLogEntries
        {
            get
            {
                if (!_maxLogEntries.HasValue)
                {
                    _maxLogEntries = GetIntSetting("MaxLogEntries", 1000);
                    // Limit to fewer entries in low performance mode
                    if (LowPerformanceMode && _maxLogEntries > 500)
                    {
                        _maxLogEntries = 500;
                    }
                }
                return _maxLogEntries.Value;
            }
        }

        /// <summary>
        /// UI Refresh Interval (ms)
        /// </summary>
        public static int UIRefreshInterval
        {
            get
            {
                if (!_uiRefreshInterval.HasValue)
                {
                    _uiRefreshInterval = GetIntSetting("UIRefreshInterval", 50);
                    // Use longer refresh interval in low performance mode
                    if (LowPerformanceMode && _uiRefreshInterval < 100)
                    {
                        _uiRefreshInterval = 100;
                    }
                }
                return _uiRefreshInterval.Value;
            }
        }

        /// <summary>
        /// Enable Double Buffering to reduce flickering
        /// </summary>
        public static bool EnableDoubleBuffering
        {
            get
            {
                if (!_enableDoubleBuffering.HasValue)
                {
                    _enableDoubleBuffering = GetBoolSetting("EnableDoubleBuffering", true);
                }
                return _enableDoubleBuffering.Value;
            }
        }

        /// <summary>
        /// Enable Lazy Loading
        /// </summary>
        public static bool EnableLazyLoading
        {
            get
            {
                if (!_enableLazyLoading.HasValue)
                {
                    _enableLazyLoading = GetBoolSetting("EnableLazyLoading", true);
                }
                return _enableLazyLoading.Value;
            }
        }

        /// <summary>
        /// Animation FPS (Lower in low performance mode)
        /// </summary>
        public static int AnimationFPS => LowPerformanceMode ? 15 : 30;

        /// <summary>
        /// Animation Timer Interval
        /// </summary>
        public static int AnimationInterval => 1000 / AnimationFPS;

        /// <summary>
        /// Log Batch Size
        /// </summary>
        public static int LogBatchSize => LowPerformanceMode ? 20 : 10;

        /// <summary>
        /// Get Boolean Setting Value
        /// </summary>
        private static bool GetBoolSetting(string key, bool defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrEmpty(value))
                    return defaultValue;
                return value.ToLower() == "true" || value == "1";
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Get Integer Setting Value
        /// </summary>
        private static int GetIntSetting(string key, int defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrEmpty(value))
                    return defaultValue;
                if (int.TryParse(value, out int result))
                    return result;
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reset Cache (For refreshing after configuration update)
        /// </summary>
        public static void ResetCache()
        {
            _lowPerformanceMode = null;
            _maxLogEntries = null;
            _uiRefreshInterval = null;
            _enableDoubleBuffering = null;
            _enableLazyLoading = null;
        }
    }
}
