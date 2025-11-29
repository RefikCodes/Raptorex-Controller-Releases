using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CncControlApp.Controls;
using CncControlApp.Helpers; // 🆕 Add helper
using IoPath = System.IO.Path;

namespace CncControlApp
{
    public partial class GCodeView
    {
        private async void FileButton_Click(object sender, RoutedEventArgs e)
        {
            try
        {
    // OLD BEHAVIOR: cleaned immediately before showing dialog (caused blank UI if user cancelled)
            // NEW: Show dialog first, only clean if a new file is actually chosen.
                Window ownerWindow = Window.GetWindow(this);
        var customDialog = new CustomFileDialog { Owner = ownerWindow };
              customDialog.ShowDialog();

    if (customDialog.DialogResult && !string.IsNullOrEmpty(customDialog.SelectedFile))
  {
        // User confirmed a file; now perform full clean just before loading/parsing.
       PerformFullCleanBeforeOpen();

              bool success = await _fileService.LoadFileWithProgressAsync(customDialog.SelectedFile);
    if (success)
     {
       LoadTableDimensionsFromSettings();
               UpdateStatusBarWithLiveFitCheck();
      }
        }
     // If cancelled: keep existing data intact.
   }
 catch { }
  }

  // CLEAN button removed. Consolidate its behavior here and call before every open
      private void PerformFullCleanBeforeOpen()
 {
            try
        {
 // Clear file service data and display collection
              _fileService.ClearAllData();
                DisplayGCodeLines.Clear();

       // Placeholder'ı göster
       UpdateEmptyStatePlaceholder();

       // Clear controller-side lines and loaded file path
       if (App.MainController != null)
           {
    App.MainController.GCodeLines.Clear();
               App.MainController.LoadedGCodeFile = null;
  // Reset execution state and counters to a known baseline
     App.MainController.GCodeManager?.ResetExecutionState();
    }
      
    // Clear stored G-code origin position (for rotation)
      _gcodeOriginCanvasPosition = null;
        
    // Reset rotation shift values
            _rotationAppliedShiftX = 0;
          _rotationAppliedShiftY = 0;

      // Zero part metrics
       UpdateCurrentPartDimensions(0, 0);

       // Reset right-panel UI/labels (progress, times, overrides display, status bar fit/origin)
    ResetUIElements();

    // Also ensure list focus sits at top item if present
  if (GCodeListBox != null && GCodeListBox.Items.Count > 0)
                {
  _suppressSelectionChanged = true;
            try
   {
    GCodeListBox.SelectedIndex = 0;
              GCodeListBox.ScrollIntoView(GCodeListBox.Items[0]);
        }
     finally { _suppressSelectionChanged = false; }
                }
        }
            catch (Exception ex)
     {
                try { MessageDialog.ShowError("Clean Before Open", ex.Message); } catch { }
  }
    }

        private void ResetUIElements()
        {
  try
    {
      // 🆕 Use UiHelper to reduce repetitive dispatcher code
                UiHelper.RunOnUi(() =>
        {
       UiHelper.SafeUpdateTextBlock(StatusTextBlock, "Ready - G-Code data cleared");
   UiHelper.SafeUpdateTextBlock(LinearDistanceTextBlock, "0.0mm");
    UiHelper.SafeUpdateTextBlock(RapidDistanceTextBlock, "0.0mm");
            UiHelper.SafeUpdateTextBlock(ArcDistanceTextBlock, "0.0mm");
             UiHelper.SafeUpdateTextBlock(TotalDistanceTextBlock, "0.0mm");
UiHelper.SafeUpdateTextBlock(LinearTimeTextBlock, "0sec");
UiHelper.SafeUpdateTextBlock(RapidTimeTextBlock, "0sec");
  UiHelper.SafeUpdateTextBlock(ArcTimeTextBlock, "0sec");
    UiHelper.SafeUpdateTextBlock(TotalTimeTextBlock, "0sec");

       // Reset file info header
   UiHelper.SafeUpdateTextBlock(FileNameTextBlock, "No file selected");
          UiHelper.SafeUpdateTextBlock(FileSizeTextBlock, "0 KB");
          UiHelper.SafeUpdateTextBlock(TotalLinesTextBlock, "0");

        UpdateExecutionControlButtons();
       ResetFeedSpindleState();

              UpdateStatusBarWithLiveFitCheck();
        });
  }
      catch { }
        }

        private void SaveGCodeButton_Click(object sender, RoutedEventArgs e)
   {
         try
     {
          var lines = DisplayGCodeLines?
           .Select(l => l?.GCodeLine)
        .Where(s => !string.IsNullOrWhiteSpace(s))
           .ToList();

      if (lines == null || lines.Count == 0)
     {
    MessageDialog.ShowInfo("Kayıt", "Kaydedilecek G-Code satırı yok.");
    return;
    }

         string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
      if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
 {
     MessageDialog.ShowError("Hata", "Masaüstü dizini bulunamadı.");
   return;
    }

                int index = 1;
            string filePath;
       do
    {
        filePath = Path.Combine(desktop, $"gcode{index:D2}.nc");
               index++;
       } while (File.Exists(filePath) && index < 1000);

      File.WriteAllLines(filePath, lines);

                string fileName = IoPath.GetFileName(filePath);
       MessageDialog.ShowInfo("Kaydedildi", $"{fileName} masaüstüne kaydedildi.\nSatır sayısı: {lines.Count}");
  }
  catch (Exception ex)
        {
          MessageDialog.ShowError("Kayıt Hatası", ex.Message);
            }
        }
    }
}