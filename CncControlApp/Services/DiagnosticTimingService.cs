using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace CncControlApp.Services
{
 /// <summary>
 /// Simple diagnostic timing collector for quick measurements.
 /// Record short event keys with timestamps and produce an aggregated report.
 /// Intended for temporary debugging — lightweight and thread-safe.
 /// </summary>
 public static class DiagnosticTimingService
 {
 private static readonly ConcurrentQueue<(string Key, DateTime Ts)> _events = new ConcurrentQueue<(string, DateTime)>();
 private const int MaxEvents =2000; // keep bounded

 public static void Record(string key)
 {
 try
 {
 var ts = DateTime.UtcNow;
 _events.Enqueue((key, ts));
 // trim if too large
 while (_events.Count > MaxEvents && _events.TryDequeue(out _)) { }
 }
 catch { }
 }

 public static IEnumerable<(string Key, DateTime Ts)> Snapshot()
 {
 return _events.ToArray();
 }

 /// <summary>
 /// Get detailed statistics: count, duration, avg interval, min/max interval per key
 /// </summary>
 public static Dictionary<string, object> GetDetailedStats()
 {
 var items = Snapshot().ToList();
 if (items.Count == 0) return new Dictionary<string, object>();

 var result = new Dictionary<string, object>();
 var grouped = items
 .GroupBy(i => i.Key)
 .Select(g => new
 {
 Key = g.Key,
 Events = g.ToList(),
 Count = g.Count(),
 First = g.First().Ts,
 Last = g.Last().Ts,
 Duration = (g.Last().Ts - g.First().Ts).TotalMilliseconds
 })
 .OrderByDescending(g => g.Count)
 .ToList();

 foreach (var g in grouped)
 {
 var intervals = new List<double>();
 for (int i = 1; i < g.Events.Count; i++)
 intervals.Add((g.Events[i].Ts - g.Events[i - 1].Ts).TotalMilliseconds);

 double avgMs = intervals.Count > 0 ? intervals.Average() : 0;
 double minMs = intervals.Count > 0 ? intervals.Min() : 0;
 double maxMs = intervals.Count > 0 ? intervals.Max() : 0;

 result[g.Key] = new
 {
 Count = g.Count,
 DurationMs = Math.Round(g.Duration, 1),
 AvgIntervalMs = Math.Round(avgMs, 1),
 MinIntervalMs = Math.Round(minMs, 1),
 MaxIntervalMs = Math.Round(maxMs, 1),
 FrequencyPerSec = Math.Round((g.Count * 1000.0) / (g.Duration > 0 ? g.Duration : 1), 2)
 };
 }

 return result;
 }

 public static string BuildReport()
 {
 var items = Snapshot().ToList();
 if (items.Count == 0) return "[DIAG] No timing events recorded.";

 var stats = GetDetailedStats();
 var sb = new System.Text.StringBuilder();
 sb.AppendLine($"[DIAG] Timing Report - total events: {items.Count}, unique keys: {stats.Count}");
 foreach (var kvp in stats)
 {
 dynamic stat = kvp.Value;
 sb.AppendLine($" - {kvp.Key}: count={stat.Count}, duration_ms={stat.DurationMs}, avg={stat.AvgIntervalMs}ms [min={stat.MinIntervalMs}, max={stat.MaxIntervalMs}], freq={stat.FrequencyPerSec}/sec");
 }

 TimeSpan totalSpan = items.Max(i => i.Ts) - items.Min(i => i.Ts);
 sb.AppendLine($"[DIAG] Overall span: {totalSpan.TotalSeconds:F2}s");
 return sb.ToString();
 }

 /// <summary>
 /// Build the diagnostic report and save to a file. If path is null, writes to temp directory with timestamped filename.
 /// Returns the file path on success, or null on failure.
 /// </summary>
 public static string SaveReportToFile(string path = null)
 {
 try
 {
 string report = BuildReport();
 if (string.IsNullOrEmpty(path))
 {
 string fileName = $"diagnostic_timing_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.txt";
 string dir = Path.GetTempPath();
 path = Path.Combine(dir, fileName);
 }
 File.WriteAllText(path, report);
 return path;
 }
 catch
 {
 return null;
 }
 }
 }
}
