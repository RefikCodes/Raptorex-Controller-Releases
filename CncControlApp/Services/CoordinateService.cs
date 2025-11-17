using System;
using System.Threading.Tasks;
using System.Diagnostics; // 🆕 for DIAG logs
using System.Globalization;
using System.Collections.Generic;
using CncControlApp.Managers;
using System.Threading; // 🆕

namespace CncControlApp.Services
{
    /// <summary>
    /// Coordinate Service - Zero setting, positioning ve coordinate management
    /// MainControll.cs'den taşınmıştır
    /// </summary>
    public class CoordinateService : IDisposable
    {
        #region Fields

        private readonly ConnectionManager _connectionManager;
        private Action<string, Exception> _logErrorDelegate;
        private Action<string> _addLogMessageDelegate;

        // ✅ Target position fields
        private string _targetX = "0.000";
        private string _targetY = "0.000";
        private string _targetZ = "0.000";
        private string _targetA = "0.000";

        #endregion

        #region Constructor

        public CoordinateService(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            InitializeTargetValues();
        }

        public CoordinateService(ConnectionManager connectionManager, 
                               Action<string, Exception> logErrorDelegate, 
                               Action<string> addLogMessageDelegate)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
            InitializeTargetValues();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Target X coordinate
        /// </summary>
        public string TargetX
        {
            get => _targetX;
            set => _targetX = value ?? "0.000";
        }

        /// <summary>
        /// Target Y coordinate  
        /// </summary>
        public string TargetY
        {
            get => _targetY;
            set => _targetY = value ?? "0.000";
        }

        /// <summary>
        /// Target Z coordinate
        /// </summary>
        public string TargetZ
        {
            get => _targetZ;
            set => _targetZ = value ?? "0.000";
        }

        /// <summary>
        /// Target A coordinate (rotary axis)
        /// </summary>
        public string TargetA
        {
            get => _targetA;
            set => _targetA = value ?? "0.000";
        }

        /// <summary>
        /// Check if connection is available
        /// </summary>
        public bool IsConnected => _connectionManager?.IsConnected ?? false;

        #endregion

        #region Zero Setting Methods

        /// <summary>
        /// Set zero position for specified axis
        /// </summary>
        /// <param name="axis">Axis name (X, Y, Z, A, ALL)</param>
        /// <param name="permanent">Use persistent WCS zero (G10 L20) or temporary (G92)</param>
        /// <returns>Success status</returns>
        public async Task<bool> SetZeroAxisAsync(string axis, bool permanent = false)
        {
            const string operation = "SetZeroAxis";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                if (string.IsNullOrWhiteSpace(axis))
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException("Axis cannot be null or empty"));
                    return false;
                }

                axis = axis.ToUpperInvariant();

                if (permanent)
                {
                    // 🆕 Persistent zero using G10 L20 Pn ...  (sets current position to provided value for active WCS)
                    int p = await GetActiveWcsIndexAsync(default);
                    if (p < 1 || p > 6) p = 1; // default G54

                    string gcode;
                    switch (axis)
                    {
                        case "X": gcode = $"G10 L20 P{p} X0"; break;
                        case "Y": gcode = $"G10 L20 P{p} Y0"; break;
                        case "Z": gcode = $"G10 L20 P{p} Z0"; break;
                        case "A": gcode = $"G10 L20 P{p} A0"; break; // FluidNC/grblHAL genelde destekler
                        case "ALL": gcode = $"G10 L20 P{p} X0 Y0 Z0"; break; // A ekseni isteğe bağlı – eklemeyelim
                        default:
                            _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid axis: {axis}"));
                            return false;
                    }

                    var swSend = Stopwatch.StartNew();
                    bool ok = await _connectionManager.SendGCodeCommandAsync(gcode);
                    swSend.Stop();
                    _addLogMessageDelegate?.Invoke($"> [DIAG] G10 sendElapsed={swSend.ElapsedMilliseconds} ms, cmd='{gcode}'");

                    var swAck = Stopwatch.StartNew();
                    var cts = new CancellationTokenSource(3000);
                    var (received, line) = await WaitForOkOrErrorAsync(cts.Token);
                    swAck.Stop();
                    _addLogMessageDelegate?.Invoke($"> [DIAG] G10 ackElapsed={swAck.ElapsedMilliseconds} ms received={(received ? "yes" : "no")} resp='{line}'");

                    if (ok)
                    {
                        _addLogMessageDelegate?.Invoke($"> ✅ Persistent zero set via G10 L20 (P{p}) for {axis}");
                    }
                    else
                    {
                        _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to set persistent zero for {axis}"));
                    }
                    return ok;
                }
                else
                {
                    // Temporary zero using G92
                    string gcode = GenerateZeroCommand(axis, false);
                    if (string.IsNullOrEmpty(gcode))
                    {
                        _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid axis: {axis}"));
                        return false;
                    }

                    // [DIAG] Send timing
                    var swSend = Stopwatch.StartNew();
                    Debug.WriteLine($"[DIAG] CoordinateService SEND START axis={axis} permanent={permanent} gcode='{gcode}' t={DateTime.UtcNow:O}");

                    bool success = await _connectionManager.SendGCodeCommandAsync(gcode);

                    swSend.Stop();
                    Debug.WriteLine($"[DIAG] CoordinateService SEND END axis={axis} elapsed={swSend.ElapsedMilliseconds}ms success={success}");
                    _addLogMessageDelegate?.Invoke($"> [DIAG] SetZero axis={axis}, perm={permanent}, sendElapsed={swSend.ElapsedMilliseconds} ms");

                    // [DIAG] Ack timing (wait for ok/error)
                    var swAck = Stopwatch.StartNew();
                    var cts = new CancellationTokenSource(3000); // 3s timeout is safe; tune as needed
                    var (received, line) = await WaitForOkOrErrorAsync(cts.Token);
                    swAck.Stop();

                    Debug.WriteLine($"[DIAG] CoordinateService ACK END axis={axis} ackElapsed={swAck.ElapsedMilliseconds}ms received={received} line='{line}'");
                    _addLogMessageDelegate?.Invoke($"> [DIAG] SetZero axis={axis} ackElapsed={swAck.ElapsedMilliseconds} ms received={(received ? "yes" : "no")}");

                    if (success)
                    {
                        string zeroType = permanent ? "Permanent" : "Temporary";
                        _addLogMessageDelegate?.Invoke($"> ✅ {zeroType} Zero set for {axis} axis: {gcode}");
                    }
                    else
                    {
                        _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to set zero for {axis} axis"));
                    }

                    return success;
                }
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                return false;
            }
        }

        /// <summary>
        /// Set permanent zero for X axis
        /// </summary>
        public async Task<bool> SetZeroXPermanentAsync() => await SetZeroAxisAsync("X", true);

        /// <summary>
        /// Set permanent zero for Y axis
        /// </summary>
        public async Task<bool> SetZeroYPermanentAsync() => await SetZeroAxisAsync("Y", true);

        /// <summary>
        /// Set permanent zero for Z axis
        /// </summary>
        public async Task<bool> SetZeroZPermanentAsync() => await SetZeroAxisAsync("Z", true);

        /// <summary>
        /// Set permanent zero for A axis
        /// </summary>
        public async Task<bool> SetZeroAPermanentAsync() => await SetZeroAxisAsync("A", true);

        /// <summary>
        /// Set temporary zero for X axis
        /// </summary>
        public async Task<bool> SetZeroXTemporaryAsync() => await SetZeroAxisAsync("X", false);

        /// <summary>
        /// Set temporary zero for Y axis
        /// </summary>
        public async Task<bool> SetZeroYTemporaryAsync() => await SetZeroAxisAsync("Y", false);

        /// <summary>
        /// Set temporary zero for Z axis
        /// </summary>
        public async Task<bool> SetZeroZTemporaryAsync() => await SetZeroAxisAsync("Z", false);

        /// <summary>
        /// Set temporary zero for A axis
        /// </summary>
        public async Task<bool> SetZeroATemporaryAsync() => await SetZeroAxisAsync("A", false);

        /// <summary>
        /// Set temporary zero for all XYZ axes
        /// </summary>
        public async Task<bool> SetZeroAllTemporaryAsync() => await SetZeroAxisAsync("ALL", false);

        /// <summary>
        /// Set permanent zero for all XYZ axes
        /// </summary>
        public async Task<bool> SetZeroAllPermanentAsync() => await SetZeroAxisAsync("ALL", true);

        /// <summary>
        /// 🆕 Set active WCS zero (X/Y/Z[/A]=0) at current position using G10 L20 Pn.
        /// </summary>
        public async Task<bool> SetWorkZeroAtCurrentPositionAsync(bool includeA = false)
        {
            try
            {
                if (!IsConnected) return false;
                int p = await GetActiveWcsIndexAsync(default);
                if (p < 1 || p > 6) p = 1;
                string cmd = includeA ? $"G10 L20 P{p} X0 Y0 Z0 A0" : $"G10 L20 P{p} X0 Y0 Z0";
                bool ok = await _connectionManager.SendGCodeCommandAsync(cmd);
                if (ok) _addLogMessageDelegate?.Invoke($"> ✅ WCS zero updated persistently at current position (P{p})");
                return ok;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke("SetWorkZeroAtCurrentPosition", ex);
                return false;
            }
        }

        #endregion

        #region Positioning Methods

        /// <summary>
        /// Move to home position (0,0,0)
        /// </summary>
        /// <returns>Success status</returns>
        public async Task<bool> HomeAllAsync()
        {
            const string operation = "HomeAll";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string gcode = "G00 X0 Y0 Z0";
                bool success = await _connectionManager.SendGCodeCommandAsync(gcode);
                
                if (success)
                {
                    _addLogMessageDelegate?.Invoke($"> ✅ Home All completed: {gcode}");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Failed to execute home all command"));
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
        /// Move XYZ axes to home position
        /// </summary>
        public async Task<bool> HomeXYZAsync() => await HomeAllAsync();

        /// <summary>
        /// GRBL/FluidNC Machine Home All - $H command
        /// </summary>
        /// <returns>Success status</returns>
        public async Task<bool> MachineHomeAllAsync()
        {
            const string operation = "MachineHomeAll";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                _addLogMessageDelegate?.Invoke("> 🏠 Machine Home All başlatılıyor ($H)...");
                _addLogMessageDelegate?.Invoke("> ⚠️ DİKKAT: Tüm eksenler limit switch'lere gidecek!");

                bool success = await _connectionManager.SendGCodeCommandAsync("$H");
                
                if (success)
                {
                    _addLogMessageDelegate?.Invoke("> ✅ Machine Home All tamamlandı!");
                    _addLogMessageDelegate?.Invoke("> 📍 Eksenler limit switch pozisyonlarında");
                    _addLogMessageDelegate?.Invoke("> 🔧 Machine coordinates sıfırlandı");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Machine home all command failed"));
                    _addLogMessageDelegate?.Invoke("> ❌ HATA: Machine Home All başarısız!");
                    _addLogMessageDelegate?.Invoke("> 🔍 Limit switch'leri ve bağlantıları kontrol edin");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logErrorDelegate?.Invoke(operation, ex);
                _addLogMessageDelegate?.Invoke($"> ❌ KRITIK HATA: Machine Home işlemi - {ex.Message}");
                _addLogMessageDelegate?.Invoke("> 🛑 Acil durdurma yapın ve sistem kontrolü edin");
                return false;
            }
        }

        /// <summary>
        /// Move to target position for specified axis
        /// </summary>
        /// <param name="axis">Axis name (X, Y, Z, A)</param>
        /// <returns>Success status</returns>
        public async Task<bool> GoToPositionAsync(string axis)
        {
            const string operation = "GoToPosition";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                if (string.IsNullOrWhiteSpace(axis))
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException("Axis cannot be null or empty"));
                    return false;
                }

                var (gcode, targetValue) = GenerateGoToCommand(axis.ToUpper());
                
                if (string.IsNullOrEmpty(gcode))
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException($"Invalid axis or target value: {axis}"));
                    return false;
                }

                bool success = await _connectionManager.SendGCodeCommandAsync(gcode);
                
                if (success)
                {
                    _addLogMessageDelegate?.Invoke($"> ✅ Moving {axis.ToUpper()} to position {targetValue}: {gcode}");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to move {axis} to position {targetValue}"));
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
        /// Move to target position with custom coordinates
        /// </summary>
        /// <param name="x">X coordinate (optional)</param>
        /// <param name="y">Y coordinate (optional)</param>
        /// <param name="z">Z coordinate (optional)</param>
        /// <param name="a">A coordinate (optional)</param>
        /// <param name="feedRate">Feed rate for movement (optional)</param>
        /// <returns>Success status</returns>
        public async Task<bool> GoToPositionAsync(double? x = null, double? y = null, double? z = null, 
                                                 double? a = null, int? feedRate = null)
        {
            const string operation = "GoToPositionCustom";

            try
            {
                if (!IsConnected)
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException("Not connected to CNC machine"));
                    return false;
                }

                string gcode = GenerateMultiAxisGoToCommand(x, y, z, a, feedRate);
                
                if (string.IsNullOrEmpty(gcode))
                {
                    _logErrorDelegate?.Invoke(operation, new ArgumentException("No valid coordinates provided"));
                    return false;
                }

                bool success = await _connectionManager.SendGCodeCommandAsync(gcode);
                
                if (success)
                {
                    _addLogMessageDelegate?.Invoke($"> ✅ Multi-axis movement: {gcode}");
                }
                else
                {
                    _logErrorDelegate?.Invoke(operation, new InvalidOperationException($"Failed to execute multi-axis movement: {gcode}"));
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

        #region Target Position Management

        /// <summary>
        /// Set target position for all axes
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="z">Z coordinate</param>
        /// <param name="a">A coordinate</param>
        public void SetTargetPosition(string x, string y, string z, string a = "0.000")
        {
            TargetX = x ?? "0.000";
            TargetY = y ?? "0.000";
            TargetZ = z ?? "0.000";
            TargetA = a ?? "0.000";

            _addLogMessageDelegate?.Invoke($"> Target position set: X={TargetX}, Y={TargetY}, Z={TargetZ}, A={TargetA}");
        }

        /// <summary>
        /// Set target position from current machine coordinates
        /// </summary>
        public void SetTargetFromCurrent(double currentX, double currentY, double currentZ, 
                                       double currentA = 0.0)
        {
            TargetX = currentX.ToString("F3", CultureInfo.InvariantCulture);
            TargetY = currentY.ToString("F3", CultureInfo.InvariantCulture);
            TargetZ = currentZ.ToString("F3", CultureInfo.InvariantCulture);
            TargetA = currentA.ToString("F3", CultureInfo.InvariantCulture);

            _addLogMessageDelegate?.Invoke($"> Target set from current: X={TargetX}, Y={TargetY}, Z={TargetZ}, A={TargetA}");
        }

        /// <summary>
        /// Reset all target positions to zero
        /// </summary>
        public void ResetTargetPositions()
        {
            TargetX = "0.000";
            TargetY = "0.000";
            TargetZ = "0.000";
            TargetA = "0.000";

            _addLogMessageDelegate?.Invoke("> Target positions reset to zero");
        }

        /// <summary>
        /// Check if target is different from current position
        /// </summary>
        public bool IsTargetDifferentFromCurrent(string targetStr, double currentValue, double tolerance = 0.001)
        {
            if (string.IsNullOrWhiteSpace(targetStr)) return false;

            if (double.TryParse(targetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double targetValue))
            {
                return Math.Abs(targetValue - currentValue) > tolerance;
            }
            return false;
        }

        #endregion

        #region Coordinate Utilities

        public bool IsValidCoordinate(string coordinate)
        {
            if (string.IsNullOrWhiteSpace(coordinate))
                return false;

            return double.TryParse(coordinate, NumberStyles.Any, CultureInfo.InvariantCulture, out double value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public string FormatCoordinate(double value, int decimals = 3)
        {
            return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        public double ParseCoordinate(string coordinate, double defaultValue = 0.0)
        {
            if (double.TryParse(coordinate, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
            return defaultValue;
        }

        public string GetTargetPositionsInfo()
        {
            return $"Target Positions - X:{TargetX}, Y:{TargetY}, Z:{TargetZ}, A:{TargetA}";
        }

        #endregion

        #region Helper Methods

        private void InitializeTargetValues()
        {
            TargetX = "0.000";
            TargetY = "0.000";
            TargetZ = "0.000";
            TargetA = "0.000";

            System.Diagnostics.Debug.WriteLine("CoordinateService: Target values initialized");
        }

        /// <summary>
        /// Generate zero setting G-code command for temporary zero (G92)
        /// </summary>
        private string GenerateZeroCommand(string axis, bool permanent)
        {
            // Temporary zero using G92 (NOTE: permanent handled via G10 L20 in SetZeroAxisAsync)
            switch (axis)
            {
                case "X": return "G92 X0";
                case "Y": return "G92 Y0"; 
                case "Z": return "G92 Z0";
                case "A": return "G92 A0";
                case "ALL": return "G92 X0 Y0 Z0"; // leave A out by default
                default: return string.Empty;
            }
        }

        // 🆕 Waits for next "ok" or "error" from the controller. Times out via the provided token.
        private Task<(bool received, string line)> WaitForOkOrErrorAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<string> handler = null;
            handler = (string resp) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(resp)) return;
                    var low = resp.ToLowerInvariant();
                    if (low.Contains("ok") || low.Contains("error"))
                    {
                        // Complete and unsubscribe
                        _connectionManager.ResponseReceived -= handler;
                        tcs.TrySetResult((true, resp.Trim()));
                    }
                }
                catch
                {
                    // ignore parsing issues
                }
            };

            _connectionManager.ResponseReceived += handler;

            // Cancellation / timeout
            ct.Register(() =>
            {
                try { _connectionManager.ResponseReceived -= handler; } catch { }
                tcs.TrySetResult((false, "timeout"));
            });

            return tcs.Task;
        }

        // 🆕 Get active WCS index by sending $G and parsing [GC: ... G5x ...]
        private Task<int> GetActiveWcsIndexAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<string> handler = null;
            handler = (string resp) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(resp)) return;
                    var t = resp.Trim();
                    if (!t.Contains("[GC:")) return;
                    // sample: [GC:G54 G0 G17 G21 G90 G94 M5 ...]
                    int p = 1;
                    if (t.IndexOf("G54", StringComparison.OrdinalIgnoreCase) >= 0) p = 1;
                    else if (t.IndexOf("G55", StringComparison.OrdinalIgnoreCase) >= 0) p = 2;
                    else if (t.IndexOf("G56", StringComparison.OrdinalIgnoreCase) >= 0) p = 3;
                    else if (t.IndexOf("G57", StringComparison.OrdinalIgnoreCase) >= 0) p = 4;
                    else if (t.IndexOf("G58", StringComparison.OrdinalIgnoreCase) >= 0) p = 5;
                    else if (t.IndexOf("G59", StringComparison.OrdinalIgnoreCase) >= 0) p = 6;

                    _connectionManager.ResponseReceived -= handler;
                    tcs.TrySetResult(p);
                }
                catch { }
            };

            _connectionManager.ResponseReceived += handler;

            // fire $G
            _ = _connectionManager.SendGCodeCommandAsync("$G");

            // timeout handling
            ct.Register(() =>
            {
                try { _connectionManager.ResponseReceived -= handler; } catch { }
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(1); // default to G54
            });

            // default 1500ms timeout if None provided
            if (!ct.CanBeCanceled)
            {
                var cts = new CancellationTokenSource(1500);
                cts.Token.Register(() =>
                {
                    try { _connectionManager.ResponseReceived -= handler; } catch { }
                    if (!tcs.Task.IsCompleted) tcs.TrySetResult(1);
                });
            }

            return tcs.Task;
        }

        private (string gcode, string targetValue) GenerateGoToCommand(string axis)
        {
            string targetValue;
            string gcode;

            switch (axis)
            {
                case "X":
                    targetValue = TargetX;
                    gcode = $"G00 X{targetValue}";
                    break;
                case "Y":
                    targetValue = TargetY;
                    gcode = $"G00 Y{targetValue}";
                    break;
                case "Z":
                    targetValue = TargetZ;
                    gcode = $"G00 Z{targetValue}";
                    break;
                case "A":
                    targetValue = TargetA;
                    gcode = $"G00 A{targetValue}";
                    break;
                default:
                    return (string.Empty, string.Empty);
            }

            if (!IsValidCoordinate(targetValue))
            {
                return (string.Empty, targetValue);
            }

            return (gcode, targetValue);
        }

        private string GenerateMultiAxisGoToCommand(double? x, double? y, double? z, double? a, int? feedRate)
        {
            var parts = new List<string> { "G00" };

            if (x.HasValue) parts.Add($"X{FormatCoordinate(x.Value)}");
            if (y.HasValue) parts.Add($"Y{FormatCoordinate(y.Value)}");
            if (z.HasValue) parts.Add($"Z{FormatCoordinate(z.Value)}");
            if (a.HasValue) parts.Add($"A{FormatCoordinate(a.Value)}");
            if (feedRate.HasValue) parts.Add($"F{feedRate.Value}");

            if (parts.Count == 1) // Only "G00"
                return string.Empty;

            return string.Join(" ", parts);
        }

        public void UpdateDelegates(Action<string, Exception> logErrorDelegate, Action<string> addLogMessageDelegate)
        {
            _logErrorDelegate = logErrorDelegate;
            _addLogMessageDelegate = addLogMessageDelegate;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                _logErrorDelegate = null;
                _addLogMessageDelegate = null;

                System.Diagnostics.Debug.WriteLine("CoordinateService disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CoordinateService dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}