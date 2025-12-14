using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void GCodeView_Loaded(object sender, RoutedEventArgs e)
        {
            // DESIGN MODE GUARD: Skip runtime initialization in designer to avoid collection enumeration issues
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            EnsureExecutionBindings();
            HookFileServiceEvents();
            EnsureGCodeScrollViewer();
            
            // ✅ Initialize live trace system
            InitializeLiveTrace();

            if (GCodeListBox != null)
 {
                ScrollViewer.SetCanContentScroll(GCodeListBox, true);
     VirtualizingPanel.SetScrollUnit(GCodeListBox, ScrollUnit.Item);

       GCodeListBox.SelectionChanged -= OnGCodeListBoxSelectionChanged;
                GCodeListBox.SelectionChanged += OnGCodeListBoxSelectionChanged;
            }

            if (App.MainController != null)
            {
 App.MainController.PropertyChanged -= MainController_PropertyChanged;
      App.MainController.PropertyChanged += MainController_PropertyChanged;
           App.MainController.StopSequenceCompleted -= OnStopSequenceCompleted;
    App.MainController.StopSequenceCompleted += OnStopSequenceCompleted;
      UpdateRotationRunningState();

       var manager = App.MainController.GCodeManager;
      if (manager != null)
          {
           manager.LineCompleted -= OnManagerLineCompleted;
      manager.LineCompleted += OnManagerLineCompleted;

    manager.PropertyChanged -= OnManagerPropertyChanged;
       manager.PropertyChanged += OnManagerPropertyChanged;
           manager.Paused -= OnManagerPaused;
    manager.Paused += OnManagerPaused;
                }
   
  // Initialize execution state
       UpdateExecutionState(App.MainController.IsGCodeRunning);
            }

            // Ensure the Run/Stop button visual state is correct initially
            UpdateExecutionControlButtons();

   // Update feed and spindle overrides from current execution state
            UpdateOverridesFromExecutionManager();
     
            ScrollToCurrentLine();
    }

        private void GCodeView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (GCodeListBox != null)
            {
                GCodeListBox.SelectionChanged -= OnGCodeListBoxSelectionChanged;
                GCodeListBox.PreviewMouseWheel -= OnListBoxPreviewMouseWheel;
                GCodeListBox.PreviewMouseDown -= OnListBoxPreviewMouseDown;
            }
            CancelSmoothScroll();
            
            // Stop execution modal updates
            StopExecutionModalUpdates();

            if (App.MainController != null)
            {
                App.MainController.PropertyChanged -= MainController_PropertyChanged;
                App.MainController.StopSequenceCompleted -= OnStopSequenceCompleted;

                // Unsubscribe from MachineStatus property changes
                if (App.MainController.MStatus != null)
                    App.MainController.MStatus.PropertyChanged -= OnMachineStatusPropertyChanged;

                var manager = App.MainController.GCodeManager;
                if (manager != null)
                {
                    manager.LineCompleted -= OnManagerLineCompleted;
                    manager.PropertyChanged -= OnManagerPropertyChanged;
                    manager.Paused -= OnManagerPaused;
                }
            }
            UnhookFileServiceEvents();
        }

        private void MainController_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
 {
   if (e.PropertyName == nameof(MainControll.IsGCodeRunning) ||
   e.PropertyName == nameof(MainControll.MachineStatus))
   {
 Application.Current.Dispatcher.BeginInvoke(new Action(() =>
  {
   if (StatusTextBlock != null && App.MainController != null)
  {
    var mgr = App.MainController.GCodeManager;
         string state = App.MainController.MachineStatus ?? string.Empty;
     bool running = App.MainController.IsGCodeRunning;
       
     // ✅ Handle live trace based on execution state
  if (e.PropertyName == nameof(MainControll.IsGCodeRunning))
  {
     UpdateExecutionState(running);
  HandleExecutionStateForLiveTrace(running);
   // 🔔 Notify UI so Feed/RPM button refreshes its IsEnabled binding
   OnPropertyChanged(nameof(IsFeedRpmButtonEnabled));
   // Start/stop periodic time-based progress refresh to keep UI live
   if (running)
   {
     if (!_executionModalValuesTimer.IsEnabled) _executionModalValuesTimer.Start();
     // ✅ Immediately update live speed display when execution starts
     UpdateLiveSpeedDisplay();
     // ✅ Reset idle tracking when execution starts
     _idleTrackingActive = false;
     _idleDetectedAtLine = -1;
   }
   else
   {
     if (_executionModalValuesTimer.IsEnabled) _executionModalValuesTimer.Stop();
     UpdateTimeBasedProgress(); // one last push
     // ✅ Log final idle out if we were tracking idle
     if (_idleTrackingActive)
     {
         LogIdleEnd("Execution stopped");
         _idleTrackingActive = false;
     }
   }
  }

  // ✅ IDLE DETECTION LOGGING: Track unexpected idle states during GCode run
  if (e.PropertyName == nameof(MainControll.MachineStatus) && running)
  {
      // NOTE: Completion is not driven by MachineStatus.
      // Use last OK'ed line (best-effort) only for diagnostics.
      bool allLinesSent = mgr != null && mgr.LastCompletedLineIndex >= (mgr.GCodeLines.Count - 1);
      bool isIdle = state.StartsWith("Idle", StringComparison.OrdinalIgnoreCase);
      bool isRun = state.StartsWith("Run", StringComparison.OrdinalIgnoreCase);
      
      // If we're running and machine goes Idle but NOT all lines sent - this is an unexpected idle!
      if (isIdle && !allLinesSent && !_idleTrackingActive)
      {
          _idleTrackingActive = true;
          _idleStartTime = DateTime.Now;
          _idleDetectedAtLine = mgr?.LastCompletedLineIndex ?? -1;
          int currentExec = mgr?.CurrentlyExecutingLineIndex ?? -1;
          int totalLines = mgr?.GCodeLines?.Count ?? 0;
            string snapshot = null;
            try { snapshot = mgr?.GetStreamingDebugSnapshot(); } catch { snapshot = null; }
          
          // Log detailed info to file
            ErrorLogger.LogWarning($"⚠️ UNEXPECTED IDLE DURING GCODE RUN - Line: {_idleDetectedAtLine + 1}/{totalLines} (exec: {currentExec + 1}), IdleStartTime: {_idleStartTime:HH:mm:ss.fff}, PrevStatus: {_lastKnownMachineStatus}, Streamer: {snapshot ?? "<null>"}");
          App.MainController?.AddLogMessage($"> ⚠️ Unexpected Idle at line {_idleDetectedAtLine + 1}/{totalLines} - {_idleStartTime:HH:mm:ss.fff}");
      }
      // If we were tracking idle and now we're running again - log the idle duration
      else if (isRun && _idleTrackingActive)
      {
          LogIdleEnd("Resumed to Run");
          _idleTrackingActive = false;
      }
      
      _lastKnownMachineStatus = state;
  }
     
  if (state.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
  {
      int lastOk = mgr?.LastCompletedLineIndex + 1 ?? 0;
    int exec = mgr?.CurrentlyExecutingLineIndex + 1 ?? 0;
   StatusTextBlock.Text = $"Paused (Hold) – Line OK: {lastOk}, Exec: {exec}";
   StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 149, 0));
        }
     else if (running)
   {
    StatusTextBlock.Text = "Running...";
StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
      }
     else
     {
   StatusTextBlock.Text = "Ready";
  StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89));
 }
     UpdateRotationRunningState();
}

    // Also update Run/Stop toggle visuals on any relevant state change
      UpdateExecutionControlButtons();
     
           // Update override displays with latest modal values when execution state changes
 UpdateOverridesFromExecutionManager();
     // Push time-based progress immediately so Progress panel is live
 UpdateTimeBasedProgress();
     }), DispatcherPriority.Background);
  }
     if (e.PropertyName == nameof(MainControll.CurrentGCodeLineIndex) ||
     e.PropertyName == nameof(MainControll.IsGCodeRunning))
 {
         if (_updateScheduled) return;
    _updateScheduled = true;
Application.Current.Dispatcher.BeginInvoke(new Action(() =>
      {
_updateScheduled = false;
    UpdateCurrentLineInDisplayCollection();
      EnsureSelectedCurrentLine();
    UpdateModalValuesFromExecutionManager();
     var now = DateTime.UtcNow;
  if ((now - _lastScroll).TotalMilliseconds >= 150)
   {
         _lastScroll = now;
  ScrollToCurrentLine();
     }
 // keep progress text refreshed too
 UpdateTimeBasedProgress();
 }), DispatcherPriority.Background);
  }
            }
       catch { }
        }

        private void OnStopSequenceCompleted()
        {
 try
{
 ErrorLogger.LogDebug("OnStopSequenceCompleted BAŞLADI");
 Application.Current.Dispatcher.BeginInvoke(new Action(() =>
 {
 ErrorLogger.LogDebug("OnStopSequenceCompleted - Dispatcher içinde");
 // ✅ Önce satır durumlarını sıfırla
 ErrorLogger.LogDebug("ResetAllLineStatus çağrılıyor...");
 ResetAllLineStatus();
 ErrorLogger.LogDebug("ResetAllLineStatus tamamlandı");
 
 var mgr = App.MainController?.GCodeManager;
 mgr?.ResetExecutionState();
 if (ProgressTextBlock != null) ProgressTextBlock.Text = "0.0%";
 if (ExecutionTimeTextBlock != null) ExecutionTimeTextBlock.Text = "00:00:00";
 if (RemainingTimeTextBlock != null) RemainingTimeTextBlock.Text = "--:--:--";
 if (StatusTextBlock != null)
 { StatusTextBlock.Text = "Ready"; StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52,199,89)); }
 if (GCodeListBox != null && GCodeListBox.Items.Count >0)
 { try { _suppressSelectionChanged = true; GCodeListBox.SelectedIndex =0; GCodeListBox.ScrollIntoView(GCodeListBox.Items[0]); } finally { _suppressSelectionChanged = false; } }
 UpdateExecutionControlButtons();
 UpdateOverridesFromExecutionManager();
 StopExecutionModalUpdates();
 ClearLiveTrace();
 ErrorLogger.LogDebug("OnStopSequenceCompleted TAMAMLANDI");
 }), DispatcherPriority.Background);
 }
catch (Exception ex) { ErrorLogger.LogError("OnStopSequenceCompleted HATA", ex); }
        }
   
        private void OnManagerLineCompleted(object sender, int lineIndex)
   {
     try
   {
      if (lineIndex < 0 || DisplayGCodeLines == null || lineIndex >= DisplayGCodeLines.Count) return;

     Application.Current.Dispatcher.BeginInvoke(new Action(() =>
       {
    DisplayGCodeLines[lineIndex].SetAsExecuted();
        
    // Update override values periodically during execution - but not too often
  if (lineIndex % 5 == 0)
   {
     UpdateOverridesFromExecutionManager();
        }
   }), DispatcherPriority.Background);
    }
    catch { }
        }

        private void OnManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
 {
     if (e.PropertyName == nameof(Managers.GCodeExecutionManager.CurrentlyExecutingLineIndex) ||
    e.PropertyName == nameof(Managers.GCodeExecutionManager.LastCompletedLineIndex))
   {
  Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
     UpdateCurrentLineInDisplayCollection();
   EnsureSelectedCurrentLine();
    ScrollToCurrentLine();
       }), DispatcherPriority.Background);
}
      
  // Update modal values and override displays when they change in the execution manager
   if (e.PropertyName == nameof(Managers.GCodeExecutionManager.CurrentModalFeed) ||
 e.PropertyName == nameof(Managers.GCodeExecutionManager.CurrentModalSpindle))
         {
    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
{
      UpdateModalValuesFromExecutionManager();
    }), DispatcherPriority.Background);
   }
        
      // Update time-based progress and Z level during execution
  if (e.PropertyName == nameof(Managers.GCodeExecutionManager.LiveElapsedSeconds) ||
     e.PropertyName == nameof(Managers.GCodeExecutionManager.ExecutionProgressTime))
        {
Application.Current.Dispatcher.BeginInvoke(new Action(() =>
       {
 UpdateTimeBasedProgress();
   }), DispatcherPriority.Background);
    }
            
   if (e.PropertyName == nameof(Managers.GCodeExecutionManager.CurrentExecutionZ))
      {
      Application.Current.Dispatcher.BeginInvoke(new Action(() =>
      {
        UpdateLiveZDisplay();
        }), DispatcherPriority.Background);
        }
     }

        private void OnManagerPaused(object sender, string message)
     {
    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
          {
      if (StatusTextBlock != null)
  {
          StatusTextBlock.Text = message;
  StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 149, 0));
       }
    // Ensure toggle reflects paused state
      UpdateExecutionControlButtons();
     
  // Update override displays in paused state
         UpdateOverridesFromExecutionManager();
   }), DispatcherPriority.Background);
        }

 private void UpdateModalValuesFromExecutionManager()
        {
    try
     {
       var manager = App.MainController?.GCodeManager;
          if (manager == null) return;

 // Only update if the values have actually changed or are non-zero
    double modalFeed = manager.CurrentModalFeed;
        double modalSpindle = manager.CurrentModalSpindle;
        
        bool feedChanged = Math.Abs(_currentModalFeed - modalFeed) > 0.01;
        bool spindleChanged = Math.Abs(_currentModalSpindle - modalSpindle) > 0.01;
    
   // Update modal values if they've changed or if current values are zero but new values aren't
       if (feedChanged || (_currentModalFeed == 0 && modalFeed > 0))
     _currentModalFeed = modalFeed;
     
        if (spindleChanged || (_currentModalSpindle == 0 && modalSpindle > 0))
   _currentModalSpindle = modalSpindle;
    
      RefreshOverrideDisplays();
         }
 catch { }
        }

      private void OnExecutionCompleted(object sender, bool isSuccess)
   {
        try
         {
 // ✅ DEBUG: Log the isSuccess value to understand why warnings popup is shown
 ErrorLogger.LogInfo($"OnExecutionCompleted called with isSuccess={isSuccess}, sender={sender?.GetType().Name ?? "null"}");
 
 Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
          try
     {
 // Suppress completion handling if we're in a manual STOP flow.
       // During STOP: DisableAutoStopPopup is set and a short grace period is active
     // while the Stop Sequence popup is running. In that case, we must not
    // show the completion popup or reset here; StopSequenceCompleted handler
         // will take care of UI cleanup.
        var mc = App.MainController;
                 if (mc != null && (mc.DisableAutoStopPopup || mc.IsInPostStopGracePeriod))
      {
     mc.AddLogMessage("> 🔇 (STOP/grace) Run-Complete popup and auto-reset bastırıldı");
        return;
    }

          // 1) Reset controls back to idle state
  UpdateExecutionControlButtons();
    if (StatusTextBlock != null)
     {
      StatusTextBlock.Text = "Ready";
     StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89));
      }

       // 2) Clear executed/current/error flags (checks/greens) in G-Code list
 ResetAllLineStatus();

  // 3) Reset manager-side counters and modal values to base state
  var mgr = App.MainController?.GCodeManager;
    mgr?.ResetExecutionState();

     // 4) Reset right-panel counters/texts
    if (ProgressTextBlock != null) ProgressTextBlock.Text = "0.0%";
    if (ExecutionTimeTextBlock != null) ExecutionTimeTextBlock.Text = "00:00:00";
     if (RemainingTimeTextBlock != null) RemainingTimeTextBlock.Text = "--:--:--";

   // 5) Reset feed/spindle override sliders and displays
   ResetFeedSpindleState();

        // 6) Scroll G-Code list to the top
    ScrollGCodeToTop();

       var endTime = DateTime.Now;
    var totalLines = App.MainController?.GCodeManager?.GCodeLines?.Count ?? 0;
  
          var popup = new Controls.CompletionPopup
    {
      Owner = Application.Current.MainWindow
       };
    
     popup.SetCompletionInfo(_executionStartTime, endTime, totalLines, _currentFileName, isSuccess);
         popup.ShowDialog();
             }
  catch { }
     }), DispatcherPriority.Background);
    }
   catch { }
     }
        
        private void UpdateTimeBasedProgress()
      {
    try
    {
  if (App.MainController == null) return;
          
      // Update progress percentage
     if (ProgressTextBlock != null)
  {
   double progressPercent = App.MainController.ExecutionProgressTime;
     ProgressTextBlock.Text = $"{progressPercent:F1}%";
      }
       
                // Update elapsed time
  if (ExecutionTimeTextBlock != null)
     {
           ExecutionTimeTextBlock.Text = App.MainController.ElapsedTimeText ?? "00:00:00";
   }
  
   // Update remaining time
    if (RemainingTimeTextBlock != null)
          {
   RemainingTimeTextBlock.Text = App.MainController.RemainingTimeText ?? "--:--:--";
          }
     }
      catch { }
    }
   
      private void UpdateLiveZDisplay()
{
            try
      {
  // ZRangeTextBlock was removed during panel reorganization
    // Z range is now static and shows file stats, not live execution Z
  // If you need to show live Z during execution, add a new dedicated TextBlock
      }
       catch { }
    }

      private void UpdateCurrentLineInDisplayCollection()
     {
       try
      {
      if (DisplayGCodeLines == null || App.MainController == null) return;
      
      // ✅ Stop/Idle durumunda satır güncellemesi yapma - negatif görüntü sorununu önle
      if (!App.MainController.IsGCodeRunning) 
      {
          ErrorLogger.LogDebug("UpdateCurrentLineInDisplayCollection - IsGCodeRunning=false, güncelleme atlandı");
          return;
      }
      
            var manager = App.MainController.GCodeManager;
    if (manager == null) return;

 int executingIndex = manager.CurrentlyExecutingLineIndex;
      int lastOk = manager.LastCompletedLineIndex;
      
      // ✅ Geçersiz index değerleri varsa güncelleme yapma
      if (executingIndex < 0 && lastOk < 0) return;

      for (int i = 0; i < DisplayGCodeLines.Count; i++)
         {
      var it = DisplayGCodeLines[i];
         if (i <= lastOk)
       {
        if (!it.IsExecuted) it.SetAsExecuted();
    it.IsCurrentLine = false;
    }
           else
    {
     if (i > lastOk && i < executingIndex)
     {
    if (!it.IsSent) it.SetAsSent();
        it.IsCurrentLine = false;
  }
       else
   {
      if (it.IsCurrentLine && i != executingIndex) it.IsCurrentLine = false;
 }
      }
   }

         if (executingIndex >= 0 && executingIndex < DisplayGCodeLines.Count)
   {
 var curr = DisplayGCodeLines[executingIndex];
   if (!curr.IsCurrentLine) curr.SetAsCurrent();
    }
    }
        catch { }
        }

        /// <summary>
        /// Log idle end with duration and details to help debug runtime issues
        /// </summary>
        private void LogIdleEnd(string reason)
        {
            try
            {
                if (!_idleTrackingActive) return;
                
                var idleEndTime = DateTime.Now;
                var idleDuration = idleEndTime - _idleStartTime;
                var mgr = App.MainController?.GCodeManager;
                int currentLine = mgr?.LastCompletedLineIndex ?? -1;
                int totalLines = mgr?.GCodeLines?.Count ?? 0;
                
                // Log detailed info to file
                ErrorLogger.LogWarning(
                    $"⏱️ IDLE END - Reason: {reason}, " +
                    $"StartLine: {_idleDetectedAtLine + 1}, EndLine: {currentLine + 1}/{totalLines}, " +
                    $"IdleStart: {_idleStartTime:HH:mm:ss.fff}, IdleEnd: {idleEndTime:HH:mm:ss.fff}, " +
                    $"Duration: {idleDuration.TotalMilliseconds:F0}ms ({idleDuration.TotalSeconds:F2}s)");
                
                App.MainController?.AddLogMessage(
                    $"> ⏱️ Idle ended ({reason}) - Duration: {idleDuration.TotalMilliseconds:F0}ms, Lines: {_idleDetectedAtLine + 1}→{currentLine + 1}");
            }
            catch { }
        }
    }
}