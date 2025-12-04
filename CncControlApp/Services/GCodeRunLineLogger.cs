using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace CncControlApp.Services
{
 public sealed class GCodeRunLineLogger : IDisposable
 {
 private static readonly Lazy<GCodeRunLineLogger> _inst = new Lazy<GCodeRunLineLogger>(() => new GCodeRunLineLogger());
 public static GCodeRunLineLogger Instance => _inst.Value;

 private TextWriter _writer;
 private string _logPath;
 private bool _active;
 private MainControll _mc;
 private object _mgr;

 // De-dupe error spam and avoid recursion
 private int _lastErrorLine = -1;
 private string _lastErrorReason = null;
 private DateTime _lastErrorTs = DateTime.MinValue;
 private bool _inLogCallback = false;
 private static readonly TimeSpan ErrorRepeatSuppress = TimeSpan.FromMilliseconds(400);

 private GCodeRunLineLogger() { }
 
 private ConnectionManager _cm;

 public void Begin(MainControll mc)
 {
 try
 {
 if (mc == null) return;
 
 // ✅ FIX: Clean up any existing subscriptions first to prevent duplicate logs
 CleanupSubscriptions();
 
 _mc = mc;
 _mgr = mc.GCodeManager;
 _cm = mc.ConnectionManagerInstance;
 OpenLogFile();
 _active = true;
 _lastLoggedRunState = null; // ✅ Reset run state tracking
 _lastErrorLine = -1; _lastErrorReason = null; _lastErrorTs = DateTime.MinValue;
 try { _mc.PropertyChanged += OnMainControllerPropertyChanged; } catch { }
 if (_mgr is INotifyPropertyChanged pc)
 {
 try { pc.PropertyChanged += OnManagerPropertyChanged; } catch { }
 }
 if (_mc.LogMessages is INotifyCollectionChanged col)
 {
 try { col.CollectionChanged += OnLogMessagesChanged; } catch { }
 }
 // Subscribe to ConnectionManager.LogMessageAdded for direct command logging
 if (_cm != null)
 {
 try { _cm.LogMessageAdded += OnConnectionLogMessage; } catch { }
 }
 WriteHeader();
 }
 catch { }
 }

 public void End()
 {
 try
 {
 _active = false;
 CleanupSubscriptions();
 _mc = null;
 _mgr = null;
 _cm = null;
 }
 catch { }
 finally
 {
 try { _writer?.Flush(); _writer?.Dispose(); } catch { }
 _writer = null;
 }
 }

 private void CleanupSubscriptions()
 {
 try
 {
 if (_mc != null)
 {
 try { _mc.PropertyChanged -= OnMainControllerPropertyChanged; } catch { }
 if (_mc.LogMessages is INotifyCollectionChanged col)
 {
 try { col.CollectionChanged -= OnLogMessagesChanged; } catch { }
 }
 }
 if (_mgr is INotifyPropertyChanged pc)
 {
 try { pc.PropertyChanged -= OnManagerPropertyChanged; } catch { }
 }
 if (_cm != null)
 {
 try { _cm.LogMessageAdded -= OnConnectionLogMessage; } catch { }
 }
 }
 catch { }
 }

 private void OpenLogFile()
 {
 try
 {
 var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Raptorex-Controller-Logs");
 Directory.CreateDirectory(baseDir);
 var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
 _logPath = Path.Combine(baseDir, $"gcode_run_{stamp}.log");
 _writer = TextWriter.Synchronized(new StreamWriter(_logPath, false, new UTF8Encoding(false)) { AutoFlush = true });
 }
 catch { }
 }

 private void WriteHeader()
 {
 try
 {
 if (_writer == null) return;
 var file = _mc?.LoadedGCodeFile ?? "<unknown>";
 _writer.WriteLine($"=== GCODE RUN LOG === {DateTime.Now:O}");
 _writer.WriteLine($"File: {file}");
 _writer.WriteLine("====================");
 }
 catch { }
 }

 // ✅ Track last logged run state to prevent duplicate RUN STARTED/ENDED logs
 private bool? _lastLoggedRunState = null;

 private void OnMainControllerPropertyChanged(object sender, PropertyChangedEventArgs e)
 {
 if (!_active) return;
 try
 {
 if (e.PropertyName == nameof(MainControll.IsGCodeRunning))
 {
 var running = _mc?.IsGCodeRunning == true;
 
 // ✅ Prevent duplicate log entries
 if (_lastLoggedRunState == running) return;
 _lastLoggedRunState = running;
 
 _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === {(running ? "RUN STARTED" : "RUN ENDED")} ===");
 if (!running) End();
 }
 // MachineStatus artık loglanmıyor - sadece hatalarda log tutulacak
 }
 catch { }
 }

 private void OnManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
 {
 // EXEC logging kaldırıldı - sadece hatalar loglanacak
 // Satır satır loglama çok fazla veri oluşturuyordu
 }

 private string TryGetLineText(int oneBasedIndex)
 {
 try
 {
 var lines = GetStringCollection(_mgr, "GCodeLines");
 if (lines == null || lines.Count ==0) return string.Empty;
 int i1 = oneBasedIndex -1;
 if (i1 >=0 && i1 < lines.Count) return lines[i1];
 if (oneBasedIndex >=0 && oneBasedIndex < lines.Count) return lines[oneBasedIndex];
 }
 catch { }
 return string.Empty;
 }

 private void OnLogMessagesChanged(object sender, NotifyCollectionChangedEventArgs e)
 {
 if (!_active) return;
 if (e.Action != NotifyCollectionChangedAction.Add) return;
 if (_inLogCallback) return; // guard re-entrancy
 _inLogCallback = true;
 try
 {
 foreach (var obj in e.NewItems)
 {
 var msg = obj as string;
 if (string.IsNullOrWhiteSpace(msg)) continue;
 var low = msg.ToLowerInvariant();
 
 // Sadece hataları logla - normal mesajlar loglanmıyor
 // Real error/alarm detection: avoid false positives like "Errors=0" or buffer stats
 bool isBufferStat = low.Contains("buffer stats") || low.Contains("errors=");
 bool isAlarm = low.Contains("alarm");
 bool isError = low.Contains("error:") || low.Contains("[err") || (low.StartsWith("error") && !low.StartsWith("errors="));
 if ((isAlarm || isError) && !isBufferStat)
 {
 int idx = GetIntProp(_mgr, "CurrentlyExecutingLineIndex");
 if (idx <=0) idx = GetIntProp(_mgr, "LastCompletedLineIndex");
 string lineTxt = TryGetLineText(idx);
 var now = DateTime.Now;
 // de-dupe same error quickly repeating
 if (idx == _lastErrorLine && string.Equals(_lastErrorReason, msg, StringComparison.OrdinalIgnoreCase)
 && (now - _lastErrorTs) < ErrorRepeatSuppress)
 {
 continue;
 }
 _lastErrorLine = idx;
 _lastErrorReason = msg;
 _lastErrorTs = now;
 _writer?.WriteLine($"[{now:HH:mm:ss.fff}] >>> DETECTED ERROR at line {idx}: {lineTxt}");
 _writer?.WriteLine($"[{now:HH:mm:ss.fff}] >>> REASON: {msg}");
 // DO NOT write back to UI logs to avoid recursion
 }
 }
 }
 catch { }
 finally { _inLogCallback = false; }
 }

 private static int GetIntProp(object obj, string name)
 {
 try
 {
 if (obj == null) return -1;
 var p = obj.GetType().GetProperty(name);
 if (p == null) return -1;
 var v = p.GetValue(obj);
 if (v == null) return -1;
 if (v is int i) return i;
 return Convert.ToInt32(v);
 }
 catch { return -1; }
 }

 private void OnConnectionLogMessage(string msg)
 {
 // SEND logging kaldırıldı - çok fazla veri oluşturuyordu
 // Sadece hatalar loglanıyor
 }

 private static ObservableCollection<string> GetStringCollection(object obj, string name)
 {
 try
 {
 if (obj == null) return null;
 var p = obj.GetType().GetProperty(name);
 if (p == null) return null;
 return p.GetValue(obj) as ObservableCollection<string>;
 }
 catch { return null; }
 }

 public void Dispose()
 {
 End();
 }
 }
}
