// MainWindow.xaml.cs - FIND CENTER PROBE AUTO-EXTEND
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using CncControlApp.Managers; // event cache erişimi için
using CncControlApp.Models; // ProbeRecord için
using System.Windows.Navigation;
using CncControlApp.Controls;
using Microsoft.Win32;

namespace CncControlApp
{
    public partial class MainWindow : Window
    {
        private readonly StatusView _statusView;
        private readonly GCodeView _gcodeView;
        private readonly JogView _jogView;
        private readonly ConsoleView _consoleView;

        /// <summary>
        /// Public access to GCodeView for canvas refresh operations
        /// </summary>
        public GCodeView GCodeViewInstance => _gcodeView;

        /// <summary>
        /// Public access to MainPanelJogCanvas for canvas refresh operations (koordinatların altındaki canvas)
        /// </summary>
        public Controls.PanelJogCanvasView PanelJogCanvasInstance => MainPanelJogCanvas;

        // Probe logging control
        private const bool ProbeLogVerbose = false;
        private const int MaxProbeFeed =300; // ← cap probe feed (mm/min) to250 (increased from180)

        private bool _probePanelVisible = false;
        private bool _probeCanvasInitialized = false;

        private bool _isKioskMode = false;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;
        private bool _previousTopmost;
        private bool _mainWindowTopmostDisabledForNumpad = false;

        // Probe panel tracking
        private double _lastProbeX;
        private double _lastProbeY;
        private double _lastProbeZ;

        private double _probeWorkspaceMaxX =300;
        private double _probeWorkspaceMaxY =200;
        private double _probeWorkspaceMaxZ =100;
        private double _probeCanvasScaleFactorX =1.0;
        private double _probeCanvasScaleFactorY =1.0;

        private Ellipse _currentPositionIndicator;
        private TextBlock _currentPositionLabel;

        private DateTime _lastWorkspaceWarning = DateTime.MinValue;

        // Since the Fine Probe checkbox is removed from UI, use a default mode here
        private bool _fineProbeMode = false; // default: fast probe

        // Z Mapping grid verileri
        private int _zMappingRows = 0;
        private int _zMappingColumns = 0;
        private bool _zMappingGridVisible = false;

        public MainWindow()
        {
            InitializeComponent();
            FitToScreen();

            _statusView = new StatusView();
            _gcodeView = new GCodeView();
            _jogView = new JogView();
            _consoleView = new ConsoleView();

            MainMenuView.MenuSelectionChanged += OnMenuSelectionChanged;
            RunUiLocker.Attach(this);

            if (App.MainController != null)
            {
                App.MainController.NavigateToPanel += OnNavigateToPanel;
            }

            MainContent.Content = _statusView;

            this.KeyDown += MainWindow_KeyDown;
            this.Loaded += MainWindow_Loaded;

            if (App.MainController?.MStatus != null)
            {
                // Canvas removed - position tracking not needed
                // App.MainController.MStatus.PropertyChanged += MStatusPropertyChanged;
            }

            // In constructor AFTER InitializeComponent():
            DataContext = App.MainController;

            // Hook ProbePanelView events
            HookProbePanelEvents();
        }

        private void HookProbePanelEvents()
        {
            var panel = MainProbePanel as ProbePanelView;
            if (panel == null) return;

            // Avoid duplicate subscriptions
            panel.ZProbeClicked -= ZProbeButton_Click;
            panel.PlusXProbeClicked -= PlusXProbeButton_Click;
            panel.MinusXProbeClicked -= MinusXProbeButton_Click;
            panel.PlusYProbeClicked -= PlusYProbeButton_Click;
            panel.MinusYProbeClicked -= MinusYProbeButton_Click;
            panel.SetZeroXClicked -= ProbeSetZeroButton_Click;
            panel.SetZeroYClicked -= ProbeSetZeroButton_Click;
            panel.SetZeroZClicked -= ProbeSetZeroButton_Click;
            panel.SetZeroAClicked -= ProbeSetZeroButton_Click;
            panel.CenterXOuterClicked -= Panel_CenterXOuterClicked;
            panel.CenterYOuterClicked -= Panel_CenterYOuterClicked;
            panel.CenterXYOuterClicked -= Panel_CenterXYOuterClicked;
            panel.ZMappingRequested -= Panel_ZMappingRequested;

            panel.ZProbeClicked += ZProbeButton_Click;
            panel.PlusXProbeClicked += PlusXProbeButton_Click;
            panel.MinusXProbeClicked += MinusXProbeButton_Click;
            panel.PlusYProbeClicked += PlusYProbeButton_Click;
            panel.MinusYProbeClicked += MinusYProbeButton_Click;
            panel.SetZeroXClicked += ProbeSetZeroButton_Click;
            panel.SetZeroYClicked += ProbeSetZeroButton_Click;
            panel.SetZeroZClicked += ProbeSetZeroButton_Click;
            panel.SetZeroAClicked += ProbeSetZeroButton_Click;
            panel.CenterXOuterClicked += Panel_CenterXOuterClicked;
            panel.CenterYOuterClicked += Panel_CenterYOuterClicked;
            panel.CenterXYOuterClicked += Panel_CenterXYOuterClicked;
            panel.ZMappingRequested += Panel_ZMappingRequested;
            
            // Subscribe to MainController PropertyChanged for IsGCodeLoaded updates
            if (App.MainController != null)
            {
                App.MainController.PropertyChanged += MainController_PropertyChanged;
                // Initialize with current value
                panel.IsGCodeLoaded = App.MainController.IsGCodeLoaded;
            }
        }
        
        private void MainController_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainControll.IsGCodeLoaded))
            {
                Dispatcher.Invoke(() =>
                {
                    var panel = MainProbePanel as ProbePanelView;
                    if (panel != null)
                    {
                        panel.IsGCodeLoaded = App.MainController?.IsGCodeLoaded ?? false;
                    }
                });
            }
        }

        /// <summary>
        /// Z Mapping grid çizimi için event handler
        /// </summary>
        private void Panel_ZMappingRequested(object sender, ZMappingEventArgs e)
        {
            DrawZMappingGrid(e.RowCount, e.ColumnCount);
        }

        /// <summary>
        /// Z Mapping grid'i GCodeView canvas üzerine çizer
        /// GCode şeklinin bounding box'ı etrafına çerçeve ve grid çizer
        /// </summary>
        private void DrawZMappingGrid(int rows, int columns)
        {
            _zMappingRows = rows;
            _zMappingColumns = columns;
            _zMappingGridVisible = true;

            // GCode bounds'u al
            var bounds = _gcodeView?.GetCurrentGCodeBounds();
            if (bounds == null)
            {
                Controls.MessageDialog.ShowError("Z Mapping Hatası", "GCode yüklü değil veya bounds hesaplanamadı.");
                return;
            }

            var (minX, maxX, minY, maxY) = bounds.Value;
            double width = maxX - minX;
            double height = maxY - minY;

            App.MainController?.AddLogMessage($"> 🗺️ Z Mapping Grid: {rows} satır x {columns} sütun");
            App.MainController?.AddLogMessage($">    Bounds: X[{minX:F2} - {maxX:F2}] Y[{minY:F2} - {maxY:F2}]");
            App.MainController?.AddLogMessage($">    Boyut: {width:F2} x {height:F2} mm");

            // MainPanelJogCanvas'a grid overlay çizdir
            MainPanelJogCanvas?.DrawZMappingGrid(minX, maxX, minY, maxY, rows, columns);

            // ProbePanelView'daki DataGrid'e noktaları yükle
            var panel = MainProbePanel as ProbePanelView;
            panel?.PopulateZMappingPoints(minX, maxX, minY, maxY, rows, columns);

            Controls.MessageDialog.ShowInfo("Z Mapping Grid", 
                $"Grid oluşturuldu:\n\n" +
                $"Satır (Y): {rows}\n" +
                $"Sütun (X): {columns}\n" +
                $"Toplam nokta: {rows * columns}\n\n" +
                $"Boyut: {width:F1} x {height:F1} mm\n" +
                $"Alan: X[{minX:F1} - {maxX:F1}] Y[{minY:F1} - {maxY:F1}]");
        }

        // Canvas elements removed from probe panel
        /*
        private Canvas MainGridLinesCanvas => (MainProbePanel as ProbePanelView)?.GridLinesCanvas;
        private Canvas MainProbeCoordinatesCanvas => (MainProbePanel as ProbePanelView)?.ProbeCoordinatesCanvas;
        private Canvas MainCrosshairCanvas => (MainProbePanel as ProbePanelView)?.CrosshairCanvas;
        private TextBlock MainProbeXCoordinate => (MainProbePanel as ProbePanelView)?.ProbeXText;
        private TextBlock MainProbeYCoordinate => (MainProbePanel as ProbePanelView)?.ProbeYText;
        private TextBlock MainProbeZCoordinate => (MainProbePanel as ProbePanelView)?.ProbeZText;
        */

        public bool IsKioskMode => _isKioskMode;

        #region Probe Panel Management (Clean Logging)

        private void LogProbe(string msg, bool force = false)
        {
            if (ProbeLogVerbose || force)
                App.MainController?.AddLogMessage(msg);
            else
                Debug.WriteLine(msg);
        }

        public void ShowProbePanel()
        {
            try
            {
                if (_probePanelVisible) return;
                if (MainProbePanel != null)
                {
                    // Keep coordinates visible in probe view
                    if (MainCoordinatesView != null)
                        MainCoordinatesView.Visibility = Visibility.Visible;

                    // Hide Home/Zero actions while probing
                    if (HomeZeroActionsPanel != null)
                        HomeZeroActionsPanel.Visibility = Visibility.Collapsed;

                    // Show probe panel (bottom row)
                    MainProbePanel.Visibility = Visibility.Visible;
                    _probePanelVisible = true;

                    // ✅ Probe history panel kaldırıldı - artık gösterilmiyor
                    // ShowProbeHistoryPanel();

                    // Canvas removed - no initialization needed
                    // Dispatcher.BeginInvoke(new Action(InitializeProbePanel),
                    //     System.Windows.Threading.DispatcherPriority.Loaded);

                    LogProbe("> 🟢 Probe panel opened (coordinates kept visible)");
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe panel show error: {ex.Message}", true);
            }
        }

        public void HideProbePanel()
        {
            try
            {
                if (!_probePanelVisible) return;
                _probePanelVisible = false;

                // Panel gizleme kodu...
                if (MainProbePanel != null && MainCoordinatesView != null)
                {
                    MainProbePanel.Visibility = Visibility.Collapsed;
                    MainCoordinatesView.Visibility = Visibility.Visible;
                    
                    // Hide probe history panel
                    HideProbeHistoryPanel();
                    
                    LogProbe("> ⚪ Probe panel closed");
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe panel hide error: {ex.Message}", true);
            }
        }

        /* Canvas removed - methods no longer needed
        private void InitializeProbePanel()
        {
            try
            {
                if (_probeCanvasInitialized) return;

                if (MainGridLinesCanvas != null)
                {
                    double w = MainGridLinesCanvas.ActualWidth;
                    double h = MainGridLinesCanvas.ActualHeight;
                    if (w <= 0 || h <= 0)
                    {
                        Dispatcher.BeginInvoke(new Action(InitializeProbePanel),
                            System.Windows.Threading.DispatcherPriority.Background);
                        return;
                    }
                }

                LoadWorkspaceLimitsForProbePanel();
                DrawProbeWorkspaceCoordinateSystem();
                UpdateCurrentPositionOnCanvas();

                _probeCanvasInitialized = true;
                LogProbe("> Probe canvas initialized");
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe canvas init error: {ex.Message}", true);
            }
        }

        private void DrawProbeWorkspaceCoordinateSystem()
        {
            try
            {
                if (MainGridLinesCanvas == null || MainCrosshairCanvas == null)
                {
                    LogProbe("> ❌ Probe workspace draw: canvas null", true);
                    return;
                }

                MainGridLinesCanvas.Children.Clear();
                MainCrosshairCanvas.Children.Clear();

                double canvasWidth = MainGridLinesCanvas.ActualWidth;
                double canvasHeight = MainGridLinesCanvas.ActualHeight;
                if (canvasWidth <= 10 || canvasHeight <= 10)
                {
                    canvasWidth = 500;
                    canvasHeight = 400;
                }

                double margin = 50;
                double usableWidth = canvasWidth - (margin * 2);
                double usableHeight = canvasHeight - (margin * 2);

                _probeCanvasScaleFactorX = usableWidth / _probeWorkspaceMaxX;
                _probeCanvasScaleFactorY = usableHeight / _probeWorkspaceMaxY;
                double uniform = Math.Min(_probeCanvasScaleFactorX, _probeCanvasScaleFactorY);
                _probeCanvasScaleFactorX = uniform;
                _probeCanvasScaleFactorY = uniform;

                double originX = margin;
                double originY = canvasHeight - margin;

                DrawDetailedWorkspaceForProbe(originX, originY, canvasWidth, canvasHeight);
                LogProbe("> Probe workspace drawn");
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe workspace draw error: {ex.Message}", true);
            }
        }

        private void DrawDetailedWorkspaceForProbe(double originX, double originY, double canvasWidth, double canvasHeight)
        {
            try
            {
                var boundary = new Rectangle
                {
                    Width = _probeWorkspaceMaxX * _probeCanvasScaleFactorX,
                    Height = _probeWorkspaceMaxY * _probeCanvasScaleFactorY,
                    Stroke = Brushes.Orange,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 5 },
                    Fill = new SolidColorBrush(Color.FromArgb(20, 255, 165, 0))
                };
                Canvas.SetLeft(boundary, originX);
                Canvas.SetTop(boundary, originY - boundary.Height);
                MainGridLinesCanvas.Children.Add(boundary);

                DrawWorkspaceGrid(originX, originY);
                DrawAxisLines(originX, originY, canvasWidth, canvasHeight);
                DrawOriginPoint(originX, originY);
                DrawWorkspaceInfoBox(originX, originY);

                LogProbe("> Probe workspace detail complete");
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe detail error: {ex.Message}", true);
            }
        }

        private void DrawAxisLines(double originX, double originY, double canvasWidth, double canvasHeight)
        {
            try
            {
                var xAxis = new Line
                {
                    X1 = originX,
                    Y1 = originY,
                    X2 = originX + (_probeWorkspaceMaxX * _probeCanvasScaleFactorX),
                    Y2 = originY,
                    Stroke = Brushes.Red,
                    StrokeThickness = 4
                };
                MainCrosshairCanvas.Children.Add(xAxis);

                var yAxis = new Line
                {
                    X1 = originX,
                    Y1 = originY,
                    X2 = originX,
                    Y2 = originY - (_probeWorkspaceMaxY * _probeCanvasScaleFactorY),
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 4
                };
                MainCrosshairCanvas.Children.Add(yAxis);

                double step = 25;
                double tick = 15;

                for (double x = step; x <= _probeWorkspaceMaxX; x += step)
                {
                    double cx = originX + x * _probeCanvasScaleFactorX;
                    MainCrosshairCanvas.Children.Add(new Line
                    {
                        X1 = cx,
                        Y1 = originY,
                        X2 = cx,
                        Y2 = originY + tick,
                        Stroke = Brushes.White,
                        StrokeThickness = 2
                    });
                    var lbl = new TextBlock
                    {
                        Text = x.ToString("F0"),
                        Foreground = Brushes.White,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
                    };
                    Canvas.SetLeft(lbl, cx - 10);
                    Canvas.SetTop(lbl, originY + tick + 5);
                    MainCrosshairCanvas.Children.Add(lbl);
                }

                for (double y = step; y <= _probeWorkspaceMaxY; y += step)
                {
                    double cy = originY - y * _probeCanvasScaleFactorY;
                    MainCrosshairCanvas.Children.Add(new Line
                    {
                        X1 = originX,
                        Y1 = cy,
                        X2 = originX - tick,
                        Y2 = cy,
                        Stroke = Brushes.White,
                        StrokeThickness = 2
                    });
                    var lbl = new TextBlock
                    {
                        Text = y.ToString("F0"),
                        Foreground = Brushes.White,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
                    };
                    Canvas.SetLeft(lbl, originX - tick - 25);
                    Canvas.SetTop(lbl, cy - 8);
                    MainCrosshairCanvas.Children.Add(lbl);
                }

                var xLabel = new TextBlock
                {
                    Text = $"X (max {_probeWorkspaceMaxX:F0})",
                    Foreground = Brushes.Red,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0))
                };
                Canvas.SetLeft(xLabel, originX + (_probeWorkspaceMaxX * _probeCanvasScaleFactorX) / 2 - 60);
                Canvas.SetTop(xLabel, originY + 25);
                MainCrosshairCanvas.Children.Add(xLabel);

                var yLabel = new TextBlock
                {
                    Text = $"Y (max {_probeWorkspaceMaxY:F0})",
                    Foreground = Brushes.LimeGreen,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    RenderTransform = new RotateTransform(-90)
                };
                Canvas.SetLeft(yLabel, originX - 55);
                Canvas.SetTop(yLabel, originY - (_probeWorkspaceMaxY * _probeCanvasScaleFactorY) / 2 + 55);
                MainCrosshairCanvas.Children.Add(yLabel);
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe axis draw error: {ex.Message}", true);
            }
        }

        private void DrawWorkspaceGrid(double originX, double originY)
        {
            try
            {
                double step = 25.0;
                for (double x = step; x < _probeWorkspaceMaxX; x += step)
                {
                    double cx = originX + x * _probeCanvasScaleFactorX;
                    MainGridLinesCanvas.Children.Add(new Line
                    {
                        X1 = cx,
                        Y1 = originY,
                        X2 = cx,
                        Y2 = originY - (_probeWorkspaceMaxY * _probeCanvasScaleFactorY),
                        Stroke = Brushes.DarkGray,
                        StrokeThickness = 1,
                        Opacity = 0.35,
                        StrokeDashArray = new DoubleCollection { 2, 2 }
                    });
                }
                for (double y = step; y < _probeWorkspaceMaxY; y += step)
                {
                    double cy = originY - y * _probeCanvasScaleFactorY;
                    MainGridLinesCanvas.Children.Add(new Line
                    {
                        X1 = originX,
                        Y1 = cy,
                        X2 = originX + (_probeWorkspaceMaxX * _probeCanvasScaleFactorX),
                        Y2 = cy,
                        Stroke = Brushes.DarkGray,
                        StrokeThickness = 1,
                        Opacity = 0.35,
                        StrokeDashArray = new DoubleCollection { 2, 2 }
                    });
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe grid error: {ex.Message}", true);
            }
        }

        private void DrawOriginPoint(double originX, double originY)
        {
            try
            {
                var origin = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.Cyan,
                    Stroke = Brushes.DarkBlue,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(origin, originX - 6);
                Canvas.SetTop(origin, originY - 6);
                MainCrosshairCanvas.Children.Add(origin);

                var label = new TextBlock
                {
                    Text = "(0,0)",
                    Foreground = Brushes.Cyan,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Padding = new Thickness(3),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, originX + 15);
                Canvas.SetTop(label, originY - 25);
                MainCrosshairCanvas.Children.Add(label);
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe origin draw error: {ex.Message}", true);
            }
        }

        private void DrawWorkspaceInfoBox(double originX, double originY)
        {
            try
            {
                var info = new TextBlock
                {
                    Text = $"Workspace\nX 0–{_probeWorkspaceMaxX:F0}\nY 0–{_probeWorkspaceMaxY:F0}\nZ 0–{_probeWorkspaceMaxZ:F0}",
                    Foreground = Brushes.Orange,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Padding = new Thickness(6)
                };
                double boxX = originX + (_probeWorkspaceMaxX * _probeCanvasScaleFactorX) - 140;
                double boxY = originY - (_probeWorkspaceMaxY * _probeCanvasScaleFactorY) + 10;
                Canvas.SetLeft(info, boxX);
                Canvas.SetTop(info, boxY);
                MainCrosshairCanvas.Children.Add(info);
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe info box error: {ex.Message}", true);
            }
        }

        private void LoadWorkspaceLimitsForProbePanel()
        {
            try
            {
                if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)      
                {
                    var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                    var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);
                    var zLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 132);

                    if (xLimit != null && double.TryParse(xLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double x))
                        _probeWorkspaceMaxX = Math.Abs(x);
                    if (yLimit != null && double.TryParse(yLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                        _probeWorkspaceMaxY = Math.Abs(y);
                    if (zLimit != null && double.TryParse(zLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double z))
                        _probeWorkspaceMaxZ = Math.Abs(z);

                    LogProbe($"> Probe workspace limits X:{_probeWorkspaceMaxX} Y:{_probeWorkspaceMaxY} Z:{_probeWorkspaceMaxZ}");
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe limit read error: {ex.Message}", true);
            }
        }

        private void UpdateCurrentPositionOnCanvas()
        {
            try
            {
                if (!_probePanelVisible ||
                    MainCrosshairCanvas == null ||
                    App.MainController?.MStatus == null)
                {
                    return;
                }

                double currentX = App.MainController.MStatus.WorkX;
                double currentY = App.MainController.MStatus.WorkY;
                double currentZ = App.MainController.MStatus.WorkZ;

                double canvasWidth = MainCrosshairCanvas.ActualWidth;
                double canvasHeight = MainCrosshairCanvas.ActualHeight;
                if (canvasWidth <= 0) canvasWidth = 500;
                if (canvasHeight <= 0) canvasHeight = 400;

                double margin = 50;
                double originX = margin;
                double originY = canvasHeight - margin;

                double canvasX = originX + (currentX * _probeCanvasScaleFactorX);
                double canvasY = originY - (currentY * _probeCanvasScaleFactorY);

                bool inside = (currentX >= 0 && currentX <= _probeWorkspaceMaxX &&
                               currentY >= 0 && currentY <= _probeWorkspaceMaxY);

                if (_currentPositionIndicator != null)
                    MainCrosshairCanvas.Children.Remove(_currentPositionIndicator);
                if (_currentPositionLabel != null)
                    MainCrosshairCanvas.Children.Remove(_currentPositionLabel);

                _currentPositionIndicator = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = inside ? Brushes.Yellow : Brushes.Orange,
                    Stroke = inside ? Brushes.Gold : Brushes.Red,
                    StrokeThickness = 3,
                    Opacity = 0.9
                };
                Canvas.SetLeft(_currentPositionIndicator, canvasX - 8);
                Canvas.SetTop(_currentPositionIndicator, canvasY - 8);
                Canvas.SetZIndex(_currentPositionIndicator, 100);
                MainCrosshairCanvas.Children.Add(_currentPositionIndicator);

                _currentPositionLabel = new TextBlock
                {
                    Text = $"X:{currentX:F2}\nY:{currentY:F2}\nZ:{currentZ:F2}",
                    Foreground = Brushes.Yellow,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    Padding = new Thickness(4),
                    TextAlignment = TextAlignment.Center
                };

                double labelX = canvasX + 18;
                double labelY = canvasY - 28;
                if (labelX + 120 > canvasWidth) labelX = canvasX - 130;
                if (labelY < 0) labelY = canvasY + 14;

                Canvas.SetLeft(_currentPositionLabel, labelX);
                Canvas.SetTop(_currentPositionLabel, labelY);
                Canvas.SetZIndex(_currentPositionLabel, 99);
                MainCrosshairCanvas.Children.Add(_currentPositionLabel);

                if (!inside && DateTime.Now.Subtract(_lastWorkspaceWarning).TotalSeconds > 5)
                {
                    LogProbe($"> ⚠️ Position outside workspace X={currentX:F2} Y={currentY:F2}");
                    _lastWorkspaceWarning = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe position update error: {ex.Message}", true);
            }
        }

        private void UpdateMainProbeCoordinates()
        {
            try
            {
                if (App.MainController?.MStatus != null)
                {
                    _lastProbeX = App.MainController.MStatus.WorkX;
                    _lastProbeY = App.MainController.MStatus.WorkY;
                    _lastProbeZ = App.MainController.MStatus.WorkZ;

                    MainProbeXCoordinate.Text = $"X: {_lastProbeX:F3}";
                    MainProbeYCoordinate.Text = $"Y: {_lastProbeY:F3}";
                    MainProbeZCoordinate.Text = $"Z: {_lastProbeZ:F3}";

                    // Stats UI removed; keep only drawing on canvas
                    AddProbePointToCanvas(_lastProbeX, _lastProbeY, _lastProbeZ);

                    LogProbe($"> Probe point captured ({_lastProbeX:F2},{_lastProbeY:F2},{_lastProbeZ:F2})");
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe coord update error: {ex.Message}", true);
            }
        }

        private void AddProbePointToCanvas(double probeX, double probeY, double probeZ)
        {
            try
            {
                if (MainProbeCoordinatesCanvas == null) return;

                double canvasWidth = MainProbeCoordinatesCanvas.ActualWidth;
                double canvasHeight = MainProbeCoordinatesCanvas.ActualHeight;
                if (canvasWidth <= 0) canvasWidth = 500;
                if (canvasHeight <= 0) canvasHeight = 400;

                double margin = 50;
                double originX = margin;
                double originY = canvasHeight - margin;

                double canvasX = originX + probeX * _probeCanvasScaleFactorX;
                double canvasY = originY - probeY * _probeCanvasScaleFactorY;

                bool inside = (probeX >= 0 && probeX <= _probeWorkspaceMaxX &&
                               probeY >= 0 && probeY <= _probeWorkspaceMaxY);

                var probePoint = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = inside ? Brushes.LimeGreen : Brushes.Red,
                    Stroke = inside ? Brushes.DarkGreen : Brushes.DarkRed,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(probePoint, canvasX - 4);
                Canvas.SetTop(probePoint, canvasY - 4);
                MainProbeCoordinatesCanvas.Children.Add(probePoint);

                var label = new TextBlock
                {
                    Text = $"X:{probeX:F1}\nY:{probeY:F1}\nZ:{probeZ:F1}",
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Background = inside ? Brushes.DarkGreen : Brushes.DarkRed,
                    Padding = new Thickness(2)
                };
                Canvas.SetLeft(label, canvasX + 8);
                Canvas.SetTop(label, canvasY - 15);
                MainProbeCoordinatesCanvas.Children.Add(label);
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe point draw error: {ex.Message}", true);
            }
        }

        private void UpdateProbeStatistics()
        {
            try
            {
                // Stats UI removed; nothing to update in UI.
                // Keep method for compatibility with existing calls.
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe stats error: {ex.Message}", true);
            }
        }

        */
        #endregion

        /* Canvas position tracking removed
        #region Current Position Tracking

        private void MStatusPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(App.MainController.MStatus.WorkX) ||
                    e.PropertyName == nameof(App.MainController.MStatus.WorkY) ||
                    e.PropertyName == nameof(App.MainController.MStatus.WorkZ))
                {
                    Dispatcher.BeginInvoke(new Action(UpdateCurrentPositionOnCanvas),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                LogProbe($"> ❌ Probe coord property error: {ex.Message}", true);
            }
        }

        #endregion
        */

        #region Probe Action Button Event Handlers (RESTORED)

        // ✅ X Mid / Y Mid handlers moved to MainWindow.ProbeMidScan.cs
        // ✅ FindAxisMidByZScanAsync moved to MainWindow.ProbeMidScan.cs
        // ✅ ParseMidOffset moved to MainWindow.ProbeMidScan.cs

      #endregion

        private async void EmergencyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> 🚨 ACİL STOP başlatılıyor...");
                if (App.MainController?.IsConnected == true)
                {
                    bool emergencyStopSuccess = await App.MainController.EmergencyStopAsync();
                    bool resetSuccess = await App.MainController.EmergencyResetAsync();
                    bool recoverySuccess = await App.MainController.RecoverFromAlarmAsync();

                    if (emergencyStopSuccess && resetSuccess && recoverySuccess)
                    {
                        App.MainController?.AddLogMessage("> ✅ ACİL STOP tamamlandı");
                    }
                    else
                    {
                        App.MainController?.AddLogMessage("> ❌ ACİL STOP bazı adımlar başarısız");
                    }
                }
                else
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil (Acil Stop)");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ KRITIK HATA: Emergency stop - {ex.Message}");
            }
        }

        private async void ClearAlarmTopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mc = App.MainController;
                if (mc?.IsConnected != true)
                {
                    mc?.AddLogMessage("> ❌ Bağlantı yok - Alarm çözülemedi");
                    return;
                }

                mc.AddLogMessage("> 🧹 Alarm temizleme (üst buton) başlatılıyor...");
                await mc.SendControlCharacterAsync('\x18'); // Ctrl+X
                await Task.Delay(700);                      // bir miktar daha bekle

                await mc.SendGCodeCommandAsync("$X");      // patch #1 ile izinli
                await Task.Delay(300);

                await mc.SendGCodeCommandAsync("?");       // durum güncelle
                await Task.Delay(200);

                mc.AddLogMessage("> ✅ Alarm temizleme tamamlandı (gerekirse $H ile Homing yapın)");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Alarm temizleme hata: {ex.Message}");
            }
        }

        #region Debug Report (Temporary Diagnostic Button)

        private void DebugReportButton_Click(object sender, RoutedEventArgs e)
        {
   try
            {
      App.MainController?.AddLogMessage("> 🔍 Debug report kaydediliyor...");

   // DiagnosticTimingService'ten rapor oluştur ve dosyaya kaydet
    string reportPath = CncControlApp.Services.DiagnosticTimingService.SaveReportToFile();

        if (!string.IsNullOrEmpty(reportPath))
        {
          App.MainController?.AddLogMessage($"> ✅ Diagnostic report saved: {reportPath}");
          System.Diagnostics.Debug.WriteLine($"[DEBUG_REPORT] Path: {reportPath}");
      }
     else
             {
   App.MainController?.AddLogMessage("> ❌ Debug report kaydedilemedi (dosya yazma hatası)");
          }
    }
 catch (Exception ex)
   {
       App.MainController?.AddLogMessage($"> ❌ Debug report hata: {ex.Message}");
   }
        }

      #endregion

      


        // New: unified handler for Set Zero buttons in probe panel top section
        private async void ProbeSetZeroButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil - Set Zero yapılamaz");
                    return;
                }

                var btn = sender as Button;
                var axis = btn?.Tag as string;
                if (string.IsNullOrWhiteSpace(axis)) return;

                bool ok = false;
                switch (axis.ToUpperInvariant())
                {
                    case "X":
                        ok = await App.MainController.SetZeroXPermanentAsync();
                        if (!ok) ok = await App.MainController.SetZeroAxisAsync("X", false);
                        break;
                    case "Y":
                        ok = await App.MainController.SetZeroYPermanentAsync();
                        if (!ok) ok = await App.MainController.SetZeroAxisAsync("Y", false);
                        break;
                    case "Z":
                        ok = await App.MainController.SetZeroZPermanentAsync();
                        if (!ok) ok = await App.MainController.SetZeroAxisAsync("Z", false);
                        break;
                    case "A":
                        ok = await App.MainController.SetZeroAxisAsync("A", true);
                        break;
                }

                if (ok)
                {
                    App.MainController?.AddLogMessage($"> ✅ {axis} ekseni sıfırlandı");
                    // Canvas removed - coordinate update not needed
                    // UpdateMainProbeCoordinates();
                }
                else
                {
                    App.MainController?.AddLogMessage($"> ❌ {axis} ekseni sıfırlanamadı");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ HATA: Set Zero - {ex.Message}");
            }
        }

        // === EVENT-DRIVEN PROBE (OUTER EDGE X CENTER TEST) ===
        // Deneme amaçlı: Sadece X ekseninde iki tarafı probe ederek merkez bulur.
        // Polling/WaitForMotionComplete kullanmaz; PRB event'ini dinler.
        private async Task<bool> EventDrivenOuterXCenterProbeAsync(double travelEachSide = 25.0, int feed = 100)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil - Event driven X center yapılamaz");
                    return false;
                }

                if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G91"))
                {
                    App.MainController?.AddLogMessage("> ❌ G91 ayarlanamadı (event driven)");
                    return false;
                }

                App.MainController?.AddLogMessage("> 🔧 [EVT] X center başlıyor (outer edge) - event tabanlı");

                async Task<(bool ok, double axisVal)> ProbeOneAsync(double distance, char axisChar)
                {
                    try
                    {
                        var cmd = $"G38.2 {axisChar}{distance.ToString("F3", CultureInfo.InvariantCulture)} F{feed}";
                        DateTime sendTs = DateTime.UtcNow;
                        App.MainController?.AddLogMessage($"> ▶ [EVT] SEND {cmd}");
                        if (!await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd))
                        {
                            App.MainController?.AddLogMessage($"> ❌ [EVT] Komut gönderilemedi: {cmd}");
                            return (false, double.NaN);
                        }

                        double tSeconds = (Math.Abs(distance) / Math.Max(1, feed)) * 60.0 * 2.0;
                        var timeout = TimeSpan.FromSeconds(Math.Max(2.0, Math.Min(30.0, tSeconds)));
                        (double x, double y, double z, DateTime ts) contact;
                        try { contact = await ProbeContactCache.WaitNextAsync(timeout); }
                        catch (TaskCanceledException) { App.MainController?.AddLogMessage("> ⌛ [EVT] PRB timeout – temas yok"); return (false, double.NaN); }
                        if (contact.ts <= sendTs)
                        {
                            App.MainController?.AddLogMessage("> ⚠️ [EVT] Eski PRB yakalandı (zaman uyuşmazlığı)");
                            return (false, double.NaN);
                        }
                        await App.MainController.SendGCodeCommandAsync("?");
                        await Task.Delay(120);
                        var ms = App.MainController.MStatus;
                        return (true, ms.WorkX);
                    }
                    catch (Exception ex)
                    {
                        App.MainController?.AddLogMessage($"> ❌ [EVT] ProbeOne hata: {ex.Message}");
                        return (false, double.NaN);
                    }
                }

                // Sağ (pozitif) dış kenar
                var pos = await ProbeOneAsync(+travelEachSide, 'X');
                if (!pos.ok) { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); return false; }
                double posEdge = pos.axisVal;
                App.MainController?.AddLogMessage($"> ✅ [EVT] Pozitif kenar X={posEdge:F3}");

                // 3mm geri çek (negatif yöne) - eventless basit G00
                await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 X-3.000");
                await Task.Delay(150);

                // Negatif dış kenar
                var neg = await ProbeOneAsync(-travelEachSide * 2, 'X');
                if (!neg.ok) { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); return false; }
                double negEdge = neg.axisVal;
                App.MainController?.AddLogMessage($"> ✅ [EVT] Negatif kenar X={negEdge:F3}");

                double center = (posEdge + negEdge) / 2.0;
                App.MainController?.AddLogMessage($"> 🎯 [EVT] Hesaplanan merkez X={center:F3}");

                // Absolute moda dön ve merkez noktasına git (G90 + G0 X..). Şu an relative moddayız.
                await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");

                // Mevcut WorkX al
                double currentWX = App.MainController.MStatus.WorkX;
                double deltaToCenter = center - currentWX;
                string moveToCenter = $"G91 G00 X{deltaToCenter.ToString("F3", CultureInfo.InvariantCulture)} G90";
                await App.MainController.SendGCodeCommandWithConfirmationAsync(moveToCenter);
                await Task.Delay(150);
                await App.MainController.SendGCodeCommandAsync("?");
                await Task.Delay(120);

                // X=0 yapmak için permanent ya da geçici (şimdilik geçici)
                if (!await App.MainController.SetZeroAxisAsync("X", false))
                    App.MainController?.AddLogMessage("> ⚠️ [EVT] X sıfırlama (G92) başarısız – manuel kontrol edin");
                else
                    App.MainController?.AddLogMessage("> ✅ [EVT] X=0 ayarlandı (geçici G92)");

                // Canvas removed - coordinate update not needed
                // UpdateMainProbeCoordinates();
                return true;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ [EVT] X center hata: {ex.Message}");
                try { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); } catch { }
                return false;
            }
        }

        private async Task<bool> EventDrivenOuterYCenterProbeAsync(double travelEachSide = 25.0, int feed = 100)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil - Event driven Y center yapılamaz");
                    return false;
                }

                if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G91"))
                {
                    App.MainController?.AddLogMessage("> ❌ G91 ayarlanamadı (event driven)");
                    return false;
                }

                App.MainController?.AddLogMessage("> 🔧 [EVT] Y center başlıyor (outer edge) - event tabanlı");

                async Task<(bool ok, double axisVal)> ProbeOneAsync(double distance, char axisChar)
                {
                    try
                    {
                        var cmd = $"G38.2 {axisChar}{distance.ToString("F3", CultureInfo.InvariantCulture)} F{feed}";
                        DateTime sendTs = DateTime.UtcNow;
                        App.MainController?.AddLogMessage($"> ▶ [EVT] SEND {cmd}");
                        if (!await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd))
                        {
                            App.MainController?.AddLogMessage($"> ❌ [EVT] Komut gönderilemedi: {cmd}");
                            return (false, double.NaN);
                        }

                        double tSeconds = (Math.Abs(distance) / Math.Max(1, feed)) * 60.0 * 2.0;
                        var timeout = TimeSpan.FromSeconds(Math.Max(2.0, Math.Min(30.0, tSeconds)));
                        (double x, double y, double z, DateTime ts) contact;
                        try { contact = await ProbeContactCache.WaitNextAsync(timeout); }
                        catch (TaskCanceledException) { App.MainController?.AddLogMessage("> ⌛ [EVT] PRB timeout – temas yok"); return (false, double.NaN); }
                        if (contact.ts <= sendTs)
                        {
                            App.MainController?.AddLogMessage("> ⚠️ [EVT] Eski PRB yakalandı (zaman uyuşmazlığı)");
                            return (false, double.NaN);
                        }
                        await App.MainController.SendGCodeCommandAsync("?");
                        await Task.Delay(120);
                        var ms = App.MainController.MStatus;
                        return (true, ms.WorkY);
                    }
                    catch (Exception ex)
                    {
                        App.MainController?.AddLogMessage($"> ❌ [EVT] ProbeOne(Y) hata: {ex.Message}");
                        return (false, double.NaN);
                    }
                }

                var pos = await ProbeOneAsync(+travelEachSide, 'Y');
                if (!pos.ok) { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); return false; }
                double posEdge = pos.axisVal;
                App.MainController?.AddLogMessage($"> ✅ [EVT] Pozitif kenar Y={posEdge:F3}");

                await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 Y-3.000");
                await Task.Delay(150);

                var neg = await ProbeOneAsync(-travelEachSide * 2, 'Y');
                if (!neg.ok) { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); return false; }
                double negEdge = neg.axisVal;
                App.MainController?.AddLogMessage($"> ✅ [EVT] Negatif kenar Y={negEdge:F3}");

                double center = (posEdge + negEdge) / 2.0;
                App.MainController?.AddLogMessage($"> 🎯 [EVT] Hesaplanan merkez Y={center:F3}");

                await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                double currentWY = App.MainController.MStatus.WorkY;
                double deltaToCenter = center - currentWY;
                string moveToCenter = $"G91 G00 Y{deltaToCenter.ToString("F3", CultureInfo.InvariantCulture)} G90";
                await App.MainController.SendGCodeCommandWithConfirmationAsync(moveToCenter);
                await Task.Delay(150);
                await App.MainController.SendGCodeCommandAsync("?");
                await Task.Delay(120);

                if (!await App.MainController.SetZeroAxisAsync("Y", false))
                    App.MainController?.AddLogMessage("> ⚠️ [EVT] Y sıfırlama (G92) başarısız – manuel kontrol edin");
                else
                    App.MainController?.AddLogMessage("> ✅ [EVT] Y=0 ayarlandı (geçici G92)");

                // Canvas removed - coordinate update not needed
                // UpdateMainProbeCoordinates();
                return true;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ [EVT] Y center hata: {ex.Message}");
                try { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); } catch { }
                return false;
            }
        }

        private async Task<bool> EventDrivenOuterXYCenterProbeAsync(double travelEachSide = 25.0, int feed = 100)
        {
            App.MainController?.AddLogMessage("> 🔧 [EVT] XY center (outer) başlatılıyor (X sonra Y)");
            var okX = await EventDrivenOuterXCenterProbeAsync(travelEachSide, feed);
            if (!okX) { App.MainController?.AddLogMessage("> ❌ [EVT] XY center: X aşaması başarısız"); return false; }
            var okY = await EventDrivenOuterYCenterProbeAsync(travelEachSide, feed);
            if (!okY) { App.MainController?.AddLogMessage("> ❌ [EVT] XY center: Y aşaması başarısız"); return false; }
            App.MainController?.AddLogMessage("> 🎉 [EVT] XY center tamamlandı (X=0, Y=0) ");
            return true;
        }

        // Center probe event handlers will be added here
        private async void Panel_CenterXOuterClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> ▶ Center X (outer edges) başlatılıyor – Z probe ile başlar");
                await CenterXOuterSequenceAsync();
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Center X (outer) hata: {ex.Message}");
            }
        }

        private async void Panel_CenterYOuterClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> ▶ Center Y (outer edges) başlatılıyor – Z probe ile başlar");
                await CenterYOuterSequenceAsync();
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Center Y (outer) hata: {ex.Message}");
            }
        }

        private async void Panel_CenterXYOuterClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> ▶ Center XY (outer edges) başlatılıyor – her eksen Z probe ile başlar");
                var okX = await CenterXOuterSequenceAsync();
                if (!okX)
                {
                    App.MainController?.AddLogMessage("> ❌ Center XY: X aşaması başarısız");
                    return;
                }
                var okY = await CenterYOuterSequenceAsync();
                if (!okY)
                {
                    App.MainController?.AddLogMessage("> ❌ Center XY: Y aşaması başarısız");
                    return;
                }
                App.MainController?.AddLogMessage("> ✅ Center XY (outer) tamamlandı");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Center XY (outer) hata: {ex.Message}");
            }
        }

        private void FitToScreen()
        {
            try
            {
                var wa = SystemParameters.WorkArea;
                double tW = wa.Width * 0.9;
                double tH = wa.Height * 0.9;
                double minW = 1200;
                double minH = 800;

                Width = Math.Max(tW, minW);
                Height = Math.Max(tH, minH);

                if (Width > wa.Width) Width = wa.Width * 0.95;
                if (Height > wa.Height) Height = wa.Height * 0.95;

                Left = (wa.Width - Width) / 2 + wa.Left;
                Top = (wa.Height - Height) / 2 + wa.Top;

                MinWidth = minW;
                MinHeight = minH;

                App.MainController?.LogMessages.Add($"> Pencere automatik olarak ekrana sığdırıldı ({Width:F0}x{Height:F0})");
            }
            catch (Exception ex)
            {
                Width = 1350;
                Height = 900;
                MinWidth = 1200;
                MinHeight = 800;
                App.MainController?.LogMessages.Add($"> UYARI: Pencere boyutu ayarlanırken hata - {ex.Message}");
            }
        }

        private void OnMenuSelectionChanged(MenuPage page) => NavigateToPage(page);
        private void OnNavigateToPanel(MenuPage page) => NavigateToPage(page);

        private void NavigateToPage(MenuPage page)
        {
 switch (page)
 {
 case MenuPage.Status:
 MainContent.Content = _statusView;
 if (MainProbePanel != null) MainProbePanel.Visibility = Visibility.Collapsed;
 if (MainCoordinatesView != null) MainCoordinatesView.Visibility = Visibility.Collapsed;
 if (HomeZeroActionsPanel != null) HomeZeroActionsPanel.Visibility = Visibility.Collapsed;
            // Hide canvas in Settings view
      var canvas1 = FindName("MainPanelJogCanvas") as FrameworkElement;
      if (canvas1 != null) canvas1.Visibility = Visibility.Collapsed;
 ToggleRightBlank(true);
 break;
 case MenuPage.GCode:
 MainContent.Content = _gcodeView;
 if (MainProbePanel != null) MainProbePanel.Visibility = Visibility.Collapsed;
 if (MainCoordinatesView != null) MainCoordinatesView.Visibility = Visibility.Visible;
 if (HomeZeroActionsPanel != null) HomeZeroActionsPanel.Visibility = Visibility.Visible;
    // Show canvas in GCode view
         var canvas2 = FindName("MainPanelJogCanvas") as FrameworkElement;
          if (canvas2 != null) canvas2.Visibility = Visibility.Visible;
 ToggleRightBlank(false);
 break;
 case MenuPage.Jog:
 MainContent.Content = _jogView;
 if (MainProbePanel != null) MainProbePanel.Visibility = Visibility.Collapsed;
 if (MainCoordinatesView != null) MainCoordinatesView.Visibility = Visibility.Visible;
 if (HomeZeroActionsPanel != null) HomeZeroActionsPanel.Visibility = Visibility.Visible;
           // Show canvas in Jog view
          var canvas3 = FindName("MainPanelJogCanvas") as FrameworkElement;
   if (canvas3 != null) canvas3.Visibility = Visibility.Visible;
 ToggleRightBlank(false);
 break;
 case MenuPage.Console:
 MainContent.Content = _consoleView;
 if (MainProbePanel != null) MainProbePanel.Visibility = Visibility.Collapsed;
 if (MainCoordinatesView != null) MainCoordinatesView.Visibility = Visibility.Visible;
 if (HomeZeroActionsPanel != null) HomeZeroActionsPanel.Visibility = Visibility.Collapsed;
      // Show canvas in Console view
      var canvas4 = FindName("MainPanelJogCanvas") as FrameworkElement;
       if (canvas4 != null) canvas4.Visibility = Visibility.Visible;
 ToggleRightBlank(false);
 break;
 case MenuPage.Probe:
 MainContent.Content = _jogView; // host jog for background controls
 if (MainCoordinatesView != null) MainCoordinatesView.Visibility = Visibility.Visible; // keep coordinates panel
 if (HomeZeroActionsPanel != null) HomeZeroActionsPanel.Visibility = Visibility.Collapsed; // hide home/zero panel
        // Hide canvas when probe panel is active
        var canvas5 = FindName("MainPanelJogCanvas") as FrameworkElement;
     if (canvas5 != null) canvas5.Visibility = Visibility.Collapsed;
 if (MainProbePanel != null)
 {
 MainProbePanel.Visibility = Visibility.Visible; // show probe UI in bottom row
 }
 ToggleRightBlank(false);
 // Canvas removed - no initialization needed
 // Dispatcher.BeginInvoke(new Action(InitializeProbePanel), System.Windows.Threading.DispatcherPriority.Loaded);
 break;
 }
 }

        private void ToggleRightBlank(bool show)
        {
            var blank = this.FindName("RightBlankPanel") as FrameworkElement;
            if (blank != null) blank.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure initial page state reflects Status (Settings): show blank right panel, hide coordinates
                NavigateToPage(MenuPage.Status);
                EnterKioskMode();
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> UYARI: Açılışta kiosk modu başlatılamadı - {ex.Message}");
            }
#if DEBUG
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(2000);
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CncControlApp.Tests.TestRunner.RunGCodeOverlayTests();
                    }));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background test execution error: {ex.Message}");
                }
            });
#endif
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (CncControlApp.Controls.NumberPadPopup.IsAnyNumberPadActive)
            {
                if (e.Key != Key.Escape)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleKioskMode();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _isKioskMode)
            {
                ExitKioskMode();
                e.Handled = true;
            }
        }

        public void ToggleKioskMode() => (_isKioskMode ? (Action)ExitKioskMode : EnterKioskMode)();

        public void EnterKioskMode()
        {
            if (_isKioskMode) return;
            try
            {
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;
                _previousResizeMode = ResizeMode;
                _previousTopmost = Topmost;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                if (!CncControlApp.Controls.NumberPadPopup.IsAnyNumberPadActive)
                    Topmost = true;
                WindowState = WindowState.Maximized;
                _isKioskMode = true;

                App.MainController?.LogMessages.Add("> KIOSK MODU AKTİF - Tam ekran CNC kontrol");
                App.MainController?.LogMessages.Add("> ESC tuşu ile çıkış yapabilirsiniz");
            }
            catch (Exception ex)
            {
                App.MainController?.LogMessages.Add($"> HATA: Kiosk modu açılamadı - {ex.Message}");
            }
        }

        public void ExitKioskMode()
        {
            if (!_isKioskMode) return;
            try
            {
                WindowStyle = _previousWindowStyle;
                ResizeMode = _previousResizeMode;
                if (!CncControlApp.Controls.NumberPadPopup.IsAnyNumberPadActive)
                    Topmost = _previousTopmost;
                WindowState = _previousWindowState;
                _isKioskMode = false;
                App.MainController?.LogMessages.Add("> Kiosk modundan çıkıldı");
            }
            catch (Exception ex)
            {
                App.MainController?.LogMessages.Add($"> HATA: Kiosk modundan çıkılamadı - {ex.Message}");
            }
        }

        public void BlockUIForNumberPad()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (Topmost)
                    {
                        Topmost = false;
                        _mainWindowTopmostDisabledForNumpad = true;
                        App.MainController?.AddLogMessage("> 🔧 MainWindow Topmost devre dışı - NumPad");
                    }

                    NumberPadOverlay.Visibility = Visibility.Visible;
                    MainMenuView.IsEnabled = false;
                    MainContent.IsEnabled = false;

                    var coordinatesView = FindName("MainCoordinatesView") as FrameworkElement;
                    if (coordinatesView != null)
                        DisableUIElementsExceptTextBoxes(coordinatesView);

                    App.MainController?.AddLogMessage("> 🔒 UI engellendi - Numpad aktif");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error blocking UI for NumberPad: {ex.Message}");
            }
        }

        public void UnblockUIForNumberPad()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_mainWindowTopmostDisabledForNumpad)
                    {
                        if (_isKioskMode)
                            Topmost = true;
                        _mainWindowTopmostDisabledForNumpad = false;
                        App.MainController?.AddLogMessage("> 🔧 MainWindow Topmost geri yüklendi");
                    }

                    NumberPadOverlay.Visibility = Visibility.Collapsed;
                    MainMenuView.IsEnabled = true;
                    MainContent.IsEnabled = true;

                    var coordinatesView = FindName("MainCoordinatesView") as FrameworkElement;
                    if (coordinatesView != null)
                        EnableAllUIElements(coordinatesView);

                    App.MainController?.AddLogMessage("> 🔓 UI engeli kaldırıldı - Numpad kapatıldı");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error unblocking UI for NumberPad: {ex.Message}");
            }
        }

        public void ActivateAlarmState()
        {
            if (StatusLED != null) StatusLED.Fill = new SolidColorBrush(Colors.Yellow);
      if (StatusText != null) StatusText.Text = "Alarm";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Link açılamadı: {ex.Message}");
            }
            e.Handled = true;
        }

        #region Probe History Panel Management

        private void ShowProbeHistoryPanel()
        {
            try
            {
                if (ProbeHistoryPanel != null)
                {
                    // Set column width to 30% of available space
                    ProbeHistoryColumn.Width = new GridLength(0.35, GridUnitType.Star);
                    ProbeHistoryPanel.Visibility = Visibility.Visible;
                    
                    // Bind to probe history manager
                    ProbeHistoryDataGrid.ItemsSource = ProbeHistoryManager.Instance.ProbeRecords;
                    
                    LogProbe("> 📋 Probe history panel opened");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing probe history panel: {ex.Message}");
            }
        }

        private void HideProbeHistoryPanel()
        {
            try
            {
                if (ProbeHistoryPanel != null)
                {
                    ProbeHistoryPanel.Visibility = Visibility.Collapsed;
                    ProbeHistoryColumn.Width = new GridLength(0);
                    
                    LogProbe("> 📋 Probe history panel closed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error hiding probe history panel: {ex.Message}");
            }
        }

        private void ClearProbeHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Tüm probe geçmişi silinecek. Emin misiniz?",
                    "Probe Geçmişini Temizle",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ProbeHistoryManager.Instance.Clear();
                    App.MainController?.AddLogMessage("> 🗑️ Probe geçmişi temizlendi");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Probe geçmişi temizleme hatası: {ex.Message}");
            }
        }

        private void ExportProbeHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "CSV Dosyası (*.csv)|*.csv|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = ".csv",
                    FileName = $"ProbeHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    ProbeHistoryManager.Instance.ExportToCsv(dialog.FileName);
                    App.MainController?.AddLogMessage($"> 💾 Probe geçmişi dışa aktarıldı: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Probe geçmişi dışa aktarma hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Call this method when a probe operation completes to record the result
        /// </summary>
        public void RecordProbeResult(string probeType, double x, double y, double z)
        {
            try
            {
                ProbeHistoryManager.Instance.AddProbe(probeType, x, y, z);
                LogProbe($"> 📌 Probe recorded: {probeType} @ X:{x:F3} Y:{y:F3} Z:{z:F3}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error recording probe result: {ex.Message}");
            }
        }

        #endregion
    }
}