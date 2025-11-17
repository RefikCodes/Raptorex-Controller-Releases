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
                    return (false, "NO TABLE", "Table dimensions not loaded");

                if (_fileService == null || _fileService.GCodeSegments == null || _fileService.GCodeSegments.Count == 0)
                    return (true, "NO PART", "No G-Code loaded");

                // Compute live fit using current rotation angle
                var (fits, details) = CheckLiveFitAtAngle(_currentRotationAngle);
                if (fits)
                {
                    return (true, "✅ FITS", details);
                }
                else
                {
                    return (false, "❌ OUT OF BOUNDS", details);
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

        // ✅ REFACTORED: Status bar update using StatusBarManager
        private void UpdateStatusBarWithLiveFitCheck()
        {
            try
            {
                // ✅ Create StatusBarManager with current state
                var statusManager = new StatusBarManager(
                    getPartDimensions: () => (_currentPartWidth, _currentPartHeight),
                    getTableDimensions: () => (_tableDimensionsLoaded, _tableMaxX, _tableMaxY),
                    getCurrentRotationAngle: () => _currentRotationAngle,
                    getEnableFitOnRotation: () => _enableFitOnRotation,
                    getIsFileLoaded: () => _fileService?.IsFileLoaded == true && DisplayGCodeLines?.Count > 0
                );

                // ✅ Get status bar info from manager
                var (partText, fitText, fitColor, fitTooltip, originText) =
                    statusManager.GetStatusBarInfo();

                // ✅ Apply to UI using UiHelper (eliminates manual Dispatcher.BeginInvoke)
                UiHelper.RunOnUi(() =>
                {
                    UiHelper.SafeUpdateTextBlock(PartSizeTextBlock, partText);
                    UiHelper.SafeUpdateTextBlock(FitStatusTextBlock, fitText, new SolidColorBrush(fitColor));

                    if (FitStatusTextBlock != null)
                    {
                        FitStatusTextBlock.ToolTip = fitTooltip;
                    }

                    UiHelper.SafeUpdateTextBlock(OriginStatusTextBlock, originText);
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatusBarWithLiveFitCheck error: {ex.Message}");
            }
        }

        // Back-compat shim (call this from old sites)
        private void UpdateStatusBar() => UpdateStatusBarWithLiveFitCheck();
    }
}