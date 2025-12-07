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
                
                System.Diagnostics.Debug.WriteLine($"GrblStreamingService: {_gcodeQueue.Count} satır yüklendi");
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
            if (_isStreaming) return;
            if (_gcodeQueue.Count == 0) return;
            if (!_connectionManager.IsConnected) return;

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

            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Streaming başladı");

            // İlk komutları gönder
            await SendNextCommandsAsync();
        }

        /// <summary>
        /// Streaming'i duraklatır
        /// </summary>
        public async Task PauseAsync()
        {
            if (!_isStreaming) return;
            
            _isPaused = true;
            await _connectionManager.SendControlCharacterAsync('!'); // Feed Hold
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Paused");
        }

        /// <summary>
        /// Streaming'i devam ettirir
        /// </summary>
        public async Task ResumeAsync()
        {
            if (!_isStreaming || !_isPaused) return;
            
            _isPaused = false;
            _isBlocked = false;
            await _connectionManager.SendControlCharacterAsync('~'); // Cycle Start
            
            // Kuyruğu devam ettir
            await Task.Delay(200);
            await SendNextCommandsAsync();
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Resumed");
        }

        /// <summary>
        /// Streaming'i durdurur
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isStreaming) return;

            _streamCts?.Cancel();
            _progressTimer?.Dispose();
            _progressTimer = null;

            // GRBL'e durdurma komutları gönder
            await _connectionManager.SendControlCharacterAsync('!'); // Feed Hold
            await Task.Delay(200);
            await _connectionManager.SendControlCharacterAsync((char)0x18); // Soft Reset

            ClearQueue();
            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;

            Stats.EndTime = DateTime.Now;
            
            StreamingStopped?.Invoke(this, EventArgs.Empty);
            JobCompleted?.Invoke(this, new JobCompletedEventArgs(false, Stats, "Job cancelled by user"));
            
            System.Diagnostics.Debug.WriteLine("GrblStreamingService: Stopped");
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
