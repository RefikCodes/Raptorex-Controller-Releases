// ConnectionManager.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CncControlApp.Services;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace CncControlApp
{
    // Holds connection and behavior settings for ConnectionManager
    public class ConnectionConfiguration
    {
        public bool EnableAutoConnect { get; set; } = true;
        public bool EnableDebugLogging { get; set; } = false;
        public bool EnableSoundNotifications { get; set; } = true;
        public string[] SupportedDevices { get; set; } = new[] { "CP210x", "CH340" };
        public int BaudRate { get; set; } = 115200;
        public int ConnectionTimeout { get; set; } = 800;
        public int PortScanDelay { get; set; } = 500;
        public int DeviceReconnectDelay { get; set; } = 1000;
        public int UnlockRetryCount { get; set; } = 3;
        public int UnlockBaseDelayMs { get; set; } = 300;
        public int PostUnlockStatusCheckCount { get; set; } = 3;
        public int PostUnlockStatusIntervalMs { get; set; } = 200;
        public bool AutoHomeAfterUnlock { get; set; } = false;
        public int PostHomeDelayMs { get; set; } = 500;
    public int SettingsRequestDelay { get; set; } = 300;
    public int SettingsCompleteDelay { get; set; } = 250;
        public int GrblResetDelay { get; set; } = 200;
        public int PostResetStabilizeDelayMs { get; set; } = 600;
    }

    public class ConnectionManager : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        private readonly StringBuilder _configBuffer = new StringBuilder();
        private bool _isReceivingConfig = false;

        private readonly ConnectionConfiguration _config;
        private readonly SerialPortManager _portManager;
        private readonly GCodeSender _gcodeSender;
        private readonly GCodeSettingsParser _settingsParser;

        // ? Comprehensive axis context tracking 
        private bool _isInXAxisSection = false;
        private bool _isInYAxisSection = false;
        private bool _isInZAxisSection = false;
        private bool _isInAAxisSection = false;
        private readonly Queue<string> _recentLines = new Queue<string>(10);

        private DateTime? _connectionStartTime = null;
        private TimeSpan _lastConnectionDuration = TimeSpan.Zero;
        private volatile bool _isPortScanPending = false;
        private volatile bool _isConnectionInProgress = false;
        private volatile bool _isInitialSetupComplete = false;
        private volatile bool _isSettingsLoadingComplete = false;

        private string _selectedPort;
        private bool _isConnected;

        private readonly HashSet<string> _loggedCommands = new HashSet<string>();
        private DateTime _lastStatusQueryTime = DateTime.MinValue;

        private readonly HashSet<string> _filteredMessages = new HashSet<string>();
        private DateTime _lastLogCleanup = DateTime.MinValue;

        private readonly Dictionary<int, GCodeSetting> _settingsById = new Dictionary<int, GCodeSetting>();

    // Synchronization locks
    private readonly object _scanLock = new object();
    private readonly object _connectionLock = new object();
    private readonly object _settingsLock = new object();

    // Protocol constants
    private const char GRBL_RESET_CHAR = (char)0x18; // Ctrl-X reset
    private const string FLUIDNC_CONFIG_COMMAND = "$CD";
    private const string GRBL_SETTINGS_COMMAND = "$$";

    // Sound feedback constants
    private const int SUCCESS_BEEP_FREQ_1 = 880;
    private const int SUCCESS_BEEP_DURATION_1 = 120;
    private const int SUCCESS_BEEP_INTERVAL = 120;
    private const int SUCCESS_BEEP_FREQ_2 = 1320;
    private const int SUCCESS_BEEP_DURATION_2 = 180;
    private const int DISCONNECT_BEEP_FREQ = 400;
    private const int DISCONNECT_BEEP_DURATION = 220;

        // ? A-Axis Detection Properties
        private bool _isAAxisAvailable = false;
    // Detect whether device speaks FluidNC config ($CD) for conditional features
    private bool _isFluidNCDetected = false;
    
    // Event-driven response tracker for ok/error/alarm handling
    private readonly GCodeResponseTracker _responseTracker = new GCodeResponseTracker();
    // Track if we ever saw a welcome line that mentions RaptorexCNC
    private bool _sawRaptorexWelcome = false;
    // Track last seen Grbl base version from any line (e.g., 3.0, 1.1f)
    private string _lastGrblVersionSeen = null;
        // Firmware capabilities (raw), if reported via [OPT:...] line
        private string _firmwareOptions = null;
        // Parser state and parameters from $G / $# queries
        private string _lastGcLine = null;
        private readonly List<string> _lastParamLines = new List<string>(16);
    // Some devices chunk bracketed diagnostic lines (e.g., [GC:], [G54:], [TLO:], [PRB:])
    // Buffer partial fragments until we see a closing ']'
    private string _pendingBracketLine = null;
        private TaskCompletionSource<bool> _configResponseTcs = null;
        private string _lastSentSetupCommand = null; // Track commands during setup for error context

        // New: Port info collection and selection
        private SerialPortManager.PortInfo _selectedPortInfo;

        #endregion

        #region Events

        public event Action<bool> ConnectionStatusChanged;
        public event Action PortsChanged;
        public event Action<ObservableCollection<GCodeSetting>> SettingsReceived;
        public event Action<string> ResponseReceived;
        public event Action<string> LogMessageAdded;
        public event Action SettingsLoadingCompleted;
        public event Action LoadingStarted;
        public event Action LoadingCompleted;
        public event Action<string> LoadingStatusChanged;
        public event Action<int> SettingsCountChanged;

        #endregion

        #region Properties

        public ObservableCollection<string> AvailablePorts { get; } = new ObservableCollection<string>();
        public ObservableCollection<SerialPortManager.PortInfo> AvailablePortInfos { get; } = new ObservableCollection<SerialPortManager.PortInfo>();

        private readonly ObservableCollection<GCodeSetting> _settings = new ObservableCollection<GCodeSetting>();
        public ObservableCollection<GCodeSetting> Settings => _settings;

        public string SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (_selectedPort != value)
                {
                    _selectedPort = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanConnect));
                    UpdateActivePortFlag();
                }
            }
        }

        public SerialPortManager.PortInfo SelectedPortInfo
        {
            get => _selectedPortInfo;
            set
            {
                if (_selectedPortInfo != value)
                {
                    _selectedPortInfo = value;
                    OnPropertyChanged();
                    // Sync legacy SelectedPort string for existing code paths
                    if (_selectedPortInfo != null)
                    {
                        SelectedPort = string.IsNullOrWhiteSpace(_selectedPortInfo.Caption)
                            ? _selectedPortInfo.PortName
                            : _selectedPortInfo.Caption;
                    }
                    OnPropertyChanged(nameof(CanConnect));
                    UpdateActivePortFlag();
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDisconnected));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    OnPropertyChanged(nameof(CanConnect));
                    OnPropertyChanged(nameof(CanRefreshPorts));

                    ConnectionStatusChanged?.Invoke(value);
                    UpdateActivePortFlag();
                }
            }
        }

        public bool IsDisconnected => !_isConnected;
        public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";
        public bool CanConnect => !string.IsNullOrEmpty(SelectedPort);
        public bool CanRefreshPorts => !IsConnected;

        public TimeSpan LastConnectionDuration => _lastConnectionDuration;
        public DateTime? ConnectionStartTime => _connectionStartTime;
        public bool IsInitialSetupComplete => _isInitialSetupComplete;
        public bool IsSettingsLoadingComplete => _isSettingsLoadingComplete;

    /// <summary>
    /// A ekseninin fiziksel varlığını belirtir - bağlantı sırasında tespit edilir
    /// </summary>
        public bool IsAAxisAvailable
        {
            get => _isAAxisAvailable;
            private set
            {
                if (_isAAxisAvailable != value)
                {
                    _isAAxisAvailable = value;
                    OnPropertyChanged();
                    LogImportantMessage($"> A ekseni varlığı: {(value ? "MEVCUT" : "YOK")}");
                    // If A axis exists but axis count is still below 4, bump it so summaries reflect reality
                    if (value && _detectedAxisCount < 4)
                    {
                        DetectedAxisCount = 4;
                    }
                }
            }
        }

        // Firmware and Machine Info Properties
        private string _firmwareName = "Unknown";
        private string _firmwareVersion = "Unknown";
        private string _boardName = "Unknown";
        private string _configName = "Unknown";
        private int _detectedAxisCount = 3;

        public string FirmwareName { get => _firmwareName; private set { if (_firmwareName != value) { _firmwareName = value; OnPropertyChanged(); } } }
        public string FirmwareVersion { get => _firmwareVersion; private set { if (_firmwareVersion != value) { _firmwareVersion = value; OnPropertyChanged(); } } }
        public string BoardName { get => _boardName; private set { if (_boardName != value) { _boardName = value; OnPropertyChanged(); } } }
        public string ConfigName { get => _configName; private set { if (_configName != value) { _configName = value; OnPropertyChanged(); } } }
        public int DetectedAxisCount { get => _detectedAxisCount; private set { if (_detectedAxisCount != value) { _detectedAxisCount = value; OnPropertyChanged(); } } }
        public bool IsFluidNC => _isFluidNCDetected;

        // Snapshot properties for UI bindings (Sistem Bilgileri panel)
        public string ConnectionPort => SelectedPort;
        public string ConnectionDescription => SelectedPortInfo?.Description;
        public string ConnectionManufacturer => SelectedPortInfo?.Manufacturer;
        public string ConnectionPnpId => SelectedPortInfo?.PnpDeviceId;
        public string ConnectionTransport
        {
            get
            {
                var info = SelectedPortInfo;
                string t = ClassifyTransport(info);
                var ids = ExtractVidPid(info?.PnpDeviceId);
                if (ids.vid != null && ids.pid != null) return $"{t} (VID:PID={ids.vid}:{ids.pid})";
                return t;
            }
        }
        public int BaudRate => _config?.BaudRate ?? 0;
        public string SerialBaud
        {
            get { try { return _portManager.GetConnectionDetails().BaudRate.ToString(); } catch { return BaudRate.ToString(); } }
        }
        public string SerialFormat
        {
            get
            {
                try
                {
                    var d = _portManager.GetConnectionDetails();
                    return $"{d.DataBits}/{d.Parity}/{d.StopBits}";
                }
                catch { return ""; }
            }
        }
        public string SerialHandshake
        {
            get
            {
                try
                {
                    var d = _portManager.GetConnectionDetails();
                    return $"{d.Handshake}, DTR:{d.DtrEnable}, RTS:{d.RtsEnable}";
                }
                catch { return ""; }
            }
        }
        public string SerialEncoding
        {
            get { try { return _portManager.GetConnectionDetails().EncodingName; } catch { return ""; } }
        }
        public string SerialTimeouts
        {
            get
            {
                try
                {
                    var d = _portManager.GetConnectionDetails();
                    return $"{d.ReadTimeout}/{d.WriteTimeout} ms";
                }
                catch { return ""; }
            }
        }
        public string SerialBuffers
        {
            get
            {
                try
                {
                    var d = _portManager.GetConnectionDetails();
                    return $"{d.ReadBufferSize}/{d.WriteBufferSize} bytes";
                }
                catch { return ""; }
            }
        }
        public string ParserStateDisplay => _lastGcLine ?? "(bilgi yok)";
        public string MachineParamsText => _lastParamLines != null && _lastParamLines.Count > 0 ? string.Join(Environment.NewLine, _lastParamLines) : "";
        
        /// <summary>
        /// Event-driven response tracker for ok/error/alarm - use for probe sequences
        /// </summary>
        public GCodeResponseTracker ResponseTracker => _responseTracker;

        #endregion

        #region Commands

        public ICommand RefreshPortsCommand { get; }
        public ICommand ConnectCommand { get; }

        #endregion

        #region Constructor

        public ConnectionManager(ConnectionConfiguration config = null)
        {
            _config = config ?? new ConnectionConfiguration();

            _portManager = new SerialPortManager();
            _gcodeSender = new GCodeSender(_portManager);
            _settingsParser = new GCodeSettingsParser();

            _portManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            _gcodeSender.ResponseReceived += OnResponseReceived;

            RefreshPortsCommand = new RelayCommand(
                async param => await FindAndSelectDeviceAsync(_config.SupportedDevices),
                param => CanRefreshPorts);

            ConnectCommand = new RelayCommand(
                async param => await ConnectOrDisconnectAsync(),
                param => CanConnect);

            _portManager.PortsChanged += async () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Detailed info
                    var infos = _portManager.GetDetailedPortInfos();

                    if (IsConnected && !infos.Any(p => p.PortName == ExtractPortName(SelectedPort)))
                    {
                        LogImportantMessage("> Bağlantılı cihaz çıkarıldı; bağlantı kesiliyor...");
                        _portManager.Disconnect();
                        IsConnected = false;
                        _connectionStartTime = null;
                    }

                    AvailablePorts.Clear();
                    AvailablePortInfos.Clear();
                    foreach (var info in infos)
                    {
                        AvailablePorts.Add(info.Caption);
                        AvailablePortInfos.Add(info);
                    }

                    // Try preserve selection
                    var currentPort = ExtractPortName(SelectedPort);
                    SelectedPortInfo = AvailablePortInfos.FirstOrDefault(i => i.PortName == currentPort);

                    PortsChanged?.Invoke();
                });

                if (!IsConnected && _config.EnableAutoConnect)
                {
                    await Task.Delay(_config.PortScanDelay);
                    await FindAndSelectDeviceAsync(_config.SupportedDevices);
                }
            };

            if (_config.EnableAutoConnect)
            {
                Task.Run(async () => await FindAndSelectDeviceAsync(_config.SupportedDevices));
            }
        }

        #endregion

        #region Public Methods

        public async Task<bool> SendGCodeCommandAsync(string gcode)
        {
            if (string.IsNullOrWhiteSpace(gcode) || !IsConnected)
                return false;

            try
            {
                string trimmedGcode = gcode.Trim();

                // GRBL Realtime Commands: Send without newline, don't add to buffer
                // OpenBuilds sends '?' as realtime (no \n) - this prevents buffer issues
                if (trimmedGcode == "?")
                {
                    await _gcodeSender.SendControlCharacterAsync('?');
                    return true;
                }

                if (!IsFilteredCommand(trimmedGcode))
                {
                    LogImportantMessage($"> Gönderiliyor: {trimmedGcode}");
                }

                await _gcodeSender.SendCommandAsync(gcode);

                // NOTE: Removed automatic $G sending after modal commands
                // This was causing buffer synchronization issues during G-code streaming:
                // - $G commands were not tracked in the inflight queue
                // - Buffer byte counting became incorrect
                // - OpenBuilds only sends $G from UI, not during streaming
                // If parser state query is needed, it should be done through CentralStatusQuerier

                return true;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendControlCharacterAsync(char controlChar)
        {
            if (!IsConnected)
            {
                LogImportantMessage("> HATA: Bağlantı yok - kontrol karakteri gönderilemedi");
                return false;
            }

            try
            {
                if (IsImportantControlCharacter(controlChar))
                {
                    LogImportantMessage($"> Kontrol karakteri gönderiliyor:0x{((int)controlChar):X2}");
                }

                await _gcodeSender.SendControlCharacterAsync(controlChar);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                LogImportantMessage($"> HATA: Port durumu geçersiz - {ex.Message}");
                await CheckAndHandleConnectionLossAsync();
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                LogImportantMessage($"> HATA: Port erişim engellendi - {ex.Message}");
                await CheckAndHandleConnectionLossAsync();
                return false;
            }
            catch (System.IO.IOException ex)
            {
                LogImportantMessage($"> HATA: I/O hatası - {ex.Message}");
                await CheckAndHandleConnectionLossAsync();
                return false;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: Kontrol karakteri gönderme hatası - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a command directly to the serial port, bypassing the GCodeSender buffer tracking.
        /// Use this for commands like $G that should not interfere with streaming buffer counting.
        /// The command will be sent with a newline appended.
        /// WARNING: The 'ok' response from this command will NOT be tracked by streaming service.
        /// </summary>
        public async Task<bool> SendDirectCommandAsync(string command)
        {
            if (!IsConnected)
            {
                return false;
            }

            try
            {
                // Send directly to port manager, bypassing GCodeSender tracking
                // Append newline like normal G-code commands
                await _portManager.SendDataAsync(command + "\n");
                System.Diagnostics.Debug.WriteLine($"[ConnectionManager] Direct command sent: {command}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConnectionManager] Direct command error: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (IsConnected)
                {
                    LogImportantMessage("> Bağlantı kapatılıyor...");
                    _portManager.Disconnect();
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> UYARI: Disconnect sırasında hata - {ex.Message}");
            }
        }

        public async Task RefreshPortsAsync()
        {
            if (CanRefreshPorts)
            {
                await FindAndSelectDeviceAsync(_config.SupportedDevices);
            }
        }

        #endregion

        #region Private Methods

        private void UpdateActivePortFlag()
        {
            try
            {
                var active = ExtractPortName(SelectedPort);
                foreach (var pi in AvailablePortInfos)
                {
                    pi.IsActive = IsConnected && !string.IsNullOrEmpty(active) && string.Equals(pi.PortName, active, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        private async Task CheckAndHandleConnectionLossAsync()
        {
            if (IsConnected && !_portManager.IsOpen)
            {
                LogImportantMessage("> Bağlantı kaybı tespit edildi, disconnect ediliyor...");
                await Task.Run(() => Disconnect());
            }
        }

        private async Task FindAndSelectDeviceAsync(params string[] deviceNames)
        {
            lock (_scanLock)
            {
                if (_isPortScanPending)
                {
                    return;
                }
                _isPortScanPending = true;
            }

            try
            {
                List<SerialPortManager.PortInfo> allInfos = await Task.Run(() => _portManager.GetDetailedPortInfos());

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailablePorts.Clear();
                    AvailablePortInfos.Clear();
                    foreach (var info in allInfos)
                    {
                        AvailablePorts.Add(info.Caption);
                        AvailablePortInfos.Add(info);
                    }
                });

                var deviceResult = FindSupportedDevice(allInfos.Select(i => i.Caption).ToList(), deviceNames);

                if (deviceResult.Found)
                {
                    await HandleDeviceFoundAsync(deviceResult);
                }
                else
                {
                    await HandleNoDeviceFoundAsync(allInfos.Select(i => i.Caption).ToList());
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: Port tarama hatası - {ex.Message}");
            }
            finally
            {
                            NotifyConnectionSnapshotChanged();
                lock (_scanLock)
                {
                    _isPortScanPending = false;
                }
            }
        }

        private (bool Found, string Port, string DeviceName) FindSupportedDevice(List<string> ports, string[] deviceNames)
        {
            foreach (string deviceName in deviceNames)
            {
                var targetPort = ports.FirstOrDefault(p =>
                    p.IndexOf(deviceName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!string.IsNullOrEmpty(targetPort))
                {
                    return (true, targetPort, deviceName);
                }
            }
            return (false, null, null);
        }

        private async Task HandleDeviceFoundAsync((bool Found, string Port, string DeviceName) deviceResult)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SelectedPort = deviceResult.Port;
                SelectedPortInfo = AvailablePortInfos.FirstOrDefault(i => i.PortName == ExtractPortName(deviceResult.Port));
                LogImportantMessage($"CİHAZ BULUNDU: {deviceResult.DeviceName} - {deviceResult.Port}");
            });

            await Task.Delay(_config.DeviceReconnectDelay);

            if (!IsConnected && _config.EnableAutoConnect)
            {
                await AutoConnectToSelectedPortAsync();
            }
        }

        private async Task HandleNoDeviceFoundAsync(List<string> allPorts)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (allPorts.Count > 0)
                {
                    SelectedPort = allPorts[0];
                    SelectedPortInfo = AvailablePortInfos.FirstOrDefault(i => i.PortName == ExtractPortName(SelectedPort));
                }
            });
        }

        private async Task AutoConnectToSelectedPortAsync()
        {
            lock (_connectionLock)
            {
                if (_isConnectionInProgress)
                {
                    return;
                }
                _isConnectionInProgress = true;
            }

            try
            {
                if (string.IsNullOrEmpty(SelectedPort) || IsConnected)
                {
                    return;
                }

                string portName = ExtractPortName(SelectedPort);
                if (string.IsNullOrEmpty(portName))
                {
                    LogImportantMessage("> HATA: Geçersiz port adı!");
                    return;
                }

                await ClearSettingsAsync();

                bool connectionResult = await _portManager.ConnectAsync(portName, _config.BaudRate);

                if (connectionResult)
                {
                    await PerformInitialConnectionSetupAsync();
                }
                else
                {
                    LogImportantMessage("> HATA: Port bağlantısı başarısız!");
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: Bağlantı hatası - {ex.Message}");
            }
            finally
            {
                lock (_connectionLock)
                {
                    _isConnectionInProgress = false;
                }
            }
        }

        private async Task ConnectOrDisconnectAsync()
        {
            if (IsConnected)
            {
                await PerformDisconnectAsync();
            }
            else
            {
                await PerformManualConnectAsync();
            }
        }

        private Task PerformDisconnectAsync()
        {
            try
            {
                LogImportantMessage("> Bağlantı kapatılıyor...");

                _portManager.Disconnect();
                var disconnectTime = DateTime.Now;

                if (_connectionStartTime.HasValue)
                {
                    _lastConnectionDuration = disconnectTime - _connectionStartTime.Value;
                }

                LogImportantMessage($"> Disconnected at {disconnectTime:HH:mm:ss}");
                _connectionStartTime = null;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: Disconnect işlemi sırasında hata - {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private async Task PerformManualConnectAsync()
        {
            lock (_connectionLock)
            {
                if (_isConnectionInProgress)
                {
                    LogImportantMessage("> Bağlantı zaten devam ediyor...");
                    return;
                }
                _isConnectionInProgress = true;
            }

            try
            {
                string portName = ExtractPortName(SelectedPort);
                if (string.IsNullOrEmpty(portName))
                {
                    LogImportantMessage("> HATA: Geçerli bir port seçin!");
                    return;
                }

                await ClearSettingsAsync();

                if (await _portManager.ConnectAsync(portName, _config.BaudRate))
                {
                    await PerformInitialConnectionSetupAsync();
                }
                else
                {
                    LogImportantMessage("> HATA: Bağlantı başarısız!");
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: Manuel bağlantı hatası - {ex.Message}");
            }
            finally
            {
                lock (_connectionLock)
                {
                    _isConnectionInProgress = false;
                }
            }
        }

        private async Task ClearSettingsAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_settingsLock)
                {
                    Settings.Clear();
                    _settingsById.Clear();
                    _isSettingsLoadingComplete = false;
                    OnPropertyChanged(nameof(IsSettingsLoadingComplete));
                }
            });
        }

    // --- Dayanıklı Unlock Yardımcı Metotları ---
        private async Task<bool> RobustUnlockAsync()
        {
            try
            {
                LogImportantMessage("> 🔓 Dayanıklı unlock başlıyor ($X)...");
                LogImportantMessage("> ? $X gönderiliyor");
                await _gcodeSender.SendCommandAsync("$X");

                // Status sorguları (realtime - no newline, like OpenBuilds)
                for (int s = 0; s < _config.PostUnlockStatusCheckCount; s++)
                {
                    await Task.Delay(_config.PostUnlockStatusIntervalMs);
                    await _gcodeSender.SendControlCharacterAsync('?');
                }

                LogImportantMessage("> ✅ Unlock sekansı tamamlandı");
                return true;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> ? RobustUnlock hata: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> OptionalHomeAsync()
        {
            if (!_config.AutoHomeAfterUnlock)
                return false;
            try
            {
                LogImportantMessage("> 🏠 Otomatik homing başlatılıyor ($H)...");
                await _gcodeSender.SendCommandAsync("$H");
                // Homing süresi boyunca periyodik status sorgusu
                int total = _config.PostHomeDelayMs;
                int step = 500;
                int elapsed = 0;
                while (elapsed < total)
                {
                    await Task.Delay(step);
                    elapsed += step;
                    await _gcodeSender.SendControlCharacterAsync('?');
                }
                LogImportantMessage("> ? Homing bekleme süresi tamamlandı");
                return true;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> ? Homing hata: {ex.Message}");
                return false;
            }
        }
        // ------------------------------------------------

        private async Task PerformInitialConnectionSetupAsync()
        {
            var setupTimer = System.Diagnostics.Stopwatch.StartNew();
            _isInitialSetupComplete = false;
            _isSettingsLoadingComplete = false;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LoadingStarted?.Invoke();
            });

            LogImportantMessage("> 🛠️ Config okuma başlatılıyor...");
            LogImportantMessage("> ⏳ Settings yüklenene kadar bekleyiniz...");

            // 1) RESET
            LoadingStatusChanged?.Invoke("⏳ GRBL Reset gönderiliyor...");
            var resetTimer = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(_config.SettingsRequestDelay); // Delay for settings request
            await _gcodeSender.SendControlCharacterAsync(GRBL_RESET_CHAR);
            await Task.Delay(_config.GrblResetDelay);
            resetTimer.Stop();
            LogImportantMessage($"> ✅ Reset temel gecikme: {resetTimer.ElapsedMilliseconds}ms");

            // Ek stabilizasyon (önceki hızlı unlock sorununu azaltır)
            LogImportantMessage($"> ⏱️ Reset sonrası stabilizasyon beklemesi: {_config.PostResetStabilizeDelayMs}ms");
            await Task.Delay(_config.PostResetStabilizeDelayMs);

            // 2) DAYANIKLI UNLOCK
            LoadingStatusChanged?.Invoke("🔓 Alarm / kilit açılıyor ($X dayanıklı)...");
            bool unlockOk = await RobustUnlockAsync();
            if (!unlockOk)
            {
                LogImportantMessage("> ⚠️ Unlock başarısız görünüyor — yine de devam edilecek");
            }

            // İlave küçük status ping (realtime - no newline)
            await _gcodeSender.SendControlCharacterAsync('?');
            await Task.Delay(250);

            // 3) OPSIYONEL HOMING
            if (_config.AutoHomeAfterUnlock)
            {
                LoadingStatusChanged?.Invoke("🏠 Homing ($H) uygulanıyor...");
                await OptionalHomeAsync();
                await _gcodeSender.SendControlCharacterAsync('?');
                await Task.Delay(400);
            }

            // 4) CONFIG ($CD) — artık unlock sonrası
            LoadingStatusChanged?.Invoke("🔎 FluidNC Config sorgulanıyor ($CD)...");
            var configTimer = System.Diagnostics.Stopwatch.StartNew();
            LogImportantMessage("> FluidNC Config sorgusu gönderiliyor...");
            
            // Create TaskCompletionSource to wait for config response
            _configResponseTcs = new TaskCompletionSource<bool>();
            await _gcodeSender.SendCommandAsync(FLUIDNC_CONFIG_COMMAND);
            
            // Wait for config response to complete or timeout
            var configTask = _configResponseTcs.Task;
            var timeoutTask = Task.Delay(_config.SettingsCompleteDelay * 2);
            await Task.WhenAny(configTask, timeoutTask);
            
            configTimer.Stop();
            LogImportantMessage($"> ✅ FluidNC Config tamamlandı: {configTimer.ElapsedMilliseconds}ms");
            LogImportantMessage($"> 🔎 FluidNC tespit durumu: {(_isFluidNCDetected ? "EVET ✅" : "HAYIR ❌")}");

            // 5) GRBL SETTINGS ($$)
            LoadingStatusChanged?.Invoke("📋 GRBL Settings sorgulanıyor ($$)...");
            var grblTimer = System.Diagnostics.Stopwatch.StartNew();
            LogImportantMessage("> GRBL Settings sorgusu gönderiliyor...");
            await _gcodeSender.SendCommandAsync(GRBL_SETTINGS_COMMAND);
            await Task.Delay(_config.SettingsCompleteDelay * 2);
            grblTimer.Stop();
            LogImportantMessage($"> ✅ GRBL Settings tamamlandı: {grblTimer.ElapsedMilliseconds}ms");

            // 6) EK EKSİK KONTROLÜ
            LoadingStatusChanged?.Invoke("?? Eksik settings kontrol ediliyor...");
            var secondTimer = System.Diagnostics.Stopwatch.StartNew();
            LogImportantMessage("> Eksik settings için ikinci sorgu...");
            await _gcodeSender.SendCommandAsync(GRBL_SETTINGS_COMMAND);
            await Task.Delay(_config.SettingsCompleteDelay);
            secondTimer.Stop();
            LogImportantMessage($"> ✅ İkinci settings sorgusu tamamlandı: {secondTimer.ElapsedMilliseconds}ms");

   var connectTime = DateTime.Now;
    _connectionStartTime = connectTime;

   LogImportantMessage($"> OTOMATİK BAĞLANTI BAŞARILI! ({connectTime:HH:mm:ss})");

            LoadingStatusChanged?.Invoke("⚙️ Settings işleniyor ve maksimum değerler atanıyor...");
            var processingTimer = System.Diagnostics.Stopwatch.StartNew();
            await ReportSettingsStatusAsync();
            await LogAllConfigurationAsync();
            processingTimer.Stop();
            LogImportantMessage($"> ✅ Settings işleme tamamlandı: {processingTimer.ElapsedMilliseconds}ms");

            _isInitialSetupComplete = true;

            LoadingStatusChanged?.Invoke("🧩 Son kontroller ve Jog panel hazırlanıyor...");
            var finalizationTimer = System.Diagnostics.Stopwatch.StartNew();
            await FinalizeSettingsAsync();
            finalizationTimer.Stop();

            setupTimer.Stop();
            LogImportantMessage($"> ✅ Finalization tamamlandı: {finalizationTimer.ElapsedMilliseconds}ms");
            LogImportantMessage($"> 🧮 TOPLAM CONFIG OKUMA SÜRESİ: {setupTimer.ElapsedMilliseconds}ms ({setupTimer.Elapsed.TotalSeconds:F1}s)");

            _isSettingsLoadingComplete = true;
            OnPropertyChanged(nameof(IsSettingsLoadingComplete));

            // FIRMWARE INFO QUERY - TÜM AYARLAR ALINDIKTAN SONRA
            LoadingStatusChanged?.Invoke("⏳ Firmware bilgisi sorgulanıyor ($I)...");
            LogImportantMessage("> ===============================================");
            LogImportantMessage("> 🔍 FIRMWARE INFO QUERY ($I) - Settings sonrası");
            LogImportantMessage("> ===============================================");
            LogImportantMessage("> Firmware info sorgusu gönderiliyor ($I)...");
            await _gcodeSender.SendCommandAsync("$I");
            await Task.Delay(800); // Longer delay to ensure response is processed
            LogImportantMessage("> ✓ $I komutu gönderildi, yanıt işlendi");

            // 6.8) MACHINE STATE ($G, $#)
            try
            {
                LoadingStatusChanged?.Invoke("🔎 Makine durumu alınıyor ($G, $#)...");
                _lastGcLine = null;
                _lastParamLines.Clear();
                _lastSentSetupCommand = "$G";
                await _gcodeSender.SendCommandAsync("$G");
                await Task.Delay(250);
                _lastSentSetupCommand = "$#";
                await _gcodeSender.SendCommandAsync("$#");
                await Task.Delay(500);
                _lastSentSetupCommand = null;
                OnPropertyChanged(nameof(ParserStateDisplay));
                OnPropertyChanged(nameof(MachineParamsText));
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> ⚠️ Makine durumu alınamadı: {ex.Message}");
            }

            // Heuristic correction: If we are on FluidNC and config clearly belongs to Raptorex
            // but welcome parsing didn't switch the firmware name, fix it now.
            if (FirmwareName == "Grbl" || FirmwareName == "Unknown")
            {
                bool configHintsRaptorex = !string.IsNullOrWhiteSpace(ConfigName) &&
                    ConfigName.IndexOf("Raptorex", StringComparison.OrdinalIgnoreCase) >= 0;
                if (_sawRaptorexWelcome || (_isFluidNCDetected && configHintsRaptorex))
                {
                    FirmwareName = "RaptorexCNC";
                    LogImportantMessage("> ✓ FirmwareName corrected to RaptorexCNC (post-parse heuristic)");
                    if (FirmwareVersion == "Unknown" && !string.IsNullOrWhiteSpace(_lastGrblVersionSeen))
                    {
                        FirmwareVersion = _lastGrblVersionSeen;
                        LogImportantMessage($"> ✓ FirmwareVersion filled from seen Grbl base: {_lastGrblVersionSeen}");
                    }
                }
            }

            // Final fallback: if version still unknown but we saw a Grbl base earlier, use it
            if (FirmwareVersion == "Unknown" && !string.IsNullOrWhiteSpace(_lastGrblVersionSeen))
            {
                FirmwareVersion = _lastGrblVersionSeen;
                LogImportantMessage($"> ✓ FirmwareVersion finalized from seen Grbl base: {_lastGrblVersionSeen}");
            }

            // Log summaries only in debug mode; keep console clean otherwise
            if (_config?.EnableDebugLogging == true)
            {
                LogImportantMessage("> ===============================================");
                LogImportantMessage("> 🧭 MACHINE STATE SUMMARY");
                LogImportantMessage("> ===============================================");
                LogImportantMessage($">   • Parser: {(_lastGcLine ?? "(bilgi yok)")}");
                if (_lastParamLines.Count > 0)
                {
                    foreach (var p in _lastParamLines)
                    {
                        LogImportantMessage($">   • {p}");
                    }
                }
                else
                {
                    LogImportantMessage(">   • Parametreler: (bilgi yok)");
                }
                LogImportantMessage("> ===============================================");

                // Firmware summary (augmented with connection + machine state)
                LogImportantMessage("> ===============================================");
                LogImportantMessage("> 📄 FIRMWARE & MACHINE INFO SUMMARY");
                LogImportantMessage("> ===============================================");
                LogImportantMessage($">   • Firmware Name: {FirmwareName}");
                LogImportantMessage($">   • Firmware Version: {FirmwareVersion}");
                if (!string.IsNullOrWhiteSpace(_lastGrblVersionSeen))
                    LogImportantMessage($">   • Core Base: Grbl {_lastGrblVersionSeen}");
                LogImportantMessage($">   • Board Name: {BoardName}");
                LogImportantMessage($">   • Config Name: {ConfigName}");
                LogImportantMessage($">   • Detected Axes: {DetectedAxisCount}");
                LogImportantMessage($">   • FluidNC Compatible: {(IsFluidNC ? "YES" : "NO")} ");
                if (!string.IsNullOrWhiteSpace(_firmwareOptions))
                {
                    LogImportantMessage($">   • Capabilities: {_firmwareOptions}");
                }
                try
                {
                    var details = _portManager.GetConnectionDetails();
                    var info = SelectedPortInfo;
                    string transport = ClassifyTransport(info);
                    var ids = ExtractVidPid(info?.PnpDeviceId);
                    string vidPid = (ids.vid != null && ids.pid != null) ? $" (VID:PID={ids.vid}:{ids.pid})" : string.Empty;

                    LogImportantMessage(">   — CONNECTION —");
                    LogImportantMessage($">   • Port: {SelectedPort}");
                    if (!string.IsNullOrWhiteSpace(info?.Description))
                        LogImportantMessage($">   • Description: {info.Description}");
                    if (!string.IsNullOrWhiteSpace(info?.Manufacturer))
                        LogImportantMessage($">   • Manufacturer: {info.Manufacturer}");
                    if (!string.IsNullOrWhiteSpace(info?.PnpDeviceId))
                        LogImportantMessage($">   • PNP ID: {info.PnpDeviceId}");
                    LogImportantMessage($">   • Transport: {transport}{vidPid}");

                    LogImportantMessage(">   — SERIAL —");
                    LogImportantMessage($">   • Baud: {details.BaudRate}");
                    LogImportantMessage($">   • Data/Parity/Stop: {details.DataBits}/{details.Parity}/{details.StopBits}");
                    LogImportantMessage($">   • Handshake: {details.Handshake}, DTR:{details.DtrEnable}, RTS:{details.RtsEnable}");
                    LogImportantMessage($">   • Encoding: {details.EncodingName}");
                    LogImportantMessage($">   • Timeouts (R/W): {details.ReadTimeout}/{details.WriteTimeout} ms");
                    LogImportantMessage($">   • Buffers (R/W): {details.ReadBufferSize}/{details.WriteBufferSize} bytes");
                }
                catch { }

                // Machine state snapshot (already captured via $G and $#)
                LogImportantMessage($">   • Parser: {(_lastGcLine ?? "(bilgi yok)")}");
                if (_lastParamLines.Count > 0)
                {
                    foreach (var p in _lastParamLines)
                    {
                        LogImportantMessage($">   • {p}");
                    }
                }
                LogImportantMessage("> ===============================================");
            }

            LogImportantMessage("> ✅ Settings yükleme tamamlandı - Jog panel aktifleştirilebilir!");
            LogImportantMessage("> ✅ Sistem hazır - Timer'lar aktif.");

            LoadingStatusChanged?.Invoke("✅ Yükleme tamamlandı! Jog paneline geçiliyor...");
            await Task.Delay(1000);
            LoadingCompleted?.Invoke();

            SettingsLoadingCompleted?.Invoke();
        }

        /// <summary>
    /// FluidNC için status rapor alanlarını çalışma anında ayarlar.
    /// Amaç: 'line' alanı ile gerçek yürütülen satırı status raporlarında görmek.
    /// GRBL klasik sürümlerde hata dönebilir; bu durumda sadece loglanır.
        /// </summary>
        private async Task ConfigureStatusReportFieldsAsync()
        {
            // FluidNC '$Report/Fields' komutunu kabul eder (büyük/küçük harf duyarsız)
            // Burada pos,state,buffer,line alanlarını talep ediyoruz.
            const string cmd = "$Report/Fields=pos,state,buffer,line";
            LogImportantMessage($"> Status report alanları konfigüre ediliyor: {cmd}");
            _lastSentSetupCommand = cmd;
            await _gcodeSender.SendCommandAsync(cmd);

            // Kısa bir beklemeden sonra bir status sorgusu gönderip ("?") ilk raporu tetikleyelim (realtime)
            await Task.Delay(200);
            _lastSentSetupCommand = "?";
            await _gcodeSender.SendControlCharacterAsync('?');
            LogImportantMessage("> Status report alanları talep edildi; ilk rapor isteniyor ('?')");
            _lastSentSetupCommand = null; // Clear after setup
        }

        private async Task FinalizeSettingsAsync()
        {
            var sortTimer = System.Diagnostics.Stopwatch.StartNew();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_settingsLock)
                {
                    try
                    {
                        var sortingTimer = System.Diagnostics.Stopwatch.StartNew();
                        SortSettingsByID();
                        sortingTimer.Stop();
                        LogImportantMessage($"> ✅ Settings sıralama: {sortingTimer.ElapsedMilliseconds}ms");

                        var detectionTimer = System.Diagnostics.Stopwatch.StartNew();
                        DetectAAxisAvailability();
                        detectionTimer.Stop();
                        LogImportantMessage($"> ✅ A ekseni algılama: {detectionTimer.ElapsedMilliseconds}ms");

                        var eventTimer = System.Diagnostics.Stopwatch.StartNew();
                        SettingsReceived?.Invoke(Settings);
                        eventTimer.Stop();
                        LogImportantMessage($"> ?? Event trigger: {eventTimer.ElapsedMilliseconds}ms");

                        LogImportantMessage($"> ? Settings finalized: {Settings.Count} total");
                        OnPropertyChanged(nameof(Settings));
                    }
                    catch (Exception ex)
                    {
                        LogImportantMessage($"> HATA: Settings finalization - {ex.Message}");
                    }
                }
            });

            sortTimer.Stop();
            LogImportantMessage($"> ?? Total finalization time: {sortTimer.ElapsedMilliseconds}ms");
        }

        private async Task ReportSettingsStatusAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_settingsLock)
                {
                    LogImportantMessage($"> Settings yüklendi: {Settings.Count} adet");
                    LogImportantMessage($"> Dictionary'de: {_settingsById.Count} adet");

                    if (Settings.Count > 0)
                    {
                        var criticalSettings = new int[] { 110, 111, 112, 113, 30, 103 };
                        var missingCritical = criticalSettings.Where(id => !Settings.Any(s => s.Id == id)).ToList();

                        if (missingCritical.Any())
                        {
                            LogImportantMessage($"> ?? EKSIK: Setting {string.Join(", ", missingCritical)}");
                        }
                        else
                        {
                            LogImportantMessage($"> ? Tüm kritik settings mevcut");
                        }

                        var maxJogSpeeds = Settings.Where(s => s.Id == 110 || s.Id == 111 || s.Id == 112 || s.Id == 113).ToList();
                        var spindleSpeed = Settings.FirstOrDefault(s => s.Id == 30);
                        var aStepsMm = Settings.FirstOrDefault(s => s.Id == 103);

                        foreach (var speed in maxJogSpeeds)
                        {
                            LogImportantMessage($"> Jog Speed ${speed.Id}: {speed.Value} mm/min");
                        }

                        if (spindleSpeed != null)
                        {
                            LogImportantMessage($"> Spindle Max Speed $30: {spindleSpeed.Value} RPM");
                        }

                        if (aStepsMm != null)
                        {
                            LogImportantMessage($"> A Axis Steps/mm $103: {aStepsMm.Value}");
                        }
                    }
                    else
                    {
                        LogImportantMessage("> ? Settings alınamadı!");
                    }
                }
            });
        }

        private async Task LogAllConfigurationAsync()
        {
            // Suppress the large GRBL settings report unless debug is enabled
            if (_config?.EnableDebugLogging != true)
            {
                return;
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_settingsLock)
                {
                    LogImportantMessage("> ===============================================");
                    LogImportantMessage("> 📄 GRBL SETTINGS RAPORU - $CD + $$ Komutları");
                    LogImportantMessage("> ===============================================");

                    LogImportantMessage($"> 🔌 BAĞLANTI AYARLARI:");
                    LogImportantMessage($">   • Port: {SelectedPort}");
                    LogImportantMessage($">   • Baud Rate: {_config.BaudRate}");
                    LogImportantMessage($">   • FluidNC Config: $CD");
                    LogImportantMessage($">   • GRBL Settings: $$");
                    LogImportantMessage($">   • FluidNC Version: v3.7.16");

                    LogImportantMessage("> ===============================================");

                    var grblSettings = Settings.Where(s => s.Id >= 0 && s.Id <= 255).ToList();
                    var fluidncSettings = Settings.Where(s => !string.IsNullOrEmpty(s.Description) &&
                                                              (s.Description.Contains(":") || s.Description.Contains("_pin") ||
                                                               s.Description.Contains("reset_") || s.Description.Contains("must_home"))).ToList();

                    LogImportantMessage($"> 📊 SETTINGS ÖZETİ:");
                    LogImportantMessage($">   • Toplam Settings: {Settings.Count}");
                    LogImportantMessage($">   • GRBL Format Settings: {grblSettings.Count}");
                    LogImportantMessage($">   • FluidNC Config Settings: {fluidncSettings.Count}");

                    LogImportantMessage("> ===============================================");

                    if (grblSettings.Any())
                    {
                        LogImportantMessage($"> ⭐ ÖNEMLİ GRBL SETTINGS:");

                        var criticalIds = new int[] { 30, 103, 110, 111, 112, 113, 120, 121, 122, 123 };
                        var importantSettings = grblSettings.Where(s => criticalIds.Contains(s.Id)).OrderBy(s => s.Id);

                        foreach (var setting in importantSettings)
                        {
                            string meaning = !string.IsNullOrEmpty(setting.KnownMeaning) ?
                                           $" ({setting.KnownMeaning})" : "";
                            LogImportantMessage($">   • ${setting.Id} = {setting.Value}{meaning}");
                        }

                        var otherCount = grblSettings.Count - importantSettings.Count();
                        if (otherCount > 0)
                        {
                            LogImportantMessage($">   • + {otherCount} other settings loaded");
                        }
                    }

                    LogImportantMessage("> ===============================================");

                    LogImportantMessage($"> 🧪 KRİTİK AYARLAR KONTROLÜ:");
                    var requiredSettingIds = new int[] { 110, 111, 112, 113, 30, 103 };
                    var missingRequired = requiredSettingIds.Where(id => !Settings.Any(s => s.Id == id)).ToList();
                    var foundRequired = requiredSettingIds.Where(id => Settings.Any(s => s.Id == id)).ToList();

                    foreach (var id in foundRequired)
                    {
                        var setting = Settings.First(s => s.Id == id);
                        LogImportantMessage($">   ? ${id} = {setting.Value} ({setting.KnownMeaning})");
                    }

                    foreach (var id in missingRequired)
                    {
                        LogMessageAdded?.Invoke($">   ? ${id} = EKSIK!");
                    }

                    LogImportantMessage("> ===============================================");
                    LogImportantMessage("> ?? GRBL SETTINGS RAPORU TAMAMLANDI");
                    LogImportantMessage("> ===============================================");
                }
            });
        }

    // ? A ekseni algılama metodu - settings'ler yüklendikten sonra çağrılır
        private void DetectAAxisAvailability()
        {
            try
            {
                LogImportantMessage($"> === A EKSENİ ALGILAMA BAŞLADI ===");
                bool hasAAxisConfig = false;
                bool hasAAxisSettings = false;
                hasAAxisConfig = _isInAAxisSection || _recentLines.Any(line =>
                    line.Trim().Equals("a:", StringComparison.OrdinalIgnoreCase));
                LogImportantMessage($"> FluidNC Config A ekseni section kontrolü:");
                LogImportantMessage($">   • _isInAAxisSection: {_isInAAxisSection}");
                LogImportantMessage($">   • Recent lines'da 'a:' var mı: {_recentLines.Any(line => line.Trim().Equals("a:", StringComparison.OrdinalIgnoreCase))}");
                LogImportantMessage($">   • hasAAxisConfig: {hasAAxisConfig}");
                var aStepSetting = Settings.FirstOrDefault(s => s.Id == 103);
                var aMaxSpeedSetting = Settings.FirstOrDefault(s => s.Id == 113);
                var aAccelSetting = Settings.FirstOrDefault(s => s.Id == 123);
                LogImportantMessage($"> GRBL Settings A ekseni ayarları kontrolü:");
                LogImportantMessage($">   • $103 (A steps/mm): {(aStepSetting != null ? aStepSetting.Value : "YOK")}");
                LogImportantMessage($">   • $113 (A max rate): {(aMaxSpeedSetting != null ? aMaxSpeedSetting.Value : "YOK")}");
                LogImportantMessage($">   • $123 (A accel): {(aAccelSetting != null ? aAccelSetting.Value : "YOK")}");
                hasAAxisSettings = aStepSetting != null && aMaxSpeedSetting != null;
                LogImportantMessage($">   • hasAAxisSettings: {hasAAxisSettings}");
                LogImportantMessage($"> Toplam Settings Count: {Settings.Count}");
                var allARelatedSettings = Settings.Where(s => s.Id >= 103 && s.Id <= 133 && (s.Id == 103 || s.Id == 113 || s.Id == 123 || s.Id == 133)).ToList();
                if (allARelatedSettings.Any())
                {
                    LogImportantMessage($"> A ekseni ile ilgili tüm settings:");
                    foreach (var setting in allARelatedSettings)
                    {
                        LogImportantMessage($">   • ${setting.Id} = {setting.Value} ({setting.KnownMeaning})");
                    }
                }
                else
                {
                    LogImportantMessage($"> A ekseni ile ilgili hiçbir setting bulunamadı");
                }
                LogImportantMessage($"> Recent lines debug (son {_recentLines.Count} satır):\n");
                foreach (var line in _recentLines)
                {
                    LogImportantMessage($">   • '{line}'");
                }
                IsAAxisAvailable = hasAAxisConfig || hasAAxisSettings;
                LogImportantMessage($"> === A EKSENİ ALGILAMA SONUCU ===");
                LogImportantMessage($"> hasAAxisConfig: {hasAAxisConfig}");
                LogImportantMessage($"> hasAAxisSettings: {hasAAxisSettings}");
                LogImportantMessage($"> FINAL IsAAxisAvailable: {IsAAxisAvailable}");
                if (IsAAxisAvailable)
                {
                    LogImportantMessage($"> ? A ekseni tespit edildi!");
                    if (hasAAxisConfig) LogImportantMessage($">   • FluidNC config'de A ekseni section'ı bulundu");
                    if (hasAAxisSettings) LogImportantMessage($">   • GRBL settings'lerde A ekseni ayarları bulundu");
                    if (aStepSetting != null) LogImportantMessage($">   • $103 (A steps/mm): {aStepSetting.Value}");
                    if (aMaxSpeedSetting != null) LogImportantMessage($">   • $113 (A max rate): {aMaxSpeedSetting.Value}");
                }
                else
                {
                    LogImportantMessage($"> ⚠️ A ekseni bulunamadı - sadece XYZ eksenler aktif");
                    LogImportantMessage($"> Olası nedenler:");
                    LogImportantMessage($">   • FluidNC config'de 'a:' section'ı yok");
                    LogImportantMessage($">   • GRBL settings'de $103 veya $113 yok");
                    LogImportantMessage($">   • CNC makinesinde fiziksel A ekseni kurulu değil");
                }
                LogImportantMessage($"> === A EKSENİ ALGILAMA BİTTİ ===");
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: A ekseni algılama - {ex.Message}");
                LogImportantMessage($"> Stack trace: {ex.StackTrace}");
                IsAAxisAvailable = false;
            }
        }

        private void OnConnectionStatusChanged(bool status)
        {
            IsConnected = status;

   if (status)
     {
    if (_config.EnableSoundNotifications)
    {
        PlayConnectionSuccessSound();
     }
    var connectionTime = DateTime.Now;
   _connectionStartTime = connectionTime;
        LogImportantMessage($"> ✅ BAĞLANTI KURULDU ({connectionTime:HH:mm:ss})");

            // Bağlantı kurulur kurulmaz ayrıntılı bağlantı bilgilerini yazdır (yalnızca debug modda)
    if (_config?.EnableDebugLogging == true)
    {
          try { LogConnectionDetails(); } catch { }
        }

             _isSettingsLoadingComplete = false;
      OnPropertyChanged(nameof(IsSettingsLoadingComplete));
         NotifyConnectionSnapshotChanged();
       }
        else
      {
      _isInitialSetupComplete = false;
     _isSettingsLoadingComplete = false;
     OnPropertyChanged(nameof(IsSettingsLoadingComplete));

       if (_config.EnableSoundNotifications)
          {
  PlayDisconnectionSound();
       }
   var disconnectTime = DateTime.Now;
       LogImportantMessage($"> ❌ BAĞLANTI KESİLDİ ({disconnectTime:HH:mm:ss})");
      NotifyConnectionSnapshotChanged();
  }
        }

        private void NotifyConnectionSnapshotChanged()
        {
            OnPropertyChanged(nameof(ConnectionPort));
            OnPropertyChanged(nameof(ConnectionDescription));
            OnPropertyChanged(nameof(ConnectionManufacturer));
            OnPropertyChanged(nameof(ConnectionPnpId));
            OnPropertyChanged(nameof(ConnectionTransport));
            OnPropertyChanged(nameof(SerialBaud));
            OnPropertyChanged(nameof(SerialFormat));
            OnPropertyChanged(nameof(SerialHandshake));
            OnPropertyChanged(nameof(SerialEncoding));
            OnPropertyChanged(nameof(SerialTimeouts));
            OnPropertyChanged(nameof(SerialBuffers));
            OnPropertyChanged(nameof(BaudRate));
        }

        private void PlayConnectionSuccessSound()
        {
            try
            {
                Task.Run(async () =>
                {
                    Console.Beep(SUCCESS_BEEP_FREQ_1, SUCCESS_BEEP_DURATION_1);
                    await Task.Delay(SUCCESS_BEEP_INTERVAL);
                    Console.Beep(SUCCESS_BEEP_FREQ_2, SUCCESS_BEEP_DURATION_2);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sound error: {ex.Message}");
            }
        }

        private void PlayDisconnectionSound()
        {
            try
            {
                Task.Run(() => Console.Beep(DISCONNECT_BEEP_FREQ, DISCONNECT_BEEP_DURATION));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sound error: {ex.Message}");
            }
        }

        private void OnResponseReceived(string response)
        {
            // IMPORTANT: Do not drop whitespace-only chunks.
            // SerialPort.ReadExisting() may deliver CR/LF delimiters as separate events.
            // If we drop those, downstream consumers (e.g., streaming OK parser) can miss line boundaries.
            if (string.IsNullOrEmpty(response)) return;

            try
            {
                if (_isReceivingConfig)
                {
                    _configBuffer.Append(response);

                    if (response.Contains("ok") || response.Contains("error") || response.Contains("verbose_errors"))
                    {
                        _isReceivingConfig = false;
                        string completeConfig = _configBuffer.ToString();
                        _configBuffer.Clear();

                        LogImportantMessage($"> COMPLETE CONFIG RECEIVED: {completeConfig.Length} chars");
                        ProcessCompleteConfigResponse(completeConfig);
                        
                        // Signal that config response is complete
                        _configResponseTcs?.TrySetResult(true);
                        return;
                    }
                    return;
                }

                if (response.Contains("board:") || response.Contains("name:") || response.Contains("axes:"))
                {
                    _isReceivingConfig = true;
                    _isFluidNCDetected = true; // Detected FluidNC-style config response
                    OnPropertyChanged(nameof(IsFluidNC));
                    _configBuffer.Clear();
                    _configBuffer.Append(response);
                    LogImportantMessage($"> CONFIG BUFFERING START - FluidNC DETECTED");
                    LogImportantMessage($"> ✓ IsFluidNC set to TRUE (from $CD response)");
                    return;
                }
                ProcessNormalResponse(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnResponseReceived error: {ex.Message}");
                LogImportantMessage($"> Response parsing error: {ex.Message}");
            }
        }

        private void ProcessCompleteConfigResponse(string completeResponse)
        {
            try
            {
                string[] lines = completeResponse.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                    ProcessConfigLine(trimmedLine);
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> Config processing error: {ex.Message}");
            }
        }

        private void ProcessConfigLine(string trimmedLine)
        {
            // Extract firmware info from FluidNC config
            if (trimmedLine.StartsWith("board:", StringComparison.OrdinalIgnoreCase))
            {
                BoardName = trimmedLine.Substring(6).Trim();
                // DON'T override FirmwareName here - VER parsing already set correct name
                LogImportantMessage($"> Board: {BoardName}");
                return;
            }
            if (trimmedLine.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                ConfigName = trimmedLine.Substring(5).Trim();
                LogImportantMessage($"> Config: {ConfigName}");
                return;
            }
            if (trimmedLine.StartsWith("axes:", StringComparison.OrdinalIgnoreCase))
            {
                var axesStr = trimmedLine.Substring(5).Trim();
                if (axesStr.Contains("abc")) { DetectedAxisCount = 6; }
                else if (axesStr.Contains("ab")) { DetectedAxisCount = 5; }
                else if (axesStr.Contains("a")) { DetectedAxisCount = 4; }
                else { DetectedAxisCount = 3; }
                LogImportantMessage($"> Axes: {axesStr} ({DetectedAxisCount} detected)");
                return;
            }

            if (trimmedLine.Equals("x:", StringComparison.OrdinalIgnoreCase))
            {
                ResetAllAxisSections();
                _isInXAxisSection = true;
                LogImportantMessage($"> X EKSENI SECTION START: {trimmedLine}");
                return;
            }
            if (trimmedLine.Equals("y:", StringComparison.OrdinalIgnoreCase))
            {
                ResetAllAxisSections();
                _isInYAxisSection = true;
                LogImportantMessage($"> Y EKSENI SECTION START: {trimmedLine}");
                return;
            }
            if (trimmedLine.Equals("z:", StringComparison.OrdinalIgnoreCase))
            {
                ResetAllAxisSections();
                _isInZAxisSection = true;
                LogImportantMessage($"> Z EKSENI SECTION START: {trimmedLine}");
                return;
            }
            if (trimmedLine.Equals("a:", StringComparison.OrdinalIgnoreCase))
            {
                ResetAllAxisSections();
                _isInAAxisSection = true;
                LogImportantMessage($"> A EKSENI SECTION START: {trimmedLine}");
                return;
            }

            if (IsAnyAxisSectionActive() && (trimmedLine.Contains(":") &&
                (trimmedLine.StartsWith("control:") || trimmedLine.StartsWith("coolant:") ||
                 trimmedLine.StartsWith("probe:") || trimmedLine.StartsWith("macros:") ||
                 trimmedLine.StartsWith("user_outputs:") || trimmedLine.StartsWith("PWM:"))))
            {
                string currentAxis = GetCurrentAxisName();
                ResetAllAxisSections();
                LogImportantMessage($"> {currentAxis} SECTION END - New section: {trimmedLine}");
            }

            if (trimmedLine.Contains("speed_map:"))
            {
                var spindleSetting = MapSpindleConfigToGRBL(trimmedLine);
                if (spindleSetting != null)
                {
                    LogImportantMessage($"> SPINDLE CONFIG MAP: {trimmedLine} → ${spindleSetting.Id}={spindleSetting.Value}");
                    AddOrUpdateSetting(spindleSetting);
                }
            }

            if (IsAnyAxisSectionActive())
            {
                var mappedSetting = MapAxisConfigToGRBL(trimmedLine);
                if (mappedSetting != null)
                {
                    string axisName = GetCurrentAxisName();
                    LogImportantMessage($"> {axisName} CONFIG MAP: {trimmedLine} → ${mappedSetting.Id}={mappedSetting.Value}");
                    AddOrUpdateSetting(mappedSetting);
                }
            }
        }

        private void ProcessNormalResponse(string response)
        {
            string[] lines = response.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                // ✅ EVENT-DRIVEN: Forward ok/error/alarm to ResponseTracker for probe sequences
                ProcessResponseForTracker(trimmedLine);

                // Assemble bracketed lines that may arrive in fragments (e.g., "[G54:...", "[GC:...")
                // If we see a segment starting with '[' but not ending with ']', keep buffering
                if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && !trimmedLine.EndsWith("]", StringComparison.Ordinal))
                {
                    _pendingBracketLine = (_pendingBracketLine ?? string.Empty) + trimmedLine;
                    continue; // wait for the rest of the line in subsequent chunks
                }
                if (_pendingBracketLine != null && trimmedLine.EndsWith("]", StringComparison.Ordinal))
                {
                    // complete the buffered line
                    trimmedLine = _pendingBracketLine + trimmedLine;
                    _pendingBracketLine = null;
                }

                // Early welcome-line parse: "Grbl X.X [RaptorexCNC vY.YY ...]"
                var welcomeMatch = System.Text.RegularExpressions.Regex.Match(
                    trimmedLine,
                    @"^Grbl\s+(\d+\.\d+[a-z]?)\s*\[(?:RaptorexCNC)\s+v?(\d+\.\d+)[^\]]*\]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (welcomeMatch.Success)
                {
                    var grblBase = welcomeMatch.Groups[1].Value;      // e.g., 3.0
                    var vendorVer = welcomeMatch.Groups[2].Value;      // e.g., 0.01
                    FirmwareName = "RaptorexCNC";
                    // Prefer vendor firmware version (RaptorexCNC version)
                    if (!string.IsNullOrWhiteSpace(vendorVer))
                        FirmwareVersion = vendorVer;
                    if (string.IsNullOrWhiteSpace(_lastGrblVersionSeen))
                        _lastGrblVersionSeen = grblBase;
                    _sawRaptorexWelcome = true;
                    _isFluidNCDetected = true;
                    OnPropertyChanged(nameof(IsFluidNC));
                    LogImportantMessage($"> ✓ Welcome parsed: Firmware=RaptorexCNC v{vendorVer}, GrblBase={grblBase}");
                }

                // Opportunistically capture version/name hints from any line
                // 1) Remember if RaptorexCNC keyword ever appears
                if (trimmedLine.IndexOf("RaptorexCNC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _sawRaptorexWelcome = true;
                }
                // 2) Capture Grbl base version (first occurrence wins)
                var anyGrbl = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"Grbl\s+(\d+\.\d+[a-z]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (anyGrbl.Success && string.IsNullOrEmpty(_lastGrblVersionSeen))
                {
                    _lastGrblVersionSeen = anyGrbl.Groups[1].Value;
                }

                // 3) Capture optional capabilities line (Grbl/FluidNC often emit [OPT:...])
                if (trimmedLine.StartsWith("[OPT:", StringComparison.OrdinalIgnoreCase))
                {
                    _firmwareOptions = trimmedLine.Trim('[', ']');
                    LogImportantMessage($"> ✓ Capabilities reported: {_firmwareOptions}");
                }

                // 4) Capture parser state from $G response: [GC:...]
                if (trimmedLine.StartsWith("[GC:", StringComparison.OrdinalIgnoreCase))
                {
                    _lastGcLine = trimmedLine.Trim();
                    LogImportantMessage($"> Parser State captured: {_lastGcLine}");
                }

                // 5) Capture parameter lines from $# response (e.g., [G54:...], [G92:...], [TLO:...], [PRB:...])
                if ((trimmedLine.StartsWith("[G", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("[TLO:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("[PRB:", StringComparison.OrdinalIgnoreCase))
                    && trimmedLine.EndsWith("]", StringComparison.Ordinal)) // only store complete bracketed lines
                {
                    // Avoid duplicates, keep recent up to 16
                    if (!_lastParamLines.Contains(trimmedLine))
                    {
                        if (_lastParamLines.Count >= 16) _lastParamLines.RemoveAt(0);
                        _lastParamLines.Add(trimmedLine);
                    }
                    
                    // Parse PRB coordinates and feed to ProbeContactCache
                    if (trimmedLine.StartsWith("[PRB:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Format: [PRB:123.456,78.901,234.567:1]
                            var prbMatch = System.Text.RegularExpressions.Regex.Match(
                                trimmedLine,
                                @"\[PRB:([\d\.-]+),([\d\.-]+),([\d\.-]+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            if (prbMatch.Success &&
                                double.TryParse(prbMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double px) &&
                                double.TryParse(prbMatch.Groups[2].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double py) &&
                                double.TryParse(prbMatch.Groups[3].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pz))
                            {
                                CncControlApp.Managers.ProbeContactCache.SetRaw(px, py, pz);
                                if (_config.EnableDebugLogging)
                                {
                                    LogImportantMessage($"> 📍 PRB parsed: X={px:F3}, Y={py:F3}, Z={pz:F3}");
                                }
                            }
                        }
                        catch (Exception prbEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"PRB parse error: {prbEx.Message}");
                        }
                    }
                }

                // Extract firmware version from welcome/startup messages
                // *** PRIORITY 1: Check for RaptorexCNC FIRST (even if FirmwareVersion already set) ***
                if (FirmwareName != "RaptorexCNC" && trimmedLine.IndexOf("RaptorexCNC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogImportantMessage($"> 🔍 RaptorexCNC keyword found in line: {trimmedLine}");
                    
                    // Try to extract version from RaptorexCNC vX.XX pattern
                    var raptorexMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"RaptorexCNC\s+v?(\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    // Also extract Grbl base version from start: "Grbl X.X"
                    var grblMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"Grbl\s+(\d+\.\d+[a-z]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (raptorexMatch.Success)
                    {
                        FirmwareVersion = grblMatch.Success ? grblMatch.Groups[1].Value : raptorexMatch.Groups[1].Value;
                        FirmwareName = "RaptorexCNC";
                        _sawRaptorexWelcome = true;
                        _isFluidNCDetected = true;
                        OnPropertyChanged(nameof(IsFluidNC));
                        LogImportantMessage($"> ✓ Firmware: RaptorexCNC (Grbl {FirmwareVersion}) - FluidNC-based");
                        LogImportantMessage($"> ✓ IsFluidNC set to TRUE");
                    }
                    else
                    {
                        // Fallback: still trust the identity, fill version from Grbl if available
                        FirmwareName = "RaptorexCNC";
                        if (grblMatch.Success)
                        {
                            FirmwareVersion = grblMatch.Groups[1].Value;
                        }
                        else if (!string.IsNullOrWhiteSpace(_lastGrblVersionSeen))
                        {
                            FirmwareVersion = _lastGrblVersionSeen;
                        }
                        _isFluidNCDetected = true;
                        OnPropertyChanged(nameof(IsFluidNC));
                        LogImportantMessage($"> ⚠️ RaptorexCNC found without explicit version; using Grbl base '{FirmwareVersion}'");
                    }
                }
                // *** PRIORITY 2: Parse other firmware types only if FirmwareVersion unknown ***
                else if (FirmwareVersion == "Unknown")
                {
                    // [VER:...] format - parse version:firmware format
                    if (trimmedLine.StartsWith("[VER:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Log the exact VER line received
                        LogImportantMessage($"> ✓ [VER] line received: {trimmedLine}");
                        
                        // Extract everything between [VER: and ]
                        var verContent = trimmedLine.Substring(5).TrimEnd(']').Trim();
                        LogImportantMessage($"> ✓ VER content: '{verContent}'");
                        
                        // Split by ":" to extract version and firmware name
                        var parts = verContent.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (parts.Length >= 2)
                        {
                            // Format: [VER:version:firmware] (e.g., "3.0:RaptorexCNC" or "1.1h.20190825:RaptorexCNC")
                            FirmwareVersion = parts[0].Trim();
                            FirmwareName = parts[parts.Length - 1].Trim(); // Use last part as firmware name
                            LogImportantMessage($"> ✓ Parsed format detected");
                            LogImportantMessage($"> ✓ FirmwareVersion: '{FirmwareVersion}'");
                            LogImportantMessage($"> ✓ FirmwareName: '{FirmwareName}'");
                        }
                        else
                        {
                            // Single value format - use as-is for both
                            FirmwareName = verContent;
                            FirmwareVersion = verContent;
                            LogImportantMessage($"> ✓ Single value format");
                            LogImportantMessage($"> ✓ Both set to: '{verContent}'");
                        }
                        
                        // FluidNC detection is already done by $CD response
                        LogImportantMessage($"> ✓ IsFluidNC current value: {_isFluidNCDetected}");
                    }
                    // FluidNC format in MSG: "[MSG:INFO: FluidNC v3.7.16 ...]"
                    else if (trimmedLine.IndexOf("FluidNC", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"FluidNC\s+v?(\d+\.\d+\.?\d*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            FirmwareVersion = match.Groups[1].Value;
                            FirmwareName = "FluidNC";
                            _isFluidNCDetected = true;
                            OnPropertyChanged(nameof(IsFluidNC));
                            LogImportantMessage($"> ✓ Firmware: FluidNC v{FirmwareVersion}");
                            LogImportantMessage($"> ✓ IsFluidNC set to TRUE");
                        }
                    }
                    // Standard Grbl format: "Grbl 1.1f" (only if NOT RaptorexCNC)
                    else if (FirmwareName != "RaptorexCNC" && trimmedLine.IndexOf("Grbl", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"Grbl\s+(\d+\.\d+[a-z]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            FirmwareVersion = match.Groups[1].Value;
                            FirmwareName = "Grbl";
                            LogImportantMessage($"> Firmware: Grbl {FirmwareVersion}");
                        }
                    }
                }
                
                // Detect FluidNC from [MSG:Machine:...] or [MSG:Mode=BT:...] messages
                if (!_isFluidNCDetected && (trimmedLine.Contains("[MSG:Machine:") || trimmedLine.Contains("[MSG:Mode=BT:")))
                {
                    _isFluidNCDetected = true;
                    OnPropertyChanged(nameof(IsFluidNC));
                    if (FirmwareName == "Unknown") FirmwareName = "FluidNC";
                    LogImportantMessage($"> ✓ FluidNC detected from MSG format");
                    LogImportantMessage($"> ✓ IsFluidNC set to TRUE");
                }
                
                // Extract machine name from [MSG:Machine: ...] message
                if (trimmedLine.StartsWith("[MSG:Machine:", StringComparison.OrdinalIgnoreCase))
                {
                    var machineMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"\[MSG:Machine:\s*([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (machineMatch.Success && BoardName == "Unknown")
                    {
                        BoardName = machineMatch.Groups[1].Value.Trim();
                        LogImportantMessage($"> Machine Name: {BoardName}");
                    }
                }

                if (trimmedLine.StartsWith("$") && char.IsDigit(trimmedLine.ElementAtOrDefault(1)) && trimmedLine.Contains("="))
                {
                    var setting = _settingsParser.ParseLine(trimmedLine);
                    if (setting != null)
                    {
                        LogImportantMessage($"> GRBL SETTING PARSED: ${setting.Id} = {setting.Value} ({setting.KnownMeaning})");
                        AddOrUpdateSetting(setting, isGrblFallback: true);
                    }
                    continue;
                }

                if (!ShouldFilterMessage(trimmedLine))
                {
                    if (IsImportantResponse(trimmedLine))
                    {
                        // Enhanced error:3 logging during connection setup
                        if ((trimmedLine.Contains("error:3") || trimmedLine.Contains("error: 3")) && !string.IsNullOrEmpty(_lastSentSetupCommand))
                        {
                            LogMessageAdded?.Invoke($"> GRBL: {trimmedLine} (Unsupported command) - Last sent: '{_lastSentSetupCommand}'");
                        }
                        else
                        {
                            LogMessageAdded?.Invoke($"> GRBL: {trimmedLine}");
                        }
                    }
                    else if (IsOtherImportantResponse(trimmedLine))
                    {
                        LogMessageAdded?.Invoke($"> {trimmedLine}");
                    }
                }
            }

            ResponseReceived?.Invoke(response);
        }

        /// <summary>
        /// Forward ok/error/alarm/PRB responses to the ResponseTracker for event-driven probe handling
        /// </summary>
        private void ProcessResponseForTracker(string trimmedLine)
        {
            try
            {
                // Handle "ok" response
                if (trimmedLine.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[ResponseTracker] OK received, forwarding to tracker");
                    _responseTracker.OnOkReceived();
                    return;
                }

                // Handle "error:N" response
                if (trimmedLine.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    int errorCode = 0;
                    var errorMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"error:\s*(\d+)");
                    if (errorMatch.Success)
                    {
                        int.TryParse(errorMatch.Groups[1].Value, out errorCode);
                    }
                    var errorMsg = GetGrblErrorMessage(errorCode);
                    System.Diagnostics.Debug.WriteLine($"[ResponseTracker] ERROR received: {errorCode} - {errorMsg}");
                    _responseTracker.OnErrorReceived(errorCode, errorMsg);
                    return;
                }

                // Handle "ALARM:N" response
                if (trimmedLine.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
                {
                    int alarmCode = 0;
                    var alarmMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"ALARM:\s*(\d+)");
                    if (alarmMatch.Success)
                    {
                        int.TryParse(alarmMatch.Groups[1].Value, out alarmCode);
                    }
                    var alarmMsg = GetGrblAlarmMessage(alarmCode);
                    System.Diagnostics.Debug.WriteLine($"[ResponseTracker] ALARM received: {alarmCode} - {alarmMsg}");
                    _responseTracker.OnAlarmReceived(alarmCode, alarmMsg);
                    return;
                }

                // Handle "[PRB:x,y,z:1]" probe result
                if (trimmedLine.StartsWith("[PRB:", StringComparison.OrdinalIgnoreCase))
                {
                    if (GCodeResponseTracker.TryParsePrbResponse(trimmedLine, out double x, out double y, out double z, out bool success))
                    {
                        _responseTracker.OnProbeResultReceived(x, y, z, success);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResponseTracker] Processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get GRBL error message from code
        /// </summary>
        private static string GetGrblErrorMessage(int code)
        {
            switch (code)
            {
                case 1: return "G-code words consist of a letter and a value. Letter was not found.";
                case 2: return "Numeric value format is not valid or missing an expected value.";
                case 3: return "Grbl '$' system command was not recognized or supported.";
                case 4: return "Negative value received for an expected positive value.";
                case 5: return "Homing cycle is not enabled via settings.";
                case 6: return "Minimum step pulse time must be greater than 3usec.";
                case 7: return "EEPROM read failed. Reset and restored to default values.";
                case 8: return "Grbl '$' command cannot be used unless Grbl is IDLE.";
                case 9: return "G-code locked out during alarm or jog state.";
                case 10: return "Soft limits cannot be enabled without homing also enabled.";
                case 11: return "Max characters per line exceeded.";
                case 12: return "Grbl '$' setting value exceeds the maximum step rate supported.";
                case 13: return "Safety door detected as opened and door state initiated.";
                case 14: return "Build info or startup line exceeds EEPROM line length limit.";
                case 15: return "Jog target exceeds machine travel. Command ignored.";
                case 16: return "Jog command with no '=' or contains prohibited g-code.";
                case 17: return "Laser mode requires PWM output.";
                case 20: return "Unsupported or invalid g-code command found.";
                case 21: return "More than one g-code command from same modal group found.";
                case 22: return "Feed rate has not yet been set or is undefined.";
                case 23: return "G-code command in block requires an integer value.";
                case 24: return "Two G-code commands that both require the use of the XYZ axis words were detected.";
                case 25: return "A G-code word was repeated in the block.";
                case 26: return "A G-code command implicitly or explicitly requires XYZ axis words in the block, but none were detected.";
                case 27: return "N line number value is not within the valid range of 1 - 9,999,999.";
                case 28: return "A G-code command was sent, but is missing some required P or L value.";
                case 29: return "Grbl supports six work coordinate systems G54-G59.";
                case 30: return "The G53 G-code command requires either a G0 seek or G1 feed motion mode to be active.";
                case 31: return "There are unused axis words in the block and G80 motion mode cancel is active.";
                case 32: return "A G2 or G3 arc was commanded but there are no XYZ axis words to trace the arc.";
                case 33: return "The motion command has an invalid target.";
                case 34: return "Arc radius value is invalid.";
                case 35: return "A G2 or G3 arc, traced along the helical path, exceeds the configured axis travel.";
                case 36: return "A G2 or G3 arc has an offset that requires a non-zero offset in the plane.";
                case 37: return "An arc could not be computed.";
                case 38: return "The selected plane for the arc is missing one of the required axis words.";
                default: return $"Unknown error code {code}";
            }
        }

        /// <summary>
        /// Get GRBL alarm message from code
        /// </summary>
        private static string GetGrblAlarmMessage(int code)
        {
            switch (code)
            {
                case 1: return "Hard limit triggered. Machine position is likely lost due to sudden and immediate halt.";
                case 2: return "G-code motion target exceeds machine travel. Machine position safely retained.";
                case 3: return "Reset while in motion. Grbl cannot guarantee position. Lost steps are likely.";
                case 4: return "Probe fail. Probe did not contact the workpiece.";
                case 5: return "Probe fail. Probe did not clear during pulloff motion.";
                case 6: return "Homing fail. Reset during active homing cycle.";
                case 7: return "Homing fail. Safety door was opened during active homing cycle.";
                case 8: return "Homing fail. Cycle failed to clear limit switch when pulling off.";
                case 9: return "Homing fail. Could not find limit switch within search distance.";
                case 10: return "EStop triggered.";
                case 11: return "Homing required.";
                case 12: return "Spindle control error.";
                case 13: return "Control pins error.";
                case 14: return "Ambiguous switch closure.";
                case 15: return "Motor fault.";
                default: return $"Unknown alarm code {code}";
            }
        }

        private void AddOrUpdateSetting(GCodeSetting setting, bool isGrblFallback = false)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_settingsLock)
                {
                    try
                    {
                        bool existsInDict = _settingsById.ContainsKey(setting.Id);
                        var existingInCollection = Settings.FirstOrDefault(s => s.Id == setting.Id);

                        if (isGrblFallback)
                        {
                            if (!existsInDict && existingInCollection == null)
                            {
                                _settingsById[setting.Id] = setting;
                                InsertSettingSorted(setting);
                                LogImportantMessage($"> ? GRBL SETTING ADDED: ${setting.Id} = {setting.Value}");

                                SettingsCountChanged?.Invoke(Settings.Count);
                            }
                            else
                            {
                                LogImportantMessage($"> ?? GRBL SETTING SKIPPED (exists): ${setting.Id}");
                            }
                        }
                        else
                        {
                            if (existingInCollection != null)
                            {
                                Settings.Remove(existingInCollection);
                                LogImportantMessage($"> OVERRIDE: ${setting.Id} FluidNC config replaces GRBL setting");
                            }

                            _settingsById[setting.Id] = setting;
                            InsertSettingSorted(setting);
                            LogImportantMessage($"> ? FLUIDNC SETTING ADDED: ${setting.Id} = {setting.Value}");

                            SettingsCountChanged?.Invoke(Settings.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogImportantMessage($"> HATA: Setting ekleme - {ex.Message}");
                    }
                }
            }), DispatcherPriority.Background);
        }

        private void SortSettingsByID()
        {
            try
            {
                lock (_settingsLock)
                {
                    var sortedSettings = Settings.OrderBy(s => s.Id).ToList();
                    Settings.Clear();
                    foreach (var setting in sortedSettings)
                    {
                        Settings.Add(setting);
                    }
                    LogImportantMessage($"> ? Settings sorted by ID: {Settings.Count} items");
                }
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> HATA: SortSettingsByID - {ex.Message}");
            }
        }

        private void InsertSettingSorted(GCodeSetting newSetting)
        {
            try
            {
                int insertIndex = 0;
                for ( int i = 0; i < Settings.Count; i++)
                {
                    if (Settings[i].Id > newSetting.Id)
                    {
                        insertIndex = i;
                        break;
                    }
                    insertIndex = i + 1;
                }
                Settings.Insert(insertIndex, newSetting);
            }
            catch (Exception ex)
            {
                Settings.Add(newSetting);
                LogImportantMessage($"> UYARI: InsertSettingSorted fallback - {ex.Message}");
            }
        }

        private GCodeSetting MapAxisConfigToGRBL(string line)
        {
            try
            {
                if (line.Contains("steps_per_mm:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"steps_per_mm:\s*([0-9.]+)");
                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;

                        if (_isInXAxisSection)
                            return new GCodeSetting { Id = 100, Value = value, Description = "$100", KnownMeaning = "X ekseni step/mm" };
                        if (_isInYAxisSection)
                            return new GCodeSetting { Id = 101, Value = value, Description = "$101", KnownMeaning = "Y ekseni step/mm" };
                        if (_isInZAxisSection)
                            return new GCodeSetting { Id = 102, Value = value, Description = "$102", KnownMeaning = "Z ekseni step/mm" };
                        if (_isInAAxisSection)
                            return new GCodeSetting { Id = 103, Value = value, Description = "$103", KnownMeaning = "A ekseni step/mm" };
                    }
                }

                if (line.Contains("max_rate_mm_per_min:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"max_rate_mm_per_min:\s*([0-9.]+)");
                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;

                        if (_isInXAxisSection)
                            return new GCodeSetting { Id = 110, Value = value, Description = "$110", KnownMeaning = "X ekseni maksimum hızı (mm/min)" };
                        if (_isInYAxisSection)
                            return new GCodeSetting { Id = 111, Value = value, Description = "$111", KnownMeaning = "Y ekseni maksimum hızı (mm/min)" };
                        if (_isInZAxisSection)
                            return new GCodeSetting { Id = 112, Value = value, Description = "$112", KnownMeaning = "Z ekseni maksimum hızı (mm/min)" };
                        if (_isInAAxisSection)
                            return new GCodeSetting { Id = 113, Value = value, Description = "$113", KnownMeaning = "A ekseni maksimum hızı (mm/min)" };
                    }
                }

                if (line.Contains("acceleration_mm_per_sec2:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"acceleration_mm_per_sec2:\s*([0-9.]+)");
                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;

                        if (_isInXAxisSection)
                            return new GCodeSetting { Id = 120, Value = value, Description = "$120", KnownMeaning = "X ekseni ivmesi (mm/s²)" };
                        if (_isInYAxisSection)
                            return new GCodeSetting { Id = 121, Value = value, Description = "$121", KnownMeaning = "Y ekseni ivmesi (mm/s²)" };
                        if (_isInZAxisSection)
                            return new GCodeSetting { Id = 122, Value = value, Description = "$122", KnownMeaning = "Z ekseni ivmesi (mm/s²)" };
                        if (_isInAAxisSection)
                            return new GCodeSetting { Id = 123, Value = value, Description = "$123", KnownMeaning = "A ekseni ivmesi (mm/s²)" };
                    }
                }

                if (line.Contains("max_travel_mm:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"max_travel_mm:\s*([0-9.]+)");
                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;

                        if (_isInXAxisSection)
                            return new GCodeSetting { Id = 130, Value = value, Description = "$130", KnownMeaning = "X ekseni maksimum travel (mm)" };
                        if (_isInYAxisSection)
                            return new GCodeSetting { Id = 131, Value = value, Description = "$131", KnownMeaning = "Y ekseni maksimum travel (mm)" };
                        if (_isInZAxisSection)
                            return new GCodeSetting { Id = 132, Value = value, Description = "$132", KnownMeaning = "Z ekseni maksimum travel (mm)" };
                        if (_isInAAxisSection)
                            return new GCodeSetting { Id = 133, Value = value, Description = "$133", KnownMeaning = "A ekseni maksimum travel (mm)" };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> Axis mapping error: {ex.Message}");
                return null;
            }
        }

        private GCodeSetting MapSpindleConfigToGRBL(string line)
        {
            try
            {
                if (line.Contains("speed_map:") && line.Contains("18000=100"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)=100");
                    if (match.Success)
                    {
                        string maxSpeed = match.Groups[1].Value;
                        return new GCodeSetting
                        {
                            Id = 30,
                            Value = maxSpeed,
                            Description = "$30",
                            KnownMeaning = "Spindle maksimum hızı (RPM)"
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogImportantMessage($"> Spindle mapping error: {ex.Message}");
                return null;
            }
        }

        private bool ShouldFilterMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;

            // Always suppress plain 'ok' acknowledgments even in debug mode
            if (string.Equals(message.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
                return true;

            // In debug mode, allow most messages through except the high-noise items handled above
            if (_config.EnableDebugLogging) return false;

            var alwaysFiltered = new[]
            {
                "Response line:",
                "RAW Response",
                "DEBUG:",
                "Bf:",
                "FS:",
                "Ov:",
                "A:",
                "Pn:",
                "WCO:",
                "ok"
            };

            foreach (var filtered in alwaysFiltered)
            {
                if (message.Contains(filtered))
                    return true;
            }

            if (message.StartsWith("<") && message.EndsWith(">") &&
                (message.Contains("MPos:") || message.Contains("WPos:")))
                return true;

            if (_filteredMessages.Contains(message))
                return true;

            if (DateTime.Now.Subtract(_lastLogCleanup).TotalMinutes > 5)
            {
                _filteredMessages.Clear();
                _lastLogCleanup = DateTime.Now;
            }

            _filteredMessages.Add(message);
            return false;
        }

        private bool IsFilteredCommand(string command)
        {
            // Always hide status ping ('?') from logs, even in debug mode
            if (string.Equals(command?.Trim(), "?", StringComparison.OrdinalIgnoreCase))
                return true;

            if (_config.EnableDebugLogging) return false;

            var filteredCommands = new[]
            {
                "$$", "$CD", "?", "!", "~", "\x18"
            };

            return filteredCommands.Any(cmd => command.Equals(cmd, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsImportantResponse(string line)
        {
            string lower = line.ToLower();
            return lower.Contains("error") ||
                   lower.Contains("alarm") ||
                   (lower.Contains("grbl") && !lower.Contains("pos:")) ||
                   lower.Contains("unlock") ||
                   lower.Contains("home") ||
                   lower.Contains("probe");
        }

        private bool IsOtherImportantResponse(string line)
        {
            if (line.StartsWith("$"))
                return false;

            string lower = line.ToLower();

            return lower.Contains("ready") ||
                   lower.Contains("reset") ||
                   lower.Contains("version") ||
                   lower.Contains("welcome") ||
                   (lower.Equals("ok") && DateTime.Now.Subtract(_lastStatusQueryTime).TotalSeconds > 10);
        }

        private bool IsImportantControlCharacter(char controlChar)
        {
            return controlChar == '\x18' ||
                   controlChar == '!' ||
                   controlChar == '~' ||
                   controlChar == '\x85';
        }

        private void LogImportantMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

// ✅ SILENTLY UPDATE FIRMWARE INFO - Bu loglar MemoryLogManager'a gitmeden direkt işlenir
        
          // Forward ALL messages to LogMessageAdded event
    // MemoryLogManager will decide what to display
      LogMessageAdded?.Invoke(message);
        }

        private string ExtractPortName(string detailedName)
        {
            if (string.IsNullOrEmpty(detailedName)) return "";
            int startIndex = detailedName.LastIndexOf("(COM");
            if (startIndex == -1)
            {
                // maybe already COMx
                if (detailedName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    return detailedName;
                return "";
            }
            int endIndex = detailedName.LastIndexOf(")");
            if (endIndex > startIndex)
                return detailedName.Substring(startIndex + 1, endIndex - startIndex - 1);
            return detailedName;
        }

        private void ResetAllAxisSections()
        {
            _isInXAxisSection = false;
            _isInYAxisSection = false;
            _isInZAxisSection = false;
            _isInAAxisSection = false;
        }

        private bool IsAnyAxisSectionActive()
        {
            return _isInXAxisSection || _isInYAxisSection || _isInZAxisSection || _isInAAxisSection;
        }

        private string GetCurrentAxisName()
        {
            if (_isInXAxisSection) return "X EKSENİ";
            if (_isInYAxisSection) return "Y EKSENİ";
            if (_isInZAxisSection) return "Z EKSENİ";
            if (_isInAAxisSection) return "A EKSENİ";
            return "UNKNOWN AXIS";
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Buffer Management

        /// <summary>
        /// Clears both input and output serial port buffers.
        /// Used during stop sequence to discard any pending data from GRBL/FluidNC.
        /// </summary>
        public void ClearSerialBuffers()
        {
            try
            {
                _portManager?.ClearSerialBuffers();
                System.Diagnostics.Debug.WriteLine("ConnectionManager: Serial buffers cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConnectionManager.ClearSerialBuffers error: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Disconnect();
            try
            {
                _portManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _gcodeSender.ResponseReceived -= OnResponseReceived;
                _filteredMessages?.Clear();
                _settingsById?.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
    /// G-Code Sender instance - BUFFER MANAGEMENT için gerekli
        /// </summary>
        // --- Connection details helpers ---
        private void LogConnectionDetails()
        {
            var details = _portManager.GetConnectionDetails();
            var info = SelectedPortInfo;

            string transport = ClassifyTransport(info);
            var ids = ExtractVidPid(info?.PnpDeviceId);
            string vidPid = (ids.vid != null && ids.pid != null) ? $" (VID:PID={ids.vid}:{ids.pid})" : string.Empty;

            LogImportantMessage("> ===============================================");
            LogImportantMessage("> 🔌 BAĞLANTI BİLGİLERİ");
            LogImportantMessage("> ===============================================");
            LogImportantMessage($">   • Port: {SelectedPort}");
            if (!string.IsNullOrWhiteSpace(info?.Description))
                LogImportantMessage($">   • Description: {info.Description}");
            if (!string.IsNullOrWhiteSpace(info?.Manufacturer))
                LogImportantMessage($">   • Manufacturer: {info.Manufacturer}");
            if (!string.IsNullOrWhiteSpace(info?.PnpDeviceId))
                LogImportantMessage($">   • PNP ID: {info.PnpDeviceId}");
            LogImportantMessage($">   • Transport: {transport}{vidPid}");

            LogImportantMessage($">   • Baud: {details.BaudRate}");
            LogImportantMessage($">   • Data/Parity/Stop: {details.DataBits}/{details.Parity}/{details.StopBits}");
            LogImportantMessage($">   • Handshake: {details.Handshake}, DTR:{details.DtrEnable}, RTS:{details.RtsEnable}");
            LogImportantMessage($">   • Encoding: {details.EncodingName}");
            LogImportantMessage($">   • Timeouts (R/W): {details.ReadTimeout}/{details.WriteTimeout} ms");
            LogImportantMessage($">   • Buffers (R/W): {details.ReadBufferSize}/{details.WriteBufferSize} bytes");
            LogImportantMessage("> ===============================================");
        }

        private string ClassifyTransport(SerialPortManager.PortInfo info)
        {
            if (info == null) return "Unknown";
            string pnp = info.PnpDeviceId?.ToUpperInvariant() ?? string.Empty;
            if (pnp.Contains("BTHENUM") || pnp.Contains("BTHLE") || pnp.Contains("BLUETOOTH"))
                return "Bluetooth (Serial Profile)";
            if (pnp.Contains("USB") || pnp.Contains("VID_") || pnp.Contains("FTDIBUS"))
                return "USB-Serial";
            if (pnp.StartsWith("PCI\\") || pnp.Contains("ACPI"))
                return "Onboard/PCI UART";
            return "Unknown";
        }

        private (string vid, string pid) ExtractVidPid(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId)) return (null, null);
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(pnpDeviceId, @"VID[_:]([0-9A-F]{4}).*PID[_:]([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    return (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant());
                }
            }
            catch { }
            return (null, null);
        }

        public GCodeSender GcodeSender => _gcodeSender;
    }
}
