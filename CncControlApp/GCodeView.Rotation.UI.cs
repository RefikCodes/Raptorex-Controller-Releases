using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.ComponentModel;
using CncControlApp.Helpers; // ✅ ADD: For EventHandlerHelper

namespace CncControlApp
{
    public partial class GCodeView
    {
        public (bool Fits, string Details) CheckLiveFitAtAngle(double angleDegrees)
 {
         try
            {
    if (_fileService == null) return (false, "No file service");
          var segments = _fileService.GCodeSegments;
      if (segments == null || segments.Count == 0)
           return (true, "No G-Code loaded");

                if (App.MainController?.Settings == null || App.MainController.Settings.Count == 0)
       return (false, "Table dimensions not loaded");

                var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
          var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);
                if (xLimit == null || yLimit == null) return (false, "Missing $130/$131");
    if (!double.TryParse(xLimit.Value, System.Globalization.NumberStyles.Any,
             System.Globalization.CultureInfo.InvariantCulture, out double tableMaxX) ||
           !double.TryParse(yLimit.Value, System.Globalization.NumberStyles.Any,
           System.Globalization.CultureInfo.InvariantCulture, out double tableMaxY))
      return (false, "Invalid table dimensions");

         if (tableMaxX <= 0 || tableMaxY <= 0) return (false, "Invalid table dimensions");

             var b = CalculateRotatedBounds(segments, angleDegrees);

              double originMachineX = 0, originMachineY = 0;
    if (App.MainController?.MStatus != null)
            {
               originMachineX = App.MainController.MStatus.X;
    originMachineY = App.MainController.MStatus.Y;
                }

      double absMinX = b.MinX + originMachineX;
                double absMaxX = b.MaxX + originMachineX;
    double absMinY = b.MinY + originMachineY;
    double absMaxY = b.MaxY + originMachineY;

   bool fitsX = absMinX >= 0 && absMaxX <= tableMaxX;
       bool fitsY = absMinY >= 0 && absMaxY <= tableMaxY;
     bool fits = fitsX && fitsY;

                string details;
          if (fits)
      {
        details = $"Inside table {tableMaxX:F0}×{tableMaxY:F0} (X:[{absMinX:F1},{absMaxX:F1}] Y:[{absMinY:F1},{absMaxY:F1}])";
    }
  else
          {
     var parts = new System.Collections.Generic.List<string>();
      if (absMinX < 0) parts.Add($"left {Math.Abs(absMinX):F1}mm");
      if (absMaxX > tableMaxX) parts.Add($"right {(absMaxX - tableMaxX):F1}mm");
   if (absMinY < 0) parts.Add($"front {Math.Abs(absMinY):F1}mm");
             if (absMaxY > tableMaxY) parts.Add($"back {(absMaxY - tableMaxY):F1}mm");
details = $"Out of bounds: {string.Join(", ", parts)}";
           }

             return (fits, details);
            }
          catch (Exception ex)
     {
     System.Diagnostics.Debug.WriteLine($"CheckLiveFitAtAngle error: {ex.Message}");
   return (false, $"Error: {ex.Message}");
            }
   }

        public double GetCurrentRotationAngle()
        {
            return _currentRotationAngle;
      }

 public void SetRotationAngle(double angleDegrees)
        {
    try
   {
                _currentRotationAngle = angleDegrees;
       if (RotationAngleSlider != null && Math.Abs(RotationAngleSlider.Value - angleDegrees) > 0.0001)
    RotationAngleSlider.Value = angleDegrees;

        UpdateStatusBar();
    UpdateXDistanceLiveFromRotationThrottled();
}
            catch (Exception ex)
         {
       System.Diagnostics.Debug.WriteLine($"SetRotationAngle error: {ex.Message}");
            }
  }

public void ResetRotation()
        {
 try
  {
                if (RotationAngleSlider != null)
        RotationAngleSlider.Value = 0;
             else
            SetRotationAngle(0);
         
     if (TopViewCanvas != null)
          {
 TopViewCanvas.RenderTransform = null;
       }
   }
  catch (Exception ex)
      {
  System.Diagnostics.Debug.WriteLine($"ResetRotation error: {ex.Message}");
    }
    }

        public async Task ApplyRotation()
        {
 GCodeProgressDialog progressDialog = null;
 try
 {
 double uiAngle = (RotationAngleSlider?.Value) ?? _currentRotationAngle;
 if (DisplayGCodeLines == null || DisplayGCodeLines.Count ==0) return;
 if (Math.Abs(uiAngle) <0.0001) return;

 // Progress dialog setup and determinate switch (so color fill appears)
 var popupOwner = Application.Current?.Windows.OfType<Window>()
 .FirstOrDefault(w => w is Controls.RotationPopup) ?? Application.Current?.MainWindow;

 progressDialog = new GCodeProgressDialog { Owner = popupOwner };
 progressDialog.ResetProgress();
 progressDialog.UpdateStatus("G-Code Döndürülüyor");
 progressDialog.UpdateProgress($"Açı: {uiAngle:F1}° → G-Code: {-uiAngle:F1}°");
 progressDialog.Show();
 await Task.Yield();
 progressDialog.SetProgress(10);

 //1) Rotate in background
 progressDialog.UpdateProgress("G-Code döndürülüyor...");
 double gcodeAngle = -uiAngle;
 var originalLines = DisplayGCodeLines.Select(l => l.GCodeLine).ToList();
 var outcome = await Task.Run(() =>
 RotatedGCodeGenerator.GenerateRotatedArbitraryAngleWithOutcome(
 originalLines,
 gcodeAngle,
 RotatedGCodeGenerator.RotationPivotMode.Origin,
 false,
 null,
 null,
3));

 if (outcome == null || outcome.Lines == null || outcome.Lines.Count ==0)
 {
 progressDialog.UpdateStatus("Hata!");
 progressDialog.UpdateProgress("Döndürme işlemi başarısız oldu");
 await Task.Delay(1200);
 return;
 }

 var rotated = outcome.Lines;
 _rotationAppliedShiftX =0;
 _rotationAppliedShiftY =0;
 App.MainController?.AddLogMessage("> 🔄 Rotation applied without normalization - origin preserved");
 progressDialog.SetProgress(30);

 //2) Prepare new items off UI thread
 progressDialog.UpdateProgress("Satırlar hazırlanıyor...");
 var displayItems = await Task.Run(() =>
 rotated.Select((line, i) => new GCodeLineItem { LineNumber = i +1, GCodeLine = line }).ToList());
 progressDialog.SetProgress(45);

 //3) Apply collections on UI thread
 await Application.Current.Dispatcher.InvokeAsync(() =>
 {
 bool countEqual = rotated.Count == DisplayGCodeLines.Count;
 if (countEqual)
 {
 for (int i =0; i < rotated.Count; i++)
 {
 var item = DisplayGCodeLines[i];
 var newText = rotated[i];
 if (!ReferenceEquals(item.GCodeLine, newText) && item.GCodeLine != newText)
 item.GCodeLine = newText;
 }
 }
 else
 {
 DisplayGCodeLines = new System.Collections.ObjectModel.ObservableCollection<GCodeLineItem>(displayItems);
 }

 if (_fileService?.GCodeLines != null)
 {
 _fileService.GCodeLines.Clear();
 foreach (var it in displayItems)
 _fileService.GCodeLines.Add(it);
 }

 if (App.MainController != null)
 {
 App.MainController.GCodeLines.Clear();
 foreach (var line in rotated)
 if (!string.IsNullOrWhiteSpace(line))
 App.MainController.GCodeLines.Add(line);
 App.MainController.GCodeManager?.ResetExecutionState();
 }
 
 // Update file info (line count) in UI if count changed
 try { _fileService?.NotifyFileInfoChangedForCurrentLines("rotation"); } catch { }
 }, System.Windows.Threading.DispatcherPriority.Background);
 progressDialog.UpdateProgress("Satırlar güncellendi");
 progressDialog.SetProgress(55);

 //4) Reprocess (no per-line UI updates to avoid overhead)
 progressDialog.UpdateProgress("Görünümler yeniden çiziliyor...");
 progressDialog.SetProgress(60);
 await _fileService?.ReprocessCurrentLinesAsNewFileAsync(
 _ => { /* no-op to avoid dispatcher churn */ },
 false);
 progressDialog.SetProgress(95);

 // After reprocess and before resetting UI, refresh statistics
 try
 {
 if (_fileService?.GCodeSegments != null && _fileService.GCodeSegments.Count >0)
 {
 // Recalculate execution time & segment-based estimates
 App.MainController?.CalculateEstimatedExecutionTime(_fileService.GCodeSegments);
 // Cache unique Z layers for layer tracking (progress / remaining time by layer)
 App.MainController?.GCodeManager?.CacheUniqueZLayers(_fileService.GCodeSegments);
 }
 }
 catch { }

 //5) Finalize UI
 await Application.Current.Dispatcher.InvokeAsync(() =>
 {
 if (TopViewCanvas != null) TopViewCanvas.RenderTransform = null;
 if (RotationAngleSlider != null) RotationAngleSlider.Value =0;
 _currentRotationAngle =0;
 });

 string fitStatus = _enableFitOnRotation ? "with FIT" : "without FIT";
 App.MainController?.AddLogMessage($"> 🔄 Rotation applied {fitStatus}: {uiAngle:F1}° UI -> {gcodeAngle:F1}° G-Code");

 progressDialog.ShowCompletionAndAutoClose($"Döndürme tamamlandı: {uiAngle:F1}°",800);
 progressDialog = null;
 UpdateStatusBar();
 }
 catch (Exception ex)
 {
 System.Diagnostics.Debug.WriteLine($"ApplyRotation error: {ex.Message}");
 App.MainController?.AddLogMessage($"> ❌ Rotation error: {ex.Message}");
 if (progressDialog != null)
 {
 progressDialog.UpdateStatus("Hata!");
 progressDialog.UpdateProgress($"Hata: {ex.Message}");
 await Task.Delay(900);
 }
 }
 finally
 {
 try
 {
 if (progressDialog != null)
 progressDialog.CloseDialog();
 }
 catch { }
 }
 }
        private void RotationAngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
     {
            try
            {
 _currentRotationAngle = e.NewValue;
     UpdateStatusBar();
      UpdateXDistanceLiveFromRotationThrottled();
     }
          catch (Exception ex)
{
                System.Diagnostics.Debug.WriteLine($"RotationAngleSlider_ValueChanged error: {ex.Message}");
}
}

        private void ApplySimpleRotation(double angleDegrees)
        {
 try
       {
  if (TopViewCanvas == null) return;
       var rotateTransform = new RotateTransform { Angle = angleDegrees };
        TopViewCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
    TopViewCanvas.RenderTransform = rotateTransform;
  }
            catch (Exception ex)
   {
  System.Diagnostics.Debug.WriteLine($"ApplySimpleRotation error: {ex.Message}");
       }
        }

        private void ApplyAngleRotationButton_Click(object sender, RoutedEventArgs e)
        {
try
       {
                double uiAngle = RotationAngleSlider?.Value ?? 0;
      if (DisplayGCodeLines == null || DisplayGCodeLines.Count == 0) return;
    if (Math.Abs(uiAngle) < 0.0001) return;

      double gcodeAngle = -uiAngle;
         var originalLines = DisplayGCodeLines.Select(l => l.GCodeLine).ToList();

   var rotated = RotatedGCodeGenerator.GenerateRotatedArbitraryAngle(
      originalLines,
          gcodeAngle,
     RotatedGCodeGenerator.RotationPivotMode.Origin,
    _enableFitOnRotation,
null,
             null,
 3);

if (rotated == null || rotated.Count == 0) return;

          if (rotated.Count == DisplayGCodeLines.Count)
 {
        for (int i = 0; i < rotated.Count; i++)
     {
     DisplayGCodeLines[i].GCodeLine = rotated[i];
              DisplayGCodeLines[i].ResetStatus();
              }
    }
         else
      {
         DisplayGCodeLines.Clear();
       for (int i = 0; i < rotated.Count; i++)
          {
      DisplayGCodeLines.Add(new GCodeLineItem
      {
         LineNumber = i + 1,
               GCodeLine = rotated[i]
  });
         }
   }

      if (App.MainController != null)
      {
      App.MainController.GCodeLines.Clear();
  foreach (var line in rotated)
        if (!string.IsNullOrWhiteSpace(line))
      App.MainController.GCodeLines.Add(line);
  App.MainController.GCodeManager?.ResetExecutionState();
    }

    _fileService?.RedrawAllViewports();

    string fitStatus = _enableFitOnRotation ? "with FIT" : "without FIT";
       App.MainController?.AddLogMessage($"> 🔄 Rotation applied {fitStatus}: {uiAngle:F1}° UI -> {gcodeAngle:F1}° G-Code");

     UpdateStatusBar();
            }
    catch (Exception ex)
          {
       System.Diagnostics.Debug.WriteLine($"ApplyAngleRotationButton_Click error: {ex.Message}");
                App.MainController?.AddLogMessage($"> ❌ Rotation error: {ex.Message}");
    }
        }

      private void RotationResetButton_Click(object sender, RoutedEventArgs e)
        {
  try
       {
       if (RotationAngleSlider != null)
     RotationAngleSlider.Value = 0;
        }
      catch (Exception ex)
            {
        System.Diagnostics.Debug.WriteLine($"RotationResetButton_Click error: {ex.Message}");
            }
        }

        // ✅ REFACTORED: Touch handlers using helper pattern
        private void RotationAngleSlider_TouchDown(object sender, TouchEventArgs e)
        {
            HandleRotationSliderTouch(sender, e, isStart: true);
        }

 private void RotationAngleSlider_TouchMove(object sender, TouchEventArgs e)
    {
            HandleRotationSliderTouch(sender, e, isStart: false);
        }

private void RotationAngleSlider_TouchUp(object sender, TouchEventArgs e)
        {
    try
            {
      var slider = sender as Slider;
     if (slider != null && slider.AreAnyTouchesCaptured)
 slider.ReleaseTouchCapture(e.TouchDevice);

     RestoreRightMenuPanning();
 _rotationTouchActive = false;
  e.Handled = true;
            }
   catch (Exception ex)
            {
         System.Diagnostics.Debug.WriteLine($"❌ RotationAngleSlider_TouchUp error: {ex.Message}");
       e.Handled = true;
       }
    }

        private void RotationAngleSlider_LostTouchCapture(object sender, TouchEventArgs e)
        {
      try
          {
      RestoreRightMenuPanning();
      _rotationTouchActive = false;
    e.Handled = true;
            }
     catch (Exception ex)
   {
        System.Diagnostics.Debug.WriteLine($"❌ RotationAngleSlider_LostTouchCapture error: {ex.Message}");
      }
        }

    // ✅ NEW: Consolidated touch handling logic
        private void HandleRotationSliderTouch(object sender, TouchEventArgs e, bool isStart)
        {
    try
            {
     var slider = sender as Slider;
       if (slider == null) return;

     // Start: Capture touch
     if (isStart)
     {
                 if (!slider.CaptureTouch(e.TouchDevice))
          {
              System.Diagnostics.Debug.WriteLine("❌ Failed to capture touch for rotation slider");
              return;
    }
         DisableRightMenuPanning();
          _rotationTouchActive = true;
          }
     // Move: Check if touch is active
  else if (!_rotationTouchActive)
      {
       return;
    }

            // Update slider value based on touch position
          Point touchPoint = e.GetTouchPoint(slider).Position;
    double sliderWidth = slider.ActualWidth;

         if (sliderWidth > 0)
   {
            double relativePosition = Math.Max(0, Math.Min(1, touchPoint.X / sliderWidth));
                double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
    slider.Value = newValue;
            }

           e.Handled = true;
}
      catch (Exception ex)
{
         System.Diagnostics.Debug.WriteLine($"❌ HandleRotationSliderTouch error: {ex.Message}");
    if (isStart)
        RestoreRightMenuPanning();
        e.Handled = true;
            }
      }

        private void UpdateRotationRunningState()
      {
       try
    {
       bool running = App.MainController?.IsGCodeRunning == true;
         if (RotationAngleSlider != null) RotationAngleSlider.IsEnabled = !running;
        if (RotationResetButton != null) RotationResetButton.IsEnabled = !running;
        if (ApplyAngleRotationButton != null) ApplyAngleRotationButton.IsEnabled = !running;

 if (TopViewCanvas != null)
      {
        TopViewCanvas.IsEnabled = !running;
          TopViewCanvas.IsHitTestVisible = !running;
      }
       if (TopViewOverlayCanvas != null)
        {
               TopViewOverlayCanvas.IsHitTestVisible = false;
       }
   }
      catch { }
  }

  private (double MinX, double MaxX, double MinY, double MaxY) CalculateRotatedBounds(
            System.Collections.Generic.List<GCodeSegment> segments, double angleDegrees)
     {
    try
         {
     if (segments == null || segments.Count == 0)
      return (0, 0, 0, 0);

    double angleRadians = angleDegrees * Math.PI / 180.0;
        double cosAngle = Math.Cos(angleRadians);
          double sinAngle = Math.Sin(angleRadians);

                double minX = double.PositiveInfinity;
      double minY = double.PositiveInfinity;
  double maxX = double.NegativeInfinity;
 double maxY = double.NegativeInfinity;

     Action<double, double> addRotated = (x, y) =>
   {
             double rx = x * cosAngle - y * sinAngle;
               double ry = x * sinAngle + y * cosAngle;
     if (rx < minX) minX = rx;
   if (ry < minY) minY = ry;
          if (rx > maxX) maxX = rx;
         if (ry > maxY) maxY = ry;
      };

       Func<double, double> norm2Pi = a =>
                {
          double t = a % (2.0 * Math.PI);
        if (t < 0) t += 2.0 * Math.PI;
        return t;
                };

        Func<double, double, double, bool, bool> angleInSweep = (start, end, test, clockwise) =>
    {
  start = norm2Pi(start);
        end = norm2Pi(end);
         test = norm2Pi(test);

if (clockwise)
        {
if (start < end) start += 2.0 * Math.PI;
       if (test < end) test += 2.0 * Math.PI;
      return test <= start && test >= end;
       }
      else
        {
 if (end < start) end += 2.0 * Math.PI;
             if (test < start) test += 2.0 * Math.PI;
    return test >= start && test <= end;
}
        };

  foreach (var segment in segments)
     {
      addRotated(segment.StartPoint.X, segment.StartPoint.Y);
               addRotated(segment.EndPoint.X, segment.EndPoint.Y);

  bool isArc = false;
        bool isClockwise = false;
          double cx = 0, cy = 0, r = 0;

     try
     {
          isClockwise = segment.IsClockwise;
          double i = segment.ArcCenterI;
          double j = segment.ArcCenterJ;
        double? rr = segment.ArcRadius;

         isArc = (Math.Abs(i) > 1e-12 || Math.Abs(j) > 1e-12) || rr.HasValue;

     if (isArc)
             {
      if (Math.Abs(i) > 1e-12 || Math.Abs(j) > 1e-12)
          {
       cx = segment.StartPoint.X + i;
        cy = segment.StartPoint.Y + j;
   r = Math.Sqrt((segment.StartPoint.X - cx) * (segment.StartPoint.X - cx) +
            (segment.StartPoint.Y - cy) * (segment.StartPoint.Y - cy));
    }
        else if (rr.HasValue)
           {
          continue;
          }

            if (r > 1e-12)
 {
             double sx = segment.StartPoint.X - cx;
            double sy = segment.StartPoint.Y - cy;
         double ex = segment.EndPoint.X - cx;
  double ey = segment.EndPoint.Y - cy;

          double thStart = Math.Atan2(sy, sx);
         double thEnd = Math.Atan2(ey, ex);

     var cardinals = new[] { 0.0, Math.PI * 0.5, Math.PI, Math.PI * 1.5 };
             foreach (var th in cardinals)
   {
   if (angleInSweep(thStart, thEnd, th, isClockwise))
      {
    double px = cx + r * Math.Cos(th);
          double py = cy + r * Math.Sin(th);
         addRotated(px, py);
       }
           }
 }
          }
  }
        catch
        {
        }
             }

                if (double.IsInfinity(minX) || double.IsInfinity(minY) ||
     double.IsInfinity(maxX) || double.IsInfinity(maxY))
         {
           return (0, 0, 0, 0);
}

                return (minX, maxX, minY, maxY);
            }
            catch (Exception ex)
      {
                System.Diagnostics.Debug.WriteLine($"❌ CalculateRotatedBounds error: {ex.Message}");
       return (0, 0, 0, 0);
    }
        }

        public void RedrawPopupTopView(Canvas targetCanvas, Canvas targetOverlayCanvas)
        {
       try
          {
                if (targetCanvas == null || _fileService == null) return;

  var segments = _fileService.GCodeSegments;
       if (segments == null || segments.Count == 0)
     {
      targetCanvas.Children.Clear();
      targetOverlayCanvas?.Children.Clear();
    return;
    }

    if (targetCanvas.ActualWidth < 10 || targetCanvas.ActualHeight < 10)
              {
  targetCanvas.Loaded += (s, e) => RedrawPopupTopView(targetCanvas, targetOverlayCanvas);
        return;
                }

      Application.Current.Dispatcher.BeginInvoke(new Action(() =>
 {
        try
        {
     var visualization = _fileService.GetVisualization();
               if (visualization != null)
         {
        var method = visualization.GetType().GetMethod("RenderTopViewSimple",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

               if (method != null)
                 {
       targetCanvas.Children.Clear();
     targetOverlayCanvas?.Children.Clear();

             double width = targetCanvas.ActualWidth;
   double height = targetCanvas.ActualHeight;

        method.Invoke(visualization, new object[] { targetCanvas, targetOverlayCanvas, segments, width, height });

        if (targetOverlayCanvas != null && _overlayManager != null)
    {
       DrawPopupOverlay(targetOverlayCanvas, width, height);
 }
       }
         }
   }
              catch (Exception ex)
     {
        System.Diagnostics.Debug.WriteLine($"RedrawPopupTopView inner error: {ex.Message}");
    }
    }), System.Windows.Threading.DispatcherPriority.Render);
  }
   catch (Exception ex)
   {
      System.Diagnostics.Debug.WriteLine($"RedrawPopupTopView error: {ex.Message}");
     }
        }

        private void DrawPopupOverlay(Canvas overlayCanvas, double width, double height)
    {
        try
     {
         if (overlayCanvas == null) return;

    overlayCanvas.Children.Clear();

         if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(width, height, out var xf)) return;

       var corners = xf.GetTableCornersOnCanvas();
                double left = corners.TL.X;
     double top = corners.TL.Y;
           double right = corners.TR.X;
    double bottom = corners.BR.Y;
      double rectWidth = right - left;
         double rectHeight = bottom - top;

     var tableBorder = new System.Windows.Shapes.Rectangle
     {
     Width = rectWidth,
           Height = rectHeight,
        Stroke = new SolidColorBrush(Color.FromArgb(180, 100, 100, 255)),
StrokeThickness = 2,
  StrokeDashArray = new DoubleCollection { 5, 3 }
 };
                Canvas.SetLeft(tableBorder, left);
      Canvas.SetTop(tableBorder, top);
    overlayCanvas.Children.Add(tableBorder);

     var originCanvas = xf.ToCanvas(0, 0);
          var originMarker = new Ellipse
     {
                    Width = 8,
      Height = 8,
          Fill = new SolidColorBrush(Colors.Red)
    };
       Canvas.SetLeft(originMarker, originCanvas.X - 4);
     Canvas.SetTop(originMarker, originCanvas.Y - 4);
       overlayCanvas.Children.Add(originMarker);

       var originLabel = new TextBlock
       {
    Text = "0,0",
         Foreground = new SolidColorBrush(Colors.Red),
       FontSize = 11,
           FontWeight = FontWeights.Bold
        };
        Canvas.SetLeft(originLabel, originCanvas.X + 8);
    Canvas.SetTop(originLabel, originCanvas.Y - 12);
       overlayCanvas.Children.Add(originLabel);

        var dimensionsLabel = new TextBlock
  {
           Text = $"Table: {xf.MaxX:F0} × {xf.MaxY:F0} mm",
       Foreground = new SolidColorBrush(Color.FromArgb(200, 100, 100, 255)),
         FontSize = 12,
       FontWeight = FontWeights.Bold
             };
      Canvas.SetLeft(dimensionsLabel, left + rectWidth / 2 - 60);
         Canvas.SetTop(dimensionsLabel, top - 25);
       overlayCanvas.Children.Add(dimensionsLabel);

     if (App.MainController?.MStatus != null)
         {
                    double machineX = App.MainController.MStatus.X;
        double machineY = App.MainController.MStatus.Y;
    
        var machinePos = xf.ToCanvas(machineX, machineY);

         var machineMarker = new Ellipse
         {
             Width = 16,
              Height = 16,
   Stroke = new SolidColorBrush(Colors.Lime),
         StrokeThickness = 3,
       Fill = new SolidColorBrush(Color.FromArgb(100, 0, 255, 0))
    };
        Canvas.SetLeft(machineMarker, machinePos.X - 8);
  Canvas.SetTop(machineMarker, machinePos.Y - 8);
           overlayCanvas.Children.Add(machineMarker);

        var machineLabel = new TextBlock
           {
      Text = $"M:{machineX:F2},{machineY:F2}",
     Foreground = new SolidColorBrush(Colors.Lime),
         FontSize = 10,
FontWeight = FontWeights.Bold
          };
            Canvas.SetLeft(machineLabel, machinePos.X + 12);
      Canvas.SetTop(machineLabel, machinePos.Y - 15);
           overlayCanvas.Children.Add(machineLabel);
            }
        }
  catch (Exception ex)
            {
    System.Diagnostics.Debug.WriteLine($"DrawPopupOverlay error: {ex.Message}");
  }
        }

        public Point? GetGCodeOriginCanvasPosition(Canvas canvas)
        {
    try
       {
      if (canvas == null) return null;
  
          double canvasWidth = canvas.ActualWidth;
       double canvasHeight = canvas.ActualHeight;
        
          if (canvasWidth <= 0 || canvasHeight <= 0) return null;
       
                double currentMachineX = 0;
    double currentMachineY = 0;
        if (App.MainController?.MStatus != null)
         {
    currentMachineX = App.MainController.MStatus.X;
 currentMachineY = App.MainController.MStatus.Y;
      }
       
             if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
       return null;
    
   var calculatedOrigin = xf.ToCanvas(currentMachineX, currentMachineY);
  System.Diagnostics.Debug.WriteLine($"🎯 Calculated G-code origin for canvas {canvasWidth:F0}x{canvasHeight:F0}: {calculatedOrigin} (scale={xf.Scale:F3})");
                
     return calculatedOrigin;
            }
  catch (Exception ex)
      {
     System.Diagnostics.Debug.WriteLine($"GetGCodeOriginCanvasPosition error: {ex.Message}");
    return null;
            }
        }

        public (double MinX, double MaxX, double MinY, double MaxY)? GetCurrentGCodeBounds()
        {
     try
  {
         if (_fileService == null) return null;
        var segs = _fileService.GCodeSegments;
       if (segs == null || segs.Count == 0) return null;

    double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
     double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

       foreach (var s in segs)
    {
       if (s.StartPoint.X < minX) minX = s.StartPoint.X;
          if (s.EndPoint.X < minX) minX = s.EndPoint.X;
        if (s.StartPoint.Y < minY) minY = s.StartPoint.Y;
   if (s.EndPoint.Y < minY) minY = s.EndPoint.Y;

   if (s.StartPoint.X > maxX) maxX = s.StartPoint.X;
           if (s.EndPoint.X > maxX) maxX = s.EndPoint.X;
            if (s.StartPoint.Y > maxY) maxY = s.StartPoint.Y;
                    if (s.EndPoint.Y > maxY) maxY = s.EndPoint.Y;
     }

    if (double.IsInfinity(minX) || double.IsInfinity(minY) ||
  double.IsInfinity(maxX) || double.IsInfinity(maxY))
 return null;

                return (minX, maxX, minY, maxY);
         }
            catch
            {
    return null;
     }
        }
    }
}
