using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrblStreamer.Enums;
using GrblStreamer.Events;
using GrblStreamer.Models;

namespace CncControlApp.Services
{
    /// <summary>
    /// GrblStreamer buffer yönetimi mantığını mevcut ConnectionManager ile entegre eden servis.
    /// Kendi bağlantısını açmaz - mevcut SerialPort'u ConnectionManager üzerinden kullanır.
    /// Character Counting Protocol ile optimum buffer kullanımı sağlar.
    /// </summary>
    public class GrblStreamingService : IDisposable
    {
        private readonly ConnectionManager _connectionManager;
        private readonly object _lockObject = new object();
        
        // Queue management (OpenBuilds CONTROL mantığı)
        private readonly List<string> _gcodeQueue = new List<string>();
        private int _queuePointer = 0;
        private readonly List<string> _sentBuffer = new List<string>();
        
        // Buffer sizes
        private const int GRBL_RX_BUFFER_SIZE = 127;
        private const int BUFFER_SAFETY_MARGIN = 5;
        private const int EFFECTIVE_BUFFER_SIZE = GRBL_RX_BUFFER_SIZE - BUFFER_SAFETY_MARGIN;
        
        // State
        private bool _isStreaming = false;
        private bool _isPaused = false;
        private bool _isBlocked = false;
        private bool _isStopping = false; // ✅ NEW: Flag to ignore errors during stop sequence
        private CancellationTokenSource _streamCts;
        private Timer _progressTimer;
        // NOTE: Status polling is handled by CentralStatusQuerier (via BeginScopedCentralStatusOverride)
        private bool _disposed;

        // Response tracking
        private Action<string> _responseHandler;

        // Stats
        public StreamingStats Stats { get; private set; } = new StreamingStats();

        // Events
        public event EventHandler<StreamingProgressEventArgs> ProgressUpdated;
        public event EventHandler<JobCompletedEventArgs> JobCompleted;
        public event EventHandler<OkReceivedEventArgs> CommandCompleted;
        public event EventHandler<ErrorReceivedEventArgs> CommandError;
        public event EventHandler<int> LineCompleted; // UI satır güncellemesi için
        public event EventHandler StreamingStarted;
        public event EventHandler StreamingStopped;

        // Properties
        public bool IsStreaming => _isStreaming;
        public bool IsPaused => _isPaused;
        public int CompletedLines => Stats.CompletedLines;
        public int TotalLines => Stats.TotalLines;
        public double ProgressPercent => Stats.ProgressPercent;
        public TimeSpan ElapsedTime => Stats.ElapsedTime;
        public TimeSpan EstimatedRemaining => Stats.EstimatedRemaining;

        public GrblStreamingService(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// Servisi başlatır ve response handler'ı bağlar
        /// </summary>
        public void Initialize()
        {
            // ConnectionManager'ın ResponseReceived event'ine abone ol
            _connectionManager.ResponseReceived += OnResponseReceived;
            System.Diagnostics.Debug.WriteLine("GrblStreamingService initialized");
        }

        /// <summary>
        /// GCode satırlarını yükler
        /// </summary>
        public void LoadGCode(IEnumerable<string> lines)
        {
            lock (_lockObject)
            {
                // ✅ CRITICAL FIX: Reset all streaming state when loading new GCode
                // This ensures a clean slate for the next run
                _isStreaming = false;
                _isPaused = false;
                _isBlocked = false;
                _isStopping = false;
                
                ClearQueue();
                
                foreach (var rawLine in lines)
                {
                    // Yorum ve boşlukları temizle
                    var line = rawLine.Split(';')[0].Trim();
                    line = line.Replace("%", "");
                    
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _gcodeQueue.Add(line);
                    }
                }
                
                Stats = new StreamingStats
                {
                    TotalLines = _gcodeQueue.Count
                };
                
                System.Diagnostics.Debug.WriteLine($"GrblStreamingService: {_gcodeQueue.Count} satır yüklendi (state reset)");
            }
        }

        /// <summary>
        /// GCode string'ini yükler
        /// </summary>
        public void LoadGCodeString(string gcode)
        {
            var lines = gcode.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            LoadGCode(lines);
        }

        /// <summary>
        /// Streaming'i başlatır
        /// </summary>
        public async Task StartAsync()
        {
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.StartAsync called - IsStreaming={_isStreaming}, QueueCount={_gcodeQueue.Count}, IsConnected={_connectionManager.IsConnected}");
            
            // ✅ CRITICAL FIX: Force reset streaming state if we're starting fresh
            // This handles the case where previous Stop didn't complete cleanly
            if (_isStreaming) 
            {
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.StartAsync: Was streaming, forcing reset for fresh start");
                _isStreaming = false;
                _isPaused = false;
                _isBlocked = false;
                _isStopping = false;
            }
            if (_gcodeQueue.Count == 0) 
            {
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.StartAsync: Queue is empty, returning");
                return;
            }
            if (!_connectionManager.IsConnected) 
            {
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.StartAsync: Not connected, returning");
                return;
            }

            _isStreaming = true;
            _isPaused = false;
            _isBlocked = false;
            _streamCts = new CancellationTokenSource();

            Stats.StartTime = DateTime.Now;
            
            StreamingStarted?.Invoke(this, EventArgs.Empty);

            // Progress timer (500ms)
            _progressTimer = new Timer(_ => 
            {
                ProgressUpdated?.Invoke(this, new StreamingProgressEventArgs(Stats));
            }, null, 0, 500);

            // NOTE: Status polling is handled by CentralStatusQuerier (200ms via BeginScopedCentralStatusOverride)
            // No separate _statusTimer needed - avoids double status queries

            System.Diagnostics.Debug.WriteLine($"GrblStreamingService: Streaming başladı - {_gcodeQueue.Count} satır");

            // İlk komutları gönder
            await SendNextCommandsAsync();
        }

        /// <summary>
        /// Streaming'i duraklatır - GRBL'e Feed Hold gönderir ama streaming state'ini korur
        /// </summary>
        public async Task PauseAsync()
        {
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.PauseAsync called - IsStreaming={_isStreaming}, IsPaused={_isPaused}");
            System.Diagnostics.Debug.WriteLine($"  QueuePointer={_queuePointer}, QueueCount={_gcodeQueue.Count}, SentBufferCount={_sentBuffer.Count}");
            
            // ✅ CRITICAL: Keep _isStreaming = true so we can resume later
            // Only set _isPaused = true to prevent new commands from being sent
            // Do NOT clear the queue or buffer - we need them for resume
            
            _isPaused = true;
            // ✅ Do NOT set _isStreaming = false here - it breaks resume functionality
            
            // Send Feed Hold to GRBL to pause motion
            bool feedHoldSent = await _connectionManager.SendControlCharacterAsync('!');
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.PauseAsync: Feed Hold sent = {feedHoldSent}");
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Paused (streaming state preserved for resume)");
        }

        /// <summary>
        /// Streaming'i devam ettirir
        /// </summary>
        public async Task ResumeAsync()
        {
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.ResumeAsync called - IsStreaming={_isStreaming}, IsPaused={_isPaused}, IsBlocked={_isBlocked}");
            System.Diagnostics.Debug.WriteLine($"  QueuePointer={_queuePointer}, QueueCount={_gcodeQueue.Count}, SentBufferCount={_sentBuffer.Count}");
            
            // ✅ CRITICAL FIX: Allow resume even if _isStreaming was set to false during pause
            // The pause might have set _isStreaming to false, but we should still be able to resume
            // Check if we have remaining work in the queue
            bool hasRemainingWork;
            lock (_lockObject)
            {
                hasRemainingWork = _queuePointer < _gcodeQueue.Count || _sentBuffer.Count > 0;
            }
            
            if (!hasRemainingWork)
            {
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.ResumeAsync: No remaining work - cannot resume");
                return;
            }
            
            // ✅ FIX: Re-enable streaming if it was disabled during pause
            if (!_isStreaming)
            {
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.ResumeAsync: Re-enabling streaming flag");
                _isStreaming = true;
                
                // Restart progress timer if needed
                if (_progressTimer == null)
                {
                    _progressTimer = new Timer(_ => 
                    {
                        ProgressUpdated?.Invoke(this, new StreamingProgressEventArgs(Stats));
                    }, null, 0, 500);
                }
            }
            
            _isPaused = false;
            _isBlocked = false;
            
            // ✅ Send Cycle Start to GRBL to resume motion
            bool cycleStartSent = await _connectionManager.SendControlCharacterAsync('~');
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.ResumeAsync: Cycle Start sent = {cycleStartSent}");
            
            // ✅ Wait for GRBL to process the cycle start and resume motion
            // GRBL needs time to restart the motion planner
            await Task.Delay(300);
            
            // ✅ Check if there are pending commands in _sentBuffer that haven't received OK yet
            // If so, we don't need to send new commands immediately - GRBL will send OK when it processes them
            int pendingCount;
            lock (_lockObject)
            {
                pendingCount = _sentBuffer.Count;
            }
            
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.ResumeAsync: Pending commands in buffer = {pendingCount}");
            
            // ✅ Only send new commands if buffer has space
            // The ProcessOk handler will continue sending when GRBL responds
            if (pendingCount == 0)
            {
                // No pending commands - need to send next batch
                await SendNextCommandsAsync();
            }
            else
            {
                // Commands are pending - GRBL will send OK responses as it executes them
                // ProcessOk will trigger SendNextCommandsAsync when space becomes available
                System.Diagnostics.Debug.WriteLine("GrblStreamingService.ResumeAsync: Waiting for GRBL to process pending commands...");
            }
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Resumed successfully");
        }

        /// <summary>
        /// Streaming'i durdurur
        /// </summary>
        public async Task StopAsync()
        {
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService.StopAsync called - IsStreaming={_isStreaming}");
            
            // ✅ CRITICAL: Immediately mark as stopping to ignore errors during stop sequence
            _isStopping = true;
            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;

            _streamCts?.Cancel();
            _progressTimer?.Dispose();
            _progressTimer = null;

            // ✅ Step 1: Send Jog Cancel first to stop any motion immediately
            try
            {
                await _connectionManager.SendControlCharacterAsync((char)0x85); // Jog Cancel
                await Task.Delay(50);
            }
            catch { }

            // ✅ Step 2: Send Feed Hold to pause any remaining motion
            try
            {
                await _connectionManager.SendControlCharacterAsync('!'); // Feed Hold
                await Task.Delay(100);
            }
            catch { }

            // ✅ Step 3: Send Soft Reset to clear GRBL's internal buffer completely
            try
            {
                await _connectionManager.SendControlCharacterAsync((char)0x18); // Soft Reset (Ctrl+X)
            }
            catch { }
            
            // ✅ CRITICAL: Wait for GRBL to complete the reset sequence
            // After soft reset, GRBL sends welcome message and takes ~500-1500ms to be ready
            await Task.Delay(1500);
            
            // ✅ Step 4: Clear our internal buffers AFTER GRBL has reset
            ClearQueue();
            
            // ✅ Step 5: Wait for GRBL to report Idle status (poll a few times)
            int maxWaitAttempts = 10;
            for (int i = 0; i < maxWaitAttempts; i++)
            {
                try
                {
                    // Request status update
                    await _connectionManager.SendGCodeCommandAsync("?");
                    await Task.Delay(200);
                    
                    // Check if machine is idle (via status text from MainController)
                    var status = App.MainController?.MachineStatus ?? "";
                    if (status.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"GrblStreamingService: Machine confirmed Idle after {i + 1} attempts");
                        break;
                    }
                    
                    // If still in Hold, send Cycle Resume to exit Hold state
                    if (status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        await _connectionManager.SendControlCharacterAsync('~'); // Cycle Resume
                        await Task.Delay(100);
                    }
                }
                catch { }
            }

            Stats.EndTime = DateTime.Now;
            
            // ✅ Reset stopping flag
            _isStopping = false;
            
            StreamingStopped?.Invoke(this, EventArgs.Empty);
            JobCompleted?.Invoke(this, new JobCompletedEventArgs(false, Stats, "Job cancelled by user"));
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Stop sequence completed");
        }

        /// <summary>
        /// Buffer'da yer varsa sonraki komutları gönderir
        /// </summary>
        private async Task SendNextCommandsAsync()
        {
            if (!_isStreaming || _isPaused || _isBlocked) return;
            if (!_connectionManager.IsConnected) return;

            // Birden fazla komutu buffer'a sığdığı kadar gönder
            while (true)
            {
                string cmd = null;
                int cmdBytes = 0;

                lock (_lockObject)
                {
                    // Kuyrukta komut var mı?
                    if (_queuePointer >= _gcodeQueue.Count)
                    {
                        // Tüm komutlar gönderildi
                        if (_sentBuffer.Count == 0)
                        {
                            // Ve tüm OK'lar alındı - iş tamamlandı
                            CompleteJob(true);
                        }
                        return;
                    }

                    cmd = _gcodeQueue[_queuePointer];
                    cmdBytes = System.Text.Encoding.ASCII.GetByteCount(cmd) + 1; // +1 for newline

                    // Buffer'da yer var mı?
                    var usedSpace = _sentBuffer.Sum(c => System.Text.Encoding.ASCII.GetByteCount(c) + 1);
                    if (usedSpace + cmdBytes > EFFECTIVE_BUFFER_SIZE)
                    {
                        // Buffer dolu
                        _isBlocked = true;
                        return;
                    }

                    // Komutu buffer'a ekle
                    _sentBuffer.Add(cmd);
                    _queuePointer++;
                    Stats.SentLines = _queuePointer;
                }

                // Komutu gönder (lock dışında - I/O)
                bool sent = await _connectionManager.SendGCodeCommandAsync(cmd);
                if (!sent)
                {
                    lock (_lockObject)
                    {
                        if (_sentBuffer.Count > 0) _sentBuffer.RemoveAt(_sentBuffer.Count - 1);
                    }
                    CompleteJob(false, "Send failed");
                    return;
                }
            }
        }

        /// <summary>
        /// ConnectionManager'dan gelen yanıtları işler
        /// </summary>
        private void OnResponseReceived(string data)
        {
            if (!_isStreaming) return;
            if (string.IsNullOrEmpty(data)) return;

            var lines = data.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // OK yanıtı
                if (trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessOk();
                }
                // Error yanıtı
                else if (trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessError(trimmed);
                }
                // Alarm
                else if (trimmed.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessAlarm(trimmed);
                }
            }
        }

        private void ProcessOk()
        {
            string completedCommand;
            int completedLine;

            lock (_lockObject)
            {
                if (_sentBuffer.Count == 0) return;
                
                completedCommand = _sentBuffer[0];
                _sentBuffer.RemoveAt(0);
                Stats.CompletedLines++;
                completedLine = Stats.CompletedLines;
                
                _isBlocked = false;
            }

            CommandCompleted?.Invoke(this, new OkReceivedEventArgs(completedCommand));
            LineCompleted?.Invoke(this, completedLine - 1); // 0-based index

            // Sonraki komutları gönder
            if (_isStreaming && !_isPaused)
            {
                _ = SendNextCommandsAsync();
            }
        }

        private void ProcessError(string errorLine)
        {
            // ✅ CRITICAL: Ignore errors during stop sequence - they are expected
            if (_isStopping)
            {
                System.Diagnostics.Debug.WriteLine($"GrblStreamingService: Ignoring error during stop sequence: {errorLine}");
                return;
            }
            
            string failedCommand;

            lock (_lockObject)
            {
                failedCommand = _sentBuffer.Count > 0 ? _sentBuffer[0] : "";
                _sentBuffer.Clear();
            }

            // Parse error code
            int errorCode = 0;
            var match = System.Text.RegularExpressions.Regex.Match(errorLine, @"error:(\d+)");
            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out errorCode);
            }

            CommandError?.Invoke(this, new ErrorReceivedEventArgs(errorCode, errorLine, failedCommand));

            // Hata durumunda streaming'i durdur
            if (_isStreaming)
            {
                CompleteJob(false, $"{errorLine} at command: {failedCommand}");
            }
        }

        private void ProcessAlarm(string alarmLine)
        {
            // ✅ CRITICAL: Ignore alarms during stop sequence - they are expected after soft reset
            if (_isStopping)
            {
                System.Diagnostics.Debug.WriteLine($"GrblStreamingService: Ignoring alarm during stop sequence: {alarmLine}");
                return;
            }
            
            if (_isStreaming)
            {
                CompleteJob(false, alarmLine);
            }
        }

        private void CompleteJob(bool success, string errorMessage = null)
        {
            _progressTimer?.Dispose();
            _progressTimer = null;

            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;
            
            Stats.EndTime = DateTime.Now;

            ClearQueue();

            StreamingStopped?.Invoke(this, EventArgs.Empty);
            JobCompleted?.Invoke(this, new JobCompletedEventArgs(success, Stats, errorMessage));
            
            System.Diagnostics.Debug.WriteLine($"GrblStreamingService: Job completed - Success={success}, Error={errorMessage}");
        }

        private void ClearQueue()
        {
            lock (_lockObject)
            {
                _gcodeQueue.Clear();
                _queuePointer = 0;
                _sentBuffer.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _streamCts?.Cancel();
            _progressTimer?.Dispose();
            
            if (_connectionManager != null)
            {
                _connectionManager.ResponseReceived -= OnResponseReceived;
            }

            GC.SuppressFinalize(this);
        }
    }
}
