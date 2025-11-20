using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CncControlApp.Helpers;

namespace CncControlApp
{
    /// <summary>
    /// Other viewports rendering (Front, Right, Isometric) and overlay management
    /// </summary>
    public partial class GCodeVisualization
    {
        /// <summary>
        /// Initialize static overlays for non-TopView viewports
        /// </summary>
        public void InitializeStaticOverlays()
        {
            try
            {
                InitializeViewportOverlay(ViewportType.Front);
                InitializeViewportOverlay(ViewportType.Right);
                InitializeViewportOverlay(ViewportType.Isometric);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InitializeStaticOverlays error: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh all static overlays
        /// </summary>
        public void RefreshAllStaticOverlays()
        {
            try
            {
                RefreshViewportOverlay(ViewportType.Front);
                RefreshViewportOverlay(ViewportType.Right);
                RefreshViewportOverlay(ViewportType.Isometric);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshAllStaticOverlays error: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize individual viewport overlay
        /// </summary>
        private void InitializeViewportOverlay(ViewportType viewportType)
        {
            try
            {
                var overlayCanvas = GetOverlayCanvas(viewportType);
                if (overlayCanvas != null)
                {
                    overlayCanvas.Children.Clear();
                    DrawStaticViewportOverlay(overlayCanvas, viewportType);
                    EnsureOverlayVisibility(overlayCanvas);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InitializeViewportOverlay error for {viewportType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh individual viewport overlay
        /// </summary>
        private void RefreshViewportOverlay(ViewportType viewportType)
        {
            try
            {
                var overlayCanvas = GetOverlayCanvas(viewportType);
                if (overlayCanvas != null)
                {
                    UiHelper.RunOnUi(() =>
                    {
                        overlayCanvas.Children.Clear();
                        DrawStaticViewportOverlay(overlayCanvas, viewportType);
                        EnsureOverlayVisibility(overlayCanvas);
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshViewportOverlay error for {viewportType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure overlay visibility
        /// </summary>
        private void EnsureOverlayVisibility(Canvas overlayCanvas)
        {
            try
            {
                if (overlayCanvas == null) return;

                overlayCanvas.Visibility = Visibility.Visible;
                overlayCanvas.IsHitTestVisible = false;
                Panel.SetZIndex(overlayCanvas, 1000);
                overlayCanvas.ClipToBounds = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnsureOverlayVisibility error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure concurrent overlay rendering
        /// </summary>
        private void EnsureConcurrentOverlayRendering(ViewportType viewportType, double canvasWidth, double canvasHeight)
        {
            try
            {
                var overlayCanvas = GetOverlayCanvas(viewportType);
                if (overlayCanvas == null) return;

                bool overlayIsEmpty = overlayCanvas.Children.Count == 0;

                if (overlayIsEmpty)
                {
                    DrawStaticViewportOverlay(overlayCanvas, viewportType);
                    EnsureOverlayVisibility(overlayCanvas);
                }
                else
                {
                    EnsureOverlayVisibility(overlayCanvas);
                }

                if (overlayCanvas.ActualWidth != canvasWidth || overlayCanvas.ActualHeight != canvasHeight)
                {
                    overlayCanvas.UpdateLayout();
                    UiHelper.RunOnUi(() =>
                    {
                        overlayCanvas.Children.Clear();
                        DrawStaticViewportOverlay(overlayCanvas, viewportType);
                        EnsureOverlayVisibility(overlayCanvas);
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnsureConcurrentOverlayRendering error for {viewportType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get overlay canvas for viewport type
        /// </summary>
        private Canvas GetOverlayCanvas(ViewportType viewportType)
        {
            try
            {
                string name = null;
                
                switch (viewportType)
                {
                    case ViewportType.Top:
                        name = "TopViewOverlayCanvas";
                        break;
                    case ViewportType.Front:
                        name = "FrontViewOverlayCanvas";
                        break;
                    case ViewportType.Right:
                        name = "RightViewOverlayCanvas";
                        break;
                    case ViewportType.Isometric:
                        name = "IsometricViewOverlayCanvas";
                        break;
                }

                if (name == null) return null;

                var root = Application.Current?.MainWindow;
                if (root == null) return null;

                return FindControlByName<Canvas>(root, name);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Draw basic G-Code segments for non-TopView viewports
        /// </summary>
        private void DrawBasicGCodeSegments(Canvas canvas, ViewportType viewportType, List<GCodeSegment> gcodeSegments, double canvasWidth, double canvasHeight)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0) return;

                var bounds = CalculateGCodeBounds(gcodeSegments);
                var allZValues = gcodeSegments.SelectMany(s => new[] { s.StartPoint.Z, s.EndPoint.Z }).ToList();
                double minZ = allZValues.Min();
                double maxZ = allZValues.Max();

                double dataWidth, dataHeight, scale;

                switch (viewportType)
                {
                    case ViewportType.Front:
                        dataWidth = bounds.MaxX - bounds.MinX;
                        dataHeight = bounds.MaxZ - bounds.MinZ;
                        break;
                    case ViewportType.Right:
                        dataWidth = bounds.MaxY - bounds.MinY;
                        dataHeight = bounds.MaxZ - bounds.MinZ;
                        break;
                    case ViewportType.Isometric:
                        var isoPoints = new List<(double X, double Y)>();
                        foreach (var segment in gcodeSegments.Take(100))
                        {
                            var iso1 = TransformToIsometric(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z);
                            var iso2 = TransformToIsometric(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z);
                            isoPoints.Add(iso1);
                            isoPoints.Add(iso2);
                        }
                        dataWidth = isoPoints.Count > 0 ? isoPoints.Max(p => p.X) - isoPoints.Min(p => p.X) : 100;
                        dataHeight = isoPoints.Count > 0 ? isoPoints.Max(p => p.Y) - isoPoints.Min(p => p.Y) : 100;
                        break;
                    default:
                        dataWidth = dataHeight = 100;
                        break;
                }

                if (dataWidth <= 0) dataWidth = 10;
                if (dataHeight <= 0) dataHeight = 10;

                double margin = 40;
                double availableWidth = canvasWidth - (2 * margin);
                double availableHeight = canvasHeight - (2 * margin);
                double scaleX = availableWidth / dataWidth;
                double scaleY = availableHeight / dataHeight;
                scale = Math.Min(scaleX, scaleY) * 0.8;

                double centerX = canvasWidth / 2;
                double centerY = canvasHeight / 2;

                double dataCenterX = 0, dataCenterY = 0;

                switch (viewportType)
                {
                    case ViewportType.Front:
                        dataCenterX = (bounds.MinX + bounds.MaxX) / 2;
                        dataCenterY = (bounds.MinZ + bounds.MaxZ) / 2;
                        break;
                    case ViewportType.Right:
                        dataCenterX = (bounds.MinY + bounds.MaxY) / 2;
                        dataCenterY = (bounds.MinZ + bounds.MaxZ) / 2;
                        break;
                    case ViewportType.Isometric:
                        dataCenterX = (bounds.MinX + bounds.MaxX) / 2;
                        dataCenterY = (bounds.MinY + bounds.MaxY) / 2;
                        break;
                }

                int maxSegments = Math.Min(gcodeSegments.Count, 2000);
                var segmentsToRender = gcodeSegments.Take(maxSegments).ToList();

                var segmentsByColor = GroupSegmentsByZLevel(segmentsToRender, minZ, maxZ, 20);

                foreach (var colorGroup in segmentsByColor)
                {
                    var geometry = new StreamGeometry();

                    using (StreamGeometryContext ctx = geometry.Open())
                    {
                        foreach (var segment in colorGroup.Value)
                        {
                            double x1, y1, x2, y2;

                            switch (viewportType)
                            {
                                case ViewportType.Front:
                                    x1 = centerX + (segment.StartPoint.X - dataCenterX) * scale;
                                    y1 = centerY - (segment.StartPoint.Z - dataCenterY) * scale;
                                    x2 = centerX + (segment.EndPoint.X - dataCenterX) * scale;
                                    y2 = centerY - (segment.EndPoint.Z - dataCenterY) * scale;
                                    break;
                                case ViewportType.Right:
                                    x1 = centerX + (segment.StartPoint.Y - dataCenterX) * scale;
                                    y1 = centerY - (segment.StartPoint.Z - dataCenterY) * scale;
                                    x2 = centerX + (segment.EndPoint.Y - dataCenterX) * scale;
                                    y2 = centerY - (segment.EndPoint.Z - dataCenterY) * scale;
                                    break;
                                case ViewportType.Isometric:
                                    var iso1 = TransformToIsometric(
                                        segment.StartPoint.X - dataCenterX,
                                        segment.StartPoint.Y - dataCenterY,
                                        segment.StartPoint.Z);
                                    var iso2 = TransformToIsometric(
                                        segment.EndPoint.X - dataCenterX,
                                        segment.EndPoint.Y - dataCenterY,
                                        segment.EndPoint.Z);
                                    x1 = centerX + iso1.X * scale;
                                    y1 = centerY - iso1.Y * scale;
                                    x2 = centerX + iso2.X * scale;
                                    y2 = centerY - iso2.Y * scale;
                                    break;
                                default:
                                    continue;
                            }

                            ctx.BeginFigure(new Point(x1, y1), false, false);
                            ctx.LineTo(new Point(x2, y2), true, false);
                        }
                    }

                    geometry.Freeze();

                    var brush = OptimizedGCodeRenderer.GetCachedBrush(colorGroup.Key);
                    var path = new Path
                    {
                        Data = geometry,
                        Stroke = brush,
                        StrokeThickness = 1.2
                    };

                    canvas.Children.Add(path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawBasicGCodeSegments error: {ex.Message}");
            }
        }

        /// <summary>
        /// Group segments by Z-level color
        /// </summary>
        private Dictionary<Color, List<GCodeSegment>> GroupSegmentsByZLevel(
            List<GCodeSegment> segments, double minZ, double maxZ, int colorLevels)
        {
            var grouped = new Dictionary<Color, List<GCodeSegment>>();

            foreach (var segment in segments)
            {
                double avgZ = (segment.StartPoint.Z + segment.EndPoint.Z) / 2.0;
                Color color = GetColorForZLayer(avgZ, minZ, maxZ, colorLevels);

                if (!grouped.ContainsKey(color))
                    grouped[color] = new List<GCodeSegment>();

                grouped[color].Add(segment);
            }

            return grouped;
        }

        /// <summary>
        /// Transform to isometric projection
        /// </summary>
        private (double X, double Y) TransformToIsometric(double x, double y, double z)
        {
            double isoX = (x - y) * 0.866;
            double isoY = (x + y) * 0.5 + z;
            return (isoX, isoY);
        }

        /// <summary>
        /// Get color for Z layer
        /// </summary>
        private Color GetColorForZLayer(double zValue, double minZ, double maxZ, int totalLayers)
        {
            if (maxZ <= minZ) return Colors.White;

            double normalizedZ = (zValue - minZ) / (maxZ - minZ);
            int level = (int)(normalizedZ * (totalLayers - 1));
            normalizedZ = level / (double)(totalLayers - 1);

            double hue = normalizedZ * 300.0;
            return HSVtoRGB(hue, 0.8, 0.9);
        }

        /// <summary>
        /// Convert HSV to RGB
        /// </summary>
        private Color HSVtoRGB(double hue, double saturation, double value)
        {
            double c = value * saturation;
            double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
            double m = value - c;

            double r = 0, g = 0, b = 0;

            if (hue >= 0 && hue < 60) { r = c; g = x; b = 0; }
            else if (hue >= 60 && hue < 120) { r = x; g = c; b = 0; }
            else if (hue >= 120 && hue < 180) { r = 0; g = c; b = x; }
            else if (hue >= 180 && hue < 240) { r = 0; g = x; b = c; }
            else if (hue >= 240 && hue < 300) { r = x; g = 0; b = c; }
            else if (hue >= 300 && hue < 360) { r = c; g = 0; b = x; }

            byte red = (byte)Math.Round((r + m) * 255);
            byte green = (byte)Math.Round((g + m) * 255);
            byte blue = (byte)Math.Round((b + m) * 255);

            return Color.FromRgb(red, green, blue);
        }

        /// <summary>
        /// Draw static viewport overlay
        /// </summary>
        private void DrawStaticViewportOverlay(Canvas overlayCanvas, ViewportType viewportType)
        {
            try
            {
                double axisOriginX = 30;
                double axisOriginY = 150;
                double axisLength = 40;

                switch (viewportType)
                {
                    case ViewportType.Front:
                        DrawStaticAxisOverlay_Front(overlayCanvas, axisOriginX, axisOriginY, axisLength);
                        break;
                    case ViewportType.Right:
                        DrawStaticAxisOverlay_Right(overlayCanvas, axisOriginX, axisOriginY, axisLength);
                        break;
                    case ViewportType.Isometric:
                        DrawStaticAxisOverlay_Isometric(overlayCanvas, axisOriginX, axisOriginY, axisLength);
                        break;
                }

                DrawStaticViewportLabel(overlayCanvas, viewportType);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawStaticViewportOverlay error: {ex.Message}");
            }
        }

        private void DrawStaticAxisOverlay_Front(Canvas canvas, double originX, double originY, double length)
        {
            // X axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX + length, Y2 = originY,
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 3,
                Opacity = 0.9
            });

            // Z axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX, Y2 = originY - length,
                Stroke = new SolidColorBrush(Colors.Blue),
                StrokeThickness = 3,
                Opacity = 0.9
            });
        }

        private void DrawStaticAxisOverlay_Right(Canvas canvas, double originX, double originY, double length)
        {
            // Y axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX + length, Y2 = originY,
                Stroke = new SolidColorBrush(Colors.Green),
                StrokeThickness = 3,
                Opacity = 0.9
            });

            // Z axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX, Y2 = originY - length,
                Stroke = new SolidColorBrush(Colors.Blue),
                StrokeThickness = 3,
                Opacity = 0.9
            });
        }

        private void DrawStaticAxisOverlay_Isometric(Canvas canvas, double originX, double originY, double length)
        {
            // X axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX + length, Y2 = originY,
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 3,
                Opacity = 0.9
            });

            // Y axis
            double yEndX = originX - length * 0.866;
            double yEndY = originY - length * 0.5;
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = yEndX, Y2 = yEndY,
                Stroke = new SolidColorBrush(Colors.Green),
                StrokeThickness = 3,
                Opacity = 0.9
            });

            // Z axis
            canvas.Children.Add(new Line
            {
                X1 = originX, Y1 = originY,
                X2 = originX, Y2 = originY - length,
                Stroke = new SolidColorBrush(Colors.Blue),
                StrokeThickness = 3,
                Opacity = 0.9
            });
        }

        private void DrawStaticViewportLabel(Canvas canvas, ViewportType viewportType)
        {
            try
            {
                string labelText = "";
                Color labelColor = Colors.LightGray;

                switch (viewportType)
                {
                    case ViewportType.Front:
                        labelText = "FRONT (X-Z)";
                        labelColor = Color.FromRgb(255, 140, 140);
                        break;
                    case ViewportType.Right:
                        labelText = "RIGHT (Y-Z)";
                        labelColor = Color.FromRgb(140, 255, 140);
                        break;
                    case ViewportType.Isometric:
                        labelText = "ISOMETRIC (3D)";
                        labelColor = Color.FromRgb(140, 140, 255);
                        break;
                }

                var viewportLabel = new TextBlock
                {
                    Text = labelText,
                    Foreground = new SolidColorBrush(labelColor),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    Opacity = 0.9
                };

                Canvas.SetRight(viewportLabel, 8);
                Canvas.SetTop(viewportLabel, 8);
                canvas.Children.Add(viewportLabel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawStaticViewportLabel error: {ex.Message}");
            }
        }
    }
}
