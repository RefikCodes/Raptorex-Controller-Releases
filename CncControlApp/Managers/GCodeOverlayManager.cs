using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // ✅ EKLE
using System.ComponentModel; // ✅ EKLE
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IoPath = System.IO.Path;

namespace CncControlApp.Managers
{
    /// <summary>
    /// Manages G-Code view overlays, coordinate transformations, and workspace visualizations
    /// Separated from GCodeView for better code organization and maintainability
    /// </summary>
    public class GCodeOverlayManager : IDisposable
    {
        #region Fields
        
        private Canvas _topViewCanvas;
        private Canvas _overlayCanvas;
        
        // Workspace limits and scaling
        private double _workspaceMaxX = 0;
        private double _workspaceMaxY = 0;
        private double _workspaceScale = 1.0;
        private double _workspaceOriginCanvasX = 0;
        private double _workspaceOriginCanvasY = 0;
        private bool _workspaceLimitsLoaded = false;
        
        // G-Code dimensions cache
        private double _lastXRange = 0;
        private double _lastYRange = 0;
        
        #endregion

        #region Constructor

        public GCodeOverlayManager(Canvas topViewCanvas, Canvas overlayCanvas)
        {
            _topViewCanvas = topViewCanvas ?? throw new ArgumentNullException(nameof(topViewCanvas));
            _overlayCanvas = overlayCanvas ?? throw new ArgumentNullException(nameof(overlayCanvas));
            
            // ✅ FIXED: Settings değişikliklerini dinle
            SubscribeToSettingsEvents();
            
            LoadWorkspaceLimitsFromSettings();
        }

        #endregion

        #region Public Properties

        public bool WorkspaceLimitsLoaded => _workspaceLimitsLoaded;
        public double WorkspaceMaxX => _workspaceMaxX;
        public double WorkspaceMaxY => _workspaceMaxY;
        public double LastXRange => _lastXRange;
        public double LastYRange => _lastYRange;

        // ✅ Existing scale access
        public double WorkspaceScale => _workspaceScale;
        public double WorkspaceOriginCanvasX => _workspaceOriginCanvasX;
        public double WorkspaceOriginCanvasY => _workspaceOriginCanvasY;

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates G-Code part dimensions and refreshes overlay
        /// </summary>
        public void UpdatePartDimensions(double xRange, double yRange)
        {
            _lastXRange = xRange;
            _lastYRange = yRange;
            RefreshOverlay();
        }

        /// <summary>
        /// Main method to draw complete top view overlay
        /// </summary>
        public void RefreshOverlay()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 RefreshOverlay called");
                
                if (_overlayCanvas == null || _topViewCanvas == null) return;

                double w = _overlayCanvas.ActualWidth;
                double h = _overlayCanvas.ActualHeight;
                if (w <= 0 || h <= 0)
                {
                    w = _topViewCanvas.ActualWidth;
                    h = _topViewCanvas.ActualHeight;
                }
                if (w <= 0 || h <= 0) return;

                ClearOverlay();

                // 1) Workspace limits yeniden yükle (settings değişmiş olabilir)
                if (!_workspaceLimitsLoaded) LoadWorkspaceLimitsFromSettings();

                if (_workspaceLimitsLoaded && _workspaceMaxX > 0 && _workspaceMaxY > 0)
                {
                    // Gerçek makine tablasını çiz
                    DrawMachineWorkspace(w, h);
                }
                else
                {
                    // Fallback: Basit merkezli eksen
                    DrawFallbackAxes(w, h);
                }

                // 2) G-code parça boyutu bilgisi
                DrawPartDimensions(w, h);

                // 3) Dosya adı
                DrawFileName(w, h);

                // 4) Machine position marker
                DrawMachinePositionMarker(w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GCodeOverlayManager.RefreshOverlay error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all overlay elements
        /// </summary>
        public void ClearOverlay()
        {
            try
            {
                _overlayCanvas?.Children.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GCodeOverlayManager.ClearOverlay error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ ENHANCED: Reload workspace limits from MainController settings with detailed logging
        /// </summary>
        public void LoadWorkspaceLimitsFromSettings()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 LoadWorkspaceLimitsFromSettings: Starting...");
                
                if (App.MainController?.Settings == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ LoadWorkspaceLimitsFromSettings: MainController.Settings is null");
                    _workspaceLimitsLoaded = false;
                    return;
                }

                if (App.MainController.Settings.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ LoadWorkspaceLimitsFromSettings: Settings collection is empty");
                    _workspaceLimitsLoaded = false;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"🔍 LoadWorkspaceLimitsFromSettings: Found {App.MainController.Settings.Count} settings");

                // $130 / $131 settings'lerini ara
                var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);

                System.Diagnostics.Debug.WriteLine($"🔍 LoadWorkspaceLimitsFromSettings: $130 = {(xLimit != null ? xLimit.Value : "NOT FOUND")}");
                System.Diagnostics.Debug.WriteLine($"🔍 LoadWorkspaceLimitsFromSettings: $131 = {(yLimit != null ? yLimit.Value : "NOT FOUND")}");

                double newX = 0, newY = 0;
                bool xParsed = false, yParsed = false;

                if (xLimit != null && double.TryParse(xLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double xv))
                {
                    newX = Math.Abs(xv);
                    xParsed = true;
                    System.Diagnostics.Debug.WriteLine($"✅ LoadWorkspaceLimitsFromSettings: X limit parsed: {newX:F1}mm");
                }

                if (yLimit != null && double.TryParse(yLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double yv))
                {
                    newY = Math.Abs(yv);
                    yParsed = true;
                    System.Diagnostics.Debug.WriteLine($"✅ LoadWorkspaceLimitsFromSettings: Y limit parsed: {newY:F1}mm");
                }

                if (xParsed && yParsed && newX > 0 && newY > 0)
                {
                    _workspaceMaxX = newX;
                    _workspaceMaxY = newY;
                    _workspaceLimitsLoaded = true;
                    
                    System.Diagnostics.Debug.WriteLine($"✅ LoadWorkspaceLimitsFromSettings: Workspace limits loaded successfully!");
                    System.Diagnostics.Debug.WriteLine($"   • X: {_workspaceMaxX:F0}mm (from $130)");
                    System.Diagnostics.Debug.WriteLine($"   • Y: {_workspaceMaxY:F0}mm (from $131)");
                    
                    App.MainController?.AddLogMessage($"> 🗺️ GCodeOverlayManager: Workspace limits loaded X={_workspaceMaxX:F0} Y={_workspaceMaxY:F0}");
                }
                else
                {
                    _workspaceLimitsLoaded = false;
                    System.Diagnostics.Debug.WriteLine($"❌ LoadWorkspaceLimitsFromSettings: Failed to load workspace limits");
                    System.Diagnostics.Debug.WriteLine($"   • X parsed: {xParsed}, Y parsed: {yParsed}");
                    System.Diagnostics.Debug.WriteLine($"   • X value: {newX:F1}, Y value: {newY:F1}");
                }
            }
            catch (Exception ex)
            {
                _workspaceLimitsLoaded = false;
                System.Diagnostics.Debug.WriteLine($"❌ LoadWorkspaceLimitsFromSettings error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Force refresh overlay when canvas size changes
        /// </summary>
        public void OnCanvasSizeChanged()
        {
            // Delay refresh to ensure canvas has updated size
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshOverlay();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        #endregion

        #region Private Methods - Machine Workspace Drawing

        /// <summary>
        /// ✅ UNIFIED SCALE: Table çizimi - merkezi canvas merkezinde, unified scale ile
        /// </summary>
        private void DrawMachineWorkspace(double canvasWidth, double canvasHeight)
        {
            if (!_workspaceLimitsLoaded || _workspaceMaxX <= 0 || _workspaceMaxY <= 0)
                return;

            // ✅ UNIFIED SCALE CALCULATION
            double scaleX = canvasWidth / _workspaceMaxX;
            double scaleY = canvasHeight / _workspaceMaxY;
            _workspaceScale = Math.Min(scaleX, scaleY) * 0.9; // %90 güvenlik marjı

            System.Diagnostics.Debug.WriteLine($"🔄 UNIFIED SCALE CALCULATION:");
            System.Diagnostics.Debug.WriteLine($"   • Canvas: {canvasWidth:F1}x{canvasHeight:F1}px");
            System.Diagnostics.Debug.WriteLine($"   • Table: {_workspaceMaxX:F0}x{_workspaceMaxY:F0}mm");
            System.Diagnostics.Debug.WriteLine($"   • Scale X: {scaleX:F3}, Scale Y: {scaleY:F3}");
            System.Diagnostics.Debug.WriteLine($"   • Final Scale: {_workspaceScale:F3}");

            double tablePixW = _workspaceMaxX * _workspaceScale;
            double tablePixH = _workspaceMaxY * _workspaceScale;

            // ✅ CENTER TABLE ON CANVAS
            double tableCenterX = canvasWidth / 2;
            double tableCenterY = canvasHeight / 2;
            
            // Table origin (bottom-left) için canvas koordinatları
            _workspaceOriginCanvasX = tableCenterX - (tablePixW / 2);
            _workspaceOriginCanvasY = tableCenterY + (tablePixH / 2);

            System.Diagnostics.Debug.WriteLine($"   • Table size on canvas: {tablePixW:F1}x{tablePixH:F1}px");
            System.Diagnostics.Debug.WriteLine($"   • Table center: ({tableCenterX:F1},{tableCenterY:F1})");
            System.Diagnostics.Debug.WriteLine($"   • Table origin (0,0): ({_workspaceOriginCanvasX:F1},{_workspaceOriginCanvasY:F1})");

            // Boundary rectangle - merkezli
            var rect = new Rectangle
            {
                Width = tablePixW,
                Height = tablePixH,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 5 },
                Fill = new SolidColorBrush(Color.FromArgb(24, 255, 165, 0))
            };
            Canvas.SetLeft(rect, _workspaceOriginCanvasX);
            Canvas.SetTop(rect, _workspaceOriginCanvasY - tablePixH);
            _overlayCanvas.Children.Add(rect);

            DrawWorkspaceGrid(tablePixW, tablePixH);
            DrawWorkspaceAxes(tablePixW, tablePixH);
            DrawWorkspaceOriginMarker();
            DrawWorkspaceInfoBox(tablePixW, tablePixH);
        }

        private void DrawWorkspaceGrid(double tablePixW, double tablePixH)
        {
            try
            {
                double stepMm = 50; // 50 mm grid
                var gridStroke = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                
                // Vertical grid lines
                for (double x = stepMm; x < _workspaceMaxX; x += stepMm)
                {
                    double cx = _workspaceOriginCanvasX + x * _workspaceScale;
                    _overlayCanvas.Children.Add(new Line
                    {
                        X1 = cx,
                        Y1 = _workspaceOriginCanvasY,
                        X2 = cx,
                        Y2 = _workspaceOriginCanvasY - tablePixH,
                        Stroke = gridStroke,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        Opacity = 0.5
                    });
                }
                
                // Horizontal grid lines
                for (double y = stepMm; y < _workspaceMaxY; y += stepMm)
                {
                    double cy = _workspaceOriginCanvasY - y * _workspaceScale;
                    _overlayCanvas.Children.Add(new Line
                    {
                        X1 = _workspaceOriginCanvasX,
                        Y1 = cy,
                        X2 = _workspaceOriginCanvasX + tablePixW,
                        Y2 = cy,
                        Stroke = gridStroke,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        Opacity = 0.5
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawWorkspaceGrid error: {ex.Message}");
            }
        }

        private void DrawWorkspaceAxes(double tablePixW, double tablePixH)
        {
            try
            {
                // X axis (right - red)
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = _workspaceOriginCanvasX,
                    Y1 = _workspaceOriginCanvasY,
                    X2 = _workspaceOriginCanvasX + tablePixW,
                    Y2 = _workspaceOriginCanvasY,
                    Stroke = Brushes.Red,
                    StrokeThickness = 3
                });

                // Y axis (up - green)
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = _workspaceOriginCanvasX,
                    Y1 = _workspaceOriginCanvasY,
                    X2 = _workspaceOriginCanvasX,
                    Y2 = _workspaceOriginCanvasY - tablePixH,
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 3
                });

                // Tick marks and labels every 50mm
                DrawAxisTicksAndLabels(tablePixW, tablePixH);

                // Axis labels
                DrawAxisLabels(tablePixW, tablePixH);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawWorkspaceAxes error: {ex.Message}");
            }
        }

        private void DrawAxisTicksAndLabels(double tablePixW, double tablePixH)
        {
            double tick = 10;
            double stepMm = 50;

            // X axis ticks and labels
            for (double x = stepMm; x <= _workspaceMaxX; x += stepMm)
            {
                double cx = _workspaceOriginCanvasX + x * _workspaceScale;
                
                // Tick mark
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = cx,
                    Y1 = _workspaceOriginCanvasY,
                    X2 = cx,
                    Y2 = _workspaceOriginCanvasY + tick,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5
                });
                
                // Label
                AddOverlayText(x.ToString("F0"), cx - 10, _workspaceOriginCanvasY + tick + 2, 
                              Brushes.White, 10, FontWeights.Bold, 1.0, 
                              new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
            }

            // Y axis ticks and labels
            for (double y = stepMm; y <= _workspaceMaxY; y += stepMm)
            {
                double cy = _workspaceOriginCanvasY - y * _workspaceScale;
                
                // Tick mark
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = _workspaceOriginCanvasX,
                    Y1 = cy,
                    X2 = _workspaceOriginCanvasX - tick,
                    Y2 = cy,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5
                });
                
                // Label
                AddOverlayText(y.ToString("F0"), _workspaceOriginCanvasX - tick - 28, cy - 8,
                              Brushes.White, 10, FontWeights.Bold, 1.0,
                              new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
            }
        }

        private void DrawAxisLabels(double tablePixW, double tablePixH)
        {
            // X axis label
            AddOverlayText($"X (0–{_workspaceMaxX:F0})", 
                          _workspaceOriginCanvasX + (tablePixW / 2) - 50,
                          _workspaceOriginCanvasY + 28,
                          Brushes.Red, 12, FontWeights.Bold, 1.0,
                          new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)));

            // Y axis label (rotated)
            var yLabel = new TextBlock
            {
                Text = $"Y (0–{_workspaceMaxY:F0})",
                Foreground = Brushes.LimeGreen,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                Padding = new Thickness(4, 1, 4, 1),
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetLeft(yLabel, _workspaceOriginCanvasX - 60);
            Canvas.SetTop(yLabel, _workspaceOriginCanvasY - (tablePixH / 2) + 60);
            _overlayCanvas.Children.Add(yLabel);
        }

        private void DrawWorkspaceOriginMarker()
        {
            try
            {
                // Origin circle
                var origin = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.Cyan,
                    Stroke = Brushes.DarkBlue,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(origin, _workspaceOriginCanvasX - 5);
                Canvas.SetTop(origin, _workspaceOriginCanvasY - 5);
                _overlayCanvas.Children.Add(origin);

                // Origin label
                AddOverlayText("(0,0)", _workspaceOriginCanvasX + 12, _workspaceOriginCanvasY - 28,
                              Brushes.Cyan, 11, FontWeights.Bold, 1.0,
                              new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawWorkspaceOriginMarker error: {ex.Message}");
            }
        }

        private void DrawWorkspaceInfoBox(double tablePixW, double tablePixH)
        {
            try
            {
                AddOverlayText($"Machine Table\nX:0–{_workspaceMaxX:F0}\nY:0–{_workspaceMaxY:F0}",
                              _workspaceOriginCanvasX + tablePixW - 120,
                              _workspaceOriginCanvasY - tablePixH + 8,
                              Brushes.Orange, 10, FontWeights.Bold, 1.0,
                              new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawWorkspaceInfoBox error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Fallback and Info Drawing

        private void DrawFallbackAxes(double canvasWidth, double canvasHeight)
        {
            try
            {
                // Eski merkezli eksen fallback
                double cx = canvasWidth / 2.0;
                double cy = canvasHeight / 2.0;

                var axisStroke = new SolidColorBrush(Color.FromRgb(180, 180, 180));
                var axisThinStroke = new SolidColorBrush(Color.FromRgb(120, 120, 120));

                // Horizontal axis
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = cy,
                    X2 = canvasWidth,
                    Y2 = cy,
                    Stroke = axisStroke,
                    StrokeThickness = 1.5,
                    SnapsToDevicePixels = true
                });

                // Vertical axis
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = cx,
                    Y1 = 0,
                    X2 = cx,
                    Y2 = canvasHeight,
                    Stroke = axisStroke,
                    StrokeThickness = 1.5,
                    SnapsToDevicePixels = true
                });

                // Center cross
                double cross = Math.Min(canvasWidth, canvasHeight) * 0.015;
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = cx - cross,
                    Y1 = cy,
                    X2 = cx + cross,
                    Y2 = cy,
                    Stroke = axisThinStroke,
                    StrokeThickness = 1
                });
                _overlayCanvas.Children.Add(new Line
                {
                    X1 = cx,
                    Y1 = cy - cross,
                    X2 = cx,
                    Y2 = cy + cross,
                    Stroke = axisThinStroke,
                    StrokeThickness = 1
                });

                // Axis labels
                AddOverlayText("X+", canvasWidth - 30, cy - 16, Brushes.LightSteelBlue);
                AddOverlayText("X-", 10, cy - 16, Brushes.LightSteelBlue);
                AddOverlayText("Y+", cx + 8, 8, Brushes.LightSteelBlue);
                AddOverlayText("Y-", cx + 8, canvasHeight - 24, Brushes.LightSteelBlue);
                AddOverlayText("O (0,0)", cx + 6, cy + 6, Brushes.Gold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawFallbackAxes error: {ex.Message}");
            }
        }

        private void DrawPartDimensions(double canvasWidth, double canvasHeight)
        {
            try
            {
                if (_lastXRange > 0 || _lastYRange > 0)
                {
                    AddOverlayText($"Part X Range: {_lastXRange:F1} mm", 8, 8, Brushes.LawnGreen);
                    AddOverlayText($"Part Y Range: {_lastYRange:F1} mm", 8, 26, Brushes.LawnGreen);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawPartDimensions error: {ex.Message}");
            }
        }

        private void DrawFileName(double canvasWidth, double canvasHeight)
        {
            try
            {
                if (!string.IsNullOrEmpty(App.MainController?.LoadedGCodeFile))
                {
                    AddOverlayText(IoPath.GetFileName(App.MainController.LoadedGCodeFile),
                                   8, canvasHeight - 20, Brushes.Silver, 10, FontWeights.Normal, 0.65);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawFileName error: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw current machine position marker on overlay
        /// </summary>
        private void DrawMachinePositionMarker(double canvasWidth, double canvasHeight)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🎯 DrawMachinePositionMarker called: Canvas={canvasWidth:F1}x{canvasHeight:F1}");
                
                if (App.MainController?.MStatus == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ DrawMachinePositionMarker: MStatus is null");
                    return;
                }
                
                if (!_workspaceLimitsLoaded || _workspaceMaxX <= 0 || _workspaceMaxY <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ DrawMachinePositionMarker: Workspace limits not loaded ({_workspaceLimitsLoaded}, {_workspaceMaxX}, {_workspaceMaxY})");
                    return;
                }

                // Get current machine position
                double machineX = App.MainController.MStatus.X;
                double machineY = App.MainController.MStatus.Y;
                System.Diagnostics.Debug.WriteLine($"🎯 Machine position: X={machineX:F2}, Y={machineY:F2}");

                // Create transform using workspace limits
                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ DrawMachinePositionMarker: Failed to create transform");
                    return;
                }

                // Convert machine coordinates to canvas position
                var canvasPos = xf.ToCanvas(machineX, machineY);
                System.Diagnostics.Debug.WriteLine($"🎯 Canvas position: X={canvasPos.X:F1}, Y={canvasPos.Y:F1}");

                // Draw machine position marker (green circle)
                var marker = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Stroke = new SolidColorBrush(Colors.Lime),
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(100, 0, 255, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, canvasPos.X - 8);
                Canvas.SetTop(marker, canvasPos.Y - 8);
                _overlayCanvas.Children.Add(marker);

                // Draw machine position label
                var label = new TextBlock
                {
                    Text = $"M:{machineX:F2},{machineY:F2}",
                    Foreground = new SolidColorBrush(Colors.Lime),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, canvasPos.X + 12);
                Canvas.SetTop(label, canvasPos.Y - 15);
                _overlayCanvas.Children.Add(label);
                
                System.Diagnostics.Debug.WriteLine($"✅ DrawMachinePositionMarker: Marker added to overlay");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawMachinePositionMarker error: {ex.Message}");
            }
        }

        #endregion

        #region Private Utility Methods

        /// <summary>
        /// Add text overlay with advanced styling options
        /// </summary>
        private void AddOverlayText(string text, double x, double y, Brush color,
                                    double fontSize = 12, FontWeight? weight = null, double opacity = 1.0,
                                    Brush background = null)
        {
            try
            {
                if (_overlayCanvas == null) return;
                
                var tb = new TextBlock
                {
                    Text = text,
                    Foreground = color,
                    FontSize = fontSize,
                    FontWeight = weight ?? FontWeights.SemiBold,
                    Opacity = opacity,
                    IsHitTestVisible = false
                };

                if (background != null)
                {
                    tb.Background = background;
                    tb.Padding = new Thickness(2, 0, 2, 0);
                }

                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
                _overlayCanvas.Children.Add(tb);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddOverlayText error: {ex.Message}");
            }
        }

        #endregion

        #region Dynamic Scale Support

        /// <summary>
        /// ✅ SIMPLIFIED: Unified scale sistemi - G-Code scale'i table scale ile aynı olacak
        /// </summary>
        public void UpdateDynamicScale(double newScale, double canvasWidth, double canvasHeight)
        {
            try
            {
                if (!_workspaceLimitsLoaded || _workspaceMaxX <= 0 || _workspaceMaxY <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Cannot update dynamic scale - workspace limits not loaded");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"🔄 UNIFIED SCALE: Using G-Code scale for table: {newScale:F3}");

                // ✅ SIMPLE: G-Code'dan gelen scale'i table için de kullan
                _workspaceScale = newScale;

                // Table boyutlarını yeniden hesapla
                double tablePixW = _workspaceMaxX * _workspaceScale;
                double tablePixH = _workspaceMaxY * _workspaceScale;

                // ✅ CENTER TABLE ON CANVAS
                double tableCenterX = canvasWidth / 2;
                double tableCenterY = canvasHeight / 2;
                
                _workspaceOriginCanvasX = tableCenterX - (tablePixW / 2);
                _workspaceOriginCanvasY = tableCenterY + (tablePixH / 2);

                System.Diagnostics.Debug.WriteLine($"🔄 UNIFIED SCALE UPDATE:");
                System.Diagnostics.Debug.WriteLine($"   • Scale: {_workspaceScale:F3}");
                System.Diagnostics.Debug.WriteLine($"   • Table canvas size: {tablePixW:F1}x{tablePixH:F1}px");
                System.Diagnostics.Debug.WriteLine($"   • Table center: ({tableCenterX:F1},{tableCenterY:F1})");
                System.Diagnostics.Debug.WriteLine($"   • Table origin: ({_workspaceOriginCanvasX:F1},{_workspaceOriginCanvasY:F1})");

                // Overlay'i refresh et
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshOverlay();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateDynamicScale error: {ex.Message}");
            }
        }

        #endregion

        #region Settings Event Management

        /// <summary>
        /// ✅ FIXED: Settings event'ini subscribe et - MainController'ın mevcut event'ini kullan
        /// </summary>
        private void SubscribeToSettingsEvents()
        {
            try
            {
                // ✅ WORKAROUND: MainController'ın PropertyChanged event'ini dinle
                if (App.MainController != null)
                {
                    App.MainController.PropertyChanged += OnMainControllerPropertyChanged;
                    System.Diagnostics.Debug.WriteLine("✅ GCodeOverlayManager: MainController PropertyChanged event subscribed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ GCodeOverlayManager: MainController not available for event subscription");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SubscribeToSettingsEvents error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ FIXED: MainController PropertyChanged event'ini dinle ve Settings değişikliklerini yakala
        /// </summary>
        private void OnMainControllerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                // Settings property'si değiştiğinde workspace limits'i yeniden yükle
                if (e.PropertyName == nameof(App.MainController.Settings))
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 GCodeOverlayManager: Settings property changed, reloading workspace limits");
                    
                    // Workspace limits'i yeniden yükle
                    LoadWorkspaceLimitsFromSettings();
                    
                    if (_workspaceLimitsLoaded)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ GCodeOverlayManager: Workspace limits loaded successfully after Settings change");
                        System.Diagnostics.Debug.WriteLine($"   • X: {_workspaceMaxX:F0}mm, Y: {_workspaceMaxY:F0}mm");
                        
                        // Overlay'i refresh et
                        RefreshOverlay();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ GCodeOverlayManager: Workspace limits still not loaded after Settings change");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnMainControllerPropertyChanged error: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// ✅ FIXED: Dispose pattern ile event subscription'ları temizle
        /// </summary>
        public void Dispose()
        {
            try
            {
                // MainController PropertyChanged event subscription'ını temizle
                if (App.MainController != null)
                {
                    App.MainController.PropertyChanged -= OnMainControllerPropertyChanged;
                    System.Diagnostics.Debug.WriteLine("✅ GCodeOverlayManager: MainController PropertyChanged event unsubscribed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GCodeOverlayManager Dispose error: {ex.Message}");
            }
        }

        #endregion
    }
}