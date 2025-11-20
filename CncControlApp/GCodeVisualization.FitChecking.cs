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
    /// Mathematical fit checking for G-Code - NO REDRAW REQUIRED
    /// Checks if G-Code fits within table bounds based on $130/$131 settings
    /// </summary>
    public partial class GCodeVisualization
    {
        /// <summary>
        /// Check if G-Code fits within table bounds mathematically
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

                // Get table dimensions from settings
                var tableDimensions = GetTableDimensionsFromSettings();
                if (!tableDimensions.HasValue)
                {
                    return (false, "Table dimensions not available ($130/$131 settings missing)", 0, 0, 0, 0);
                }

                double tableMaxX = tableDimensions.Value.MaxX;
                double tableMaxY = tableDimensions.Value.MaxY;

                // Calculate G-Code bounds
                var gcodeBounds = CalculateGCodeBounds(gcodeSegments);

                // Calculate required dimensions
                double gcodeWidth = gcodeBounds.MaxX - gcodeBounds.MinX;
                double gcodeHeight = gcodeBounds.MaxY - gcodeBounds.MinY;

                // Check if part fits when centered
                double centerX = tableMaxX / 2.0;
                double centerY = tableMaxY / 2.0;

                double gcodeCenterX = (gcodeBounds.MinX + gcodeBounds.MaxX) / 2.0;
                double gcodeCenterY = (gcodeBounds.MinY + gcodeBounds.MaxY) / 2.0;

                double offsetX = centerX - gcodeCenterX;
                double offsetY = centerY - gcodeCenterY;

                double newMinX = gcodeBounds.MinX + offsetX;
                double newMaxX = gcodeBounds.MaxX + offsetX;
                double newMinY = gcodeBounds.MinY + offsetY;
                double newMaxY = gcodeBounds.MaxY + offsetY;

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

                return (fits, reason, gcodeWidth, gcodeHeight, tableMaxX, tableMaxY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CheckGCodeFitsInTable error: {ex.Message}");
                return (false, $"Error checking fit: {ex.Message}", 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Check if G-Code requires fit (extends beyond positive quadrant)
        /// </summary>
        public bool DoesGCodeRequireFit(List<GCodeSegment> gcodeSegments)
        {
            try
            {
                if (gcodeSegments == null || gcodeSegments.Count == 0)
                    return false;

                var bounds = CalculateGCodeBounds(gcodeSegments);
                bool requiresFit = bounds.MinX < 0 || bounds.MinY < 0;

                return requiresFit;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DoesGCodeRequireFit error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get fit preview text for UI
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
        /// Update fit preview in UI
        /// </summary>
        public void UpdateFitPreview(List<GCodeSegment> gcodeSegments, bool enableFit)
        {
            try
            {
                UiHelper.RunOnUi(() =>
                {
                    try
                    {
                        var fitPreviewTextBlock = FindControlByName<TextBlock>(Application.Current.MainWindow, "RotationFitPreviewTextBlock");

                        if (fitPreviewTextBlock != null)
                        {
                            string previewText = GetFitPreviewText(gcodeSegments, enableFit);
                            fitPreviewTextBlock.Text = previewText;

                            if (previewText.Contains("✅"))
                            {
                                fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(52, 199, 89));
                            }
                            else if (previewText.Contains("❌"))
                            {
                                fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                            }
                            else if (previewText.Contains("⚠️"))
                            {
                                fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
                            }
                            else
                            {
                                fitPreviewTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ UpdateFitPreview inner error: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateFitPreview error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get table dimensions from GRBL settings
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
        /// Calculate G-Code bounds
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
    }
}
