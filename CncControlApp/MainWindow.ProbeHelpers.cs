using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;

namespace CncControlApp
{
    public partial class MainWindow
    {
        // Helpers: estimate a safe timeout for feed/rapid moves
        private int EstimateTimeoutMsForFeed(double distanceMm, int feedMmMin, int minMs =8000)
        {
            if (feedMmMin <1) feedMmMin =1;
            double minutes = Math.Abs(distanceMm) / feedMmMin; // minutes = mm / (mm/min)
            int ms = (int)(minutes *60000.0); // expected duration
            ms = (int)(ms *2.0) +3000; //2x slack +3s overhead
            return Math.Max(ms, minMs);
        }

        private int EstimateTimeoutMsForRapid(double distanceMm, double rapidMmMin, int minMs =5000)
        {
            double feed = Math.Max(1.0, rapidMmMin);
            double minutes = Math.Abs(distanceMm) / feed;
            int ms = (int)(minutes *60000.0);
            ms = (int)(ms *1.8) +2000; //1.8x slack +2s overhead
            return Math.Max(ms, minMs);
        }

        // Center X Outer - Find X center from outer edges using Z-drop detection
        private async void CenterXOuter_Click(object sender, RoutedEventArgs e)
        {
            await CenterXOuterSequenceAsync();
        }

        // Center Y Outer - Find Y center from outer edges using Z-drop detection
        private async void CenterYOuter_Click(object sender, RoutedEventArgs e)
        {
            await CenterYOuterSequenceAsync();
        }

        // Orchestrate XY center (outer): run X center fully, then Y center fully
        private async void CenterXYOuter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> 🔧 Center XY (Outer edges) starting — X then Y");

                bool okX = await CenterXOuterSequenceAsync();
                if (!okX)
                {
                    App.MainController?.AddLogMessage("> ❌ Center XY: X center failed, aborting");
                    return;
                }

                bool okY = await CenterYOuterSequenceAsync();
                if (!okY)
                {
                    App.MainController?.AddLogMessage("> ❌ Center XY: Y center failed");
                    return;
                }

                App.MainController?.AddLogMessage("> ✅ Center XY (Outer edges) completed");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterXYOuter_Click error: {ex.Message}");
            }
        }

        // === Full X outer-center sequence (extracted from CenterXOuter_Click) ===
        private async Task<bool> CenterXOuterSequenceAsync()
        {
            Controls.StreamingPopup streamPopup = null;
            try
            {
                var mc = App.MainController;

                if (mc?.IsConnected != true)
                {
                    mc?.AddLogMessage("> ❌ CNC not connected");
                    return false;
                }

                // BEGIN PROBE SESSION for entire X center sequence (locks UI until complete)
                RunUiLocker.BeginProbeSession();

                // Capture initial Work X for returning later
                double startWorkX = mc.MStatus?.WorkX ??0.0;

                // Create and show streaming popup
                streamPopup = new Controls.StreamingPopup { Owner = this };
                streamPopup.SetTitle("Center X Outer - Edge Detection");
                streamPopup.SetSubtitle("> Finding X center from outer edges using Z-drop detection\n> Real-time coordinates and progress shown below:");
                streamPopup.Show();

                mc?.AddLogMessage("> 🔧 Center X Outer sequence starting...");
                streamPopup.Append("> 🔧 Center X Outer sequence starting...");

                var probeManager = new Services.ProbeManager(mc);

                // Phase1: Initial Z probe at center
                mc?.AddLogMessage("> 📍 Phase1: Z probe at center position...");
                streamPopup.Append("> 📍 Phase1: Z probe at center position...");

                var zProbeResult = await probeManager.ProbeZAsync(manageSession: false); // Don't manage session internally

                if (!zProbeResult.Success)
                {
                    mc?.AddLogMessage($"> ❌ Initial Z probe failed: {zProbeResult.ErrorMessage}");
                    streamPopup.Append($"> ❌ Initial Z probe failed: {zProbeResult.ErrorMessage}");
                    return false;
                }

                mc?.AddLogMessage("> ✅ Z probe successful. Retracted to +10mm");
                streamPopup.Append("> ✅ Z probe successful. Retracted to +10mm");

                // Phase2: Set local Z coordinate to +10
                mc?.AddLogMessage("> 🔄 Phase2: Setting Z=+10 at current position...");
                streamPopup.Append("> 🔄 Phase2: Setting Z=+10 at current position...");

                if (!await mc.SendGCodeCommandWithConfirmationAsync("G10 L20 P0 Z10"))
                {
                    mc?.AddLogMessage("> ❌ Failed to set Z=+10");
                    streamPopup.Append("> ❌ Failed to set Z=+10");
                    return false;
                }

                await Task.Delay(500); // Give time for coordinate update
                mc?.AddLogMessage("> ✅ Local Z set to +10mm");
                streamPopup.Append("> ✅ Local Z set to +10mm");

                // Determine Z coarse feed from $112 (rapid/8), capped to300, with safe fallback
                double zRapid =1000.0;
                try
                {
                    var zSetting = mc?.Settings?.FirstOrDefault(s => s.Id ==112);
                    if (zSetting != null && double.TryParse(zSetting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double zVal))
                    {
                        zRapid = Math.Max(1.0, zVal);
                    }
                }
                catch { }
                int zCoarseFeed = (int)Math.Max(1.0, zRapid /8.0);
                if (zCoarseFeed >300) zCoarseFeed =300;
                if (zCoarseFeed <1) zCoarseFeed =100; // explicit fallback
                streamPopup.Append($"> ⚙️ Z probe feed set to F{zCoarseFeed} (from rapid {zRapid:F0}/8)");

                // Phase3 &4: Loop - Move -X, probe, check WorkZ
                bool edgeFound = false;
                int moveCount =0;
                const int MAX_MOVES =10; // Safety limit

                while (!edgeFound && moveCount < MAX_MOVES)
                {
                    moveCount++;

                    // Phase3: Move -20mm in X
                    mc?.AddLogMessage($"> 🔄 Move {moveCount}: Moving -20mm in X direction...");
                    streamPopup.Append($"> 🔄 Move {moveCount}: Moving -20mm in X direction...");

                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G91"))
                    {
                        mc?.AddLogMessage("> ❌ Failed to set G91");
                        streamPopup.Append("> ❌ Failed to set G91");
                        break;
                    }

                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G00 X-20.000"))
                    {
                        mc?.AddLogMessage("> ❌ Failed to move -X");
                        streamPopup.Append("> ❌ Failed to move -X");
                        break;
                    }

                    if (!await WaitForIdleAsync(5000, $"CenterXOuter_Move{moveCount}"))
                    {
                        mc?.AddLogMessage("> ❌ Idle timeout after X move");
                        streamPopup.Append("> ❌ Idle timeout after X move");
                        break;
                    }

                    // Show current coordinates after move
                    double currentX = mc.MStatus?.X ??0.0;
                    double currentWorkX = mc.MStatus?.WorkX ??0.0;
                    double currentY = mc.MStatus?.Y ??0.0;
                    double currentWorkY = mc.MStatus?.WorkY ??0.0;
                    streamPopup.Append($"> Machine X={currentX:F3}, Work X={currentWorkX:F3}");
                    // Also reflect a live line snapshot including X/Y here
                    streamPopup.SetLiveLine($"Live: MX={currentX:F3}, WX={currentWorkX:F3} | MY={currentY:F3}, WY={currentWorkY:F3} | MZ={(mc.MStatus?.Z ??0.0):F3}, WZ={(mc.MStatus?.WorkZ ??0.0):F3} | Status={(mc?.MachineStatus ?? string.Empty).ToLowerInvariant()}");

                    // Phase4: Probe Z and check local Z
                    mc?.AddLogMessage($"> 📍 Probing Z at position {moveCount}...");
                    streamPopup.Append($"> 📍 Probing Z at position {moveCount}...");

                    // Send probe command manually to monitor during motion
                    bool probeSuccess = false;
                    bool edgeDetected = false;

                    // Ensure relative mode before probe
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");

                    // Start probe command (non-blocking) using coarse Z feed
                    string zProbeCmd = string.Format(CultureInfo.InvariantCulture, "G38.2 Z-20 F{0}", zCoarseFeed);
                    streamPopup.Append($"> ▶ Z probe cmd: {zProbeCmd}");
                    await mc.SendGCodeCommandAsync(zProbeCmd);

                    // Monitor WorkZ during probe motion
                    var monitorStart = DateTime.Now;
                    bool seenRunning = false; // only accept Idle completion after we've seen a non-idle state
                    while ((DateTime.Now - monitorStart).TotalSeconds <10) //10 second timeout
                    {
                        await Task.Delay(50); // Check every50ms

                        var machineStatus = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                        double workZ = mc.MStatus?.WorkZ ??0.0;
                        double machineZ = mc.MStatus?.Z ??0.0;
                        double liveX = mc.MStatus?.X ??0.0;
                        double liveWX = mc.MStatus?.WorkX ??0.0;
                        double liveY = mc.MStatus?.Y ??0.0;
                        double liveWY = mc.MStatus?.WorkY ??0.0;

                        // Update live position in popup (now includes X/Y as well)
                        streamPopup.SetLiveLine($"Live: MX={liveX:F3}, WX={liveWX:F3} | MY={liveY:F3}, WY={liveWY:F3} | MZ={machineZ:F3}, WZ={workZ:F3} | Status={machineStatus}");

                        // Mark that motion started when controller leaves Idle
                        if (machineStatus.StartsWith("run") || machineStatus.StartsWith("jog"))
                        {
                            seenRunning = true;
                        }

                        // Check if WorkZ dropped below -2mm without touch
                        if (workZ <= -2.0 && !machineStatus.Contains("idle"))
                        {
                            // Edge detected - stop probe
                            mc?.AddLogMessage($"> 🎯 Edge detected! WorkZ={workZ:F3}mm <= -2.0mm during probe");
                            streamPopup.Append($"> 🎯 Edge detected! WorkZ={workZ:F3}mm <= -2.0mm during probe");

                            // Send reset to stop motion (skip EmergencyResetAsync - too slow)
                            await mc.SendControlCharacterAsync('\x18'); // Ctrl+X soft reset
                            await Task.Delay(200); // Quick settle

                            // Clear alarm immediately
                            var status = mc?.MachineStatus?.ToLowerInvariant() ?? "";
                            if (status.Contains("alarm"))
                            {
                                await mc.SendGCodeCommandAsync("$X");
                                await Task.Delay(150); // Minimal delay
                            }

                            edgeDetected = true;
                            edgeFound = true;
                            break;
                        }

                        // Check if probe completed (idle after having been running)
                        if (machineStatus.Contains("idle") && seenRunning)
                        {
                            probeSuccess = true;
                            mc?.AddLogMessage($"> ✅ Probe touched at WorkZ={workZ:F3}mm");
                            streamPopup.Append($"> ✅ Probe touched, retracted to WorkZ={workZ:F3}");
                            break;
                        }

                        if (machineStatus.Contains("alarm"))
                        {
                            // Probe failed - no surface found
                            mc?.AddLogMessage("> ⚠️ Probe failed - no surface found (edge detected)");
                            streamPopup.Append("> ⚠️ Probe failed - no surface found (edge detected)");

                            mc?.AddLogMessage("> 🔓 Clearing alarm...");
                            streamPopup.Append("> 🔓 Clearing alarm...");
                            await mc.SendGCodeCommandAsync("$X");
                            await Task.Delay(500);
                            mc?.AddLogMessage("> ✅ Alarm cleared");
                            streamPopup.Append("> ✅ Alarm cleared");

                            edgeDetected = true;
                            edgeFound = true;
                            break;
                        }
                    }

                    if (edgeDetected)
                    {
                        mc?.AddLogMessage("> 🎯 Edge found - probe sequence complete");
                        streamPopup.Append("> 🎯 Edge found - probe sequence complete");
                        break;
                    }

                    if (!probeSuccess)
                    {
                        mc?.AddLogMessage("> ❌ Probe timeout");
                        streamPopup.Append("> ❌ Probe timeout");
                        break;
                    }

                    // Retract after successful probe
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync("G00 Z10");
                    await WaitForIdleAsync(3000, "ProbeRetract");

                    double finalWorkZ = mc.MStatus?.WorkZ ??0.0;
                    streamPopup.Append($"> After retract: Work Z={finalWorkZ:F3}");
                    mc?.AddLogMessage($"> Still on surface, continuing...");
                    streamPopup.Append($"> Still on surface, continuing...");
                }

                if (!edgeFound && moveCount >= MAX_MOVES)
                {
                    mc?.AddLogMessage($"> ⚠️ Max moves ({MAX_MOVES}) reached without finding edge");
                    streamPopup.Append($"> ⚠️ Max moves ({MAX_MOVES}) reached without finding edge");
                }

                // === After left-side edge sequence: run X+ probe, record edgeX1, then prepare for right side ===
                double edgeX1 = double.NaN;
                try
                {
                    // Ensure not in HOLD
                    if (mc != null && mc.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        streamPopup.Append("> ⏳ Hold detected – releasing...");
                        await mc.SendControlCharacterAsync('\x18'); // Soft reset
                        await Task.Delay(200);
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Ensure not in ALARM
                    if (mc != null && mc.IsAlarmState())
                    {
                        streamPopup.Append("> ⏳ Alarm detected – clearing...");
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Quick idle check (reduced from 8000ms to 2000ms)
                    await WaitForIdleAsync(2000, "Prepare_XPlus");

                    streamPopup.Append("> ▶ Starting X+ probe (same as X+ button), from current Z...");
                    mc?.AddLogMessage("> ▶ Starting X+ probe after center sequence...");
                     var xPlusRes = await probeManager.ProbeXPlusAsync(30.0, manageSession: false); // Don't manage session - parent handles it
                      if (!xPlusRes.Success)
                    {
                        streamPopup.Append($"> ❌ X+ probe failed: {xPlusRes.ErrorMessage}");
                        mc?.AddLogMessage($"> ❌ X+ probe failed: {xPlusRes.ErrorMessage}");
                        return false;
                    }
                    edgeX1 = xPlusRes.ContactPosition;
                    streamPopup.Append($"> ✅ X+ probe completed. edgeX1 = {edgeX1:F3}");
                }
                catch (Exception xpEx)
                {
                    mc?.AddLogMessage($"> ❌ X+ probe error: {xpEx.Message}");
                    streamPopup.Append($"> ❌ X+ probe error: {xpEx.Message}");
                    return false;
                }

                // Retract Z to WorkZ = +10mm (move, not set)
                try
                {
                    double wzNow = mc.MStatus?.WorkZ ??0.0;
                    double dz =10.0 - wzNow;
                    string dzText = dz.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔼 Retracting to Work Z=+10 (ΔZ={dzText})...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 Z{dzText}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dz),1000), "Retract_To_WZ10");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // Return to initial starting X (work coordinates)
                try
                {
                    double nowWorkX = mc.MStatus?.WorkX ??0.0;
                    double dxBack = startWorkX - nowWorkX;
                    string dxBackText = dxBack.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔁 Returning to start WorkX (ΔX={dxBackText})...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 X{dxBackText}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dxBack),1000), "Return_To_StartX");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // === Right-side sequence: move +X steps, probe Z, detect edge; loop handles +20 per step ===
                bool edgeFoundRight = false;
                int moveCountRight =0;
                double edgeX2 = double.NaN;
                while (!edgeFoundRight && moveCountRight < MAX_MOVES)
                {
                    moveCountRight++;
                    mc?.AddLogMessage($"> 🔄 [Right] Move {moveCountRight}: Moving +20mm in X direction...");
                    streamPopup.Append($"> 🔄 [Right] Move {moveCountRight}: Moving +20mm in X direction...");

                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G91"))
                    {
                        mc?.AddLogMessage("> ❌ Failed to set G91");
                        streamPopup.Append("> ❌ Failed to set G91");
                        break;
                    }
                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G00 X20.000"))
                    {
                        mc?.AddLogMessage("> ❌ Failed to move +X");
                        streamPopup.Append("> ❌ Failed to move +X");
                        break;
                    }
                    if (!await WaitForIdleAsync(5000, $"CenterXOuter_Right_Move{moveCountRight}"))
                    {
                        mc?.AddLogMessage("> ❌ Idle timeout after +X move");
                        streamPopup.Append("> ❌ Idle timeout after +X move");
                        break;
                    }

                    // Snapshot after move
                    double rX = mc.MStatus?.X ??0.0;
                    double rWX = mc.MStatus?.WorkX ??0.0;
                    double rY = mc.MStatus?.Y ??0.0;
                    double rWY = mc.MStatus?.WorkY ??0.0;
                    streamPopup.Append($"> [Right] Machine X={rX:F3}, Work X={rWX:F3}");
                    streamPopup.SetLiveLine($"Live: MX={rX:F3}, WX={rWX:F3} | MY={rY:F3}, WY={rWY:F3} | MZ={(mc.MStatus?.Z ??0.0):F3}, WZ={(mc.MStatus?.WorkZ ??0.0):F3} | Status={(mc?.MachineStatus ?? string.Empty).ToLowerInvariant()}");

                    // Probe Z
                    mc?.AddLogMessage($"> 📍 [Right] Probing Z at position {moveCountRight}...");
                    streamPopup.Append($"> 📍 [Right] Probing Z at position {moveCountRight}...");

                    // Ensure relative mode before probe
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");

                    string zProbeCmdR = string.Format(CultureInfo.InvariantCulture, "G38.2 Z-20 F{0}", zCoarseFeed);
                    streamPopup.Append($"> ▶ Z probe cmd: {zProbeCmdR}");
                    await mc.SendGCodeCommandAsync(zProbeCmdR);

                    var monitorStartR = DateTime.Now;
                    bool seenRunningR = false;
                    bool edgeDetectedR = false;
                    bool probeSuccessR = false;
                    while ((DateTime.Now - monitorStartR).TotalSeconds <10)
                    {
                        await Task.Delay(50);
                        var st = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                        double wZ = mc.MStatus?.WorkZ ??0.0;
                        double mZ = mc.MStatus?.Z ??0.0;
                        double lx = mc.MStatus?.X ??0.0;
                        double lwx = mc.MStatus?.WorkX ??0.0;
                        double ly = mc.MStatus?.Y ??0.0;
                        double lwy = mc.MStatus?.WorkY ??0.0;
                        streamPopup.SetLiveLine($"Live: MX={lx:F3}, WX={lwx:F3} | MY={ly:F3}, WY={lwy:F3} | MZ={mZ:F3}, WZ={wZ:F3} | Status={st}");

                        if (st.StartsWith("run") || st.StartsWith("jog")) seenRunningR = true;

                        if (wZ <= -2.0 && !st.Contains("idle"))
                        {
                            mc?.AddLogMessage($"> 🎯 [Right] Edge detected! WorkZ={wZ:F3}mm <= -2.0mm during probe");
                            streamPopup.Append($"> 🎯 [Right] Edge detected! WorkZ={wZ:F3}mm <= -2.0mm during probe");
    
                            // Fast reset instead of EmergencyResetAsync
                            await mc.SendControlCharacterAsync('\x18'); // Ctrl+X soft reset
                            await Task.Delay(200);
    
                            var st2 = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                            if (st2.Contains("alarm"))
                            {
                                await mc.SendGCodeCommandAsync("$X");
                                await Task.Delay(150);
                            }
 
                           edgeDetectedR = true;
      edgeFoundRight = true;
    break;
                        }
                        if (st.Contains("idle") && seenRunningR)
                        {
                            probeSuccessR = true;
                            streamPopup.Append($"> ✅ [Right] Probe touched, retracted to WorkZ={wZ:F3}");
                            break;
                        }
                        if (st.Contains("alarm"))
                        {
                            streamPopup.Append("> ⚠️ [Right] Probe failed - no surface found");
                            streamPopup.Append("> 🔓 Clearing alarm...");
                            await mc.SendGCodeCommandAsync("$X");
                            await Task.Delay(500);
                            streamPopup.Append("> ✅ Alarm cleared");
                            edgeDetectedR = true; // edge via failure
                            edgeFoundRight = true;
                            break;
                        }
                    }

                    if (edgeDetectedR)
                    {
                        streamPopup.Append("> 🎯 [Right] Edge found - probe sequence complete");
                        break;
                    }
                    if (!probeSuccessR)
                    {
                        streamPopup.Append("> ❌ [Right] Probe timeout");
                        break;
                    }

                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync("G00 Z10");
                    await WaitForIdleAsync(3000, "Right_ProbeRetract");
                }

                // Run X- probe and record edgeX2
                try
                {
                  // Fast state check (reduced idle wait)
      await WaitForIdleAsync(2000, "Prepare_XMinus");
  streamPopup.Append("> ▶ Starting X- probe (right side), from current Z...");
     var xMinusRes = await probeManager.ProbeXMinusAsync(30.0, manageSession: false); // Don't manage session - parent handles it
 if (!xMinusRes.Success)
                    {
                        streamPopup.Append($"> ❌ X- probe failed: {xMinusRes.ErrorMessage}");
                        mc?.AddLogMessage($"> ❌ X- probe failed: {xMinusRes.ErrorMessage}");
                        return false;
                    }
                    edgeX2 = xMinusRes.ContactPosition;
                    streamPopup.Append($"> ✅ X- probe completed. edgeX2 = {edgeX2:F3}");
                }
                catch (Exception xmEx)
                {
                    streamPopup.Append($"> ❌ X- probe error: {xmEx.Message}");
                }

                // Retract Z to WorkZ = +10mm before moving to center
                try
                {
                    double wzNow2 = mc.MStatus?.WorkZ ??0.0;
                    double dz2 =10.0 - wzNow2;
                    string dzText2 = dz2.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔼 Retracting to Work Z=+10 (ΔZ={dzText2}) before center move...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 Z{dzText2}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dz2),1000), "Retract_To_WZ10_Final");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // Compute center and move there, then set Work X =0
                if (!double.IsNaN(edgeX1) && !double.IsNaN(edgeX2))
                {
                    try
                    {
                        double centerX = (edgeX1 + edgeX2) /2.0; // average of work-coordinate edges
                        string centerText = centerX.ToString("F3", CultureInfo.InvariantCulture);
                        streamPopup.Append($"> 🎯 Center X computed: (edgeX1 + edgeX2) /2 = ({edgeX1:F3} + {edgeX2:F3}) /2 = {centerX:F3}");
                        await mc.SendGCodeCommandWithConfirmationAsync("G90");
                        streamPopup.Append($"> 🚚 Moving to X center (Work X) ...");
                        await mc.SendGCodeCommandWithConfirmationAsync($"G00 X{centerText}");
                        await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(centerX - (mc.MStatus?.WorkX ??0.0)),1000), "Move_To_XCenter");
                        // Set work X =0 at center
                        streamPopup.Append("> 🔧 Setting Work X=0 at center...");
                        await mc.SendGCodeCommandWithConfirmationAsync("G10 L20 P0 X0");
                        streamPopup.Append("> ✅ Work X set to0 at center");
                    }
                    catch (Exception ce)
                    {
                        streamPopup.Append($"> ❌ Failed to move to X center or set zero: {ce.Message}");
                        return false;
                    }
                }
                else
                {
                    streamPopup.Append("> ❌ Cannot compute center: edgeX1/edgeX2 invalid");
                    return false;
                }

                // Return to absolute mode
                await mc.SendGCodeCommandWithConfirmationAsync("G90");
                mc?.AddLogMessage("> ✅ Sequence complete");
                streamPopup.Append("> ✅ Sequence complete");

                // Keep popup open for3 seconds before auto-closing
                await Task.Delay(1500);
                try { streamPopup?.Close(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ ERROR: Center X Outer - {ex.Message}");
                streamPopup?.Append($"> ❌ ERROR: Center X Outer - {ex.Message}");
                try { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); } catch { }
                return false;
            }
            finally
            {
  // END PROBE SESSION - release UI lock
     RunUiLocker.EndProbeSession();
       }
  }

        // === Full Y outer-center sequence (fixed to return a bool and compute center) ===
        private async Task<bool> CenterYOuterSequenceAsync()
        {
            Controls.StreamingPopup streamPopup = null;
            try
            {
                var mc = App.MainController;
                if (mc?.IsConnected != true)
                {
                    mc?.AddLogMessage("> ❌ CNC not connected");
                    return false;
                }

                // BEGIN PROBE SESSION for entire Y center sequence (locks UI until complete)
                RunUiLocker.BeginProbeSession();

                // Capture initial Work Y for returning later
                double startWorkY = mc.MStatus?.WorkY ??0.0;

                // Create and show streaming popup
                streamPopup = new Controls.StreamingPopup { Owner = this };
                streamPopup.SetTitle("Center Y Outer - Edge Detection");
                streamPopup.SetSubtitle("> Finding Y center from outer edges using Z-drop detection\n> Real-time coordinates and progress shown below:");
                streamPopup.Show();

                mc?.AddLogMessage("> 🔧 Center Y Outer sequence starting...");
                streamPopup.Append("> 🔧 Center Y Outer sequence starting...");

                var probeManager = new Services.ProbeManager(mc);

                // Phase1: Initial Z probe at center
                streamPopup.Append("> 📍 Phase1: Z probe at center position...");
                var zProbeResult = await probeManager.ProbeZAsync(manageSession: false); // Don't manage session - parent handles it
                if (!zProbeResult.Success)
                {
                    streamPopup.Append($"> ❌ Initial Z probe failed: {zProbeResult.ErrorMessage}");
                    return false;
                }
                streamPopup.Append("> ✅ Z probe successful. Retracted to +10mm");

                // Phase2: Set local Z coordinate to +10
                streamPopup.Append("> 🔄 Phase2: Setting Z=+10 at current position...");
                if (!await mc.SendGCodeCommandWithConfirmationAsync("G10 L20 P0 Z10"))
                {
                    streamPopup.Append("> ❌ Failed to set Z=+10");
                    return false;
                }
                await Task.Delay(500);
                streamPopup.Append("> ✅ Local Z set to +10mm");

                // Determine Z coarse feed from $112 (rapid/8), capped to300, with safe fallback
                double zRapid =1000.0;
                try
                {
                    var zSetting = mc?.Settings?.FirstOrDefault(s => s.Id ==112);
                    if (zSetting != null && double.TryParse(zSetting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double zVal))
                    {
                        zRapid = Math.Max(1.0, zVal);
                    }
                }
                catch { }
                int zCoarseFeed = (int)Math.Max(1.0, zRapid /8.0);
                if (zCoarseFeed >300) zCoarseFeed =300;
                if (zCoarseFeed <1) zCoarseFeed =100; // explicit fallback
                streamPopup.Append($"> ⚙️ Z probe feed set to F{zCoarseFeed} (from rapid {zRapid:F0}/8)");

                // === Left-side (negative Y) scan with Z-drop detection ===
                bool edgeFound = false;
                int moveCount =0;
                const int MAX_MOVES =10;
                while (!edgeFound && moveCount < MAX_MOVES)
                {
                    moveCount++;
                    streamPopup.Append($"> 🔄 Move {moveCount}: Moving -20mm in Y direction...");

                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G91"))
                    {
                        streamPopup.Append("> ❌ Failed to set G91");
                        break;
                    }
                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G00 Y-20.000"))
                    {
                        streamPopup.Append("> ❌ Failed to move -Y");
                        break;
                    }
                    if (!await WaitForIdleAsync(5000, $"CenterYOuter_Move{moveCount}"))
                    {
                        streamPopup.Append("> ❌ Idle timeout after Y move");
                        break;
                    }

                    // Show current coordinates after move
                    double currentX = mc.MStatus?.X ??0.0;
                    double currentWorkX = mc.MStatus?.WorkX ??0.0;
                    double currentY = mc.MStatus?.Y ??0.0;
                    double currentWorkY = mc.MStatus?.WorkY ??0.0;
                    streamPopup.Append($"> Machine Y={currentY:F3}, Work Y={currentWorkY:F3}");
                    streamPopup.SetLiveLine($"Live: MX={currentX:F3}, WX={currentWorkX:F3} | MY={currentY:F3}, WY={currentWorkY:F3} | MZ={(mc.MStatus?.Z ??0.0):F3}, WZ={(mc.MStatus?.WorkZ ??0.0):F3} | Status={(mc?.MachineStatus ?? string.Empty).ToLowerInvariant()}");

                    // Z probe with live monitoring
                    streamPopup.Append($"> 📍 Probing Z at position {moveCount}...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    string zProbeCmd = string.Format(CultureInfo.InvariantCulture, "G38.2 Z-20 F{0}", zCoarseFeed);
                    streamPopup.Append($"> ▶ Z probe cmd: {zProbeCmd}");
                    await mc.SendGCodeCommandAsync(zProbeCmd);

                    var t0 = DateTime.Now;
                    bool seenRunning = false;
                    bool edgeDetected = false;
                    bool probeSuccess = false;
                    while ((DateTime.Now - t0).TotalSeconds <10)
                    {
                        await Task.Delay(50);
                        var st = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                        double wZ = mc.MStatus?.WorkZ ??0.0;
                        double mZ = mc.MStatus?.Z ??0.0;
                        double mx = mc.MStatus?.X ??0.0;
                        double mwx = mc.MStatus?.WorkX ??0.0;
                        double my = mc.MStatus?.Y ??0.0;
                        double mwy = mc.MStatus?.WorkY ??0.0;
                        streamPopup.SetLiveLine($"Live: MX={mx:F3}, WX={mwx:F3} | MY={my:F3}, WY={mwy:F3} | MZ={mZ:F3}, WZ={wZ:F3} | Status={st}");
                        if (st.StartsWith("run") || st.StartsWith("jog")) seenRunning = true;
                        if (wZ <= -2.0 && !st.Contains("idle"))
                        {
                            streamPopup.Append($"> 🎯 Edge detected! WorkZ={wZ:F3}mm <= -2.0mm during probe");
                            await mc.EmergencyResetAsync();
                            await Task.Delay(500);
                            var st2 = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                            if (st2.Contains("alarm"))
                            {
                                streamPopup.Append("> 🔓 Clearing alarm...");
                                await mc.SendGCodeCommandAsync("$X");
                                await Task.Delay(500);
                                streamPopup.Append("> ✅ Alarm cleared");
                            }
                            edgeDetected = true;
                            edgeFound = true;
                            break;
                        }
                        if (st.Contains("idle") && seenRunning)
                        {
                            probeSuccess = true;
                            streamPopup.Append($"> ✅ Probe touched, retracted to WorkZ={wZ:F3}");
                            break;
                        }
                        if (st.Contains("alarm"))
                        {
                            streamPopup.Append("> ⚠️ Probe failed - no surface found (edge detected)");
                            streamPopup.Append("> 🔓 Clearing alarm...");
                            await mc.SendGCodeCommandAsync("$X");
                            await Task.Delay(500);
                            streamPopup.Append("> ✅ Alarm cleared");
                            edgeDetected = true;
                            edgeFound = true;
                            break;
                        }
                    }

                    if (edgeDetected)
                    {
                        streamPopup.Append("> 🎯 Edge found - probe sequence complete");
                        break;
                    }
                    if (!probeSuccess)
                    {
                        streamPopup.Append("> ❌ Probe timeout");
                        break;
                    }

                    // Retract after successful probe
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync("G00 Z10");
                    await WaitForIdleAsync(3000, "ProbeRetract");
                }

                if (!edgeFound && moveCount >= MAX_MOVES)
                {
                    streamPopup.Append($"> ⚠️ Max moves ({MAX_MOVES}) reached without finding edge");
                }

                // === After negative Y scan: run Y+ probe, record edgeY1 ===
                double edgeY1 = double.NaN;
                try
                {
                    // Ensure not in HOLD
                    if (mc != null && mc.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        streamPopup.Append("> ⏳ Hold detected – releasing...");
                        await mc.SendControlCharacterAsync('\x18'); // Soft reset
                        await Task.Delay(200);
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Ensure not in ALARM
                    if (mc != null && mc.IsAlarmState())
                    {
                        streamPopup.Append("> ⏳ Alarm detected – clearing...");
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Quick idle check (reduced from 8000ms to 2000ms)
                    await WaitForIdleAsync(2000, "Prepare_YPlus");

                    streamPopup.Append("> ▶ Starting Y+ probe (same as Y+ button), from current Z...");
                    mc?.AddLogMessage("> ▶ Starting Y+ probe after center sequence...");
     var yPlusRes = await probeManager.ProbeYPlusAsync(30.0, manageSession: false); // Don't manage session - parent handles it
   if (!yPlusRes.Success)
                    {
                        streamPopup.Append($"> ❌ Y+ probe failed: {yPlusRes.ErrorMessage}");
                        mc?.AddLogMessage($"> ❌ Y+ probe failed: {yPlusRes.ErrorMessage}");
                        return false;
                    }
                    edgeY1 = yPlusRes.ContactPosition;
                    streamPopup.Append($"> ✅ Y+ probe completed. edgeY1 = {edgeY1:F3}");
                }
                catch (Exception ypEx)
                {
                    streamPopup.Append($"> ❌ Y+ probe error: {ypEx.Message}");
                    return false;
                }

                // Retract Z to WorkZ = +10 (move, not set)
                try
                {
                    double wzNow = mc.MStatus?.WorkZ ??0.0;
                    double dz =10.0 - wzNow;
                    string dzText = dz.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔼 Retracting to Work Z=+10 (ΔZ={dzText})...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 Z{dzText}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dz),1000), "Retract_To_WZ10");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // Return to initial starting Y (work coordinates)
                try
                {
                    double nowWorkY = mc.MStatus?.WorkY ??0.0;
                    double dyBack = startWorkY - nowWorkY;
                    string dyBackText = dyBack.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔁 Returning to start WorkY (ΔY={dyBackText})...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 Y{dyBackText}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dyBack),1000), "Return_To_StartY");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // === Right-side (positive Y) scan with Z-drop detection ===
                bool edgeFoundRight = false;
                int moveCountRight =0;
                double edgeY2 = double.NaN;
                while (!edgeFoundRight && moveCountRight < MAX_MOVES)
                {
                    moveCountRight++;
                    streamPopup.Append($"> 🔄 [Right] Move {moveCountRight}: Moving +20mm in Y direction...");

                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G91"))
                    {
                        streamPopup.Append("> ❌ Failed to set G91");
                        break;
                    }
                    if (!await mc.SendGCodeCommandWithConfirmationAsync("G00 Y20.000"))
                    {
                        streamPopup.Append("> ❌ Failed to move +Y");
                        break;
                    }
                    if (!await WaitForIdleAsync(5000, $"CenterYOuter_Right_Move{moveCountRight}"))
                    {
                        streamPopup.Append("> ❌ Idle timeout after +Y move");
                        break;
                    }

                    // Snapshot after move
                    double rX = mc.MStatus?.X ??0.0;
                    double rWX = mc.MStatus?.WorkX ??0.0;
                    double rY = mc.MStatus?.Y ??0.0;
                    double rWY = mc.MStatus?.WorkY ??0.0;
                    streamPopup.Append($"> [Right] Machine Y={rY:F3}, Work Y={rWY:F3}");
                    streamPopup.SetLiveLine($"Live: MX={rX:F3}, WX={rWX:F3} | MY={rY:F3}, WY={rWY:F3} | MZ={(mc.MStatus?.Z ??0.0):F3}, WZ={(mc.MStatus?.WorkZ ??0.0):F3} | Status={(mc?.MachineStatus ?? string.Empty).ToLowerInvariant()}");

                    // Probe Z at this Y
                    streamPopup.Append($"> 📍 [Right] Probing Z at position {moveCountRight}...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    string zProbeCmdR = string.Format(CultureInfo.InvariantCulture, "G38.2 Z-20 F{0}", zCoarseFeed);
                    streamPopup.Append($"> ▶ Z probe cmd: {zProbeCmdR}");
                    await mc.SendGCodeCommandAsync(zProbeCmdR);

                    var tR = DateTime.Now;
                    bool seenRunningR = false;
                    bool edgeDetectedR = false;
                    bool probeSuccessR = false;
                    while ((DateTime.Now - tR).TotalSeconds <10)
                    {
                        await Task.Delay(50);
                        var st = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                        double wZ = mc.MStatus?.WorkZ ??0.0;
                        double mZ = mc.MStatus?.Z ??0.0;
                        double lx = mc.MStatus?.X ??0.0;
                        double lwx = mc.MStatus?.WorkX ??0.0;
                        double ly = mc.MStatus?.Y ??0.0;
                        double lwy = mc.MStatus?.WorkY ??0.0;
                        streamPopup.SetLiveLine($"Live: MX={lx:F3}, WX={lwx:F3} | MY={ly:F3}, WY={lwy:F3} | MZ={mZ:F3}, WZ={wZ:F3} | Status={st}");
                        if (st.StartsWith("run") || st.StartsWith("jog")) seenRunningR = true;
                        if (wZ <= -2.0 && !st.Contains("idle"))
                        {
                            streamPopup.Append($"> 🎯 [Right] Edge detected! WorkZ={wZ:F3}mm <= -2.0mm during probe");
                            // Fast reset instead of EmergencyResetAsync
                            await mc.SendControlCharacterAsync('\x18'); // Ctrl+X soft reset
                            await Task.Delay(200);
    
                            var st2 = mc?.MachineStatus?.ToLowerInvariant() ?? string.Empty;
                            if (st2.Contains("alarm"))
                            {
                                await mc.SendGCodeCommandAsync("$X");
                                await Task.Delay(150);
                            }
 
                           edgeDetectedR = true;
      edgeFoundRight = true;
    break;
                        }
                        if (st.Contains("idle") && seenRunningR)
                        {
                            probeSuccessR = true;
                            streamPopup.Append($"> ✅ [Right] Probe touched, retracted to WorkZ={wZ:F3}");
                            break;
                        }
                        if (st.Contains("alarm"))
                        {
                            streamPopup.Append("> ⚠️ [Right] Probe failed - no surface found");
                            streamPopup.Append("> 🔓 Clearing alarm...");
                            await mc.SendGCodeCommandAsync("$X");
                            await Task.Delay(500);
                            streamPopup.Append("> ✅ Alarm cleared");
                            edgeDetectedR = true; // edge via failure
                            edgeFoundRight = true;
                            break;
                        }
                    }

                    if (edgeDetectedR)
                    {
                        streamPopup.Append("> 🎯 [Right] Edge found - probe sequence complete");
                        break;
                    }
                    if (!probeSuccessR)
                    {
                        streamPopup.Append("> ❌ [Right] Probe timeout");
                        break;
                    }

                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync("G00 Z10");
                    await WaitForIdleAsync(3000, "Right_ProbeRetract");
                }

                // Run Y- probe and record edgeY2
                try
                {
                    // Ensure not in HOLD
                    if (mc != null && mc.MachineStatus.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        streamPopup.Append("> ⏳ Hold detected – releasing...");
                        await mc.SendControlCharacterAsync('\x18'); // Soft reset
                        await Task.Delay(200);
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Ensure not in ALARM
                    if (mc != null && mc.IsAlarmState())
                    {
                        streamPopup.Append("> ⏳ Alarm detected – clearing...");
                        await mc.SendGCodeCommandAsync("$X");
                        await Task.Delay(150);
                    }
                    // Quick idle check (reduced from 8000ms to 2000ms)
                    await WaitForIdleAsync(2000, "Prepare_YMinus");

                    streamPopup.Append("> ▶ Starting Y- probe (right side), from current Z...");
                    var yMinusRes = await probeManager.ProbeYMinusAsync(30.0, manageSession: false); // Don't manage session - parent handles it
                    if (!yMinusRes.Success)
                    {
                        streamPopup.Append($"> ❌ Y- probe failed: {yMinusRes.ErrorMessage}");
                        mc?.AddLogMessage($"> ❌ Y- probe failed: {yMinusRes.ErrorMessage}");
                        return false;
                    }
                    edgeY2 = yMinusRes.ContactPosition;
                    streamPopup.Append($"> ✅ Y- probe completed. edgeY2 = {edgeY2:F3}");
                }
                catch (Exception ymEx)
                {
                    streamPopup.Append($"> ❌ Y- probe error: {ymEx.Message}");
                }

                // Retract Z to WorkZ = +10mm before moving to center
                try
                {
                    double wzNow2 = mc.MStatus?.WorkZ ??0.0;
                    double dz2 =10.0 - wzNow2;
                    string dzText2 = dz2.ToString("F3", CultureInfo.InvariantCulture);
                    streamPopup.Append($"> 🔼 Retracting to Work Z=+10 (ΔZ={dzText2}) before center move...");
                    await mc.SendGCodeCommandWithConfirmationAsync("G91");
                    await mc.SendGCodeCommandWithConfirmationAsync($"G00 Z{dzText2}");
                    await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(dz2),1000), "Retract_To_WZ10_Final");
                    await mc.SendGCodeCommandWithConfirmationAsync("G90");
                }
                catch { }

                // Compute center and move there, then set Work Y =0
                if (!double.IsNaN(edgeY1) && !double.IsNaN(edgeY2))
                {
                    try
                    {
                        double centerY = (edgeY1 + edgeY2) /2.0;
                        string centerText = centerY.ToString("F3", CultureInfo.InvariantCulture);
                        streamPopup.Append($"> 🎯 Center Y computed: (edgeY1 + edgeY2) /2 = ({edgeY1:F3} + {edgeY2:F3}) /2 = {centerY:F3}");
                        await mc.SendGCodeCommandWithConfirmationAsync("G90");
                        streamPopup.Append($"> 🚚 Moving to Y center (Work Y) ...");
                        await mc.SendGCodeCommandWithConfirmationAsync($"G00 Y{centerText}");
                        await WaitForIdleAsync(EstimateTimeoutMsForRapid(Math.Abs(centerY - (mc.MStatus?.WorkY ??0.0)),1000), "Move_To_YCenter");
                        // Set work Y =0 at center
                        streamPopup.Append("> 🔧 Setting Work Y=0 at center...");
                        await mc.SendGCodeCommandWithConfirmationAsync("G10 L20 P0 Y0");
                        streamPopup.Append("> ✅ Work Y set to0 at center");
                    }
                    catch (Exception ce)
                    {
                        streamPopup.Append($"> ❌ Failed to move to Y center or set zero: {ce.Message}");
                        return false;
                    }
                }
                else
                {
                    streamPopup.Append("> ❌ Cannot compute center: edgeY1/edgeY2 invalid");
                    return false;
                }

                await mc.SendGCodeCommandWithConfirmationAsync("G90");
                streamPopup.Append("> ✅ Y sequence complete");

                await Task.Delay(1500);
                try { streamPopup?.Close(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ ERROR: Center Y Outer - {ex.Message}");
                return false;
            }
            finally
            {
      // END PROBE SESSION - release UI lock
   RunUiLocker.EndProbeSession();
    }
        }

    // Probe helper methods for centering operations will be added here
    }
}