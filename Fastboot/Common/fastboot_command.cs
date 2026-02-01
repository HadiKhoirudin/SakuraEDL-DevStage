
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Common
{
    /// <summary>
    /// Fastboot Command Executor
    /// Encapsulates the fastboot.exe command line tool
    /// </summary>
    public class FastbootCommand : IDisposable
    {
        private Process _process;
        private static string _fastbootPath;

        public StreamReader StdOut { get; private set; }
        public StreamReader StdErr { get; private set; }
        public StreamWriter StdIn { get; private set; }

        /// <summary>
        /// Set fastboot.exe path
        /// </summary>
        public static void SetFastbootPath(string path)
        {
            _fastbootPath = path;
        }

        /// <summary>
        /// Get fastboot.exe path
        /// </summary>
        public static string GetFastbootPath()
        {
            if (string.IsNullOrEmpty(_fastbootPath))
            {
                // Search in the program directory by default
                _fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastboot.exe");
            }
            return _fastbootPath;
        }

        /// <summary>
        /// Create House Fastboot command instance
        /// </summary>
        /// <param name="serial">Device serial number (can be null for default device)</param>
        /// <param name="action">Command to execute</param>
        public FastbootCommand(string serial, string action)
        {
            string fastbootExe = GetFastbootPath();
            if (!File.Exists(fastbootExe))
            {
                throw new FileNotFoundException("fastboot.exe does not exist", fastbootExe);
            }

            _process = new Process();
            _process.StartInfo.FileName = fastbootExe;
            _process.StartInfo.Arguments = string.IsNullOrEmpty(serial) 
                ? action 
                : $"-s \"{serial}\" {action}";
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            _process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            _process.Start();

            StdOut = _process.StandardOutput;
            StdErr = _process.StandardError;
            StdIn = _process.StandardInput;
        }

        /// <summary>
        /// Wait for command execution to complete
        /// </summary>
        public void WaitForExit()
        {
            _process?.WaitForExit();
        }

        /// <summary>
        /// Wait for command execution to complete (with timeout)
        /// </summary>
        public bool WaitForExit(int milliseconds)
        {
            return _process?.WaitForExit(milliseconds) ?? true;
        }

        /// <summary>
        /// Get exit code
        /// </summary>
        public int ExitCode => _process?.ExitCode ?? -1;

        /// <summary>
        /// Asynchronously execute command and return output
        /// </summary>
        public static async Task<FastbootResult> ExecuteAsync(string serial, string action, 
            CancellationToken ct = default, Action<string> onOutput = null)
        {
            var result = new FastbootResult();
            
            try
            {
                using (var cmd = new FastbootCommand(serial, action))
                {
                    var stdoutBuilder = new System.Text.StringBuilder();
                    var stderrBuilder = new System.Text.StringBuilder();
                    
                    // Read output in real-time
                    var stdoutTask = Task.Run(async () =>
                    {
                        string line;
                        while ((line = await cmd.StdOut.ReadLineAsync()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            stdoutBuilder.AppendLine(line);
                            onOutput?.Invoke(line);
                        }
                    }, ct);
                    
                    var stderrTask = Task.Run(async () =>
                    {
                        string line;
                        while ((line = await cmd.StdErr.ReadLineAsync()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            stderrBuilder.AppendLine(line);
                            onOutput?.Invoke(line);
                        }
                    }, ct);

                    // Wait for process termination and output reading completion
                    await Task.WhenAll(stdoutTask, stderrTask);
                    
                    // Ensure process termination
                    if (!cmd._process.HasExited)
                    {
                        cmd._process.WaitForExit(5000);
                    }

                    result.StdOut = stdoutBuilder.ToString();
                    result.StdErr = stderrBuilder.ToString();
                    result.ExitCode = cmd.ExitCode;
                    result.Success = cmd.ExitCode == 0;
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.StdErr = "Operation cancelled";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = ex.Message;
            }

            return result;
        }
        
        /// <summary>
        /// Asynchronously execute command with progress callback support
        /// </summary>
        public static async Task<FastbootResult> ExecuteWithProgressAsync(string serial, string action, 
            CancellationToken ct = default, Action<string> onOutput = null, Action<FlashProgress> onProgress = null)
        {
            var result = new FastbootResult();
            var progress = new FlashProgress();
            
            try
            {
                using (var cmd = new FastbootCommand(serial, action))
                {
                    var stderrBuilder = new System.Text.StringBuilder();
                    var stdoutBuilder = new System.Text.StringBuilder();
                    
                    // Read stderr in real-time (fastboot main output is here)
                    var stderrTask = Task.Run(async () =>
                    {
                        string line;
                        while ((line = await cmd.StdErr.ReadLineAsync()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            stderrBuilder.AppendLine(line);
                            onOutput?.Invoke(line);
                            
                            // Parse progress
                            ParseProgressFromLine(line, progress);
                            onProgress?.Invoke(progress);
                        }
                    }, ct);
                    
                    var stdoutTask = Task.Run(async () =>
                    {
                        string line;
                        while ((line = await cmd.StdOut.ReadLineAsync()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            stdoutBuilder.AppendLine(line);
                        }
                    }, ct);

                    await Task.WhenAll(stdoutTask, stderrTask);
                    
                    if (!cmd._process.HasExited)
                    {
                        cmd._process.WaitForExit(5000);
                    }

                    result.StdOut = stdoutBuilder.ToString();
                    result.StdErr = stderrBuilder.ToString();
                    result.ExitCode = cmd.ExitCode;
                    result.Success = cmd.ExitCode == 0;
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.StdErr = "Operation cancelled";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = ex.Message;
            }

            return result;
        }
        
        /// <summary>
        /// Parse progress from fastboot output line
        /// </summary>
        private static void ParseProgressFromLine(string line, FlashProgress progress)
        {
            if (string.IsNullOrEmpty(line)) return;
            
            // Parse Sending line: Sending 'boot_a' (65536 KB)
            // Or Sending sparse 'system' 1/12 (393216 KB)
            var sendingMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"Sending(?:\s+sparse)?\s+'([^']+)'(?:\s+(\d+)/(\d+))?\s+\((\d+)\s*KB\)");
            
            if (sendingMatch.Success)
            {
                progress.PartitionName = sendingMatch.Groups[1].Value;
                progress.Phase = "Sending";
                
                if (sendingMatch.Groups[2].Success && sendingMatch.Groups[3].Success)
                {
                    progress.CurrentChunk = int.Parse(sendingMatch.Groups[2].Value);
                    progress.TotalChunks = int.Parse(sendingMatch.Groups[3].Value);
                }
                else
                {
                    progress.CurrentChunk = 1;
                    progress.TotalChunks = 1;
                }
                
                progress.SizeKB = long.Parse(sendingMatch.Groups[4].Value);
                return;
            }
            
            // Parse Writing line: Writing 'boot_a'
            var writingMatch = System.Text.RegularExpressions.Regex.Match(line, @"Writing\s+'([^']+)'");
            if (writingMatch.Success)
            {
                progress.PartitionName = writingMatch.Groups[1].Value;
                progress.Phase = "Writing";
                return;
            }
            
            // Parse OKAY line: OKAY [  1.234s]
            var okayMatch = System.Text.RegularExpressions.Regex.Match(line, @"OKAY\s+\[\s*([\d.]+)s\]");
            if (okayMatch.Success)
            {
                progress.ElapsedSeconds = double.Parse(okayMatch.Groups[1].Value);
                
                // Calculate speed
                if (progress.Phase == "Sending" && progress.ElapsedSeconds > 0 && progress.SizeKB > 0)
                {
                    progress.SpeedKBps = progress.SizeKB / progress.ElapsedSeconds;
                }
                return;
            }
        }

        /// <summary>
        /// Synchronously execute command and return output
        /// </summary>
        public static FastbootResult Execute(string serial, string action)
        {
            var result = new FastbootResult();

            try
            {
                using (var cmd = new FastbootCommand(serial, action))
                {
                    result.StdOut = cmd.StdOut.ReadToEnd();
                    result.StdErr = cmd.StdErr.ReadToEnd();
                    cmd.WaitForExit();
                    result.ExitCode = cmd.ExitCode;
                    result.Success = cmd.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = ex.Message;
            }

            return result;
        }

        public void Dispose()
        {
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch { }
                _process.Close();
                _process.Dispose();
                _process = null;
            }
        }

        ~FastbootCommand()
        {
            Dispose();
        }
    }

    /// <summary>
    /// Fastboot Command Execution Result
    /// </summary>
    public class FastbootResult
    {
        public bool Success { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public int ExitCode { get; set; }

        /// <summary>
        /// Get all output (stdout + stderr)
        /// </summary>
        public string AllOutput => string.IsNullOrEmpty(StdOut) ? StdErr : $"{StdOut}\n{StdErr}";
    }
    
    /// <summary>
    /// Fastboot Flashing Progress Information
    /// </summary>
    public class FlashProgress
    {
        /// <summary>
        /// Partition name
        /// </summary>
        public string PartitionName { get; set; }
        
        /// <summary>
        /// Current phase: Sending, Writing
        /// </summary>
        public string Phase { get; set; }
        
        /// <summary>
        /// Current chunk (Sparse image)
        /// </summary>
        public int CurrentChunk { get; set; } = 1;
        
        /// <summary>
        /// Total chunks (Sparse image)
        /// </summary>
        public int TotalChunks { get; set; } = 1;
        
        /// <summary>
        /// Current chunk size (KB)
        /// </summary>
        public long SizeKB { get; set; }
        
        /// <summary>
        /// Current operation duration (seconds)
        /// </summary>
        public double ElapsedSeconds { get; set; }
        
        /// <summary>
        /// Transfer speed (KB/s)
        /// </summary>
        public double SpeedKBps { get; set; }
        
        /// <summary>
        /// Progress percentage (0-100)
        /// </summary>
        public double Percent { get; set; }
        
        /// <summary>
        /// Formatted speed display
        /// </summary>
        public string SpeedFormatted
        {
            get
            {
                if (SpeedKBps <= 0) return "";
                if (SpeedKBps >= 1024)
                    return $"{SpeedKBps / 1024:F2} MB/s";
                return $"{SpeedKBps:F2} KB/s";
            }
        }
    }
}
