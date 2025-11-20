using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CncControlApp.Helpers;

namespace CncControlApp
{
    /// <summary>
    /// Core rendering methods for G-Code visualization
    /// ✅ OPTIMIZED: Single WorkspaceTransform creation - no duplication
    /// </summary>
    public partial class GCodeVisualization
    {
        private ViewportManager _viewportManager;

        public GCodeVisualization(ViewportManager viewportManager)
        {
            _viewportManager = viewportManager;
        }

        /// <summary>
        /// Main canvas rendering entry point
        /// </summary>
        public void RenderCanvas(ViewportType viewportType, List<GCodeSegment> gcodeSegments)
        {
            // Design mode guard
            try
            {
                if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject()))
                    return;
            }
            catch { }

            System.Diagnostics.Debug.WriteLine($"🎯 RenderCanvas called: viewport={viewportType}, segments={gcodeSegments?.Count ?? 0}");

            var mainCanvas = _viewportManager.GetViewportCanvas(viewportType);
            if (mainCanvas == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ No canvas found for viewport: {viewportType}");
                return;
            }

            mainCanvas.Children.Clear();

            if (viewportType == ViewportType.Top)
            {
                AnalyzeZLevels(gcodeSegments);

                if (gcodeSegments?.Count > 0)
                {
                    bool currentFitState = GetCurrentFitState();
                    UpdateFitPreview(gcodeSegments, currentFitState);
                }

                GetReliableCanvasSize(mainCanvas, (canvasWidth, canvasHeight) =>
                {
                    RenderTopViewOptimized(mainCanvas, null, gcodeSegments, canvasWidth, canvasHeight);
                });
                return;
            }

            GetReliableCanvasSize(mainCanvas, (canvasWidth, canvasHeight) =>
            {
                if (gcodeSegments?.Count > 0)
                {
                    DrawBasicGCodeSegments(mainCanvas, viewportType, gcodeSegments, canvasWidth, canvasHeight);
                }

                EnsureConcurrentOverlayRendering(viewportType, canvasWidth, canvasHeight);
            });
        }

        /// <summary>
        /// ✅ OPTIMIZED: TopView render - creates WorkspaceTransform ONCE and passes to all methods
        /// Eliminates 66% of redundant scale calculations
        /// </summary>
        private Point? RenderTopViewOptimized(Canvas mainCanvas, Canvas overlayCanvas, 
            List<GCodeSegment> gcodeSegments, double canvasWidth, double canvasHeight)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 OPTIMIZED RENDER: Canvas={canvasWidth:F1}x{canvasHeight:F1}");

                // ✅ STEP 1: Create WorkspaceTransform ONCE (single settings read)
                if (!WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
                    System.Diagnostics.Debug.WriteLine($"❌ Cannot render: No valid settings ($130/$131)");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ WorkspaceTransform created: Scale={xf.Scale:F3}, Table={xf.MaxX:F0}x{xf.MaxY:F0}mm");

                // ✅ STEP 2: Update overlay with the SAME transform (no recalculation)
                var overlayManager = GetOverlayManager();
                if (overlayManager != null)
                {
                    overlayManager.UpdateWithTransform(xf);
                    System.Diagnostics.Debug.WriteLine($"✅ Overlay updated with unified transform");
                }

                // ✅ STEP 3: Draw G-CODE using the SAME transform (no recalculation)
                Point? originPosition = null;
                if (gcodeSegments?.Count > 0)
                {
                    originPosition = DrawGCodeWithTransform(mainCanvas, gcodeSegments, xf);
                }

                // ✅ STEP 4: Refresh overlay
                RefreshTopViewOverlay();

                System.Diagnostics.Debug.WriteLine($"✅ OPTIMIZED RENDER complete (Scale={xf.Scale:F3})");
                return originPosition;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RenderTopViewOptimized error: {ex.Message}");
                DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
                return null;
            }
        }

        /// <summary>
        /// ✅ OPTIMIZED: Draw G-Code using provided WorkspaceTransform (no recalculation)
        /// Places G-code origin (0,0) at current machine position
        /// Uses StreamGeometry for 10-50x faster rendering
        /// </summary>
        private Point? DrawGCodeWithTransform(Canvas canvas, List<GCodeSegment> gcodeSegments, 
            WorkspaceTransform xf)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🎨 G-CODE DRAWING: segments={gcodeSegments?.Count ?? 0}");

                if (gcodeSegments == null || gcodeSegments.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No G-Code segments to draw!");
                    return null;
                }

                // Limit segments for performance
                int maxSegments = Math.Min(gcodeSegments.Count, 5000);
                var segmentsToRender = gcodeSegments.Take(maxSegments).ToList();

                // Z range for coloring
                var allZValues = gcodeSegments.SelectMany(s => new[] { s.StartPoint.Z, s.EndPoint.Z }).ToList();
                double minZ = allZValues.Min();
                double maxZ = allZValues.Max();

                // Get current machine position (where spindle is)
                double currentMachineX = App.MainController?.MStatus?.X ?? 0;
                double currentMachineY = App.MainController?.MStatus?.Y ?? 0;

                // Use provided transform to get canvas position (NO RECALCULATION)
                var machineCanvasPt = xf.ToCanvas(currentMachineX, currentMachineY);

                System.Diagnostics.Debug.WriteLine($"✅ POSITIONING:");
                System.Diagnostics.Debug.WriteLine($"   • Machine pos: ({currentMachineX:F1},{currentMachineY:F1})mm");
                System.Diagnostics.Debug.WriteLine($"   • Canvas pos: ({machineCanvasPt.X:F1},{machineCanvasPt.Y:F1})px");
                System.Diagnostics.Debug.WriteLine($"   • Using scale: {xf.Scale:F3}");

                // Use optimized StreamGeometry renderer
                OptimizedGCodeRenderer.DrawGCodeOptimized(canvas, segmentsToRender, xf.Scale, 
                    machineCanvasPt.X, machineCanvasPt.Y, minZ, maxZ);

                System.Diagnostics.Debug.WriteLine($"✅ G-CODE DRAWN: {canvas.Children.Count} canvas children");

                // Draw origin marker at machine position
                DrawOriginMarker(canvas, machineCanvasPt.X, machineCanvasPt.Y);
                
                return machineCanvasPt;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawGCodeWithTransform error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Draw origin marker at G-code (0,0) coordinates (current machine position)
        /// </summary>
        private void DrawOriginMarker(Canvas canvas, double originCanvasX, double originCanvasY)
        {
            try
            {
                double crossSize = 10.0;
                double circleRadius = 6.0;

                // Red circle
                var originCircle = new Ellipse
                {
                    Width = circleRadius * 2,
                    Height = circleRadius * 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 255, 60, 60))
                };
                Canvas.SetLeft(originCircle, originCanvasX - circleRadius);
                Canvas.SetTop(originCircle, originCanvasY - circleRadius);
                canvas.Children.Add(originCircle);

                // Red crosshair - horizontal
                var crossHorizontal = new Line
                {
                    X1 = originCanvasX - crossSize, Y1 = originCanvasY,
                    X2 = originCanvasX + crossSize, Y2 = originCanvasY,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2
                };
                canvas.Children.Add(crossHorizontal);

                // Red crosshair - vertical
                var crossVertical = new Line
                {
                    X1 = originCanvasX, Y1 = originCanvasY - crossSize,
                    X2 = originCanvasX, Y2 = originCanvasY + crossSize,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                    StrokeThickness = 2
                };
                canvas.Children.Add(crossVertical);

                System.Diagnostics.Debug.WriteLine($"✅ ORIGIN MARKER at Canvas({originCanvasX:F1},{originCanvasY:F1})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawOriginMarker error: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw settings error message when table dimensions are not available
        /// </summary>
        private void DrawSettingsErrorMessage(Canvas canvas, double canvasWidth, double canvasHeight)
        {
            try
            {
                canvas.Children.Clear();

                var errorBackground = new Rectangle
                {
                    Width = canvasWidth,
                    Height = canvasHeight,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0))
                };
                canvas.Children.Add(errorBackground);

                var errorTitle = new TextBlock
                {
                    Text = "⚠️ G-Code Cannot Be Displayed",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Red),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(errorTitle, (canvasWidth - 200) / 2);
                Canvas.SetTop(errorTitle, (canvasHeight / 2) - 40);
                canvas.Children.Add(errorTitle);

                var errorMessage = new TextBlock
                {
                    Text = "Machine settings ($130/$131) not found.\nPlease connect to machine and load settings.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.DarkRed),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 250
                };
                Canvas.SetLeft(errorMessage, (canvasWidth - 250) / 2);
                Canvas.SetTop(errorMessage, (canvasHeight / 2) - 10);
                canvas.Children.Add(errorMessage);

                var solutionMessage = new TextBlock
                {
                    Text = "💡 Solution: Connect to machine and retrieve GRBL settings",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(solutionMessage, (canvasWidth - 300) / 2);
                Canvas.SetTop(solutionMessage, (canvasHeight / 2) + 40);
                canvas.Children.Add(solutionMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawSettingsErrorMessage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get canvas size reliably
        /// </summary>
        private void GetReliableCanvasSize(Canvas canvas, Action<double, double> callback)
        {
            canvas.UpdateLayout();
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;

            if (width > 0 && height > 0)
            {
                callback(width, height);
                return;
            }

            if (canvas.Parent is FrameworkElement parent)
            {
                parent.UpdateLayout();
                width = parent.ActualWidth;
                height = parent.ActualHeight;

                if (width > 50 && height > 50)
                {
                    callback(width, height);
                    return;
                }
            }

            UiHelper.RunOnUi(() =>
            {
                canvas.UpdateLayout();
                double finalWidth = canvas.ActualWidth;
                double finalHeight = canvas.ActualHeight;

                if (finalWidth <= 0 || finalHeight <= 0)
                {
                    finalWidth = 800;
                    finalHeight = 600;
                }

                callback(finalWidth, finalHeight);
            }, DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Get overlay manager instance
        /// </summary>
        private dynamic GetOverlayManager()
        {
            try
            {
                var gCodeView = Application.Current.MainWindow?.FindName("GCodeViewControl") as GCodeView;
                return gCodeView?.GetOverlayManager();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get current fit state from GCodeView
        /// </summary>
        private bool GetCurrentFitState()
        {
            try
            {
                var gCodeView = Application.Current.MainWindow?.FindName("GCodeViewControl") as GCodeView;
                if (gCodeView != null)
                {
                    return gCodeView.EnableFitOnRotation;
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCurrentFitState error: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Refresh TopView overlay
        /// </summary>
        private void RefreshTopViewOverlay()
        {
            try
            {
                var overlayManager = GetOverlayManager();
                if (overlayManager != null)
                {
                    overlayManager.RefreshOverlay();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshTopViewOverlay error: {ex.Message}");
            }
        }
    }
}
