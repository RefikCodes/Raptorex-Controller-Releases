using System;
using System.Threading;
using System.Threading.Tasks;
using CncControlApp.Managers;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;

namespace CncControlApp
{
    /// <summary>
    /// Central status querier that periodically sends status (?) queries to GRBL.
    /// </summary>
    public class CentralStatusQuerier : IDisposable
    {
        private readonly ConnectionManager _connectionManager;
        private CancellationTokenSource _cts;
        private Task _queryTask;
        private bool _isRunning;
        private readonly object _lock = new object();
        
        public int DefaultIntervalMs { get; set; } = 300; // Idle interval
        private readonly ConcurrentDictionary<Guid, int> _subscribers = new ConcurrentDictionary<Guid, int>();
        private const int SAFETY_MIN_INTERVAL_MS = 25; // do not go faster than this

        public CentralStatusQuerier(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                
                Stop(); // Clean up any previous state
                
                _cts = new CancellationTokenSource();
                _isRunning = true;
                _queryTask = Task.Run(() => QueryLoopAsync(_cts.Token));
                
                System.Diagnostics.Debug.WriteLine($"[CentralStatusQuerier] Started with interval {CalculateEffectiveInterval()}ms");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _isRunning = false;
                
                try { _cts?.Cancel(); } catch { }
                
                try
                {
                    _queryTask?.Wait(500);
                }
                catch (AggregateException) { }
                catch (TaskCanceledException) { }
                
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                
                System.Diagnostics.Debug.WriteLine("[CentralStatusQuerier] Stopped");
            }
        }

        /// <summary>
        /// Main query loop - sends status query (?) periodically
        /// </summary>
        private async Task QueryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (_connectionManager?.IsConnected != true)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    int intervalMs = CalculateEffectiveInterval();

                    // Send status query (?) - REALTIME command
                    await SendStatusQueryAsync();

                    // Normal delay
                    await Task.Delay(intervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CentralStatusQuerier] Error: {ex.Message}");
                    try { await Task.Delay(500, token); } catch { break; }
                }
            }
        }

        /// <summary>
        /// Sends the status query (?) as REALTIME command
        /// </summary>
        private async Task SendStatusQueryAsync()
        {
            try
            {
                // CRITICAL: Send ? as REALTIME command (single byte 0x3F, no newline, no buffer tracking)
                await _connectionManager.SendControlCharacterAsync('?');
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CentralStatusQuerier] Status query error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Subscribe to status updates demand. Caller provides the minimum interval (in ms) it needs.
        /// The querier will adapt its loop to satisfy the fastest requested interval among subscribers,
        /// subject to the safety cap.
        /// Returns IDisposable which will unsubscribe on Dispose.
        /// </summary>
        public IDisposable SubscribeMinimumInterval(int minIntervalMs)
        {
            if (minIntervalMs <= 0) minIntervalMs = DefaultIntervalMs;
            var id = Guid.NewGuid();
            _subscribers[id] = minIntervalMs;
            return new Unsubscriber(_subscribers, id);
        }

        /// <summary>
        /// Return a snapshot of current subscribers and their requested minimum intervals (ms).
        /// </summary>
        public Dictionary<Guid, int> GetSubscriberRequests()
        {
            try
            {
                return new Dictionary<Guid, int>(_subscribers);
            }
            catch
            {
                return new Dictionary<Guid, int>();
            }
        }

        private int CalculateEffectiveInterval()
        {
            try
            {
                if (_subscribers.IsEmpty) return DefaultIntervalMs;
                int fastest = _subscribers.Values.Min();
                // ensure we don't go faster than safety cap
                int effective = Math.Max(SAFETY_MIN_INTERVAL_MS, fastest);
                return effective;
            }
            catch
            {
                return DefaultIntervalMs;
            }
        }

        private class Unsubscriber : IDisposable
        {
            private readonly ConcurrentDictionary<Guid, int> _dict;
            private readonly Guid _id;
            private bool _disposed;

            public Unsubscriber(ConcurrentDictionary<Guid, int> dict, Guid id)
            {
                _dict = dict;
                _id = id;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                int ignored;
                try { _dict.TryRemove(_id, out ignored); } catch { }
            }
        }
    }
}
