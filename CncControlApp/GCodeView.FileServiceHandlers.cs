using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CncControlApp.Helpers; // 🆕 Add helper

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void OnFileInformationUpdated(string fileName, long fileSize, int lineCount)
      {
     // 🆕 Use UiHelper instead of raw Dispatcher.BeginInvoke
         UiHelper.RunOnUi(() =>
     {
                try
           {
       UiHelper.SafeUpdateTextBlock(FileNameTextBlock, fileName);

        if (FileSizeTextBlock != null)
    {
  // 🆕 Use helper method for file size formatting
       FileSizeTextBlock.Text = UiHelper.FormatFileSize(fileSize);
           }

           UiHelper.SafeUpdateTextBlock(TotalLinesTextBlock, lineCount.ToString());
     }
         catch { }
 });
        }

  private void OnDistanceStatisticsUpdated(double xRange, double yRange, double zRange,
          double xTotalDistance, double yTotalDistance, double zTotalDistance)
   {
       UiHelper.RunOnUi(() =>
   {
           try
          {
            UiHelper.SafeUpdateTextBlock(XDistanceTextBlock, $"{xRange:F1}mm");
   UiHelper.SafeUpdateTextBlock(YDistanceTextBlock, $"{yRange:F1}mm");
        UiHelper.SafeUpdateTextBlock(ZDistanceTextBlock, $"{zRange:F1}mm");

           UpdateCurrentPartDimensions(xRange, yRange);
              UpdateStatusBarWithLiveFitCheck();
           }
       catch { }
            });
        }

        private void OnCuttingDistanceStatisticsUpdated(double linearDistance, double rapidDistance, double arcDistance, double totalDistance)
        {
      UiHelper.RunOnUi(() =>
        {
    try
       {
     UiHelper.SafeUpdateTextBlock(LinearDistanceTextBlock, $"{linearDistance:F1}mm");
    UiHelper.SafeUpdateTextBlock(RapidDistanceTextBlock, $"{rapidDistance:F1}mm");
            UiHelper.SafeUpdateTextBlock(ArcDistanceTextBlock, $"{arcDistance:F1}mm");
      UiHelper.SafeUpdateTextBlock(TotalDistanceTextBlock, $"{totalDistance:F1}mm");
 }
  catch { }
            });
     }

      private void OnCuttingTimeStatisticsUpdated(double linearTime, double rapidTime, double arcTime, double totalTime)
        {
     UiHelper.RunOnUi(() =>
      {
     try
       {
             // 🆕 Use helper method for time formatting
      UiHelper.SafeUpdateTextBlock(LinearTimeTextBlock, UiHelper.FormatTime(linearTime));
         UiHelper.SafeUpdateTextBlock(RapidTimeTextBlock, UiHelper.FormatTime(rapidTime));
     UiHelper.SafeUpdateTextBlock(ArcTimeTextBlock, UiHelper.FormatTime(arcTime));
           UiHelper.SafeUpdateTextBlock(TotalTimeTextBlock, UiHelper.FormatTime(totalTime));
    }
   catch { }
         });
      }

  private void OnFileLoadCompleted(string fileName)
        {
            UiHelper.RunOnUi(() =>
            {
            try
          {
        UiHelper.SafeUpdateTextBlock(StatusTextBlock, $"File loaded: {fileName} - Ready for execution");

  DisplayGCodeLines.Clear();
         if (_fileService?.GCodeLines != null)
{
             foreach (var item in _fileService.GCodeLines)
   {
        var text = item.GCodeLine ?? string.Empty;
    var trimmed = text.TrimStart();

      bool isInfo =
          string.IsNullOrWhiteSpace(trimmed) ||
    trimmed.StartsWith(";") ||
  trimmed.StartsWith("(") ||
      trimmed.StartsWith("MSG", StringComparison.OrdinalIgnoreCase);

  DisplayGCodeLines.Add(new GCodeLineItem
        {
   LineNumber = item.LineNumber,
         GCodeLine = text,
          IsInfo = isInfo
        });
         }
  }

  // Dosya yüklendi - placeholder'ı gizle
  UpdateEmptyStatePlaceholder();

     if (_fileService?.GCodeLines != null && App.MainController != null)
        {
            App.MainController.GCodeLines.Clear();
           foreach (var item in _fileService.GCodeLines)
        {
       if (!string.IsNullOrWhiteSpace(item.GCodeLine))
                App.MainController.GCodeLines.Add(item.GCodeLine);
         }
  App.MainController.LoadedGCodeFile = fileName;
   }

       if (_fileService?.GCodeSegments != null && _fileService.GCodeSegments.Count > 0)
        {
 App.MainController?.CalculateEstimatedExecutionTime(_fileService.GCodeSegments);
  }

      UpdateExecutionControlButtons();
    EnsureExecutionBindings();
           ResetFeedSpindleState();

         ResetRotationState();

 LoadTableDimensionsFromSettings();

           ScrollToCurrentLine();

            // ✅ TRIGGER: Notify all subscribers that G-code is loaded
            RaiseGCodePreviewUpdated();
            System.Diagnostics.Debug.WriteLine("✅ GCodePreviewUpdated event raised after file load");
                }
    catch { }
            });
        }

        private void ResetRotationState()
        {
        try
         {
    _currentRotationAngle = 0;

              if (RotationAngleSlider != null && Math.Abs(RotationAngleSlider.Value) > 0.0001)
       RotationAngleSlider.Value = 0;

             UpdateStatusBarWithLiveFitCheck();
          }
   catch { }
        }
    }
}