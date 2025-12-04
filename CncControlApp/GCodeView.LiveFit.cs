using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CncControlApp.Helpers; // ✅ ADD: For StatusBarManager and UiHelper

namespace CncControlApp
{
    public partial class GCodeView
    {
        // Live fit checking fields
        private double _currentPartWidth = 0;
        private double _currentPartHeight = 0;
        private double _tableMaxX = 0;
        private double _tableMaxY = 0;
        private bool _tableDimensionsLoaded = false;

        // Load table limits from settings ($130/$131)
        private void LoadTableDimensionsFromSettings()
        {
            try
            {
                if (App.MainController?.Settings != null && App.MainController.Settings.Count > 0)
                {
                    var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                    var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);

                    if (xLimit != null && yLimit != null)
                    {
                        if (double.TryParse(xLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tableMaxX) &&
                            double.TryParse(yLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double tableMaxY))
                        {
                            if (tableMaxX > 0 && tableMaxY > 0)
                            {
                                _tableMaxX = tableMaxX;
                                _tableMaxY = tableMaxY;
                                _tableDimensionsLoaded = true;

                                App.MainController?.AddLogMessage($"> 🔧 Table dimensions loaded: {_tableMaxX:F0}×{_tableMaxY:F0}mm (from $130/$131)");
                                return;
                            }
                        }
                    }
                }

                _tableDimensionsLoaded = false;
                App.MainController?.AddLogMessage("> ⚠️ Table dimensions not available - fit checking disabled");
            }
            catch (Exception ex)
            {
                _tableDimensionsLoaded = false;
                System.Diagnostics.Debug.WriteLine($"❌ LoadTableDimensionsFromSettings error: {ex.Message}");
            }
        }

        // Live fit evaluation against table bounds using current rotation angle
        private (bool Fits, string FitStatus, string DetailedInfo) CheckLiveFitStatus()
        {
            try
            {
                // If no table dims or no file/segments, fall back to placeholders
                if (!_tableDimensionsLoaded)
                    return (false, "NO TABLE", "Table dimensions not loaded from GRBL ($130/$131)");

                if (_fileService == null || _fileService.GCodeSegments == null || _fileService.GCodeSegments.Count == 0)
                    return (true, "-", "No G-Code segments");

                // Compute live fit using current rotation angle - this checks POSITION not just size
                var (fits, details) = CheckLiveFitAtAngle(_currentRotationAngle);
                if (fits)
                {
                    return (true, "✓ OK", details);
                }
                else
                {
                    return (false, "✗ UNFIT", details);
                }
            }
            catch (Exception ex)
            {
                return (false, "ERROR", $"Fit check error: {ex.Message}");
            }
        }

        // Update cached part dimensions used for status bar live fit check
        private void UpdateCurrentPartDimensions(double width, double height)
        {
            _currentPartWidth = width;
            _currentPartHeight = height;
        }

        /// <summary>
        /// Public method to refresh status bar fit status - can be called from RotationPopup
        /// </summary>
        public void RefreshFitStatus()
        {
            UpdateStatusBarWithLiveFitCheck();
        }

        // ✅ REFACTORED: Status bar update using live fit check (position-based)
        private void UpdateStatusBarWithLiveFitCheck()
        {
            try
            {
                bool isFileLoaded = _fileService?.IsFileLoaded == true && DisplayGCodeLines?.Count > 0;

                // Part size text
                string partText;
                if (isFileLoaded && (_currentPartWidth > 0 || _currentPartHeight > 0))
                    partText = $"| PART: {_currentPartWidth:F1}×{_currentPartHeight:F1}mm";
                else
                    partText = "| PART: -";

                // Fit status - use live position-based check
                string fitText;
                Color fitColor;
                string fitTooltip;

                if (!isFileLoaded)
                {
                    fitText = "| FIT: -";
                    fitColor = Color.FromRgb(221, 221, 221); // gray
                    fitTooltip = "No G-Code loaded";
                }
                else
                {
                    // Use CheckLiveFitStatus which checks actual position, not just size
                    var (fits, fitStatus, detailedInfo) = CheckLiveFitStatus();
                    fitText = $"| FIT: {fitStatus}";
                    fitTooltip = detailedInfo;

                    if (!_tableDimensionsLoaded)
                        fitColor = Color.FromRgb(170, 170, 170); // gray
                    else if (fits)
                        fitColor = Color.FromRgb(52, 199, 89); // green
                    else
                        fitColor = Color.FromRgb(255, 59, 48); // red
                }

                // ✅ Apply to UI
                UiHelper.RunOnUi(() =>
                {
                    if (PartSizeTextBlock != null)
                        PartSizeTextBlock.Text = partText;
                    
                    if (FitStatusTextBlock != null)
                    {
                        FitStatusTextBlock.Text = fitText;
                        FitStatusTextBlock.Foreground = new SolidColorBrush(fitColor);
                        FitStatusTextBlock.ToolTip = fitTooltip;
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatusBarWithLiveFitCheck error: {ex.Message}");
            }
        }

        // Back-compat shim (call this from old sites)
        private void UpdateStatusBar() => UpdateStatusBarWithLiveFitCheck();

        /// <summary>
        /// Public method to refresh status bar (FIT status) from external callers.
        /// </summary>
        public void RefreshStatusBar() => UpdateStatusBarWithLiveFitCheck();
    }
}