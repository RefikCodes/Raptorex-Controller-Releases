using CncControlApp.Managers;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CncControlApp
{
    // ✅ Optimized jog configuration - Class içinde tanımlandı
    public class JoggingConfiguration
    {
        public int ContinuousJogResponseTime { get; set; } = 20; // 10ms'den 20ms'e artır
        public int StatusUpdateInterval { get; set; } = 50;     // 50ms kalsın
        public int JogCommandThrottle { get; set; } = 150;     // 5ms'den 150ms'e artır
        public bool EnableSmoothJogging { get; set; } = true;

        // ✅ ESP32 specific settings
        public int ESP32ProcessingDelay { get; set; } = 30;    // ESP32 command processing delay
        public int MaxConcurrentCommands { get; set; } = 1;    // Concurrent command limit
        public bool UseSequentialStopCommands { get; set; } = true; // Sequential stop commands
    }

    public class JoggingController : INotifyPropertyChanged
    {
        #region Fields

        private readonly ConnectionManager _connectionManager;
        private readonly JoggingConfiguration _jogConfig;

        // ✅ Existing fields (keep these)
        private bool _isJogging = false;
        private bool _isStepMode = false;
        private double _selectedStepSize = 0.1;
        private double _jogSpeedPercentage = 50.0;
        private int _maxXYJogSpeed = 1000;
        private int _maxZJogSpeed = 500;
        private int _maxAJogSpeed = 360;

        // ✅ NEW: Timer-based jogging control fields
        private bool _pendingJogStop = false;
        private bool _waitingForMachineIdle = false;
        private DateTime _lastJogCommandTime = DateTime.MinValue;
        private DateTime _lastStatusReceived = DateTime.MinValue;
        private string _pendingJogDirection = "";

        // ✅ NEW: Machine state tracking
        private string _lastMachineState = "Idle";
        private bool _isMachineMoving = false;

        // ✅ UPDATED: Complete ESP32 optimized timing constants
        private const int MIN_COMMAND_INTERVAL_MS = 150;    // Command interval
        private const int MACHINE_IDLE_TIMEOUT_MS = 750;    // Machine idle timeout
        private const int STATUS_TIMEOUT_MS = 300;          // Status freshness timeout
        private const int COMMAND_RETRY_DELAY_MS = 50;      // ESP32 command processing delay
        private const int FAST_STATUS_INTERVAL_MS = 25;     // Fast status query interval
        private const int NORMAL_STATUS_INTERVAL_MS = 100;  // Normal status query interval

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Properties

        public bool IsJogging
        {
            get => _isJogging;
            private set
            {
                if (_isJogging != value)
                {
                    _isJogging = value;
                    OnPropertyChanged();

                    System.Diagnostics.Debug.WriteLine($"Jogging state changed: {value}");
                    if (!value)
                    {
                        _pendingJogStop = false;
                        _waitingForMachineIdle = false;
                    }
                }
            }
        }

        public bool IsStepMode
        {
            get => _isStepMode;
            set
            {
                if (_isStepMode != value)
                {
                    _isStepMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModeDisplayText));
                    OnPropertyChanged(nameof(ModeDescriptionText));
                    OnPropertyChanged(nameof(SelectedStepSizeDisplay));
                }
            }
        }

        public double SelectedStepSize
        {
            get => _selectedStepSize;
            set
            {
                if (Math.Abs(_selectedStepSize - value) > 0.001)
                {
                    _selectedStepSize = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedStepSizeDisplay));
                }
            }
        }

        public string SelectedStepSizeDisplay
        {
            get
            {
                if (!_isStepMode)
                {
                    return "∞ mm";
                }

                return _selectedStepSize.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
            }
        }

        public string ModeDisplayText => IsStepMode ? "STEP" : "CONTINUOUS";
        public string ModeDescriptionText => IsStepMode ? "Precise Movement" : "Free Movement";

        public double JogSpeedPercentage
        {
            get => _jogSpeedPercentage;
            set
            {
                if (_jogSpeedPercentage != value)
                {
                    _jogSpeedPercentage = Math.Max(1.0, Math.Min(100.0, value));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentXYJogSpeed));
                    OnPropertyChanged(nameof(CurrentZJogSpeed));
                    OnPropertyChanged(nameof(CurrentAJogSpeed));
                    OnPropertyChanged(nameof(JogSpeedDisplayText));
                }
            }
        }

        public int MaxXYJogSpeed
        {
            get => _maxXYJogSpeed;
            set
            {
                if (_maxXYJogSpeed != value)
                {
                    _maxXYJogSpeed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentXYJogSpeed));
                    OnPropertyChanged(nameof(JogSpeedDisplayText));
                }
            }
        }

        public int MaxZJogSpeed
        {
            get => _maxZJogSpeed;
            set
            {
                if (_maxZJogSpeed != value)
                {
                    _maxZJogSpeed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentZJogSpeed));
                    OnPropertyChanged(nameof(JogSpeedDisplayText));
                }
            }
        }

        public int MaxAJogSpeed
        {
            get => _maxAJogSpeed;
            set
            {
                if (_maxAJogSpeed != value)
                {
                    _maxAJogSpeed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentAJogSpeed));
                }
            }
        }

        public int CurrentXYJogSpeed => (int)(_maxXYJogSpeed * _jogSpeedPercentage / 100.0);
        public int CurrentZJogSpeed => (int)(_maxZJogSpeed * _jogSpeedPercentage / 100.0);
        public int CurrentAJogSpeed => (int)(_maxAJogSpeed * _jogSpeedPercentage / 100.0);

        public string JogSpeedDisplayText => $"{_jogSpeedPercentage:F0}% (XY: {CurrentXYJogSpeed} mm/min, Z: {CurrentZJogSpeed} mm/min)";

        // ✅ NEW: Enhanced state properties
        public bool IsMachineIdle => !_isMachineMoving && _lastMachineState == "Idle";

        public bool CanAcceptNewJogCommand
        {
            get
            {
                var now = DateTime.Now;

                // ✅ Dynamic command interval based on machine state
                int dynamicInterval = _isMachineMoving ? MIN_COMMAND_INTERVAL_MS + 50 : MIN_COMMAND_INTERVAL_MS;

                bool commandIntervalOk = (now - _lastJogCommandTime).TotalMilliseconds >= dynamicInterval;
                bool statusFresh = (now - _lastStatusReceived).TotalMilliseconds <= STATUS_TIMEOUT_MS;
                bool notPendingStop = !_pendingJogStop && !_waitingForMachineIdle;

                // ✅ Extra safety check for rapid fire prevention
                bool notRapidFiring = (now - _lastJogCommandTime).TotalMilliseconds >= 80;

                return commandIntervalOk && statusFresh && notPendingStop && IsMachineIdle && notRapidFiring;
            }
        }
        #endregion

        #region Constructor

        public JoggingController(ConnectionManager connectionManager, JoggingConfiguration jogConfig = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _jogConfig = jogConfig ?? new JoggingConfiguration();

            // Connection status değişikliklerini dinle
            _connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;
        }

        #endregion

        #region Event Handlers

        private void OnConnectionStatusChanged(bool isConnected)
        {
            if (!isConnected)
            {
                IsJogging = false;
                ResetJoggingState();
            }
        }
        #endregion

        #region Public Methods

        public void ToggleJogMode()
        {
            IsStepMode = !IsStepMode;
        }

        public void SetStepSize(double stepSize)
        {
            SelectedStepSize = stepSize;
        }

        public void UpdateMaxJogSpeedsFromSettings(System.Collections.ObjectModel.ObservableCollection<GCodeSetting> settings)
        {
            try
            {
                if (settings == null) return;

                // ✅ GRBL format için mevcut kod
                var xMaxSpeed = settings.FirstOrDefault(s => s.Id == 110);
                var yMaxSpeed = settings.FirstOrDefault(s => s.Id == 111);
                var zMaxSpeed = settings.FirstOrDefault(s => s.Id == 112);
                var aMaxSpeed = settings.FirstOrDefault(s => s.Id == 113); // A axis max speed

                bool foundGrblSettings = false;

                if (xMaxSpeed != null && int.TryParse(xMaxSpeed.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int xMax))
                {
                    MaxXYJogSpeed = xMax;
                    foundGrblSettings = true;
                }

                if (yMaxSpeed != null && int.TryParse(yMaxSpeed.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int yMax))
                {
                    MaxXYJogSpeed = Math.Min(MaxXYJogSpeed, yMax);
                    foundGrblSettings = true;
                }

                if (zMaxSpeed != null && int.TryParse(zMaxSpeed.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int zMax))
                {
                    MaxZJogSpeed = zMax;
                    foundGrblSettings = true;
                }

                if (aMaxSpeed != null && int.TryParse(aMaxSpeed.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int aMax))
                {
                    MaxAJogSpeed = aMax;
                    foundGrblSettings = true;
                }

                // ✅ FluidNC format için gelişmiş kod - sadece GRBL bulunamazsa
                if (!foundGrblSettings)
                {
                    ProcessFluidNCSettings(settings);
                }

                // ✅ Property changed notifications
                NotifySpeedPropertiesChanged();

                System.Diagnostics.Debug.WriteLine($"Final Jog Speeds - XY: {MaxXYJogSpeed}, Z: {MaxZJogSpeed}, A: {MaxAJogSpeed}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateMaxJogSpeedsFromSettings error: {ex.Message}");
            }
        }
        #endregion

        #region Machine State Update Methods

        /// <summary>
        /// Machine status'u güncelle (MainControll timer'ından çağrılacak)
        /// </summary>
        public void UpdateMachineState(string machineStatus, bool isMoving)
        {
            try
            {
                _lastStatusReceived = DateTime.Now;
                bool stateChanged = _lastMachineState != machineStatus || _isMachineMoving != isMoving;

                _lastMachineState = machineStatus ?? "Unknown";
                _isMachineMoving = isMoving;

                if (stateChanged && IsMachineIdle)
                {
                    HandleMachineIdleState();
                }

                OnPropertyChanged(nameof(IsMachineIdle));
                OnPropertyChanged(nameof(CanAcceptNewJogCommand));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateMachineState error: {ex.Message}");
            }
        }

        private void HandleMachineIdleState()
        {
            try
            {
                if (_pendingJogStop || _waitingForMachineIdle)
                {
                    if (IsJogging)
                    {
                        IsJogging = false;
                        System.Diagnostics.Debug.WriteLine("Jogging stopped - machine is idle");
                    }
                    _pendingJogStop = false;
                    _waitingForMachineIdle = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandleMachineIdleState error: {ex.Message}");
            }
        }

        #endregion

        #region Enhanced Jogging Methods

        /// <summary>
        /// Enhanced jogging start method with state control
        /// </summary>
        public async Task StartJogAsync(string axisDirection, int? customFeedRate = null)
        {
            try
            {
                if (!_connectionManager.IsConnected)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot jog - not connected");
                    return;
                }

                // ✅ Step mode için ayrı işlem (hemen gönder)
                if (_isStepMode)
                {
                    await ExecuteStepJog(axisDirection, customFeedRate);
                    return;
                }

                // ✅ Continuous mode için state-based kontrol
                if (!CanAcceptNewJogCommand)
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot start jog - CanAcceptNewJogCommand: {CanAcceptNewJogCommand}");
                    return;
                }

                await ExecuteContinuousJog(axisDirection, customFeedRate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartJogAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Step jogging execution (immediate)
        /// </summary>
        private async Task ExecuteStepJog(string axisDirection, int? customFeedRate)
        {
            try
            {
                int feedRate = GetFeedRateForAxis(axisDirection, customFeedRate);
                string jogCommand = BuildJogCommand(axisDirection, feedRate);

                _lastJogCommandTime = DateTime.Now;
                await _connectionManager.SendGCodeCommandAsync(jogCommand);

                System.Diagnostics.Debug.WriteLine($"Step jog executed: {jogCommand}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteStepJog error: {ex.Message}");
            }
        }

        /// <summary>
        /// Continuous jogging execution (state-controlled) - ESP32 optimized
        /// </summary>
        private async Task ExecuteContinuousJog(string axisDirection, int? customFeedRate)
        {
            try
            {
                // ✅ Eğer zaten jogging yapılıyorsa ve aynı yöndeyse, ignore et
                if (IsJogging && _pendingJogDirection == axisDirection)
                {
                    System.Diagnostics.Debug.WriteLine($"Already jogging in same direction: {axisDirection}");
                    return;
                }

                // ✅ Farklı yöndeyse önce dur
                if (IsJogging && _pendingJogDirection != axisDirection)
                {
                    System.Diagnostics.Debug.WriteLine($"Direction change: {_pendingJogDirection} -> {axisDirection}");
                    await StopJogAsync();

                    // ✅ Machine'in durması için bekle - ESP32 için optimize edilmiş
                    var timeout = DateTime.Now.AddMilliseconds(MACHINE_IDLE_TIMEOUT_MS);
                    int waitCount = 0;

                    while (!IsMachineIdle && DateTime.Now < timeout)
                    {
                        await Task.Delay(25); // Daha sık kontrol
                        waitCount++;

                        // ✅ Her 20 check'te debug bilgisi (500ms)
                        if (waitCount % 20 == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Waiting for machine idle... State: {_lastMachineState}");
                        }
                    }

                    if (!IsMachineIdle)
                    {
                        System.Diagnostics.Debug.WriteLine($"Timeout waiting for machine to stop - State: {_lastMachineState}");
                        return;
                    }
                }

                // ✅ Yeni jogging başlat - ESP32'ye uygun timing
                int feedRate = GetFeedRateForAxis(axisDirection, customFeedRate);
                string jogCommand = BuildJogCommand(axisDirection, feedRate);

                _pendingJogDirection = axisDirection;
                _lastJogCommandTime = DateTime.Now;
                IsJogging = true;

                // ✅ Command gönderme ile state update arasında kısa delay
                await _connectionManager.SendGCodeCommandAsync(jogCommand);
                await Task.Delay(COMMAND_RETRY_DELAY_MS); // ESP32 processing time

                System.Diagnostics.Debug.WriteLine($"Continuous jog started: {jogCommand}");
            }
            catch (Exception ex)
            {
                IsJogging = false;
                System.Diagnostics.Debug.WriteLine($"ExecuteContinuousJog error: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced stop method with better ESP32 timing
        /// </summary>
        public async Task StopJogAsync()
        {
            try
            {
                if (!IsJogging)
                {
                    System.Diagnostics.Debug.WriteLine("Not jogging - stop ignored");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("STOP JOG INITIATED");

                // ✅ Stop komutlarını sequential olarak gönder (parallel yerine)
                _pendingJogStop = true;
                _waitingForMachineIdle = true;

                // ✅ İlk önce feed hold
                await _connectionManager.SendGCodeCommandAsync("!");
                await Task.Delay(COMMAND_RETRY_DELAY_MS); // ESP32'nin processing yapması için bekle

                // ✅ Sonra jog cancel
                await _connectionManager.SendControlCharacterAsync('\x85');
                await Task.Delay(COMMAND_RETRY_DELAY_MS);

                // ✅ Resume (daha uzun bekleme)
                await Task.Delay(_jogConfig.ContinuousJogResponseTime * 2); // 20ms'e çıkar
                await _connectionManager.SendGCodeCommandAsync("~");

                System.Diagnostics.Debug.WriteLine("Stop commands sent - waiting for machine idle");

                // ✅ Machine idle olana kadar bekle (daha uzun timeout)
                var timeout = DateTime.Now.AddMilliseconds(MACHINE_IDLE_TIMEOUT_MS);
                int checkCount = 0;

                while (!IsMachineIdle && DateTime.Now < timeout && _waitingForMachineIdle)
                {
                    await Task.Delay(25); // Daha sık kontrol et
                    checkCount++;

                    // ✅ Her 10 check'te bir debug bilgisi
                    if (checkCount % 10 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Waiting for idle... State: {_lastMachineState}, Moving: {_isMachineMoving}");
                    }
                }

                // ✅ Timeout durumunda force stop
                if (_waitingForMachineIdle)
                {
                    System.Diagnostics.Debug.WriteLine($"Force stopping jog due to timeout - Final state: {_lastMachineState}");
                    IsJogging = false;
                    _waitingForMachineIdle = false;
                    _pendingJogStop = false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Jogging stopped successfully via machine idle detection");
                }
            }
            catch (Exception ex)
            {
                // ✅ Error durumunda force stop
                IsJogging = false;
                _waitingForMachineIdle = false;
                _pendingJogStop = false;
                System.Diagnostics.Debug.WriteLine($"StopJogAsync error: {ex.Message}");
            }
        }

        // ✅ NEW: Specific axis jogging methods
        public async Task StartJogXPlusAsync(int? feedRate = null) => await StartJogAsync("X", feedRate);
        public async Task StartJogXMinusAsync(int? feedRate = null) => await StartJogAsync("X-", feedRate);
        public async Task StartJogYPlusAsync(int? feedRate = null) => await StartJogAsync("Y", feedRate);
        public async Task StartJogYMinusAsync(int? feedRate = null) => await StartJogAsync("Y-", feedRate);
        public async Task StartJogZPlusAsync(int? feedRate = null) => await StartJogAsync("Z", feedRate);
        public async Task StartJogZMinusAsync(int? feedRate = null) => await StartJogAsync("Z-", feedRate);
        public async Task StartJogAPlusAsync(int? feedRate = null) => await StartJogAsync("A", feedRate);
        public async Task StartJogAMinusAsync(int? feedRate = null) => await StartJogAsync("A-", feedRate);

        #endregion

        #region Emergency Methods

        /// <summary>
        /// Emergency stop - tüm jogging işlemlerini hemen durdur
        /// </summary>
        public async Task EmergencyStopAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("EMERGENCY STOP triggered");

                // ✅ State'leri hemen temizle
                IsJogging = false;
                _pendingJogStop = false;
                _waitingForMachineIdle = false;
                _pendingJogDirection = "";

                // ✅ Emergency stop komutları
                var emergencyTasks = new Task[]
                {
                    _connectionManager.SendControlCharacterAsync('\x18'), // Reset
                    _connectionManager.SendGCodeCommandAsync("!"),        // Feed hold
                    _connectionManager.SendControlCharacterAsync('\x85')  // Jog cancel
                };

                await Task.WhenAll(emergencyTasks);

                System.Diagnostics.Debug.WriteLine("Emergency stop commands sent");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EmergencyStopAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset jogging state (connection problems için)
        /// </summary>
        public void ResetJoggingState()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Resetting jogging state");

                IsJogging = false;
                _pendingJogStop = false;
                _waitingForMachineIdle = false;
                _pendingJogDirection = "";
                _isMachineMoving = false;
                _lastMachineState = "Idle";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetJoggingState error: {ex.Message}");
            }
        }

        #endregion

        #region Diagnostic Methods

        /// <summary>
        /// Enhanced diagnostics with timing analysis
        /// </summary>
        public string GetJoggingDiagnostics()
        {
            try
            {
                var now = DateTime.Now;
                var sb = new System.Text.StringBuilder();

                sb.AppendLine("=== JOGGING DIAGNOSTICS ===");
                sb.AppendLine($"IsJogging: {IsJogging}");
                sb.AppendLine($"IsMachineIdle: {IsMachineIdle}");
                sb.AppendLine($"CanAcceptNewJogCommand: {CanAcceptNewJogCommand}");
                sb.AppendLine($"PendingJogStop: {_pendingJogStop}");
                sb.AppendLine($"WaitingForMachineIdle: {_waitingForMachineIdle}");
                sb.AppendLine($"LastMachineState: {_lastMachineState}");
                sb.AppendLine($"IsMachineMoving: {_isMachineMoving}");
                sb.AppendLine($"PendingJogDirection: {_pendingJogDirection}");

                // ✅ Enhanced timing analysis
                var timeSinceLastCommand = (now - _lastJogCommandTime).TotalMilliseconds;
                var timeSinceLastStatus = (now - _lastStatusReceived).TotalMilliseconds;

                sb.AppendLine($"LastJogCommandTime: {timeSinceLastCommand:F0}ms ago");
                sb.AppendLine($"LastStatusReceived: {timeSinceLastStatus:F0}ms ago");
                sb.AppendLine($"Command Interval OK: {timeSinceLastCommand >= MIN_COMMAND_INTERVAL_MS}");
                sb.AppendLine($"Status Fresh: {timeSinceLastStatus <= STATUS_TIMEOUT_MS}");

                // ✅ Timing recommendations
                if (timeSinceLastCommand < MIN_COMMAND_INTERVAL_MS)
                {
                    sb.AppendLine($"⚠️ Command interval too short! Need {MIN_COMMAND_INTERVAL_MS - timeSinceLastCommand:F0}ms more");
                }

                if (timeSinceLastStatus > STATUS_TIMEOUT_MS)
                {
                    sb.AppendLine($"⚠️ Status data stale! {timeSinceLastStatus - STATUS_TIMEOUT_MS:F0}ms overdue");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Diagnostics error: {ex.Message}";
            }
        }

        #endregion

        #region Private Helper Methods

        private void ProcessFluidNCSettings(System.Collections.ObjectModel.ObservableCollection<GCodeSetting> settings)
        {
            // X Axis max speed
            var fluidncXSpeed = settings.FirstOrDefault(s =>
                s.Description != null &&
                (s.Description.ToLower().Contains("x") &&
                 s.Description.ToLower().Contains("max_rate")));

            // Y Axis max speed  
            var fluidncYSpeed = settings.FirstOrDefault(s =>
                s.Description != null &&
                (s.Description.ToLower().Contains("y") &&
                 s.Description.ToLower().Contains("max_rate")));

            // Z Axis max speed
            var fluidncZSpeed = settings.FirstOrDefault(s =>
                s.Description != null &&
                (s.Description.ToLower().Contains("z") &&
                 s.Description.ToLower().Contains("max_rate")));

            // A Axis max speed
            var fluidncASpeed = settings.FirstOrDefault(s =>
                s.Description != null &&
                (s.Description.ToLower().Contains("a") &&
                 s.Description.ToLower().Contains("max_rate")));

            // Process found FluidNC settings
            ProcessFluidNCAxisSpeed(fluidncXSpeed, "X", ref _maxXYJogSpeed);
            ProcessFluidNCAxisSpeed(fluidncYSpeed, "Y", ref _maxXYJogSpeed, true); // Use Min for Y
            ProcessFluidNCAxisSpeed(fluidncZSpeed, "Z", ref _maxZJogSpeed);
            ProcessFluidNCAxisSpeed(fluidncASpeed, "A", ref _maxAJogSpeed);

            // Fallback processing
            ProcessFallbackSpeed(settings);
        }

        private void ProcessFluidNCAxisSpeed(GCodeSetting setting, String axisName, ref int targetSpeed, bool useMin = false)
        {
            if (setting != null && TryParseFluidNCSpeed(setting.Value, out int speed))
            {
                if (useMin)
                {
                    targetSpeed = Math.Min(targetSpeed, speed);
                }
                else
                {
                    targetSpeed = speed;
                }
                System.Diagnostics.Debug.WriteLine($"FluidNC {axisName} Max Speed: {speed}");
            }
        }

        private void ProcessFallbackSpeed(System.Collections.ObjectModel.ObservableCollection<GCodeSetting> settings)
        {
            if (MaxXYJogSpeed <= 1000) // If still default values
            {
                var anySpeedSetting = settings.FirstOrDefault(s =>
                    s.Description != null &&
                    s.Description.ToLower().Contains("max") &&
                    s.Description.ToLower().Contains("rate") &&
                    TryParseFluidNCSpeed(s.Value, out _));

                if (anySpeedSetting != null &&
                    TryParseFluidNCSpeed(anySpeedSetting.Value, out int fallbackSpeed))
                {
                    MaxXYJogSpeed = fallbackSpeed;
                    MaxZJogSpeed = Math.Min(fallbackSpeed, 1000); // Z usually slower
                    System.Diagnostics.Debug.WriteLine($"FluidNC Fallback Speed: {fallbackSpeed}");
                }
            }
        }

        private void NotifySpeedPropertiesChanged()
        {
            OnPropertyChanged(nameof(MaxXYJogSpeed));
            OnPropertyChanged(nameof(MaxZJogSpeed));
            OnPropertyChanged(nameof(MaxAJogSpeed));
            OnPropertyChanged(nameof(CurrentXYJogSpeed));
            OnPropertyChanged(nameof(CurrentZJogSpeed));
            OnPropertyChanged(nameof(CurrentAJogSpeed));
            OnPropertyChanged(nameof(JogSpeedDisplayText));
        }

        private int GetFeedRateForAxis(string axisDirection, int? customFeedRate)
        {
            if (customFeedRate.HasValue)
            {
                return customFeedRate.Value;
            }

            bool isZAxis = axisDirection.ToUpper().Contains("Z");
            bool isAAxis = axisDirection.ToUpper().Contains("A");

            if (isZAxis)
                return CurrentZJogSpeed;
            else if (isAAxis)
                return CurrentAJogSpeed;
            else
                return CurrentXYJogSpeed;
        }

        private string BuildJogCommand(string axisDirection, int feedRate)
        {
            if (_isStepMode)
            {
                return $"$J=G91{axisDirection}{SelectedStepSize.ToString("F3", CultureInfo.InvariantCulture)} F{feedRate}";
            }
            else
            {
                // For rotary axes (A only), use smaller continuous movement distance
                bool isRotaryAxis = axisDirection.ToUpper().Contains("A");
                double moveDistance = isRotaryAxis ? 360.0 : 1000.0; // 360 degrees for rotary, 1000mm for linear

                return $"$J=G91{axisDirection}{moveDistance.ToString("F3", CultureInfo.InvariantCulture)} F{feedRate}";
            }
        }

        // ✅ FluidNC speed parsing helper
        private bool TryParseFluidNCSpeed(string speedValue, out int speed)
        {
            speed = 0;

            if (string.IsNullOrWhiteSpace(speedValue))
                return false;

            try
            {
                // Remove common units and clean the string
                string cleanValue = speedValue.Trim()
                    .Replace("mm/min", "")
                    .Replace("mm/s", "")
                    .Replace("mm", "")
                    .Replace("/min", "")
                    .Replace("/s", "")
                    .Replace("deg/min", "")
                    .Replace("degrees/min", "")
                    .Trim();

                // Handle different numeric formats
                if (double.TryParse(cleanValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleValue))
                {
                    // Convert mm/s to mm/min if needed
                    if (speedValue.Contains("mm/s") && !speedValue.Contains("mm/min"))
                    {
                        doubleValue *= 60; // Convert to mm/min
                    }

                    // Check if value exceeds int.MaxValue
                    if (doubleValue > int.MaxValue)
                    {
                        speed = int.MaxValue;
                    }
                    else
                    {
                        speed = (int)Math.Round(doubleValue);
                    }

                    // Ensure speed is within reasonable range
                    if (speed <= 0)
                        speed = 1000; // Default minimum
                    else if (speed > 50000)
                        speed = 50000; // Default maximum

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryParseFluidNCSpeed error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}