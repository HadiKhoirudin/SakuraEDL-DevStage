using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Fastboot.Common
{
    /// <summary>
    /// Fastboot 命令执行器
    /// 封装 fastboot.exe 命令行工具
    /// </summary>
    public class FastbootCommand : IDisposable
    {
        private Process _process;
        private static string _fastbootPath;

        public StreamReader StdOut { get; private set; }
        public StreamReader StdErr { get; private set; }
        public StreamWriter StdIn { get; private set; }

        /// <summary>
        /// 设置 fastboot.exe 路径
        /// </summary>
        public static void SetFastbootPath(string path)
        {
            _fastbootPath = path;
        }

        /// <summary>
        /// 获取 fastboot.exe 路径
        /// </summary>
        public static string GetFastbootPath()
        {
            if (string.IsNullOrEmpty(_fastbootPath))
            {
                // 默认在程序目录下查找
                _fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastboot.exe");
            }
            return _fastbootPath;
        }

        /// <summary>
        /// 创建 Fastboot 命令实例
        /// </summary>
        /// <param name="serial">设备序列号（可为null表示使用默认设备）</param>
        /// <param name="action">要执行的命令</param>
        public FastbootCommand(string serial, string action)
        {
            string fastbootExe = GetFastbootPath();
            if (!File.Exists(fastbootExe))
            {
                throw new FileNotFoundException("fastboot.exe 不存在", fastbootExe);
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
        /// 等待命令执行完成
        /// </summary>
        public void WaitForExit()
        {
            _process?.WaitForExit();
        }

        /// <summary>
        /// 等待命令执行完成（带超时）
        /// </summary>
        public bool WaitForExit(int milliseconds)
        {
            return _process?.WaitForExit(milliseconds) ?? true;
        }

        /// <summary>
        /// 获取退出码
        /// </summary>
        public int ExitCode => _process?.ExitCode ?? -1;

        /// <summary>
        /// 异步执行命令并返回输出
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
                    
                    // 实时读取输出
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

                    // 等待进程结束和输出读取完成
                    await Task.WhenAll(stdoutTask, stderrTask);
                    
                    // 确保进程结束
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
                result.StdErr = "操作已取消";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = ex.Message;
            }

            return result;
        }
        
        /// <summary>
        /// 异步执行命令并支持进度回调
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
                    
                    // 实时读取 stderr（fastboot 主要输出在这里）
                    var stderrTask = Task.Run(async () =>
                    {
                        string line;
                        while ((line = await cmd.StdErr.ReadLineAsync()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            stderrBuilder.AppendLine(line);
                            onOutput?.Invoke(line);
                            
                            // 解析进度
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
                result.StdErr = "操作已取消";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = ex.Message;
            }

            return result;
        }
        
        /// <summary>
        /// 从 fastboot 输出行解析进度
        /// </summary>
        private static void ParseProgressFromLine(string line, FlashProgress progress)
        {
            if (string.IsNullOrEmpty(line)) return;
            
            // 解析 Sending 行: Sending 'boot_a' (65536 KB)
            // 或 Sending sparse 'system' 1/12 (393216 KB)
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
            
            // 解析 Writing 行: Writing 'boot_a'
            var writingMatch = System.Text.RegularExpressions.Regex.Match(line, @"Writing\s+'([^']+)'");
            if (writingMatch.Success)
            {
                progress.PartitionName = writingMatch.Groups[1].Value;
                progress.Phase = "Writing";
                return;
            }
            
            // 解析 OKAY 行: OKAY [  1.234s]
            var okayMatch = System.Text.RegularExpressions.Regex.Match(line, @"OKAY\s+\[\s*([\d.]+)s\]");
            if (okayMatch.Success)
            {
                progress.ElapsedSeconds = double.Parse(okayMatch.Groups[1].Value);
                
                // 计算速度
                if (progress.Phase == "Sending" && progress.ElapsedSeconds > 0 && progress.SizeKB > 0)
                {
                    progress.SpeedKBps = progress.SizeKB / progress.ElapsedSeconds;
                }
                return;
            }
        }

        /// <summary>
        /// 同步执行命令并返回输出
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
    /// Fastboot 命令执行结果
    /// </summary>
    public class FastbootResult
    {
        public bool Success { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public int ExitCode { get; set; }

        /// <summary>
        /// 获取所有输出（stdout + stderr）
        /// </summary>
        public string AllOutput => string.IsNullOrEmpty(StdOut) ? StdErr : $"{StdOut}\n{StdErr}";
    }
    
    /// <summary>
    /// Fastboot 刷写进度信息
    /// </summary>
    public class FlashProgress
    {
        /// <summary>
        /// 分区名称
        /// </summary>
        public string PartitionName { get; set; }
        
        /// <summary>
        /// 当前阶段: Sending, Writing
        /// </summary>
        public string Phase { get; set; }
        
        /// <summary>
        /// 当前块 (Sparse 镜像)
        /// </summary>
        public int CurrentChunk { get; set; } = 1;
        
        /// <summary>
        /// 总块数 (Sparse 镜像)
        /// </summary>
        public int TotalChunks { get; set; } = 1;
        
        /// <summary>
        /// 当前块大小 (KB)
        /// </summary>
        public long SizeKB { get; set; }
        
        /// <summary>
        /// 当前操作耗时 (秒)
        /// </summary>
        public double ElapsedSeconds { get; set; }
        
        /// <summary>
        /// 传输速度 (KB/s)
        /// </summary>
        public double SpeedKBps { get; set; }
        
        /// <summary>
        /// 进度百分比 (0-100)
        /// </summary>
        public double Percent { get; set; }
        
        /// <summary>
        /// 格式化的速度显示
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
