// ============================================================================
// LoveAlways - Global Logger
// Global Logger - Unified log output and management
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace LoveAlways.Common
{
    /// <summary>
    /// Log Level
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>
    /// Global Logger - Unified log output
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static LogLevel _minLevel = LogLevel.Debug;
        private static Action<string, Color?> _uiLogger;
        private static bool _isInitialized;

        /// <summary>
        /// Minimum Log Level (Logs below this level will not be output)
        /// </summary>
        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        /// <summary>
        /// Initialize Log System
        /// </summary>
        /// <param name="logFilePath">Log File Path</param>
        /// <param name="uiLogger">UI Log Callback (Optional)</param>
        public static void Initialize(string logFilePath, Action<string, Color?> uiLogger = null)
        {
            lock (_lock)
            {
                _logFilePath = logFilePath;
                _uiLogger = uiLogger;
                _isInitialized = true;

                // Ensure log directory exists
                try
                {
                    string dir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Logger] Failed to create log directory: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Set UI Log Callback
        /// </summary>
        public static void SetUILogger(Action<string, Color?> uiLogger)
        {
            lock (_lock)
            {
                _uiLogger = uiLogger;
            }
        }

        /// <summary>
        /// Record Debug Log (Write to file only)
        /// </summary>
        public static void Debug(string message, string category = null)
        {
            Log(LogLevel.Debug, message, category, null, false);
        }

        /// <summary>
        /// Record Info Log
        /// </summary>
        public static void Info(string message, string category = null, bool showInUI = true)
        {
            Log(LogLevel.Info, message, category, null, showInUI);
        }

        /// <summary>
        /// Record Warning Log
        /// </summary>
        public static void Warning(string message, string category = null, bool showInUI = true)
        {
            Log(LogLevel.Warning, message, category, Color.Orange, showInUI);
        }

        /// <summary>
        /// Record Error Log
        /// </summary>
        public static void Error(string message, string category = null, bool showInUI = true)
        {
            Log(LogLevel.Error, message, category, Color.Red, showInUI);
        }

        /// <summary>
        /// Record Error Log (With Exception)
        /// </summary>
        public static void Error(string message, Exception ex, string category = null, bool showInUI = true)
        {
            string fullMessage = $"{message}: {ex.Message}";
            Log(LogLevel.Error, fullMessage, category, Color.Red, showInUI);

            // Write detailed stack trace to file
            WriteToFile(LogLevel.Error, $"Exception Details: {ex}", category);
        }

        /// <summary>
        /// Record Fatal Error Log
        /// </summary>
        public static void Fatal(string message, Exception ex = null, string category = null)
        {
            string fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
            Log(LogLevel.Fatal, fullMessage, category, Color.DarkRed, true);

            if (ex != null)
            {
                WriteToFile(LogLevel.Fatal, $"Fatal Exception Details: {ex}", category);
            }
        }

        /// <summary>
        /// Core Log Method
        /// </summary>
        private static void Log(LogLevel level, string message, string category, Color? color, bool showInUI)
        {
            if (level < _minLevel)
                return;

            string formattedMessage = FormatMessage(level, message, category);

            // Write to file
            WriteToFile(level, message, category);

            // Output to debug window
            System.Diagnostics.Debug.WriteLine(formattedMessage);

            // Output to UI
            if (showInUI && _uiLogger != null)
            {
                try
                {
                    string uiMessage = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
                    _uiLogger(uiMessage, color);
                }
                catch
                {
                    // UI CAllback Failed, Ignore
                }
            }
        }

        /// <summary>
        /// Format Log Message
        /// </summary>
        private static string FormatMessage(LogLevel level, string message, string category)
        {
            string levelStr = level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Fatal => "FTL",
                _ => "???"
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            if (string.IsNullOrEmpty(category))
                return $"[{timestamp}] [{levelStr}] {message}";
            else
                return $"[{timestamp}] [{levelStr}] [{category}] {message}";
        }

        /// <summary>
        /// Write to log file
        /// </summary>
        private static void WriteToFile(LogLevel level, string message, string category)
        {
            if (string.IsNullOrEmpty(_logFilePath))
                return;

            try
            {
                string formattedMessage = FormatMessage(level, message, category);
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // File write failed, ignore
            }
        }

        /// <summary>
        /// Create Logger with Category
        /// </summary>
        public static CategoryLogger ForCategory(string category)
        {
            return new CategoryLogger(category);
        }
    }

    /// <summary>
    /// Logger with Category
    /// </summary>
    public class CategoryLogger
    {
        private readonly string _category;

        public CategoryLogger(string category)
        {
            _category = category;
        }

        public void Debug(string message) => Logger.Debug(message, _category);
        public void Info(string message, bool showInUI = true) => Logger.Info(message, _category, showInUI);
        public void Warning(string message, bool showInUI = true) => Logger.Warning(message, _category, showInUI);
        public void Error(string message, bool showInUI = true) => Logger.Error(message, _category, showInUI);
        public void Error(string message, Exception ex, bool showInUI = true) => Logger.Error(message, ex, _category, showInUI);
        public void Fatal(string message, Exception ex = null) => Logger.Fatal(message, ex, _category);
    }
}
