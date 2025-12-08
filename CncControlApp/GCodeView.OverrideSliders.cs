using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading; // for SemaphoreSlim

namespace CncControlApp
{
    public partial class GCodeView
    {
        // Added locks to prevent concurrent override flooding
        private readonly SemaphoreSlim _feedOverrideLock = new SemaphoreSlim(1,1);
        private readonly SemaphoreSlim _spindleOverrideLock = new SemaphoreSlim(1,1);

        private bool IsExecutionActive =>
            App.MainController?.IsGCodeRunning == true ||
            App.MainController?.CanContinueExecution == true;

        private void RefreshOverrideDisplays()
        {
            int feedPercent = _feedDebounceTimer.IsActive ? _pendingFeedOverridePercent : _lastFeedOverridePercent;
            int spindlePercent = _spindleDebounceTimer.IsActive ? _pendingSpindleOverridePercent : _lastSpindleOverridePercent;

            Func<double, string> pad5 = v => Math.Max(0, v).ToString("0", CultureInfo.InvariantCulture).PadLeft(5);

            // Always update the override percentages in controller to ensure they're tracked properly
            // during both idle and running states
            if (App.MainController != null)
            {
                if (_currentModalFeed == 0 && App.MainController.GCodeManager?.CurrentModalFeed > 0)
                {
                    _currentModalFeed = App.MainController.GCodeManager.CurrentModalFeed;
                }
                
                if (_currentModalSpindle == 0 && App.MainController.GCodeManager?.CurrentModalSpindle > 0)
                {
                    _currentModalSpindle = App.MainController.GCodeManager.CurrentModalSpindle;
                }
            }

            if (_currentModalFeed > 0)
            {
                double eff = _currentModalFeed * feedPercent / 100.0;
                CurrentFeedDisplay = $"F: {pad5(_currentModalFeed)} → {pad5(eff)} mm/min ({feedPercent}%)";
            }
            else
            {
                CurrentFeedDisplay = $"F: — ({feedPercent}%)";
            }

            if (_currentModalSpindle > 0)
            {
                double effS = _currentModalSpindle * spindlePercent / 100.0;
                int spindleDiff = spindlePercent - 100;
                string diffStr = spindleDiff >= 0 ? $"+{spindleDiff}" : $"{spindleDiff}";
                CurrentSpindleDisplay = $"S: {pad5(_currentModalSpindle)} → {pad5(effS)} RPM ({diffStr}%)";
            }
            else
            {
                int spindleDiff = spindlePercent - 100;
                string diffStr = spindleDiff >= 0 ? $"+{spindleDiff}" : $"{spindleDiff}";
                CurrentSpindleDisplay = $"S: — ({diffStr}%)";
            }
            
            // Also update the live speed panel
            UpdateLiveSpeedDisplay();
        }

        private void FeedOverrideSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                _pendingFeedOverridePercent = Math.Max(10, Math.Min(200, (int)Math.Round(e.NewValue)));
                int delta = _pendingFeedOverridePercent - _lastFeedOverridePercent;
                RefreshOverrideDisplays();

                if (IsExecutionActive)
                {
                    if (!_feedTouchActive && Math.Abs(delta) >= 5)
                    {
                        _feedDebounceTimer.Cancel();
                        _ = ApplyFeedOverrideAsync(_pendingFeedOverridePercent);
                        return;
                    }
                    // 🆕 Use SetInterval instead of accessing raw timer
                    _feedDebounceTimer.SetInterval(_feedTouchActive ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(80));
                    _feedDebounceTimer.Trigger();
                }
                else
                {
                    _feedDebounceTimer.SetInterval(_feedTouchActive ? TimeSpan.FromMilliseconds(90) : TimeSpan.FromMilliseconds(150));
                    _feedDebounceTimer.Trigger();
                }
            }
            catch { }
        }

        private void SpindleOverrideSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                // Slider is -50 to +50, convert to actual percent (50 to 150)
                int sliderValue = (int)Math.Round(e.NewValue);
                _pendingSpindleOverridePercent = Math.Max(50, Math.Min(150, 100 + sliderValue));
                int delta = _pendingSpindleOverridePercent - _lastSpindleOverridePercent;
                
                // Use more aggressive updates during execution, like feed override
                if (IsExecutionActive && !_spindleTouchActive && Math.Abs(delta) >= 5)
                {
                    _spindleDebounceTimer.Cancel();
                    _ = ApplySpindleOverrideAsync(_pendingSpindleOverridePercent);
                    return;
                }
                
                // 🆕 Use SetInterval method
                _spindleDebounceTimer.SetInterval(_spindleTouchActive ? TimeSpan.FromMilliseconds(90) : TimeSpan.FromMilliseconds(150));
                if (IsExecutionActive)
                {
                    _spindleDebounceTimer.SetInterval(_spindleTouchActive ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(80));
                }
                _spindleDebounceTimer.Trigger();
                RefreshOverrideDisplays();
            }
            catch { }
        }

        public async Task ApplyFeedOverrideAsync(int targetPercent)
        {
            if (App.MainController?.IsConnected != true)
                return;

            targetPercent = Math.Max(10, Math.Min(200, targetPercent));

            // Early exit if unchanged
            if (targetPercent == _lastFeedOverridePercent)
                return;

            await _feedOverrideLock.WaitAsync();
            try
            {
                int current = _lastFeedOverridePercent;
                int diff = targetPercent - current;

                // If returning to baseline100, send single reset character
                if (targetPercent ==100)
                {
                    await App.MainController.SendControlCharacterAsync('\x90'); // feed reset
                    _lastFeedOverridePercent =100;
                    RefreshOverrideDisplays();
                    App.MainController.RecalculateRemainingTimeWithFeedOverride(_lastFeedOverridePercent);
                    App.MainController.AddLogMessage("> Feed override reset to100% (baseline)");
                    return;
                }

                bool increase = diff >0;
                int distance = Math.Abs(diff);
                if (distance ==0) return;

                char step10 = increase ? '\x91' : '\x92';
                char step1 = increase ? '\x93' : '\x94';

                int steps10 = distance /10;
                int steps1 = distance %10;

                // Send larger steps first, minimal delay to avoid freezing execution
                for (int i =0; i < steps10; i++)
                {
                    await App.MainController.SendControlCharacterAsync(step10);
                    // tiny yield every few chars
                    if ((i +1) %5 ==0)
                        await Task.Yield();
                }
                for (int i =0; i < steps1; i++)
                {
                    await App.MainController.SendControlCharacterAsync(step1);
                }

                _lastFeedOverridePercent = targetPercent;
                RefreshOverrideDisplays();
                App.MainController.RecalculateRemainingTimeWithFeedOverride(_lastFeedOverridePercent);
                App.MainController.AddLogMessage($"> Feed override changed {current}% -> {_lastFeedOverridePercent}% (diff {diff:+#;-#;0})");
            }
            catch { }
            finally
            {
                _feedOverrideLock.Release();
            }
        }

        public async Task ApplySpindleOverrideAsync(int targetPercent)
        {
            if (App.MainController?.IsConnected != true)
                return;

            targetPercent = Math.Max(50, Math.Min(150, targetPercent));
            if (targetPercent == _lastSpindleOverridePercent)
                return;

            await _spindleOverrideLock.WaitAsync();
            try
            {
                int current = _lastSpindleOverridePercent;
                int diff = targetPercent - current;

                if (targetPercent == 100)
                {
                    await App.MainController.SendControlCharacterAsync('\x99'); // spindle reset (0x99)
                    _lastSpindleOverridePercent = 100;
                    RefreshOverrideDisplays();
                    App.MainController.AddLogMessage("> Spindle override reset to 100% (baseline)");
                    return;
                }

                bool increase = diff > 0;
                int distance = Math.Abs(diff);
                if (distance == 0) return;

                // GRBL Spindle Override: 0x9A=+10%, 0x9B=-10%, 0x9C=+1%, 0x9D=-1%
                char step10 = increase ? '\x9A' : '\x9B';
                char step1 = increase ? '\x9C' : '\x9D';

                int steps10 = distance /10;
                int steps1 = distance %10;

                for (int i =0; i < steps10; i++)
                {
                    await App.MainController.SendControlCharacterAsync(step10);
                    if ((i +1) %5 ==0)
                        await Task.Yield();
                }
                for (int i =0; i < steps1; i++)
                {
                    await App.MainController.SendControlCharacterAsync(step1);
                }

                _lastSpindleOverridePercent = targetPercent;
                RefreshOverrideDisplays();
                App.MainController.AddLogMessage($"> Spindle override changed {current}% -> {_lastSpindleOverridePercent}% (diff {diff:+#;-#;0})");
            }
            catch { }
            finally
            {
                _spindleOverrideLock.Release();
            }
        }

        private void OverrideSlider_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                var slider = sender as Slider;
                if (slider == null) return;

                bool captured = slider.CaptureTouch(e.TouchDevice);
                if (!captured)
                {
                    return;
                }

                Point touchPoint = e.GetTouchPoint(slider).Position;
                ApplyTouchPointToSlider(slider, touchPoint);

                DisableRightMenuPanning();

                if (ReferenceEquals(slider, FeedOverrideSlider))
                {
                    _feedTouchActive = true;
                    _feedDebounceTimer.SetInterval(TimeSpan.FromMilliseconds(30));
                }
                else if (ReferenceEquals(slider, SpindleOverrideSlider))
                {
                    _spindleTouchActive = true;
                    _spindleDebounceTimer.SetInterval(TimeSpan.FromMilliseconds(30));
                }
                else if (ReferenceEquals(slider, RotationAngleSlider))
                {
                    _rotationTouchActive = true;
                }

                e.Handled = true;
            }
            catch
            {
                RestoreRightMenuPanning();
                e.Handled = true;
            }
        }

        private void OverrideSlider_TouchMove(object sender, TouchEventArgs e)
        {
            try
            {
                var slider = sender as Slider;
                if (slider == null) return;

                bool isActive = false;
                if (ReferenceEquals(slider, FeedOverrideSlider) && _feedTouchActive)
                    isActive = true;
                else if (ReferenceEquals(slider, SpindleOverrideSlider) && _spindleTouchActive)
                    isActive = true;
                else if (ReferenceEquals(slider, RotationAngleSlider) && _rotationTouchActive)
                    isActive = true;

                if (!isActive) return;

                Point touchPoint = e.GetTouchPoint(slider).Position;
                ApplyTouchPointToSlider(slider, touchPoint);

                if (ReferenceEquals(slider, FeedOverrideSlider))
                {
                    if (DateTime.UtcNow - _lastFeedImmediate >= _touchImmediateInterval)
                    {
                        _lastFeedImmediate = DateTime.UtcNow;
                        _feedDebounceTimer.Trigger();
                    }
                }
                else if (ReferenceEquals(slider, SpindleOverrideSlider))
                {
                    if (DateTime.UtcNow - _lastSpindleImmediate >= _touchImmediateInterval)
                    {
                        _lastSpindleImmediate = DateTime.UtcNow;
                        _spindleDebounceTimer.Trigger();
                    }
                }

                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private async void OverrideSlider_TouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                RestoreRightMenuPanning();

                var slider = sender as Slider;
                if (slider != null && slider.AreAnyTouchesCaptured)
                {
                    slider.ReleaseTouchCapture(e.TouchDevice);
                }

                if (ReferenceEquals(slider, FeedOverrideSlider) && _feedTouchActive)
                {
                    _feedTouchActive = false;
                    _feedDebounceTimer.Cancel();
                    await ApplyFeedOverrideAsync(_pendingFeedOverridePercent);
                }
                else if (ReferenceEquals(slider, SpindleOverrideSlider) && _spindleTouchActive)
                {
                    _spindleTouchActive = false;
                    _spindleDebounceTimer.Cancel();
                    await ApplySpindleOverrideAsync(_pendingSpindleOverridePercent);
                }
                else if (ReferenceEquals(slider, RotationAngleSlider) && _rotationTouchActive)
                {
                    _rotationTouchActive = false;
                }

                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void OverrideSlider_LostTouchCapture(object sender, TouchEventArgs e)
        {
            try
            {
                RestoreRightMenuPanning();
                _feedTouchActive = false;
                _spindleTouchActive = false;
                _rotationTouchActive = false;
                e.Handled = true;
            }
            catch { }
        }

        private void DisableRightMenuPanning()
        {
            try
            {
                RightMenuScrollViewer?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (RightMenuScrollViewer != null &&
                        RightMenuScrollViewer.PanningMode != PanningMode.None)
                        RightMenuScrollViewer.PanningMode = PanningMode.None;
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch { }
        }

        private void RestoreRightMenuPanning()
        {
            try
            {
                RightMenuScrollViewer?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (RightMenuScrollViewer != null &&
                        RightMenuScrollViewer.PanningMode != PanningMode.VerticalOnly)
                        RightMenuScrollViewer.PanningMode = PanningMode.VerticalOnly;
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch { }
        }

        private void FeedOverrideResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FeedOverrideSlider != null)
                    FeedOverrideSlider.Value = 100;
            }
            catch { }
        }

        private void SpindleOverrideResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SpindleOverrideSlider != null)
                    SpindleOverrideSlider.Value = 0; // 0 = 100% (no change)
            }
            catch { }
        }

        private void ResetFeedSpindleState()
        {
            try
            {
                _lastFeedOverridePercent = 100;
                _lastSpindleOverridePercent = 100;
                _pendingFeedOverridePercent = 100;
                _pendingSpindleOverridePercent = 100;
                _currentModalFeed = 0;
                _currentModalSpindle = 0;

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (FeedOverrideSlider != null)
                            FeedOverrideSlider.Value = 100;
                        if (SpindleOverrideSlider != null)
                            SpindleOverrideSlider.Value = 0; // 0 = 100% (no change)
                        RefreshOverrideDisplays();
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        // Method to update override values from GCodeExecutionManager during execution
        public void UpdateOverridesFromExecutionManager()
        {
            try
            {
                if (App.MainController?.GCodeManager != null)
                {
                    _currentModalFeed = App.MainController.GCodeManager.CurrentModalFeed;
                    _currentModalSpindle = App.MainController.GCodeManager.CurrentModalSpindle;
                    RefreshOverrideDisplays();
                }
            }
            catch { }
        }

        private static void ApplyTouchPointToSlider(Slider slider, Point touchPoint)
        {
            if (slider == null)
                return;

            try
            {
                double min = slider.Minimum;
                double max = slider.Maximum;
                double span = max - min;
                if (span <= 0)
                    return;

                if (slider.Orientation == Orientation.Vertical)
                {
                    double h = Math.Max(1, slider.ActualHeight);
                    // Y artınca aşağı iner; üst = Max, alt = Min olacak şekilde ters çevir
                    double rel = 1.0 - Math.Max(0, Math.Min(1, touchPoint.Y / h));
                    slider.Value = min + span * rel;
                }
                else
                {
                    double w = Math.Max(1, slider.ActualWidth);
                    double rel = Math.Max(0, Math.Min(1, touchPoint.X / w));
                    slider.Value = min + span * rel;
                }
            }
            catch { }
        }
    }
}