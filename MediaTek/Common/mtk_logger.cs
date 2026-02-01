// ============================================================================
// LoveAlways - MediaTek Logging System
// MediaTek Logging System with Formatting
// ============================================================================
// Unified logging format output, supports multiple log levels and styles
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Collections.Generic;
using System.Text;

namespace LoveAlways.MediaTek.Common
{
    /// <summary>
    /// Log Level
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Debug information</summary>
        Debug = 0,
        
        /// <summary>Detailed information</summary>
        Verbose = 1,
        
        /// <summary>General information</summary>
        Info = 2,
        
        /// <summary>Success message</summary>
        Success = 3,
        
        /// <summary>Warning message</summary>
        Warning = 4,
        
        /// <summary>Error message</summary>
        Error = 5,
        
        /// <summary>Critical error</summary>
        Critical = 6
    }

    /// <summary>
    /// Log Category
    /// </summary>
    public enum LogCategory
    {
        General,      // General
        Brom,         // BROM Protocol
        Da,           // DA Protocol
        XFlash,       // XFlash (V5)
        Xml,          // XML (V6)
        Exploit,      // Exploit
        Security,     // Security related
        Device,       // Device operation
        Network,      // Network/Serial
        Protocol      // Protocol layer
    }

    /// <summary>
    /// MTK Log Entry
    /// </summary>
    public class MtkLogEntry
    {
        /// <summary>Timestamp</summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>Log level</summary>
        public LogLevel Level { get; set; }
        
        /// <summary>Log category</summary>
        public LogCategory Category { get; set; }
        
        /// <summary>Message content</summary>
        public string Message { get; set; }
        
        /// <summary>Attached data</summary>
        public object Data { get; set; }
        
        /// <summary>Exception information</summary>
        public Exception Exception { get; set; }

        public MtkLogEntry()
        {
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// MTK Logger
    /// </summary>
    public class MtkLogger
    {
        private readonly string _name;
        private readonly List<MtkLogEntry> _history;
        private readonly Action<string> _outputHandler;
        private LogLevel _minLevel;
        private bool _showTimestamp;
        private bool _showCategory;
        private bool _useColors;

        #region Constructor

        public MtkLogger(string name = "MTK", Action<string> outputHandler = null)
        {
            _name = name;
            _outputHandler = outputHandler ?? Console.WriteLine;
            _history = new List<MtkLogEntry>();
            _minLevel = LogLevel.Info;
            _showTimestamp = true;
            _showCategory = true;
            _useColors = false;  // Do not use colors by default (compatibility)
        }

        #endregion

        #region Configuration Methods

        /// <summary>Set minimum log level</summary>
        public MtkLogger SetMinLevel(LogLevel level)
        {
            _minLevel = level;
            return this;
        }

        /// <summary>Set whether to show timestamp</summary>
        public MtkLogger ShowTimestamp(bool show)
        {
            _showTimestamp = show;
            return this;
        }

        /// <summary>Set whether to show category</summary>
        public MtkLogger ShowCategory(bool show)
        {
            _showCategory = show;
            return this;
        }

        /// <summary>Set whether to use colors (requires terminal support)</summary>
        public MtkLogger UseColors(bool use)
        {
            _useColors = use;
            return this;
        }

        #endregion

        #region Log Recording Methods

        /// <summary>Record debug log</summary>
        public void Debug(string message, LogCategory category = LogCategory.General)
        {
            Log(LogLevel.Debug, category, message);
        }

        /// <summary>Record verbose log</summary>
        public void Verbose(string message, LogCategory category = LogCategory.General)
        {
            Log(LogLevel.Verbose, category, message);
        }

        /// <summary>Record info log</summary>
        public void Info(string message, LogCategory category = LogCategory.General)
        {
            Log(LogLevel.Info, category, message);
        }

        /// <summary>Record success log</summary>
        public void Success(string message, LogCategory category = LogCategory.General)
        {
            Log(LogLevel.Success, category, message);
        }

        /// <summary>Record warning log</summary>
        public void Warning(string message, LogCategory category = LogCategory.General)
        {
            Log(LogLevel.Warning, category, message);
        }

        /// <summary>Record error log</summary>
        public void Error(string message, LogCategory category = LogCategory.General, Exception ex = null)
        {
            var entry = new MtkLogEntry
            {
                Level = LogLevel.Error,
                Category = category,
                Message = message,
                Exception = ex
            };
            LogEntry(entry);
        }

        /// <summary>Record critical log</summary>
        public void Critical(string message, LogCategory category = LogCategory.General, Exception ex = null)
        {
            var entry = new MtkLogEntry
            {
                Level = LogLevel.Critical,
                Category = category,
                Message = message,
                Exception = ex
            };
            LogEntry(entry);
        }

        /// <summary>Record basic log</summary>
        public void Log(LogLevel level, LogCategory category, string message, object data = null)
        {
            var entry = new MtkLogEntry
            {
                Level = level,
                Category = category,
                Message = message,
                Data = data
            };
            LogEntry(entry);
        }

        #endregion

        #region Special Format Logs

        /// <summary>Record hexadecimal data</summary>
        public void LogHex(string label, byte[] data, int maxLength = 32, LogLevel level = LogLevel.Verbose)
        {
            if (data == null || level < _minLevel) return;

            var hex = BytesToHex(data, maxLength);
            var msg = $"{label}: {hex}";
            if (data.Length > maxLength)
                msg += $" ... ({data.Length} bytes)";
            
            Log(level, LogCategory.Protocol, msg);
        }

        /// <summary>Record protocol command</summary>
        public void LogCommand(string command, uint cmdCode, LogCategory category = LogCategory.Protocol)
        {
            var msg = $"→ {command} (0x{cmdCode:X8})";
            Log(LogLevel.Info, category, msg);
        }

        /// <summary>Record protocol response</summary>
        public void LogResponse(string response, uint statusCode, LogCategory category = LogCategory.Protocol)
        {
            var level = (statusCode == 0) ? LogLevel.Success : LogLevel.Warning;
            var msg = $"← {response} (0x{statusCode:X8})";
            Log(level, category, msg);
        }

        /// <summary>Record error code</summary>
        public void LogErrorCode(uint errorCode, LogCategory category = LogCategory.General)
        {
            var formatted = MtkErrorCodes.FormatError(errorCode);
            var level = MtkErrorCodes.IsError(errorCode) ? LogLevel.Error : LogLevel.Warning;
            Log(level, category, formatted);
        }

        /// <summary>Record progress</summary>
        public void LogProgress(string operation, int current, int total, LogCategory category = LogCategory.General)
        {
            var percentage = total > 0 ? (current * 100 / total) : 0;
            var msg = $"{operation}: {current}/{total} ({percentage}%)";
            Log(LogLevel.Info, category, msg);
        }

        /// <summary>Record device info</summary>
        public void LogDeviceInfo(string key, object value, LogCategory category = LogCategory.Device)
        {
            var msg = $"  {key,-20}: {value}";
            Log(LogLevel.Info, category, msg);
        }

        /// <summary>Record separator line</summary>
        public void LogSeparator(char character = '=', int length = 60)
        {
            var line = new string(character, length);
            _outputHandler?.Invoke(line);
        }

        /// <summary>Record header</summary>
        public void LogHeader(string title, char borderChar = '=')
        {
            int totalWidth = 60;
            int padding = (totalWidth - title.Length - 2) / 2;
            
            LogSeparator(borderChar, totalWidth);
            var header = new string(' ', padding) + title + new string(' ', padding);
            if (header.Length < totalWidth) header += " ";
            _outputHandler?.Invoke(header);
            LogSeparator(borderChar, totalWidth);
        }

        #endregion

        #region Formatted Output

        private void LogEntry(MtkLogEntry entry)
        {
            if (entry.Level < _minLevel) return;

            // Add to history
            _history.Add(entry);

            // Format and output
            var formatted = FormatEntry(entry);
            _outputHandler?.Invoke(formatted);

            // If there is an exception, output exception details
            if (entry.Exception != null && entry.Level >= LogLevel.Error)
            {
                var exDetails = FormatException(entry.Exception);
                _outputHandler?.Invoke(exDetails);
            }
        }

        private string FormatEntry(MtkLogEntry entry)
        {
            var sb = new StringBuilder();

            // Timestamp
            if (_showTimestamp)
            {
                sb.Append($"[{entry.Timestamp:HH:mm:ss.fff}] ");
            }

            // Log level
            var levelStr = GetLevelString(entry.Level);
            sb.Append(levelStr);

            // Category
            if (_showCategory && entry.Category != LogCategory.General)
            {
                sb.Append($" [{entry.Category}]");
            }

            // Message
            sb.Append(" ");
            sb.Append(entry.Message);

            return sb.ToString();
        }

        private string GetLevelString(LogLevel level)
        {
            if (_useColors)
            {
                return level switch
                {
                    LogLevel.Debug => "[DBG]",
                    LogLevel.Verbose => "[VRB]",
                    LogLevel.Info => "[INF]",
                    LogLevel.Success => "[✓]",
                    LogLevel.Warning => "[WRN]",
                    LogLevel.Error => "[ERR]",
                    LogLevel.Critical => "[!!!]",
                    _ => "[???]"
                };
            }
            else
            {
                return level switch
                {
                    LogLevel.Debug => "[DEBUG]",
                    LogLevel.Verbose => "[VERBOSE]",
                    LogLevel.Info => "[INFO]",
                    LogLevel.Success => "[SUCCESS]",
                    LogLevel.Warning => "[WARNING]",
                    LogLevel.Error => "[ERROR]",
                    LogLevel.Critical => "[CRITICAL]",
                    _ => "[UNKNOWN]"
                };
            }
        }

        private string FormatException(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("  Exception Details:");
            sb.AppendLine($"    Type: {ex.GetType().Name}");
            sb.AppendLine($"    Message: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine("    Stack Trace:");
                var lines = ex.StackTrace.Split('\n');
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"      {line.Trim()}");
                }
            }
            if (ex.InnerException != null)
            {
                sb.AppendLine("  Inner Exception:");
                sb.Append(FormatException(ex.InnerException));
            }
            return sb.ToString();
        }

        #endregion

        #region Helper Methods

        /// <summary>Convert byte array to hex string</summary>
        private string BytesToHex(byte[] data, int maxLength)
        {
            if (data == null || data.Length == 0)
                return "[]";

            int length = Math.Min(data.Length, maxLength);
            var sb = new StringBuilder();
            
            for (int i = 0; i < length; i++)
            {
                sb.Append($"{data[i]:X2}");
                if (i < length - 1)
                    sb.Append(" ");
            }

            return sb.ToString();
        }

        /// <summary>Get history</summary>
        public IReadOnlyList<MtkLogEntry> GetHistory()
        {
            return _history.AsReadOnly();
        }

        /// <summary>Clear history</summary>
        public void ClearHistory()
        {
            _history.Clear();
        }

        /// <summary>Export log to file</summary>
        public void ExportToFile(string filePath)
        {
            var lines = new List<string>();
            foreach (var entry in _history)
            {
                lines.Add(FormatEntry(entry));
            }
            System.IO.File.WriteAllLines(filePath, lines);
        }

        #endregion
    }

    /// <summary>
    /// Global Log Instance
    /// </summary>
    public static class MtkLog
    {
        private static MtkLogger _instance;
        private static readonly object _lock = new object();

        /// <summary>Get global log instance</summary>
        public static MtkLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MtkLogger("MTK")
                                .SetMinLevel(LogLevel.Info)
                                .ShowTimestamp(true)
                                .ShowCategory(true);
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>Set custom log instance</summary>
        public static void SetInstance(MtkLogger logger)
        {
            lock (_lock)
            {
                _instance = logger;
            }
        }

        /// <summary>Reset to default instance</summary>
        public static void ResetInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        // Convenient methods
        public static void Debug(string message) => Instance.Debug(message);
        public static void Verbose(string message) => Instance.Verbose(message);
        public static void Info(string message) => Instance.Info(message);
        public static void Success(string message) => Instance.Success(message);
        public static void Warning(string message) => Instance.Warning(message);
        public static void Error(string message, Exception ex = null) => Instance.Error(message, LogCategory.General, ex);
        public static void Critical(string message, Exception ex = null) => Instance.Critical(message, LogCategory.General, ex);
    }

    /// <summary>
    /// Formatted log builder (chained calls)
    /// </summary>
    public class MtkLogBuilder
    {
        private readonly MtkLogger _logger;
        private LogLevel _level = LogLevel.Info;
        private LogCategory _category = LogCategory.General;
        private readonly StringBuilder _message = new StringBuilder();
        private object _data;
        private Exception _exception;

        public MtkLogBuilder(MtkLogger logger)
        {
            _logger = logger;
        }

        public MtkLogBuilder Level(LogLevel level)
        {
            _level = level;
            return this;
        }

        public MtkLogBuilder Category(LogCategory category)
        {
            _category = category;
            return this;
        }

        public MtkLogBuilder Message(string msg)
        {
            _message.Clear();
            _message.Append(msg);
            return this;
        }

        public MtkLogBuilder Append(string text)
        {
            _message.Append(text);
            return this;
        }

        public MtkLogBuilder AppendLine(string text = "")
        {
            _message.AppendLine(text);
            return this;
        }

        public MtkLogBuilder Data(object data)
        {
            _data = data;
            return this;
        }

        public MtkLogBuilder Exception(Exception ex)
        {
            _exception = ex;
            return this;
        }

        public void Write()
        {
            if (_exception != null)
            {
                _logger.Error(_message.ToString(), _category, _exception);
            }
            else
            {
                _logger.Log(_level, _category, _message.ToString(), _data);
            }
        }
    }
}
