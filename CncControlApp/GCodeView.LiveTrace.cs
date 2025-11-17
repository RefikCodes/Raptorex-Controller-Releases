using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CncControlApp
{
    /// <summary>
    /// GCodeView partial class - Live coordinate tracing during G-code execution
    /// Shows real-time machine path as execution progresses
    /// </summary>
    public partial class GCodeView
    {
        #region Fields

     private Canvas _liveTraceCanvas;
        private readonly List<Point> _liveTracePoints = new List<Point>();
      private Polyline _liveTracePath;
        private Ellipse _livePositionMarker;
        private DispatcherTimer _liveTraceTimer;
   private bool _isLiveTracingActive = false;

        // Last recorded position for trace optimization
    private double _lastTraceX = double.NaN;
        private double _lastTraceY = double.NaN;
        private const double MinTraceDistanceMm = 0.5; // Minimum distance to record new point (mm)

        #endregion

        #region Initialization

     /// <summary>
/// Initialize live tracing components
        /// Call this during GCodeView initialization
 /// </summary>
        private void InitializeLiveTrace()
        {
   try
      {
    // Find or create live trace canvas layer
        if (TopViewCanvas != null && TopViewOverlayCanvas != null)
      {
      // Live trace canvas sits between main canvas and overlay
      _liveTraceCanvas = new Canvas
      {
  Background = Brushes.Transparent,
     IsHitTestVisible = false,
             Visibility = Visibility.Collapsed
       };
          
     // Add to same parent grid as other canvases
      var parent = TopViewCanvas.Parent as Grid;
     if (parent != null)
        {
             // Insert between main canvas and overlay
            int mainIndex = parent.Children.IndexOf(TopViewCanvas);
   parent.Children.Insert(mainIndex + 1, _liveTraceCanvas);
      
           // Set grid position
     Grid.SetRow(_liveTraceCanvas, Grid.GetRow(TopViewCanvas));
      Grid.SetColumn(_liveTraceCanvas, Grid.GetColumn(TopViewCanvas));
      
      // Set z-index (between main canvas and overlay)
     Panel.SetZIndex(_liveTraceCanvas, Panel.GetZIndex(TopViewCanvas) + 1);
     }
         
       System.Diagnostics.Debug.WriteLine("✅ Live trace canvas initialized");
   }

 // Create trace timer (50ms interval for smooth updates)
  _liveTraceTimer = new DispatcherTimer
              {
    Interval = TimeSpan.FromMilliseconds(50)
      };
       _liveTraceTimer.Tick += LiveTraceTimer_Tick;

                System.Diagnostics.Debug.WriteLine("✅ Live trace timer created");
   }
 catch (Exception ex)
            {
    System.Diagnostics.Debug.WriteLine($"❌ InitializeLiveTrace error: {ex.Message}");
       }
        }

        #endregion

        #region Start/Stop Control

        /// <summary>
        /// Start live coordinate tracing
        /// </summary>
        private void StartLiveTrace()
   {
         try
            {
if (_isLiveTracingActive) return;

    System.Diagnostics.Debug.WriteLine("🎯 Starting live trace...");

  // Clear previous trace
    ClearLiveTrace();

                // Reset last position
     _lastTraceX = double.NaN;
       _lastTraceY = double.NaN;

   // Create trace path
    _liveTracePath = new Polyline
                {
Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 215, 0)), // Semi-transparent gold
               StrokeThickness = 3,
      StrokeLineJoin = PenLineJoin.Round,
         StrokeStartLineCap = PenLineCap.Round,
  StrokeEndLineCap = PenLineCap.Round,
       IsHitTestVisible = false
        };
           _liveTraceCanvas?.Children.Add(_liveTracePath);

       // Create live position marker (animated)
            _livePositionMarker = new Ellipse
    {
   Width = 12,
          Height = 12,
         Fill = new SolidColorBrush(Color.FromArgb(150, 255, 215, 0)),
       Stroke = new SolidColorBrush(Colors.Gold),
StrokeThickness = 2,
      IsHitTestVisible = false
          };
       _liveTraceCanvas?.Children.Add(_livePositionMarker);

       // Show trace canvas
    if (_liveTraceCanvas != null)
         {
       _liveTraceCanvas.Visibility = Visibility.Visible;
       }

                // Start update timer
                _isLiveTracingActive = true;
      _liveTraceTimer?.Start();

            System.Diagnostics.Debug.WriteLine("✅ Live trace started");
     }
         catch (Exception ex)
     {
    System.Diagnostics.Debug.WriteLine($"❌ StartLiveTrace error: {ex.Message}");
     }
      }

        /// <summary>
        /// Stop live coordinate tracing
        /// </summary>
        private void StopLiveTrace()
     {
          try
            {
 if (!_isLiveTracingActive) return;

 System.Diagnostics.Debug.WriteLine("🛑 Stopping live trace...");

         // Stop timer
    _isLiveTracingActive = false;
       _liveTraceTimer?.Stop();

        // Fade out trace after a delay
    var fadeTimer = new DispatcherTimer
           {
       Interval = TimeSpan.FromSeconds(2)
        };
  fadeTimer.Tick += (s, e) =>
          {
          fadeTimer.Stop();
 ClearLiveTrace();
                };
      fadeTimer.Start();

       System.Diagnostics.Debug.WriteLine("✅ Live trace stopped");
            }
            catch (Exception ex)
            {
    System.Diagnostics.Debug.WriteLine($"❌ StopLiveTrace error: {ex.Message}");
    }
        }

        /// <summary>
      /// Clear live trace
        /// </summary>
        private void ClearLiveTrace()
{
            try
            {
         _liveTracePoints.Clear();
          
    if (_liveTraceCanvas != null)
                {
         _liveTraceCanvas.Children.Clear();
  _liveTraceCanvas.Visibility = Visibility.Collapsed;
   }

 _liveTracePath = null;
      _livePositionMarker = null;

                _lastTraceX = double.NaN;
                _lastTraceY = double.NaN;
            }
            catch (Exception ex)
        {
        System.Diagnostics.Debug.WriteLine($"❌ ClearLiveTrace error: {ex.Message}");
     }
        }

        #endregion

        #region Update Logic

 /// <summary>
        /// Live trace timer tick - update trace path and position marker
        /// </summary>
        private void LiveTraceTimer_Tick(object sender, EventArgs e)
        {
    try
            {
                if (!_isLiveTracingActive) return;
        if (_liveTraceCanvas == null || App.MainController?.MStatus == null) return;

         // Get current machine position
   double machineX = App.MainController.MStatus.X;
        double machineY = App.MainController.MStatus.Y;

       // Validate coordinates
            if (double.IsNaN(machineX) || double.IsInfinity(machineX) ||
         double.IsNaN(machineY) || double.IsInfinity(machineY))
    {
       return;
   }

      // Convert machine coordinates to canvas coordinates
         if (!TryConvertMachineToCanvas(machineX, machineY, out Point canvasPoint))
                {
       return;
        }

        // Check if position has changed enough to record
             bool shouldRecord = double.IsNaN(_lastTraceX) || double.IsNaN(_lastTraceY);
    if (!shouldRecord)
                {
        double dx = Math.Abs(machineX - _lastTraceX);
    double dy = Math.Abs(machineY - _lastTraceY);
   double distance = Math.Sqrt(dx * dx + dy * dy);
                shouldRecord = distance >= MinTraceDistanceMm;
      }

          // Add point to trace path if moved enough
    if (shouldRecord)
        {
       _liveTracePoints.Add(canvasPoint);
    _lastTraceX = machineX;
            _lastTraceY = machineY;

          // Update polyline
       if (_liveTracePath != null)
          {
          _liveTracePath.Points.Add(canvasPoint);
           }

          // Limit trace points to prevent memory issues (keep last 5000 points)
            if (_liveTracePoints.Count > 5000)
        {
      _liveTracePoints.RemoveAt(0);
           if (_liveTracePath != null && _liveTracePath.Points.Count > 0)
    {
     _liveTracePath.Points.RemoveAt(0);
             }
                    }
   }

                // Update live position marker (always update even if not recording point)
             if (_livePositionMarker != null)
  {
    Canvas.SetLeft(_livePositionMarker, canvasPoint.X - 6);
           Canvas.SetTop(_livePositionMarker, canvasPoint.Y - 6);
     }
         }
        catch (Exception ex)
       {
     System.Diagnostics.Debug.WriteLine($"❌ LiveTraceTimer_Tick error: {ex.Message}");
   }
        }

      #endregion

        #region Coordinate Conversion

        /// <summary>
        /// Convert machine coordinates (mm) to canvas pixel coordinates
     /// Uses same transform as G-code drawing for consistency
      /// </summary>
    private bool TryConvertMachineToCanvas(double machineX, double machineY, out Point canvasPoint)
        {
   canvasPoint = new Point();

            try
  {
        if (TopViewCanvas == null) return false;

     double canvasWidth = TopViewCanvas.ActualWidth;
          double canvasHeight = TopViewCanvas.ActualHeight;

                if (canvasWidth <= 0 || canvasHeight <= 0) return false;

            // Use WorkspaceTransform for consistent coordinate mapping
     if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
   {
           return false;
       }

         // Convert machine coordinates to canvas coordinates
        canvasPoint = xf.ToCanvas(machineX, machineY);
 return true;
            }
            catch (Exception ex)
     {
        System.Diagnostics.Debug.WriteLine($"❌ TryConvertMachineToCanvas error: {ex.Message}");
     return false;
        }
        }

        #endregion

        #region Execution State Management

        /// <summary>
        /// Handle execution state changes to control live tracing
        /// Call this from execution state change handlers
        /// </summary>
  private void HandleExecutionStateForLiveTrace(bool isRunning)
  {
            try
    {
                if (isRunning)
                {
    // Start tracing when execution begins
        StartLiveTrace();
         }
     else
                {
 // Stop tracing when execution ends
    StopLiveTrace();
             }
            }
            catch (Exception ex)
        {
     System.Diagnostics.Debug.WriteLine($"❌ HandleExecutionStateForLiveTrace error: {ex.Message}");
      }
        }

        #endregion
    }
}
