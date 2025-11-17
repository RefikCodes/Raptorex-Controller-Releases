using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CncControlApp.Controls
{
    public partial class FeedRpmPopup : Window
    {
        private readonly CncControlApp.GCodeView _gcodeView;
        private DateTime _lastFeedSend = DateTime.MinValue;
        private DateTime _lastSpindleSend = DateTime.MinValue;
        private readonly TimeSpan _sendThrottle = TimeSpan.FromMilliseconds(140); // prevent flooding

        public FeedRpmPopup(CncControlApp.GCodeView gcodeView)
        {
            InitializeComponent();
            _gcodeView = gcodeView;
            try { Owner = Application.Current?.MainWindow; } catch { }

            // ✅ NEW: Initialize sliders with current override values
            InitializeSliderValues();
        }

        /// <summary>
        /// ✅ NEW: Initialize feed and spindle sliders with current values from main view
        /// </summary>
        private void InitializeSliderValues()
        {
            try
            {
                if (_gcodeView == null) return;

                // Get current override percentages from GCodeView
                // Use reflection to access private fields since they're not exposed as public properties
                var gcodeViewType = _gcodeView.GetType();

                // Get feed override value
                var feedField = gcodeViewType.GetField("_lastFeedOverridePercent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (feedField != null)
                {
                    int currentFeedPercent = (int)feedField.GetValue(_gcodeView);
                    FeedSlider.Value = currentFeedPercent;
                    FeedValueText.Text = $"{currentFeedPercent:F0}%";
                    System.Diagnostics.Debug.WriteLine($"✅ Feed slider initialized to {currentFeedPercent}%");
                }

                // Get spindle override value
                var spindleField = gcodeViewType.GetField("_lastSpindleOverridePercent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (spindleField != null)
                {
                    int currentSpindlePercent = (int)spindleField.GetValue(_gcodeView);
                    SpindleSlider.Value = currentSpindlePercent;
                    SpindleValueText.Text = $"{currentSpindlePercent:F0}%";
                    System.Diagnostics.Debug.WriteLine($"✅ Spindle slider initialized to {currentSpindlePercent}%");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InitializeSliderValues error: {ex.Message}");
                // Fallback to default values if initialization fails
                FeedSlider.Value = 100;
                SpindleSlider.Value = 100;
            }
        }

        private void FeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { FeedValueText.Text = $"{Math.Round(e.NewValue):F0}%"; } catch { }
            try
            {
                if (_gcodeView == null) return;
                if (App.MainController?.IsConnected != true) return;
                if (DateTime.UtcNow - _lastFeedSend < _sendThrottle) return;
                _lastFeedSend = DateTime.UtcNow;
                int v = (int)Math.Round(e.NewValue);
                _ = _gcodeView.ApplyFeedOverrideAsync(v);
            }
            catch { }
        }

        private void SpindleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { SpindleValueText.Text = $"{Math.Round(e.NewValue):F0}%"; } catch { }
            try
            {
                if (_gcodeView == null) return;
                if (App.MainController?.IsConnected != true) return;
                if (DateTime.UtcNow - _lastSpindleSend < _sendThrottle) return;
                _lastSpindleSend = DateTime.UtcNow;
                int v = (int)Math.Round(e.NewValue);
                _ = _gcodeView.ApplySpindleOverrideAsync(v);
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        private void FeedResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FeedSlider.Value =100;
                if (_gcodeView != null)
                {
                    _lastFeedSend = DateTime.UtcNow; // reset throttle timestamp
                    _ = _gcodeView.ApplyFeedOverrideAsync(100);
                }
            }
            catch { }
        }

        private void SpindleResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SpindleSlider.Value =100;
                if (_gcodeView != null)
                {
                    _lastSpindleSend = DateTime.UtcNow;
                    _ = _gcodeView.ApplySpindleOverrideAsync(100);
                }
            }
            catch { }
        }

        // Ensure final value applied on release even if throttled
        private void FeedSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_gcodeView != null && App.MainController?.IsConnected == true)
                {
                    int v = (int)Math.Round(FeedSlider.Value);
                    _lastFeedSend = DateTime.UtcNow;
                    _ = _gcodeView.ApplyFeedOverrideAsync(v);
                }
            }
            catch { }
        }

        private void SpindleSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_gcodeView != null && App.MainController?.IsConnected == true)
                {
                    int v = (int)Math.Round(SpindleSlider.Value);
                    _lastSpindleSend = DateTime.UtcNow;
                    _ = _gcodeView.ApplySpindleOverrideAsync(v);
                }
            }
            catch { }
        }
    }
}
