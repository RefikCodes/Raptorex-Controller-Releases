using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CncControlApp.Controls
{
    /// <summary>
    /// Compact workspace visualization canvas - always visible panel jog interface
    /// Embedded version optimized for MainWindow permanent placement
  /// </summary>
    public partial class PanelJogCanvasView : UserControl
    {
        private bool _initialized;
    private double _maxX = 300, _maxY = 200, _maxZ = 100;
    private double _scale = 1.0;
    private const double MarginPx = 30.0;
    private Ellipse _posIndicator;
    private TextBlock _posLabel;
    private bool _settingsLoaded;
    private System.Collections.Generic.List<GCodeSegment> _lastPreviewSegments; // cached subset
    private const int MaxPreviewSegments = 5000; // ✅ FIXED: Match TopView limit (was 1500)

     public PanelJogCanvasView()
      {
            InitializeComponent();
    Loaded += PanelJogCanvasView_Loaded;
Unloaded += PanelJogCanvasView_Unloaded;
        }

        private void PanelJogCanvasView_Loaded(object sender, RoutedEventArgs e)
        {
    // Initial load with defaults
   Redraw();
            SubscribeMposUpdates();
  SubscribeToSettingsChanges();
UpdateCurrentPositionIndicator();
     _initialized = true;

 // Try immediate load if settings already exist
            if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)
      {
   System.Diagnostics.Debug.WriteLine($"📋 PanelJogCanvas: Settings already loaded ({App.MainController.Settings.Count} items), refreshing immediately");
                RefreshWorkspaceLimits();
      }
    else
     {
       System.Diagnostics.Debug.WriteLine("⏳ PanelJogCanvas: Waiting for settings to load...");
            }
            
            // ✅ SUBSCRIBE: Listen for G-Code preview updates
            GCodeView.GCodePreviewUpdated -= OnGCodePreviewUpdated; // ensure no duplicate
            GCodeView.GCodePreviewUpdated += OnGCodePreviewUpdated; // subscribe
            
            System.Diagnostics.Debug.WriteLine("✅ PanelJogCanvas: Subscribed to GCodePreviewUpdated event");
  }

        private void PanelJogCanvasView_Unloaded(object sender, RoutedEventArgs e)
        {
     UnsubscribeFromSettingsChanges();
   UnsubscribeMposUpdates();
            GCodeView.GCodePreviewUpdated -= OnGCodePreviewUpdated; // unsubscribe
        }

        private void SubscribeToSettingsChanges()
        {
        try
       {
           if (App.MainController?.Settings != null)
        {
                    // Subscribe to collection changed events
 ((INotifyCollectionChanged)App.MainController.Settings).CollectionChanged -= OnSettingsCollectionChanged;
           ((INotifyCollectionChanged)App.MainController.Settings).CollectionChanged += OnSettingsCollectionChanged;
  System.Diagnostics.Debug.WriteLine("✅ PanelJogCanvas: Subscribed to Settings collection changes");
  }
          }
     catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PanelJogCanvas settings subscription error: {ex.Message}");
         }
        }

        private void UnsubscribeFromSettingsChanges()
   {
          try
    {
         if (App.MainController?.Settings != null)
     {
        ((INotifyCollectionChanged)App.MainController.Settings).CollectionChanged -= OnSettingsCollectionChanged;
     }
        }
            catch { }
        }

        private void OnSettingsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
   {
      try
            {
          // Only refresh once when we detect meaningful settings additions
            if (e.Action == NotifyCollectionChangedAction.Add && !_settingsLoaded)
       {
             var settings = App.MainController?.Settings;
if (settings != null && settings.Count > 10) // Wait for substantial number of settings
      {
            // Check if we have the critical workspace settings
        bool hasWorkspaceSettings = settings.Any(s => s.Id == 130 || s.Id == 131 || s.Id == 132);
    
   if (hasWorkspaceSettings)
             {
     _settingsLoaded = true;
              Dispatcher.BeginInvoke(new Action(() =>
        {
     System.Diagnostics.Debug.WriteLine($"🔔 PanelJogCanvas: Settings loaded ({settings.Count} items), refreshing workspace");
          RefreshWorkspaceLimits();
      }), System.Windows.Threading.DispatcherPriority.Background);
         }
         }
    }
        }
        catch (Exception ex)
{
            System.Diagnostics.Debug.WriteLine($"❌ PanelJogCanvas settings change handler error: {ex.Message}");
            }
}

        /// <summary>
        /// Public method to force refresh workspace limits and redraw canvas
      /// Call this after settings are loaded or changed
        /// </summary>
        public void RefreshWorkspaceLimits()
     {
          try
            {
         double prevX = _maxX, prevY = _maxY, prevZ = _maxZ;

              TryLoadWorkspaceLimits();

          bool changed = Math.Abs(prevX - _maxX) > 0.1 ||
     Math.Abs(prevY - _maxY) > 0.1 ||
      Math.Abs(prevZ - _maxZ) > 0.1;

          if (changed)
       {
        System.Diagnostics.Debug.WriteLine($"✅ PanelJogCanvas: Workspace updated X:{_maxX} Y:{_maxY} Z:{_maxZ}");
    Redraw();
           UpdateCurrentPositionIndicator();
         }
            else
          {
  System.Diagnostics.Debug.WriteLine($"ℹ️ PanelJogCanvas: Workspace unchanged X:{_maxX} Y:{_maxY} Z:{_maxZ}");
       }
      }
            catch (Exception ex)
   {
                System.Diagnostics.Debug.WriteLine($"❌ PanelJogCanvas refresh error: {ex.Message}");
  }
        }

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Redraw();
         UpdateCurrentPositionIndicator();
  }

        private void TryLoadWorkspaceLimits()
     {
     try
            {
   if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)
      {
           var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
        var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);
        var zLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 132);

        bool foundAny = false;

    if (xLimit != null && double.TryParse(xLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double x) && x > 0)
            {
                _maxX = Math.Abs(x);
    foundAny = true;
           System.Diagnostics.Debug.WriteLine($"📐 PanelJogCanvas: X limit = {_maxX}mm (from $130)");
          }

                 if (yLimit != null && double.TryParse(yLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double y) && y > 0)
  {
         _maxY = Math.Abs(y);
             foundAny = true;
  System.Diagnostics.Debug.WriteLine($"📐 PanelJogCanvas: Y limit = {_maxY}mm (from $131)");
          }

         if (zLimit != null && double.TryParse(zLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double z) && z > 0)
{
          _maxZ = Math.Abs(z);
              foundAny = true;
        System.Diagnostics.Debug.WriteLine($"📐 PanelJogCanvas: Z limit = {_maxZ}mm (from $132)");
         }

        if (foundAny)
 {
          System.Diagnostics.Debug.WriteLine($"✅ PanelJogCanvas: Successfully loaded workspace limits from {App.MainController.Settings.Count} settings");
     }
      else
        {
        System.Diagnostics.Debug.WriteLine($"⚠️ PanelJogCanvas: No valid workspace settings found in {App.MainController.Settings.Count} settings, using defaults");
  }
         }
     else
    {
        System.Diagnostics.Debug.WriteLine($"⚠️ PanelJogCanvas: Settings not available (Count={(App.MainController?.Settings?.Count ?? 0)}), using defaults X:{_maxX} Y:{_maxY} Z:{_maxZ}");
 }
      }
    catch (Exception ex)
         {
           System.Diagnostics.Debug.WriteLine($"❌ PanelJogCanvas load limits error: {ex.Message}");
}
        }

        /// <summary>
        /// Public method to force redraw the canvas (called after G92 origin change)
        /// </summary>
        public void RefreshCanvas()
        {
            Redraw();
        }

        private void Redraw()
        {
  if (GridLinesCanvas == null || CrosshairCanvas == null) return;

  GridLinesCanvas.Children.Clear();
  CrosshairCanvas.Children.Clear();

          double w = Root.ActualWidth;
         double h = Root.ActualHeight;

   if (w <= 10 || h <= 10)
{
    w = 600; h = 250;
   }

   DrawWorkspace(w, h);
   DrawGCodePreview(w, h); // ✅ add miniature preview overlay
   
   // ✅ FIX: Always redraw position indicator after canvas refresh
   UpdateCurrentPositionIndicator();
   
   // ✅ Z Mapping grid overlay (if active)
   DrawZMappingGridOverlay();
}

        private void DrawWorkspace(double canvasWidth, double canvasHeight)
   {
   // Center the workspace table in the canvas
      double usableW = Math.Max(10, canvasWidth - (MarginPx * 2));
   double usableH = Math.Max(10, canvasHeight - (MarginPx * 2));
            
        // Calculate scale to fit workspace in canvas
   _scale = Math.Min(usableW / _maxX, usableH / _maxY);
        
     // Calculate actual workspace dimensions on canvas
 double workspaceWidth = _maxX * _scale;
  double workspaceHeight = _maxY * _scale;
      
        // Center the workspace horizontally and vertically
   double originX = (canvasWidth - workspaceWidth) / 2;
 double originY = (canvasHeight + workspaceHeight) / 2;

     // Boundary rectangle
   var boundary = new Rectangle
   {
    Width = workspaceWidth,
      Height = workspaceHeight,
       Stroke = Brushes.Orange,
  StrokeThickness = 2,
   StrokeDashArray = new DoubleCollection { 5, 5 },
        Fill = new SolidColorBrush(Color.FromArgb(10, 255, 165, 0))
    };
   Canvas.SetLeft(boundary, originX);
  Canvas.SetTop(boundary, originY - boundary.Height);
    GridLinesCanvas.Children.Add(boundary);

// Grid lines with scale markings
 double step = 25.0;
       for (double x = step; x < _maxX; x += step)
        {
       double cx = originX + x * _scale;
  GridLinesCanvas.Children.Add(new Line
      {
X1 = cx, Y1 = originY,
  X2 = cx, Y2 = originY - workspaceHeight,
   Stroke = Brushes.DarkGray,
   StrokeThickness = 1,
     Opacity = 0.3,
    StrokeDashArray = new DoubleCollection { 2, 2 }
      });
      
      // X-axis scale markings every 50mm
      if (x % 50 == 0)
      {
          // Tick mark on X-axis
          GridLinesCanvas.Children.Add(new Line
          {
              X1 = cx, Y1 = originY,
              X2 = cx, Y2 = originY + 8,
              Stroke = Brushes.White,
              StrokeThickness = 2
          });
          
          // Scale number
          var scaleLabel = new TextBlock
          {
              Text = $"{x:F0}",
              Foreground = Brushes.White,
              FontSize = 9,
              FontWeight = FontWeights.Bold,
              Background = Brushes.Transparent,
              Padding = new Thickness(2, 0, 2, 0)
          };
          Canvas.SetLeft(scaleLabel, cx - 12);
          Canvas.SetTop(scaleLabel, originY + 10);
          GridLinesCanvas.Children.Add(scaleLabel);
      }
  }

  for (double y = step; y < _maxY; y += step)
     {
              double cy = originY - y * _scale;
   GridLinesCanvas.Children.Add(new Line
      {
       X1 = originX, Y1 = cy,
 X2 = originX + workspaceWidth, Y2 = cy,
Stroke = Brushes.DarkGray,
 StrokeThickness = 1,
 Opacity = 0.3,
 StrokeDashArray = new DoubleCollection { 2, 2 }
      });
      
      // Y-axis scale markings every 50mm
      if (y % 50 == 0)
      {
          // Tick mark on Y-axis
          GridLinesCanvas.Children.Add(new Line
          {
              X1 = originX, Y1 = cy,
              X2 = originX - 8, Y2 = cy,
              Stroke = Brushes.White,
              StrokeThickness = 2
          });
          
          // Scale number
          var scaleLabel = new TextBlock
          {
              Text = $"{y:F0}",
              Foreground = Brushes.White,
              FontSize = 9,
              FontWeight = FontWeights.Bold,
              Background = Brushes.Transparent,
              Padding = new Thickness(2, 0, 2, 0)
          };
          Canvas.SetLeft(scaleLabel, originX - 32);
          Canvas.SetTop(scaleLabel, cy - 8);
          GridLinesCanvas.Children.Add(scaleLabel);
      }
      }

// Origin
 var originDot = new Ellipse
  {
  Width = 10,
    Height = 10,
      Fill = Brushes.Cyan,
     Stroke = Brushes.DarkBlue,
   StrokeThickness = 2
       };
   Canvas.SetLeft(originDot, originX - 5);
       Canvas.SetTop(originDot, originY - 5);
 CrosshairCanvas.Children.Add(originDot);

    var originLabel = new TextBlock
    {
         Text = "(0,0)",
   Foreground = Brushes.Cyan,
 FontSize = 9,
       FontWeight = FontWeights.Bold,
      Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
    Padding = new Thickness(2),
 TextAlignment = TextAlignment.Center
  };
     Canvas.SetLeft(originLabel, originX + 10);
 Canvas.SetTop(originLabel, originY - 20);
     CrosshairCanvas.Children.Add(originLabel);

   // Info box (top-left corner of workspace)
 var info = new TextBlock
     {
  Text = $"Workspace: {_maxX:F0} × {_maxY:F0} mm",
 Foreground = Brushes.Orange,
       FontSize = 9,
     FontWeight = FontWeights.Bold,
     Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
  Padding = new Thickness(4)
            };
    Canvas.SetLeft(info, originX + 5);
    Canvas.SetTop(info, originY - workspaceHeight + 5);
      CrosshairCanvas.Children.Add(info);
     }

        private void InteractionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
     {
            var p = e.GetPosition(InteractionCanvas);
        HandleTouch(p);
      }

        private void InteractionCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            var p = e.GetTouchPoint(InteractionCanvas).Position;
 HandleTouch(p);
            e.Handled = true;
        }

        private void HandleTouch(Point p)
        {
      if (!TryMapToMachine(p, out double mx, out double my))
    return;

   DrawTouchMarker(p);
            _ = JogToMachineXYAsync(mx, my);
        }

        private bool TryMapToMachine(Point p, out double machineX, out double machineY)
        {
            machineX = 0;
machineY = 0;

double w = Root.ActualWidth;
         double h = Root.ActualHeight;
   if (w <= 0 || h <= 0) return false;

  // Calculate centered origin (same as DrawWorkspace)
  double usableW = Math.Max(10, w - (MarginPx * 2));
     double usableH = Math.Max(10, h - (MarginPx * 2));
            double scale = Math.Min(usableW / _maxX, usableH / _maxY);
       double workspaceWidth = _maxX * scale;
     double workspaceHeight = _maxY * scale;
     
     double originX = (w - workspaceWidth) / 2;
   double originY = (h + workspaceHeight) / 2;
   
            double left = originX;
      double right = originX + workspaceWidth;
            double bottom = originY;
      double top = originY - workspaceHeight;

       if (p.X < left || p.X > right || p.Y < top || p.Y > bottom)
 return false;

       machineX = (p.X - originX) / scale;
     machineY = (originY - p.Y) / scale;
  return true;
      }

        private void DrawTouchMarker(Point p)
        {
            var marker = new Ellipse
         {
             Width = 12,
           Height = 12,
      Stroke = Brushes.Gold,
      StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 215, 0))
            };
            Canvas.SetLeft(marker, p.X - 6);
            Canvas.SetTop(marker, p.Y - 6);
     CrosshairCanvas.Children.Add(marker);

    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
  timer.Tick += (s, e) =>
   {
      timer.Stop();
              CrosshairCanvas.Children.Remove(marker);
       };
            timer.Start();
        }

        private async Task JogToMachineXYAsync(double mx, double my)
      {
            try
            {
     if (App.MainController?.IsConnected != true)
      {
      App.MainController?.AddLogMessage("> ❌ CNC not connected - PanelJog move cancelled");
        return;
        }

           string cmd = string.Format(System.Globalization.CultureInfo.InvariantCulture,
 "G53 G00 X{0:F3} Y{1:F3}", mx, my);

      App.MainController?.AddLogMessage($"> ▶ PanelJog G53: {cmd}");
            await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd);
            }
       catch (Exception ex)
        {
        App.MainController?.AddLogMessage($"> ❌ PanelJog move error: {ex.Message}");
   }
        }

        private void SubscribeMposUpdates()
        {
      try
            {
          if (App.MainController?.MStatus != null)
    {
    App.MainController.MStatus.PropertyChanged -= MStatus_PropertyChanged;
   App.MainController.MStatus.PropertyChanged += MStatus_PropertyChanged;
       }
          }
        catch { }
        }

        private void UnsubscribeMposUpdates()
        {
     try
    {
                if (App.MainController?.MStatus != null)
       {
   App.MainController.MStatus.PropertyChanged -= MStatus_PropertyChanged;
        }
         }
     catch { }
        }

   private void MStatus_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
    if (e.PropertyName == nameof(App.MainController.MStatus.X) ||
          e.PropertyName == nameof(App.MainController.MStatus.Y) ||
   e.PropertyName == nameof(App.MainController.MStatus.WorkX) ||
    e.PropertyName == nameof(App.MainController.MStatus.WorkY))
            {
            Dispatcher.BeginInvoke(new Action(UpdateCurrentPositionIndicator));
            }
   }

  private void UpdateCurrentPositionIndicator()
   {
            try
            {
  double mx = App.MainController?.MStatus?.X ?? double.NaN;
      double my = App.MainController?.MStatus?.Y ?? double.NaN;

     if (double.IsNaN(mx) || double.IsNaN(my))
       {
  mx = App.MainController?.MStatus?.WorkX ?? 0;
   my = App.MainController?.MStatus?.WorkY ?? 0;
     }

        double w = Root.ActualWidth;
  double h = Root.ActualHeight;
      if (w <= 0 || h <= 0) return;

   // Calculate centered origin (same as DrawWorkspace)
     double usableW = Math.Max(10, w - (MarginPx * 2));
       double usableH = Math.Max(10, h - (MarginPx * 2));
         double scale = Math.Min(usableW / _maxX, usableH / _maxY);
      double workspaceWidth = _maxX * scale;
   double workspaceHeight = _maxY * scale;
       
   double originX = (w - workspaceWidth) / 2;
   double originY = (h + workspaceHeight) / 2;
     
 double cx = originX + mx * scale;
          double cy = originY - my * scale;
 bool inside = (mx >= 0 && mx <= _maxX && my >= 0 && my <= _maxY);

    if (_posIndicator != null) CrosshairCanvas.Children.Remove(_posIndicator);
   if (_posLabel != null) CrosshairCanvas.Children.Remove(_posLabel);

   _posIndicator = new Ellipse
  {
    Width = 14,
       Height = 14,
  Fill = inside ? Brushes.Yellow : Brushes.Orange,
    Stroke = inside ? Brushes.Gold : Brushes.Red,
         StrokeThickness = 2,
        Opacity = 0.9
     };
   Canvas.SetLeft(_posIndicator, cx - 7);
 Canvas.SetTop(_posIndicator, cy - 7);
     Panel.SetZIndex(_posIndicator, 100);
     CrosshairCanvas.Children.Add(_posIndicator);

     _posLabel = new TextBlock
 {
   Text = $"X:{mx:F2}\nY:{my:F2}",
       Foreground = Brushes.Yellow,
       FontFamily = new FontFamily("Consolas"),
 FontSize = 9,
      FontWeight = FontWeights.Bold,
  Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
Padding = new Thickness(3),
    TextAlignment = TextAlignment.Center
   };

 double lx = cx + 15, ly = cy - 25;
if (lx + 100 > w) lx = cx - 110;
 if (ly < 0) ly = cy + 12;

     Canvas.SetLeft(_posLabel, lx);
        Canvas.SetTop(_posLabel, ly);
    Panel.SetZIndex(_posLabel, 99);
  CrosshairCanvas.Children.Add(_posLabel);
}
         catch { }
     }

        private void OnGCodePreviewUpdated(System.Collections.Generic.IReadOnlyList<GCodeSegment> segs)
        {
            try
            {
                if (segs == null || segs.Count == 0)
                {
                    _lastPreviewSegments = null;
                    Redraw(); // clears preview
                    return;
                }
                // cache limited copy for performance
                _lastPreviewSegments = segs.Take(MaxPreviewSegments).ToList();
                Redraw(); // triggers preview draw
            }
            catch { }
        }

        /// <summary>
        /// Draw G-Code preview using EXACT same method as TopViewCanvas:
        /// - WorkspaceTransform for scale
        /// - Place G-code origin at current machine position
        /// - Use OptimizedGCodeRenderer
        /// </summary>
        private void DrawGCodePreview(double canvasWidth, double canvasHeight)
        {
            try
            {
                if (_lastPreviewSegments == null || _lastPreviewSegments.Count == 0) return;

                // Use same transform as TopView
                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    return; // no settings, can't draw
                }

                // Get current machine position (where spindle is)
                double currentMachineX = 0;
                double currentMachineY = 0;
                if (App.MainController?.MStatus != null)
                {
                    currentMachineX = App.MainController.MStatus.X;
                    currentMachineY = App.MainController.MStatus.Y;
                }

                // Canvas position of current machine position (G-code origin will be placed here)
                var machineCanvasPt = xf.ToCanvas(currentMachineX, currentMachineY);
                double machineCanvasX = machineCanvasPt.X;
                double machineCanvasY = machineCanvasPt.Y;

                // Z values for color grouping
                var allZ = _lastPreviewSegments.SelectMany(s => new[] { s.StartPoint.Z, s.EndPoint.Z }).ToList();
                double minZ = allZ.Min();
                double maxZ = allZ.Max();

                // Use OptimizedGCodeRenderer (exact same as TopView)
                OptimizedGCodeRenderer.DrawGCodeOptimized(CrosshairCanvas, _lastPreviewSegments, xf.Scale, 
                    machineCanvasX, machineCanvasY, minZ, maxZ);

                // Draw origin marker (red crosshair) at machine position
                double crossSize = 10;
                double circleRadius = 6;

                var originCircle = new Ellipse
                {
                    Width = circleRadius * 2,
                    Height = circleRadius * 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 255, 60, 60))
                };
                Canvas.SetLeft(originCircle, machineCanvasX - circleRadius);
                Canvas.SetTop(originCircle, machineCanvasY - circleRadius);
                CrosshairCanvas.Children.Add(originCircle);

                var crossHorizontal = new Line
                {
                    X1 = machineCanvasX - crossSize,
                    Y1 = machineCanvasY,
                    X2 = machineCanvasX + crossSize,
                    Y2 = machineCanvasY,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2
                };
                CrosshairCanvas.Children.Add(crossHorizontal);

                var crossVertical = new Line
                {
                    X1 = machineCanvasX,
                    Y1 = machineCanvasY - crossSize,
                    X2 = machineCanvasX,
                    Y2 = machineCanvasY + crossSize,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2
                };
                CrosshairCanvas.Children.Add(crossVertical);
            }
            catch { }
        }

        #region Z Mapping Grid Overlay
        
        private bool _zMappingGridVisible = false;
        private int _zMappingRows = 0;
        private int _zMappingColumns = 0;
        private double _zMappingMinX, _zMappingMaxX, _zMappingMinY, _zMappingMaxY;

        /// <summary>
        /// Z Mapping grid overlay çizer - GCode bounding box etrafına çerçeve ve grid
        /// </summary>
        public void DrawZMappingGrid(double minX, double maxX, double minY, double maxY, int rows, int columns)
        {
            _zMappingGridVisible = true;
            _zMappingRows = rows;
            _zMappingColumns = columns;
            _zMappingMinX = minX;
            _zMappingMaxX = maxX;
            _zMappingMinY = minY;
            _zMappingMaxY = maxY;

            // Yeniden çiz (mevcut workspace + grid overlay)
            Redraw();
        }

        /// <summary>
        /// Z Mapping grid overlay'i temizle
        /// </summary>
        public void ClearZMappingGrid()
        {
            _zMappingGridVisible = false;
            Redraw();
        }

        /// <summary>
        /// Z Mapping grid overlay'i çizer (Redraw içinden çağrılır)
        /// GCode çizimi ile aynı koordinat sistemini kullanır
        /// </summary>
        private void DrawZMappingGridOverlay()
        {
            if (!_zMappingGridVisible || _zMappingRows < 2 || _zMappingColumns < 2) return;

            try
            {
                double canvasWidth = GridLinesCanvas.ActualWidth;
                double canvasHeight = GridLinesCanvas.ActualHeight;
                if (canvasWidth <= 0 || canvasHeight <= 0) return;

                // GCode çizimi ile aynı WorkspaceTransform kullan
                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    return; // no settings, can't draw
                }

                // Makine pozisyonu (GCode origin'i buraya çizilir)
                double currentMachineX = 0;
                double currentMachineY = 0;
                if (App.MainController?.MStatus != null)
                {
                    currentMachineX = App.MainController.MStatus.X;
                    currentMachineY = App.MainController.MStatus.Y;
                }

                // Makine pozisyonunun canvas koordinatı
                var machineCanvasPt = xf.ToCanvas(currentMachineX, currentMachineY);
                double machineCanvasX = machineCanvasPt.X;
                double machineCanvasY = machineCanvasPt.Y;

                // GCode bounds -> canvas coordinates (GCode origin = makine pozisyonu)
                // GCode koordinatları makine pozisyonuna göre offset edilir
                double left = machineCanvasX + _zMappingMinX * xf.Scale;
                double right = machineCanvasX + _zMappingMaxX * xf.Scale;
                double top = machineCanvasY - _zMappingMaxY * xf.Scale;    // Y flipped
                double bottom = machineCanvasY - _zMappingMinY * xf.Scale; // Y flipped

                double gridWidth = right - left;
                double gridHeight = bottom - top;

                if (gridWidth <= 0 || gridHeight <= 0) return;

                // Bounding box çerçevesi (mavi kesikli çizgi)
                var boundingRect = new Rectangle
                {
                    Width = gridWidth,
                    Height = gridHeight,
                    Stroke = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(30, 33, 150, 243))
                };
                Canvas.SetLeft(boundingRect, left);
                Canvas.SetTop(boundingRect, top);
                GridLinesCanvas.Children.Add(boundingRect);

                // Grid çizgileri
                double cellWidth = gridWidth / (_zMappingColumns - 1);
                double cellHeight = gridHeight / (_zMappingRows - 1);

                // Yatay grid çizgileri
                for (int r = 0; r < _zMappingRows; r++)
                {
                    double y = top + r * cellHeight;
                    var line = new Line
                    {
                        X1 = left,
                        Y1 = y,
                        X2 = right,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 193, 7)),
                        StrokeThickness = 1
                    };
                    GridLinesCanvas.Children.Add(line);
                }

                // Dikey grid çizgileri
                for (int c = 0; c < _zMappingColumns; c++)
                {
                    double x = left + c * cellWidth;
                    var line = new Line
                    {
                        X1 = x,
                        Y1 = top,
                        X2 = x,
                        Y2 = bottom,
                        Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 193, 7)),
                        StrokeThickness = 1
                    };
                    GridLinesCanvas.Children.Add(line);
                }

                // Probe noktaları (kesişim noktalarında)
                for (int r = 0; r < _zMappingRows; r++)
                {
                    for (int c = 0; c < _zMappingColumns; c++)
                    {
                        double x = left + c * cellWidth;
                        double y = top + r * cellHeight;

                        var dot = new Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                            Stroke = new SolidColorBrush(Colors.White),
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(dot, x - 4);
                        Canvas.SetTop(dot, y - 4);
                        GridLinesCanvas.Children.Add(dot);
                    }
                }

                // Grid bilgisi label
                var label = new TextBlock
                {
                    Text = $"🗺️ Z Map: {_zMappingRows}x{_zMappingColumns} ({_zMappingRows * _zMappingColumns} nokta)",
                    Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, top - 20);
                GridLinesCanvas.Children.Add(label);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawZMappingGridOverlay error: {ex.Message}");
            }
        }

        #endregion
    }
}
