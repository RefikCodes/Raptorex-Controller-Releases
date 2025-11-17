using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CncControlApp.Helpers
{
    /// <summary>
    /// Helper class for common UI operations to reduce code duplication
/// </summary>
    public static class UiHelper
    {
        /// <summary>
        /// Execute action on UI thread (dispatcher-aware)
        /// </summary>
    public static void RunOnUi(Action action, DispatcherPriority priority = DispatcherPriority.Background)
        {
     if (action == null) return;

            try
    {
   if (Application.Current?.Dispatcher?.CheckAccess() == true)
      action();
       else
   Application.Current?.Dispatcher?.BeginInvoke(action, priority);
            }
 catch (Exception ex)
      {
            System.Diagnostics.Debug.WriteLine($"RunOnUi error: {ex.Message}");
  }
  }

        /// <summary>
        /// Safely update TextBlock text and foreground
      /// </summary>
        public static void SafeUpdateTextBlock(TextBlock textBlock, string text, Brush foreground = null)
        {
RunOnUi(() =>
            {
  if (textBlock != null)
           {
      textBlock.Text = text;
           if (foreground != null)
      textBlock.Foreground = foreground;
         }
         });
        }

   /// <summary>
        /// Safely enable/disable a control
        /// </summary>
      public static void SafeSetEnabled(FrameworkElement element, bool enabled)
 {
     RunOnUi(() =>
      {
       if (element != null)
             element.IsEnabled = enabled;
     });
    }

        /// <summary>
     /// Safely set visibility
        /// </summary>
     public static void SafeSetVisibility(UIElement element, Visibility visibility)
        {
            RunOnUi(() =>
          {
    if (element != null)
          element.Visibility = visibility;
            });
        }

     /// <summary>
        /// Create a common status brush (reused colors)
     /// </summary>
 public static SolidColorBrush GetStatusBrush(string status)
        {
if (string.IsNullOrEmpty(status))
     return new SolidColorBrush(Colors.White);

            if (status.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
       return new SolidColorBrush(Color.FromRgb(52, 199, 89)); // Green

  if (status.StartsWith("Run", StringComparison.OrdinalIgnoreCase))
    return new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange

            if (status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
        return new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange

            if (status.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase))
          return new SolidColorBrush(Color.FromRgb(255, 59, 48)); // Red

 return new SolidColorBrush(Colors.White);
        }

        /// <summary>
    /// Format time duration (minutes) to human-readable string
        /// </summary>
        public static string FormatTime(double minutes)
    {
            if (minutes <= 0) return "0sec";
            if (minutes < 1.0) return $"{minutes * 60:F0}sec";
   if (minutes < 60.0) return $"{minutes:F1}min";
 
            int hours = (int)(minutes / 60);
double remaining = minutes % 60;
    return $"{hours}h {remaining:F0}min";
        }

 /// <summary>
    /// Format file size to human-readable string
        /// </summary>
    public static string FormatFileSize(long bytes)
        {
      if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}
