using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace CncControlApp
{
    /// <summary>
    /// MainControll partial class - Status Query Helpers and Rate Limiting
    /// Handles fast status query scopes and "?" rate limiting to prevent MCU overload
    /// </summary>
    public partial class MainControll
    {
        #region Fast Status Query Scope Management

        // Track active fast status scopes (owner -> subscription)
        private readonly ConcurrentDictionary<string, IDisposable> _fastStatusScopes = new ConcurrentDictionary<string, IDisposable>();

        /// <summary>
  /// Check if central status querier is active and querying
        /// </summary>
     public bool IsCentralQueryActive => _centralStatusQuerierEnabled && _centralStatusQuerier != null;

        /// <summary>
        /// Start a fast status query scope with specified minimum interval.
  /// Returns an IDisposable that will automatically restore the default interval when disposed.
   /// Use in probe operations, motion waits, etc. to temporarily increase query frequency.
        /// </summary>
        /// <param name="owner">Owner/purpose of the scope (for logging and tracking)</param>
      /// <param name="minIntervalMs">Minimum query interval in milliseconds (default: 100ms)</param>
        /// <returns>IDisposable subscription - dispose to end the fast query scope</returns>
      public IDisposable StartFastStatusScope(string owner, int minIntervalMs =100)
        {
  try
    {
       // Safety: enforce minimum interval >=25ms (device stability)
      if (minIntervalMs <25)
         {
     AddLogMessage($"> ⚠️ FastStatusScope({owner}): {minIntervalMs}ms too fast, clamped to25ms");
       minIntervalMs =25;
       }

       // If central querier is not active, log warning and return empty disposable
     if (!IsCentralQueryActive)
  {
            AddLogMessage($"> ⚠️ FastStatusScope({owner}): CentralStatusQuerier not active - scope ignored");
return new EmptyDisposable();
    }

    // Check if this owner already has an active scope
if (_fastStatusScopes.ContainsKey(owner))
      {
 AddLogMessage($"> ⚠️ FastStatusScope({owner}): Already active - replacing existing scope");
                 // Dispose existing scope
    if (_fastStatusScopes.TryRemove(owner, out var existingScope))
   {
  try { existingScope?.Dispose(); } catch { }
             }
        }

            // Subscribe to central querier with requested interval
        var subscription = _centralStatusQuerier?.SubscribeMinimumInterval(minIntervalMs);
      if (subscription == null)
          {
         AddLogMessage($"> ❌ FastStatusScope({owner}): Failed to subscribe to CentralStatusQuerier");
        return new EmptyDisposable();
         }

         // Wrap subscription in a tracking disposable
  var trackedSubscription = new FastStatusScopeDisposable(owner, subscription, () =>
        {
       // Remove from tracking dict when disposed
   _fastStatusScopes.TryRemove(owner, out _);
    AddLogMessage($"> 🔄 FastStatusScope({owner}) ended - interval restored to default");
             });

     // Store in tracking dict
        _fastStatusScopes[owner] = trackedSubscription;
 AddLogMessage($"> ⚡ FastStatusScope({owner}) started - interval: {minIntervalMs}ms");

         return trackedSubscription;
   }
            catch (Exception ex)
  {
                AddLogMessage($"> ❌ StartFastStatusScope({owner}) error: {ex.Message}");
      return new EmptyDisposable();
        }
     }

/// <summary>
 /// Scoped override for the baseline central status subscription interval.
 /// Disposes the baseline subscription, applies the requested interval, and restores baseline (150ms) on dispose.
 /// Use when you need to slow down central queries temporarily (e.g., to200ms during probe).
 /// </summary>
 public IDisposable BeginScopedCentralStatusOverride(int intervalMs)
 {
 try
 {
 if (!IsCentralQueryActive)
 {
 AddLogMessage($"> ⚠️ Central override ignored – querier not active");
 return new EmptyDisposable();
 }

 // Dispose current baseline subscription
 try { _centralStatusSubscription?.Dispose(); } catch { }
 _centralStatusSubscription = null;

 // Apply override
 _centralStatusSubscription = _centralStatusQuerier?.SubscribeMinimumInterval(intervalMs);
 AddLogMessage($"> 🌐 Central status interval OVERRIDE -> {intervalMs}ms (scoped)");

 return new ActionOnDispose(() =>
 {
 try { _centralStatusSubscription?.Dispose(); } catch { }
 // Restore baseline 1000ms (idle)
 try { _centralStatusSubscription = _centralStatusQuerier?.SubscribeMinimumInterval(1000); } catch { }
 AddLogMessage($"> 🌐 Central status interval restored -> 1000ms");
 });
 }
 catch (Exception ex)
 {
 AddLogMessage($"> ❌ BeginScopedCentralStatusOverride error: {ex.Message}");
 return new EmptyDisposable();
 }
 }

/// <summary>
 /// Get count of active fast status scopes (for diagnostics)
 /// </summary>
 public int ActiveFastStatusScopeCount => _fastStatusScopes?.Count ??0;

 /// <summary>
 /// Get diagnostic info about active fast status scopes
 /// </summary>
 public string GetFastStatusScopeDiagnostics()
 {
 try
 {
 if (_fastStatusScopes == null || _fastStatusScopes.IsEmpty)
 return "> No active fast status scopes";

 var owners = string.Join(", ", _fastStatusScopes.Keys);
 return $"> Active fast status scopes ({_fastStatusScopes.Count}): {owners}";
 }
 catch
 {
 return "> Error getting fast status scope diagnostics";
 }
 }

 #endregion

 #region Helper Classes

 /// <summary>
 /// Wrapper for fast status scope subscription that tracks owner and provides disposal callback
 /// </summary>
 private class FastStatusScopeDisposable : IDisposable
 {
 private readonly string _owner;
 private readonly IDisposable _subscription;
 private readonly Action _onDispose;
 private bool _disposed;

 public FastStatusScopeDisposable(string owner, IDisposable subscription, Action onDispose)
 {
 _owner = owner;
 _subscription = subscription;
 _onDispose = onDispose;
 }

 public void Dispose()
 {
 if (_disposed) return;
 _disposed = true;

 try
 {
 _subscription?.Dispose();
 }
 catch { }

 try
 {
 _onDispose?.Invoke();
 }
 catch { }
 }
 }

 /// <summary>
 /// Empty disposable for cases where fast status scope cannot be created
 /// </summary>
 private class EmptyDisposable : IDisposable
 {
 public void Dispose() { }
 }

 /// <summary>
 /// Simple action-on-dispose helper
 /// </summary>
 private class ActionOnDispose : IDisposable
 {
 private readonly Action _action;
 private bool _disposed;
 public ActionOnDispose(Action action) { _action = action; }
 public void Dispose()
 {
 if (_disposed) return;
 _disposed = true;
 try { _action?.Invoke(); } catch { }
 }
 }

 #endregion
 }

 /// <summary>
 /// Static rate limiter for "?" status query commands.
 /// Prevents excessive status query spam that can overload MCU (especially ESP32/FluidNC).
 /// Use this in legacy code paths and fallback queries to ensure minimum spacing between queries.
 /// </summary>
 public static class QuestionMarkRateLimiter
 {
 private static DateTime _lastQuestionMark = DateTime.MinValue;
 private static readonly object _lock = new object();

 /// <summary>
 /// Minimum spacing between "?" queries in milliseconds (default:100ms =10 QPS max)
 /// For ESP32/FluidNC:100ms is safe. For faster MCU: can be reduced to50-75ms.
 /// </summary>
 public const int MinSpacingMs =100;

 /// <summary>
 /// Check if a "?" query is allowed now based on rate limiting.
 /// If allowed, updates the last query timestamp.
 /// Thread-safe.
 /// </summary>
 /// <returns>True if query is allowed, false if too soon (should be throttled)</returns>
 public static bool AllowNow()
 {
 lock (_lock)
 {
 var now = DateTime.UtcNow;
 var elapsed = (now - _lastQuestionMark).TotalMilliseconds;

 if (elapsed >= MinSpacingMs)
 {
 _lastQuestionMark = now;
 return true;
 }

 return false;
 }
 }

 /// <summary>
 /// Get time since last "?" query in milliseconds
 /// </summary>
 public static double TimeSinceLastQueryMs()
 {
 lock (_lock)
 {
 return (DateTime.UtcNow - _lastQuestionMark).TotalMilliseconds;
 }
 }

 /// <summary>
 /// Force reset the rate limiter (for testing or after connection reset)
 /// </summary>
 public static void Reset()
 {
 lock (_lock)
 {
 _lastQuestionMark = DateTime.MinValue;
 }
 }

 /// <summary>
 /// Get diagnostic info about rate limiter state
 /// </summary>
 public static string GetDiagnostics()
 {
 lock (_lock)
 {
 var elapsed = TimeSinceLastQueryMs();
 var allowed = elapsed >= MinSpacingMs;
 return $"RateLimiter: Last query {elapsed:F0}ms ago - {(allowed ? "ALLOWED" : "THROTTLED")} (min: {MinSpacingMs}ms)";
 }
 }
 }
}
