// ============================================================================
// LoveAlways - Watchdog Mechanism
// Watchdog Mechanism for Protocol Communication
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Common
{
    /// <summary>
    /// Watchdog State
    /// </summary>
    public enum WatchdogState
    {
        Idle,       // Idle
        Running,    // Running
        Timeout,    // Timeout
        Stopped     // Stopped
    }

    /// <summary>
    /// Watchdog Timeout Event Arguments
    /// </summary>
    public class WatchdogTimeoutEventArgs : EventArgs
    {
        public string ModuleName { get; set; }
        public string OperationName { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public int TimeoutCount { get; set; }
        public bool ShouldReset { get; set; } = true;
    }

    /// <summary>
    /// General Watchdog Interface
    /// </summary>
    public interface IWatchdog : IDisposable
    {
        /// <summary>
        /// Current State
        /// </summary>
        WatchdogState State { get; }

        /// <summary>
        /// Timeout Value
        /// </summary>
        TimeSpan Timeout { get; set; }

        /// <summary>
        /// Timeout Count
        /// </summary>
        int TimeoutCount { get; }

        /// <summary>
        /// Start Watchdog
        /// </summary>
        void Start(string operationName = null);

        /// <summary>
        /// Stop Watchdog
        /// </summary>
        void Stop();

        /// <summary>
        /// Feed Dog (Reset Timer)
        /// </summary>
        void Feed();

        /// <summary>
        /// Check if Timed Out
        /// </summary>
        bool IsTimedOut { get; }

        /// <summary>
        /// On Timeout Event
        /// </summary>
        event EventHandler<WatchdogTimeoutEventArgs> OnTimeout;
    }

    /// <summary>
    /// General Watchdog Implementation
    /// </summary>
    public class Watchdog : IWatchdog
    {
        private readonly string _moduleName;
        private readonly Action<string> _log;
        private readonly Stopwatch _stopwatch;
        private readonly object _lock = new object();

        private string _currentOperation;
        private int _timeoutCount;
        private bool _disposed;
        private CancellationTokenSource _cts;
        private Task _monitorTask;

        public WatchdogState State { get; private set; } = WatchdogState.Idle;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public int TimeoutCount => _timeoutCount;
        public bool IsTimedOut => State == WatchdogState.Timeout;

        public event EventHandler<WatchdogTimeoutEventArgs> OnTimeout;

        /// <summary>
        /// Create Watchdog
        /// </summary>
        /// <param name="moduleName">Module Name (Qualcomm/Spreadtrum/Fastboot)</param>
        /// <param name="timeout">Timeout Value</param>
        /// <param name="log">Log Callback</param>
        public Watchdog(string moduleName, TimeSpan timeout, Action<string> log = null)
        {
            _moduleName = moduleName;
            Timeout = timeout;
            _log = log;
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// Start Watchdog
        /// </summary>
        public void Start(string operationName = null)
        {
            lock (_lock)
            {
                if (_disposed) return;

                _currentOperation = operationName ?? "Unknown";
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _stopwatch.Restart();
                State = WatchdogState.Running;

                _log?.Invoke($"[{_moduleName}] Watchdog started: {_currentOperation} (Timeout: {Timeout.TotalSeconds}s)");

                // Start background monitor task
                _monitorTask = MonitorAsync(_cts.Token);
            }
        }

        /// <summary>
        /// Stop Watchdog
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _stopwatch.Stop();
                State = WatchdogState.Stopped;
                _log?.Invoke($"[{_moduleName}] Watchdog stopped: {_currentOperation}");
            }
        }

        /// <summary>
        /// Feed Dog - Reset Timer
        /// </summary>
        public void Feed()
        {
            lock (_lock)
            {
                if (State == WatchdogState.Running)
                {
                    _stopwatch.Restart();
                }
            }
        }

        /// <summary>
        /// Background Monitor Task
        /// </summary>
        private async Task MonitorAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && State == WatchdogState.Running)
                {
                    await Task.Delay(1000, ct); // Check every second

                    lock (_lock)
                    {
                        if (_stopwatch.Elapsed > Timeout && State == WatchdogState.Running)
                        {
                            State = WatchdogState.Timeout;
                            _timeoutCount++;

                            _log?.Invoke($"[{_moduleName}] Watchdog timeout! Operation: {_currentOperation}, Elapsed: {_stopwatch.Elapsed.TotalSeconds:F1}s, Timeout Count: {_timeoutCount}");

                            var args = new WatchdogTimeoutEventArgs
                            {
                                ModuleName = _moduleName,
                                OperationName = _currentOperation,
                                ElapsedTime = _stopwatch.Elapsed,
                                TimeoutCount = _timeoutCount
                            };

                            OnTimeout?.Invoke(this, args);

                            // If reset is needed, automatically restart watchdog
                            if (args.ShouldReset)
                            {
                                _stopwatch.Restart();
                                State = WatchdogState.Running;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[{_moduleName}] Watchdog monitor exception: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Task taskToWait = null;

            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                _cts?.Cancel();
                _stopwatch.Stop();
                State = WatchdogState.Stopped;
                taskToWait = _monitorTask;
            }

            // Wait for task completion outside lock to avoid deadlock
            if (taskToWait != null)
            {
                try
                {
                    // Wait at most 2 seconds
                    taskToWait.Wait(2000);
                }
                catch (AggregateException)
                {
                    // Ignore task cancellation exception
                }
                catch (ObjectDisposedException)
                {
                    // Ignore disposed exception
                }
            }

            lock (_lock)
            {
                _cts?.Dispose();
                _cts = null;
                _monitorTask = null;
            }
        }
    }

    /// <summary>
    /// Watchdog Scope (using pattern)
    /// </summary>
    public class WatchdogScope : IDisposable
    {
        private readonly IWatchdog _watchdog;

        public WatchdogScope(IWatchdog watchdog, string operationName)
        {
            _watchdog = watchdog;
            _watchdog.Start(operationName);
        }

        /// <summary>
        /// Feed Dog
        /// </summary>
        public void Feed() => _watchdog.Feed();

        public void Dispose()
        {
            _watchdog.Stop();
        }
    }

    /// <summary>
    /// Watchdog Manager - Unified management of watchdogs for each module
    /// </summary>
    public static class WatchdogManager
    {
        private static readonly object _lock = new object();
        private static Watchdog _qualcommWatchdog;
        private static Watchdog _spreadtrumWatchdog;
        private static Watchdog _fastbootWatchdog;

        /// <summary>
        /// Default Timeout Configuration
        /// </summary>
        public static class DefaultTimeouts
        {
            public static readonly TimeSpan Qualcomm = TimeSpan.FromSeconds(60);
            public static readonly TimeSpan Spreadtrum = TimeSpan.FromSeconds(45);
            public static readonly TimeSpan Fastboot = TimeSpan.FromSeconds(90);
        }

        /// <summary>
        /// Get or Create Qualcomm Watchdog
        /// </summary>
        public static Watchdog GetQualcommWatchdog(Action<string> log = null)
        {
            lock (_lock)
            {
                if (_qualcommWatchdog == null)
                {
                    _qualcommWatchdog = new Watchdog("Qualcomm", DefaultTimeouts.Qualcomm, log);
                }
                return _qualcommWatchdog;
            }
        }

        /// <summary>
        /// Get or Create Spreadtrum Watchdog
        /// </summary>
        public static Watchdog GetSpreadtrumWatchdog(Action<string> log = null)
        {
            lock (_lock)
            {
                if (_spreadtrumWatchdog == null)
                {
                    _spreadtrumWatchdog = new Watchdog("Spreadtrum", DefaultTimeouts.Spreadtrum, log);
                }
                return _spreadtrumWatchdog;
            }
        }

        /// <summary>
        /// Get or Create Fastboot Watchdog
        /// </summary>
        public static Watchdog GetFastbootWatchdog(Action<string> log = null)
        {
            lock (_lock)
            {
                if (_fastbootWatchdog == null)
                {
                    _fastbootWatchdog = new Watchdog("Fastboot", DefaultTimeouts.Fastboot, log);
                }
                return _fastbootWatchdog;
            }
        }

        /// <summary>
        /// Dispose All Watchdogs
        /// </summary>
        public static void DisposeAll()
        {
            lock (_lock)
            {
                _qualcommWatchdog?.Dispose();
                _qualcommWatchdog = null;

                _spreadtrumWatchdog?.Dispose();
                _spreadtrumWatchdog = null;

                _fastbootWatchdog?.Dispose();
                _fastbootWatchdog = null;
            }
        }
    }
}
