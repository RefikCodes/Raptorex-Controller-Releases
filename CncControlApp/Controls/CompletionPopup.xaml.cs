using System;
using System.Windows;

namespace CncControlApp.Controls
{
    public partial class CompletionPopup : Window
    {
        public CompletionPopup()
        {
            InitializeComponent();
        }

        public void SetCompletionInfo(DateTime startTime, DateTime endTime, int totalLines, string fileName, bool isSuccess = true)
        {
            StartTimeText.Text = startTime.ToString("HH:mm:ss");
            EndTimeText.Text = endTime.ToString("HH:mm:ss");
            
            var duration = endTime - startTime;
            DurationText.Text = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            
            LinesText.Text = totalLines.ToString("#,##0");
            FileNameText.Text = fileName;
            
            if (isSuccess)
            {
                StatusText.Text = "✓ Completed Successfully";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF34C759"));
                TitleText.Text = "✓ Run Complete";
            }
            else
            {
                StatusText.Text = "⚠ Completed with Warnings";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFCC00"));
                TitleText.Text = "⚠ Run Complete";
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
