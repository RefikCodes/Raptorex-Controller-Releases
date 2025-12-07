using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using GrblStreamer.Enums;
using GrblStreamer.Models;

namespace GrblStreamer.Core
{
    /// <summary>
    /// GRBL seri port bağlantı yöneticisi
    /// </summary>
    public class GrblConnection : IDisposable
    {
        private SerialPort _port;
        private readonly object _lockObject = new object();
        private CancellationTokenSource _readCts;
        private Task _readTask;

        /// <summary>Bağlantı durumu</summary>
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
        
        /// <summary>Firmware bilgileri</summary>
        public FirmwareInfo Firmware { get; private set; } = new FirmwareInfo();
        
        /// <summary>Son alınan durum</summary>
        public GrblStatus LastStatus { get; private set; } = new GrblStatus();
        
        /// <summary>Port açık mı?</summary>
        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }
        
        /// <summary>Aktif port adı</summary>
        public string PortName
        {
            get { return _port != null ? _port.PortName : null; }
        }

        // Events
        public event EventHandler<Events.DataReceivedEventArgs> DataReceived;
        public event EventHandler<Events.StatusUpdateEventArgs> StatusUpdated;
        public event EventHandler<Events.OkReceivedEventArgs> OkReceived;
        public event EventHandler<Events.ErrorReceivedEventArgs> ErrorReceived;
        public event EventHandler<Events.AlarmReceivedEventArgs> AlarmReceived;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        /// <summary>
        /// Mevcut COM portlarını listeler
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Belirtilen porta bağlanır
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, int baudRate = 115200)
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    await DisconnectAsync();
                }

                _port = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true,
                    NewLine = "\n"
                };

                _port.Open();

                // Bağlantı sonrası GRBL'in hazır olmasını bekle
                await Task.Delay(2000);

                // Okuma task'ını başlat
                _readCts = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadLoop(_readCts.Token));

                // Soft reset gönder ve firmware bilgisini al
                SendRealtime(GrblRealtime.SoftReset);
                await Task.Delay(500);

                Status = ConnectionStatus.Connected;
                Connected?.Invoke(this, EventArgs.Empty);

                // Firmware bilgisini sorgula
                SendCommand("$I");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Connection error: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Bağlantıyı kapatır
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_readCts != null)
            {
                _readCts.Cancel();
            }
            
            if (_readTask != null)
            {
                try
                {
                    // Wait with timeout
                    var completedTask = await Task.WhenAny(_readTask, Task.Delay(TimeSpan.FromSeconds(2)));
                }
                catch
                {
                    // ignore timeout
                }
            }

            lock (_lockObject)
            {
                if (_port != null && _port.IsOpen)
                {
                    try
                    {
                        _port.Close();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                if (_port != null)
                {
                    _port.Dispose();
                }
                _port = null;
            }

            Status = ConnectionStatus.Disconnected;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// GCODE komutu gönderir (OK bekler)
        /// </summary>
        public void SendCommand(string command)
        {
            if (!IsOpen) return;

            lock (_lockObject)
            {
                try
                {
                    _port.WriteLine(command);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Send error: {0}", ex.Message));
                }
            }
        }

        /// <summary>
        /// Realtime komut gönderir (buffer'a girmez)
        /// </summary>
        public void SendRealtime(byte command)
        {
            if (!IsOpen) return;

            lock (_lockObject)
            {
                try
                {
                    _port.Write(new byte[] { command }, 0, 1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Realtime send error: {0}", ex.Message));
                }
            }
        }

        /// <summary>
        /// Realtime karakter komutu gönderir
        /// </summary>
        public void SendRealtime(char command)
        {
            if (!IsOpen) return;

            lock (_lockObject)
            {
                try
                {
                    _port.Write(command.ToString());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Realtime send error: {0}", ex.Message));
                }
            }
        }

        /// <summary>
        /// Durum sorgular (?)
        /// </summary>
        public void QueryStatus()
        {
            SendRealtime(GrblRealtime.StatusQuery);
        }

        /// <summary>
        /// Soft reset gönderir
        /// </summary>
        public void Reset()
        {
            SendRealtime(GrblRealtime.SoftReset);
        }

        /// <summary>
        /// Feed Hold gönderir (!)
        /// </summary>
        public void Hold()
        {
            SendRealtime(GrblRealtime.FeedHold);
        }

        /// <summary>
        /// Resume gönderir (~)
        /// </summary>
        public void Resume()
        {
            SendRealtime(GrblRealtime.CycleStart);
        }

        /// <summary>
        /// Jog iptal eder
        /// </summary>
        public void CancelJog()
        {
            SendRealtime(GrblRealtime.JogCancel);
        }

        /// <summary>
        /// Alarm kilidini açar
        /// </summary>
        public void Unlock()
        {
            SendCommand("$X");
        }

        /// <summary>
        /// Homing başlatır
        /// </summary>
        public void Home()
        {
            SendCommand("$H");
        }

        private void ReadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _port != null && _port.IsOpen)
            {
                try
                {
                    var line = _port.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ProcessResponse(line.Trim());
                    }
                }
                catch (TimeoutException)
                {
                    // Normal - timeout'lar bekleniyor
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Read error: {0}", ex.Message));
                    if (_port == null || !_port.IsOpen) break;
                }
            }
        }

        private void ProcessResponse(string data)
        {
            // Ham veriyi gönder
            DataReceived?.Invoke(this, new Events.DataReceivedEventArgs(data));

            // Durum mesajı
            if (GrblParser.IsStatus(data))
            {
                LastStatus = GrblParser.ParseStatus(data);
                StatusUpdated?.Invoke(this, new Events.StatusUpdateEventArgs(LastStatus));
                
                // Bağlantı durumunu güncelle
                if (LastStatus.State == GrblState.Alarm)
                {
                    Status = ConnectionStatus.Alarm;
                }
                else if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Alarm)
                {
                    Status = ConnectionStatus.Ready;
                }
            }
            // OK
            else if (GrblParser.IsOk(data))
            {
                OkReceived?.Invoke(this, new Events.OkReceivedEventArgs(""));
            }
            // Error
            else if (GrblParser.IsError(data))
            {
                var error = GrblParser.ParseError(data);
                if (error != null)
                {
                    ErrorReceived?.Invoke(this, new Events.ErrorReceivedEventArgs(error.Item1, error.Item2, ""));
                }
            }
            // Alarm
            else if (GrblParser.IsAlarm(data))
            {
                var alarm = GrblParser.ParseAlarm(data);
                if (alarm != null)
                {
                    Status = ConnectionStatus.Alarm;
                    AlarmReceived?.Invoke(this, new Events.AlarmReceivedEventArgs(alarm.Item1, alarm.Item2));
                }
            }
            // Version info
            else if (data.StartsWith("[VER:"))
            {
                var info = GrblParser.ParseVersion(data);
                if (info != null)
                {
                    Firmware = info;
                }
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
            if (_readCts != null)
            {
                _readCts.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
