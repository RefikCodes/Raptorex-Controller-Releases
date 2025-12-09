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
using GrblStreamer.Events;

namespace CncControlApp.Managers
{
    public class GCodeExecutionManager : INotifyPropertyChanged, IDisposable
    {
        // Dependencies
        private readonly ConnectionManager _connectionManager;
        private readonly GrblStreamingService _streamingService;
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
        
        // Status query scope for GCode run (200ms while running)
        private IDisposable _gcodeRunScope;
        
        // Modal value tracking regex patterns
        private static readonly Regex FeedRatePattern = new Regex(@"F(\d+\.?\d*)", RegexOptions.Compiled);
        private static readonly Regex SpindleSpeedPattern = new Regex(@"S(\d+\.?\d*)", RegexOptions.Compiled);

        // Constants
        private const int MAX_FILE_SIZE_MB = 10;
        private const int MAX_LINE_COUNT = 50000;
        private const int MAX_GCODE_LINE_LENGTH = 256;
        private const int MAX_ERROR_COUNT = 5;
        
        // GRBL RX Buffer size (Character Counting Protocol - same as OpenBuilds)
        // Standard GRBL uses 127 bytes, we use 127 with safety margin
        private const int GRBL_RX_BUFFER_SIZE = 127;
        private const int BUFFER_SAFETY_MARGIN = 5;  // Keep some margin to avoid overflow
        private const int EFFECTIVE_BUFFER_SIZE = GRBL_RX_BUFFER_SIZE - BUFFER_SAFETY_MARGIN;  // 122 bytes usable

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
            
            // Initialize GrblStreamingService (from GrblStreamer library)
            _streamingService = new GrblStreamingService(_connectionManager);
            _streamingService.Initialize();
            _streamingService.LineCompleted += OnStreamingLineCompleted;
            _streamingService.JobCompleted += OnStreamingJobCompleted;
            _streamingService.ProgressUpdated += OnStreamingProgressUpdated;
            
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
                    {
                        _liveElapsedTimer?.Start();
                    }
                    else
                    {
                        _liveElapsedTimer?.Stop();
                        // ✅ Dispose status query scope when execution ends (200ms → 300ms)
                        try { _gcodeRunScope?.Dispose(); _gcodeRunScope = null; } catch { }
                    }
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
        public bool CanContinueExecution 
        { 
            get 
            {
                // ✅ FIX: Check streaming service pause state, not just machine Hold state
                // This allows resume even if GRBL reports Idle but streaming is paused
                if (!IsConnected) return false;
                if (_wasStopped) return false;
                if (IsGCodeRunning) return false;
                
                // Check if streaming service has paused work
                bool streamingServicePaused = _streamingService?.IsPaused ?? false;
                
                // Also check traditional Hold state
                bool machineInHold = StatusIsHold(GetMachineStatusSafe());
                
                return streamingServicePaused || machineInHold;
            }
        }
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
                // ✅ DEBUG: Log the values to understand what's happening
                System.Diagnostics.Debug.WriteLine($"⏱️ RemainingTimeText called: IsGCodeRunning={IsGCodeRunning}, TotalEstimatedMinutes={_totalEstimatedMinutes:F2}");
                
                if (!IsGCodeRunning) 
                {
                    System.Diagnostics.Debug.WriteLine($"⏱️ RemainingTimeText returning --:--:-- (not running)");
                    return "--:--:--";
                }
                if (_totalEstimatedMinutes <= 0) 
                {
                    System.Diagnostics.Debug.WriteLine($"⏱️ RemainingTimeText returning --:--:-- (no estimate)");
                    return "--:--:--";
                }
                
                double totalMinutes = _totalEstimatedMinutes;
                if (_currentFeedOverridePercent != 100 && _currentFeedOverridePercent > 0)
                {
                    // Adjust total time by feed override
                    totalMinutes = _totalEstimatedMinutes * 100.0 / _currentFeedOverridePercent;
                }
                
                double elapsedMinutes = _liveElapsedSeconds / 60.0;
                double remaining = totalMinutes - elapsedMinutes;
                if (remaining < 0) remaining = 0;
                
                var result = TimeSpan.FromMinutes(remaining).ToString(@"hh\:mm\:ss");
                System.Diagnostics.Debug.WriteLine($"⏱️ RemainingTimeText returning {result} (total={totalMinutes:F2}, elapsed={elapsedMinutes:F2})");
                return result;
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
            ErrorLogger.LogDebug($"RunGCodeAsync başladı - CanStartExecution={CanStartExecution}, IsConnected={IsConnected}, IsGCodeLoaded={IsGCodeLoaded}, IsGCodeRunning={IsGCodeRunning}");
       try
            {
 if (!CanStartExecution)
    {
        string reason = !IsConnected ? "Not connected" : !IsGCodeLoaded ? "No G-Code loaded" : IsGCodeRunning ? "Already running" : CanContinueExecution ? "Hold-resumable (use Continue)" : "Unknown";
        ErrorLogger.LogDebug($"RunGCode başlatılamadı: {reason}");
 _logError("RunGCode", new InvalidOperationException(reason), ErrorHandlingService.ErrorSeverity.Warning);
       return false;
   }
        
       // ✅ CRITICAL FIX: Clear stop state before starting new execution
       _wasStopped = false;
       ErrorLogger.LogDebug("Stop state temizlendi, execution başlıyor");
      
          ClearLineAnnotations();
                LastCompletedLineIndex = -1; 
CurrentlyExecutingLineIndex = -1; 
         CurrentGCodeLineIndex = 0;
     
       if (!await WaitForControllerReadyAsync()) { _log("> ⚠️ Controller not ready (still Alarm/Hold) — aborting run"); ErrorLogger.LogDebug("Controller ready değil - abort"); return false; }
           
    // ✅ Reset execution timing BEFORE setting IsGCodeRunning to ensure timer starts fresh
    ExecutionStartTime = DateTime.Now;
         _liveElapsedSeconds = 0; // Reset live elapsed timer
          
                // ✅ Ensure timer is stopped and ready for restart
        _liveElapsedTimer?.Stop();
        
            // ✅ Reset modal values for fresh run
    _currentModalFeed = 0;
        _currentModalSpindle = 0;
      _currentFeedOverridePercent = 100;
      
    // ✅ CRITICAL FIX: Reset completed segments tracking for fresh remaining time calculation
    _completedSegments.Clear();
    _completedMinutes = 0;
    _hasOverrideAdjustment = false;
    _overrideRemainingMinutes = 0;
    
    // ✅ DEBUG: Log the total estimated minutes before starting
    System.Diagnostics.Debug.WriteLine($"⏱️ RunGCodeAsync: _totalEstimatedMinutes={_totalEstimatedMinutes:F2} before starting");
    _log($"> ⏱️ Estimated time: {_totalEstimatedMinutes:F2} minutes");
           
    // Set IsGCodeRunning FIRST which will start the timer via the setter
    if (!IsGCodeRunning) { IsGCodeRunning = true; if ((GetMachineStatusSafe() ?? string.Empty).StartsWith("Idle", StringComparison.OrdinalIgnoreCase)) { SetMachineStatusSafe("Run"); } }
    
    // ✅ Force UI refresh for timing properties AFTER IsGCodeRunning is set
    // This ensures RemainingTimeText doesn't return "--:--:--" due to !IsGCodeRunning check
    RaisePropertyChanged(nameof(ElapsedTimeText));
    RaisePropertyChanged(nameof(RemainingTimeText));
    RaisePropertyChanged(nameof(AdjustedTotalPredictedTimeText));
    RaisePropertyChanged(nameof(ExecutionProgressTime));
    
    ErrorLogger.LogDebug($"GCode çalışma başladı - satır sayısı: {GCodeLines?.Count ?? 0}, TotalEstimatedMinutes: {_totalEstimatedMinutes:F2}");
      
       // ✅ GCode çalışırken status query interval'ını 200ms'ye ayarla
       // Scope field olarak tutulur, IsGCodeRunning=false olduğunda dispose edilir
       try { _gcodeRunScope?.Dispose(); _gcodeRunScope = App.MainController?.BeginScopedCentralStatusOverride(200); } catch { }
       
       _internalStreaming = true; _executionCts?.Dispose(); _executionCts = new CancellationTokenSource();
      
       // ✅ GrblStreamingService ile streaming başlat
       // GCode satırlarını servise yükle (GCodeLines zaten string koleksiyonu)
       _streamingService.LoadGCode(GCodeLines);
       
       // Streaming başlat (async fire-and-forget - completion event ile tamamlanacak)
       await _streamingService.StartAsync();
       
       _log("> ✅ GrblStreamer: Streaming started - machine executing buffered commands");
       ErrorLogger.LogDebug("GrblStreamer ile streaming başladı");
 
       // ✅ CRITICAL FIX: Just return true - machine will keep executing
       // IsGCodeRunning stays true until we manually set it false later
       // The completion popup will be triggered by JobCompleted event
       return true;
      }
     catch (Exception ex)
            {
    _internalStreaming = false; 
    ErrorLogger.LogError("RunGCodeAsync exception", ex);
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
                await _streamingService.PauseAsync();
                SetMachineStatusSafe("Hold");
                _executionCts?.Cancel(); _internalStreaming = false; if (IsGCodeRunning) IsGCodeRunning = false; _wasStopped = false;
                var msg = $"Paused (Hold) at line {LastCompletedLineIndex + 1} (executing: {CurrentlyExecutingLineIndex + 1})";
                _log($"> ⏸ {msg}"); Paused?.Invoke(this, msg); UpdateExecutionProperties(); return true;
            }
            catch (Exception ex) { _logError("PauseGCode", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
        }

        public async Task<bool> ContinueGCodeAsync()
        {
            var status = GetMachineStatusSafe();
            ErrorLogger.LogDebug($"ContinueGCodeAsync called - Status={status}, WasStopped={_wasStopped}, IsGCodeRunning={IsGCodeRunning}");
            ErrorLogger.LogDebug($"  StreamingService.IsStreaming={_streamingService.IsStreaming}, StreamingService.IsPaused={_streamingService.IsPaused}");
            
            // ✅ CRITICAL FIX: Check if we were stopped (not just paused)
            if (_wasStopped)
            {
                _log("> ❌ Cannot continue - execution was stopped (not paused)");
                ErrorLogger.LogDebug("Continue rejected - _wasStopped is true");
                return false;
            }
            
            // ✅ FIX: Allow continue even if not in Hold state - the streaming service tracks its own pause state
            // This handles cases where GRBL reports Idle but streaming service is still paused
            bool isHoldOrPaused = status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) || _streamingService.IsPaused;
            
            if (!isHoldOrPaused)
            {
                _log($"> ⚠️ Cannot continue - not in Hold state (current: {status}) and streaming not paused");
                ErrorLogger.LogDebug($"Continue rejected - status={status}, IsPaused={_streamingService.IsPaused}");
                return false;
            }
            
            try
            {
                _log($"> ▶ Attempting to resume streaming...");
                ErrorLogger.LogDebug("Calling _streamingService.ResumeAsync()");
                
                // ✅ Resume the streaming service - this will send ~ (Cycle Start) and continue buffering
                await _streamingService.ResumeAsync();
                
                // ✅ Update internal state
                SetMachineStatusSafe("Run");
                if (!IsGCodeRunning) 
                { 
                    IsGCodeRunning = true;
                    // Restart elapsed timer if it was stopped
                    _liveElapsedTimer?.Start();
                }
                _internalStreaming = true;
                
                // ✅ Re-enable status query scope for fast updates during execution
                try 
                { 
                    _gcodeRunScope?.Dispose(); 
                    _gcodeRunScope = App.MainController?.BeginScopedCentralStatusOverride(200); 
                } 
                catch { }
                
                _log("> ▶ Continued (streaming resumed via GrblStreamer)"); 
                ErrorLogger.LogDebug("ContinueGCodeAsync completed successfully");
                UpdateExecutionProperties(); 
                return true;
            }
            catch (Exception ex) 
            { 
                ErrorLogger.LogError("ContinueGCode exception", ex);
                _logError("ContinueGCode", ex, ErrorHandlingService.ErrorSeverity.Error); 
                return false; 
            }
        }

        public async Task<bool> StopGCodeAsync()
        {
            if (!CanStopExecution) return false;
            try
            {
                await _streamingService.StopAsync();
                SetMachineStatusSafe("Idle");
                _executionCts?.Cancel(); 
                _internalStreaming = false; 
                _wasStopped = true; // ✅ CRITICAL: Mark as stopped so next run starts fresh
                if (IsGCodeRunning) IsGCodeRunning = false; 
                _log("> ⏹ Job stopped via GrblStreamer"); 
                UpdateExecutionProperties(); 
                return true;
            }
            catch (Exception ex) { _logError("StopGCode", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
        }

        public void CompleteStopSequence()
        {
            _wasStopped = true; if (IsGCodeRunning) IsGCodeRunning = false; CurrentGCodeLineIndex = 0; LastCompletedLineIndex = -1; CurrentlyExecutingLineIndex = -1; UpdateExecutionProperties();
        }

        // OpenBuilds-style Character Counting Protocol streaming
        // Tracks bytes in GRBL RX buffer and fills it optimally
        private async Task<bool> ExecuteOpenBuildsStreamingAsync(CancellationToken ct)
        {
            var inflight = new Queue<(string Cmd, int Line, int Bytes)>();
            int inflightBytes = 0;  // Current bytes in GRBL RX buffer
            int errors = 0;
            bool aborted = false;
            var allAckTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            object sync = new object();

            // Character Counting Protocol: No artificial delays needed!
            // Buffer is tracked by byte count, not command count
            // OPTIMIZED: Zero delay like OpenBuilds - synchronous serial write is already fast
            const int SEND_DELAY_MS = 0;  // OpenBuilds has no delay between commands
            
            // Semaphore to ensure only one FillAsync runs at a time (prevents parallel buffer overflow)
            var fillSemaphore = new SemaphoreSlim(1, 1);
            
            // Fill buffer while space is available (Character Counting Protocol)
            async Task FillAsync()
            {
                // Ensure only one FillAsync runs at a time to prevent buffer overflow
                if (!await fillSemaphore.WaitAsync(0)) return; // Skip if another FillAsync is running
                try
                {
                if (aborted) return;

                // Loop to fill buffer optimally using byte counting
                while (_internalStreaming && !_wasStopped && !ct.IsCancellationRequested)
                {
                    string cmd = null;
                    int uiIdx = -1;
                    int cmdBytes = 0;

                    lock (sync)
                    {
                        if (CurrentGCodeLineIndex >= GCodeLines.Count) break; // Done sending
                        
                        // Peek next command to check if it fits in buffer
                        if (!TryGetNextValidCommand(out cmd, out uiIdx))
                        {
                            break; // Should not happen if index < count
                        }

                        // NOTE: FormatGCodeLine KALDIRILDI - 3 decimal yuvarlaması arc hatalarına (error:33) sebep oluyordu
                        // OpenBuilds gibi orijinal G-Code'u olduğu gibi gönderiyoruz
                        // cmd = FormatGCodeLine(cmd);

                        // Character Counting: Calculate byte size (command + newline)
                        cmdBytes = System.Text.Encoding.ASCII.GetByteCount(cmd) + 1;  // +1 for \n
                        
                        // Check if command fits in remaining buffer space
                        if (inflightBytes + cmdBytes > EFFECTIVE_BUFFER_SIZE)
                        {
                            // Buffer would overflow - wait for OK responses
                            break;
                        }

                        // Command fits - add to inflight queue
                        inflight.Enqueue((cmd, uiIdx, cmdBytes));
                        inflightBytes += cmdBytes;
                        if (inflight.Count == 1) CurrentlyExecutingLineIndex = uiIdx;
                    }

                    // Send outside lock (I/O operation)
                    ParseModalValues(cmd);
                    bool sent = await _connectionManager.SendGCodeCommandAsync(cmd);

                    // Character Counting: Minimal delay just for thread yielding
                    // No artificial delays needed - buffer is tracked by bytes!
                    if (SEND_DELAY_MS > 0)
                    {
                        await Task.Delay(SEND_DELAY_MS);
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

                    // Yield to allow UI/Events to process
                    await Task.Yield();
                }

                lock (sync)
                {
                    if (CurrentGCodeLineIndex >= GCodeLines.Count && inflight.Count == 0 && !aborted)
                    {
                        allAckTcs.TrySetResult(true);
                    }
                }
                }
                finally
                {
                    fillSemaphore.Release();
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
                                string detail = $"{line} on cmd: '{item.Cmd}'";
                                MarkLineError(item.Line, detail);
                                // OPTIMIZED: Debug logging removed for performance
                                // System.Diagnostics.Debug.WriteLine($"[ERROR MATCH] error:33 at Line {item.Line}: '{item.Cmd}' (inflight remaining: {inflight.Count})");
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

                using (ct.Register(() => { allAckTcs.TrySetCanceled(); }))
                {
                    bool success;
                    try { success = await allAckTcs.Task; } // Wait for ACKs
                    catch (TaskCanceledException) { success = false; }
                    if (!success || aborted) return false;
                }
                _log($"> ✅ Character Counting Protocol streaming COMPLETED (Buffer: {GRBL_RX_BUFFER_SIZE} bytes)");
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

        // GrblStreamingService event handlers
        private void OnStreamingLineCompleted(object sender, int lineIndex)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    disp.BeginInvoke(new Action(() => HandleLineCompleted(lineIndex)));
                }
                else
                {
                    HandleLineCompleted(lineIndex);
                }
            }
            catch { }
        }

        private void HandleLineCompleted(int lineIndex)
        {
            MarkLineCompleted(lineIndex);
            LastCompletedLineIndex = lineIndex;
            LineCompleted?.Invoke(this, lineIndex);
        }

        private void OnStreamingJobCompleted(object sender, JobCompletedEventArgs e)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    disp.BeginInvoke(new Action(() => HandleJobCompleted(e)));
                }
                else
                {
                    HandleJobCompleted(e);
                }
            }
            catch { }
        }

        private void HandleJobCompleted(JobCompletedEventArgs e)
        {
            _internalStreaming = false;
            if (IsGCodeRunning) IsGCodeRunning = false;
            
            if (e.Success)
            {
                _log($"> ✅ GrblStreamer: Job completed successfully");
            }
            else
            {
                _log($"> ❌ GrblStreamer: Job failed - {e.ErrorMessage}");
            }
            
            ExecutionCompleted?.Invoke(this, e.Success);
            UpdateExecutionProperties();
        }

        private void OnStreamingProgressUpdated(object sender, StreamingProgressEventArgs e)
        {
            // Progress update (streaming service zaten timer ile tetikliyor)
            // Burada ek işlem gerekirse eklenebilir
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
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string trimmed = raw.Trim();
                
                // ✅ FIX: Strip any previous error/note annotations before sending
                // Annotations are added as "  ?" or "  ??" suffixes by MarkLineError/MarkLineNote
                int annotationIdx = trimmed.IndexOf("  ?");
                if (annotationIdx > 0) trimmed = trimmed.Substring(0, annotationIdx).TrimEnd();
                
                // If annotation stripping resulted in empty command, skip it
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                
                if (trimmed.StartsWith(";") || trimmed.StartsWith("(")) continue;
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
        
        /// <summary>
        /// Get machine setting value by ID ($110=X max rate, $111=Y max rate, $112=Z max rate, $120=X accel, etc.)
        /// </summary>
        private double GetMachineSetting(int settingId, double defaultValue)
        {
            try
            {
                var setting = App.MainController?.Settings?.FirstOrDefault(s => s.Id == settingId);
                if (setting != null && double.TryParse(setting.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return Math.Max(1.0, v);
            }
            catch { }
            return defaultValue;
        }
        
        /// <summary>
        /// Calculate time for a segment with proper physics-based acceleration modeling
        /// Uses trapezoidal/triangular velocity profile
        /// </summary>
        private double CalculateSegmentTime(GCodeSegment seg, double distance, double rapidRateXY, double rapidRateZ, double acceleration, 
                                            double entrySpeed = 0, double exitSpeed = 0)
        {
            if (distance <= 0) return 0;
            
            double targetFeedMmPerMin;
            
            if (seg.MovementType == GCodeMovementType.Rapid)
            {
                // For rapid moves, use machine's max rate
                // Calculate effective rate based on movement direction
                double dx = Math.Abs(seg.EndPoint.X - seg.StartPoint.X);
                double dy = Math.Abs(seg.EndPoint.Y - seg.StartPoint.Y);
                double dz = Math.Abs(seg.EndPoint.Z - seg.StartPoint.Z);
                
                if (dx < 0.001 && dy < 0.001)
                {
                    // Z-only move
                    targetFeedMmPerMin = rapidRateZ;
                }
                else if (dz < 0.001)
                {
                    // XY-only move
                    targetFeedMmPerMin = rapidRateXY;
                }
                else
                {
                    // Combined XYZ move - use vector-limited rate
                    // The actual rate is limited by the slowest axis component
                    double xyDist = Math.Sqrt(dx * dx + dy * dy);
                    double totalDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    
                    // Time if limited by XY rate
                    double timeIfXYLimited = totalDist / (rapidRateXY * (xyDist / totalDist + 0.001));
                    // Time if limited by Z rate  
                    double timeIfZLimited = totalDist / (rapidRateZ * (dz / totalDist + 0.001));
                    
                    // Use the slower (longer time)
                    targetFeedMmPerMin = totalDist / Math.Max(timeIfXYLimited, timeIfZLimited);
                }
            }
            else
            {
                // For cutting moves (G1, G2, G3), use the programmed feed rate
                targetFeedMmPerMin = seg?.FeedRate ?? 0;
                if (targetFeedMmPerMin <= 0) targetFeedMmPerMin = 500; // Default cutting feed
            }
            
            // Convert to mm/sec for physics calculation
            double Vmax = targetFeedMmPerMin / 60.0;  // Target max speed (mm/s)
            double Vi = entrySpeed / 60.0;            // Entry speed (mm/s)
            double Vf = exitSpeed / 60.0;             // Exit speed (mm/s)
            double a = acceleration;                   // Acceleration (mm/s²)
            double d = distance;                       // Distance (mm)
            
            // Clamp speeds to valid range
            Vi = Math.Min(Vi, Vmax);
            Vf = Math.Min(Vf, Vmax);
            
            // Calculate if we can reach Vmax given distance and acceleration
            // Using kinematic equations:
            // Distance to accelerate from Vi to Vmax: d_accel = (Vmax² - Vi²) / (2*a)
            // Distance to decelerate from Vmax to Vf: d_decel = (Vmax² - Vf²) / (2*a)
            
            double d_accel = (Vmax * Vmax - Vi * Vi) / (2 * a);
            double d_decel = (Vmax * Vmax - Vf * Vf) / (2 * a);
            
            double totalTimeSeconds;
            
            if (d_accel + d_decel <= d)
            {
                // TRAPEZOIDAL PROFILE: We can reach Vmax
                // Time = accel_time + cruise_time + decel_time
                double t_accel = (Vmax - Vi) / a;
                double t_decel = (Vmax - Vf) / a;
                double d_cruise = d - d_accel - d_decel;
                double t_cruise = d_cruise / Vmax;
                
                totalTimeSeconds = t_accel + t_cruise + t_decel;
            }
            else
            {
                // TRIANGULAR PROFILE: Cannot reach Vmax
                // Find peak velocity Vpeak where accel + decel distance = total distance
                // (Vpeak² - Vi²)/(2a) + (Vpeak² - Vf²)/(2a) = d
                // 2*Vpeak² - Vi² - Vf² = 2*a*d
                // Vpeak = sqrt((Vi² + Vf² + 2*a*d) / 2)
                
                double Vpeak_squared = (Vi * Vi + Vf * Vf + 2 * a * d) / 2;
                
                if (Vpeak_squared < Vi * Vi || Vpeak_squared < Vf * Vf)
                {
                    // Edge case: need to decelerate immediately
                    // This happens when Vi or Vf is already too high for the distance
                    // Time ≈ d / average_speed
                    double avgSpeed = (Vi + Vf) / 2;
                    if (avgSpeed < 0.1) avgSpeed = 0.1;
                    totalTimeSeconds = d / avgSpeed;
                }
                else
                {
                    double Vpeak = Math.Sqrt(Vpeak_squared);
                    
                    // Time to accelerate from Vi to Vpeak
                    double t_accel = (Vpeak - Vi) / a;
                    // Time to decelerate from Vpeak to Vf
                    double t_decel = (Vpeak - Vf) / a;
                    
                    totalTimeSeconds = t_accel + t_decel;
                }
            }
            
            // Sanity check: time should be at least distance/Vmax
            double minTime = d / Vmax;
            if (totalTimeSeconds < minTime * 0.9)
            {
                totalTimeSeconds = minTime;
            }
            
            // Return time in minutes
            return totalTimeSeconds / 60.0;
        }
        
        /// <summary>
        /// Estimate junction speed between two segments based on angle
        /// </summary>
        private double EstimateJunctionSpeed(GCodeSegment current, GCodeSegment next, double maxSpeed)
        {
            if (current == null || next == null) return 0;
            
            // Get direction vectors
            double dx1 = current.EndPoint.X - current.StartPoint.X;
            double dy1 = current.EndPoint.Y - current.StartPoint.Y;
            double dz1 = current.EndPoint.Z - current.StartPoint.Z;
            double len1 = Math.Sqrt(dx1*dx1 + dy1*dy1 + dz1*dz1);
            
            double dx2 = next.EndPoint.X - next.StartPoint.X;
            double dy2 = next.EndPoint.Y - next.StartPoint.Y;
            double dz2 = next.EndPoint.Z - next.StartPoint.Z;
            double len2 = Math.Sqrt(dx2*dx2 + dy2*dy2 + dz2*dz2);
            
            if (len1 < 0.001 || len2 < 0.001) return 0;
            
            // Normalize
            dx1 /= len1; dy1 /= len1; dz1 /= len1;
            dx2 /= len2; dy2 /= len2; dz2 /= len2;
            
            // Dot product gives cos(angle)
            double dotProduct = dx1*dx2 + dy1*dy2 + dz1*dz2;
            
            // Clamp to [-1, 1] to avoid acos errors
            dotProduct = Math.Max(-1, Math.Min(1, dotProduct));
            
            // Junction speed is reduced based on angle
            // Straight line (cos=1): full speed
            // 90° turn (cos=0): ~30% speed
            // 180° turn (cos=-1): 0% speed
            double speedFactor = (1 + dotProduct) / 2; // Range [0, 1]
            
            // Apply a junction deviation factor (GRBL uses $11 junction deviation)
            // More aggressive slowdown for sharper angles
            speedFactor = Math.Pow(speedFactor, 0.5); // Square root for gentler curve
            
            return maxSpeed * speedFactor;
        }
        
        public void BuildTimeMapFromSegments(List<GCodeSegment> segments)
        {
            _segmentEstimatedTimes.Clear(); 
            _completedSegments.Clear(); 
            _completedMinutes = 0; 
            _totalEstimatedMinutes = 0; 
            
            if (segments == null || segments.Count == 0) 
            { 
                TotalEstimatedTime = TimeSpan.Zero; 
                return; 
            }
            
            // Get machine settings for accurate time calculation
            // $110 = X max rate (mm/min), $111 = Y max rate, $112 = Z max rate
            // $120 = X acceleration (mm/s²), $121 = Y acceleration, $122 = Z acceleration
            double rapidRateXY = Math.Min(GetMachineSetting(110, 3000), GetMachineSetting(111, 3000)); // Use slower of X/Y
            double rapidRateZ = GetMachineSetting(112, 1000);
            double acceleration = GetMachineSetting(120, 200); // Use X acceleration as baseline (mm/s²)
            
            System.Diagnostics.Debug.WriteLine($"⏱️ Time estimation using: RapidXY={rapidRateXY}mm/min, RapidZ={rapidRateZ}mm/min, Accel={acceleration}mm/s²");
            
            // First pass: Calculate entry/exit speeds for each segment using look-ahead
            double[] entrySpeedsPerMin = new double[segments.Count];
            double[] exitSpeedsPerMin = new double[segments.Count];
            
            // Forward pass: calculate maximum entry speeds based on previous segment
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg == null) continue;
                
                // Get target speed for this segment
                double targetSpeed;
                if (seg.MovementType == GCodeMovementType.Rapid)
                {
                    bool isZOnly = Math.Abs(seg.EndPoint.X - seg.StartPoint.X) < 0.001 && 
                                   Math.Abs(seg.EndPoint.Y - seg.StartPoint.Y) < 0.001;
                    targetSpeed = isZOnly ? rapidRateZ : rapidRateXY;
                }
                else
                {
                    targetSpeed = seg.FeedRate > 0 ? seg.FeedRate : 500;
                }
                
                if (i == 0)
                {
                    // First segment: start from zero
                    entrySpeedsPerMin[i] = 0;
                }
                else
                {
                    // Entry speed limited by junction with previous segment
                    var prevSeg = segments[i - 1];
                    double junctionSpeed = EstimateJunctionSpeed(prevSeg, seg, Math.Min(targetSpeed, exitSpeedsPerMin[i - 1]));
                    entrySpeedsPerMin[i] = junctionSpeed;
                }
                
                // Calculate maximum exit speed achievable from this distance
                double distance = 0;
                try { distance = seg.GetActualDistance(); } catch { }
                
                if (distance > 0)
                {
                    // Max speed achievable: V² = Vi² + 2*a*d
                    double maxExitSpeed = Math.Sqrt(entrySpeedsPerMin[i] * entrySpeedsPerMin[i] / 3600 + 2 * acceleration * distance) * 60;
                    exitSpeedsPerMin[i] = Math.Min(targetSpeed, maxExitSpeed);
                }
                else
                {
                    exitSpeedsPerMin[i] = entrySpeedsPerMin[i];
                }
            }
            
            // Backward pass: ensure we can decelerate to required speeds
            exitSpeedsPerMin[segments.Count - 1] = 0; // Last segment must end at zero
            
            for (int i = segments.Count - 2; i >= 0; i--)
            {
                var seg = segments[i];
                if (seg == null) continue;
                
                double distance = 0;
                try { distance = seg.GetActualDistance(); } catch { }
                
                if (distance > 0)
                {
                    // Required exit speed to be able to reach next segment's entry speed
                    double requiredExitSpeed = entrySpeedsPerMin[i + 1];
                    
                    // Max exit speed that allows deceleration to required speed
                    // Using V² = Vf² + 2*a*d (backwards)
                    double maxExitFromDecel = Math.Sqrt(requiredExitSpeed * requiredExitSpeed / 3600 + 2 * acceleration * distance) * 60;
                    
                    exitSpeedsPerMin[i] = Math.Min(exitSpeedsPerMin[i], maxExitFromDecel);
                    exitSpeedsPerMin[i] = Math.Min(exitSpeedsPerMin[i], requiredExitSpeed * 1.5); // Smooth junction
                }
            }
            
            // Final pass: calculate time for each segment with proper entry/exit speeds
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i]; 
                double distance = 0; 
                try { distance = seg?.GetActualDistance() ?? 0; } catch { }
                
                double entrySpeed = entrySpeedsPerMin[i];
                double exitSpeed = exitSpeedsPerMin[i];
                
                double minutes = CalculateSegmentTime(seg, distance, rapidRateXY, rapidRateZ, acceleration, entrySpeed, exitSpeed);
                _segmentEstimatedTimes[i] = minutes; 
                _totalEstimatedMinutes += minutes;
            }
            
            TotalEstimatedTime = TimeSpan.FromMinutes(_totalEstimatedMinutes);
            System.Diagnostics.Debug.WriteLine($"⏱️ Total estimated time: {TotalEstimatedTime} ({segments.Count} segments)");
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
                _streamingService?.Dispose();
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
            
            // ✅ CRITICAL FIX: Do NOT clear _segmentEstimatedTimes and _totalEstimatedMinutes here!
            // These are calculated when the file is loaded, not per-run.
            // Clearing them causes remaining time to show "--:--:--" on second run.
            // _segmentEstimatedTimes.Clear();  // ❌ REMOVED - breaks time estimation on re-run
            // _totalEstimatedMinutes = 0;      // ❌ REMOVED - breaks time estimation on re-run
            // TotalEstimatedTime = TimeSpan.Zero; // ❌ REMOVED - breaks time estimation on re-run
            
            // Only reset per-run tracking values
            _completedSegments.Clear();
            _completedMinutes = 0;
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
                    // Round to 3 decimal places and ALWAYS show 3 decimals to avoid GRBL parsing issues
                    string rounded = Math.Round(val, 3).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                    return $"{axis}{rounded}";
                }
                return m.Value;
            }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}

