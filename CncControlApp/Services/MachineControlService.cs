using System;
using System.Threading.Tasks;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CncControlApp.Managers;

namespace CncControlApp.Services
{
    /// <summary>
    /// Machine Control Service - Spindle, Coolant, Mist, Lights ve diğer makine kontrolleri
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class MachineControlService : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        private readonly ConnectionManager _connectionManager;
        private Action<string, Exception> _logErrorDelegate;
        private Action<string> _addLogMessageDelegate;

        // ✅ Spindle control fields
        private bool _isSpindleOn = false;
        private double _spindleSpeed = 0;
        private double _maxSpindleSpeed = 24000;

        // ✅ Coolant and auxiliary control fields
        private bool _isCoolantOn = false;
        private bool _isMistOn = false;
        private bool _isLightsOn = false;
        private bool _isToolChangeOn = false;
        private bool _isProbeOn = false;
        private bool _isVacuumOn = false;
        private bool _isAirBlastOn = false;

        #endregion

        #region Constructor

        public MachineControlService(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            InitializeMachineControlState();
        }

        public MachineControlService(ConnectionManager connectionManager, 
                                   Action<string, Exception> logErrorDelegate, 
                                   Action<string> addLogMessageDelegate)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
            InitializeMachineControlState();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Check if connection is available
        /// </summary>
        public bool IsConnected => _connectionManager?.IsConnected ?? false;

        #region Spindle Properties

        /// <summary>
        /// Spindle ON/OFF state
        /// </summary>
        public bool IsSpindleOn
        {
            get => _isSpindleOn;
            set // ✅ private set yerine public set
            {
                if (_isSpindleOn != value)
                {
                    _isSpindleOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Current spindle speed in RPM
        /// </summary>
        public double SpindleSpeed
        {
            get => _spindleSpeed;
            set
            {
                if (Math.Abs(_spindleSpeed - value) > 0.1)
                {
                    _spindleSpeed = Math.Max(0, Math.Min(_maxSpindleSpeed, value));
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Maximum spindle speed in RPM
        /// </summary>
        public double MaxSpindleSpeed
        {
            get => _maxSpindleSpeed;
            set
            {
                if (Math.Abs(_maxSpindleSpeed - value) > 0.1)
                {
                    _maxSpindleSpeed = Math.Max(100, value);
                    OnPropertyChanged();

                    // Ensure current speed doesn't exceed max
                    if (_spindleSpeed > _maxSpindleSpeed)
                    {
                        SpindleSpeed = _maxSpindleSpeed;
                    }
                }
            }
        }

        /// <summary>
        /// Spindle speed as formatted string
        /// </summary>
        public string SpindleSpeedDisplay => $"{SpindleSpeed:F0} RPM";

        /// <summary>
        /// Spindle speed percentage (0-100%)
        /// </summary>
        public double SpindleSpeedPercentage
        {
            get => MaxSpindleSpeed > 0 ? (SpindleSpeed / MaxSpindleSpeed) * 100.0 : 0.0;
            set
            {
                var clampedPercentage = Math.Max(0.0, Math.Min(100.0, value));
                SpindleSpeed = (MaxSpindleSpeed * clampedPercentage) / 100.0;
            }
        }

        #endregion

        #region Coolant and Auxiliary Properties

        /// <summary>
        /// Coolant (Flood) ON/OFF state
        /// </summary>
        public bool IsCoolantOn
        {
            get => _isCoolantOn;
            set // ✅ private set yerine public set
            {
                if (_isCoolantOn != value)
                {
                    _isCoolantOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Mist coolant ON/OFF state
        /// </summary>
        public bool IsMistOn
        {
            get => _isMistOn;
            set // ✅ private set yerine public set
            {
                if (_isMistOn != value)
                {
                    _isMistOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Work lights ON/OFF state
        /// </summary>
        public bool IsLightsOn
        {
            get => _isLightsOn;
            set // ✅ private set yerine public set
            {
                if (_isLightsOn != value)
                {
                    _isLightsOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Tool change system ON/OFF state
        /// </summary>
        public bool IsToolChangeOn
        {
            get => _isToolChangeOn;
            set // ✅ private set yerine public set
            {
                if (_isToolChangeOn != value)
                {
                    _isToolChangeOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Probe system ON/OFF state
        /// </summary>
        public bool IsProbeOn
        {
            get => _isProbeOn;
            set // ✅ private set yerine public set
            {
                if (_isProbeOn != value)
                {
                    _isProbeOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Vacuum system ON/OFF state
        /// </summary>
        public bool IsVacuumOn
        {
            get => _isVacuumOn;
            set // ✅ private set yerine public set
            {
                if (_isVacuumOn != value)
                {
                    _isVacuumOn = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Air blast system ON/OFF state
        /// </summary>
        public bool IsAirBlastOn
        {
            get => _isAirBlastOn;
            set // ✅ private set yerine public set
            {
                if (_isAirBlastOn != value)
                {
                    _isAirBlastOn = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #endregion

        #region Spindle Control Methods

        /// <summary>
        /// Toggle spindle ON/OFF with speed control
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="speed">Custom speed (optional, uses current SpindleSpeed if null)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleSpindleAsync(bool turnOn, double? speed = null)
        {
            const string operation = "ToggleSpindle";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                bool success;

                if (turnOn)
                {
                    // Set custom speed if provided
                    if (speed.HasValue)
                    {
                        SpindleSpeed = speed.Value;
                    }

                    // ✅ Spindle speed validation
                    if (SpindleSpeed <= 0)
                    {
                        SpindleSpeed = 1000; // Default fallback speed
                        _addLogMessageDelegate?.Invoke($"> ⚠️ Spindle speed was 0, set to default: {SpindleSpeed} RPM");
                    }

                    if (SpindleSpeed > MaxSpindleSpeed)
                    {
                        _logErrorDelegate?.Invoke(operation, new ArgumentException($"Spindle speed {SpindleSpeed} exceeds maximum {MaxSpindleSpeed}"));
                        SpindleSpeed = MaxSpindleSpeed;
                    }

                    string spindleCommand = $"M03 S{SpindleSpeed:F0}";
                    success = await _connectionManager.SendGCodeCommandAsync(spindleCommand);

                    if (success)
                    {
                        IsSpindleOn = true;
                        _addLogMessageDelegate?.Invoke($"> ✅ Spindle ON: {SpindleSpeed} RPM");
                    }
                    else
                    {
                        _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to start spindle at {SpindleSpeed} RPM"));
                    }
                }
                else
                {
                    success = await _connectionManager.SendGCodeCommandAsync("M05");

                    if (success)
                    {
                        IsSpindleOn = false;
                        SpindleSpeed = 0.0;
                        _addLogMessageDelegate?.Invoke("> ✅ Spindle OFF");
                    }
                    else
                    {
                        _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Failed to stop spindle"));
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Start spindle with specific speed
        /// </summary>
        /// <param name="speed">Spindle speed in RPM</param>
        /// <returns>Success status</returns>
        public async Task<bool> StartSpindleAsync(double speed)
        {
            return await ToggleSpindleAsync(true, speed);
        }

        /// <summary>
        /// Stop spindle
        /// </summary>
        /// <returns>Success status</returns>
        public async Task<bool> StopSpindleAsync()
        {
            return await ToggleSpindleAsync(false);
        }

        /// <summary>
        /// Set spindle speed without changing ON/OFF state
        /// </summary>
        /// <param name="speed">New spindle speed</param>
        /// <returns>Success status</returns>
        public async Task<bool> SetSpindleSpeedAsync(double speed)
        {
            const string operation = "SetSpindleSpeed";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                if (!IsSpindleOn)
                {
                    // Just update the speed property, don't send command
                    SpindleSpeed = speed;
                    _addLogMessageDelegate?.Invoke($"> Spindle speed set to {speed} RPM (spindle is off)");
                    return true;
                }

                // Validate speed
                if (speed < 0 || speed > MaxSpindleSpeed)
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid spindle speed: {speed} (max: {MaxSpindleSpeed})"));
                    return false;
                }

                SpindleSpeed = speed;
                string command = $"M03 S{speed:F0}";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    _addLogMessageDelegate?.Invoke($"> ✅ Spindle speed changed to {speed} RPM");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to set spindle speed to {speed}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        #endregion

        #region Coolant Control Methods

        /// <summary>
        /// Toggle coolant (flood) ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleCoolantAsync(bool turnOn)
        {
            const string operation = "ToggleCoolant";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? "M08" : "M09";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsCoolantOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Coolant {(turnOn ? "ON" : "OFF")}");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn coolant {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle mist coolant ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleMistAsync(bool turnOn)
        {
            const string operation = "ToggleMist";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? "M07" : "M09";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsMistOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Mist {(turnOn ? "ON" : "OFF")}");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn mist {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        #endregion

        #region Auxiliary Control Methods

        /// <summary>
        /// Toggle work lights ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="pin">Custom pin number (default: 11)</param>
        /// <param name="intensity">Light intensity 0-255 (default: 255)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleLightsAsync(bool turnOn, int pin = 11, int intensity = 255)
        {
            const string operation = "ToggleLights";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                // Validate parameters
                if (pin < 0 || pin > 255)
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid pin number: {pin} (0-255)"));
                    return false;
                }

                if (intensity < 0 || intensity > 255)
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid intensity: {intensity} (0-255)"));
                    return false;
                }

                int actualIntensity = turnOn ? intensity : 0;
                string command = $"M42 P{pin} S{actualIntensity}";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsLightsOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Lights {(turnOn ? "ON" : "OFF")}");

                    // ✅ Detailed control information
                    if (turnOn)
                    {
                        _addLogMessageDelegate?.Invoke($"> Light Control: Pin {pin}, Intensity {intensity} - Command: {command}");
                    }
                    else
                    {
                        _addLogMessageDelegate?.Invoke($"> Light Control: Lights turned off - Command: {command}");
                    }
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn lights {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle tool change system ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="pin">Custom pin number (default: 12)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleToolChangeAsync(bool turnOn, int pin = 12)
        {
            const string operation = "ToggleToolChange";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? $"M42 P{pin} S255" : $"M42 P{pin} S0";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsToolChangeOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Tool Change {(turnOn ? "ON" : "OFF")} (Pin {pin})");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn tool change {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle vacuum system ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="pin">Custom pin number (default: 13)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleVacuumAsync(bool turnOn, int pin = 13)
        {
            const string operation = "ToggleVacuum";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? $"M42 P{pin} S255" : $"M42 P{pin} S0";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsVacuumOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Vacuum {(turnOn ? "ON" : "OFF")} (Pin {pin})");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn vacuum {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle air blast system ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="pin">Custom pin number (default: 14)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleAirBlastAsync(bool turnOn, int pin = 14)
        {
            const string operation = "ToggleAirBlast";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? $"M42 P{pin} S255" : $"M42 P{pin} S0";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsAirBlastOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Air Blast {(turnOn ? "ON" : "OFF")} (Pin {pin})");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn air blast {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Toggle probe system ON/OFF
        /// </summary>
        /// <param name="turnOn">True to turn on, false to turn off</param>
        /// <param name="pin">Custom pin number (default: 15)</param>
        /// <returns>Success status</returns>
        public async Task<bool> ToggleProbeAsync(bool turnOn, int pin = 15)
        {
            const string operation = "ToggleProbe";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string command = turnOn ? $"M42 P{pin} S255" : $"M42 P{pin} S0";
                bool success = await _connectionManager.SendGCodeCommandAsync(command);

                if (success)
                {
                    IsProbeOn = turnOn;
                    _addLogMessageDelegate?.Invoke($"> ✅ Probe {(turnOn ? "ON" : "OFF")} (Pin {pin})");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to turn probe {(turnOn ? "ON" : "OFF")}"));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        #endregion

        #region Emergency and Safety Methods

        /// <summary>
        /// Emergency stop - stops all machine functions immediately
        /// </summary>
        /// <returns>Success status</returns>
        public async Task<bool> EmergencyStopAsync()
        {
            const string operation = "EmergencyStop";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                _addLogMessageDelegate?.Invoke("> 🚨 EMERGENCY STOP - Stopping all machine functions!");

                // Send feed hold first
                bool holdSuccess = await _connectionManager.SendControlCharacterAsync('!');

                // Stop spindle
                bool spindleSuccess = await _connectionManager.SendGCodeCommandAsync("M05");

                // Turn off coolant
                bool coolantSuccess = await _connectionManager.SendGCodeCommandAsync("M09");

                // Update states
                if (spindleSuccess)
                {
                    IsSpindleOn = false;
                    SpindleSpeed = 0.0;
                }

                if (coolantSuccess)
                {
                    IsCoolantOn = false;
                    IsMistOn = false;
                }

                _addLogMessageDelegate?.Invoke("> 🛑 Emergency stop completed - All systems stopped");

                return holdSuccess && spindleSuccess && coolantSuccess;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Turn off all auxiliary systems (lights, vacuum, air blast, etc.)
        /// </summary>
        /// <returns>Success status</returns>
        public async Task<bool> TurnOffAllAuxiliaryAsync()
        {
            const string operation = "TurnOffAllAuxiliary";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                _addLogMessageDelegate?.Invoke("> 🔌 Turning off all auxiliary systems...");

                bool success = true;

                // Turn off lights
                if (IsLightsOn)
                {
                    success &= await ToggleLightsAsync(false);
                }

                // Turn off vacuum
                if (IsVacuumOn)
                {
                    success &= await ToggleVacuumAsync(false);
                }

                // Turn off air blast
                if (IsAirBlastOn)
                {
                    success &= await ToggleAirBlastAsync(false);
                }

                // Turn off tool change
                if (IsToolChangeOn)
                {
                    success &= await ToggleToolChangeAsync(false);
                }

                // Turn off probe
                if (IsProbeOn)
                {
                    success &= await ToggleProbeAsync(false);
                }

                if (success)
                {
                    _addLogMessageDelegate?.Invoke("> ✅ All auxiliary systems turned off");
                }
                else
                {
                    _addLogMessageDelegate?.Invoke("> ⚠️ Some auxiliary systems may not have turned off completely");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        #endregion

        #region Configuration and Status Methods

        /// <summary>
        /// Update max spindle speed from settings
        /// </summary>
        /// <param name="maxSpeed">New maximum spindle speed</param>
        public void UpdateMaxSpindleSpeed(double maxSpeed)
        {
            try
            {
                if (maxSpeed > 0 && maxSpeed <= 100000) // Reasonable range
                {
                    MaxSpindleSpeed = maxSpeed;
                    _addLogMessageDelegate?.Invoke($"> Max spindle speed updated: {maxSpeed} RPM");
                }
                else
                {
                    _logErrorDelegate?.Invoke("UpdateMaxSpindleSpeed", new ArgumentException($"Invalid max spindle speed: {maxSpeed}"));
                }
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("UpdateMaxSpindleSpeed", ex);
            }
        }

        /// <summary>
        /// Get current machine control status
        /// </summary>
        /// <returns>Formatted status string</returns>
        public string GetMachineControlStatus()
        {
            try
            {
                var status = new System.Text.StringBuilder();
                status.AppendLine("=== MACHINE CONTROL STATUS ===");
                status.AppendLine($"Connection: {(IsConnected ? "Connected" : "Disconnected")}");
                status.AppendLine($"Spindle: {(IsSpindleOn ? "ON" : "OFF")} - {SpindleSpeed:F0} RPM (Max: {MaxSpindleSpeed:F0})");
                status.AppendLine($"Coolant: {(IsCoolantOn ? "ON" : "OFF")}");
                status.AppendLine($"Mist: {(IsMistOn ? "ON" : "OFF")}");
                status.AppendLine($"Lights: {(IsLightsOn ? "ON" : "OFF")}");
                status.AppendLine($"Tool Change: {(IsToolChangeOn ? "ON" : "OFF")}");
                status.AppendLine($"Probe: {(IsProbeOn ? "ON" : "OFF")}");
                status.AppendLine($"Vacuum: {(IsVacuumOn ? "ON" : "OFF")}");
                status.AppendLine($"Air Blast: {(IsAirBlastOn ? "ON" : "OFF")}");

                return status.ToString();
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("GetMachineControlStatus", ex);
                return "Error getting machine control status";
            }
        }

        /// <summary>
        /// Reset all control states to default (OFF)
        /// </summary>
        public void ResetControlStates()
        {
            try
            {
                IsSpindleOn = false;
                SpindleSpeed = 0.0;
                IsCoolantOn = false;
                IsMistOn = false;
                IsLightsOn = false;
                IsToolChangeOn = false;
                IsProbeOn = false;
                IsVacuumOn = false;
                IsAirBlastOn = false;

                _addLogMessageDelegate?.Invoke("> Machine control states reset to defaults");
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("ResetControlStates", ex);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Initialize machine control state to defaults
        /// </summary>
        private void InitializeMachineControlState()
        {
            try
            {
                IsSpindleOn = false;
                SpindleSpeed = 0.0;
                MaxSpindleSpeed = 24000; // Default max speed
                IsCoolantOn = false;
                IsMistOn = false;
                IsLightsOn = false;
                IsToolChangeOn = false;
                IsProbeOn = false;
                IsVacuumOn = false;
                IsAirBlastOn = false;

                System.Diagnostics.Debug.WriteLine("MachineControlService: Initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MachineControlService initialization error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update error delegates
        /// </summary>
        /// <param name="logErrorDelegate">Error logging delegate</param>
        /// <param name="addLogMessageDelegate">Message logging delegate</param>
        public void UpdateDelegates(Action<string, Exception> logErrorDelegate, Action<string> addLogMessageDelegate)
        {
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
        }

        /// <summary>
        /// Validate pin number for auxiliary controls
        /// </summary>
        /// <param name="pin">Pin number to validate</param>
        /// <returns>True if valid</returns>
        private bool IsValidPin(int pin)
        {
            return pin >= 0 && pin <= 255;
        }

        /// <summary>
        /// Validate spindle speed
        /// </summary>
        /// <param name="speed">Speed to validate</param>
        /// <returns>True if valid</returns>
        private bool IsValidSpindleSpeed(double speed)
        {
            return speed >= 0 && speed <= MaxSpindleSpeed && !double.IsNaN(speed) && !double.IsInfinity(speed);
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                _logErrorDelegate = null;
                _addLogMessageDelegate = null;

                System.Diagnostics.Debug.WriteLine("MachineControlService disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MachineControlService dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}