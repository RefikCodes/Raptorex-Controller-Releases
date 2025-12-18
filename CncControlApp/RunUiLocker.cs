using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CncControlApp
{
    public static class RunUiLocker
    {
        // Always enabled regardless of state (safety / emergency / jog). Do NOT include Pause/Stop here anymore.
        private static readonly HashSet<string> AlwaysEnabledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EmergencyButton","EmergencyStopButton","EStopButton","BtnEmergencyStop","EmergencyResetButton",
     // ✅ UPDATED: Removed FileButton - it should be disabled during G-Code execution
        // Jog buttons (allow manual movement during non-execution states)
   "JogXPlusButton","JogXMinusButton","JogYPlusButton","JogYMinusButton","JogZPlusButton","JogZMinusButton","JogAPlusButton","JogAMinusButton"
        };

        // Idle-only items (active ONLY when controller is Idle). Rotation slider + its reset & apply also here.
        private static readonly HashSet<string> IdleOnlyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ✅ UPDATED: FileButton added here - should only be enabled during Idle state
            "FileButton",
            // File / GCode operations
            "SaveGCodeButton","OpenFileButton","BtnGenerateRotated","BtnSaveRotated","BtnAnalyzeGCode","BtnParseGCode",
            // Rotation controls
            "ApplyAngleRotationButton","RotationResetButton","RotationAngleSlider"
        };
        
      // ✅ NEW: Buttons that require G-Code loaded but otherwise follow normal state rules
        private static readonly HashSet<string> RequiresGCodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
     {
        "GCodeTabButton","ViewTabButton","GCodeRotationButton","GCodeMoveButton",
        // ✅ StartFromLineButton: Requires G-Code loaded AND Idle state (not running)
        "StartFromLineButton"
        };

        // ✅ NEW: Buttons that remain active during execution (viewing G-Code, changing views)
        // FeedRpmButton REMOVED from here; it must be Run-only
        private static readonly HashSet<string> ActiveDuringExecutionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GCodeTabButton",    // G-CODE tab - view current code
 "ViewTabButton"     // MULTI-VIEW tab - see visualization
   };

        // Run-only items (active ONLY when status is Run). Feed / Spindle overrides + resets + Pause / Stop + FeedRpmButton.
        private static readonly HashSet<string> RunOnlyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FeedOverrideSlider","FeedOverrideResetButton","SpindleOverrideSlider","SpindleOverrideResetButton",
            "PauseButton","StopButton",
            // ✅ NEW: Feed/RPM open button is RUN-only
 "FeedRpmButton"
        };

        private static bool? _lastLockActive;
        private static bool _isApplying;
        private static string _lastStatus;
        private static DispatcherTimer _enforceTimer; // periodic enforcement to override external enables
        private static Window _attachedWindow;
        private static bool _currentLockActive;

        // Probe session lock: prevents UI updates during entire probe sequence (not just individual moves)
        private static bool _probeSessionActive = false;

        // Legacy probe UI gate (kept for compatibility but deprecated in favor of session lock)
        private static bool _probeUiGateActive;
        private static bool _probeUiAppliedOnce;
        private static bool _probeForceLockActive;

        public static void Attach(Window window)
        {
            if (window == null) return;
            _attachedWindow = window;
            EnsureEnforceTimer();

            void ApplyFromController()
            {
                var ctrl = App.MainController;
                if (ctrl == null) return;
                var status = ctrl.MachineStatus ?? string.Empty;
                string lower = status.ToLowerInvariant();

                // Global lock for execution / blocked states except Jog
                bool statusLock = lower.StartsWith("run") || lower.StartsWith("hold") || lower.StartsWith("alarm") || lower.StartsWith("door");
                if (lower.StartsWith("jog")) statusLock = false; // jog keeps UI interactive

                bool changed = (_lastStatus != status) || (!_lastLockActive.HasValue || _lastLockActive.Value != statusLock);
                _lastStatus = status;
                _lastLockActive = statusLock;
                _currentLockActive = statusLock;

                if (changed)
                {
                    try { ctrl.AddLogMessage($"> UI-LOCK: status='{status}' lockActive={statusLock}"); } catch { }

                    // CRITICAL: If probe session is active, skip UI updates to prevent flickering
                    // Probe session lock will be released explicitly via EndProbeSession()
                    if (_probeSessionActive)
                    {
                        // Suppress UI updates during probe session - keep locked state
                        return;
                    }

                    // Respect probe UI gate: during probe, force one-time lock apply even if Idle
                    bool effectiveLock = statusLock || (_probeUiGateActive && _probeForceLockActive);

                    if (_probeUiGateActive && _probeUiAppliedOnce)
                    {
                        // Suppress additional applies while probe is gated
                        return;
                    }

                    ApplyRunUiLock(window, status, effectiveLock);

                    if (_probeUiGateActive)
                    {
                        _probeUiAppliedOnce = true; // mark applied once
                    }
                }
            }

            window.Dispatcher.Invoke(ApplyFromController);

            if (App.MainController != null)
            {
                App.MainController.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainControll.IsGCodeRunning) ||
                        e.PropertyName == nameof(MainControll.MachineStatus) ||
                        e.PropertyName == nameof(MainControll.CanContinueExecution) ||
                        e.PropertyName == nameof(MainControll.IsGCodeLoaded))
                    {
                        ApplyFromController();
                    }
                };
            }
        }

        // Public API: begin/end probe UI gate and force-apply once at start
        public static void BeginProbeUiGate()
        {
            _probeUiGateActive = true;
            _probeUiAppliedOnce = false;
            _probeForceLockActive = true; // force lock on first apply regardless of status
            try { App.MainController?.AddLogMessage("> UI-LOCK: Probe UI gate START (force lock)"); } catch { }
            ApplyNow();
        }

        public static void EndProbeUiGate()
        {
            _probeUiGateActive = false;
            _probeUiAppliedOnce = false;
            _probeForceLockActive = false; // release force lock
            try { App.MainController?.AddLogMessage("> UI-LOCK: Probe UI gate END (release lock)"); } catch { }
            ApplyNow(); // apply current status lock (likely unlock on Idle)
        }

        /// <summary>
        /// Begin a probe session - locks UI until EndProbeSession is called.
        /// Prevents UI flickering during multi-step probe sequences (Z probe, X/Y center probes, etc.)
        /// </summary>
        public static void BeginProbeSession()
        {
            _probeSessionActive = true;
            try { App.MainController?.AddLogMessage("> UI-LOCK: 🔒 Probe session START - UI locked until completion"); } catch { }
            ApplyNow(); // Force UI lock immediately
        }

        /// <summary>
        /// End a probe session - releases UI lock and restores normal Idle state.
        /// </summary>
        public static void EndProbeSession()
        {
            _probeSessionActive = false;
            try { App.MainController?.AddLogMessage("> UI-LOCK: 🔓 Probe session END - UI unlocked"); } catch { }
            ApplyNow(); // Restore UI to current controller state
        }

        /// <summary>
        /// Check if a probe session is currently active.
        /// </summary>
        public static bool IsProbeSessionActive()
        {
            return _probeSessionActive;
        }

        public static void ApplyNow()
        {
            if (_attachedWindow == null) return;
        var status = App.MainController?.MachineStatus ?? string.Empty;
 string lower = status.ToLowerInvariant();
      bool statusLock = lower.StartsWith("run") || lower.StartsWith("hold") || lower.StartsWith("alarm") || lower.StartsWith("door");
            if (lower.StartsWith("jog")) statusLock = false;

  // CRITICAL: Probe session lock overrides all status-based unlocking
    // Even if status is Idle, keep UI locked during probe session
          bool effectiveLock = statusLock || (_probeUiGateActive && _probeForceLockActive) || _probeSessionActive;

      ApplyRunUiLock(_attachedWindow, status, effectiveLock);

            if (_probeUiGateActive)
         _probeUiAppliedOnce = true;
      }

        private static void EnsureEnforceTimer()
        {
            if (_enforceTimer != null) return;
            _enforceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _enforceTimer.Tick += (s, e) =>
            {
           if (_attachedWindow == null) return;
     
      // CRITICAL: If probe session is active, skip periodic enforcement to prevent flickering
     if (_probeSessionActive) return;
     
         // Respect gate: after first apply during probe, skip further enforcements
    if (_probeUiGateActive && _probeUiAppliedOnce) return;
        
       var status = App.MainController?.MachineStatus ?? string.Empty;
       bool effectiveLock = _currentLockActive || (_probeUiGateActive && _probeForceLockActive);
       ApplyRunUiLock(_attachedWindow, status, effectiveLock);
    if (_probeUiGateActive) _probeUiAppliedOnce = true;
      };
            _enforceTimer.Start();
        }

        private static void ApplyRunUiLock(Window window, string status, bool lockActive)
        {
            if (_isApplying) return;
            _isApplying = true;
            try
          {
          bool isIdle = status.StartsWith("Idle", StringComparison.OrdinalIgnoreCase);
       bool isRun = status.StartsWith("Run", StringComparison.OrdinalIgnoreCase);
     bool isHold = status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);
    bool isAlarm = status.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase);
    
    // ✅ Check if G-Code is loaded for Run button logic
    bool hasGCode = App.MainController?.IsGCodeLoaded ?? false;

        // Buttons
  foreach (var btn in FindVisualChildren<Button>(window))
   {
            if (btn == null) continue;
   string name = btn.Name ?? string.Empty;

   // Special toggle button: Run acts as RUN (Idle) and STOP/PAUSE (Run/Hold)
     if (string.Equals(name, "RunButton", StringComparison.OrdinalIgnoreCase))
          {
        // ✅ UPDATED: Run button requires G-Code loaded AND valid machine state
        // During probe session, disable Run button (except during actual Run state)
  if (_probeSessionActive)
    btn.IsEnabled = false;
    else
   btn.IsEnabled = (isIdle || isRun || isHold) && !isAlarm && hasGCode;
   continue;
        }

      // Always enabled overrides everything (including probe session)
   if (IsAlwaysEnabled(btn))
       {
      btn.IsEnabled = true;
   continue;
    }
   
        // ✅ NEW: Buttons that remain active during execution
        if (ActiveDuringExecutionNames.Contains(name))
        {
  // These buttons remain active during Run/Hold to allow viewing
 // Only disable during probe session, alarm, or when no G-Code loaded
            if (_probeSessionActive || isAlarm || !hasGCode)
    btn.IsEnabled = false;
     else
       btn.IsEnabled = true; // Active during Idle, Run, Hold
            continue;
        }

        // ✅ NEW: Buttons that require G-Code to be loaded
        if (RequiresGCodeNames.Contains(name))
  {
   // These buttons need G-Code loaded, but otherwise follow general lock rules
     // Disable during probe session or when no G-Code loaded
       if (_probeSessionActive || !hasGCode)
 btn.IsEnabled = false;
 else
           btn.IsEnabled = !lockActive;
      continue;
    }

       // Idle-only rule
       if (IdleOnlyNames.Contains(name))
         {
     // During probe session, force disable
     if (_probeSessionActive)
     btn.IsEnabled = false;
   else
      btn.IsEnabled = isIdle && !isAlarm;
     continue;
         }

        // Run-only rule
   if (RunOnlyNames.Contains(name))
           {
      // During probe session, force disable
       if (_probeSessionActive)
               btn.IsEnabled = false;
      else if (name == "PauseButton" || name == "StopButton")
   btn.IsEnabled = (isRun || isHold);
   else
         btn.IsEnabled = isRun; // FeedRpmButton and override controls are RUN-only
    continue;
             }

    // General rule: disable if global/probe lock active OR probe session active
    btn.IsEnabled = !lockActive && !_probeSessionActive;
       }

         // Sliders (feed / spindle / rotation)
     foreach (var slider in FindVisualChildren<Slider>(window))
                {
      if (slider == null) continue;
               string name = slider.Name ?? string.Empty;

         if (IdleOnlyNames.Contains(name))
     {
            if (_probeSessionActive)
          slider.IsEnabled = false;
  else
      slider.IsEnabled = isIdle && !isAlarm;
       continue;
     }
              if (RunOnlyNames.Contains(name))
  {
        if (_probeSessionActive)
     slider.IsEnabled = false;
else if (name == "FeedOverrideSlider" || name == "SpindleOverrideSlider")
    slider.IsEnabled = isRun;
 else
       slider.IsEnabled = isRun;
          continue;
          }

   // Otherwise follow global/probe lock or probe session
    slider.IsEnabled = !lockActive && !_probeSessionActive;
         }

           // Menu items
    foreach (var mi in FindVisualChildren<MenuItem>(window))
                {
        string name = mi.Name ?? string.Empty;
   if (IdleOnlyNames.Contains(name))
        {
    if (_probeSessionActive)
    mi.IsEnabled = false;
              else
      mi.IsEnabled = isIdle && !isAlarm;
  continue;
             }
 if (RunOnlyNames.Contains(name))
     {
     if (_probeSessionActive)
          mi.IsEnabled = false;
    else if (name == "PauseButton" || name == "StopButton")
     mi.IsEnabled = (isRun || isHold);
      else
            mi.IsEnabled = isRun;
   continue;
        }
     mi.IsEnabled = !lockActive && !_probeSessionActive;
     }
            }
 finally
       {
       _isApplying = false;
   }
     }

        private static bool IsAlwaysEnabled(Button btn)
        {
   if (btn == null) return false;
if (!string.IsNullOrEmpty(btn.Name) && AlwaysEnabledNames.Contains(btn.Name)) return true;
    if (btn.Tag is string tag && (tag.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase) || tag.Equals("Jog", StringComparison.OrdinalIgnoreCase))) return true;
          return false;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject start) where T : DependencyObject
        {
            if (start == null) yield break;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int count = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);
                    if (child is T tChild)
                        yield return tChild;
                    queue.Enqueue(child);
                }
            }
        }
    }
}