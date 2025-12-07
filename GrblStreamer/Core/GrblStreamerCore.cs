using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrblStreamer.Enums;
using GrblStreamer.Events;
using GrblStreamer.Models;

namespace GrblStreamer.Core
{
    /// <summary>
    /// GCODE streaming yöneticisi - Buffer-aware flow control
    /// OpenBuilds CONTROL'ün streaming mantığı ile uyumlu
    /// </summary>
    public class GrblStreamer : IDisposable
    {
        private readonly GrblConnection _connection;
        private readonly object _lockObject = new object();
        
        // Queue management (OpenBuilds CONTROL'deki gcodeQueue, queuePointer, sentBuffer mantığı)
        private readonly List<string> _gcodeQueue = new List<string>();
        private int _queuePointer = 0;
        private readonly List<string> _sentBuffer = new List<string>();
        
        // Buffer sizes
        private const int GRBL_RX_BUFFER_SIZE = 127;
        private const int GRBLHAL_RX_BUFFER_SIZE = 1023;
        
        // State
        private bool _isStreaming = false;
        private bool _isPaused = false;
        private bool _isBlocked = false;
        private CancellationTokenSource _streamCts;
        private Timer _progressTimer;

        /// <summary>Streaming istatistikleri</summary>
        public StreamingStats Stats { get; private set; } = new StreamingStats();
        
        /// <summary>Aktif RX buffer boyutu</summary>
        public int RxBufferSize
        {
            get
            {
                return _connection.Firmware.Type == FirmwareType.GrblHAL 
                    ? GRBLHAL_RX_BUFFER_SIZE 
                    : (_connection.Firmware.RxBufferSize > 0 ? _connection.Firmware.RxBufferSize : GRBL_RX_BUFFER_SIZE);
            }
        }

        // Events
        public event EventHandler<StreamingProgressEventArgs> ProgressUpdated;
        public event EventHandler<JobCompletedEventArgs> JobCompleted;
        public event EventHandler<OkReceivedEventArgs> CommandCompleted;
        public event EventHandler<ErrorReceivedEventArgs> CommandError;

        public GrblStreamer(GrblConnection connection)
        {
            _connection = connection;
            _connection.OkReceived += OnOkReceived;
            _connection.ErrorReceived += OnErrorReceived;
            _connection.AlarmReceived += OnAlarmReceived;
        }

        /// <summary>
        /// GCODE dosyasını yükler ve streaming için hazırlar
        /// </summary>
        public void LoadGcode(string gcode)
        {
            lock (_lockObject)
            {
                ClearQueue();
                
                var lines = gcode.Split('\n');
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
            }
        }

        /// <summary>
        /// GCODE dosyasını dosyadan yükler
        /// </summary>
        public void LoadGcodeFromFile(string filePath)
        {
            var content = File.ReadAllText(filePath);
            LoadGcode(content);
        }

        /// <summary>
        /// Streaming'i başlatır
        /// </summary>
        public void Start()
        {
            if (_isStreaming) return;
            if (_gcodeQueue.Count == 0) return;
            if (!_connection.IsOpen) return;

            _isStreaming = true;
            _isPaused = false;
            _isBlocked = false;
            _streamCts = new CancellationTokenSource();

            Stats.StartTime = DateTime.Now;
            _connection.Status = ConnectionStatus.Streaming;

            // İlerleme timer'ı başlat
            _progressTimer = new Timer(_ => 
            {
                ProgressUpdated?.Invoke(this, new StreamingProgressEventArgs(Stats));
            }, null, 0, 500);

            // İlk komutu gönder
            SendNextCommand();
        }

        /// <summary>
        /// Streaming'i duraklatır
        /// </summary>
        public void Pause()
        {
            if (!_isStreaming) return;
            
            _isPaused = true;
            _connection.Hold(); // GRBL'e feed hold gönder
            _connection.Status = ConnectionStatus.Paused;
        }

        /// <summary>
        /// Streaming'i devam ettirir
        /// </summary>
        public void Resume()
        {
            if (!_isStreaming || !_isPaused) return;
            
            _isPaused = false;
            _isBlocked = false;
            _connection.Resume(); // GRBL'e cycle start gönder
            _connection.Status = ConnectionStatus.Streaming;
            
            // Kuyruğu devam ettir
            Task.Delay(200).ContinueWith(_ => SendNextCommand());
        }

        /// <summary>
        /// Streaming'i durdurur ve iptal eder
        /// </summary>
        public void Stop()
        {
            if (!_isStreaming) return;

            if (_streamCts != null)
            {
                _streamCts.Cancel();
            }
            if (_progressTimer != null)
            {
                _progressTimer.Dispose();
                _progressTimer = null;
            }

            // GRBL'e durdurma komutları gönder
            _connection.Hold();
            Task.Delay(200).ContinueWith(_ =>
            {
                _connection.Reset();
            });

            ClearQueue();
            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;
            _connection.Status = ConnectionStatus.Ready;

            Stats.EndTime = DateTime.Now;
            JobCompleted?.Invoke(this, new JobCompletedEventArgs(false, Stats, "Job cancelled by user"));
        }

        /// <summary>
        /// Tek bir komut gönderir (streaming dışında)
        /// </summary>
        public void SendSingleCommand(string command)
        {
            if (_isStreaming) return;
            
            lock (_lockObject)
            {
                _sentBuffer.Add(command);
            }
            _connection.SendCommand(command);
        }

        /// <summary>
        /// Buffer'da kalan boş alanı hesaplar
        /// OpenBuilds CONTROL'deki BufferSpace fonksiyonu ile aynı mantık
        /// </summary>
        private int GetBufferSpace()
        {
            lock (_lockObject)
            {
                var usedSpace = _sentBuffer.Sum(cmd => cmd.Length + 1); // +1 for newline
                return RxBufferSize - usedSpace;
            }
        }

        /// <summary>
        /// Sıradaki komutu gönderir
        /// OpenBuilds CONTROL'deki send1Q fonksiyonu ile aynı mantık
        /// </summary>
        private void SendNextCommand()
        {
            if (!_isStreaming || _isPaused || _isBlocked) return;
            if (!_connection.IsOpen) return;

            lock (_lockObject)
            {
                // Kuyrukta komut var mı?
                if (_queuePointer >= _gcodeQueue.Count)
                {
                    // İş tamamlandı
                    CompleteJob(true);
                    return;
                }

                // Buffer'da yer var mı?
                var spaceLeft = GetBufferSpace();
                var nextCommand = _gcodeQueue[_queuePointer];

                if (nextCommand.Length + 1 < spaceLeft) // +1 for newline
                {
                    // Komutu gönder
                    _sentBuffer.Add(nextCommand);
                    _queuePointer++;
                    Stats.SentLines = _queuePointer;
                    
                    _connection.SendCommand(nextCommand);
                }
                else
                {
                    // Buffer dolu, blocked durumuna geç
                    _isBlocked = true;
                }
            }
        }

        /// <summary>
        /// OK alındığında çağrılır
        /// </summary>
        private void OnOkReceived(object sender, OkReceivedEventArgs e)
        {
            string completedCommand;
            
            lock (_lockObject)
            {
                if (_sentBuffer.Count == 0) return;
                
                completedCommand = _sentBuffer[0];
                _sentBuffer.RemoveAt(0);
                Stats.CompletedLines++;
                
                _isBlocked = false;
            }

            CommandCompleted?.Invoke(this, new OkReceivedEventArgs(completedCommand));

            // Sonraki komutu gönder
            if (_isStreaming && !_isPaused)
            {
                SendNextCommand();
            }
        }

        /// <summary>
        /// Error alındığında çağrılır
        /// </summary>
        private void OnErrorReceived(object sender, ErrorReceivedEventArgs e)
        {
            string failedCommand;
            
            lock (_lockObject)
            {
                failedCommand = _sentBuffer.Count > 0 ? _sentBuffer[0] : "";
                _sentBuffer.Clear();
            }

            CommandError?.Invoke(this, new ErrorReceivedEventArgs(e.ErrorCode, e.ErrorMessage, failedCommand));

            // Hata durumunda streaming'i durdur
            if (_isStreaming)
            {
                CompleteJob(false, string.Format("Error {0}: {1} at command: {2}", e.ErrorCode, e.ErrorMessage, failedCommand));
            }
        }

        /// <summary>
        /// Alarm alındığında çağrılır
        /// </summary>
        private void OnAlarmReceived(object sender, AlarmReceivedEventArgs e)
        {
            if (_isStreaming)
            {
                CompleteJob(false, string.Format("Alarm {0}: {1}", e.AlarmCode, e.AlarmMessage));
            }
        }

        /// <summary>
        /// İşi tamamlar
        /// </summary>
        private void CompleteJob(bool success, string errorMessage = null)
        {
            if (_progressTimer != null)
            {
                _progressTimer.Dispose();
                _progressTimer = null;
            }

            _isStreaming = false;
            _isPaused = false;
            _isBlocked = false;
            
            Stats.EndTime = DateTime.Now;
            _connection.Status = ConnectionStatus.Ready;

            ClearQueue();

            JobCompleted?.Invoke(this, new JobCompletedEventArgs(success, Stats, errorMessage));
        }

        /// <summary>
        /// Kuyruğu temizler
        /// </summary>
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
            if (_streamCts != null)
            {
                _streamCts.Cancel();
            }
            if (_progressTimer != null)
            {
                _progressTimer.Dispose();
            }
            _connection.OkReceived -= OnOkReceived;
            _connection.ErrorReceived -= OnErrorReceived;
            _connection.AlarmReceived -= OnAlarmReceived;
            GC.SuppressFinalize(this);
        }
    }
}
