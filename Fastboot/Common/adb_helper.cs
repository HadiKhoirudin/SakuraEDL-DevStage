
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Common
{
    /// <summary>
    /// ADB Command Execution Helper Class
    /// Depends on external adb.exe to execute commands
    /// </summary>
    public static class AdbHelper
    {
        // ADB executable path
        private static string _adbPath = null;

        /// <summary>
        /// Get ADB path (prioritize adb.exe in the application directory)
        /// </summary>
        public static string GetAdbPath()
        {
            if (_adbPath != null)
                return _adbPath;

            // 1. Prioritize adb.exe in the application directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAdb = Path.Combine(appDir, "adb.exe");
            if (File.Exists(localAdb))
            {
                _adbPath = localAdb;
                return _adbPath;
            }

            // 2. Try platform-tools subdirectory
            string platformTools = Path.Combine(appDir, "platform-tools", "adb.exe");
            if (File.Exists(platformTools))
            {
                _adbPath = platformTools;
                return _adbPath;
            }

            // 3. Assume adb is in the system PATH
            _adbPath = "adb";
            return _adbPath;
        }

        /// <summary>
        /// Check if ADB is available
        /// </summary>
        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                var result = await ExecuteAsync("version", 5000);
                return result.ExitCode == 0 && result.Output.Contains("Android Debug Bridge");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Execute ADB command
        /// </summary>
        /// <param name="arguments">ADB command parameters</param>
        /// <param name="timeoutMs">Timeout (milliseconds)</param>
        /// <param name="ct">Cancellation token</param>
        public static async Task<AdbResult> ExecuteAsync(string arguments, int timeoutMs = 10000, CancellationToken ct = default)
        {
            var result = new AdbResult();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GetAdbPath(),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                using (var process = new Process { StartInfo = psi })
                {
                    var outputBuilder = new System.Text.StringBuilder();
                    var errorBuilder = new System.Text.StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            outputBuilder.AppendLine(e.Data);
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            errorBuilder.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait for process completion or timeout
                    var completed = await Task.Run(() => process.WaitForExit(timeoutMs), ct);

                    if (!completed)
                    {
                        try { process.Kill(); } catch { }
                        result.ExitCode = -1;
                        result.Error = "Command execution timed out";
                        return result;
                    }

                    result.ExitCode = process.ExitCode;
                    result.Output = outputBuilder.ToString().Trim();
                    result.Error = errorBuilder.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Error = $"Execution failed: {ex.Message}";
            }

            return result;
        }

        #region Shortcut Methods

        /// <summary>
        /// Reboot to system
        /// </summary>
        public static Task<AdbResult> RebootAsync(CancellationToken ct = default)
            => ExecuteAsync("reboot", 10000, ct);

        /// <summary>
        /// Reboot to Bootloader (Fastboot)
        /// </summary>
        public static Task<AdbResult> RebootBootloaderAsync(CancellationToken ct = default)
            => ExecuteAsync("reboot bootloader", 10000, ct);

        /// <summary>
        /// Reboot to Fastbootd
        /// </summary>
        public static Task<AdbResult> RebootFastbootAsync(CancellationToken ct = default)
            => ExecuteAsync("reboot fastboot", 10000, ct);

        /// <summary>
        /// Reboot to Recovery
        /// </summary>
        public static Task<AdbResult> RebootRecoveryAsync(CancellationToken ct = default)
            => ExecuteAsync("reboot recovery", 10000, ct);

        /// <summary>
        /// Reboot to EDL mode
        /// </summary>
        public static Task<AdbResult> RebootEdlAsync(CancellationToken ct = default)
            => ExecuteAsync("reboot edl", 10000, ct);

        /// <summary>
        /// Get device list
        /// </summary>
        public static Task<AdbResult> DevicesAsync(CancellationToken ct = default)
            => ExecuteAsync("devices", 5000, ct);

        /// <summary>
        /// Get device state
        /// </summary>
        public static Task<AdbResult> GetStateAsync(CancellationToken ct = default)
            => ExecuteAsync("get-state", 5000, ct);

        /// <summary>
        /// Execute shell command
        /// </summary>
        public static Task<AdbResult> ShellAsync(string command, int timeoutMs = 30000, CancellationToken ct = default)
            => ExecuteAsync($"shell {command}", timeoutMs, ct);

        #endregion
    }

    /// <summary>
    /// ADB Command Execution Result
    /// </summary>
    public class AdbResult
    {
        /// <summary>
        /// Exit code (0 = success)
        /// </summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>
        /// Standard output
        /// </summary>
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// Error output
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Whether successful
        /// </summary>
        public bool Success => ExitCode == 0;

        /// <summary>
        /// Get full output (stdout + stderr)
        /// </summary>
        public string FullOutput => string.IsNullOrEmpty(Error) ? Output : $"{Output}\n{Error}".Trim();
    }
}
