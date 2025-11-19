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
  }

        private void PanelJogCanvasView_Unloaded(object sender, RoutedEventArgs e)
        {
     UnsubscribeFromSettingsChanges();
   UnsubscribeMposUpdates();
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

// Grid lines
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
      }

  // Axes
    CrosshairCanvas.Children.Add(new Line
      {
   X1 = originX, Y1 = originY,
    X2 = originX + workspaceWidth, Y2 = originY,
         Stroke = Brushes.Red,
  StrokeThickness = 2
  });

  CrosshairCanvas.Children.Add(new Line
      {
X1 = originX, Y1 = originY,
 X2 = originX, Y2 = originY - workspaceHeight,
        Stroke = Brushes.LimeGreen,
    StrokeThickness = 2
          });

// Labels
   var xLabel = new TextBlock
  {
  Text = $"X",
 Foreground = Brushes.Red,
   FontSize = 11,
   FontWeight = FontWeights.Bold,
  Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
  };
    Canvas.SetLeft(xLabel, originX + workspaceWidth - 15);
        Canvas.SetTop(xLabel, originY + 5);
    CrosshairCanvas.Children.Add(xLabel);

     var yLabel = new TextBlock
      {
Text = $"Y",
  Foreground = Brushes.LimeGreen,
 FontSize = 11,
         FontWeight = FontWeights.Bold,
      Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
     };
    Canvas.SetLeft(yLabel, originX + 5);
       Canvas.SetTop(yLabel, originY - workspaceHeight + 5);
   CrosshairCanvas.Children.Add(yLabel);

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
    }
}
