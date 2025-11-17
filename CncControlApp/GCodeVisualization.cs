using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CncControlApp.Helpers; // ✅ ADD: Import UiHelper namespace

namespace CncControlApp
{
    /// <summary>
    /// Handles G-code visualization and rendering with unified scale system
    /// ✅ UNIFIED: Canvas-centered scale system for both table and G-Code
    /// ✅ LIVE FIT: Mathematical fit checking without redraw
    /// </summary>
    public class GCodeVisualization
    {
        private ViewportManager _viewportManager;

        // ✅ SINGLE UNIFIED SCALE: Table'dan gelen tek ölçek - her yerden erişilebilir
        private double _unifiedScale = 1.0;

        public GCodeVisualization(ViewportManager viewportManager)
        {
            _viewportManager = viewportManager;
        }

        #region Mathematical Fit Checking - NO REDRAW REQUIRED

        /// <summary>
        /// ✅ NEW: Check if G-Code fits within table bounds mathematically
        /// </summary>
        public (bool Fits, string Reason, double RequiredX, double RequiredY, double AvailableX, double AvailableY)
            CheckGCodeFitsInTable(List<GCodeSegment> gcodeSegments)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0)
                {
                    return (true, "No G-Code to check", 0, 0, 0, 0);
                }

                // ✅ 1. GET TABLE DIMENSIONS from settings
                var tableDimensions = GetTableDimensionsFromSettings();
                if (!tableDimensions.HasValue)
                {
                    return (false, "Table dimensions not available ($130/$131 settings missing)", 0, 0, 0, 0);
                }

                double tableMaxX = tableDimensions.Value.MaxX;
                double tableMaxY = tableDimensions.Value.MaxY;

                // ✅ 2. CALCULATE G-CODE BOUNDS
                var gcodeBounds = CalculateGCodeBounds(gcodeSegments);

                // ✅ 3. CALCULATE REQUIRED DIMENSIONS
                double gcodeWidth = gcodeBounds.MaxX - gcodeBounds.MinX;
                double gcodeHeight = gcodeBounds.MaxY - gcodeBounds.MinY;

                System.Diagnostics.Debug.WriteLine($"🔍 FIT CHECK:");
                System.Diagnostics.Debug.WriteLine($"   • Table: {tableMaxX:F1} x {tableMaxY:F1} mm");
                System.Diagnostics.Debug.WriteLine($"   • G-Code bounds: X:[{gcodeBounds.MinX:F1},{gcodeBounds.MaxX:F1}] Y:[{gcodeBounds.MinY:F1},{gcodeBounds.MaxY:F1}]");
                System.Diagnostics.Debug.WriteLine($"   • G-Code dimensions: {gcodeWidth:F1} x {gcodeHeight:F1} mm");

                // ✅ 4. CHECK IF PART FITS WHEN CENTERED
                // Parça merkeze yerleştirildiğinde tabla sınırları içinde kalıyor mu?

                // Merkezden en uzak noktaların tabla sınırlarını aşıp aşmadığını kontrol et
                double centerX = tableMaxX / 2.0;
                double centerY = tableMaxY / 2.0;

                // G-Code'un geometrik merkezi
                double gcodeCenterX = (gcodeBounds.MinX + gcodeBounds.MaxX) / 2.0;
                double gcodeCenterY = (gcodeBounds.MinY + gcodeBounds.MaxY) / 2.0;

                // G-Code merkez tabla merkezine yerleştirildiğinde yeni bounds
                double offsetX = centerX - gcodeCenterX;
                double offsetY = centerY - gcodeCenterY;

                double newMinX = gcodeBounds.MinX + offsetX;
                double newMaxX = gcodeBounds.MaxX + offsetX;
                double newMinY = gcodeBounds.MinY + offsetY;
                double newMaxY = gcodeBounds.MaxY + offsetY;

                System.Diagnostics.Debug.WriteLine($"   • Centered bounds: X:[{newMinX:F1},{newMaxX:F1}] Y:[{newMinY:F1},{newMaxY:F1}]");

                // ✅ 5. FIT CONTROL
                bool fitsX = newMinX >= 0 && newMaxX <= tableMaxX;
                bool fitsY = newMinY >= 0 && newMaxY <= tableMaxY;
                bool fits = fitsX && fitsY;

                string reason;
                if (fits)
                {
                    reason = "G-Code fits within table bounds when centered";
                }
                else
                {
                    var problems = new List<string>();
                    if (!fitsX)
                    {
                        if (newMinX < 0) problems.Add($"X extends {Math.Abs(newMinX):F1}mm beyond left edge");
                        if (newMaxX > tableMaxX) problems.Add($"X extends {(newMaxX - tableMaxX):F1}mm beyond right edge");
                    }
                    if (!fitsY)
                    {
                        if (newMinY < 0) problems.Add($"Y extends {Math.Abs(newMinY):F1}mm beyond front edge");
                        if (newMaxY > tableMaxY) problems.Add($"Y extends {(newMaxY - tableMaxY):F1}mm beyond back edge");
                    }
                    reason = $"G-Code exceeds table bounds: {string.Join(", ", problems)}";
                }

                System.Diagnostics.Debug.WriteLine($"   • Fit result: {fits} - {reason}");

                return (fits, reason, gcodeWidth, gcodeHeight, tableMaxX, tableMaxY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CheckGCodeFitsInTable error: {ex.Message}");
                return (false, $"Error checking fit: {ex.Message}", 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// ✅ NEW: Check if G-Code requires fit (extends beyond positive quadrant)
        /// </summary>
        public bool DoesGCodeRequireFit(List<GCodeSegment> gcodeSegments)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0)
                    return false;

                var bounds = CalculateGCodeBounds(gcodeSegments);

                // Eğer minimum X veya Y değeri negatifse fit gerekiyor
                bool requiresFit = bounds.MinX < 0 || bounds.MinY < 0;

                System.Diagnostics.Debug.WriteLine($"🔍 FIT REQUIREMENT CHECK:");
                System.Diagnostics.Debug.WriteLine($"   • Bounds: X:[{bounds.MinX:F1},{bounds.MaxX:F1}] Y:[{bounds.MinY:F1},{bounds.MaxY:F1}]");
                System.Diagnostics.Debug.WriteLine($"   • Requires fit: {requiresFit} (negative coordinates present)");

                return requiresFit;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DoesGCodeRequireFit error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ NEW: Get fit preview text for UI
        /// </summary>
        public string GetFitPreviewText(List<GCodeSegment> gcodeSegments, bool enableFit)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0)
                    return "FIT: —";

                var fitCheck = CheckGCodeFitsInTable(gcodeSegments);
                var requiresFit = DoesGCodeRequireFit(gcodeSegments);

                if (enableFit)
                {
                    if (fitCheck.Fits)
                    {
                        return $"FIT: ✅ {fitCheck.RequiredX:F0}×{fitCheck.RequiredY:F0}mm";
                    }
                    else
                    {
                        return $"FIT: ❌ {fitCheck.RequiredX:F0}×{fitCheck.RequiredY:F0}mm > {fitCheck.AvailableX:F0}×{fitCheck.AvailableY:F0}mm";
                    }
                }
                else
                {
                    if (requiresFit)
                    {
                        return "FIT: ⚠️ Negative coords";
                    }
                    else if (fitCheck.Fits)
                    {
                        return "FIT: ✅ No fit needed";
                    }
                    else
                    {
                        return "FIT: ❌ Exceeds table";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetFitPreviewText error: {ex.Message}");
                return "FIT: Error";
            }
        }

        /// <summary>
        /// ✅ NEW: Update fit preview in UI
        /// </summary>
        public void UpdateFitPreview(List<GCodeSegment> gcodeSegments, bool enableFit)
        {
            try
            {
       // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.BeginInvoke
           UiHelper.RunOnUi(() =>
      {
 try
   {
           var fitPreviewTextBlock = FindControlByName<TextBlock>(Application.Current.MainWindow, "RotationFitPreviewTextBlock");

       if (fitPreviewTextBlock != null)
       {
         string previewText = GetFitPreviewText(gcodeSegments, enableFit);
           fitPreviewTextBlock.Text = previewText;

       // Renk kodlaması
   if (previewText.Contains("✅"))
             {
     fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(52, 199, 89)); // Yeşil
         }
        else if (previewText.Contains("❌"))
    {
           fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)); // Kırmızı
     }
    else if (previewText.Contains("⚠️"))
           {
     fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Turuncu
           }
           else
 {
      fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)); // Gri
          }

  System.Diagnostics.Debug.WriteLine($"✅ Fit preview updated: {previewText}");
                }
           }
        catch (Exception ex)
          {
   System.Diagnostics.Debug.WriteLine($"❌ UpdateFitPreview inner error: {ex.Message}");
    }
           }, DispatcherPriority.Background);
}
        catch (Exception ex)
        {
 System.Diagnostics.Debug.WriteLine($"❌ UpdateFitPreview error: {ex.Message}");
     }
        }

        #endregion

        #region Helper Methods for Fit Checking

        /// <summary>
        /// ✅ NEW: Get table dimensions from GRBL settings
        /// </summary>
        private (double MaxX, double MaxY)? GetTableDimensionsFromSettings()
        {
            try
            {
                if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)
                {
                    var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                    var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);

                    if (xLimit != null && yLimit != null)
                    {
                        if (double.TryParse(xLimit.Value, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out double tableMaxX) &&
                            double.TryParse(yLimit.Value, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out double tableMaxY))
                        {
                            if (tableMaxX > 0 && tableMaxY > 0)
                            {
                                return (tableMaxX, tableMaxY);
                            }
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetTableDimensionsFromSettings error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ✅ NEW: Calculate G-Code bounds
        /// </summary>
        private (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ)
            CalculateGCodeBounds(List<GCodeSegment> gcodeSegments)
        {
            if (gcodeSegments == null || gcodeSegments.Count == 0)
            {
                return (0, 0, 0, 0, 0, 0);
            }

            double minX = gcodeSegments.Min(s => Math.Min(s.StartPoint.X, s.EndPoint.X));
            double maxX = gcodeSegments.Max(s => Math.Max(s.StartPoint.X, s.EndPoint.X));
            double minY = gcodeSegments.Min(s => Math.Min(s.StartPoint.Y, s.EndPoint.Y));
            double maxY = gcodeSegments.Max(s => Math.Max(s.StartPoint.Y, s.EndPoint.Y));
            double minZ = gcodeSegments.Min(s => Math.Min(s.StartPoint.Z, s.EndPoint.Z));
            double maxZ = gcodeSegments.Max(s => Math.Max(s.StartPoint.Z, s.EndPoint.Z));

            return (minX, maxX, minY, maxY, minZ, maxZ);
        }

        #endregion

        #region Original Visualization Methods

        /// <summary>
        /// ✅ DIRECT SETTINGS: Settings'den direkt table boyutlarını al ve scale hesapla
        /// </summary>
        private bool GetUnifiedScaleFromSettings(double canvasWidth, double canvasHeight)
        {
            try
            {
                // ✅ DIRECT ACCESS: MainController.Settings'den direkt al
                if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)
                {
                    // $130 ve $131 settings'lerini ara
                    var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                    var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);

                    if (xLimit != null && yLimit != null)
                    {
                        if (double.TryParse(xLimit.Value, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out double tableMaxX) &&
                            double.TryParse(yLimit.Value, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out double tableMaxY))
                        {
                            if (tableMaxX > 0 && tableMaxY > 0)
                            {
                                double scaleX = canvasWidth / tableMaxX;
                                double scaleY = canvasHeight / tableMaxY;
                                _unifiedScale = Math.Min(scaleX, scaleY) * 0.9;

                                System.Diagnostics.Debug.WriteLine($"✅ DIRECT SETTINGS SCALE: {_unifiedScale:F3}");
                                System.Diagnostics.Debug.WriteLine($"   • Table: {tableMaxX:F0}x{tableMaxY:F0}mm (from $130/$131)");
                                System.Diagnostics.Debug.WriteLine($"   • Canvas: {canvasWidth:F1}x{canvasHeight:F1}px");
                                System.Diagnostics.Debug.WriteLine($"   • Scale: {_unifiedScale:F3}");
                                return true; // ✅ Settings başarıyla alındı
                            }
                        }
                    }
                }

                // ❌ SETTINGS YOK: Çizim yapılamaz
                System.Diagnostics.Debug.WriteLine($"❌ NO VALID SETTINGS: Cannot draw G-Code without table dimensions");
                return false; // ❌ Settings alınamadı
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetUnifiedScaleFromSettings error: {ex.Message}");
                return false; // ❌ Hata durumunda çizim yapma
            }
        }

        /// <summary>
        /// ✅ HELPER: Overlay manager'a güvenli erişim
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
        /// ✅ NEW: Get current fit state from GCodeView
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
                return true; // Default to enabled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCurrentFitState error: {ex.Message}");
                return true; // Default to enabled
            }
        }

        /// <summary>
        /// ✅ SIMPLE: G-Code çizimi - G-code origin (0,0) placed at current machine position
        /// Returns the canvas position of G-code origin (0,0)
        /// ✅ OPTIMIZED: Now uses StreamGeometry for 10-50x faster rendering
        /// </summary>
        private Point? DrawGCodeCentered(Canvas canvas, List<GCodeSegment> gcodeSegments, double scale, double canvasWidth, double canvasHeight)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🎨 G-CODE DRAWING: segments={gcodeSegments?.Count ?? 0}, scale={scale:F3}");

                if (gcodeSegments == null || gcodeSegments.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No G-Code segments to draw!");
                    return null;
                }

                // Use unified WorkspaceTransform for THIS canvas
                if (!Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot create WorkspaceTransform for drawing");
                    return null;
                }

                // Segment sayısını sınırla
                int maxSegments = Math.Min(gcodeSegments.Count, 5000);
                var segmentsToRender = gcodeSegments.Take(maxSegments).ToList();

                // Z değerlerini analiz et (renklendirme için)
                var allZValues = gcodeSegments.SelectMany(s => new[] { s.StartPoint.Z, s.EndPoint.Z }).ToList();
                double minZ = allZValues.Min();
                double maxZ = allZValues.Max();

                // ✅ GET CURRENT MACHINE POSITION (where spindle is)
                double currentMachineX = 0;
                double currentMachineY = 0;
                if (App.MainController?.MStatus != null)
                {
                    currentMachineX = App.MainController.MStatus.X; // Machine position
                    currentMachineY = App.MainController.MStatus.Y;
                }

                // ✅ CANVAS POSITION OF CURRENT MACHINE POSITION using unified transform
                var machineCanvasPt = xf.ToCanvas(currentMachineX, currentMachineY);
                double machineCanvasX = machineCanvasPt.X;
                double machineCanvasY = machineCanvasPt.Y;

                System.Diagnostics.Debug.WriteLine($"✅ POSITIONING:");
                System.Diagnostics.Debug.WriteLine($"   • Current machine pos: ({currentMachineX:F1},{currentMachineY:F1})");
                System.Diagnostics.Debug.WriteLine($"   • Machine canvas pos: ({machineCanvasX:F1},{machineCanvasY:F1})");
                System.Diagnostics.Debug.WriteLine($"   • G-code origin (0,0) will be at machine position");
                System.Diagnostics.Debug.WriteLine($"   • Scale: {xf.Scale:F3}");

                // ✅ OPTIMIZED: Use StreamGeometry-based rendering instead of individual Line objects
                OptimizedGCodeRenderer.DrawGCodeOptimized(canvas, segmentsToRender, xf.Scale, 
                    machineCanvasX, machineCanvasY, minZ, maxZ);

                System.Diagnostics.Debug.WriteLine($"✅ G-CODE DRAWN: canvas children: {canvas.Children.Count}");

                // ✅ DRAW ORIGIN MARKER: Red crosshair at G-code origin (0,0) = current machine position
                DrawOriginMarker(canvas, machineCanvasX, machineCanvasY);
                
                // Return the canvas position of G-code origin for rotation
                return new Point(machineCanvasX, machineCanvasY);

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawGCodeCentered error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ✅ Draw origin marker at G-code (0,0) coordinates (current machine position)
        /// </summary>
        private void DrawOriginMarker(Canvas canvas, double originCanvasX, double originCanvasY)
        {
            try
            {
                // Marker size
                double crossSize = 10.0;
                double circleRadius = 6.0;

                // Red circle (outer boundary)
                var originCircle = new Ellipse
                {
                    Width = circleRadius * 2,
                    Height = circleRadius * 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)), // Bright red
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 255, 60, 60)) // Semi-transparent red fill
                };
                Canvas.SetLeft(originCircle, originCanvasX - circleRadius);
                Canvas.SetTop(originCircle, originCanvasY - circleRadius);
                canvas.Children.Add(originCircle);

                // Red crosshair - horizontal line
                var crossHorizontal = new Line
                {
                    X1 = originCanvasX - crossSize,
                    Y1 = originCanvasY,
                    X2 = originCanvasX + crossSize,
                    Y2 = originCanvasY,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)), // Bright red
                    StrokeThickness = 2
                };
                canvas.Children.Add(crossHorizontal);

                // Red crosshair - vertical line
                var crossVertical = new Line
                {
                    X1 = originCanvasX,
                    Y1 = originCanvasY - crossSize,
                    X2 = originCanvasX,
                    Y2 = originCanvasY + crossSize,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 60, 60)), // Bright red
                    StrokeThickness = 2
                };
                canvas.Children.Add(crossVertical);

                System.Diagnostics.Debug.WriteLine($"✅ ORIGIN MARKER: Red crosshair at machine position → Canvas({originCanvasX:F1},{originCanvasY:F1})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DrawOriginMarker error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ SIMPLE: G-Code geometrik merkezini hesapla
        /// </summary>
        private (double X, double Y) CalculateGCodeGeometricCenter(List<GCodeSegment> gcodeSegments)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0)
                    return (0, 0);

                // G-Code bounds hesapla
                double minX = gcodeSegments.Min(s => Math.Min(s.StartPoint.X, s.EndPoint.X));
                double maxX = gcodeSegments.Max(s => Math.Max(s.StartPoint.X, s.EndPoint.X));
                double minY = gcodeSegments.Min(s => Math.Min(s.StartPoint.Y, s.EndPoint.Y));
                double maxY = gcodeSegments.Max(s => Math.Max(s.StartPoint.Y, s.EndPoint.Y));

                // Geometrik orta = bounding box'ın merkezi
                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                System.Diagnostics.Debug.WriteLine($"🔍 G-Code bounds: X:[{minX:F1},{maxX:F1}] Y:[{minY:F1},{maxY:F1}]");
                System.Diagnostics.Debug.WriteLine($"🔍 G-Code geometric center: ({centerX:F1},{centerY:F1})");

                return (centerX, centerY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CalculateGCodeGeometricCenter error: {ex.Message}");
                return (0, 0);
            }
        }

        /// <summary>
        /// TopView overlay'ini refresh et
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

        /// <summary>
        /// Z seviyesi analizi yap ve UI'ı güncelle
        /// </summary>
        public void AnalyzeZLevels(List<GCodeSegment> gcodeSegments)
        {
            if (gcodeSegments == null || gcodeSegments.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Z Analysis: No segments");
                UpdateZInfoInUI(0, 0, 0, 0);
                return;
            }

            // Tüm Z değerlerini topla
            var allZValues = new List<double>();
            foreach (var segment in gcodeSegments)
            {
                allZValues.Add(segment.StartPoint.Z);
                allZValues.Add(segment.EndPoint.Z);
            }

            // Z analizi
            double minZ = allZValues.Min();
            double maxZ = allZValues.Max();
            double zRange = maxZ - minZ;
            double totalZHeight = Math.Abs(zRange);

            // Benzersiz Z seviyeleri (0.001 hassasiyetle)
            var uniqueZLevels = allZValues
                .Select(z => Math.Round(z, 3))
                .Distinct()
                .OrderBy(z => z)
                .ToList();

            // Debug çıktısı
            System.Diagnostics.Debug.WriteLine($"=== Z Level Analysis ===");
            System.Diagnostics.Debug.WriteLine($"Total Z Range: {minZ:F3} to {maxZ:F3} (Range: {zRange:F3})");
            System.Diagnostics.Debug.WriteLine($"Total Z Height: {totalZHeight:F3}mm");
            System.Diagnostics.Debug.WriteLine($"Unique Z Levels: {uniqueZLevels.Count}");
            System.Diagnostics.Debug.WriteLine($"Z Levels: {string.Join(", ", uniqueZLevels.Select(z => z.ToString("F3")))}");
            System.Diagnostics.Debug.WriteLine($"Total Segments: {gcodeSegments.Count}");

            // UI'ı güncelle
            UpdateZInfoInUI(uniqueZLevels.Count, totalZHeight, minZ, maxZ);
        }

        /// <summary>
        /// Z bilgilerini UI'da güncelle
        /// </summary>
        private void UpdateZInfoInUI(int layerCount, double totalHeight, double minZ, double maxZ)
        {
   try
       {
  // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.Invoke
        UiHelper.RunOnUi(() =>
     {
     var mainWindow = Application.Current.MainWindow;

// Z Layers TextBlock'u güncelle
        var zLayersTextBlock = FindControlByName<TextBlock>(mainWindow, "ZLayersTextBlock");
   if (zLayersTextBlock != null)
   {
  zLayersTextBlock.Text = layerCount.ToString();
    }

  // Z Height TextBlock'u güncelle
         var zHeightTextBlock = FindControlByName<TextBlock>(mainWindow, "ZHeightTextBlock");
 if (zHeightTextBlock != null)
              {
     zHeightTextBlock.Text = $"{totalHeight:F3}mm";
    }

// Z Range TextBlock'u güncelle
    var zRangeTextBlock = FindControlByName<TextBlock>(mainWindow, "ZRangeTextBlock");
    if (zRangeTextBlock != null)
     {
   zRangeTextBlock.Text = $"{minZ:F3} ↔ {maxZ:F3}";
          }

   System.Diagnostics.Debug.WriteLine($"UI Updated: Layers={layerCount}, Height={totalHeight:F3}mm, Range={minZ:F3}↔{maxZ:F3}");
        }, DispatcherPriority.Send);
    }
      catch (Exception ex)
            {
 System.Diagnostics.Debug.WriteLine($"UI Update Error: {ex.Message}");
         }
        }

        /// <summary>
        /// ✅ SIMPLE: Canvas çizimi
        /// </summary>
        public void RenderCanvas(ViewportType viewportType, List<GCodeSegment> gcodeSegments)
        {
            // DESIGN MODE GUARD: avoid rendering in designer to prevent collection modification exceptions
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

            System.Diagnostics.Debug.WriteLine($"✅ Canvas found for {viewportType}: {mainCanvas.Name}");

            mainCanvas.Children.Clear();
            System.Diagnostics.Debug.WriteLine($"🧹 Main canvas cleared, children count: {mainCanvas.Children.Count}");

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
                    System.Diagnostics.Debug.WriteLine($"📐 Top view canvas size: {canvasWidth:F1}x{canvasHeight:F1}");
                    RenderTopViewSimple(mainCanvas, null, gcodeSegments, canvasWidth, canvasHeight);
                });
                return;
            }

            GetReliableCanvasSize(mainCanvas, (canvasWidth, canvasHeight) =>
            {
                System.Diagnostics.Debug.WriteLine($"📐 {viewportType} canvas size: {canvasWidth:F1}x{canvasHeight:F1}");

                if (gcodeSegments?.Count > 0)
                {
                    DrawBasicGCodeSegments(mainCanvas, viewportType, gcodeSegments, canvasWidth, canvasHeight);
                }

                EnsureConcurrentOverlayRendering(viewportType, canvasWidth, canvasHeight);
            });

            System.Diagnostics.Debug.WriteLine($"{viewportType} viewport: drew {gcodeSegments?.Count ?? 0} segments with concurrent overlay rendering");
        }

        /// <summary>
        /// ✅ NEW: Ensure concurrent overlay rendering for non-TopView viewports
        /// </summary>
        private void EnsureConcurrentOverlayRendering(ViewportType viewportType, double canvasWidth, double canvasHeight)
        {
            try
            {
                var overlayCanvas = GetOverlayCanvas(viewportType);
                if (overlayCanvas == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ {viewportType} overlay canvas not found - cannot ensure concurrent rendering");
                    return;
                }

                // ✅ CONCURRENT CHECK: Overlay'nin çizili olup olmadığını kontrol et
                bool overlayIsEmpty = overlayCanvas.Children.Count == 0;
                
                if (overlayIsEmpty)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 {viewportType} overlay is empty, re-initializing...");
                    
                    // Re-initialize the overlay if it's empty
                    DrawStaticViewportOverlay(overlayCanvas, viewportType);
                    EnsureOverlayVisibility(overlayCanvas);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ {viewportType} overlay is present with {overlayCanvas.Children.Count} elements");
                    
                    // Ensure visibility is maintained
                    EnsureOverlayVisibility(overlayCanvas);
                }

                // ✅ ADAPTIVE SIZE: Canvas boyutu değiştiyse overlay'i güncelle
                if (overlayCanvas.ActualWidth != canvasWidth || overlayCanvas.ActualHeight != canvasHeight)
                {
   System.Diagnostics.Debug.WriteLine($"🔄 {viewportType} overlay size mismatch, refreshing...");
            
      // Force update layout and redraw if size changed
     overlayCanvas.UpdateLayout();
         // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.BeginInvoke
  UiHelper.RunOnUi(() =>
       {
   overlayCanvas.Children.Clear();
      DrawStaticViewportOverlay(overlayCanvas, viewportType);
         EnsureOverlayVisibility(overlayCanvas);
}, DispatcherPriority.Render);
 }

           System.Diagnostics.Debug.WriteLine($"✅ {viewportType} concurrent overlay rendering ensured");
   }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ EnsureConcurrentOverlayRendering error for {viewportType}: {ex.Message}");
        }
    }

    /// <summary>
    /// ✅ NEW: Initialize and ensure concurrent rendering of static overlay elements for non-TopView viewports
    /// Bu method canvas oluşturulduğunda bir kez çağrılır ve overlay'ler G-Code ile eşzamanlı çizilir
    /// </summary>
    public void InitializeStaticOverlays()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("🎯 Initializing static overlays for Front, Right, Isometric viewports");

            // ✅ ENHANCED: Initialize and ensure persistent rendering for each viewport
            InitializeViewportOverlay(ViewportType.Front);
            InitializeViewportOverlay(ViewportType.Right);  
            InitializeViewportOverlay(ViewportType.Isometric);

            System.Diagnostics.Debug.WriteLine("✅ All static viewport overlays initialized and configured for concurrent rendering");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ InitializeStaticOverlays error: {ex.Message}");
        }
    }

    /// <summary>
    /// ✅ NEW: Initialize individual viewport overlay with proper setup
    /// </summary>
    private void InitializeViewportOverlay(ViewportType viewportType)
    {
        try
        {
            var overlayCanvas = GetOverlayCanvas(viewportType);
            if (overlayCanvas != null)
            {
                // Clear any existing overlays
                overlayCanvas.Children.Clear();
                
                // Draw static overlay elements
                DrawStaticViewportOverlay(overlayCanvas, viewportType);
                
                // ✅ CRITICAL: Ensure overlay canvas is always on top and visible
                EnsureOverlayVisibility(overlayCanvas);
                
                System.Diagnostics.Debug.WriteLine($"✅ {viewportType} viewport static overlay initialized and configured");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ {viewportType} overlay canvas not found - overlay will not be visible");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ InitializeViewportOverlay error for {viewportType}: {ex.Message}");
        }
    }

    /// <summary>
    /// ✅ NEW: Ensure overlay canvas is properly visible and on top
    /// </summary>
    private void EnsureOverlayVisibility(Canvas overlayCanvas)
    {
        try
        {
            if (overlayCanvas == null) return;

                // Ensure the overlay is visible and on top
                overlayCanvas.Visibility = Visibility.Visible;
                overlayCanvas.IsHitTestVisible = false; // Allow mouse events to pass through
                
                // Set high Z-index to ensure it's always on top
                Panel.SetZIndex(overlayCanvas, 1000);
                
                // Ensure it covers the full area
                overlayCanvas.ClipToBounds = false;
                
                System.Diagnostics.Debug.WriteLine($"✅ Overlay canvas visibility ensured: Z-Index=1000, Visible=True");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnsureOverlayVisibility error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Force refresh all static overlays - call when needed
        /// </summary>
        public void RefreshAllStaticOverlays()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Refreshing all static overlays");
                
                RefreshViewportOverlay(ViewportType.Front);
                RefreshViewportOverlay(ViewportType.Right);
                RefreshViewportOverlay(ViewportType.Isometric);
                
                System.Diagnostics.Debug.WriteLine("✅ All static overlays refreshed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshAllStaticOverlays error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Refresh individual viewport overlay
        /// </summary>
        private void RefreshViewportOverlay(ViewportType viewportType)
        {
       try
  {
    var overlayCanvas = GetOverlayCanvas(viewportType);
   if (overlayCanvas != null)
        {
      // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.BeginInvoke
   UiHelper.RunOnUi(() =>
          {
   overlayCanvas.Children.Clear();
     DrawStaticViewportOverlay(overlayCanvas, viewportType);
   EnsureOverlayVisibility(overlayCanvas);
       
  System.Diagnostics.Debug.WriteLine($"✅ {viewportType} overlay refreshed with {overlayCanvas.Children.Count} elements");
       }, DispatcherPriority.Render);
  }
   }
   catch (Exception ex)
   {
  System.Diagnostics.Debug.WriteLine($"❌ RefreshViewportOverlay error for {viewportType}: {ex.Message}");
   }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// UI control'ü isimle bul
        /// </summary>
        private T FindControlByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T control && control.Name == name)
                    return control;

                var result = FindControlByName<T>(child, name);
                if (result != null) return result;
            }

            return null;
        }

        /// <summary>
        /// CNC dostu izometrik projeksiyon transformasyonu
        /// </summary>
        private (double X, double Y) TransformToIsometric(double x, double y, double z)
        {
            // CNC koordinat sistemi: Z yukarı, Y ileri, X sağa
            // İzometrik projeksiyon: 30° rotasyon
            double isoX = (x - y) * 0.866; // cos(30°) = 0.866
            double isoY = (x + y) * 0.5 + z;

            return (isoX, isoY);
        }

        /// <summary>
        /// Z katmanına göre renk hesapla - HSV renk uzayıında
        /// </summary>
        private Color GetColorForZLayer(double zValue, double minZ, double maxZ, int totalLayers)
        {
            if (maxZ <= minZ) return Colors.White;

            // Z değerini 0-1 aralığına normalize et
            double normalizedZ = (zValue - minZ) / (maxZ - minZ);

            // HSV renk uzayında renk üret
            // Hue: 0° (kırmızı) -> 300° (magenta) aralığında
            double hue = normalizedZ * 300.0; // 0-300 derece
            double saturation = 0.8; // %80 doygunluk
            double value = 0.9; // %90 parlaklık

            return HSVtoRGB(hue, saturation, value);
        }

        /// <summary>
        /// HSV'den RGB'ye dönüştür
        /// </summary>
        private Color HSVtoRGB(double hue, double saturation, double value)
        {
            double c = value * saturation;
            double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
            double m = value - c;

            double r = 0, g = 0, b = 0;

            if (hue >= 0 && hue < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (hue >= 60 && hue < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (hue >= 120 && hue < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (hue >= 180 && hue < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (hue >= 240 && hue < 300)
            {
                r = x; g = 0; b = c;
            }
            else if (hue >= 300 && hue < 360)
            {
                r = c; g = 0; b = x;
            }

            byte red = (byte)Math.Round((r + m) * 255);
            byte green = (byte)Math.Round((g + m) * 255);
            byte blue = (byte)Math.Round((b + m) * 255);

            return Color.FromRgb(red, green, blue);
        }

        /// <summary>
        /// Overlay canvas'ı viewport türüne göre bul
        /// </summary>
        private Canvas GetOverlayCanvas(ViewportType viewportType)
        {
            try
            {
                string name = null;
                switch (viewportType)
                {
                    case ViewportType.Top: name = "TopViewOverlayCanvas"; break;
                    case ViewportType.Front: name = "FrontViewOverlayCanvas"; break;
                    case ViewportType.Right: name = "RightViewOverlayCanvas"; break;
                    case ViewportType.Isometric: name = "IsometricViewOverlayCanvas"; break;
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
/// ✅ SIMPLE: Canvas boyutunu güvenilir şekilde al - MOVED FROM MISSING
/// </summary>
private void GetReliableCanvasSize(Canvas canvas, Action<double, double> callback)
{
    // Hemen kontrol et
    canvas.UpdateLayout();
    double width = canvas.ActualWidth;
    double height = canvas.ActualHeight;

    if (width > 0 && height > 0)
    {
        // Canvas hazır, direkt çağır
        callback(width, height);
        return;
    }

    // Canvas henüz hazır değil, parent boyutlarını dene
    if (canvas.Parent is FrameworkElement parent)
    {
        parent.UpdateLayout();
        width = parent.ActualWidth;
        height = parent.ActualHeight;

        if (width > 50 && height > 50) // Minimum geçerli boyut
        {
            callback(width, height);
            return;
        }
    }

    // Hala geçerli boyut yok, async bekle
    // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.BeginInvoke
    UiHelper.RunOnUi(() =>
    {
        canvas.UpdateLayout();
double finalWidth = canvas.ActualWidth;
   double finalHeight = canvas.ActualHeight;

   // Son kontrol - hala geçersizse fallback
 if (finalWidth <= 0 || finalHeight <= 0)
   {
  finalWidth = 800;
   finalHeight = 600;
    System.Diagnostics.Debug.WriteLine($"⚠️ Using fallback canvas size: {finalWidth}x{finalHeight}");
        }

      callback(finalWidth, finalHeight);
  }, DispatcherPriority.Loaded);
}

/// <summary>
/// ✅ SETTINGS CHECK: TopView render - settings kontrolü ile - MOVED FROM MISSING
/// Returns the canvas position of G-code origin (0,0)
/// </summary>
private Point? RenderTopViewSimple(Canvas mainCanvas, Canvas overlayCanvas, List<GCodeSegment> gcodeSegments, double canvasWidth, double canvasHeight)
{
    try
    {
        System.Diagnostics.Debug.WriteLine($"🔄 SIMPLE RENDER: Canvas={canvasWidth:F1}x{canvasHeight:F1}");

        // ✅ 1. SETTINGS KONTROLÜ: Scale alınabilir mi?
        bool settingsAvailable = GetUnifiedScaleFromSettings(canvasWidth, canvasHeight);

        if (!settingsAvailable)
        {
            // ❌ SETTINGS YOK: Mesaj göster ve çıkış yap
            DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
            System.Diagnostics.Debug.WriteLine($"❌ Cannot render G-Code: No valid settings available");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"🔄 Using settings-based scale: {_unifiedScale:F3}");

        // ✅ 2. OVERLAY'E SCALE VER (opsiyonel)
        var overlayManager = GetOverlayManager();
        if (overlayManager != null)
        {
            overlayManager.UpdateDynamicScale(_unifiedScale, canvasWidth, canvasHeight);
            System.Diagnostics.Debug.WriteLine($"✅ Updated overlay manager with settings scale: {_unifiedScale:F3}");
        }

        // ✅ 3. G-CODE ÇİZ: Settings varsa çiz
        Point? originPosition = null;
        if (gcodeSegments?.Count > 0)
        {
            originPosition = DrawGCodeCentered(mainCanvas, gcodeSegments, _unifiedScale, canvasWidth, canvasHeight);
        }

        // 4. Overlay refresh
        RefreshTopViewOverlay();

        System.Diagnostics.Debug.WriteLine($"✅ SIMPLE RENDER complete (Settings Scale={_unifiedScale:F3})");
        return originPosition;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"❌ RenderTopViewSimple error: {ex.Message}");
        DrawSettingsErrorMessage(mainCanvas, canvasWidth, canvasHeight);
        return null;
    }
}

/// <summary>
/// ✅ ERROR MESSAGE: Settings olmadığında hata mesajı göster - MOVED FROM MISSING
/// </summary>
private void DrawSettingsErrorMessage(Canvas canvas, double canvasWidth, double canvasHeight)
{
    try
    {
        // Canvas'ı temizle
        canvas.Children.Clear();

        // Hata mesajı background
        var errorBackground = new Rectangle
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0)) // Hafif kırmızı background
        };
        canvas.Children.Add(errorBackground);

        // Ana hata mesajı
        var errorTitle = new TextBlock
        {
            Text = "⚠️ G-Code Çizilemedi",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.Red),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(errorTitle, (canvasWidth - 200) / 2);
        Canvas.SetTop(errorTitle, (canvasHeight / 2) - 40);
        canvas.Children.Add(errorTitle);

        // Detay mesajı
        var errorMessage = new TextBlock
        {
            Text = "Makine ayarları ($130/$131) bulunamadı.\nLütfen makineye bağlanın ve ayarları yükleyin.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.DarkRed),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 250
        };
        Canvas.SetLeft(errorMessage, (canvasWidth - 250) / 2);
        Canvas.SetTop(errorMessage, (canvasWidth / 2) - 10);
        canvas.Children.Add(errorMessage);

        // Çözüm önerisi
        var solutionMessage = new TextBlock
        {
            Text = "💡 Çözüm: Makineye bağlanıp GRBL ayarlarını alın",
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.Orange),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(solutionMessage, (canvasWidth - 300) / 2);
        Canvas.SetTop(solutionMessage, (canvasHeight / 2) + 40);
        canvas.Children.Add(solutionMessage);

        System.Diagnostics.Debug.WriteLine($"✅ Settings error message displayed on canvas");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"❌ DrawSettingsErrorMessage error: {ex.Message}");
    }
}

/// <summary>
/// ✅ BASIC: Basit G-Code segment çizimi - MOVED FROM MISSING
/// ✅ OPTIMIZED: Now uses StreamGeometry for better performance
/// </summary>
private void DrawBasicGCodeSegments(Canvas canvas, ViewportType viewportType, List<GCodeSegment> gcodeSegments, double canvasWidth, double canvasHeight)
{
    try
    {
        if (gcodeSegments == null || gcodeSegments.Count == 0) return;

        System.Diagnostics.Debug.WriteLine($"🎨 OPTIMIZED G-CODE DRAWING: viewport={viewportType}, segments={gcodeSegments.Count}");
         var sw = System.Diagnostics.Stopwatch.StartNew();

   // ✅ CALCULATE BOUNDS for proper scaling and centering
      var bounds = CalculateGCodeBounds(gcodeSegments);
      
     // ✅ Z VALUES for depth coloring
      var allZValues = gcodeSegments.SelectMany(s => new[] { s.StartPoint.Z, s.EndPoint.Z }).ToList();
   double minZ = allZValues.Min();
        double maxZ = allZValues.Max();

      System.Diagnostics.Debug.WriteLine($"   • Z Range: {minZ:F3} to {maxZ:F3} for depth coloring");

   // ✅ CALCULATE APPROPRIATE SCALE based on viewport type
              double dataWidth, dataHeight, scale;
          
       switch (viewportType)
   {
         case ViewportType.Front: // X-Z view
       dataWidth = bounds.MaxX - bounds.MinX;
   dataHeight = bounds.MaxZ - bounds.MinZ;
                 break;
      case ViewportType.Right: // Y-Z view
              dataWidth = bounds.MaxY - bounds.MinY;
   dataHeight = bounds.MaxZ - bounds.MinZ;
      break;
   case ViewportType.Isometric: // 3D view
       // For isometric, we need to calculate the bounds after projection
        var isoPoints = new List<(double X, double Y)>();
        foreach (var segment in gcodeSegments.Take(100)) // Sample for bounds calculation
     {
         var iso1 = TransformToIsometric(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z);
           var iso2 = TransformToIsometric(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z);
            isoPoints.Add(iso1);
          isoPoints.Add(iso2);
       }
  
     if (isoPoints.Count > 0)
      {
  dataWidth = isoPoints.Max(p => p.X) - isoPoints.Min(p => p.X);
     dataHeight = isoPoints.Max(p => p.Y) - isoPoints.Min(p => p.Y);
        }
        else
       {
        dataWidth = dataHeight = 100; // Fallback
      }
        break;
   default:
       dataWidth = dataHeight = 100; // Fallback
   break;
   }

       // ✅ ENSURE MINIMUM SIZE and calculate scale
   if (dataWidth <= 0) dataWidth = 10;
       if (dataHeight <= 0) dataHeight = 10;

        double margin = 40;
     double availableWidth = canvasWidth - (2 * margin);
   double availableHeight = canvasHeight - (2 * margin);
      double scaleX = availableWidth / dataWidth;
        double scaleY = availableHeight / dataHeight;
     scale = Math.Min(scaleX, scaleY) * 0.8; // Use 80% of available space

       // ✅ CALCULATE CENTER POINTS for proper positioning
        double centerX = canvasWidth / 2;
       double centerY = canvasHeight / 2;
    
        // Data center points based on viewport
       double dataCenterX = 0, dataCenterY = 0;
        
       switch (viewportType)
   {
    case ViewportType.Front: // X-Z view
             dataCenterX = (bounds.MinX + bounds.MaxX) / 2;
  dataCenterY = (bounds.MinZ + bounds.MaxZ) / 2;
       break;
       case ViewportType.Right: // Y-Z view
   dataCenterX = (bounds.MinY + bounds.MaxY) / 2;
            dataCenterY = (bounds.MinZ + bounds.MaxZ) / 2;
        break;
           case ViewportType.Isometric: // 3D view
      // Use the center of the original 3D data
     dataCenterX = (bounds.MinX + bounds.MaxX) / 2;
     dataCenterY = (bounds.MinY + bounds.MaxY) / 2;
  break;
            }

        System.Diagnostics.Debug.WriteLine($"   • Scale: {scale:F3}, Center: ({centerX:F1},{centerY:F1})");
        System.Diagnostics.Debug.WriteLine($"   • Data range: {dataWidth:F1}×{dataHeight:F1}");

      // ✅ OPTIMIZED: Use StreamGeometry for batch rendering
           int maxSegments = Math.Min(gcodeSegments.Count, 2000); // Performance limit
  var segmentsToRender = gcodeSegments.Take(maxSegments).ToList();

       // Group by Z-level color (20 colors max for good batching)
    var segmentsByColor = GroupSegmentsByZLevel(segmentsToRender, minZ, maxZ, 20);

      int totalLines = 0;
      foreach (var colorGroup in segmentsByColor)
    {
        var geometry = new StreamGeometry();

      using (StreamGeometryContext ctx = geometry.Open())
        {
       foreach (var segment in colorGroup.Value)
        {
  double x1, y1, x2, y2;

      // ✅ COORDINATE TRANSFORMATION based on viewport type
        switch (viewportType)
   {
   case ViewportType.Front: // X-Z view
       x1 = centerX + (segment.StartPoint.X - dataCenterX) * scale;
        y1 = centerY - (segment.StartPoint.Z - dataCenterY) * scale; // Y inverted
 x2 = centerX + (segment.EndPoint.X - dataCenterX) * scale;
         y2 = centerY - (segment.EndPoint.Z - dataCenterY) * scale;
        break;

   case ViewportType.Right: // Y-Z view
     x1 = centerX + (segment.StartPoint.Y - dataCenterX) * scale;
 y1 = centerY - (segment.StartPoint.Z - dataCenterY) * scale; // Y inverted
     x2 = centerX + (segment.EndPoint.Y - dataCenterX) * scale;
    y2 = centerY - (segment.EndPoint.Z - dataCenterY) * scale;
     break;

          case ViewportType.Isometric: // 3D view
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

        // Add line to geometry batch
         ctx.BeginFigure(new Point(x1, y1), false, false);
       ctx.LineTo(new Point(x2, y2), true, false);
       totalLines++;
       }
     }

      // Freeze for performance
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

     sw.Stop();
       System.Diagnostics.Debug.WriteLine($"✅ {viewportType} optimized drawing: {totalLines} lines in {sw.ElapsedMilliseconds}ms ({segmentsByColor.Count} geometry objects)");
}
      catch (Exception ex)
   {
  System.Diagnostics.Debug.WriteLine($"❌ DrawBasicGCodeSegments error: {ex.Message}");
        
   // ✅ FALLBACK: Simple drawing if enhanced fails
      DrawFallbackSegments(canvas, viewportType, gcodeSegments, canvasWidth, canvasHeight);
   }
}

// ✅ Helper method for grouping segments by color
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
/// ✅ FALLBACK: Simple segment drawing if enhanced method fails
/// </summary>
private void DrawFallbackSegments(Canvas canvas, ViewportType viewportType, List<GCodeSegment> gcodeSegments, double canvasWidth, double canvasHeight)
{
    try
    {
        double centerX = canvasWidth / 2;
    double centerY = canvasHeight / 2;
        double scale = 0.5; // Conservative scale

        foreach (var segment in gcodeSegments.Take(500)) // Very limited for safety
     {
            var line = new Line 
          { 
           StrokeThickness = 1,
  Stroke = new SolidColorBrush(Colors.Gray),
    Opacity = 0.7
       };

 // Simple coordinate mapping
         switch (viewportType)
   {
      case ViewportType.Front:
        line.X1 = centerX + segment.StartPoint.X * scale;
    line.Y1 = centerY - segment.StartPoint.Z * scale;
        line.X2 = centerX + segment.EndPoint.X * scale;
 line.Y2 = centerY - segment.EndPoint.Z * scale;
        break;
    case ViewportType.Right:
       line.X1 = centerX + segment.StartPoint.Y * scale;
      line.Y1 = centerY - segment.StartPoint.Z * scale;
       line.X2 = centerX + segment.EndPoint.Y * scale;
     line.Y2 = centerY - segment.EndPoint.Z * scale;
               break;
   case ViewportType.Isometric:
             var iso1 = TransformToIsometric(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z);
        var iso2 = TransformToIsometric(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z);
             line.X1 = centerX + iso1.X * scale;
     line.Y1 = centerY - iso1.Y * scale;
         line.X2 = centerX + iso2.X * scale;
        line.Y2 = centerY - iso2.Y * scale;
   break;
 }

         canvas.Children.Add(line);
        }

        System.Diagnostics.Debug.WriteLine($"⚠️ {viewportType} fallback drawing used");
    }
    catch (Exception ex)
    {
 System.Diagnostics.Debug.WriteLine($"❌ DrawFallbackSegments error: {ex.Message}");
    }
}

/// <summary>
/// ✅ NEW: Draw static overlay elements for non-TopView viewports
/// Canvas boyutundan bağımsız, statik konumlarda eksen yönlerini gösterir
/// </summary>
private void DrawStaticViewportOverlay(Canvas overlayCanvas, ViewportType viewportType)
{
    try
    {
        // ✅ STATIC POSITIONS: Canvas boyutundan bağımsız sabit konumlar
        double axisOriginX = 30;
     double axisOriginY = 150; // Canvas alt köşesinden yukarı
        double axisLength = 40;

        // ✅ VIEWPORT-SPECIFIC static axis drawing
        switch (viewportType)
        {
  case ViewportType.Front: // X-Z view
  DrawStaticAxisOverlay_Front(overlayCanvas, axisOriginX, axisOriginY, axisLength);
       break;

  case ViewportType.Right: // Y-Z view  
      DrawStaticAxisOverlay_Right(overlayCanvas, axisOriginX, axisOriginY, axisLength);
         break;

            case ViewportType.Isometric: // 3D view
   DrawStaticAxisOverlay_Isometric(overlayCanvas, axisOriginX, axisOriginY, axisLength);
            break;
      }

        // ✅ STATIC VIEWPORT LABEL: Sağ üst köşede sabit viewport adı
        DrawStaticViewportLabel(overlayCanvas, viewportType);

        System.Diagnostics.Debug.WriteLine($"✅ {viewportType} static overlay complete");
  }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"❌ DrawStaticViewportOverlay error: {ex.Message}");
    }
}

/// <summary>
/// ✅ STATIC FRONT VIEW: X (horizontal) ve Z (vertical) eksenleri - sabit konum
/// </summary>
private void DrawStaticAxisOverlay_Front(Canvas canvas, double originX, double originY, double length)
{
    // X ekseni (sağa - kırmızı)
    var xAxis = new Line
    {
    X1 = originX, Y1 = originY,
        X2 = originX + length, Y2 = originY,
        Stroke = new SolidColorBrush(Colors.Red),
      StrokeThickness = 3,
        Opacity = 0.9
    };
    canvas.Children.Add(xAxis);

    // Z ekseni (yukarı - mavi)  
    var zAxis = new Line
    {
        X1 = originX, Y1 = originY,
  X2 = originX, Y2 = originY - length,
        Stroke = new SolidColorBrush(Colors.Blue),
        StrokeThickness = 3,
        Opacity = 0.9
    };
 canvas.Children.Add(zAxis);

    // Ok uçları
    DrawArrowHead(canvas, originX + length, originY, 0, Colors.Red); // X ok ucu
    DrawArrowHead(canvas, originX, originY - length, 90, Colors.Blue); // Z ok ucu

    // Origin noktası
    DrawOriginDot(canvas, originX, originY, Colors.White);

    // Etiketler
    AddStaticAxisLabel(canvas, originX + length + 8, originY - 8, "X", Colors.Red);
    AddStaticAxisLabel(canvas, originX - 8, originY - length - 8, "Z", Colors.Blue);
}

/// <summary>
/// ✅ STATIC RIGHT VIEW: Y (horizontal) ve Z (vertical) eksenleri - sabit konum
/// </summary>
private void DrawStaticAxisOverlay_Right(Canvas canvas, double originX, double originY, double length)
{
    // Y ekseni (sağa - yeşil)
    var yAxis = new Line
    {
        X1 = originX, Y1 = originY,
        X2 = originX + length, Y2 = originY,
     Stroke = new SolidColorBrush(Colors.Green),
        StrokeThickness = 3,
        Opacity = 0.9
    };
    canvas.Children.Add(yAxis);

    // Z ekseni (yukarı - mavi)
    var zAxis = new Line
    {
        X1 = originX, Y1 = originY,
        X2 = originX, Y2 = originY - length,
        Stroke = new SolidColorBrush(Colors.Blue),
        StrokeThickness = 3,
        Opacity = 0.9
    };
    canvas.Children.Add(zAxis);

    // Ok uçları
    DrawArrowHead(canvas, originX + length, originY, 0, Colors.Green); // Y ok ucu
    DrawArrowHead(canvas, originX, originY - length, 90, Colors.Blue); // Z ok ucu

    // Origin noktası
    DrawOriginDot(canvas, originX, originY, Colors.White);

    // Etiketler
    AddStaticAxisLabel(canvas, originX + length + 8, originY - 8, "Y", Colors.Green);
    AddStaticAxisLabel(canvas, originX - 8, originY - length - 8, "Z", Colors.Blue);
}

/// <summary>
/// ✅ STATIC ISOMETRIC VIEW: X, Y, Z eksenleri 3D perspektifinde - sabit konum
/// </summary>
private void DrawStaticAxisOverlay_Isometric(Canvas canvas, double originX, double originY, double length)
{
    // X ekseni (sağa - kırmızı)
    var xAxis = new Line
{
      X1 = originX, Y1 = originY,
        X2 = originX + length, Y2 = originY,
        Stroke = new SolidColorBrush(Colors.Red),
   StrokeThickness = 3,
    Opacity = 0.9
    };
 canvas.Children.Add(xAxis);

    // Y ekseni (sol üst diagonal - yeşil)
    double yEndX = originX - length * 0.866; // cos(30°)
    double yEndY = originY - length * 0.5;   // sin(30°)
    var yAxis = new Line
    {
 X1 = originX, Y1 = originY,
        X2 = yEndX, Y2 = yEndY,
        Stroke = new SolidColorBrush(Colors.Green),
StrokeThickness = 3,
      Opacity = 0.9
    };
    canvas.Children.Add(yAxis);

    // Z ekseni (yukarı - mavi)
var zAxis = new Line
    {
        X1 = originX, Y1 = originY,
        X2 = originX, Y2 = originY - length,
     Stroke = new SolidColorBrush(Colors.Blue),
        StrokeThickness = 3,
        Opacity = 0.9
    };
    canvas.Children.Add(zAxis);

    // Ok uçları
    DrawArrowHead(canvas, originX + length, originY, 0, Colors.Red); // X ok ucu
    DrawArrowHead(canvas, yEndX, yEndY, 150, Colors.Green); // Y ok ucu (150° açı)
    DrawArrowHead(canvas, originX, originY - length, 90, Colors.Blue); // Z ok ucu

    // Origin noktası
    DrawOriginDot(canvas, originX, originY, Colors.White);

    // Etiketler
    AddStaticAxisLabel(canvas, originX + length + 8, originY - 8, "X", Colors.Red);
  AddStaticAxisLabel(canvas, yEndX - 15, yEndY - 8, "Y", Colors.Green);
    AddStaticAxisLabel(canvas, originX - 8, originY - length - 8, "Z", Colors.Blue);
}

/// <summary>
/// ✅ HELPER: Ok ucu çiz
/// </summary>
private void DrawArrowHead(Canvas canvas, double x, double y, double angleDegrees, Color color)
{
    try
    {
        double arrowSize = 8;
        double angleRad = angleDegrees * Math.PI / 180.0;

   // Ok ucu üçgeni için noktalar
        double x1 = x - arrowSize * Math.Cos(angleRad - 0.3);
     double y1 = y - arrowSize * Math.Sin(angleRad - 0.3);
        double x2 = x - arrowSize * Math.Cos(angleRad + 0.3);
  double y2 = y - arrowSize * Math.Sin(angleRad + 0.3);

        // Ok ucu çizgileri
        var arrow1 = new Line
        {
       X1 = x, Y1 = y, X2 = x1, Y2 = y1,
          Stroke = new SolidColorBrush(color),
StrokeThickness = 2,
        Opacity = 0.8
 };
        var arrow2 = new Line
        {
 X1 = x, Y1 = y, X2 = x2, Y2 = y2,
            Stroke = new SolidColorBrush(color),
          StrokeThickness = 2,
         Opacity = 0.8
      };

canvas.Children.Add(arrow1);
     canvas.Children.Add(arrow2);
    }
 catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"DrawArrowHead error: {ex.Message}");
    }
}

/// <summary>
/// ✅ HELPER: Origin noktası çiz (küçük beyaz daire)
/// </summary>
private void DrawOriginDot(Canvas canvas, double x, double y, Color color)
{
    try
    {
        var originDot = new Ellipse
      {
       Width = 6, Height = 6,
        Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Colors.Black),
     StrokeThickness = 1,
            Opacity = 0.9
     };
   Canvas.SetLeft(originDot, x - 3);
  Canvas.SetTop(originDot, y - 3);
  canvas.Children.Add(originDot);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"DrawOriginDot error: {ex.Message}");
    }
}

/// <summary>
/// ✅ HELPER: Statik eksen etiketi (sabit konum)
/// </summary>
private void AddStaticAxisLabel(Canvas canvas, double x, double y, string text, Color color)
{
    try
    {
   var label = new TextBlock
        {
            Text = text,
   Foreground = new SolidColorBrush(color),
      FontSize = 13,
   FontWeight = FontWeights.Bold,
         Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), // Semi-transparent background
            Padding = new Thickness(3, 1, 3, 1),
   Opacity = 0.9
        };
        Canvas.SetLeft(label, x);
     Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"AddStaticAxisLabel error: {ex.Message}");
 }
}

/// <summary>
/// ✅ HELPER: Statik viewport etiketi (sabit sağ üst konum)
/// </summary>
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
    labelColor = Color.FromRgb(255, 140, 140); // Light red
   break;
    case ViewportType.Right:
           labelText = "RIGHT (Y-Z)";
     labelColor = Color.FromRgb(140, 255, 140); // Light green
     break;
         case ViewportType.Isometric:
                labelText = "ISOMETRIC (3D)";
  labelColor = Color.FromRgb(140, 140, 255); // Light blue
    break;
        }

  var viewportLabel = new TextBlock
        {
    Text = labelText,
            Foreground = new SolidColorBrush(labelColor),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
      Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), // Semi-transparent background
  Padding = new Thickness(4, 2, 4, 2),
  Opacity = 0.9
      };

// ✅ STATIC POSITION: Sabit sağ üst konum (canvas boyutundan bağımsız)
        Canvas.SetRight(viewportLabel, 8);
        Canvas.SetTop(viewportLabel, 8);
    canvas.Children.Add(viewportLabel);
    }
    catch (Exception ex)
    {
   System.Diagnostics.Debug.WriteLine($"DrawStaticViewportLabel error: {ex.Message}");
    }
}

#endregion


  } // Close GCodeVisualization class
} // Close namespace