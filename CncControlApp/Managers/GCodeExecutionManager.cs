using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CncControlApp.Services;

namespace CncControlApp.Managers
{
    public class GCodeExecutionManager : INotifyPropertyChanged, IDisposable
    {
        // Dependencies
        private readonly ConnectionManager _connectionManager;
        private readonly UIPropertiesManager _uiManager;
        private readonly Action<string, Exception, ErrorHandlingService.ErrorSeverity> _logError;
        private readonly Action<string> _log;
        private readonly Func<string> _getMachineStatus;
        private readonly Action<string> _setMachineStatus;
        private readonly Func<double> _getCurrentWorkZ; // Delegate to get current WorkZ

        // State
        private bool _isGCodeRunning;
        private int _currentGCodeLineIndex;
        private CancellationTokenSource _executionCts;
        private bool _internalStreaming;
        private bool _wasStopped;

        // Per-line progress and resume support
        private int _lastCompletedLineIndex = -1;
        private int _currentlyExecutingLineIndex = -1;

        // Estimation
        private readonly Dictionary<int, double> _segmentEstimatedTimes = new Dictionary<int, double>();
        private readonly HashSet<int> _completedSegments = new HashSet<int>();
        private double _totalEstimatedMinutes;
        private double _completedMinutes;
        private TimeSpan _totalEstimatedTime = TimeSpan.Zero;
        private DateTime _executionStartTime;
        private double _overrideRemainingMinutes;
        private bool _hasOverrideAdjustment;
        private int _currentFeedOverridePercent = 100;
        
        // Live timer fields for real elapsed time
        private System.Windows.Threading.DispatcherTimer _liveElapsedTimer;
        private double _liveElapsedSeconds = 0;

        // Modal values
        private double _currentModalFeed;
        private double _currentModalSpindle;
        private double _currentExecutionZ; // Live Z level during execution
        
        // Modal value tracking regex patterns
        private static readonly Regex FeedRatePattern = new Regex(@"F(\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex SpindleSpeedPattern = new Regex(@"S(\d+\.?\d*)", RegexOptions.Compiled);

        // Constants
        private const int MAX_FILE_SIZE_MB = 10;
        private const int MAX_LINE_COUNT = 50000;
        private const int MAX_GCODE_LINE_LENGTH = 256;
        private const int MAX_ERROR_COUNT = 5;
        private const int TARGET_BUFFER_SIZE = 15;
        
        // Byte-based buffer limit (800 bytes for larger buffer)
        private const int MAX_BUFFER_BYTES = 800;

        public GCodeExecutionManager(
            ConnectionManager connectionManager,
            UIPropertiesManager uiManager,
            Action<string, Exception, ErrorHandlingService.ErrorSeverity> logError,
            Action<string> addLogMessage,
            Func<string> getMachineStatus,
            Action<string> setMachineStatus,
            Func<double> getCurrentWorkZ = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
            _logError = logError ?? throw new ArgumentNullException(nameof(logError));
            _log = addLogMessage ?? throw new ArgumentNullException(nameof(addLogMessage));
            _getMachineStatus = getMachineStatus ?? throw new ArgumentNullException(nameof(getMachineStatus));
            _setMachineStatus = setMachineStatus ?? throw new ArgumentNullException(nameof(setMachineStatus));
            _getCurrentWorkZ = getCurrentWorkZ; // Can be null
            _uiManager.PropertyChanged += OnUIManagerPropertyChanged;
            
            // Initialize live elapsed timer (ticks every 500 ms for smooth updates)
            _liveElapsedTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _liveElapsedTimer.Tick += LiveElapsedTimer_Tick;
        }

        // Events
        public event EventHandler ExecutionStatusChanged;
        public event EventHandler<string> FileLoaded;
        public event EventHandler<bool> ExecutionCompleted;
        public event EventHandler<(string Command, string MachineStatus)> CommandBlockedDueToHold;
        public event EventHandler<int> LineCompleted;
        public event EventHandler<string> Paused;

        // Properties used by UI
        public bool IsGCodeRunning
        {
            get => _isGCodeRunning;
            private set
            {
                if (_isGCodeRunning != value)
                {
                    _isGCodeRunning = value;
                    RaisePropertyChanged();
                    UpdateExecutionProperties();
                    RaiseExecutionStatusChanged();
                    
                    // Start/stop the live elapsed timer based on running state
                    if (_isGCodeRunning)
                        _liveElapsedTimer?.Start();
                    else
                        _liveElapsedTimer?.Stop();
                }
            }
        }
        public int CurrentGCodeLineIndex
        {
            get => _currentGCodeLineIndex;
            private set { if (_currentGCodeLineIndex != value) { _currentGCodeLineIndex = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExecutionProgress)); RaisePropertyChanged(nameof(ExecutionProgressText)); } }
        }
        public double ExecutionProgress => _uiManager.GCodeLines.Count ==0 ?0 : (double)(LastCompletedLineIndex +1) / _uiManager.GCodeLines.Count *100.0;
        public string ExecutionProgressText => _uiManager.GCodeLines.Count ==0 ? "0 /0" : $"{Math.Max(0, LastCompletedLineIndex +1)} / {_uiManager.GCodeLines.Count}";
        public bool CanStartExecution => IsConnected && IsGCodeLoaded && !IsGCodeRunning && !CanContinueExecution;
        public bool CanPauseExecution
        {
            get
            {
                if (!IsConnected) return false;
                var s = GetMachineStatusSafe();
                return s.StartsWith("Run", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
            }
        }
        public bool CanContinueExecution => IsConnected && !_wasStopped && !IsGCodeRunning && StatusIsHold(GetMachineStatusSafe());
        public bool CanStopExecution
        {
            get
            {
                if (!IsConnected) return false;
                var s = GetMachineStatusSafe();
                return s.StartsWith("Run", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
            }
        }
        public TimeSpan TotalEstimatedTime { get => _totalEstimatedTime; private set { _totalEstimatedTime = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(RemainingTimeText)); RaisePropertyChanged(nameof(AdjustedTotalPredictedTimeText)); } }
        public DateTime ExecutionStartTime { get => _executionStartTime; private set { _executionStartTime = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ElapsedTimeText)); RaisePropertyChanged(nameof(RemainingTimeText)); } }
        
        // Live elapsed time (clock-based, not segment-based)
        public double LiveElapsedSeconds
        {
            get => _liveElapsedSeconds;
            private set
            {
                if (Math.Abs(_liveElapsedSeconds - value) > 0.1)
                {
                    _liveElapsedSeconds = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(ElapsedTimeText));
                    RaisePropertyChanged(nameof(RemainingTimeText));
                    RaisePropertyChanged(nameof(ExecutionProgressTime));
                }
            }
        }
        
        public string ElapsedTimeText
        {
            get
            {
                // Show live elapsed time whenever running, even if total estimate is not yet available
                if (!IsGCodeRunning)
                    return "00:00:00";
                return TimeSpan.FromSeconds(_liveElapsedSeconds).ToString(@"hh\:mm\:ss");
            }
        }
        
        public string RemainingTimeText
        {
            get
            {
                if (!IsGCodeRunning) return "--:--:--";
                if (_totalEstimatedMinutes <= 0) return "--:--:--";
                
                double totalMinutes = _totalEstimatedMinutes;
                if (_currentFeedOverridePercent != 100 && _currentFeedOverridePercent > 0)
                {
                    // Adjust total time by feed override
                    totalMinutes = _totalEstimatedMinutes * 100.0 / _currentFeedOverridePercent;
                }
                
                double elapsedMinutes = _liveElapsedSeconds / 60.0;
                double remaining = totalMinutes - elapsedMinutes;
                if (remaining < 0) remaining = 0;
                
                return TimeSpan.FromMinutes(remaining).ToString(@"hh\:mm\:ss");
            }
        }
        
        // Adjusted total predicted time (with feed override applied)
        public string AdjustedTotalPredictedTimeText
        {
            get
            {
                if (_totalEstimatedMinutes <= 0) return "00:00:00";
                
                double adjustedMinutes = _totalEstimatedMinutes;
                if (_currentFeedOverridePercent != 100 && _currentFeedOverridePercent > 0)
                {
                    adjustedMinutes = _totalEstimatedMinutes * 100.0 / _currentFeedOverridePercent;
                }
                
                return TimeSpan.FromMinutes(adjustedMinutes).ToString(@"hh\:mm\:ss");
            }
        }
        
        // Time-based execution progress (percentage)
        public double ExecutionProgressTime
        {
            get
            {
                if (!IsGCodeRunning || _totalEstimatedMinutes <= 0)
                    return 0;
                
                double totalMinutes = _totalEstimatedMinutes;
                if (_currentFeedOverridePercent != 100 && _currentFeedOverridePercent > 0)
                {
                    totalMinutes = _totalEstimatedMinutes * 100.0 / _currentFeedOverridePercent;
                }
                
                double elapsedMinutes = _liveElapsedSeconds / 60.0;
                double progress = (elapsedMinutes / totalMinutes) * 100.0;
                
                return Math.Min(100.0, Math.Max(0, progress));
            }
        }

        public bool IsGCodeLoaded => _uiManager.IsGCodeLoaded;
        public string LoadedGCodeFile => _uiManager.LoadedGCodeFile;
        public ObservableCollection<string> GCodeLines => _uiManager.GCodeLines;
        public bool IsConnected => _connectionManager?.IsConnected ?? false;

        public int LastCompletedLineIndex { get => _lastCompletedLineIndex; private set { if (_lastCompletedLineIndex != value) { _lastCompletedLineIndex = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExecutionProgress)); RaisePropertyChanged(nameof(ExecutionProgressText)); } } }
        public int CurrentlyExecutingLineIndex { get => _currentlyExecutingLineIndex; private set { if (_currentlyExecutingLineIndex != value) { _currentlyExecutingLineIndex = value; RaisePropertyChanged(); } } }
        
        public double CurrentModalFeed 
        { 
            get => _currentModalFeed; 
            private set 
            { 
                if (Math.Abs(_currentModalFeed - value) > 0.001) 
                { 
                    _currentModalFeed = value; 
                    RaisePropertyChanged(); 
                } 
            } 
        }
        
        public double CurrentModalSpindle 
        { 
            get => _currentModalSpindle; 
            private set 
            { 
                if (Math.Abs(_currentModalSpindle - value) > 0.001) 
                { 
                    _currentModalSpindle = value; 
                    RaisePropertyChanged(); 
                } 
            } 
        }
        
        public double CurrentExecutionZ
        {
            get => _currentExecutionZ;
         private set
    {
       if (Math.Abs(_currentExecutionZ - value) > 0.001)
       {
          _currentExecutionZ = value;
               RaisePropertyChanged();
            
        // Recalculate current layer based on new Z
     UpdateCurrentExecutionLayer();
      }
       }
        }
     
        // New: Current layer number during execution
        private int _currentExecutionLayer = 0;
        private List<double> _uniqueZLayers = new List<double>(); // Cache of unique Z values
        
        public int CurrentExecutionLayer
        {
     get => _currentExecutionLayer;
   private set
            {
          if (_currentExecutionLayer != value)
       {
    _currentExecutionLayer = value;
     RaisePropertyChanged();
            }
            }
        }
        
        /// <summary>
        /// Calculate which layer we're currently at based on WorkZ and unique Z levels.
        /// Layer 1 is the topmost (highest Z), counting downward.
        /// </summary>
 private void UpdateCurrentExecutionLayer()
        {
            try
          {
    if (_uniqueZLayers == null || _uniqueZLayers.Count == 0)
      {
                  CurrentExecutionLayer = 0;
               return;
                }
          
  // Unique Z levels should be sorted from highest to lowest (for top-down cutting)
         var sortedZ = _uniqueZLayers.OrderByDescending(z => z).ToList();
     
     // Find current layer: which level are we at or closest to?
      int layer = 0;
         for (int i = 0; i < sortedZ.Count; i++)
    {
  // If current Z is at or above this level, we're at this layer
         if (Math.Abs(_currentExecutionZ - sortedZ[i]) < 0.5 || _currentExecutionZ >= sortedZ[i])
             {
             layer = i + 1; // 1-based
             break;
 }
            }
           
  CurrentExecutionLayer = layer > 0 ? layer : sortedZ.Count;
        }
            catch
  {
  CurrentExecutionLayer = 0;
   }
        }
        
        /// <summary>
        /// Cache unique Z layers from segments for layer tracking during execution.
      /// Call this when G-Code is loaded to initialize layer tracking.
        /// </summary>
        public void CacheUniqueZLayers(List<GCodeSegment> segments)
   {
        try
            {
        _uniqueZLayers.Clear();
                
  if (segments == null || segments.Count == 0)
   {
       return;
 }
                
   // Extract all unique Z values with 1mm tolerance
        var zValues = new HashSet<double>();
        foreach (var segment in segments)
      {
 zValues.Add(Math.Round(segment.StartPoint.Z, 1));
      zValues.Add(Math.Round(segment.EndPoint.Z, 1));
}
  
   _uniqueZLayers = zValues.OrderByDescending(z => z).ToList();
 
    System.Diagnostics.Debug.WriteLine($"🔍 Cached {_uniqueZLayers.Count} unique Z layers for execution tracking");
          }
        catch (Exception ex)
    {
 System.Diagnostics.Debug.WriteLine($"❌ CacheUniqueZLayers error: {ex.Message}");
            }
        }

        // Operations
        public bool LoadGCodeFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _logError("LoadGCodeFile", new FileNotFoundException("File not found", filePath ?? "<null>"), ErrorHandlingService.ErrorSeverity.Error);
                    return false;
                }
                var fi = new FileInfo(filePath);
                if (fi.Length > MAX_FILE_SIZE_MB * 1024L * 1024L)
                {
                    _logError("LoadGCodeFile", new InvalidOperationException($"File too large: {fi.Length / 1024 / 1024}MB"), ErrorHandlingService.ErrorSeverity.Error);
                    return false;
                }
                var lines = File.ReadAllLines(filePath);
                if (lines.Length > MAX_LINE_COUNT)
                {
                    _logError("LoadGCodeFile", new InvalidOperationException($"Too many lines: {lines.Length}"), ErrorHandlingService.ErrorSeverity.Error);
                    return false;
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _uiManager.ClearGCodeLines();
                    int valid = 0;
                    foreach (var raw in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(raw)) { _uiManager.AddGCodeLine(raw.Trim()); valid++; }
                    }
                    _uiManager.UpdateGCodeLoadStatus(Path.GetFileName(filePath), valid);
                    CurrentGCodeLineIndex = 0; IsGCodeRunning = false; _log($"> ? G-Code loaded: {valid:N0} lines ({Path.GetFileName(filePath)})"); FileLoaded?.Invoke(this, Path.GetFileName(filePath)); UpdateExecutionProperties();
                });
                return true;
            }
            catch (Exception ex)
            {
                _logError("LoadGCodeFile", ex, ErrorHandlingService.ErrorSeverity.Critical);
                return false;
            }
        }

        private async Task<bool> WaitForControllerReadyAsync(int timeoutMs = 4000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string lastStatus = string.Empty;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var s = GetMachineStatusSafe();
                if (!string.Equals(s, lastStatus)) { _log($"> Status: {s}"); lastStatus = s; }
                if (!StatusIsAlarm(s) && !StatusIsHold(s)) return true;
                if (StatusIsAlarm(s)) await SendGCodeCommandAsync("$X");
                await Task.Delay(250);
                // Request central status update instead of sending '?' directly
                try { await StatusQueryService.QueryStatusOnce(250); } catch { }
            }
            return false;
        }

        public async Task<bool> RunGCodeAsync()
        {
       try
            {
 if (!CanStartExecution)
    {
        string reason = !IsConnected ? "Not connected" : !IsGCodeLoaded ? "No G-Code loaded" : IsGCodeRunning ? "Already running" : CanContinueExecution ? "Hold-resumable (use Continue)" : "Unknown";
 _logError("RunGCode", new InvalidOperationException(reason), ErrorHandlingService.ErrorSeverity.Warning);
       return false;
   }
        
       // ✅ CRITICAL FIX: Clear stop state before starting new execution
       _wasStopped = false;
      
          ClearLineAnnotations();
                LastCompletedLineIndex = -1; 
CurrentlyExecutingLineIndex = -1; 
         CurrentGCodeLineIndex = 0;
     
       if (!await WaitForControllerReadyAsync()) { _log("> ⚠️ Controller not ready (still Alarm/Hold) — aborting run"); return false; }
           
    // ✅ Reset execution timing BEFORE setting IsGCodeRunning to ensure timer starts fresh
    ExecutionStartTime = DateTime.Now;
         _liveElapsedSeconds = 0; // Reset live elapsed timer
          
                // ✅ Ensure timer is stopped and ready for restart
        _liveElapsedTimer?.Stop();
        
            // ✅ Reset modal values for fresh run
    _currentModalFeed = 0;
        _currentModalSpindle = 0;
      _currentFeedOverridePercent = 100;
           
// Set IsGCodeRunning which will start the timer via the setter
       if (!IsGCodeRunning) { IsGCodeRunning = true; if ((GetMachineStatusSafe() ?? string.Empty).StartsWith("Idle", StringComparison.OrdinalIgnoreCase)) { SetMachineStatusSafe("Run"); } }
      
 _internalStreaming = true; _executionCts?.Dispose(); _executionCts = new CancellationTokenSource();
      

      // ✅ Execute commands - this queues all commands into buffer and returns when queue is empty
    bool streamedOk = await ExecuteOpenBuildsStreamingAsync(_executionCts.Token);
 _internalStreaming = false;
      
      if (!streamedOk) 
      { 
          // Streaming failed - cancel execution
          if (IsGCodeRunning) IsGCodeRunning = false; 
          _log("> ❌ Streaming aborted"); 
          ExecutionCompleted?.Invoke(this, false); 
          UpdateExecutionProperties(); 
          return false; 
      }
        
_log("> ✅ All commands streamed successfully - machine executing buffered commands");
 
        // ✅ CRITICAL FIX: Just return true - machine will keep executing
      // IsGCodeRunning stays true until we manually set it false later
        // The completion popup will be triggered by external completion detection
      return true;
      }
     catch (Exception ex)
            {
    _internalStreaming = false; 
    if (IsGCodeRunning) IsGCodeRunning = false; 
    _logError("RunGCode", ex, ErrorHandlingService.ErrorSeverity.Critical); 
    ExecutionCompleted?.Invoke(this, false); 
    UpdateExecutionProperties(); 
    return false;
     }
      finally { _executionCts?.Dispose(); _executionCts = null; }
      }

        public async Task<bool> PauseGCodeAsync()
        {
            if (!CanPauseExecution) return false;
            try
            {
                await _connectionManager.SendGCodeCommandAsync("!");
                SetMachineStatusSafe("Hold");
                _executionCts?.Cancel(); _internalStreaming = false; if (IsGCodeRunning) IsGCodeRunning = false; _wasStopped = false;
                var msg = $"Paused (Hold) at line {LastCompletedLineIndex + 1} (executing: {CurrentlyExecutingLineIndex + 1})";
                _log($"> ?? {msg}"); Paused?.Invoke(this, msg); UpdateExecutionProperties(); return true;
            }
            catch (Exception ex) { _logError("PauseGCode", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
        }

        public async Task<bool> ContinueGCodeAsync()
        {
            var status = GetMachineStatusSafe();
            if (!(status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase)) || _wasStopped) return false;
            try
            {
                bool sent = await _connectionManager.SendGCodeCommandAsync("~"); if (!sent) return false;
                SetMachineStatusSafe("Run"); if (!IsGCodeRunning) IsGCodeRunning = true;
                _executionCts?.Dispose(); _executionCts = new CancellationTokenSource(); _internalStreaming = true;
                _ = Task.Run(() => ExecuteOpenBuildsStreamingAsync(_executionCts.Token));
                _log("> ?? Continued (streaming resumed)"); UpdateExecutionProperties(); return true;
            }
            catch (Exception ex) { _logError("ContinueGCode", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
        }

        public async Task<bool> StopGCodeAsync()
        {
            if (!CanStopExecution) return false;
            try
            {
                await _connectionManager.SendGCodeCommandAsync("!"); SetMachineStatusSafe("Hold");
                _executionCts?.Cancel(); _internalStreaming = false; if (IsGCodeRunning) IsGCodeRunning = false; _log("> ?? Feed Hold (!) sent - waiting for user decision..."); UpdateExecutionProperties(); return true;
            }
            catch (Exception ex) { _logError("StopGCode", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
        }

        public void CompleteStopSequence()
        {
            _wasStopped = true; if (IsGCodeRunning) IsGCodeRunning = false; CurrentGCodeLineIndex = 0; LastCompletedLineIndex = -1; CurrentlyExecutingLineIndex = -1; UpdateExecutionProperties();
        }

        // OpenBuilds-style event driven streaming with stall watchdog
        private async Task<bool> ExecuteOpenBuildsStreamingAsync(CancellationToken ct)
        {
            var inflight = new Queue<(string Cmd, int Line, int Bytes)>();
            int inflightBytes = 0;
            int errors = 0;
            bool aborted = false;
            DateTime lastOkTime = DateTime.UtcNow;
            DateTime lastStallLog = DateTime.MinValue;
            var allAckTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            object sync = new object();

            const int STALL_THRESHOLD_MS = 2500; // time without OK before considering stall
            const int STALL_LOG_INTERVAL_MS = 3000; // avoid spamming log
            const int INITIAL_FILL_DELAY_MS = 100; // TEST: Delay between commands during initial buffer fill

            bool initialFillDone = false;
            int initialFillCount = 0; // Track how many commands sent during initial fill
            
            // Fill buffer while space is available
            async Task FillAsync()
            {
                if (aborted) return;

                // Loop to fill buffer, but yield to UI/Events occasionally
                while (_internalStreaming && !_wasStopped && !ct.IsCancellationRequested)
                {
                    string cmd = null;
                    int uiIdx = -1;
                    int bytes = 0;

                    lock (sync)
                    {
                        if (CurrentGCodeLineIndex >= GCodeLines.Count) break; // Done sending
                        
                        // INITIAL FILL: Fill to TARGET_BUFFER_SIZE, then PING-PONG (1 OK = 1 command)
                        int maxInflight = initialFillDone ? 1 : TARGET_BUFFER_SIZE;
                        if (inflight.Count >= maxInflight) break; 

                        // Peek next command to check size
                        if (!TryGetNextValidCommand(out cmd, out uiIdx))
                        {
                            break; // Should not happen if index < count
                        }

                        // Format command to 3 decimal places to avoid controller precision errors (error:33)
                        cmd = FormatGCodeLine(cmd);

                        bytes = 0; // Not used in Ping-Pong mode

                        // Optimistically add to inflight to reserve space immediately
                        inflight.Enqueue((cmd, uiIdx, bytes));
                        inflightBytes += bytes;
                        if (inflight.Count == 1) CurrentlyExecutingLineIndex = uiIdx;
                    }

                    // Send outside lock (I/O operation)
                    ParseModalValues(cmd);
                    bool sent = await _connectionManager.SendGCodeCommandAsync(cmd);

                    // TEST: Add delay ONLY during initial buffer fill to let controller process setup commands
                    if (!initialFillDone)
                    {
                        initialFillCount++;
                        await Task.Delay(INITIAL_FILL_DELAY_MS);
                        System.Diagnostics.Debug.WriteLine($"🔄 Initial fill #{initialFillCount}: {cmd} (waited {INITIAL_FILL_DELAY_MS}ms)");
                    }

                    if (!sent)
                    {
                        lock (sync)
                        {
                            // Sending failed, we must abort
                            MarkLineError(uiIdx, "send failed");
                            errors++;
                            aborted = true;
                            allAckTcs.TrySetResult(false);
                        }
                        return;
                    }

                    // Yield to allow UI/Events to process, preventing lock starvation
                    await Task.Yield();
                }
                
                // Mark initial fill as done after first FillAsync completes
                if (!initialFillDone)
                {
                    initialFillDone = true;
                    _log($"> 🧪 TEST: Initial buffer fill complete ({initialFillCount} commands with {INITIAL_FILL_DELAY_MS}ms delay each)");
                }

                lock (sync)
                {
                    if (CurrentGCodeLineIndex >= GCodeLines.Count && inflight.Count == 0 && !aborted)
                    {
                        allAckTcs.TrySetResult(true);
                    }
                }
            }

            // Response callback
            void OnResp(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return;
                var lines = raw.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                bool needRefill = false;

                lock (sync)
                {
                    foreach (var ln in lines)
                    {
                        var line = ln.Trim(); if (line.Length == 0) continue; var low = line.ToLowerInvariant();
                        if (low == "ok")
                        {
                            lastOkTime = DateTime.UtcNow;
                            if (inflight.Count > 0)
                            {
                                var item = inflight.Dequeue(); 
                                inflightBytes = Math.Max(0, inflightBytes - item.Bytes);
                                MarkLineCompleted(item.Line); 
                                LastCompletedLineIndex = item.Line; 
                                CurrentlyExecutingLineIndex = inflight.Count > 0 ? inflight.Peek().Line : -1;
                                needRefill = true;
                            }
                        }
                        else if (low.StartsWith("error"))
                        {
                            if (inflight.Count > 0)
                            {
                                var item = inflight.Dequeue(); 
                                inflightBytes = Math.Max(0, inflightBytes - item.Bytes);
                                string detail = line;
                                if (line.Contains("error:3")) detail = $"error:3 (Unsupported) '{item.Cmd}'";
                                MarkLineError(item.Line, detail);
                                needRefill = true;
                            }
                            errors++;
                            // IGNORE ERRORS: Log and continue streaming
                            _log($"> ⚠️ Error ignored: {line}");
                        }
                        else if (low.StartsWith("alarm"))
                        {
                            if (inflight.Count > 0) MarkLineError(inflight.Peek().Line, line);
                            aborted = true; allAckTcs.TrySetResult(false);
                        }
                    }

                    // Check completion inside lock
                    if (CurrentGCodeLineIndex >= GCodeLines.Count && inflight.Count == 0 && !aborted)
                    {
                        allAckTcs.TrySetResult(true);
                    }
                }

                if (needRefill)
                {
                    // Fire and forget refill on thread pool to avoid blocking the event handler
                    _ = Task.Run(FillAsync);
                }
            }

            // Stall watchdog
            async Task WatchdogAsync()
            {
                while (_internalStreaming && !ct.IsCancellationRequested && !aborted && !allAckTcs.Task.IsCompleted)
                {
                    await Task.Delay(500, ct).ContinueWith(_ => { });
                    var now = DateTime.UtcNow;
                    int pendingCount = 0;
                    lock (sync) { pendingCount = inflight.Count; }

                    if (pendingCount > 0 && (now - lastOkTime).TotalMilliseconds > STALL_THRESHOLD_MS)
                    {
                        if ((now - lastStallLog).TotalMilliseconds > STALL_LOG_INTERVAL_MS)
                        {
                            lastStallLog = now;
                            _log($"> ⚠️ STALL: No 'ok' for {(now - lastOkTime).TotalMilliseconds:F0}ms; inflight={pendingCount}");
                            // Poke controller with status query
                            _ = _connectionManager.SendGCodeCommandAsync("?");
                        }
                    }
                }
            }

            _connectionManager.GcodeSender.ResponseReceived += OnResp;
            try
            {
                await FillAsync(); // initial fill
                
                // Check immediate completion
                lock (sync)
                {
                    if (CurrentGCodeLineIndex >= GCodeLines.Count && inflight.Count == 0 && !aborted)
                    {
                        allAckTcs.TrySetResult(true);
                    }
                }

                var wdTask = WatchdogAsync();
                using (ct.Register(() => { allAckTcs.TrySetCanceled(); }))
                {
                    bool success;
                    try { success = await allAckTcs.Task; } // Wait for ACKs
                    catch (TaskCanceledException) { success = false; }
                    if (!success || aborted) return false;
                }
                _log($"> ✅ OpenBuilds streaming COMPLETED (All OKs received)");
                return true;
            }
            finally
            {
                _connectionManager.GcodeSender.ResponseReceived -= OnResp;
            }
        }

        // Parse and track modal values from G-code commands
        private void ParseModalValues(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;
                
            try
            {
                string upper = command.ToUpperInvariant();
                
                // Only process G0, G1, G2, G3 commands for feed rate
                if (upper.Contains("G0") || upper.Contains("G00") || 
                    upper.Contains("G1") || upper.Contains("G01") ||
                    upper.Contains("G2") || upper.Contains("G02") ||
                    upper.Contains("G3") || upper.Contains("G03"))
                {
                    Match feedMatch = FeedRatePattern.Match(upper);
                    if (feedMatch.Success && feedMatch.Groups.Count > 1)
                    {
                        if (double.TryParse(feedMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double feedRate))
                        {
                            if (feedRate > 0)
                            {
                                CurrentModalFeed = feedRate;
                            }
                        }
                    }
                }
                
                // Check for S (spindle speed) parameter
                if (upper.Contains("M3") || upper.Contains("M03") || 
                    upper.Contains("M4") || upper.Contains("M04"))
                {
                    Match spindleMatch = SpindleSpeedPattern.Match(upper);
                    if (spindleMatch.Success && spindleMatch.Groups.Count > 1)
                    {
                        if (double.TryParse(spindleMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double spindleSpeed))
                        {
                            if (spindleSpeed > 0)
                            {
                                CurrentModalSpindle = spindleSpeed;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // Helpers
        private void UpdateExecutionProperties()
        {
            RaisePropertyChanged(nameof(CanStartExecution)); RaisePropertyChanged(nameof(CanPauseExecution)); RaisePropertyChanged(nameof(CanContinueExecution)); RaisePropertyChanged(nameof(CanStopExecution));
        }
        private string GetMachineStatusSafe() { try { return _getMachineStatus()?.Trim() ?? string.Empty; } catch { return string.Empty; } }
        private static bool StatusIsHold(string s) => s.StartsWith("hold", StringComparison.OrdinalIgnoreCase);
        private static bool StatusIsAlarm(string s) => s.StartsWith("alarm", StringComparison.OrdinalIgnoreCase);

        private void SetMachineStatusSafe(string status)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                    disp.BeginInvoke(new Action(() => _setMachineStatus(status)));
                else
                    _setMachineStatus(status);
            }
            catch { }
        }

        private void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    disp.BeginInvoke(new Action(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name))));
                }
                else
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
            catch { }
        }
        private void RaiseExecutionStatusChanged()
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                    disp.BeginInvoke(new Action(() => ExecutionStatusChanged?.Invoke(this, EventArgs.Empty)));
                else
                    ExecutionStatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        private void OnUIManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UIPropertiesManager.IsGCodeLoaded)) { RaisePropertyChanged(nameof(IsGCodeLoaded)); UpdateExecutionProperties(); }
            else if (e.PropertyName == nameof(UIPropertiesManager.LoadedGCodeFile)) { RaisePropertyChanged(nameof(LoadedGCodeFile)); }
        }

        private bool TryGetNextValidCommand(out string command, out int uiLineIndex)
        {
            command = null; uiLineIndex = -1;
            while (CurrentGCodeLineIndex < GCodeLines.Count)
            {
                string raw = GCodeLines[CurrentGCodeLineIndex]; CurrentGCodeLineIndex++;
                if (string.IsNullOrWhiteSpace(raw)) continue; string trimmed = raw.Trim(); if (trimmed.StartsWith(";") || trimmed.StartsWith("(")) continue;
                command = trimmed; uiLineIndex = CurrentGCodeLineIndex - 1; return true;
            }
            return false;
        }
        private void AnnotateLine(int lineIndex, string tag)
        {
            try
            {
                if (lineIndex < 0 || lineIndex >= GCodeLines.Count) return; if (string.IsNullOrWhiteSpace(tag)) return;
                var disp = Application.Current?.Dispatcher;
                Action update = () => { var current = GCodeLines[lineIndex] ?? string.Empty; if (current.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return; GCodeLines[lineIndex] = $"{current}  {tag}"; };
                if (disp != null && !disp.CheckAccess()) disp.BeginInvoke(update); else update();
            }
            catch { }
        }
        private void MarkLineError(int lineIndex, string errorText) => AnnotateLine(lineIndex, $"? {errorText}");
        private void MarkLineNote(int lineIndex, string noteText) => AnnotateLine(lineIndex, $"?? {noteText}");
        public void MarkLineCompleted(int completedLineIndex) { try { LineCompleted?.Invoke(this, completedLineIndex); } catch { } }
        private void ClearLineAnnotations()
        {
            try
            {
                if (GCodeLines == null || GCodeLines.Count == 0) return;
                var disp = Application.Current?.Dispatcher;
                Action clear = () =>
                {
                    for (int i = 0; i < GCodeLines.Count; i++)
                    {
                        var line = GCodeLines[i]; if (string.IsNullOrWhiteSpace(line)) continue; int tagIndex = line.IndexOf("  ?"); if (tagIndex < 0) tagIndex = line.IndexOf("  ??"); if (tagIndex < 0) tagIndex = line.IndexOf("  aborted due"); if (tagIndex < 0) tagIndex = line.IndexOf("  unconfirmed"); if (tagIndex >= 0) { var cleaned = line.Substring(0, tagIndex).TrimEnd(); if (cleaned != line) GCodeLines[i] = cleaned; }
                    }
                };
                if (disp != null && !disp.CheckAccess()) disp.BeginInvoke(clear); else clear();
            }
            catch { }
        }

        // Estimation helpers
        public void CalculateEstimatedExecutionTime(List<GCodeSegment> segments) => BuildTimeMapFromSegments(segments);
        public void BuildTimeMapFromSegments(List<GCodeSegment> segments)
        {
            _segmentEstimatedTimes.Clear(); _completedSegments.Clear(); _completedMinutes = 0; _totalEstimatedMinutes = 0; if (segments == null) { TotalEstimatedTime = TimeSpan.Zero; return; }
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i]; double distance = 0; try { distance = seg?.GetActualDistance() ?? 0; } catch { }
                double feed = seg?.FeedRate ?? 0; if (feed <= 0) feed = 500; double minutes = (feed > 0 && distance > 0) ? distance / feed : 0; _segmentEstimatedTimes[i] = minutes; _totalEstimatedMinutes += minutes;
            }
            TotalEstimatedTime = TimeSpan.FromMinutes(_totalEstimatedMinutes);
        }
        public void RecalculateRemainingTimeWithFeedOverride(int feedPercent)
        {
            try
            {
    if (!IsGCodeRunning || _segmentEstimatedTimes.Count == 0) 
{ 
            _hasOverrideAdjustment = false; 
    RaisePropertyChanged(nameof(RemainingTimeText)); 
        RaisePropertyChanged(nameof(AdjustedTotalPredictedTimeText)); 
         RaisePropertyChanged(nameof(ExecutionProgressTime)); 
        return; 
           }
          if (feedPercent <= 0) feedPercent = 100;
    _currentFeedOverridePercent = feedPercent;
       _hasOverrideAdjustment = (feedPercent != 100);
 
              // ✅ CRITICAL FIX: Trigger re-calculation by raising ALL dependent properties
      // This ensures UI bindings refresh when feed override changes
                RaisePropertyChanged(nameof(RemainingTimeText));
       RaisePropertyChanged(nameof(AdjustedTotalPredictedTimeText));
       RaisePropertyChanged(nameof(ExecutionProgressTime));
     
    // Also trigger elapsed time to force full UI refresh
             RaisePropertyChanged(nameof(ElapsedTimeText));
        
            // Debug log to confirm the method is being called
     System.Diagnostics.Debug.WriteLine($"🔄 Feed override recalculated: {feedPercent}% (hasOverride={_hasOverrideAdjustment})");
        }
    catch (Exception ex)
            {
      System.Diagnostics.Debug.WriteLine($"❌ RecalculateRemainingTimeWithFeedOverride error: {ex.Message}");
                _hasOverrideAdjustment = false;
            }
        }

        // Public sending helpers expected by other UI components
        public async Task<bool> SendGCodeCommandAsync(string gcode)
        {
            if (string.IsNullOrWhiteSpace(gcode)) return false; var trimmed = gcode.Trim(); if (!IsConnected) return false; if (trimmed.Length > MAX_GCODE_LINE_LENGTH) return false;
            var status = GetMachineStatusSafe(); var upper = trimmed.ToUpperInvariant(); bool allowWhileLocked = upper == "$X" || upper == "$H" || upper == "?" || upper == "~" || upper == "!";
            if ((StatusIsHold(status) || StatusIsAlarm(status)) && !allowWhileLocked) { try { CommandBlockedDueToHold?.Invoke(this, (trimmed, status)); } catch { } return false; }
            
            // Parse and track modal values when sending commands manually
            ParseModalValues(trimmed);
            
            return await _connectionManager.SendGCodeCommandAsync(trimmed);
        }
        public async Task<bool> SendGCodeCommandWithConfirmationAsync(string gcode) => await SendGCodeCommandAsync(gcode);
        public async Task<bool> SendControlCharacterAsync(char c) { if (!IsConnected) return false; return await _connectionManager.SendControlCharacterAsync(c); }
        public void CancelExecution() { try { _executionCts?.Cancel(); } catch { } finally { _internalStreaming = false; UpdateExecutionProperties(); } }

        private void OnUIManagerPropertyChanged_Dupl(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UIPropertiesManager.IsGCodeLoaded)) { RaisePropertyChanged(nameof(IsGCodeLoaded)); UpdateExecutionProperties(); }
            else if (e.PropertyName == nameof(UIPropertiesManager.LoadedGCodeFile)) { RaisePropertyChanged(nameof(LoadedGCodeFile)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void Dispose()
        {
            try
            {
                _liveElapsedTimer?.Stop();
                _uiManager.PropertyChanged -= OnUIManagerPropertyChanged; _executionCts?.Cancel(); _executionCts?.Dispose(); _executionCts = null; _internalStreaming = false;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"GCodeExecutionManager dispose error: {ex.Message}"); }
        }

        public void ResetExecutionState()
        {
            _internalStreaming = false;
            _wasStopped = false;
            CurrentGCodeLineIndex = 0;
            LastCompletedLineIndex = -1;
            CurrentlyExecutingLineIndex = -1;
            _segmentEstimatedTimes.Clear();
            _completedSegments.Clear();
            _completedMinutes = 0;
            _totalEstimatedMinutes = 0;
            TotalEstimatedTime = TimeSpan.Zero;
            _overrideRemainingMinutes = 0;
            _hasOverrideAdjustment = false;
            _currentModalFeed = 0;
            _currentModalSpindle = 0;
            _currentFeedOverridePercent = 100;
            _liveElapsedSeconds = 0; // Reset live elapsed time
            
   // ✅ Ensure timer is stopped when resetting execution state
  _liveElapsedTimer?.Stop();
    
            RaisePropertyChanged(nameof(ElapsedTimeText));
            RaisePropertyChanged(nameof(RemainingTimeText));
            RaisePropertyChanged(nameof(AdjustedTotalPredictedTimeText));
            RaisePropertyChanged(nameof(ExecutionProgressTime));
            RaisePropertyChanged(nameof(CurrentModalFeed));
            RaisePropertyChanged(nameof(CurrentModalSpindle));
         UpdateExecutionProperties();
            RaiseExecutionStatusChanged();
        }
        
        /// <summary>
        /// Update currently executing line from controller-reported status (1-based line number).
        /// This prefers authoritative status over internal estimates when available.
        /// </summary>
        /// <param name="oneBasedLine">1-based line number from status report (<=0 will clear selection)</param>
        public void UpdateExecutingLineFromController(int oneBasedLine)
        {
            try
            {
                int idx = oneBasedLine - 1;
                if (oneBasedLine <= 0)
                {
                    idx = -1;
                }
                else if (GCodeLines != null && idx >= GCodeLines.Count)
                {
                    idx = GCodeLines.Count - 1;
                }
                CurrentlyExecutingLineIndex = idx;
            }
            catch { }
        }
        
        // Live elapsed timer tick handler
        private void LiveElapsedTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (IsGCodeRunning && ExecutionStartTime != DateTime.MinValue)
                {
                    var elapsed = DateTime.Now - ExecutionStartTime;
                    LiveElapsedSeconds = elapsed.TotalSeconds;
                    
                    // Update current Z level if delegate is provided
                    if (_getCurrentWorkZ != null)
                    {
                        try
                        {
                            double workZ = _getCurrentWorkZ();
                            CurrentExecutionZ = workZ;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
        
        // Helper to format GCode lines to 3 decimal places to avoid controller precision errors
        private string FormatGCodeLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;
            
            // Regex to find decimal numbers for axes and feed/speed and round them
            // Matches X, Y, Z, I, J, K, F, S followed by a number
            return System.Text.RegularExpressions.Regex.Replace(line, @"([XYZIJKFS])\s*(-?[0-9]*\.?[0-9]+)", m =>
            {
                string axis = m.Groups[1].Value;
                string numberStr = m.Groups[2].Value;
                
                if (double.TryParse(numberStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    // Round to 3 decimal places to avoid error:33
                    string rounded = Math.Round(val, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    return $"{axis}{rounded}";
                }
                return m.Value;
            }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}

