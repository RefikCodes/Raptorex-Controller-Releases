using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CncControlApp.Managers;

namespace CncControlApp.Services
{
    /// <summary>
    /// Centralized Probe Manager - Unified probing system for all axes (Z, X+, X-, Y+, Y-)
    /// Uses the same robust sequence: coarse probe → multiple fine probes with validation
    /// </summary>
    public class ProbeManager
    {
        private readonly MainControll _controller;
        private const int MaxProbeFeed = 300;
        private const double FineToleranceThreshold = 0.06; // mm
        private const int MaxFineAttempts = 6;

        public ProbeManager(MainControll controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        #region Public Probe Methods

        /// <summary>
        /// Execute Z probe with validated fine measurements
        /// </summary>
        public async Task<ProbeResult> ProbeZAsync(bool manageSession = true)
        {
            return await ExecuteProbeSequenceAsync(
                axis: 'Z',
                direction: -1, // Z probes downward (negative direction)
                coarseDistance: 30.0,
                fineDistance: 6.0,
                retractAfterCoarse: 2.0,
                retractAfterFine: 1.0,
                finalRetract: 10.0,
                manageSession: manageSession // Caller controls session management
            );
        }

        /// <summary>
        /// Execute X+ probe (positive direction)
        /// </summary>
        public async Task<ProbeResult> ProbeXPlusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return await ExecuteProbeSequenceAsync(
                axis: 'X',
                direction: 1, // X+ moves in positive direction
                coarseDistance: maxDistance,
                fineDistance: 6.0,
                retractAfterCoarse: 2.0,
                retractAfterFine: 1.0,
                finalRetract: 10.0,
                manageSession: manageSession // Caller controls session management
            );
        }

        /// <summary>
        /// Execute X- probe (negative direction)
        /// </summary>
        public async Task<ProbeResult> ProbeXMinusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return await ExecuteProbeSequenceAsync(
                axis: 'X',
                direction: -1, // X- moves in negative direction
                coarseDistance: maxDistance,
                fineDistance: 6.0,
                retractAfterCoarse: 2.0,
                retractAfterFine: 1.0,
                finalRetract: 10.0,
                manageSession: manageSession
            );
        }

        /// <summary>
        /// Execute Y+ probe (positive direction)
        /// </summary>
        public async Task<ProbeResult> ProbeYPlusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return await ExecuteProbeSequenceAsync(
                axis: 'Y',
                direction: 1, // Y+ moves in positive direction
                coarseDistance: maxDistance,
                fineDistance: 6.0,
                retractAfterCoarse: 2.0,
                retractAfterFine: 1.0,
                finalRetract: 10.0,
                manageSession: manageSession
            );
        }

        /// <summary>
        /// Execute Y- probe (negative direction)
        /// </summary>
        public async Task<ProbeResult> ProbeYMinusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return await ExecuteProbeSequenceAsync(
                axis: 'Y',
                direction: -1, // Y- moves in negative direction
                coarseDistance: maxDistance,
                fineDistance: 6.0,
                retractAfterCoarse: 2.0,
                retractAfterFine: 1.0,
                finalRetract: 10.0,
                manageSession: manageSession
            );
        }

        #endregion

        #region Core Probe Sequence

        private async Task<bool> EnsureAbsoluteModeAsync()
        {
            if (_controller == null) return false;
            try
            {
                string status = _controller.MachineStatus ?? string.Empty;
                status = status.ToLowerInvariant();

                // Fast alarm clear
                if (status.StartsWith("alarm"))
                {
                    _controller.AddLogMessage("> ⚠️ G90 pre-check: Alarm state detected – sending $X");
                    await _controller.SendGCodeCommandAsync("$X");
                    await Task.Delay(200);
                }

                // Fast hold clear
                if (status.StartsWith("hold"))
                {
                    _controller.AddLogMessage("> ⚠️ G90 pre-check: Hold state detected – soft reset + unlock");
                    try { await _controller.SendControlCharacterAsync('\x18'); } catch { }
                    await Task.Delay(250);
                    await _controller.SendGCodeCommandAsync("$X");
                    await Task.Delay(200);
                }

                // Retry G90 up to3 times, fallback to direct send
                for (int attempt =1; attempt <=3; attempt++)
                {
                    bool ok = await _controller.SendGCodeCommandWithConfirmationAsync("G90");
                    if (!ok)
                    {
                        _controller.AddLogMessage($"> ⚠️ G90 attempt {attempt} failed – retrying");
                        // Fallback direct send on last attempt
                        if (attempt ==3)
                        {
                            _controller.AddLogMessage("> 🔄 G90 direct send fallback");
                            try { ok = await _controller.ForceSendGCodeCommandAsync("G90"); } catch { ok = false; }
                        }
                    }
                    if (ok)
                    {
                        await Task.Delay(120);
                        _controller.AddLogMessage("> ✅ G90 confirmed (absolute mode)");
                        return true;
                    }
                    await Task.Delay(180);
                }
                _controller.AddLogMessage("> ❌ Failed to acquire G90 after retries");
                return false;
            }
            catch (Exception ex)
            {
                _controller.AddLogMessage($"> ❌ EnsureAbsoluteModeAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unified probe sequence for all axes
        /// SEQUENCE:
        /// 1. Switch to G91 (relative mode)
        /// 2. Coarse probe: retract → idle → probe → idle (Z probe style: retract BEFORE probe)
        /// 3. Fine probes (up to 6): retract → idle → probe → idle → wait 400ms → read coordinate
        /// 4. Validate fine measurements (find pair with difference < threshold)
        /// 5. Final retract and return result
        /// </summary>
        private async Task<ProbeResult> ExecuteProbeSequenceAsync(
            char axis,
            int direction,
            double coarseDistance,
            double fineDistance,
            double retractAfterCoarse,
            double retractAfterFine,
            double finalRetract,
            bool manageSession = true) // NEW: allow caller to manage session
        {
            IDisposable fastScope = null;
            bool sessionStartedHere = false;
            try
            {
                _controller?.AddLogMessage($"> 🔧 {axis} Probe başlatılıyor (direction: {(direction >0 ? "+" : "-")})...");

                // BEGIN PROBE SESSION only if we're managing it (standalone probe)
                // For multi-probe sequences (Center XY), caller manages the session
                if (manageSession && !RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStartedHere = true;
                }

                // Enable fast central status updates (200ms) during probing
                fastScope = _controller?.BeginScopedCentralStatusOverride(200);

                if (_controller?.IsConnected != true)
                {
                    return ProbeResult.Failed($"{axis} probe failed: Not connected");
                }

                // Robust absolute mode acquisition
                if (!await EnsureAbsoluteModeAsync())
                {
                    return ProbeResult.Failed($"{axis} probe failed: Could not set G90 (robust acquisition)");
                }

                // Switch to relative mode (G91) with retry once
                if (!await SendCommandAsync("G91"))
                {
                    _controller?.AddLogMessage("> ⚠️ G91 first attempt failed – retrying");
                    await Task.Delay(150);
                    if (!await SendCommandAsync("G91"))
                    {
                        return ProbeResult.Failed($"{axis} probe failed: Could not set G91");
                    }
                }
                await Task.Delay(120); // Let controller process mode change
                _controller?.AddLogMessage("> ⚙️ G91 aktif (Rölatif mod)");

                // Get axis rapid speed and calculate feeds
                double axisRapid = GetAxisRapid(axis);
                int coarseFeed = ClampProbeFeed((int)(axisRapid /8.0));
                int fineFeed = ClampProbeFeed((int)(axisRapid /15.0));

                _controller?.AddLogMessage($"> 🚀 {axis} rapid: {axisRapid:F0} mm/min");
                _controller?.AddLogMessage($"> 📏 Coarse feed: {coarseFeed} mm/min (rapid/8)");
                _controller?.AddLogMessage($"> 📏 Fine feed: {fineFeed} mm/min (rapid/15)");

                // ===== COARSE PROBE =====
                if (!await ExecuteCoarseProbeAsync(axis, direction, coarseDistance, retractAfterCoarse, coarseFeed))
                {
                    await SendCommandAsync("G90");
                    return ProbeResult.Failed($"{axis} coarse probe failed");
                }

                // ===== FINE PROBES WITH VALIDATION =====
                var fineResult = await ExecuteFineProbesAsync(
                    axis, direction, fineDistance, retractAfterFine, fineFeed);

                if (!fineResult.Success)
                {
                    await SendCommandAsync("G90");
                    return fineResult;
                }

                // ===== FINAL RETRACT =====
                double retractDirection = -direction; // Retract in opposite direction (away from contact)
                // Use rapid move for final retract to avoid slow feed-limited motion
                string retractCmd = FormatMoveCommand(axis, retractDirection * finalRetract, rapid: true);
                
                _controller?.AddLogMessage($"> 🔼 Final retract (rapid): {finalRetract:F1}mm");
                if (!await SendCommandAsync(retractCmd))
                {
                    await SendCommandAsync("G90");
                    return ProbeResult.Failed($"{axis} final retract failed");
                }

                int retractTimeout = EstimateTimeoutMsForRapid(finalRetract, axisRapid);
                if (!await WaitForIdleAsync(retractTimeout, $"{axis}Probe_FinalRetract"))
                {
                    await SendCommandAsync("G90");
                    return ProbeResult.Failed($"{axis} final retract - idle timeout");
                }

                // Switch back to absolute mode (G90)
                await SendCommandAsync("G90");
                _controller?.AddLogMessage("> ⚙️ G90 aktif (Absolut mod)");

                _controller?.AddLogMessage($"> ✅ {axis} Probe tamamlandı: {fineResult.ContactPosition:F3} mm (tolerance: {fineResult.Tolerance:F3} mm)");
                return fineResult;
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ {axis} Probe HATA: {ex.Message}");
                try { await SendCommandAsync("G90"); } catch { }
                return ProbeResult.Failed($"{axis} probe exception: {ex.Message}");
            }
            finally
            {
                // END PROBE SESSION only if we started it here
                if (sessionStartedHere)
                {
                    RunUiLocker.EndProbeSession();
                }
 
     // Restore normal central status interval
      fastScope?.Dispose();
    }
  }

        #endregion

        #region Coarse Probe

        private async Task<bool> ExecuteCoarseProbeAsync(
            char axis, int direction, double distance, double retract, int feed)
        {
                // 1. Initial retract BEFORE coarse probe (Z probe style)
                _controller?.AddLogMessage($"> 🔼 Coarse: {retract:F1}mm initial retract");
                string initialRetractCmd = FormatMoveCommand(axis, -direction * retract, rapid: true);
                if (!await SendCommandAsync(initialRetractCmd))
                {
                    _controller?.AddLogMessage("> ❌ Coarse initial retract failed");
                    return false;
                }

                // 2. Wait for idle after initial retract
                if (!await WaitForIdleAsync(15000, $"{axis}Coarse_InitialRetract"))
                {
                    _controller?.AddLogMessage("> ❌ Idle timeout (coarse initial retract)");
                    return false;
                }

                // 3. Coarse probe
                string probeCmd = FormatProbeCommand(axis, direction * distance, feed);
            _controller?.AddLogMessage($"> 🔍 Coarse probe: {probeCmd}");
            if (!await SendCommandAsync(probeCmd))
            {
                _controller?.AddLogMessage("> ❌ Coarse probe command failed");
                return false;
            }

                // 4. Wait for idle (probe completion)
            if (!await WaitForIdleAsync(45000, $"{axis}Coarse_Probe"))
            {
                _controller?.AddLogMessage("> ❌ Idle timeout (coarse probe)");
                return false;
            }

            _controller?.AddLogMessage("> ✅ Coarse probe completed");
            return true;
        }

        #endregion

        #region Fine Probes with Validation

        private async Task<ProbeResult> ExecuteFineProbesAsync(
            char axis, int direction, double distance, double retract, int feed)
        {
            var fineContacts = new List<double>(MaxFineAttempts);
            bool validated = false;
            double avgUsed = 0.0;
            double tolerance = 0.0;
            int usedA = -1, usedB = -1;

            for (int i = 0; i < MaxFineAttempts; i++)
            {
                int stepIndex = i + 1;

                    // A. Retract before fine probe (Z probe style - always retract first)
                    _controller?.AddLogMessage($"> 🔼 Fine#{stepIndex}: {retract:F1}mm retract");
                    string retractCmd = FormatMoveCommand(axis, -direction * retract, rapid: true);
                    if (!await SendCommandAsync(retractCmd))
                    {
                        _controller?.AddLogMessage($"> ❌ Fine#{stepIndex} retract failed");
                        return ProbeResult.Failed($"Fine#{stepIndex} retract failed");
                    }

                    // B. Wait for idle after retract
                    if (!await WaitForIdleAsync(15000, $"{axis}Fine{stepIndex}_Retract"))
                    {
                        _controller?.AddLogMessage($"> ❌ Fine#{stepIndex} retract idle timeout");
                        return ProbeResult.Failed($"Fine#{stepIndex} retract idle timeout");
                    }

                    // C. Fine probe
                DateTime fineStartTs = DateTime.UtcNow;
                string probeCmd = FormatProbeCommand(axis, direction * distance, feed);
                _controller?.AddLogMessage($"> 🎯 Fine#{stepIndex} probe: {probeCmd}");
                if (!await SendCommandAsync(probeCmd))
                {
                    _controller?.AddLogMessage($"> ❌ Fine#{stepIndex} probe command failed");
                    return ProbeResult.Failed($"Fine#{stepIndex} probe command failed");
                }

                    // D. Wait for idle (probe completion)
                if (!await WaitForIdleAsync(30000, $"{axis}Fine{stepIndex}_Probe"))
                {
                    _controller?.AddLogMessage($"> ❌ Fine#{stepIndex} probe idle timeout");
                    return ProbeResult.Failed($"Fine#{stepIndex} probe idle timeout");
                }

                    // E. Wait 400ms before reading (Z probe does this)
                    await Task.Delay(400);

                    // F. Read contact coordinate
                var (ok, contactValue, fromPrb) = await ReadContactCoordinateAsync(axis, fineStartTs, 800);
                if (!ok || !IsFinite(contactValue))
                {
                    _controller?.AddLogMessage($"> ❌ Fine#{stepIndex} coordinate read failed");
                    return ProbeResult.Failed($"Fine#{stepIndex} coordinate read failed");
                }

                // Prefer WORK coordinates for X/Y; keep previous behavior for Z
                double toRecord = contactValue;
                if (axis == 'X' || axis == 'Y')
                {
                    var st = _controller?.MStatus;
                    if (st != null)
                    {
                        double w = axis == 'X' ? st.WorkX : st.WorkY;
                        if (!double.IsNaN(w) && !double.IsInfinity(w))
                            toRecord = w;
                    }
                }

                fineContacts.Add(toRecord);
                string src = (axis == 'X' || axis == 'Y') ? "Work" : (fromPrb ? "PRB" : "MPos");
                _controller?.AddLogMessage($"> 📍 Fine#{stepIndex} contact {axis} = {toRecord:F3} mm (source: {src})");

                    // G. Validation check (from 2nd measurement onwards)
                if (fineContacts.Count >= 2)
                {
                    // Find the pair with smallest difference
                    double minDiff = double.MaxValue;
                    int bestI = -1, bestJ = -1;
                    for (int a = 0; a < fineContacts.Count - 1; a++)
                    {
                        for (int b = a + 1; b < fineContacts.Count; b++)
                        {
                            double diff = Math.Abs(fineContacts[a] - fineContacts[b]);
                            if (diff < minDiff)
                            {
                                minDiff = diff;
                                bestI = a;
                                bestJ = b;
                            }
                        }
                    }

                    if (minDiff < FineToleranceThreshold)
                    {
                        usedA = bestI;
                        usedB = bestJ;
                        avgUsed = (fineContacts[bestI] + fineContacts[bestJ]) / 2.0;
                        tolerance = Math.Abs(fineContacts[bestI] - fineContacts[bestJ]);
                        validated = true;
                        _controller?.AddLogMessage($"> ✅ Validation OK: Fine#{bestI + 1} and Fine#{bestJ + 1}, diff={tolerance:F3}mm < {FineToleranceThreshold:F3}mm, avg={avgUsed:F3}mm");
                        break; // Early exit - validation successful
                    }
                    else
                    {
                        _controller?.AddLogMessage($"> ℹ️ No validation yet: min diff={minDiff:F3}mm (threshold {FineToleranceThreshold:F3}mm)");
                    }
                }
            }

            // Check if validation succeeded
            if (!validated)
            {
                string measurements = string.Join(", ", fineContacts.Select((v, idx) => $"Fine#{idx + 1}:{v:F3}"));
                _controller?.AddLogMessage($"> ❌ Validation failed after {MaxFineAttempts} attempts: {measurements}");
                return ProbeResult.Failed(
                    $"Validation failed. Measurements: {measurements}",
                    fineContacts
                );
            }

            return ProbeResult.CreateSuccess(avgUsed, tolerance, fineContacts, usedA, usedB);
        }

        #endregion

        #region Helper Methods - Command Formatting

        private string FormatMoveCommand(char axis, double distance, bool rapid = false)
        {
            string gCode = rapid ? "G00" : "G01";
            string distStr = distance.ToString("F3", CultureInfo.InvariantCulture);
            return $"{gCode} {axis}{distStr}";
        }

        private string FormatProbeCommand(char axis, double distance, int feed)
        {
            string distStr = distance.ToString("F3", CultureInfo.InvariantCulture);
            return $"G38.2 {axis}{distStr} F{feed}";
        }

        private static int ClampProbeFeed(int feed)
        {
            if (feed < 1) return 1;
            return feed > MaxProbeFeed ? MaxProbeFeed : feed;
        }

        #endregion

        #region Helper Methods - Coordinate Reading

        /// <summary>
        /// Read contact coordinate for specific axis after probe
        /// First tries PRB data, falls back to current machine position
        /// </summary>
        private async Task<(bool ok, double value, bool fromPrb)> ReadContactCoordinateAsync(
            char axis, DateTime sinceUtc, int waitPrbMs = 800)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(waitPrbMs);

            // Try to get PRB data
            while (DateTime.UtcNow < deadline)
            {
                if (ProbeContactCache.TryGetAfter(sinceUtc, out double prbX, out double prbY, out double prbZ, out _))
                {
                    double prbValue;
                    if (axis == 'X')
                        prbValue = prbX;
                    else if (axis == 'Y')
                        prbValue = prbY;
                    else if (axis == 'Z')
                        prbValue = prbZ;
                    else
                        prbValue = double.NaN;
                        
                    return (true, prbValue, true);
                }
                await Task.Delay(50);
            }

            // Fallback: request status update and read machine position
            await StatusQueryService.QueryStatusOnce(200);
            await Task.Delay(150);

            var mStatus = _controller?.MStatus;
            double value;
            if (axis == 'X')
                value = mStatus?.X ?? double.NaN;
            else if (axis == 'Y')
                value = mStatus?.Y ?? double.NaN;
            else if (axis == 'Z')
                value = mStatus?.Z ?? double.NaN;
            else
                value = double.NaN;

            return (true, value, false);
        }

        #endregion

        #region Helper Methods - Axis Configuration

        private double GetAxisRapid(char axis)
        {
            int settingId;
            if (axis == 'X')
                settingId = 110; // $110 X-axis maximum rate
            else if (axis == 'Y')
                settingId = 111; // $111 Y-axis maximum rate
            else if (axis == 'Z')
                settingId = 112; // $112 Z-axis maximum rate
            else
                settingId = 110;

            double defaultRapid = 1000.0;
            try
            {
                var setting = _controller?.Settings?.FirstOrDefault(s => s.Id == settingId);
                if (setting != null && double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                {
                    return Math.Max(1.0, value);
                }
            }
            catch { }

            return defaultRapid;
        }

        #endregion

        #region Helper Methods - Timing and Waiting

        private async Task<bool> WaitForIdleAsync(int timeoutMs, string context)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            bool seenNonIdle = false;
            int consecutiveIdleCount = 0;
  const int requiredIdleCount = 2; // Require 2 consecutive Idle readings for stability

    while (DateTime.UtcNow < deadline)
 {
  string state = _controller?.MachineStatus ?? string.Empty;

        // Check for Idle state
        if (state.Equals("Idle", StringComparison.OrdinalIgnoreCase))
 {
            consecutiveIdleCount++;
            
            // For probe commands: accept immediate Idle (contact can be instant)
            // For other commands: accept Idle after seeing non-Idle OR after 2 consecutive Idles
        if (context.Contains("Probe") || seenNonIdle || consecutiveIdleCount >= requiredIdleCount)
         {
_controller?.AddLogMessage($"> ✅ Idle confirmed in {context} (consecutive: {consecutiveIdleCount})");
     return true;
            }
        }
  else
        {
            consecutiveIdleCount = 0; // Reset count if not Idle
            
            if (state.StartsWith("Run", StringComparison.OrdinalIgnoreCase) ||
       state.StartsWith("Jog", StringComparison.OrdinalIgnoreCase))
        {
       seenNonIdle = true;
            }
            else if (state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase))
          {
  _controller?.AddLogMessage($"> ⚠️ ALARM detected during {context}");
    return false;
        }
        }

   await Task.Delay(50);
 }

    _controller?.AddLogMessage($"> ⚠️ Idle timeout in {context} (last state: {_controller?.MachineStatus})");
    return false;
        }

        private int EstimateTimeoutMsForRapid(double distanceMm, double rapidMmMin, int minMs = 5000)
        {
            double feed = Math.Max(1.0, rapidMmMin);
            double minutes = Math.Abs(distanceMm) / feed;
            int ms = (int)(minutes * 60000.0);
            ms = (int)(ms * 1.8) + 2000; // 1.8x slack + 2s overhead
            return Math.Max(ms, minMs);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        #endregion

        #region Helper Methods - Command Sending

        private async Task<bool> SendCommandAsync(string command)
        {
            if (_controller == null) return false;
            return await _controller.SendGCodeCommandWithConfirmationAsync(command);
        }

        #endregion
    }

    #region Probe Result Class

    /// <summary>
    /// Result of a probe operation
    /// </summary>
    public class ProbeResult
    {
        public bool Success { get; set; }
        public double ContactPosition { get; set; }
        public double Tolerance { get; set; }
        public List<double> FineReadings { get; set; }
        public int UsedIndexA { get; set; }
        public int UsedIndexB { get; set; }
        public string ErrorMessage { get; set; }

        public static ProbeResult CreateSuccess(double position, double tolerance, List<double> readings, int idxA, int idxB)
        {
            return new ProbeResult
            {
                Success = true,
                ContactPosition = position,
                Tolerance = tolerance,
                FineReadings = readings,
                UsedIndexA = idxA,
                UsedIndexB = idxB
            };
        }

        public static ProbeResult Failed(string error, List<double> readings = null)
        {
            return new ProbeResult
            {
                Success = false,
                ErrorMessage = error,
                ContactPosition = double.NaN,
                FineReadings = readings
            };
        }
    }

    #endregion
}
