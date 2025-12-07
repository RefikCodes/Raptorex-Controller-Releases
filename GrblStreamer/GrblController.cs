using System;
using System.Threading;
using System.Threading.Tasks;
using GrblStreamer.Core;
using GrblStreamer.Enums;
using GrblStreamer.Events;
using GrblStreamer.Models;

namespace GrblStreamer
{
    /// <summary>
    /// Ana GRBL Controller sınıfı - Tüm işlevleri bir arada sunar
    /// </summary>
    public class GrblController : IDisposable
    {
        private readonly GrblConnection _connection;
        private readonly Core.GrblStreamer _streamer;
        private Timer _statusTimer;

        /// <summary>Bağlantı durumu</summary>
        public ConnectionStatus Status
        {
            get { return _connection.Status; }
        }
        
        /// <summary>Port açık mı?</summary>
        public bool IsConnected
        {
            get { return _connection.IsOpen; }
        }
        
        /// <summary>Firmware bilgileri</summary>
        public FirmwareInfo Firmware
        {
            get { return _connection.Firmware; }
        }
        
        /// <summary>Son durum bilgisi</summary>
        public GrblStatus CurrentStatus
        {
            get { return _connection.LastStatus; }
        }
        
        /// <summary>Streaming istatistikleri</summary>
        public StreamingStats StreamingStats
        {
            get { return _streamer.Stats; }
        }

        // Events
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<StatusUpdateEventArgs> StatusUpdated;
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<StreamingProgressEventArgs> StreamingProgress;
        public event EventHandler<JobCompletedEventArgs> JobCompleted;
        public event EventHandler<ErrorReceivedEventArgs> ErrorOccurred;
        public event EventHandler<AlarmReceivedEventArgs> AlarmOccurred;

        public GrblController()
        {
            _connection = new GrblConnection();
            _streamer = new Core.GrblStreamer(_connection);

            // Event forwarding
            _connection.Connected += (s, e) => Connected?.Invoke(this, e);
            _connection.Disconnected += (s, e) => Disconnected?.Invoke(this, e);
            _connection.StatusUpdated += (s, e) => StatusUpdated?.Invoke(this, e);
            _connection.DataReceived += (s, e) => DataReceived?.Invoke(this, e);
            _connection.ErrorReceived += (s, e) => ErrorOccurred?.Invoke(this, e);
            _connection.AlarmReceived += (s, e) => AlarmOccurred?.Invoke(this, e);
            
            _streamer.ProgressUpdated += (s, e) => StreamingProgress?.Invoke(this, e);
            _streamer.JobCompleted += (s, e) => JobCompleted?.Invoke(this, e);
        }

        #region Connection

        /// <summary>
        /// Mevcut COM portlarını listeler
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return GrblConnection.GetAvailablePorts();
        }

        /// <summary>
        /// Belirtilen porta bağlanır
        /// </summary>
        public Task<bool> ConnectAsync(string portName, int baudRate = 115200)
        {
            return _connection.ConnectAsync(portName, baudRate);
        }

        /// <summary>
        /// Bağlantıyı kapatır
        /// </summary>
        public Task DisconnectAsync()
        {
            StopStatusPolling();
            return _connection.DisconnectAsync();
        }

        #endregion

        #region Status Polling

        /// <summary>
        /// Otomatik durum sorgulama başlatır
        /// </summary>
        public void StartStatusPolling(int intervalMs = 250)
        {
            StopStatusPolling();
            _statusTimer = new Timer(_ => _connection.QueryStatus(), null, 0, intervalMs);
        }

        /// <summary>
        /// Otomatik durum sorgulamayı durdurur
        /// </summary>
        public void StopStatusPolling()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Dispose();
                _statusTimer = null;
            }
        }

        /// <summary>
        /// Tek seferlik durum sorgular
        /// </summary>
        public void QueryStatus()
        {
            _connection.QueryStatus();
        }

        #endregion

        #region Machine Control

        /// <summary>
        /// Soft reset gönderir
        /// </summary>
        public void Reset()
        {
            _connection.Reset();
        }

        /// <summary>
        /// Alarm kilidini açar ($X)
        /// </summary>
        public void Unlock()
        {
            _connection.Unlock();
        }

        /// <summary>
        /// Homing başlatır ($H)
        /// </summary>
        public void Home()
        {
            _connection.Home();
        }

        /// <summary>
        /// Feed Hold gönderir (!)
        /// </summary>
        public void Hold()
        {
            _connection.Hold();
        }

        /// <summary>
        /// Cycle Start gönderir (~)
        /// </summary>
        public void Resume()
        {
            _connection.Resume();
        }

        /// <summary>
        /// Jog iptal eder
        /// </summary>
        public void CancelJog()
        {
            _connection.CancelJog();
        }

        #endregion

        #region Manual Commands

        /// <summary>
        /// GCODE komutu gönderir
        /// </summary>
        public void SendCommand(string command)
        {
            _streamer.SendSingleCommand(command);
        }

        /// <summary>
        /// Jog komutu gönderir
        /// </summary>
        public void Jog(double x = 0, double y = 0, double z = 0, double feedRate = 1000, bool incremental = true)
        {
            var mode = incremental ? "G91" : "G90";
            var cmd = string.Format("$J={0}G21", mode);
            if (x != 0) cmd += string.Format("X{0:F3}", x);
            if (y != 0) cmd += string.Format("Y{0:F3}", y);
            if (z != 0) cmd += string.Format("Z{0:F3}", z);
            cmd += string.Format("F{0:F0}", feedRate);
            
            SendCommand(cmd);
        }

        /// <summary>
        /// Work pozisyonunu sıfırlar
        /// </summary>
        public void ZeroWorkPosition(bool x = true, bool y = true, bool z = true)
        {
            var axes = "";
            if (x) axes += "X0";
            if (y) axes += "Y0";
            if (z) axes += "Z0";
            
            if (!string.IsNullOrEmpty(axes))
            {
                SendCommand(string.Format("G10 L20 P0 {0}", axes));
            }
        }

        /// <summary>
        /// Work sıfır noktasına gider
        /// </summary>
        public void GoToWorkZero(double feedRate = 1000)
        {
            SendCommand("G0 X0 Y0");
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Feed override ayarlar
        /// </summary>
        public void SetFeedOverride(int percent)
        {
            if (percent == 100)
            {
                _connection.SendRealtime(GrblRealtime.FeedOverride100);
            }
            else
            {
                // 100'den hedefe git
                _connection.SendRealtime(GrblRealtime.FeedOverride100);
                var diff = percent - 100;
                var tens = Math.Abs(diff) / 10;
                var ones = Math.Abs(diff) % 10;
                
                var tenCmd = diff > 0 ? GrblRealtime.FeedOverrideInc10 : GrblRealtime.FeedOverrideDec10;
                var oneCmd = diff > 0 ? GrblRealtime.FeedOverrideInc1 : GrblRealtime.FeedOverrideDec1;
                
                for (int i = 0; i < tens; i++) _connection.SendRealtime(tenCmd);
                for (int i = 0; i < ones; i++) _connection.SendRealtime(oneCmd);
            }
        }

        /// <summary>
        /// Spindle override ayarlar
        /// </summary>
        public void SetSpindleOverride(int percent)
        {
            if (percent == 100)
            {
                _connection.SendRealtime(GrblRealtime.SpindleOverride100);
            }
            else
            {
                _connection.SendRealtime(GrblRealtime.SpindleOverride100);
                var diff = percent - 100;
                var tens = Math.Abs(diff) / 10;
                var ones = Math.Abs(diff) % 10;
                
                var tenCmd = diff > 0 ? GrblRealtime.SpindleOverrideInc10 : GrblRealtime.SpindleOverrideDec10;
                var oneCmd = diff > 0 ? GrblRealtime.SpindleOverrideInc1 : GrblRealtime.SpindleOverrideDec1;
                
                for (int i = 0; i < tens; i++) _connection.SendRealtime(tenCmd);
                for (int i = 0; i < ones; i++) _connection.SendRealtime(oneCmd);
            }
        }

        #endregion

        #region Streaming

        /// <summary>
        /// GCODE yükler (string olarak)
        /// </summary>
        public void LoadGcode(string gcode)
        {
            _streamer.LoadGcode(gcode);
        }

        /// <summary>
        /// GCODE dosyası yükler
        /// </summary>
        public void LoadGcodeFile(string filePath)
        {
            _streamer.LoadGcodeFromFile(filePath);
        }

        /// <summary>
        /// Streaming başlatır
        /// </summary>
        public void StartJob()
        {
            _streamer.Start();
        }

        /// <summary>
        /// Streaming duraklatır
        /// </summary>
        public void PauseJob()
        {
            _streamer.Pause();
        }

        /// <summary>
        /// Streaming devam ettirir
        /// </summary>
        public void ResumeJob()
        {
            _streamer.Resume();
        }

        /// <summary>
        /// Streaming durdurur
        /// </summary>
        public void StopJob()
        {
            _streamer.Stop();
        }

        #endregion

        public void Dispose()
        {
            StopStatusPolling();
            _streamer.Dispose();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
