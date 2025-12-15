using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private readonly object _rxLock = new object();
        private readonly SemaphoreSlim _sendLoopGate = new SemaphoreSlim(1, 1);
        
        // Queue management (OpenBuilds CONTROL mantığı)
        private readonly List<string> _gcodeQueue = new List<string>();
        private int _queuePointer = 0;
        private readonly List<string> _sentBuffer = new List<string>();
        private int _sentBufferBytes = 0;
        
        // Buffer sizes
        private const int GRBL_RX_BUFFER_SIZE = 127;
        private const int BUFFER_SAFETY_MARGIN = 5;
        private const int EFFECTIVE_BUFFER_SIZE = GRBL_RX_BUFFER_SIZE - BUFFER_SAFETY_MARGIN;
        
        // State
        private bool _isStreaming = false;
        private bool _isPaused = false;
        private bool _isBlocked = false;
        private bool _isStopping = false; // ✅ NEW: Flag to ignore errors during stop sequence
        private int _lineOffset = 0; // ✅ Resume için: orijinal dosyadaki başlangıç satır indeksi
        private CancellationTokenSource _streamCts;
        private Timer _progressTimer;
        // NOTE: Status polling is handled by CentralStatusQuerier (via BeginScopedCentralStatusOverride)
        private bool _disposed;

        // Incoming serial buffering (ConnectionManager raises raw chunks, not line-delimited)
        private readonly StringBuilder _incomingBuffer = new StringBuilder(2048);

        // Raw RX tail capture (for post-mortem on stalls: did we actually receive 'ok'?)
        private readonly StringBuilder _rxTail = new StringBuilder(4096);
        private const int RX_TAIL_MAX_CHARS = 8192;

        // Diagnostics (helps identify: missed OK vs flow-control poisoning vs controller pause)
        private DateTime _lastOkUtc = DateTime.MinValue;
        private DateTime _lastRxUtc = DateTime.MinValue;
        private long _okCount;
        private long _errorCount;
        private long _alarmCount;

        // Rate-limited stall diagnostics
        private DateTime _lastBlockedDiagDumpUtc = DateTime.MinValue;

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

        public string GetDebugSnapshot()
        {
            try
            {
                lock (_lockObject)
                {
                    var now = DateTime.UtcNow;
                    long okAgeMs = _lastOkUtc == DateTime.MinValue ? -1 : (long)(now - _lastOkUtc).TotalMilliseconds;
                    long rxAgeMs = _lastRxUtc == DateTime.MinValue ? -1 : (long)(now - _lastRxUtc).TotalMilliseconds;
                    var inflightPreview = GetInflightPreviewLocked(5);

                    return $"IsStreaming={_isStreaming}, IsPaused={_isPaused}, IsBlocked={_isBlocked}, IsStopping={_isStopping}, " +
                           $"QueuePointer={_queuePointer}, QueueCount={_gcodeQueue.Count}, SentBufferCount={_sentBuffer.Count}, SentBufferBytes={_sentBufferBytes}, " +
                           $"Stats.CompletedLines={Stats?.CompletedLines ?? -1}, Stats.TotalLines={Stats?.TotalLines ?? -1}, Stats.ProgressPercent={(Stats != null ? Stats.ProgressPercent.ToString("0.00") : "-1")}, " +
                           $"Diag.OkAgeMs={okAgeMs}, Diag.RxAgeMs={rxAgeMs}, Diag.OkCount={_okCount}, Diag.ErrorCount={_errorCount}, Diag.AlarmCount={_alarmCount}, " +
                           $"Diag.InflightPreview='{inflightPreview}'";
                }
            }
            catch
            {
                return "<snapshot_failed>";
            }
        }

        private string GetInflightPreviewLocked(int max)
        {
            if (_sentBuffer == null || _sentBuffer.Count == 0) return string.Empty;
            try
            {
                return string.Join(" | ", _sentBuffer.Take(max).Select(s => (s ?? string.Empty).Trim()));
            }
            catch
            {
                return string.Empty;
            }
        }

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
            LoadGCode(lines, 0);
        }
        
        /// <summary>
        /// GCode satırlarını yükler - Resume için lineOffset destekli
        /// </summary>
        /// <param name="lines">GCode satırları</param>
        /// <param name="lineOffset">Orijinal dosyadaki başlangıç satır indeksi (LineCompleted event'inde kullanılır)</param>
        public void LoadGCode(IEnumerable<string> lines, int lineOffset)
        {
            lock (_lockObject)
            {
                // ✅ CRITICAL FIX: Reset all streaming state when loading new GCode
                // This ensures a clean slate for the next run
                _isStreaming = false;
                _isPaused = false;
                _isBlocked = false;
                _isStopping = false;
                _lineOffset = lineOffset; // Resume için offset'i sakla

                // Reset diagnostics for this run
                _lastOkUtc = DateTime.MinValue;
                _lastRxUtc = DateTime.MinValue;
                _okCount = 0;
                _errorCount = 0;
                _alarmCount = 0;

                _lastBlockedDiagDumpUtc = DateTime.MinValue;

                lock (_rxLock)
                {
                    _incomingBuffer.Clear();
                    _rxTail.Clear();
                }
                
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
                
                System.Diagnostics.Debug.WriteLine($"GrblStreamingService: {_gcodeQueue.Count} satır yüklendi (lineOffset={lineOffset}, state reset)");
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

            // ✅ CRITICAL FIX: Always reset all state flags before starting
            // This ensures clean state even if previous stop sequence was interrupted
            _isStreaming = true;
            _isPaused = false;
            _isBlocked = false;
            _isStopping = false;  // Reset stopping flag to allow normal operation
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
            await TriggerSendLoopAsync();
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
                await TriggerSendLoopAsync();
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
            // Immediately mark as stopping to ignore errors during stop sequence
            _isStopping = true;
            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;

            _streamCts?.Cancel();
            _progressTimer?.Dispose();
            _progressTimer = null;

            // Clear internal buffers FIRST
            ClearQueue();

            // Feed Hold - stops motion planner
            try { await _connectionManager.SendControlCharacterAsync('!'); } catch { }
            await Task.Delay(100);

            // Queue Flush (FluidNC)
            try { await _connectionManager.SendControlCharacterAsync((char)0x15); } catch { }
            await Task.Delay(50);

            // Jog Cancel
            try { await _connectionManager.SendControlCharacterAsync((char)0x85); } catch { }
            await Task.Delay(50);

            // Soft Reset
            try { await _connectionManager.SendControlCharacterAsync((char)0x18); } catch { }
            await Task.Delay(1500);
            
            // Clear serial buffers
            try { _connectionManager.ClearSerialBuffers(); } catch { }
            
            // Wait for Idle status
            int maxWaitAttempts = 10;
            for (int i = 0; i < maxWaitAttempts; i++)
            {
                try
                {
                    await _connectionManager.SendGCodeCommandAsync("?");
                    await Task.Delay(200);
                    
                    var status = App.MainController?.MachineStatus ?? "";
                    if (status.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
                        break;
                    
                    if (status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        await _connectionManager.SendControlCharacterAsync('~');
                        await Task.Delay(100);
                    }
                }
                catch { }
            }

            Stats.EndTime = DateTime.Now;
            _isStopping = false;
            
            StreamingStopped?.Invoke(this, EventArgs.Empty);
            JobCompleted?.Invoke(this, new JobCompletedEventArgs(false, Stats, "Job cancelled by user"));
        }

        /// <summary>
        /// Buffer'da yer varsa sonraki komutları gönderir
        /// </summary>
        private async Task TriggerSendLoopAsync()
        {
            // ✅ CRITICAL FIX: Don't check _isBlocked here - it may have just been cleared by ProcessOk
            // The actual blocking check happens inside SendNextCommandsCoreAsync with proper locking
            if (!_isStreaming || _isPaused) return;
            if (!_connectionManager.IsConnected) return;

            // ✅ CRITICAL FIX: Use a retry loop to ensure we don't miss sends
            // OpenBuilds sends synchronously from the OK handler - we need equivalent reliability
            int retryAttempts = 3;
            for (int attempt = 0; attempt < retryAttempts; attempt++)
            {
                // Try to acquire the send lock - if someone else has it, they're sending
                if (await _sendLoopGate.WaitAsync(0))
                {
                    try
                    {
                        await SendNextCommandsCoreAsync();
                        return; // Successfully sent (or determined nothing to send)
                    }
                    finally
                    {
                        _sendLoopGate.Release();
                    }
                }
                else
                {
                    // Another sender is active - wait a tiny bit and retry
                    // This handles the race where ProcessOk fires while previous send is finishing
                    await Task.Delay(5);
                }
            }
            
            // If we still couldn't acquire the lock after retries, that's OK - the active sender will handle it
        }

        private async Task SendNextCommandsCoreAsync()
        {
            // Fill the controller's RX buffer as much as possible using Character Counting Protocol.
            // IMPORTANT: This loop must not be entered concurrently (protected by _sendLoopGate).
            // ✅ CRITICAL FIX: Like OpenBuilds, we loop and send until buffer is full, then return.
            // ProcessOk will call us again when space frees up.
            while (true)
            {
                // ✅ Check streaming state (but NOT _isBlocked - that's just a flag for diagnostics)
                if (!_isStreaming || _isPaused) return;
                if (!_connectionManager.IsConnected) return;

                string cmd;
                int cmdBytes;
                bool bufferFull = false;

                lock (_lockObject)
                {
                    // ✅ Clear blocked flag at start of each send attempt
                    // OpenBuilds does: status.comms.blocked = false after each ok
                    _isBlocked = false;
                    
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
                    cmdBytes = Encoding.ASCII.GetByteCount(cmd) + 1; // +1 for newline

                    // ✅ Check buffer space (like OpenBuilds BufferSpace function)
                    if (_sentBufferBytes + cmdBytes > EFFECTIVE_BUFFER_SIZE)
                    {
                        bufferFull = true;
                        _isBlocked = true;
                        
                        // Log only periodically to avoid spam
                        try
                        {
                            var now = DateTime.UtcNow;
                            if ((now - _lastBlockedDiagDumpUtc).TotalMilliseconds >= 2000)
                            {
                                _lastBlockedDiagDumpUtc = now;
                                long okAgeMs = _lastOkUtc == DateTime.MinValue ? -1 : (long)(now - _lastOkUtc).TotalMilliseconds;
                                long rxAgeMs = _lastRxUtc == DateTime.MinValue ? -1 : (long)(now - _lastRxUtc).TotalMilliseconds;
                                var inflightPreview = GetInflightPreviewLocked(5);
                                global::CncControlApp.ErrorLogger.LogDebug(
                                    $"Streamer blocked (RX buffer full). SentBufferBytes={_sentBufferBytes}, NextCmdBytes={cmdBytes}, " +
                                    $"NextCmdLen={cmd.Length}, QueuePointer={_queuePointer}, SentBufferCount={_sentBuffer.Count}, " +
                                    $"OkAgeMs={okAgeMs}, RxAgeMs={rxAgeMs}, OkCount={_okCount}, ErrorCount={_errorCount}, AlarmCount={_alarmCount}, " +
                                    $"InflightPreview='{inflightPreview}'");
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        // ✅ Space available - add to sent buffer (like OpenBuilds sentBuffer.push)
                        _sentBuffer.Add(cmd);
                        _sentBufferBytes += cmdBytes;
                        _queuePointer++;
                        Stats.SentLines = _queuePointer;
                    }
                }

                // If buffer was full, return and wait for ProcessOk to call us again
                if (bufferFull)
                {
                    return;
                }

                // Komutu gönder (lock dışında - I/O)
                bool sent = await _connectionManager.SendGCodeCommandAsync(cmd);
                if (!sent)
                {
                    lock (_lockObject)
                    {
                        if (_sentBuffer.Count > 0)
                        {
                            // remove the command we just enqueued (should be last)
                            var removed = _sentBuffer[_sentBuffer.Count - 1];
                            _sentBuffer.RemoveAt(_sentBuffer.Count - 1);
                            _sentBufferBytes -= (Encoding.ASCII.GetByteCount(removed) + 1);
                            if (_sentBufferBytes < 0) _sentBufferBytes = 0;
                        }
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

            _lastRxUtc = DateTime.UtcNow;

            // ConnectionManager forwards raw serial chunks (ReadExisting). We must reassemble full lines.
            List<string> linesToProcess = null;

            lock (_rxLock)
            {
                AppendToRxTailLocked(data);
                _incomingBuffer.Append(data);

                for (int i = 0; i < _incomingBuffer.Length; i++)
                {
                    char c = _incomingBuffer[i];
                    if (c != '\n' && c != '\r') continue;

                    var line = _incomingBuffer.ToString(0, i);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        if (linesToProcess == null) linesToProcess = new List<string>();
                        linesToProcess.Add(line.Trim());
                    }

                    // consume delimiter(s)
                    int consume = 1;
                    if (c == '\r' && i + 1 < _incomingBuffer.Length && _incomingBuffer[i + 1] == '\n')
                    {
                        consume = 2;
                    }

                    _incomingBuffer.Remove(0, i + consume);
                    i = -1; // restart scan from beginning
                }
            }

            if (linesToProcess == null || linesToProcess.Count == 0)
            {
                // No full line yet, but we are still receiving data.
                // If we are stalled waiting for OK, this is still valuable evidence.
                MaybeLogRxStallFromReceive();
                return;
            }

            foreach (var trimmed in linesToProcess)
            {
                if (trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessOk();
                }
                else if (trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessError(trimmed);
                }
                else if (trimmed.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessAlarm(trimmed);
                }
            }

            // After handling OK/error/alarm, check if we are still stuck receiving data but no OKs.
            MaybeLogRxStallFromReceive();
        }

        private void MaybeLogRxStallFromReceive()
        {
            // This runs on the RX path (ConnectionManager event). It must never throw.
            // Purpose: capture definitive evidence when we are receiving serial traffic but OKs stop.
            try
            {
                if (!_isStreaming) return;
                if (_isPaused) return;

                var now = DateTime.UtcNow;
                long okAgeMs;
                long rxAgeMs;
                int sentCount;
                int sentBytes;
                string inflightPreview;

                lock (_lockObject)
                {
                    okAgeMs = _lastOkUtc == DateTime.MinValue ? -1 : (long)(now - _lastOkUtc).TotalMilliseconds;
                    rxAgeMs = _lastRxUtc == DateTime.MinValue ? -1 : (long)(now - _lastRxUtc).TotalMilliseconds;
                    sentCount = _sentBuffer.Count;
                    sentBytes = _sentBufferBytes;
                    inflightPreview = GetInflightPreviewLocked(5);
                }

                // Only log when:
                // - we have inflight commands waiting for OK
                // - we are currently receiving data (status frames etc.)
                // - OKs are not being observed for a while
                if (sentCount <= 0) return;
                if (okAgeMs < 500) return;
                if (rxAgeMs < 0 || rxAgeMs > 250) return;

                if ((now - _lastBlockedDiagDumpUtc).TotalMilliseconds < 2000) return;
                _lastBlockedDiagDumpUtc = now;

                var rxSnapshot = GetRxStallSnapshot();
                global::CncControlApp.ErrorLogger.LogDebug(
                    $"RX indicates stall (receiving but no ok). IsBlocked={_isBlocked}, SentBufferBytes={sentBytes}, SentBufferCount={sentCount}, " +
                    $"OkAgeMs={okAgeMs}, RxAgeMs={rxAgeMs}, OkCount={_okCount}, ErrorCount={_errorCount}, AlarmCount={_alarmCount}, " +
                    $"InflightPreview='{inflightPreview}', RxStallSnapshot='{rxSnapshot}'");
            }
            catch
            {
                // swallow
            }
        }

        private void AppendToRxTailLocked(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            _rxTail.Append(data);
            if (_rxTail.Length <= RX_TAIL_MAX_CHARS) return;

            // Trim from the start to keep only the last RX_TAIL_MAX_CHARS chars.
            int overflow = _rxTail.Length - RX_TAIL_MAX_CHARS;
            if (overflow > 0)
            {
                _rxTail.Remove(0, overflow);
            }
        }

        private string GetRxStallSnapshot()
        {
            lock (_rxLock)
            {
                var incomingTail = GetTailForLog(_incomingBuffer?.ToString() ?? string.Empty, 200);
                var rxTail = GetTailForLog(_rxTail?.ToString() ?? string.Empty, 600);

                incomingTail = NormalizeForLog(incomingTail);
                rxTail = NormalizeForLog(rxTail);

                return $"IncomingBufferLen={_incomingBuffer?.Length ?? 0}, IncomingTail={incomingTail}, RxTail={rxTail}";
            }
        }

        private static string GetTailForLog(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (maxChars <= 0) return string.Empty;
            if (value.Length <= maxChars) return value;
            return value.Substring(value.Length - maxChars, maxChars);
        }

        private static string NormalizeForLog(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\r\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private void ProcessOk()
        {
            string completedCommand;
            int completedLine;

            lock (_lockObject)
            {
                _lastOkUtc = DateTime.UtcNow;
                _okCount++;

                if (_sentBuffer.Count == 0) return;
                
                completedCommand = _sentBuffer[0];
                _sentBuffer.RemoveAt(0);
                _sentBufferBytes -= (Encoding.ASCII.GetByteCount(completedCommand) + 1);
                if (_sentBufferBytes < 0) _sentBufferBytes = 0;
                Stats.CompletedLines++;
                completedLine = Stats.CompletedLines;
                
                _isBlocked = false;
            }

            CommandCompleted?.Invoke(this, new OkReceivedEventArgs(completedCommand));
            LineCompleted?.Invoke(this, completedLine - 1 + _lineOffset); // 0-based index + resume offset

            // Sonraki komutları gönder
            if (_isStreaming && !_isPaused)
            {
                _ = TriggerSendLoopAsync();
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
                _errorCount++;
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

            lock (_lockObject)
            {
                _alarmCount++;
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

            lock (_rxLock)
            {
                _incomingBuffer.Clear();
                _rxTail.Clear();
            }

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
                _sentBufferBytes = 0;
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
