using System;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace CncControlApp.Helpers
{
    /// <summary>
 /// Consolidates status bar update logic to eliminate duplication across GCodeView partials
    /// </summary>
    public class StatusBarManager
    {
        private readonly Func<(double width, double height)> _getPartDimensions;
    private readonly Func<(bool tableDimensionsLoaded, double tableMaxX, double tableMaxY)> _getTableDimensions;
        private readonly Func<double> _getCurrentRotationAngle;
        private readonly Func<bool> _getEnableFitOnRotation;
        private readonly Func<bool> _getIsFileLoaded;

        public StatusBarManager(
     Func<(double width, double height)> getPartDimensions,
        Func<(bool loaded, double maxX, double maxY)> getTableDimensions,
Func<double> getCurrentRotationAngle,
   Func<bool> getEnableFitOnRotation,
         Func<bool> getIsFileLoaded)
 {
       _getPartDimensions = getPartDimensions ?? throw new ArgumentNullException(nameof(getPartDimensions));
            _getTableDimensions = getTableDimensions ?? throw new ArgumentNullException(nameof(getTableDimensions));
 _getCurrentRotationAngle = getCurrentRotationAngle ?? throw new ArgumentNullException(nameof(getCurrentRotationAngle));
            _getEnableFitOnRotation = getEnableFitOnRotation ?? throw new ArgumentNullException(nameof(getEnableFitOnRotation));
    _getIsFileLoaded = getIsFileLoaded ?? throw new ArgumentNullException(nameof(getIsFileLoaded));
      }

        public (string PartSizeText, string FitStatusText, Color FitColor, string FitTooltip, string OriginText) GetStatusBarInfo()
        {
        var partDims = _getPartDimensions();
          var (tableDimensionsLoaded, tableMaxX, tableMaxY) = _getTableDimensions();
        double currentAngle = _getCurrentRotationAngle();
            bool enableFit = _getEnableFitOnRotation();
  bool isFileLoaded = _getIsFileLoaded();

  string partSizeText;
    if (isFileLoaded && (partDims.width > 0 || partDims.height > 0))
         partSizeText = $"| PART: {partDims.width:F1}×{partDims.height:F1}mm";
    else
  partSizeText = "| PART: -";

    string fitStatusText;
        Color fitColor;
            string fitTooltip;

          if (!isFileLoaded)
{
             fitStatusText = "| FIT: -";
fitColor = Color.FromRgb(221, 221, 221);
      fitTooltip = "No G-Code loaded";
      }
         else
        {
        var (fits, fitStatus, detailedInfo) = CheckFitStatus(
             partDims.width, partDims.height,
           tableDimensionsLoaded, tableMaxX, tableMaxY,
             currentAngle);

           fitStatusText = $"| FIT: {fitStatus}";
        fitTooltip = detailedInfo;

if (!tableDimensionsLoaded)
            fitColor = Color.FromRgb(170, 170, 170); // gray
 else if (fits)
             fitColor = Color.FromRgb(52, 199, 89); // green
      else
      fitColor = Color.FromRgb(255, 59, 48); // red
            }

        string originText;
            if (isFileLoaded)
{
                double angle = Math.Abs(currentAngle);
    originText = angle < 0.1
      ? "| ORIGIN: 0,0"
         : $"| ORIGIN: rotated {currentAngle:F1}°";
            }
      else
            {
     originText = "| ORIGIN: -";
          }

    return (partSizeText, fitStatusText, fitColor, fitTooltip, originText);
        }

        private (bool Fits, string FitStatus, string DetailedInfo) CheckFitStatus(
      double partWidth, double partHeight,
     bool tableDimensionsLoaded, double tableMaxX, double tableMaxY,
   double currentAngle)
    {
      if (!tableDimensionsLoaded)
    return (false, "NO TABLE", "Table dimensions not loaded from GRBL ($130/$131)");

     if (partWidth <= 0 && partHeight <= 0)
    return (true, "-", "No part dimensions");

            // Simple bounding box check
    bool fits = partWidth <= tableMaxX && partHeight <= tableMaxY;

        if (fits)
 {
        return (true, "✓ OK", $"Part {partWidth:F1}×{partHeight:F1}mm fits in table {tableMaxX:F0}×{tableMaxY:F0}mm");
        }
  else
          {
        return (false, "✗ TOO BIG", $"Part {partWidth:F1}×{partHeight:F1}mm exceeds table {tableMaxX:F0}×{tableMaxY:F0}mm");
            }
    }
    }
}
