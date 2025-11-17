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

        private ViewportManager _viewportManager;
        private GCodeFileService _fileService;
        private GCodeOverlayManager _overlayManager;
        private GCodeVisualization _visualization;

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
  
   // G-code origin canvas position (captured when G-code is drawn)
        private Point? _gcodeOriginCanvasPosition = null;
      
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

        private bool _isTopViewMaximized = false;
    private GridLength _savedRow0Height = new GridLength(1, GridUnitType.Star);
    private GridLength _savedRow2Height = new GridLength(1, GridUnitType.Star);
        private GridLength _savedCol0Width = new GridLength(1, GridUnitType.Star);
    private GridLength _savedCol2Width = new GridLength(1, GridUnitType.Star);

        // Execution tracking
      private DateTime _executionStartTime;
        private string _currentFileName = "";

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
 _executionModalValuesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
 _executionModalValuesTimer.Tick += (s, e) =>
       {
   try
    {
          if (App.MainController?.IsGCodeRunning == true)
        {
 // Update overrides + progress panel each tick (live time / remaining)
     UpdateOverridesFromExecutionManager();
     UpdateTimeBasedProgress();
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

SetActiveTab(true); // default to G-Code tab

            if (App.MainController != null)
                App.MainController.CommandBlockedDueToHold += OnCommandBlockedDueToHold;

    // Load LiveFit data at startup
      LoadTableDimensionsFromSettings(); // in LiveFit partial
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

     public GCodeOverlayManager GetOverlayManager() => _overlayManager;

        // Method to start modal value updates during execution (also drives periodic progress refresh)
   private void StartExecutionModalUpdates()
   {
            try
            {
  if (!_executionModalValuesTimer.IsEnabled && App.MainController?.IsGCodeRunning == true)
    {
         _executionModalValuesTimer.Start();
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

    private void ToggleTopViewMaximize()
        {
            if (ViewTabContent.Visibility != Visibility.Visible) return;
     var layoutRoot = ViewTabContent.Children[0] as Grid;
            if (layoutRoot == null) return;

    bool willMaximize = !_isTopViewMaximized;

       if (willMaximize)
          {
   _savedRow0Height = layoutRoot.RowDefinitions[0].Height;
     _savedRow2Height = layoutRoot.RowDefinitions[2].Height;
         _savedCol0Width = layoutRoot.ColumnDefinitions[0].Width;
    _savedCol2Width = layoutRoot.ColumnDefinitions[2].Width;

                layoutRoot.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
      layoutRoot.RowDefinitions[2].Height = new GridLength(0);
      layoutRoot.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
         layoutRoot.ColumnDefinitions[2].Width = new GridLength(0);

         if (RightViewBorder != null) RightViewBorder.Visibility = Visibility.Collapsed;
        if (FrontViewBorder != null) FrontViewBorder.Visibility = Visibility.Collapsed;
     if (IsoViewBorder != null) IsoViewBorder.Visibility = Visibility.Collapsed;
     if (VerticalSplitter != null) VerticalSplitter.Visibility = Visibility.Collapsed;
        if (HorizontalSplitter != null) HorizontalSplitter.Visibility = Visibility.Collapsed;
     _isTopViewMaximized = true;
          }
    else
          {
           layoutRoot.RowDefinitions[0].Height = _savedRow0Height;
         layoutRoot.RowDefinitions[2].Height = _savedRow2Height;
 layoutRoot.ColumnDefinitions[0].Width = _savedCol0Width;
   layoutRoot.ColumnDefinitions[2].Width = _savedCol2Width;

      if (RightViewBorder != null) RightViewBorder.Visibility = Visibility.Visible;
           if (FrontViewBorder != null) FrontViewBorder.Visibility = Visibility.Visible;
      if (IsoViewBorder != null) IsoViewBorder.Visibility = Visibility.Visible;
       if (VerticalSplitter != null) VerticalSplitter.Visibility = Visibility.Visible;
       if (HorizontalSplitter != null) HorizontalSplitter.Visibility = Visibility.Visible;
        _isTopViewMaximized = false;
         }

   try
            {
       if (TopViewMaxToggleButton != null)
        {
     TopViewMaxToggleButton.Content = _isTopViewMaximized ? "❐" : "⛶"; // ❐ restore, ⛶ maximize
            TopViewMaxToggleButton.ToolTip = _isTopViewMaximized ? "Restore views" : "Maximize top view";
        }
   }
            catch { }

        Dispatcher.BeginInvoke(new Action(() =>
     {
              try
     {
       _fileService?.RedrawTopViewport();
             _overlayManager?.OnCanvasSizeChanged();
  }
              catch { }
       }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TopViewHeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleTopViewMaximize();
        }

        private void TopViewHeaderBar_TouchDown(object sender, TouchEventArgs e)
 {
ToggleTopViewMaximize();
       e.Handled = true;
    }

private void TopViewMaxToggleButton_Click(object sender, RoutedEventArgs e)
    {
 ToggleTopViewMaximize();
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
    }
}