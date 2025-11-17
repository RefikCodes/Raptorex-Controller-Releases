using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CncControlApp.Services
{
 public static class StatusQueryService
 {
 private static CncControlApp.CentralStatusQuerier _central;
 private static Func<string, Task<bool>> _sendFunc;
 private static readonly object _lock = new object();
 private static readonly ConcurrentBag<int> _requestedIntervals = new ConcurrentBag<int>();

 /// <summary>
 /// When true, if there is no central querier registered, StatusQueryService.QueryStatusOnce will refuse
 /// to send status queries on behalf of callers. This prevents legacy/duplicate polling loops from issuing
 /// periodic "?" when a central querier is intentionally disabled. Default: true.
 /// </summary>
 public static bool BlockNonCentralWhenNoCentral { get; set; } = true;

 public static bool HasCentralQuerier
 {
 get { lock (_lock) { return _central != null; } }
 }

 public static void RegisterCentralQuerier(CncControlApp.CentralStatusQuerier central, Func<string, Task<bool>> sendFunc)
 {
 lock (_lock)
 {
 _central = central;
 _sendFunc = sendFunc ?? _sendFunc;
 }
 }

 public static void UnregisterCentralQuerier()
 {
 lock (_lock)
 {
 _central = null;
 _sendFunc = null;
 }
 }

 public static async Task<bool> QueryStatusOnce(int? requestedIntervalMs = null)
 {
 try
 {
 int req = requestedIntervalMs ??200;
 _requestedIntervals.Add(req);
 CncControlApp.CentralStatusQuerier central;
 Func<string, Task<bool>> send;
 lock (_lock)
 {
 central = _central;
 send = _sendFunc;
 }

 // If no central querier and global blocking is enabled, refuse to send status queries here.
 if (central == null && BlockNonCentralWhenNoCentral)
 {
 return false;
 }

 if (central != null)
 {
 int r = Math.Max(25, req);
 IDisposable sub = null;
 try
 {
 sub = central.SubscribeMinimumInterval(r);
 }
 catch
 {
 if (send != null) return await send("?");
 return false;
 }
 int disposeMs = Math.Max(150, r *2);
 _ = Task.Run(async () => { try { await Task.Delay(disposeMs); } catch { } try { sub.Dispose(); } catch { } });
 return true;
 }

 if (send != null) return await send("?");
 return false;
 }
 catch
 {
 return false;
 }
 }

 public static IReadOnlyDictionary<int, int> GetRequestedIntervalStats()
 {
 return _requestedIntervals.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
 }
 }
}
