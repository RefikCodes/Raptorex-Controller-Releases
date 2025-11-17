// MainWindow.ExecutionHelpers.cs
// Execution and motion control helper methods
// Separated from MainWindow.xaml.cs for better organization

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CncControlApp.Services; // added to use StatusQueryService instead of direct '?'

namespace CncControlApp
{
    /// <summary>
    /// MainWindow partial class - Execution and motion control helpers
    /// Contains methods for G-code execution, motion waiting, and coordinate reading
    /// </summary>
 public partial class MainWindow
    {
        #region Execution Helpers

    /// <summary>
        /// Execute G-code command and wait for motion completion
        /// </summary>
/// <param name="gcode">G-code command to execute</param>
        /// <param name="tag">Tag for logging purposes</param>
     /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <param name="stableIdleCount">Number of consecutive Idle states required</param>
        /// <returns>True if command executed successfully and motion completed</returns>
        private async Task<bool> ExecuteAndWaitAsync(string gcode, string tag, int timeoutMs = 15000, int stableIdleCount = 2)
        {
try
    {
        App.MainController?.AddLogMessage($"> ▶ [{tag}] SEND: {gcode} | {GetWPosSnapshot()}");
         
bool sentOk = await App.MainController.SendGCodeCommandWithConfirmationAsync(gcode);
if (!sentOk)
{
 App.MainController?.AddLogMessage($"> ❌ [{tag}] send failed: {gcode}");
 return false;
}

              bool waitOk = await WaitForMotionCompleteAsync(timeoutMs, stableIdleCount, tag, false);
     if (!waitOk)
     {
          App.MainController?.AddLogMessage($"> ⚠️ [{tag}] wait did not complete | {GetWPosSnapshot()}");
                }
       else
    {
       App.MainController?.AddLogMessage($"> ✅ [{tag}] completed | {GetWPosSnapshot()}");
             }
    
         return waitOk;
 }
            catch (Exception ex)
        {
        App.MainController?.AddLogMessage($"> ❌ [{tag}] exception: {ex.Message}");
           return false;
            }
    }

        /// <summary>
        /// Execute G-code command with automatic timeout extension for long-running operations
        /// Specifically designed for probe commands (G38.x) that may take longer than expected
   /// </summary>
        /// <param name="gcode">G-code command to execute</param>
        /// <param name="tag">Tag for logging purposes</param>
        /// <param name="initialTimeoutMs">Initial timeout in milliseconds</param>
        /// <param name="stableIdleCount">Number of consecutive Idle states required</param>
      /// <param name="maxExtensions">Maximum number of timeout extensions allowed</param>
        /// <param name="extendByMs">Milliseconds to extend timeout by</param>
        /// <returns>True if command executed successfully and motion completed</returns>
 private async Task<bool> ExecuteAndWaitWithAutoExtendAsync(
        string gcode,
    string tag,
 int initialTimeoutMs,
     int stableIdleCount = 2,
     int maxExtensions = 2,
          int extendByMs = 20000)
        {
            try
            {
    App.MainController?.AddLogMessage($"> ▶ [{tag}] SEND: {gcode} | {GetWPosSnapshot()}");
    
           bool sentOk = await App.MainController.SendGCodeCommandWithConfirmationAsync(gcode);
     if (!sentOk)
   {
       App.MainController?.AddLogMessage($"> ❌ [{tag}] send failed: {gcode}");
  return false;
           }

  int attempt = 0;
  int timeout = initialTimeoutMs;

     // Probe-like commands (G38.x) can finish instantly on contact; allow Idle without seen-move
   bool probeMode = gcode.IndexOf("G38.", StringComparison.OrdinalIgnoreCase) >= 0;

            while (true)
                {
     bool waitOk = await WaitForMotionCompleteAsync(timeout, stableIdleCount, tag, probeMode);
     if (waitOk)
     {
             App.MainController?.AddLogMessage($"> ✅ [{tag}] completed | {GetWPosSnapshot()}");
        return true;
 }

string state = App.MainController?.MachineStatus ?? string.Empty;
             bool stillRunning =
      state.StartsWith("Run", StringComparison.OrdinalIgnoreCase) ||
state.StartsWith("Jog", StringComparison.OrdinalIgnoreCase) ||
     state.IndexOf("<Run|", StringComparison.OrdinalIgnoreCase) >= 0 ||
   state.IndexOf("<Jog|", StringComparison.OrdinalIgnoreCase) >= 0;

       if (attempt < maxExtensions && stillRunning)
        {
         attempt++;
      App.MainController?.AddLogMessage($"> ⏱ [{tag}] extending wait ({attempt}/{maxExtensions}) by {extendByMs}ms | state='{state}'");
        timeout = extendByMs;
     continue;
        }

            App.MainController?.AddLogMessage($"> ⚠️ [{tag}] wait did not complete | state='{state}' | {GetWPosSnapshot()}");
              return false;
   }
      }
            catch (Exception ex)
  {
         App.MainController?.AddLogMessage($"> ❌ [{tag}] exception: {ex.Message}");
             return false;
            }
        }

        /// <summary>
     /// Wait for machine motion to complete by monitoring state and position
        /// Uses polling with configurable query interval
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait in milliseconds</param>
 /// <param name="stableIdleCount">Number of consecutive Idle states required to confirm completion</param>
        /// <param name="tag">Tag for logging purposes</param>
        /// <param name="allowIdleWithoutSeenMove">Allow Idle state without detecting movement (for probe operations)</param>
        /// <returns>True if motion completed successfully within timeout</returns>
      private async Task<bool> WaitForMotionCompleteAsync(
            int timeoutMs = 15000, 
            int stableIdleCount = 2, 
string tag = null, 
            bool allowIdleWithoutSeenMove = false)
        {
  try
 {
                var sw = Stopwatch.StartNew();
  int idleStreak = 0;
    bool seenMoving = false;
     string lastState = App.MainController?.MachineStatus ?? string.Empty;
      string tagVal = tag ?? "-";

       var s0 = App.MainController?.MStatus;
     double x0 = (s0 != null && IsFinite(s0.WorkX)) ? s0.WorkX : double.NaN;
          double y0 = (s0 != null && IsFinite(s0.WorkY)) ? s0.WorkY : double.NaN;
    double z0 = (s0 != null && IsFinite(s0.WorkZ)) ? s0.WorkZ : double.NaN;

        App.MainController?.AddLogMessage($"> 🔍 WaitForMotion[{tagVal}] start t={timeoutMs}ms idleStreak={stableIdleCount} | {GetWPosSnapshot()} | state='{lastState}'");

        const double moveEps = 0.005;
       long lastQueryMs = -9999;
                const int queryIntervalMs = 200;

           while (sw.ElapsedMilliseconds < timeoutMs)
      {
           string state = App.MainController?.MachineStatus ?? string.Empty;

      if (!string.Equals(state, lastState, StringComparison.OrdinalIgnoreCase))
             {
         App.MainController?.AddLogMessage($"> 📡 [{tagVal}] state: '{lastState}' -> '{state}' @ {sw.ElapsedMilliseconds}ms");
    lastState = state;
       }

    bool isIdle =
     state.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ||
            state.IndexOf("<Idle|", StringComparison.OrdinalIgnoreCase) >= 0;

    bool isMoving =
                 state.StartsWith("Run", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Jog", StringComparison.OrdinalIgnoreCase) ||
      state.IndexOf("<Run|", StringComparison.OrdinalIgnoreCase) >= 0 ||
 state.IndexOf("<Jog|", StringComparison.OrdinalIgnoreCase) >= 0;

     bool isAlarmOrHold =
       state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase) ||
      state.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) ||
       state.IndexOf("<Alarm|", StringComparison.OrdinalIgnoreCase) >= 0 ||
            state.IndexOf("<Hold|", StringComparison.OrdinalIgnoreCase) >= 0;

   if (isAlarmOrHold)
  {
     App.MainController?.AddLogMessage($"> 🛑 WaitForMotion[{tagVal}] abort: '{state}' @ {sw.ElapsedMilliseconds}ms | {GetWPosSnapshot()}");
            return false;
          }

          if (allowIdleWithoutSeenMove && isIdle)
      {
        idleStreak++;
                  if (idleStreak >= stableIdleCount)
         {
    App.MainController?.AddLogMessage($"> ✅ WaitForMotion[{tagVal}] (probe-mode) done in {sw.ElapsedMilliseconds}ms | {GetWPosSnapshot()}");
       return true;
        }
     }

      if (!seenMoving)
             {
      if (isMoving)
   {
      seenMoving = true;
          idleStreak = 0;
         }
             else
              {
         var s = App.MainController?.MStatus;
     if (s != null && IsFinite(x0) && IsFinite(y0) && IsFinite(z0) &&
     IsFinite(s.WorkX) && IsFinite(s.WorkY) && IsFinite(s.WorkZ))
               {
   double dx = Math.Abs(s.WorkX - x0);
    double dy = Math.Abs(s.WorkY - y0);
double dz = Math.Abs(s.WorkZ - z0);
    if (dx > moveEps || dy > moveEps || dz > moveEps)
         {
         seenMoving = true;
        idleStreak = 0;
       }
     }
   }
          }
        else
            {
                    if (isIdle)
 {
         idleStreak++;
                 if (idleStreak >= stableIdleCount)
          {
             App.MainController?.AddLogMessage($"> ✅ WaitForMotion[{tagVal}] done in {sw.ElapsedMilliseconds}ms | {GetWPosSnapshot()}");
            return true;
    }
 }
            else
           {
      idleStreak = 0;
   }
          }

  if (App.MainController != null)
           {
    if (sw.ElapsedMilliseconds - lastQueryMs >= queryIntervalMs)
        {
       lastQueryMs = sw.ElapsedMilliseconds;
     // Subscribe to central querier instead of sending '?' directly
     try { await StatusQueryService.QueryStatusOnce(queryIntervalMs); } catch { }
        }
   }

       await Task.Delay(50);
             }

 App.MainController?.AddLogMessage($"> ⌛ WaitForMotion[{tagVal}] timeout ({timeoutMs}ms) | lastState='{lastState}' | {GetWPosSnapshot()}");
         return false;
    }
            catch (Exception ex)
  {
         App.MainController?.AddLogMessage($"> ❌ WaitForMotion[{tag ?? "-"}] exception: {ex.Message}");
   return false;
      }
        }

        #endregion

        #region Coordinate Reading Helpers

        /// <summary>
        /// Read contact Z coordinate after probe operation
        /// First attempts to get PRB coordinate, falls back to current Z position
      /// </summary>
        /// <param name="sinceUtc">Only accept PRB data after this UTC timestamp</param>
     /// <param name="waitPrbMs">Maximum time to wait for PRB data in milliseconds</param>
        /// <returns>Tuple: (success, Z coordinate, whether from PRB)</returns>
        private async Task<(bool ok, double z, bool fromPrb)> ReadContactZAsync(DateTime sinceUtc, int waitPrbMs = 800)
        {
         var deadline = DateTime.UtcNow.AddMilliseconds(waitPrbMs);

            while (DateTime.UtcNow < deadline)
            {
          if (CncControlApp.Managers.ProbeContactCache.TryGetAfter(sinceUtc, out _, out _, out double prbZ, out _))
  return (true, prbZ, true);

   await Task.Delay(50);
   }

     // Request update via central status querier; do not send '?' directly
     await StatusQueryService.QueryStatusOnce(200);
      await Task.Delay(150);
   double wz = App.MainController?.MStatus?.Z ?? double.NaN;
return (true, wz, false);
        }

        /// <summary>
        /// Read contact XYZ coordinates after probe operation
        /// First attempts to get PRB coordinate, falls back to current position
    /// </summary>
        /// <param name="sinceUtc">Only accept PRB data after this UTC timestamp</param>
        /// <param name="waitPrbMs">Maximum time to wait for PRB data in milliseconds</param>
        /// <returns>Tuple: (success, X, Y, Z coordinates, whether from PRB)</returns>
        private async Task<(bool ok, double x, double y, double z, bool fromPrb)> ReadContactXYZAsync(DateTime sinceUtc, int waitPrbMs = 800)
        {
     var deadline = DateTime.UtcNow.AddMilliseconds(waitPrbMs);

            while (DateTime.UtcNow < deadline)
            {
      if (CncControlApp.Managers.ProbeContactCache.TryGetAfter(sinceUtc, out double prbX, out double prbY, out double prbZ, out _))
           return (true, prbX, prbY, prbZ, true);
      await Task.Delay(50);
       }

 // Request update via central status querier; do not send '?' directly
 await StatusQueryService.QueryStatusOnce(200);
        await Task.Delay(150);
var s = App.MainController?.MStatus;
          return (true, s?.WorkX ?? double.NaN, s?.WorkY ?? double.NaN, s?.WorkZ ?? double.NaN, false);
        }

        #endregion

        #region Utility Methods

      /// <summary>
        /// Get current work position as formatted string for logging
        /// </summary>
        /// <returns>Formatted work position string</returns>
        private string GetWPosSnapshot()
        {
          var s = App.MainController?.MStatus;
            return (s == null)
  ? "WPos N/A"
            : $"WPos X:{s.WorkX:F3} Y:{s.WorkY:F3} Z:{s.WorkZ:F3}";
        }

        /// <summary>
        /// Check if a double value is finite (not NaN or Infinity)
        /// </summary>
     /// <param name="v">Value to check</param>
     /// <returns>True if value is finite</returns>
   private static bool IsFinite(double v) => !(double.IsNaN(v) || double.IsInfinity(v));

    #endregion
    }
}
