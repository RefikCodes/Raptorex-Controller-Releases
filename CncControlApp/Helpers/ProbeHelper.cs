using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CncControlApp.Helpers
{
    /// <summary>
 /// Helper for common probe operations to reduce duplication in probe sequences
    /// </summary>
    public static class ProbeHelper
    {
  /// <summary>
        /// Wait for machine to reach Idle state with retry logic
    /// </summary>
        public static async Task<bool> WaitForIdleAsync(
 Func<string> getStatus,
  int timeoutMs,
            string operationTag,
        Action<string> logger = null,
 int requiredIdleCount = 3,
      int checkIntervalMs = 50)
   {
            var sw = Stopwatch.StartNew();
            int idleCount = 0;

       while (sw.ElapsedMilliseconds < timeoutMs)
            {
      string state = getStatus?.Invoke() ?? string.Empty;

      bool isIdle = state.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ||
    state.IndexOf("<Idle|", StringComparison.OrdinalIgnoreCase) >= 0;

    bool isAlarm = state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase) ||
      state.IndexOf("<Alarm|", StringComparison.OrdinalIgnoreCase) >= 0;

if (isAlarm)
      {
       logger?.Invoke($"> 🛑 [{operationTag}] Alarm detected!");
        return false;
   }

     if (isIdle)
      {
    idleCount++;
  if (idleCount >= requiredIdleCount)
         {
            logger?.Invoke($"> ✅ [{operationTag}] Idle confirmed ({sw.ElapsedMilliseconds}ms)");
return true;
     }
      }
     else
       {
        idleCount = 0; // Reset if not idle
    }

         await Task.Delay(checkIntervalMs);
        }

logger?.Invoke($"> ⌛ [{operationTag}] Timeout ({timeoutMs}ms)");
       return false;
        }

  /// <summary>
        /// Check if a coordinate value is finite (not NaN or Infinity)
        /// </summary>
  public static bool IsFinite(double value)
    {
       return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Clamp feed rate to safe range
   /// </summary>
   public static int ClampFeed(int feed, int min = 1, int max = 1000)
        {
  if (feed < min) return min;
 if (feed > max) return max;
   return feed;
        }

        /// <summary>
        /// Calculate timeout based on distance and feed rate
        /// </summary>
 public static int EstimateTimeoutForFeed(double distanceMm, int feedMmMin, int minMs = 8000)
        {
      if (feedMmMin <= 0) return minMs;
    
        double minutes = Math.Abs(distanceMm) / feedMmMin;
       int estimated = (int)(minutes * 60000) + 2000; // Add 2s safety margin
       return Math.Max(minMs, estimated);
        }

 /// <summary>
     /// Calculate timeout based on distance and rapid rate
    /// </summary>
        public static int EstimateTimeoutForRapid(double distanceMm, double rapidMmMin, int minMs = 5000)
        {
if (rapidMmMin <= 0) return minMs;
            
 double minutes = Math.Abs(distanceMm) / rapidMmMin;
    int estimated = (int)(minutes * 60000) + 1500; // Add 1.5s safety margin
 return Math.Max(minMs, estimated);
        }

        /// <summary>
   /// Validate probe measurement tolerance
        /// </summary>
  public static (bool valid, double tolerance, int indexA, int indexB) ValidateMeasurements(
       double[] measurements,
  double toleranceThreshold,
     int startIndex = 0)
     {
      if (measurements == null || measurements.Length < 2)
  return (false, double.MaxValue, -1, -1);

  double minDiff = double.MaxValue;
   int bestI = -1, bestJ = -1;

            // Find pair with smallest difference
       for (int i = Math.Max(0, startIndex); i < measurements.Length - 1; i++)
            {
  for (int j = i + 1; j < measurements.Length; j++)
    {
      double diff = Math.Abs(measurements[i] - measurements[j]);
 if (diff < minDiff)
     {
     minDiff = diff;
      bestI = i;
  bestJ = j;
         }
    }
    }

      bool valid = minDiff < toleranceThreshold && bestI >= 0 && bestJ >= 0;
 return (valid, minDiff, bestI, bestJ);
   }

   /// <summary>
  /// Calculate average of two measurements
   /// </summary>
     public static double AveragePair(double a, double b)
        {
            return (a + b) / 2.0;
        }

        /// <summary>
   /// Format coordinate for G-code command
 /// </summary>
        public static string FormatCoordinate(double value, int decimals = 3)
        {
   return value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
   }

   /// <summary>
    /// Build axis movement command
        /// </summary>
     public static string BuildAxisMove(char axis, double distanceMm)
        {
     return $"{char.ToUpper(axis)}{FormatCoordinate(distanceMm)}";
   }

   /// <summary>
        /// Build probe command
 /// </summary>
 public static string BuildProbeCommand(char axis, double distanceMm, int feedRate)
  {
    return $"G38.2 {BuildAxisMove(axis, distanceMm)} F{feedRate}";
        }

  /// <summary>
     /// Build retract (move away) command
     /// </summary>
  public static string BuildRetractCommand(char axis, double distanceMm, bool isRapid = true)
        {
    string moveType = isRapid ? "G00" : "G01";
return $"{moveType} {BuildAxisMove(axis, distanceMm)}";
     }

        /// <summary>
        /// Execute sequence of probe operations with logging
      /// </summary>
  public static async Task<bool> ExecuteProbeSequence(
            Func<string, Task<bool>> sendCommand,
    Func<string> getStatus,
    ProbeSequenceStep[] steps,
            Action<string> logger = null,
            string sequenceName = null)
        {
            foreach (var step in steps)
            {
                logger?.Invoke($"> {step.Description}");

                if (!await sendCommand(step.Command))
                {
                    logger?.Invoke($"> ❌ Command failed: {step.Command}");
                    return false;
                }

                if (step.WaitForIdle)
                {
                    if (!await WaitForIdleAsync(getStatus, step.TimeoutMs, step.Tag, logger))
                    {
                        logger?.Invoke($"> ❌ Idle wait failed for: {step.Tag}");
                        return false;
                    }
                }

                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs);
                }
            }

            return true;
      }
    }

    /// <summary>
    /// Represents a step in a probe sequence
    /// </summary>
    public class ProbeSequenceStep
    {
public string Command { get; set; }
   public string Description { get; set; }
   public string Tag { get; set; }
public bool WaitForIdle { get; set; } = true;
  public int TimeoutMs { get; set; } = 15000;
   public int DelayMs { get; set; } = 0;

   public ProbeSequenceStep(string command, string description, string tag = null)
      {
 Command = command;
    Description = description;
 Tag = tag ?? description;
    }
    }
}
