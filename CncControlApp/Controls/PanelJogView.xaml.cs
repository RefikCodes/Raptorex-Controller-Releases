using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CncControlApp.Controls
{
    public partial class PanelJogView : UserControl
    {
        private bool _initialized;
        private double _maxX = 300, _maxY = 200, _maxZ = 100;
        private double _scale = 1.0;
        private const double MarginPx = 50.0;
        private Ellipse _posIndicator;
        private TextBlock _posLabel;

        // New: indicator-only mode
        public bool ShowOnlyIndicator { get; set; }

        public PanelJogView()
        {
            InitializeComponent();
            Loaded += PanelJogView_Loaded;
        }

        private void PanelJogView_Loaded(object sender, RoutedEventArgs e)
        {
            TryLoadWorkspaceLimits();
            Redraw();
            SubscribeMposUpdates();
            UpdateCurrentPositionIndicator();
        }

        public void Open()
        {
            EnterFullMode();
        }

        public void Close()
        {
            // Keep indicator visible instead of collapsing
            EnterIndicatorMode();
        }

        private void EnterFullMode()
        {
            Visibility = Visibility.Visible;
            ShowOnlyIndicator = false;
            InteractionCanvas.IsHitTestVisible = true;

            if (!_initialized)
            {
                TryLoadWorkspaceLimits();
                _initialized = true;
            }

            Redraw();
            SubscribeMposUpdates();
            UpdateCurrentPositionIndicator();
        }

        public void EnterIndicatorMode()
        {
            Visibility = Visibility.Visible;
            ShowOnlyIndicator = true;
            InteractionCanvas.IsHitTestVisible = false;

            // redraw without grid/axes
            Redraw();
            // keep subscription to reflect live position
            SubscribeMposUpdates();
            UpdateCurrentPositionIndicator();
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
                    if (xLimit != null && double.TryParse(xLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double x)) _maxX = Math.Abs(x);
                    if (yLimit != null && double.TryParse(yLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double y)) _maxY = Math.Abs(y);
                    if (zLimit != null && double.TryParse(zLimit.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double z)) _maxZ = Math.Abs(z);
                }
            }
            catch { }
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
                w = 600; h = 450;
            }

            double usableW = Math.Max(10, w - (MarginPx * 2));
            double usableH = Math.Max(10, h - (MarginPx * 2));
            _scale = Math.Min(usableW / _maxX, usableH / _maxY);

            if (!ShowOnlyIndicator)
            {
                DrawWorkspace(w, h);
            }
        }

        private void DrawWorkspace(double canvasWidth, double canvasHeight)
        {
            double originX = MarginPx;
            double originY = canvasHeight - MarginPx;

            var boundary = new Rectangle
            {
                Width = _maxX * _scale,
                Height = _maxY * _scale,
                Stroke = Brushes.Orange,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 5 },
                Fill = new SolidColorBrush(Color.FromArgb(10, 255, 165, 0))
            };
            Canvas.SetLeft(boundary, originX);
            Canvas.SetTop(boundary, originY - boundary.Height);
            GridLinesCanvas.Children.Add(boundary);

            // grid
            double step = 25.0;
            for (double x = step; x < _maxX; x += step)
            {
                double cx = originX + x * _scale;
                GridLinesCanvas.Children.Add(new Line
                {
                    X1 = cx,
                    Y1 = originY,
                    X2 = cx,
                    Y2 = originY - (_maxY * _scale),
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
                    X1 = originX,
                    Y1 = cy,
                    X2 = originX + (_maxX * _scale),
                    Y2 = cy,
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 1,
                    Opacity = 0.3,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                });
            }

            // axes
            CrosshairCanvas.Children.Add(new Line
            {
                X1 = originX,
                Y1 = originY,
                X2 = originX + (_maxX * _scale),
                Y2 = originY,
                Stroke = Brushes.Red,
                StrokeThickness = 3
            });
            CrosshairCanvas.Children.Add(new Line
            {
                X1 = originX,
                Y1 = originY,
                X2 = originX,
                Y2 = originY - (_maxY * _scale),
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 3
            });

            var xLabel = new TextBlock
            {
                Text = $"X (max {_maxX:F0})",
                Foreground = Brushes.Red,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
            };
            Canvas.SetLeft(xLabel, originX + (_maxX * _scale) / 2 - 60);
            Canvas.SetTop(xLabel, originY + 25);
            CrosshairCanvas.Children.Add(xLabel);

            var yLabel = new TextBlock
            {
                Text = $"Y (max {_maxY:F0})",
                Foreground = Brushes.LimeGreen,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetLeft(yLabel, originX - 55);
            Canvas.SetTop(yLabel, originY - (_maxY * _scale) / 2 + 55);
            CrosshairCanvas.Children.Add(yLabel);

            var originDot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.Cyan,
                Stroke = Brushes.DarkBlue,
                StrokeThickness = 2
            };
            Canvas.SetLeft(originDot, originX - 6);
            Canvas.SetTop(originDot, originY - 6);
            CrosshairCanvas.Children.Add(originDot);

            var originLabel = new TextBlock
            {
                Text = "(0,0)",
                Foreground = Brushes.Cyan,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(3),
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(originLabel, originX + 15);
            Canvas.SetTop(originLabel, originY - 25);
            CrosshairCanvas.Children.Add(originLabel);

            var info = new TextBlock
            {
                Text = $"Workspace\nX 0–{_maxX:F0}\nY 0–{_maxY:F0}\nZ 0–{_maxZ:F0}",
                Foreground = Brushes.Orange,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(6)
            };
            double boxX = originX + (_maxX * _scale) - 140;
            double boxY = originY - (_maxY * _scale) + 10;
            Canvas.SetLeft(info, boxX);
            Canvas.SetTop(info, boxY);
            CrosshairCanvas.Children.Add(info);
        }

        private void InteractionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ShowOnlyIndicator) return; // disable in indicator-only mode
            var p = e.GetPosition(InteractionCanvas);
            HandleTouch(p);
        }

        private void InteractionCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            if (ShowOnlyIndicator) { e.Handled = true; return; }
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
            machineX = 0; machineY = 0;
            double w = Root.ActualWidth;
            double h = Root.ActualHeight;
            if (w <= 0 || h <= 0) return false;

            double originX = MarginPx;
            double originY = h - MarginPx;
            double left = originX;
            double right = originX + _maxX * _scale;
            double bottom = originY;
            double top = originY - _maxY * _scale;

            if (p.X < left || p.X > right || p.Y < top || p.Y > bottom)
                return false;

            machineX = (p.X - originX) / _scale;
            machineY = (originY - p.Y) / _scale;
            return true;
        }

        private void DrawTouchMarker(Point p)
        {
            var marker = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = Brushes.Gold,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 215, 0))
            };
            Canvas.SetLeft(marker, p.X - 7);
            Canvas.SetTop(marker, p.Y - 7);
            CrosshairCanvas.Children.Add(marker);

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (s, e) => { timer.Stop(); CrosshairCanvas.Children.Remove(marker); };
            timer.Start();
        }

        private async Task JogToMachineXYAsync(double mx, double my)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil - PanelJog hareketi gönderilmedi");
                    return;
                }
                string cmd = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "G53 G00 X{0:F3} Y{1:F3}", mx, my);
                App.MainController?.AddLogMessage($"> ▶ PanelJog G53: {cmd}");
                await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd);
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ PanelJog hareket hata: {ex.Message}");
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
                // Always update indicator when loaded
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

                double originX = MarginPx;
                double originY = h - MarginPx;
                double cx = originX + mx * _scale;
                double cy = originY - my * _scale;
                bool inside = (mx >= 0 && mx <= _maxX && my >= 0 && my <= _maxY);

                if (_posIndicator != null) CrosshairCanvas.Children.Remove(_posIndicator);
                if (_posLabel != null) CrosshairCanvas.Children.Remove(_posLabel);

                _posIndicator = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = inside ? Brushes.Yellow : Brushes.Orange,
                    Stroke = inside ? Brushes.Gold : Brushes.Red,
                    StrokeThickness = 3,
                    Opacity = 0.9
                };
                Canvas.SetLeft(_posIndicator, cx - 8);
                Canvas.SetTop(_posIndicator, cy - 8);
                Panel.SetZIndex(_posIndicator, 100);
                CrosshairCanvas.Children.Add(_posIndicator);

                _posLabel = new TextBlock
                {
                    Text = $"X:{mx:F2}\nY:{my:F2}",
                    Foreground = Brushes.Yellow,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    Padding = new Thickness(4),
                    TextAlignment = TextAlignment.Center
                };
                double lx = cx + 18, ly = cy - 28;
                if (lx + 120 > w) lx = cx - 130;
                if (ly < 0) ly = cy + 14;
                Canvas.SetLeft(_posLabel, lx);
                Canvas.SetTop(_posLabel, ly);
                Panel.SetZIndex(_posLabel, 99);
                CrosshairCanvas.Children.Add(_posLabel);
            }
            catch { }
        }
    }
}
