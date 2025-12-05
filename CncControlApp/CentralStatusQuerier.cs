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
 /// Lightweight periodic status querier used by MainControll to poll machine status.
 /// Minimal implementation to satisfy references; expand as needed.
 /// Added subscription API so callers can request minimum update interval (demand-driven/adaptive).
 /// </summary>
 public class CentralStatusQuerier : IDisposable
 {
 private readonly ConnectionManager _connectionManager;
 private Timer _timer;
 public int DefaultIntervalMs { get; set; } = 300; // Idle interval
 private readonly ConcurrentDictionary<Guid, int> _subscribers = new ConcurrentDictionary<Guid, int>();
 private readonly object _timerLock = new object();
 private const int SAFETY_MIN_INTERVAL_MS = 25; // do not go faster than this
 
 // $G query counter - send $G every N ticks for spindle/coolant modal state
 private int _queryCount = 0;
 private const int G_QUERY_EVERY_N = 3; // Every 3 ticks (e.g., 900ms at 300ms interval)

 public CentralStatusQuerier(ConnectionManager connectionManager)
 {
 _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
 }

 public void Start()
 {
 Stop();
 int interval = CalculateEffectiveInterval();
 // dueTime0 to start immediately
 _timer = new Timer(_ => TimerTick(), null,0, interval);
 }

 public void Stop()
 {
 try { _timer?.Dispose(); } catch { }
 _timer = null;
 }

 private void TimerTick()
 {
 try
 {
 if (_connectionManager?.IsConnected == true)
 {
 // Every N ticks, also send $G to get modal state (spindle/coolant)
 _queryCount++;
 if (_queryCount >= G_QUERY_EVERY_N)
 {
 _queryCount = 0;
 _ = _connectionManager.SendGCodeCommandAsync("$G");
 }
 
 // Always send ? for status (coordinates, state, etc.)
 _ = _connectionManager.SendGCodeCommandAsync("?");
 }
 }
 catch { }
 }

 public void Dispose()
 {
 Stop();
 }

 /// <summary>
 /// Subscribe to status updates demand. Caller provides the minimum interval (in ms) it needs.
 /// The querier will adapt its timer to satisfy the fastest requested interval among subscribers,
 /// subject to the safety cap.
 /// Returns IDisposable which will unsubscribe on Dispose.
 /// </summary>
 public IDisposable SubscribeMinimumInterval(int minIntervalMs)
 {
 if (minIntervalMs <=0) minIntervalMs = DefaultIntervalMs;
 var id = Guid.NewGuid();
 _subscribers[id] = minIntervalMs;
 UpdateTimerInterval();
 return new Unsubscriber(_subscribers, id, UpdateTimerInterval);
 }

 /// <summary>
 /// Return a snapshot of current subscribers and their requested minimum intervals (ms).
 /// </summary>
 public Dictionary<Guid,int> GetSubscriberRequests()
 {
 try
 {
 // return a shallow copy snapshot to avoid exposing internal collection
 return new Dictionary<Guid,int>(_subscribers);
 }
 catch
 {
 return new Dictionary<Guid,int>();
 }
 }

 private int CalculateEffectiveInterval()
 {
 try
 {
 if (_subscribers.IsEmpty) return DefaultIntervalMs;
 int fastest = _subscribers.Values.Min();
 // ensure we don't go faster than safety cap (i.e., interval cannot be below SAFETY_MIN_INTERVAL_MS)
 int effective = Math.Max(SAFETY_MIN_INTERVAL_MS, fastest);
 return effective;
 }
 catch
 {
 return DefaultIntervalMs;
 }
 }

 private void UpdateTimerInterval()
 {
 try
 {
 lock (_timerLock)
 {
 if (_timer == null) return;
 int interval = CalculateEffectiveInterval();
 // Change timer period; keep dueTime same (0 = immediate)
 try { _timer.Change(0, interval); } catch { }
 }
 }
 catch { }
 }

 private class Unsubscriber : IDisposable
 {
 private readonly ConcurrentDictionary<Guid,int> _dict;
 private readonly Guid _id;
 private readonly Action _onRemoved;
 private bool _disposed;
 public Unsubscriber(ConcurrentDictionary<Guid,int> dict, Guid id, Action onRemoved)
 {
 _dict = dict; _id = id; _onRemoved = onRemoved;
 }
 public void Dispose()
 {
 if (_disposed) return;
 _disposed = true;
 int ignored;
 try { _dict.TryRemove(_id, out ignored); } catch { }
 try { _onRemoved?.Invoke(); } catch { }
 }
 }
 }
}
