using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CncControlApp.Controls;
using CncControlApp.Managers;
using CncControlApp.Services;

namespace CncControlApp
{
    public class MainControll : INotifyPropertyChanged, IDisposable
    {
        // Services/Managers
        private readonly StepControlService _stepControlService;
        private readonly ErrorHandlingService _errorHandlingService;
        private readonly CoordinateService _coordinateService;
        private readonly MachineControlService _machineControlService;
        private readonly ConnectionManager _connectionManager;
        private readonly JoggingController _joggingController;
        private readonly DataProcessingManager _dataProcessingManager;
        private readonly UIPropertiesManager _uiManager;
        private readonly GCodeExecutionManager _gCodeManager;
        private readonly MemoryLogManager _memoryLogManager;

        // UI/Timers
        private readonly DispatcherTimer _statusAndProcessTimer;
        private int _timerTickCount =0;
        private LoadingPopup _loadingPopup;

        // Homing
        private bool _hasHomed;
        private bool _homingPopupShown;
        private bool _pendingHomingPopup;
        private Window _homingPopupWindow;
        public bool HasHomed => _hasHomed;

        // Stop/Hold decision
        private bool _stopHoldPopupShown;
        private bool _isWaitingForStopDecision;
        private bool _pendingStopSequence;
        private bool _stopSequenceRunning;
        private DateTime _postStopGraceUntil;
        public bool IsInPostStopGracePeriod => DateTime.UtcNow < _postStopGraceUntil;
        public bool DisableAutoStopPopup { get; set; } = false;
        private MessageDialog _stopProgressDialog;

        // State
        private bool _suppressNonEssentialLogs = true;
        private string _machineStatus = string.Empty;
        private bool isGCodeRunning = false;
        private bool _alarmRecoveryInProgress;

        // Central status querier
        private CentralStatusQuerier _centralStatusQuerier;
        private IDisposable _centralStatusSubscription;
        private bool _centralStatusQuerierEnabled = false;

        // Events
        public event Action<MenuPage> NavigateToPanel;
        public event Action StopSequenceCompleted;
        public event EventHandler BufferStatusChanged;
        public event EventHandler<(string Command, string MachineStatus)> CommandBlockedDueToHold;
        public event Action<string> RawResponseReceived; // Console için ham yanıt event'i

        // Delegated properties/commands
        public ObservableCollection<string> AvailablePorts => _connectionManager.AvailablePorts;
        public ObservableCollection<GCodeSetting> Settings => _connectionManager.Settings;
        public string SelectedPort { get => _connectionManager.SelectedPort; set => _connectionManager.SelectedPort = value; }
        public bool IsConnected => _connectionManager.IsConnected;
        public bool IsDisconnected => _connectionManager.IsDisconnected;
        public string ConnectButtonText => _connectionManager.ConnectButtonText;
        public ICommand RefreshPortsCommand => _connectionManager.RefreshPortsCommand;
        public ICommand ConnectCommand => _connectionManager.ConnectCommand;

        // JoggingController
        public bool IsStepMode { get => _joggingController.IsStepMode; set => _joggingController.IsStepMode = value; }
        public double SelectedStepSize { get => _joggingController.SelectedStepSize; set => _joggingController.SelectedStepSize = value; }
        public string SelectedStepSizeDisplay => _joggingController.SelectedStepSizeDisplay;
        public string ModeDisplayText => _joggingController.ModeDisplayText;
        public double JogSpeedPercentage { get => _joggingController.JogSpeedPercentage; set => _joggingController.JogSpeedPercentage = value; }
        public int MaxXYJogSpeed => _joggingController.MaxXYJogSpeed;
        public int MaxZJogSpeed => _joggingController.MaxZJogSpeed;
        public int MaxAJogSpeed => _joggingController.MaxAJogSpeed;
        public int CurrentXYJogSpeed => _joggingController.CurrentXYJogSpeed;
        public int CurrentZJogSpeed => _joggingController.CurrentZJogSpeed;
        public int CurrentAJogSpeed => _joggingController.CurrentAJogSpeed;
        public string JogSpeedDisplayText => _joggingController.JogSpeedDisplayText;

        // StepControlService
        public bool IsXYZStepMode { get => _stepControlService.IsXYZStepMode; set => _stepControlService.IsXYZStepMode = value; }
        public double SelectedXYZStepSize => _stepControlService.SelectedXYZStepSize;
        public string SelectedXYZStepSizeDisplay => _stepControlService.SelectedXYZStepSizeDisplay;
        public string XYZModeDisplayText => _stepControlService.XYZModeDisplayText;
        public bool IsAStepMode { get => _stepControlService.IsAStepMode; set => _stepControlService.IsAStepMode = value; }
        public double SelectedAStepSize => _stepControlService.SelectedAStepSize;
        public string SelectedAStepSizeDisplay => _stepControlService.SelectedAStepSizeDisplay;
        public string AModeDisplayText => _stepControlService.AModeDisplayText;
        public double AAxisSpeedPercentage { get => _stepControlService.AAxisSpeedPercentage; set { if (Math.Abs(_stepControlService.AAxisSpeedPercentage - value) >0.01) { _stepControlService.AAxisSpeedPercentage = value; AddLogMessage($"> AAxisSpeedPercentage changed to: {value}%"); OnPropertyChanged(); } } }
        public int CurrentAAxisJogSpeed => _stepControlService.GetCurrentAAxisJogSpeed(MaxAJogSpeed);
        public string AAxisSpeedDisplayText => _stepControlService.GetAAxisSpeedDisplayText(MaxAJogSpeed);

        // CoordinateService
        public string TargetX { get => _coordinateService.TargetX; set { _coordinateService.TargetX = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGoXButtonEnabled)); } }
        public string TargetY { get => _coordinateService.TargetY; set { _coordinateService.TargetY = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGoYButtonEnabled)); } }
        public string TargetZ { get => _coordinateService.TargetZ; set { _coordinateService.TargetZ = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGoZButtonEnabled)); } }
        public string TargetA { get => _coordinateService.TargetA; set { _coordinateService.TargetA = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGoAButtonEnabled)); } }
        public bool IsGoXButtonEnabled => IsConnected && _coordinateService.IsTargetDifferentFromCurrent(TargetX, MStatus.WorkX);
        public bool IsGoYButtonEnabled => IsConnected && _coordinateService.IsTargetDifferentFromCurrent(TargetY, MStatus.WorkY);
        public bool IsGoZButtonEnabled => IsConnected && _coordinateService.IsTargetDifferentFromCurrent(TargetZ, MStatus.WorkZ);
        public bool IsGoAButtonEnabled => IsConnected && _coordinateService.IsTargetDifferentFromCurrent(TargetA, MStatus.WorkA);

        // MachineControlService (get only)
        public bool IsSpindleOn { get => _machineControlService.IsSpindleOn; set { } }
        public double SpindleSpeed { get => _machineControlService.SpindleSpeed; set => _machineControlService.SpindleSpeed = value; }
        public double MaxSpindleSpeed { get => _machineControlService.MaxSpindleSpeed; set => _machineControlService.MaxSpindleSpeed = value; }
        public string SpindleSpeedDisplay => _machineControlService.SpindleSpeedDisplay;
        public double SpindleSpeedPercentage { get => _machineControlService.SpindleSpeedPercentage; set => _machineControlService.SpindleSpeedPercentage = value; }
        public bool IsCoolantOn { get => _machineControlService.IsCoolantOn; set { } }
        public bool IsMistOn { get => _machineControlService.IsMistOn; set { } }
        public bool IsLightsOn { get => _machineControlService.IsLightsOn; set { } }
        public bool IsToolChangeOn { get => _machineControlService.IsToolChangeOn; set { } }
        public bool IsProbeOn { get => _machineControlService.IsProbeOn; set { } }
        public bool IsVacuumOn { get => _machineControlService.IsVacuumOn; set { } }
        public bool IsAirBlastOn { get => _machineControlService.IsAirBlastOn; set { } }

        // Errors delegations
        public List<string> GetErrorHistory() => _errorHandlingService.GetErrorHistory();
        public Dictionary<string, int> GetErrorStatistics() => _errorHandlingService.GetErrorStatistics();
        public Dictionary<string, int> GetGrblErrorStatistics() => _errorHandlingService.GetGrblErrorStatistics();
        public string GetErrorReport() => _errorHandlingService.GetErrorReport();
        public void ClearErrorHistory() => _errorHandlingService.ClearErrorHistory();

        // GCode manager delegations
        public bool IsGCodeRunning => _gCodeManager.IsGCodeRunning;
        public int CurrentGCodeLineIndex => _gCodeManager.CurrentGCodeLineIndex;
        public double ExecutionProgress => _gCodeManager.ExecutionProgress;
        public string ExecutionProgressText => _gCodeManager.ExecutionProgressText;
        public bool CanStartExecution => _gCodeManager.CanStartExecution;
        public bool CanPauseExecution => _gCodeManager.CanPauseExecution;
        public bool CanContinueExecution => _gCodeManager.CanContinueExecution;
        public bool CanStopExecution => _gCodeManager.CanStopExecution;
        public TimeSpan TotalEstimatedTime => _gCodeManager.TotalEstimatedTime;
        public DateTime ExecutionStartTime => _gCodeManager.ExecutionStartTime;
        public string ElapsedTimeText => _gCodeManager.ElapsedTimeText;
        public string RemainingTimeText => _gCodeManager.RemainingTimeText;
        public void CalculateEstimatedExecutionTime(List<GCodeSegment> segs) { _gCodeManager?.CalculateEstimatedExecutionTime(segs); _gCodeManager?.BuildTimeMapFromSegments(segs); }
        public GCodeExecutionManager GCodeManager => _gCodeManager;

        // Memory manager delegates
        public int LogCount => _memoryLogManager.LogCount;
        public long EstimatedMemoryUsage => _memoryLogManager.EstimatedMemoryUsage;
        public double EstimatedMemoryUsageKB => _memoryLogManager.EstimatedMemoryUsageKB;
        public double EstimatedMemoryUsageMB => _memoryLogManager.EstimatedMemoryUsageMB;
        public double SystemMemoryUsageMB => _memoryLogManager.SystemMemoryUsageMB;
        public bool IsMemoryUsageCritical => _memoryLogManager.IsMemoryUsageCritical;
        public bool IsMemoryUsageHigh => _memoryLogManager.IsMemoryUsageHigh;
        public int ClearOldLogs(int? items = null) => _memoryLogManager.ClearOldLogs(items);
        public void ClearAllLogs() => _memoryLogManager.ClearAllLogs();
        public void CheckMemoryUsage() => _memoryLogManager.CheckMemoryUsage();
        public long ForceMemoryCleanup() => _memoryLogManager.ForceMemoryCleanup();
        public string GetMemoryStatusReport() => _memoryLogManager.GetMemoryStatusReport();

        // UI collections
        public ObservableCollection<string> LogMessages => _uiManager.LogMessages;
        public ObservableCollection<string> GCodeLines => _uiManager.GCodeLines;
        public string LoadedGCodeFile { get => _uiManager.LoadedGCodeFile; set => _uiManager.LoadedGCodeFile = value; }
        public bool IsGCodeLoaded => _uiManager.IsGCodeLoaded;
        public MachineStatus MStatus => _uiManager.MStatus;
        public string MachineStatus
        {
            get => _machineStatus;
            set
            {
                if (_machineStatus != value)
                {
                    var prev = _machineStatus;
                    _machineStatus = value;
                    OnPropertyChanged();
                    bool newRunning = string.Equals(value, "Run", StringComparison.OrdinalIgnoreCase);
                    if (isGCodeRunning != newRunning)
                    {
                        isGCodeRunning = newRunning;
                        OnPropertyChanged(nameof(IsGCodeRunning));
                    }
                    HandleHoldStateTransition(prev, _machineStatus);
                    OnPropertyChanged(nameof(SystemStateIndicator));
                }
            }
        }
        public string SystemStateIndicator
        {
            get
            {
                if (!IsConnected) return "Disconnected";
                var s = (MachineStatus ?? string.Empty).ToLowerInvariant();
                if (s.Contains("error")) return "Error";
                if (s.Contains("alarm") || s.Contains("hold") || s.Contains("door")) return "Alarm";
                return "Normal";
            }
        }

        // ✅ PROBE WINDOW STATE PROPERTY - YENİ EKLENDİ
        private bool _isProbeWindowOpen = false;

        /// <summary>
        /// Probe Window'un açık olup olmadığını belirtir
        /// </summary>
        public bool IsProbeWindowOpen
        {
            get => _isProbeWindowOpen;
            set
            {
                if (_isProbeWindowOpen != value)
                {
                    _isProbeWindowOpen = value;
                    OnPropertyChanged();
                    AddLogMessage($"> 🔧 Probe window state: {(value ? "OPENED" : "CLOSED")}");
                }
            }
        }

        // NEW: If true, probe routines will not send their own '?' status queries
        // This prevents duplicate querying when a central status querier is active.
        private bool _suppressProbeSelfQueries = false;

        /// <summary>
        /// When true, probe routines (FindAxisMid, EventDriven probes, ReadContact*) will avoid sending their own "?" queries
        /// and rely on existing status updates / PRB events. Default: false.
        /// </summary>
        public bool SuppressProbeSelfQueries
        {
            get => _suppressProbeSelfQueries;
            set
            {
                if (_suppressProbeSelfQueries != value)
                {
                    _suppressProbeSelfQueries = value;
                    OnPropertyChanged();
                    AddLogMessage($"> 🔒 SuppressProbeSelfQueries set to: {_suppressProbeSelfQueries}");
                }
            }
        }

        // Controls whether the timer and other components should avoid emitting status queries.
        // When true, no periodic status queries should be sent by MainControll timer or by any other
        // component that respects this flag (probes, legacy pollers). Default: false.
        private bool _statusQueriesBlocked = false;
        public bool StatusQueriesBlocked
        {
            get => _statusQueriesBlocked;
            set
            {
                if (_statusQueriesBlocked != value)
                {
                    _statusQueriesBlocked = value;
                    OnPropertyChanged();
                    AddLogMessage($"> 🔒 StatusQueriesBlocked set to: {_statusQueriesBlocked}");
                }
            }
        }

        // Diagnostic dump helper
        /// <summary>
        /// Build diagnostic timing report and save to a timestamped file. Returns path or null on failure.
        /// </summary>
        public string DumpDiagnosticReportToFile()
        {
            try
            {
                var path = DiagnosticTimingService.SaveReportToFile();
                if (!string.IsNullOrEmpty(path))
                {
                    AddLogMessage($"> ✅ Diagnostic report saved: {path}");
                    return path;
                }
                AddLogMessage("> ❌ Diagnostic report could not be saved");
                return null;
            }
            catch (Exception ex)
            {
                AddLogMessage($"> ❌ DumpDiagnosticReportToFile error: {ex.Message}");
                return null;
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Ctor
        public MainControll()
        {
            _uiManager = new UIPropertiesManager();
            _memoryLogManager = new MemoryLogManager(_uiManager.LogMessages, _ => { });
            _memoryLogManager.MemoryUsageCritical += OnMemoryUsageCritical;
            _memoryLogManager.MemoryCleanupCompleted += OnMemoryCleanupCompleted;
            _memoryLogManager.PropertyChanged += OnMemoryLogManagerPropertyChanged;

            _dataProcessingManager = new DataProcessingManager(
                m => AddLogMessage(m),
                (st, log) => { MachineStatus = st; if (!string.IsNullOrEmpty(log)) AddLogMessage(log); },
                coords => { MStatus.X = coords.X; MStatus.Y = coords.Y; MStatus.Z = coords.Z; MStatus.A = coords.A; MStatus.WorkX = coords.WorkX; MStatus.WorkY = coords.WorkY; MStatus.WorkZ = coords.WorkZ; MStatus.WorkA = coords.WorkA; });

            _dataProcessingManager.AlarmDetected += (code, raw) =>
            {
                try
                {
                    var lower = raw.ToLowerInvariant();
                    bool unhomed = lower.Contains("unhomed");
                    bool lockAlarm = code ==9 || code ==14 || lower.Contains("lock");
                    var homingSetting = Settings?.FirstOrDefault(s => s.Id ==22);
                    bool homingEnabled = homingSetting == null || homingSetting.Value == "1";
                    if (!_homingPopupShown && !_hasHomed && homingEnabled && (unhomed || lockAlarm))
                    {
                        if (_loadingPopup != null)
                        {
                            _pendingHomingPopup = true;
                            AddLogMessage("> ⏳ Homing popup bekletiliyor (loading kapanınca gösterilecek)");
                        }
                        else
                        {
                            ShowHomingRequiredPopup();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage($"> Homing alarm handle error: {ex.Message}");
                }
            };

            _errorHandlingService = new ErrorHandlingService(m => _uiManager.LogMessages.Add(m));
            var config = new ConnectionConfiguration { EnableAutoConnect = true, EnableSoundNotifications = true, SupportedDevices = new[] { "CP210x", "CH340" }, BaudRate =115200, ConnectionTimeout =800 };
            _connectionManager = new ConnectionManager(config);

            _gCodeManager = new GCodeExecutionManager(
                _connectionManager,
                _uiManager,
                (op, ex, sev) => _errorHandlingService.LogError(op, ex, sev),
                msg => AddLogMessage(msg),
                () => MachineStatus,
                s => MachineStatus = s);

            _gCodeManager.PropertyChanged += OnGCodeManagerPropertyChanged;
            _gCodeManager.ExecutionStatusChanged += OnGCodeExecutionStatusChanged;
            _gCodeManager.FileLoaded += OnGCodeFileLoaded;
            _gCodeManager.ExecutionCompleted += OnGCodeExecutionCompleted;

            _stepControlService = new StepControlService((op, ex) => _errorHandlingService.LogError("StepService", ex, ErrorHandlingService.ErrorSeverity.Error), m => AddLogMessage(m));
            _coordinateService = new CoordinateService(_connectionManager, (op, ex) => _errorHandlingService.LogError(op, ex, ErrorHandlingService.ErrorSeverity.Error), m => AddLogMessage(m));
            _machineControlService = new MachineControlService(_connectionManager, (op, ex) => _errorHandlingService.LogError(op, ex, ErrorHandlingService.ErrorSeverity.Error), m => AddLogMessage(m));
            _joggingController = new JoggingController(_connectionManager);

            _stepControlService.PropertyChanged += OnStepControlServicePropertyChanged;
            _machineControlService.PropertyChanged += OnMachineControlServicePropertyChanged;
            _joggingController.PropertyChanged += OnJoggingControllerPropertyChanged;
            _uiManager.PropertyChanged += OnUIManagerPropertyChanged;
            _uiManager.BufferStatusChanged += OnBufferStatusChanged;

            _connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            _connectionManager.ResponseReceived += OnResponseReceived;
            _connectionManager.SettingsReceived += OnSettingsReceived;
            _connectionManager.LogMessageAdded += OnLogMessageAdded;
            _connectionManager.PortsChanged += OnPortsChanged;
            _connectionManager.LoadingStarted += OnLoadingStarted;
            _connectionManager.LoadingCompleted += OnLoadingCompleted;
            _connectionManager.LoadingStatusChanged += OnLoadingStatusChanged;
            _connectionManager.SettingsCountChanged += OnSettingsCountChanged;

            _statusAndProcessTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _statusAndProcessTimer.Tick += StatusAndProcessTimer_Tick;

            MStatus.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MStatus.WorkX)) OnPropertyChanged(nameof(IsGoXButtonEnabled));
                if (e.PropertyName == nameof(MStatus.WorkY)) OnPropertyChanged(nameof(IsGoYButtonEnabled));
                if (e.PropertyName == nameof(MStatus.WorkZ)) OnPropertyChanged(nameof(IsGoZButtonEnabled));
                if (e.PropertyName == nameof(MStatus.WorkA)) OnPropertyChanged(nameof(IsGoAButtonEnabled));
            };

            AddLogMessage("> ✅ MainControll başlatıldı - Tüm servisler entegre edildi");
            _gCodeManager.CommandBlockedDueToHold += (s, e) => { AddLogMessage($"> ⏸️ Command blocked (Hold): '{e.Command}' (status: {e.MachineStatus})"); CommandBlockedDueToHold?.Invoke(this, e); };
        }

        // Logging
        public void AddLogMessage(string message, bool essential = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                if (_suppressNonEssentialLogs && !essential && !IsUserImportantMessage(message))
                {
                    Debug.WriteLine($"[LOG suppressed] {message}");
                    return;
                }
                _memoryLogManager?.AddLogMessage(message);
                // Push to progress dialog if open (basic mirroring for critical steps)
                try { _stopProgressDialog?.AppendProgress(message); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddLogMessage error: {ex.Message}");
            }
        }
        public void AddImportantLog(string message) => AddLogMessage(message, true);
        private bool ShouldFilterLogMessage(string m) => string.IsNullOrWhiteSpace(m) || m.Contains("DEBUG:") || m.Contains("Response line:") || m.Contains("RAW Response");
        private bool IsUserImportantMessage(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return false;
            var l = m.ToLowerInvariant();
            if (l.Contains("error") || l.Contains("hata") || l.Contains("alarm") || l.Contains("emergency")) return true;
            if (l.Contains("g-code") && (l.Contains("loaded") || l.Contains("completed") || l.Contains("started"))) return true;
            return false;
        }

        // GCode control wrappers
        public bool LoadGCodeFile(string f) => _gCodeManager.LoadGCodeFile(f);
        public Task<bool> RunGCodeAsync() => _gCodeManager.RunGCodeAsync();
        public Task<bool> PauseGCodeAsync() => _gCodeManager.PauseGCodeAsync();
        public Task<bool> ContinueGCodeAsync() => _gCodeManager.ContinueGCodeAsync();
        public async Task<bool> StopGCodeAsync()
        {
            try
            {
                if (!CanStopExecution)
                {
                    AddLogMessage("> ❌ Stop edilecek G-Code execution yok");
                    return false;
                }
                AddLogMessage("> ⏸️ STOP süreci başlatılıyor...");
                _isWaitingForStopDecision = true;
                _pendingStopSequence = true;
                bool ok = await _gCodeManager.StopGCodeAsync();
                if (!ok)
                {
                    AddLogMessage("> ❌ Stop gönderilemedi - işlem iptal");
                    _isWaitingForStopDecision = false;
                    _pendingStopSequence = false;
                }
                return ok;
            }
            catch (Exception ex)
            {
                AddLogMessage($"> ❌ Stop işlemi hatası: {ex.Message}");
                _isWaitingForStopDecision = false;
                _pendingStopSequence = false;
                return false;
            }
        }
        public Task<bool> SendGCodeCommandAsync(string g) => _gCodeManager.SendGCodeCommandAsync(g);
        /// <summary>
        /// Force-send G-Code command directly via connection manager, bypassing any central query suppression.
        /// Use this from probe routines when they need explicit status queries while global status querying is suppressed.
        /// </summary>
        public Task<bool> ForceSendGCodeCommandAsync(string g)
        {
            try
            {
                return _connection_manager_send_wrapper(g);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        
        // internal helper to call connection manager directly (keeps error handling centralized)
        private async Task<bool> _connection_manager_send_wrapper(string gcode)
        {
            if (string.IsNullOrWhiteSpace(gcode) || !IsConnected)
                return false;
            try
            {
                return await _connection_manager_send_core(gcode);
            }
            catch
            {
                return false;
            }
        }
        
        private Task<bool> _connection_manager_send_core(string gcode)
        {
            return _connectionManager.SendGCodeCommandAsync(gcode);
        }

        public Task<bool> SendGCodeCommandWithConfirmationAsync(string g) => _gCodeManager.SendGCodeCommandWithConfirmationAsync(g);
        public Task<bool> SendControlCharacterAsync(char c) => _gCodeManager.SendControlCharacterAsync(c);
        public void RecalculateRemainingTimeWithFeedOverride(int p) { try { _gCodeManager?.RecalculateRemainingTimeWithFeedOverride(p); } catch { } }

        // Step helpers
        public void SetXYZStepSize(double s) => _stepControlService.SetXYZStepSize(s);
        public void SetAStepSize(double s) => _stepControlService.SetAStepSize(s);
        public void ToggleXYZStepMode() => _stepControlService.ToggleXYZStepMode();
        public void ToggleAStepMode() => _stepControlService.ToggleAStepMode();
        public string GetStepModeInfo() => _stepControlService.GetStepModeInfo();
        public void SetAAxisSpeedPercentage(double p) => _stepControlService.SetAAxisSpeedPercentage(p);

        // Coordinate helpers
        public Task<bool> SetZeroAxisAsync(string axis, bool permanent = false) => _coordinateService.SetZeroAxisAsync(axis, permanent);
        public Task<bool> SetZeroXPermanentAsync() => _coordinateService.SetZeroXPermanentAsync();
        public Task<bool> SetZeroYPermanentAsync() => _coordinateService.SetZeroYPermanentAsync();
        public Task<bool> SetZeroZPermanentAsync() => _coordinateService.SetZeroZPermanentAsync();
        public Task<bool> SetZeroAllPermanentAsync() => _coordinateService.SetZeroAllPermanentAsync();
        public Task<bool> HomeAllAsync() => HomeInternalAsync();
        public Task<bool> HomeXYZAsync() => HomeAllAsync();
        private async Task<bool> HomeInternalAsync()
        {
            if (!IsConnected) { AddLogMessage("> ❌ Not connected - cannot home"); return false; }
            try
            {
                bool hr = false;
                try { hr = await _coordinateService.MachineHomeAllAsync(); }
                catch (MissingMethodException) { try { hr = await _coordinateService.HomeAllAsync(); } catch { hr = false; } }
                catch (Exception ex)
                {
                    _errorHandlingService.LogError("HomeAllAsync", ex, ErrorHandlingService.ErrorSeverity.Warning);
                    try { hr = await _coordinateService.HomeAllAsync(); } catch { hr = false; }
                }
                AddLogMessage(hr ? "> ✅ Homing completed" : "> ❌ Homing failed");
                return hr;
            }
            catch (Exception ex)
            {
                _errorHandlingService.LogError("HomeAllAsync", ex, ErrorHandlingService.ErrorSeverity.Error);
                AddLogMessage($"> ❌ Home error: {ex.Message}");
                return false;
            }
        }

        // Machine control wrappers
        public Task<bool> ToggleSpindleAsync(bool on) => _machineControlService.ToggleSpindleAsync(on);
        public Task<bool> StartSpindleAsync(double s) => _machineControlService.StartSpindleAsync(s);
        public Task<bool> StopSpindleAsync() => _machineControlService.StopSpindleAsync();
        public Task<bool> SetSpindleSpeedAsync(double s) => _machineControlService.SetSpindleSpeedAsync(s);
        public Task<bool> ToggleCoolantAsync(bool on) => _machineControlService.ToggleCoolantAsync(on);
        public Task<bool> ToggleMistAsync(bool on) => _machineControlService.ToggleMistAsync(on);
        public Task<bool> ToggleLightsAsync(bool on) => _machineControlService.ToggleLightsAsync(on);
        public Task<bool> ToggleToolChangeAsync(bool on) => _machineControlService.ToggleToolChangeAsync(on);
        public Task<bool> ToggleVacuumAsync(bool on) => _machineControlService.ToggleVacuumAsync(on);
        public Task<bool> ToggleAirBlastAsync(bool on) => _machineControlService.ToggleAirBlastAsync(on);
        public Task<bool> ToggleProbeAsync(bool on) => _machineControlService.ToggleProbeAsync(on);
        public async Task<bool> EmergencyStopAsync() { _gCodeManager?.CancelExecution(); return await _machineControlService.EmergencyStopAsync(); }
        public Task<bool> TurnOffAllAuxiliaryAsync() => _machineControlService.TurnOffAllAuxiliaryAsync();

        // Jog public wrappers
        public void ToggleJogMode() => _joggingController.ToggleJogMode();
        public void SetStepSize(double s) => _joggingController.SetStepSize(s);
        public Task StartJogAsync(string dir, int? fr = null) => _joggingController.StartJogAsync(dir, fr);
        public Task StopJogAsync() => _joggingController.StopJogAsync();
        public Task StartJogXPlusAsync(int? f = null) => IsXYZStepMode ? SendStep("X", "+", f) : _joggingController.StartJogAsync("X", f);
        public Task StartJogXMinusAsync(int? f = null) => IsXYZStepMode ? SendStep("X", "-", f) : _joggingController.StartJogAsync("X-", f);
        public Task StartJogYPlusAsync(int? f = null) => IsXYZStepMode ? SendStep("Y", "+", f) : _joggingController.StartJogAsync("Y", f);
        public Task StartJogYMinusAsync(int? f = null) => IsXYZStepMode ? SendStep("Y", "-", f) : _joggingController.StartJogAsync("Y-", f);
        public Task StartJogZPlusAsync(int? f = null) => IsXYZStepMode ? SendStep("Z", "+", f) : _joggingController.StartJogAsync("Z", f);
        public Task StartJogZMinusAsync(int? f = null) => IsXYZStepMode ? SendStep("Z", "-", f) : _joggingController.StartJogAsync("Z-", f);
        public Task StartJogAPlusAsync(int? f = null) => IsAStepMode ? SendAStep("+", f) : _joggingController.StartJogAsync("A", f);
        public Task StartJogAMinusAsync(int? f = null) => IsAStepMode ? SendAStep("-", f) : _joggingController.StartJogAsync("A-", f);
        private async Task SendStep(string axis, string sign, int? fr) { int rate = fr ?? (axis == "Z" ? _joggingController.CurrentZJogSpeed : _joggingController.CurrentXYJogSpeed); var cmd = _stepControlService.GenerateXYZStepCommand(axis, sign, rate); if (!string.IsNullOrEmpty(cmd)) await _connectionManager.SendGCodeCommandAsync(cmd); }
        private async Task SendAStep(string sign, int? fr) { int rate = fr ?? CurrentAAxisJogSpeed; var cmd = _stepControlService.GenerateAStepCommand(sign, rate); if (!string.IsNullOrEmpty(cmd)) await _connectionManager.SendGCodeCommandAsync(cmd); }

        // Event handlers
        private void OnStepControlServicePropertyChanged(object s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IsXYZStepMode): _joggingController.SetStepSize(SelectedXYZStepSize); OnPropertyChanged(nameof(IsXYZStepMode)); break;
                case nameof(SelectedXYZStepSize): _joggingController.SetStepSize(SelectedXYZStepSize); OnPropertyChanged(nameof(SelectedXYZStepSize)); OnPropertyChanged(nameof(SelectedXYZStepSizeDisplay)); break;
                case nameof(AAxisSpeedPercentage): OnPropertyChanged(nameof(CurrentAAxisJogSpeed)); OnPropertyChanged(nameof(AAxisSpeedDisplayText)); break;
                case nameof(IsAStepMode):
                case nameof(SelectedAStepSize): OnPropertyChanged(nameof(SelectedAStepSizeDisplay)); break;
                default: OnPropertyChanged(e.PropertyName); break;
            }
        }
        private void OnJoggingControllerPropertyChanged(object s, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(MaxAJogSpeed))
                {
                    OnPropertyChanged(nameof(MaxAJogSpeed));
                    OnPropertyChanged(nameof(CurrentAAxisJogSpeed));
                    OnPropertyChanged(nameof(AAxisSpeedDisplayText));
                    AddLogMessage($"> A-axis MaxJogSpeed updated: {MaxAJogSpeed} deg/min");
                    return;
                }
                OnPropertyChanged(e.PropertyName);
            }
            catch (Exception ex) { AddLogMessage($"> OnJoggingControllerPropertyChanged error: {ex.Message}"); }
        }
        private void OnGCodeManagerPropertyChanged(object s, PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);
        private void OnMachineControlServicePropertyChanged(object s, PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);
        private void OnUIManagerPropertyChanged(object s, PropertyChangedEventArgs e) { OnPropertyChanged(e.PropertyName); if (e.PropertyName == nameof(MachineStatus)) OnPropertyChanged(nameof(SystemStateIndicator)); }
        private void OnBufferStatusChanged(object s, EventArgs e) => BufferStatusChanged?.Invoke(this, EventArgs.Empty);

        private void OnConnectionStatusChanged(bool isConnected)
        {
            _statusAndProcessTimer.IsEnabled = isConnected;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(IsAAxisAvailable));
            UpdateAllGoButtonStates();
            OnPropertyChanged(nameof(SystemStateIndicator));

            if (isConnected)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddLogMessage("> ✅ CNC bağlantısı kuruldu");
                    NavigateToPanel?.Invoke(MenuPage.Status);
                });
                try { StartCentralStatusQuerier(); CentralStatusQuerierEnabled = true; } catch (Exception ex) { AddLogMessage($"> ❌ CentralStatusQuerier start error: {ex.Message}"); }
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try { _loadingPopup?.ClosePopup(); _loadingPopup = null; AddLogMessage("> ❌ CNC bağlantısı kesildi"); }
                    catch (Exception ex) { AddLogMessage($"> UYARI: Loading popup kapatma hatası - {ex.Message}"); }
                });
                try { StopCentralStatusQuerier(); CentralStatusQuerierEnabled = false; } catch (Exception ex) { AddLogMessage($"> ❌ CentralStatusQuerier stop error: {ex.Message}"); }
            }
        }
        private void OnResponseReceived(string r) 
        { 
            _dataProcessingManager.EnqueueReceivedData(r);
            // Console için raw response bildirimi
            try { RawResponseReceived?.Invoke(r); } catch { }
        }
        private void OnSettingsReceived(ObservableCollection<GCodeSetting> s)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var aMax = s.FirstOrDefault(x => x.Id ==113);
                if (aMax != null) AddLogMessage($"> ✅ A-ekseni config'den okunan maksimum hız: {aMax.Value} deg/min");
                else AddLogMessage($"> ⚠️ A-ekseni maksimum hız ayarı config'de bulunamadı ($113)");
                _joggingController.UpdateMaxJogSpeedsFromSettings(s);
                _machineControlService.UpdateMaxSpindleSpeed(ExtractMaxSpindleSpeedFromSettings(s));
                JogSpeedPercentage =50.0;
                OnPropertyChanged(nameof(MaxAJogSpeed));
                OnPropertyChanged(nameof(CurrentAAxisJogSpeed));
                OnPropertyChanged(nameof(AAxisSpeedDisplayText));
                OnPropertyChanged(nameof(IsAAxisAvailable));
            }), DispatcherPriority.Background);
        }
        private void OnLogMessageAdded(string m) { if (ShouldFilterLogMessage(m)) return; if (IsUserImportantMessage(m)) AddLogMessage(m); }
        private void OnLoadingStarted()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try { _loadingPopup = new LoadingPopup { Owner = Application.Current.MainWindow }; _loadingPopup.Show(); }
                catch (Exception ex) { AddLogMessage($"> HATA: Loading popup açılamadı - {ex.Message}"); }
            });
        }
        private void OnLoadingCompleted()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _loadingPopup?.ClosePopup(); _loadingPopup = null; AddLogMessage("> ✅ Settings yükleme tamamlandı - Jog paneline geçiliyor...");
                    NavigateToPanel?.Invoke(MenuPage.Jog);
                    if (_pendingHomingPopup && !_homingPopupShown && !_hasHomed) { _pendingHomingPopup = false; ShowHomingRequiredPopup(); }
                }
                catch (Exception ex) { AddLogMessage($"> HATA: Loading popup kapatılamadı - {ex.Message}"); }
            });
        }
        private void OnLoadingStatusChanged(string s) { try { _loadingPopup?.UpdateStatus(s); } catch { } }
        private void OnPortsChanged() => OnPropertyChanged(nameof(AvailablePorts));
        private void OnSettingsCountChanged(int c) { try { _loadingPopup?.UpdateSettingsCount(c); } catch { } }
        private void OnGCodeExecutionStatusChanged(object s, EventArgs e) => BufferStatusChanged?.Invoke(this, EventArgs.Empty);
        private void OnGCodeFileLoaded(object s, string f) => AddLogMessage($"> 📁 G-Code file loaded: {f}");
        private void OnGCodeExecutionCompleted(object s, bool ok) => AddLogMessage(ok ? "> ✅ G-Code execution completed successfully!" : "> ❌ G-Code execution failed or stopped");
        private void OnMemoryLogManagerPropertyChanged(object s, PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);
        private void OnMemoryUsageCritical(object s, EventArgs e) => AddLogMessage("> ⚠️ Critical memory usage detected - performing emergency cleanup");
        private void OnMemoryCleanupCompleted(object s, int removed) { if (removed >50) AddLogMessage($"> 🧹 Memory cleanup completed - removed {removed:N0} old log entries"); }

        private double ExtractMaxSpindleSpeedFromSettings(ObservableCollection<GCodeSetting> settings)
        {
            try
            {
                var v = settings?.FirstOrDefault(s => s.Id ==30);
                double res;
                if (v != null && double.TryParse(v.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out res))
                {
                    return res;
                }
            }
            catch
            {
                // ignore and fall through to default
            }
            return 24000;
        }
        private void UpdateAllGoButtonStates() { OnPropertyChanged(nameof(IsGoXButtonEnabled)); OnPropertyChanged(nameof(IsGoYButtonEnabled)); OnPropertyChanged(nameof(IsGoZButtonEnabled)); OnPropertyChanged(nameof(IsGoAButtonEnabled)); }

        // Homing popup
        public void MarkHomed() { _hasHomed = true; CloseHomingPopupIfPossible(); }
        private void ShowHomingRequiredPopup()
        {
            if (_homingPopupShown) return;
            _homingPopupShown = true;
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_homingPopupWindow == null)
                        {
                            var mw = Application.Current.MainWindow;
                            if (mw != null) mw.IsEnabled = false;
                            _homingPopupWindow = new HomingRequiredPopup { Owner = mw, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                            _homingPopupWindow.Closed += (s, e) => { if (mw != null) mw.IsEnabled = true; };
                            _homingPopupWindow.Show();
                            AddLogMessage("> ⚠️ Homing gerekli – popup gösterildi");
                        }
                    }
                    catch (Exception ex) { AddLogMessage($"> Popup gösterme hatası: {ex.Message}"); }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex) { AddLogMessage($"> Homing popup dispatch hatası: {ex.Message}"); }
        }
        private void CloseHomingPopupIfPossible()
        {
            if (_homingPopupWindow == null || !_hasHomed) return;
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var owner = _homingPopupWindow.Owner;
                        _homingPopupWindow.Close();
                        _homingPopupWindow = null;
                        if (owner != null) owner.IsEnabled = true;
                        AddLogMessage("> ✅ Homing tamamlandı – popup kapatıldı");
                    }
                    catch (Exception ex) { AddLogMessage($"> Popup kapatma hatası: {ex.Message}"); }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex) { AddLogMessage($"> Homing popup kapatma dispatch hatası: {ex.Message}"); }
        }

        // Timer
        private void StatusAndProcessTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _dataProcessingManager.ProcessReceivedData();
                if (IsConnected && _connectionManager.IsInitialSetupComplete)
                {
                    _timerTickCount++;
                    bool moving = MachineStatus.Contains("Run") || MachineStatus.Contains("Jog");
                    _joggingController.UpdateMachineState(MachineStatus, moving);

                    // ✅ HIZLI QUERY: JOG sırasında timer'ı 25ms'ye ayarla
                    if (_joggingController.IsJogging)
                    {
                        _statusAndProcessTimer.Interval = TimeSpan.FromMilliseconds(25);
                        // Her tick query gönder
                        _ = QueryStatusOnce(200);
                    }
                    else
                    {
                        // NORMAL QUERY: JOG değilse timer 50ms'ye geri dön
                        if (_statusAndProcessTimer.Interval.TotalMilliseconds != 50)
                        {
                            _statusAndProcessTimer.Interval = TimeSpan.FromMilliseconds(50);
                        }

                        // Do not send periodic status queries when status queries are blocked by probe operations
                        if (!StatusQueriesBlocked && !(CentralStatusQuerierEnabled || StatusQueryService.HasCentralQuerier))
                        {
                            bool query = false;
                            if (_joggingController.CanAcceptNewJogCommand) query = (_timerTickCount %4 ==0);
                            else query = (_timerTickCount %2 ==0);
                            if (query) _ = QueryStatusOnce(200);
                        }

                        if (_timerTickCount >=400) _timerTickCount =0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException)) System.Diagnostics.Debug.WriteLine($"StatusTimer error: {ex.Message}");
            }
        }
        private async Task<bool> QueryStatusOnce(int? requestedIntervalMs = null) => await StatusQueryService.QueryStatusOnce(requestedIntervalMs);

        // Buffer helpers
        private void ClearBuffer() { try { _dataProcessingManager?.ClearBuffer(); AddLogMessage("> 🧹 Buffer cleared"); } catch (Exception ex) { AddLogMessage($"> ❌ Buffer clear error: {ex.Message}"); } }
        private void ClearCommandQueue() { try { _dataProcessingManager?.ClearDataQueue(); AddLogMessage("> 🧹 Command queue cleared"); } catch (Exception ex) { AddLogMessage($"> ❌ Command queue clear error: {ex.Message}"); } }

        // Stop popup controls
        private void ShowStopDecisionPopup()
        {
            try
            {
                if (_stopHoldPopupShown) return;
                _stopHoldPopupShown = true;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        bool confirmStop = Controls.MessageDialog.ShowConfirm(
                            "G-Code Yürütme Duraklatıldı",
                            "İşlem güvenli şekilde duraklatıldı. Bir karar gerekli.\n\n" +
                            "Durdurma seçenekleri:\n" +
                            "• STOP Run: Yürütmeyi tamamen durdur ve güvenli duruş sekansını çalıştır.\n" +
                            "• Continue: Duraklatmadan devam et (kaldığı yerden).\n\n" +
                            "Lütfen bir seçenek seçin.");
                        if (confirmStop) ConfirmStopAndRunSequence();
                        else { _isWaitingForStopDecision = false; _pendingStopSequence = false; }
                        _stopHoldPopupShown = false;
                    }
                    catch (Exception ex) { AddLogMessage($"> ❌ Stop popup error: {ex.Message}"); _stopHoldPopupShown = false; }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex) { AddLogMessage($"> ❌ ShowStopDecisionPopup error: {ex.Message}"); _stopHoldPopupShown = false; }
        }
        private void CloseStopDecisionPopupIfOpen()
        {
            try { _stopHoldPopupShown = false; AddLogMessage("> ✅ Stop decision no longer needed - state resolved"); }
            catch (Exception ex) { AddLogMessage($"> ❌ CloseStopDecisionPopup error: {ex.Message}"); }
        }

        public bool IsAAxisAvailable => _connectionManager?.IsAAxisAvailable ?? false;
        public bool IsAlarmState() { try { var s = MachineStatus?.ToLowerInvariant() ?? ""; return s.Contains("alarm"); } catch { return false; } }

        private void HandleHoldStateTransition(string prev, string current)
        {
            try
            {
                bool nowHold = current != null && current.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
                bool wasHold = prev != null && prev.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
                if (nowHold && !wasHold)
                {
                    AddLogMessage($"> 🔄 Hold durumuna geçiş - önceki: '{prev}', şimdi: '{current}'");
                    if (_isWaitingForStopDecision && _pendingStopSequence)
                    {
                        if (!DisableAutoStopPopup) ShowStopDecisionPopup();
                        else AddLogMessage("> ⏸️ Hold: Auto popup disabled – UI will handle decision");
                    }
                    else AddLogMessage("> (Auto Hold recovery devre dışı) Kullanıcı kararı beklenmiyor – hiçbir otomatik reset yapılmayacak");
                }
                else if (!nowHold && wasHold)
                {
                    AddLogMessage($"> ✅ Hold durumundan çıkış - önceki: '{prev}', şimdi: '{current}'");
                    if (current != null && current.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)) CloseStopDecisionPopupIfOpen();
                }
            }
            catch (Exception ex) { AddLogMessage($"> Hold transition handler error: {ex.Message}"); }
        }

        public async Task<bool> RecoverFromAlarmAsync()
        {
            if (_alarmRecoveryInProgress) { AddLogMessage("> 🔁 Alarm recovery already running – skipping"); return false; }
            if (!IsAlarmState()) { AddLogMessage("> ℹ️ No active ALARM state – recovery skipped"); return true; }
            _alarmRecoveryInProgress = true;
            try
            {
                AddLogMessage("> 🆘 ALARM RECOVERY START (simplified)");
                AddLogMessage($"> Current status: {MachineStatus}");
                AddLogMessage("> 🔓 Trying $X unlock (fast path)...");
                bool unlocked = await UnlockMachineAsync();
                if (unlocked && !IsAlarmState()) { AddLogMessage("> ✅ Alarm cleared with direct unlock"); await SendGCodeCommandAsync("?"); return true; }
                AddLogMessage("> ⚠️ Direct unlock failed or still in alarm – performing emergency reset");
                bool resetOk = await EmergencyResetAsync();
                if (!resetOk) { AddLogMessage("> ❌ Emergency reset failed – manual intervention required"); return false; }
                await Task.Delay(1500);
                await SendGCodeCommandAsync("?");
                await Task.Delay(400);
                if (!IsAlarmState()) { AddLogMessage("> ✅ Alarm state cleared after reset"); return true; }
                AddLogMessage("> 🔓 Final unlock attempt after reset...");
                bool finalUnlock = await UnlockMachineAsync();
                await Task.Delay(300);
                await SendGCodeCommandAsync("?");
                if (!IsAlarmState()) { AddLogMessage("> ✅ ALARM RECOVERY SUCCESS (reset + unlock)"); return true; }
                AddLogMessage("> ❌ Alarm persists after recovery attempts – manual action needed");
                return false;
            }
            catch (Exception ex) { AddLogMessage($"> ❌ Alarm recovery exception: {ex.Message}"); _errorHandlingService.LogError("RecoverFromAlarmAsync", ex, ErrorHandlingService.ErrorSeverity.Error); return false; }
            finally { _alarmRecoveryInProgress = false; }
        }

        // Execute Complete Stop Sequence (simplified but safe)
        private async Task ExecuteCompleteStopSequence()
        {
            if (_stopSequenceRunning) { AddLogMessage("> ⏹️ Stop sequence already running – ignored duplicate"); return; }
            _stopSequenceRunning = true;
            _postStopGraceUntil = DateTime.UtcNow.AddSeconds(6);
            try
            {
                _stopProgressDialog?.AppendProgress("> === STOP SEQUENCE START ===");
                _gCodeManager?.CompleteStopSequence();
                _stopProgressDialog?.AppendProgress("> Planner/completion flags set (CompleteStopSequence)");
                _gCodeManager?.CancelExecution();
                _stopProgressDialog?.AppendProgress("> Execution cancelled");
                ClearBuffer();
                ClearCommandQueue();
                _stopProgressDialog?.AppendProgress("> Buffers and queues cleared");
                bool es = await EmergencyStopAsync();
                _stopProgressDialog?.AppendProgress(es ? "> ✅ Emergency stop sent" : "> ⚠️ Emergency stop could not be sent");
                await Task.Delay(800);
                await SendGCodeCommandAsync("?");
                await Task.Delay(300);
                _stopProgressDialog?.AppendProgress($"> Status: {MachineStatus}");
                if (MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                {
                    _stopProgressDialog?.AppendProgress("> ⚠️ Hold persists – attempting automatic release");
                    await ForceExitHoldAsync("StopSequence");
                }
                if (IsAlarmState())
                {
                    _stopProgressDialog?.AppendProgress("> ⚠️ Alarm detected – attempting recovery");
                    await RecoverFromAlarmAsync();
                }
                _postStopGraceUntil = DateTime.UtcNow.AddSeconds(6);
                _stopProgressDialog?.AppendProgress("> ⏹️ Complete Stop Sequence finished");
                AddLogMessage("> ⏹️ Complete Stop Sequence finished");
            }
            catch (Exception ex)
            {
                _stopProgressDialog?.AppendProgress($"> ❌ Complete Stop Sequence error: {ex.Message}");
                AddLogMessage($"> ❌ Complete Stop Sequence error: {ex.Message}");
            }
            finally
            {
                _isWaitingForStopDecision = false;
                _pendingStopSequence = false;
                _stopHoldPopupShown = false;
                _stopSequenceRunning = false;
                try { Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { try { StopSequenceCompleted?.Invoke(); } catch { } }), DispatcherPriority.Background); } catch { }
            }
        }

        // Wait until controller reports Idle (simple polling)
        private async Task<bool> WaitUntilIdleAsync(int timeoutMs =15000, int stableCount =2)
        {
            var sw = Stopwatch.StartNew();
            int idleStreak =0;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string st = MachineStatus ?? string.Empty;
                if (st.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
                {
                    idleStreak++;
                    if (idleStreak >= stableCount) return true;
                }
                else idleStreak =0;
                try { await SendGCodeCommandAsync("?"); } catch { }
                await Task.Delay(200);
            }
            return false;
        }

        // Release Hold state (soft reset + unlock), no auto-resume
        private async Task<bool> ForceExitHoldAsync(string context, int attempts =3)
        {
            try
            {
                bool wasHold = MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
                if (!wasHold) { AddLogMessage($"> ℹ️ Hold release skip ({context}) – status: {MachineStatus}"); return true; }
                AddLogMessage($"> ⏳ Hold release started ({context}) – SAFE RESET STRATEGY – status: {MachineStatus}");
                for (int i =1; i <= attempts; i++)
                {
                    AddLogMessage($"> ▶ Hold release attempt {i}/{attempts} (soft reset + unlock)");
                    _stopProgressDialog?.AppendProgress($"> ▶ Hold release attempt {i}/{attempts}");
                    try { await SendControlCharacterAsync('\x85'); } catch { }
                    await Task.Delay(80);
                    await SendControlCharacterAsync('\x18');
                    await Task.Delay(500);
                    await SendGCodeCommandAsync("$X");
                    await Task.Delay(400);
                    for (int q =0; q <3; q++)
                    {
                        await SendGCodeCommandAsync("?");
                        await Task.Delay(250);
                        if (!MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase)) break;
                    }
                    if (!MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MachineStatus.StartsWith("Run", StringComparison.OrdinalIgnoreCase)) { AddLogMessage("> ⚠️ Controller reports Run after hold release – forcing Idle tag"); MachineStatus = "Idle"; }
                        AddLogMessage($"> ✅ Hold released without resume ({context}) -> {MachineStatus}");
                        return true;
                    }
                    AddLogMessage("> … still Hold – retrying");
                }
                AddLogMessage($"> ❌ Hold could not be released automatically ({context}) – final status: {MachineStatus}");
                return false;
            }
            catch (Exception ex) { AddLogMessage($"> ❌ Hold release exception: {ex.Message}"); return false; }
        }

        // Optional parking after stop (not called in simplified sequence but kept for completeness)
        private async Task ParkAfterStopAsync()
        {
            try
            {
                _stopProgressDialog?.AppendProgress("> ⏳ Waiting for IDLE before parking...");
                bool idle = await WaitUntilIdleAsync(20000,2);
                if (!idle) { _stopProgressDialog?.AppendProgress("> ⚠️ Timeout waiting IDLE – parking skipped"); return; }
                _stopProgressDialog?.AppendProgress("> 🔁 Switching to ABS (G90)");
                await SendGCodeCommandWithConfirmationAsync("G90");
                _stopProgressDialog?.AppendProgress("> ⬆ Z park to +15.000 (local)");
                await SendGCodeCommandWithConfirmationAsync("G00 Z15.000");
                await WaitUntilIdleAsync(20000,2);
                _stopProgressDialog?.AppendProgress("> ➡ Parking X,Y to0,0 (local)");
                await SendGCodeCommandWithConfirmationAsync("G00 X0 Y0");
                await WaitUntilIdleAsync(30000,2);
            }
            catch (Exception ex) { _stopProgressDialog?.AppendProgress($"> ❌ Parking error: {ex.Message}"); }
            finally
            {
                try { Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { try { _stopProgressDialog?.Close(); } catch { } _stopProgressDialog = null; }), DispatcherPriority.Background); } catch { _stopProgressDialog = null; }
            }
        }

        public void ConfirmStopAndRunSequence()
        {
            try
            {
                AddLogMessage("> ✅ Stop onaylandı - Emergency Stop sequence başlatılıyor...");
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _stopProgressDialog = MessageDialog.ShowPersistentStopDialog(
                            "STOP Sequence Çalışıyor",
                            "> G-Code yürütme güvenli şekilde durduruluyor...\n> İşlem adımları aşağıda anlık olarak listelenir.",
                            autoCloseOnIdle: false);
                    }
                    catch { }
                }), DispatcherPriority.Background);
                _ = Task.Run(async () => await ExecuteCompleteStopSequence());
            }
            catch (Exception ex) { AddLogMessage($"> ❌ ConfirmStop hata: {ex.Message}"); }
        }
        public async Task<bool> CancelStopAndContinueAsync()
        {
            try
            {
                _isWaitingForStopDecision = false;
                _pendingStopSequence = false;
                bool ok = await ContinueGCodeAsync();
                AddLogMessage(ok ? "> ▶️ Devam edildi (~ gönderildi)" : "> ⚠️ Devam edilemedi (Continue şartları sağlanmıyor)");
                return ok;
            }
            catch (Exception ex) { AddLogMessage($"> ❌ CancelStop hata: {ex.Message}"); return false; }
        }

        // Alarm / Unlock / Reset
        public async Task<bool> UnlockMachineAsync()
        {
            const string op = "UnlockMachine"; const int maxRetries =3; const int baseDelay =800;
            try
            {
                if (!IsConnected) { _errorHandlingService.LogError(op, new InvalidOperationException("Not connected"), ErrorHandlingService.ErrorSeverity.Warning); AddLogMessage("> ❌ Makine bağlı değil - unlock yapılamaz"); return false; }
                AddLogMessage("> 🔓 Makine kilidi açılıyor ($X komutu gönderiliyor)...");
                for (int attempt =1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        AddLogMessage($"> 🔄 Unlock denemesi {attempt}/{maxRetries}");
                        if (attempt >1) await Task.Delay(baseDelay * attempt);
                        if (!IsConnected) { AddLogMessage("> ⚠️ Bağlantı kopmuş, yeniden bağlanmayı deniyor..."); await Task.Delay(1000); continue; }
                        bool success = await SendGCodeCommandAsync("$X");
                        if (!success)
                        {
                            AddLogMessage("> ⚠️ $X blocked or failed via GCodeManager - trying direct send...");
                            try { success = await _connectionManager.SendGCodeCommandAsync("$X"); }
                            catch (Exception dx) { _errorHandlingService.LogError(op + ".DirectSend", dx, ErrorHandlingService.ErrorSeverity.Warning); AddLogMessage($"> ⚠️ Direct send failed: {dx.Message}"); success = false; }
                        }
                        if (success)
                        {
                            AddLogMessage($"> ✅ $X komutu gönderildi (deneme {attempt}) - Makine kilidi açılması gerekiyor");
                            await Task.Delay(1500);
                            for (int sr =0; sr <3; sr++) { await QueryStatusOnce(300); await Task.Delay(300); if (!IsAlarmState()) { AddLogMessage("> ✅ Makine alarm durumundan çıktı"); return true; } }
                            AddLogMessage("> 📊 Status sorgusu gönderildi - Durum kontrol ediliyor");
                            return true;
                        }
                        else
                        {
                            AddLogMessage($"> ⚠️ $X komutu gönderilemedi (deneme {attempt})");
                            if (attempt < maxRetries) AddLogMessage($"> 🔄 {baseDelay * (attempt +1)}ms bekleyip tekrar denenecek...");
                        }
                    }
                    catch (Exception aex) { AddLogMessage($"> ⚠️ Unlock deneme {attempt} hatası: {aex.Message}"); }
                }
                _errorHandlingService.LogError(op, new InvalidOperationException("Failed to send $X"), ErrorHandlingService.ErrorSeverity.Error);
                AddLogMessage($"> ❌ {maxRetries} deneme sonrası $X komutu gönderilemedi - manuel müdahale gerekli");
                return false;
            }
            catch (Exception ex) { _errorHandlingService.LogError(op, ex, ErrorHandlingService.ErrorSeverity.Error); AddLogMessage($"> ❌ Unlock hatası: {ex.Message}"); return false; }
        }
        public async Task<bool> EmergencyResetAsync()
        {
            const string op = "EmergencyReset";
            try
            {
                if (!IsConnected) { _errorHandlingService.LogError(op, new InvalidOperationException("Not connected"), ErrorHandlingService.ErrorSeverity.Warning); AddLogMessage("> ❌ Makine bağlı değil - reset yapılamaz"); return false; }
                _gCodeManager?.CancelExecution();
                AddLogMessage("> 🚨 Acil reset yapılıyor (Ctrl+X gönderiliyor)...");
                bool success = await SendControlCharacterAsync('\x18');
                if (success)
                {
                    AddLogMessage("> ✅ Acil reset gönderildi");
                    await Task.Delay(3000);
                    for (int i =0; i <5; i++) { if (IsConnected) break; AddLogMessage($"> 🔄 Bağlantı kontrolü {i +1}/5"); await Task.Delay(500); }
                    if (!IsConnected) { AddLogMessage("> ⚠️ Reset sonrası bağlantı problemi - unlock atlanıyor"); return true; }
                    await Task.Delay(800);
                    AddLogMessage("> 🔓 Reset sonrası unlock deneniyor...");
                    bool unlock = await UnlockMachineAsync();
                    AddLogMessage(unlock ? "> 🔓 Reset sonrası unlock başarılı" : "> ⚠️ Reset sonrası unlock başarısız - sonra tekrar denecek");
                    return true;
                }
                _errorHandlingService.LogError(op, new InvalidOperationException("Failed to send emergency reset"), ErrorHandlingService.ErrorSeverity.Error);
                AddLogMessage("> ❌ Acil reset gönderilemedi");
                return false;
            }
            catch (Exception ex) { _errorHandlingService.LogError(op, ex, ErrorHandlingService.ErrorSeverity.Error); AddLogMessage($"> ❌ Reset hatası: {ex.Message}"); return false; }
        }

        // Central status querier controls
        public bool CentralStatusQuerierEnabled
        {
            get => _centralStatusQuerierEnabled;
            set
            {
                if (_centralStatusQuerierEnabled == value) return;
                _centralStatusQuerierEnabled = value;
                if (value) StartCentralStatusQuerier(); else StopCentralStatusQuerier();
                OnPropertyChanged();
            }
        }
        public void StartCentralStatusQuerier()
        {
            try
            {
                if (_centralStatusQuerier != null) return;
                _centralStatusQuerier = new CentralStatusQuerier(_connectionManager) { DefaultIntervalMs =200 };
                _centralStatusQuerier.Start();
                try { _centralStatusSubscription = _centralStatusQuerier.SubscribeMinimumInterval(150); } catch { }
                AddLogMessage("> ✅ CentralStatusQuerier started");
                StatusQueryService.RegisterCentralQuerier(_centralStatusQuerier, s => _connectionManager.SendGCodeCommandAsync(s));
            }
            catch (Exception ex) { AddLogMessage($"> ❌ StartCentralStatusQuerier error: {ex.Message}"); }
        }
        public void StopCentralStatusQuerier()
        {
            try
            {
                if (_centralStatusQuerier == null) return;
                try { _centralStatusSubscription?.Dispose(); } catch { }
                _centralStatusSubscription = null;
                _centralStatusQuerier.Dispose();
                _centralStatusQuerier = null;
                AddLogMessage("> ✅ CentralStatusQuerier stopped");
                StatusQueryService.UnregisterCentralQuerier();
            }
            catch (Exception ex) { AddLogMessage($"> ❌ StopCentralStatusQuerier error: {ex.Message}"); }
        }
        public void ToggleCentralStatusQuerier() => CentralStatusQuerierEnabled = !CentralStatusQuerierEnabled;

        // Dispose
        public void Dispose()
        {
            try
            {
                try { StopCentralStatusQuerier(); } catch { }
                _statusAndProcessTimer?.Stop();
                _loadingPopup?.ClosePopup();
                _loadingPopup = null;
                _stepControlService?.Dispose();
                _errorHandlingService?.Dispose();
                _coordinateService?.Dispose();
                _machineControlService?.Dispose();
                if (_connectionManager != null)
                {
                    _connectionManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _connectionManager.ResponseReceived -= OnResponseReceived;
                    _connectionManager.SettingsReceived -= OnSettingsReceived;
                    _connectionManager.LogMessageAdded -= OnLogMessageAdded;
                    _connectionManager.PortsChanged -= OnPortsChanged;
                    _connectionManager.LoadingStarted -= OnLoadingStarted;
                    _connectionManager.LoadingCompleted -= OnLoadingCompleted;
                    _connectionManager.LoadingStatusChanged -= OnLoadingStatusChanged;
                    _connectionManager.SettingsCountChanged -= OnSettingsCountChanged;
                    _connectionManager.Dispose();
                }
                if (_joggingController != null) _joggingController.PropertyChanged -= OnJoggingControllerPropertyChanged;
                if (_stepControlService != null) _stepControlService.PropertyChanged -= OnStepControlServicePropertyChanged;
                if (_machineControlService != null) _machineControlService.PropertyChanged -= OnMachineControlServicePropertyChanged;
                if (_uiManager != null)
                {
                    _uiManager.PropertyChanged -= OnUIManagerPropertyChanged;
                    _uiManager.BufferStatusChanged -= OnBufferStatusChanged;
                    _uiManager.Dispose();
                }
                _dataProcessingManager?.Dispose();
                if (_gCodeManager != null)
                {
                    _gCodeManager.PropertyChanged -= OnGCodeManagerPropertyChanged;
                    _gCodeManager.ExecutionStatusChanged -= OnGCodeExecutionStatusChanged;
                    _gCodeManager.FileLoaded -= OnGCodeFileLoaded;
                    _gCodeManager.ExecutionCompleted -= OnGCodeExecutionCompleted;
                    _gCodeManager.Dispose();
                }
                if (_memoryLogManager != null)
                {
                    _memoryLogManager.MemoryUsageCritical -= OnMemoryUsageCritical;
                    _memoryLogManager.MemoryCleanupCompleted -= OnMemoryCleanupCompleted;
                    _memoryLogManager.PropertyChanged -= OnMemoryLogManagerPropertyChanged;
                    _memoryLogManager.Dispose();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"MainControll dispose error: {ex.Message}"); }
        }
    }
}
