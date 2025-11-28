using System;
using System.Windows;
using System.Windows.Threading;
using CncControlApp.Controls;

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void FeedRpmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popup = new FeedRpmPopup(this)
                {
                    Owner = Application.Current?.MainWindow
                };
                popup.Show();
            }
            catch { }
        }

        private void GCodeRotationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if G-code is loaded
                if (_fileService?.GCodeSegments == null || _fileService.GCodeSegments.Count == 0)
                {
                    MessageBox.Show("Please load a G-code file first.", "No G-Code Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var popup = new RotationPopup(this)
                {
                    Owner = Application.Current?.MainWindow
                };
                popup.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GCodeRotationButton_Click error: {ex.Message}");
                MessageBox.Show($"Error opening rotation popup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}