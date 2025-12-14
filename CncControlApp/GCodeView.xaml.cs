using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using CncControlApp.Managers;
using CncControlApp.Helpers; // 🆕 Add helper namespace
using System.Windows.Shapes;

namespace CncControlApp
{
    public partial class GCodeView : UserControl, INotifyPropertyChanged
    {
        #region Fields

        private GCodeFileService _fileService;

        private ObservableCollection<GCodeLineItem> _displayGCodeLines;

     private bool _updateScheduled = false;
        private DateTime _lastScroll = DateTime.MinValue;

    private bool _fileServiceEventsHooked = false;

        // 🆕 Refactored: Use DebounceTimer helper instead of raw DispatcherTimer
        private DebounceTimer _feedDebounceTimer;
        private DebounceTimer _spindleDebounceTimer;
        private DispatcherTimer _executionModalValuesTimer; // Keep this as-is (not debounce)

// Overrides state
        private int _lastFeedOverridePercent = 100;
        private int _lastSpindleOverridePercent = 100;
        private int _pendingFeedOverridePercent = 100;
      private int _pendingSpindleOverridePercent = 100;

        // Modal F/S
  private double _currentModalFeed = 0;
  private double _currentModalSpindle = 0;

        // Touch handling
        private bool _feedTouchActive = false;
        private bool _spindleTouchActive = false;
  private bool _rotationTouchActive = false;
        private DateTime _lastFeedImmediate = DateTime.MinValue;
    private DateTime _lastSpindleImmediate = DateTime.MinValue;
        private readonly TimeSpan _touchImmediateInterval = TimeSpan.FromMilliseconds(50);

   // Rotation
   private double _currentRotationAngle = 0;
private bool _enableFitOnRotation = true;
  
     // Rotation normalization shift (applied when fit-to-table is enabled)
        private double _rotationAppliedShiftX = 0;
        private double _rotationAppliedShiftY = 0;

        // Smooth scrolling
      private DispatcherTimer _smoothScrollTimer;
        private bool _isScrollAnimating = false;
private double _scrollFromOffset = 0;
        private double _scrollToOffset = 0;
        private DateTime _scrollStartTime;
    private TimeSpan _scrollDuration = TimeSpan.FromMilliseconds(220);
      private double _lastRequestedTop = -1;
    private DateTime _userScrollSuppressUntil = DateTime.MinValue;
  private ScrollViewer _gcodeScrollViewer;

// Keep selection aligned with current line
        private bool _suppressSelectionChanged = false;

        // Execution tracking
      private DateTime _executionStartTime;
        private string _currentFileName = "";

        // ✅ Idle detection during GCode run - for logging unexpected idle states
        private bool _idleTrackingActive = false;
        private DateTime _idleStartTime;
        private int _idleDetectedAtLine = -1;
        private string _lastKnownMachineStatus = "";

    #endregion

        #region Properties

      public ObservableCollection<GCodeLineItem> DisplayGCodeLines
        {
     get => _displayGCodeLines ?? (_displayGCodeLines = new ObservableCollection<GCodeLineItem>());
       private set { _displayGCodeLines = value; OnPropertyChanged(); }
     }

      private string _currentFeedDisplay = "F: —";
        public string CurrentFeedDisplay
    {
      get => _currentFeedDisplay;
            private set { _currentFeedDisplay = value; OnPropertyChanged(); }
        }

      private string _currentSpindleDisplay = "S: —";
        public string CurrentSpindleDisplay
      {
            get => _currentSpindleDisplay;
            private set { _currentSpindleDisplay = value; OnPropertyChanged(); }
        }

   public bool EnableFitOnRotation
        {
     get => _enableFitOnRotation;
  set
       {
    _enableFitOnRotation = value;
      OnPropertyChanged();
                UpdateStatusBarWithLiveFitCheck(); // in LiveFit partial
      }
        }

        public double RotationAppliedShiftX => _rotationAppliedShiftX;
        public double RotationAppliedShiftY => _rotationAppliedShiftY;

 // Feed/RPM button enabled only while a G-Code program is actively running
 public bool IsFeedRpmButtonEnabled => App.MainController?.IsGCodeRunning == true;

    #endregion

        #region Constructor

     public GCodeView()
        {
 // 🆕 Initialize debounce timers using helper class (eliminates duplication)
            _feedDebounceTimer = new DebounceTimer(
         TimeSpan.FromMilliseconds(150), 
    async () => await ApplyFeedOverrideAsync(_pendingFeedOverridePercent));

   _spindleDebounceTimer = new DebounceTimer(
   TimeSpan.FromMilliseconds(150), 
  async () => await ApplySpindleOverrideAsync(_pendingSpindleOverridePercent));
  
            // Timer to periodically update modal values during execution
 _executionModalValuesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
 _executionModalValuesTimer.Tick += (s, e) =>
       {
   try
    {
          if (App.MainController?.IsGCodeRunning == true)
        {
 // Update overrides + progress panel each tick (live time / remaining)
     UpdateOverridesFromExecutionManager();
     UpdateTimeBasedProgress();
     UpdateLiveSpeedDisplay(); // Update live F and S display
      }
   else
           {
           // Only run when execution is active
       if (_executionModalValuesTimer.IsEnabled)
    _executionModalValuesTimer.Stop();
         }
   }
          catch { }
 };

    InitializeComponent();

            InitializeGCodeWorkspace();

            if (App.MainController != null)
            {
                App.MainController.CommandBlockedDueToHold += OnCommandBlockedDueToHold;
                
                // Subscribe to MachineStatus property changes for live speed updates
                if (App.MainController.MStatus != null)
                    App.MainController.MStatus.PropertyChanged += OnMachineStatusPropertyChanged;
            }

    // Load LiveFit data at startup
      LoadTableDimensionsFromSettings(); // in LiveFit partial
        }

        private void OnMachineStatusPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Update live speed display when CurrentFeed or CurrentSpindle changes
            if (e.PropertyName == nameof(MachineStatus.CurrentFeed) || 
                e.PropertyName == nameof(MachineStatus.CurrentSpindle))
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateLiveSpeedDisplay()));
            }
        }

        /// <summary>
        /// Update live Feed Rate (F) and Spindle Speed (S) display in the panel
        /// Shows effective values after override percentages are applied
        /// </summary>
        private void UpdateLiveSpeedDisplay()
        {
            try
            {
                var mc = App.MainController;
                if (mc == null) return;

                // Get real-time feed from GRBL status report (FS: or F: field)
                double liveFeed = mc.MStatus?.CurrentFeed ?? 0;
                
                // Get real-time spindle from GRBL status report (FS: field)
                double liveSpindle = mc.MStatus?.CurrentSpindle ?? 0;
                
                // DEBUG: Log feed values to diagnose update issues
                System.Diagnostics.Debug.WriteLine($"[FEED_DEBUG] MStatus.CurrentFeed={liveFeed:F1}, CurrentSpindle={liveSpindle:F1}, IsRunning={mc.IsGCodeRunning}");
                
                // If no live feed from GRBL, fall back to modal feed with override
                if (liveFeed <= 0)
                {
                    double modalFeed = mc.GCodeManager?.CurrentModalFeed ?? 0;
                    int feedPercent = _lastFeedOverridePercent;
                    liveFeed = modalFeed * feedPercent / 100.0;
                    System.Diagnostics.Debug.WriteLine($"[FEED_DEBUG] Fallback to modal: modalFeed={modalFeed:F1}, override={feedPercent}%, result={liveFeed:F1}");
                }
                
                // If no live spindle from GRBL, fall back to modal spindle with override
                if (liveSpindle <= 0)
                {
                    double modalSpindle = mc.GCodeManager?.CurrentModalSpindle ?? mc.SpindleSpeed;
                    int spindlePercent = _lastSpindleOverridePercent;
                    liveSpindle = modalSpindle * spindlePercent / 100.0;
                }

                // Update UI with effective values (compact format for button caption)
                if (LiveFeedRateText != null)
                    LiveFeedRateText.Text = liveFeed > 0 ? $"{liveFeed:F0}" : "—";
                
                if (LiveSpindleSpeedText != null)
                    LiveSpindleSpeedText.Text = liveSpindle > 0 ? $"{liveSpindle:F0}" : "—";
            }
            catch { }
        }

        private void OnCommandBlockedDueToHold(object sender, (string Command, string MachineStatus) e)
      {
       if (StatusTextBlock != null)
         {
StatusTextBlock.Text = $"Paused (Hold) – skipped: {e.Command}";
        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
     }
        }

        #endregion

        #region INotifyPropertyChanged

  public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion

        // Method to start modal value updates during execution (also drives periodic progress refresh)
   private void StartExecutionModalUpdates()
   {
            try
            {
  if (!_executionModalValuesTimer.IsEnabled && App.MainController?.IsGCodeRunning == true)
    {
         _executionModalValuesTimer.Start();
         // Immediately update display when execution starts
         UpdateLiveSpeedDisplay();
      }
  }
            catch { }
    }

        // Method to stop modal value updates (halts periodic refresh)
        private void StopExecutionModalUpdates()
        {
            try
        {
    if (_executionModalValuesTimer.IsEnabled)
             {
          _executionModalValuesTimer.Stop();
                }
  }
            catch { }
      }

        // Listen to IsGCodeRunning property changes to start/stop updates
        public void UpdateExecutionState(bool isRunning)
 {
     try
            {
       if (isRunning)
      {
        StartExecutionModalUpdates();
   }
   else
         {
   StopExecutionModalUpdates();
       }
     }
       catch { }
     }

        /// <summary>
        /// Update empty state placeholder visibility based on whether files are loaded
        /// </summary>
        public void UpdateEmptyStatePlaceholder()
        {
            try
            {
                if (EmptyStatePlaceholder == null) return;
                
                bool hasContent = DisplayGCodeLines != null && DisplayGCodeLines.Count > 0;
                EmptyStatePlaceholder.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        /// <summary>
        /// Refresh the top view canvas to reflect current work coordinates
        /// </summary>
        public void RefreshTopView()
        {
            try
            {
                // Use RedrawAllViewports for full refresh (same as panel switch)
                _fileService?.RedrawAllViewports();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshTopView error: {ex.Message}");
            }
        }
    }
}