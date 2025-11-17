using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp.Handlers
{
    public class SliderHandler
    {
        private readonly MainControll _controller;
        private bool _isSliderTouchActive = false;
        private bool _isSpindleSliderTouchActive = false;
        private bool _isAAxisSliderTouchActive = false;
        
        private DateTime _lastSpindleLogTime = DateTime.MinValue;
        private double _lastLoggedSpindleSpeed = -1;
        private DateTime _lastSpindleUpdate = DateTime.MinValue;
        private const int SPINDLE_UPDATE_THROTTLE = 50;

        private DateTime _lastAAxisLogTime = DateTime.MinValue;
        private double _lastLoggedAAxisSpeed = -1;
        private DateTime _lastAAxisUpdate = DateTime.MinValue;
        private const int A_AXIS_UPDATE_THROTTLE = 100;

        // ✅ Jog Speed Slider için gerekli değişkenler
        private DateTime _lastJogSpeedLogTime = DateTime.MinValue;
        private double _lastLoggedJogSpeed = -1;
        private DateTime _lastJogSpeedUpdate = DateTime.MinValue;
        private const int JOG_SPEED_UPDATE_THROTTLE = 100;

        public SliderHandler(MainControll controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        #region Spindle Speed Slider Handlers

        public void HandleSpindleSpeedSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = HandleSpindleSpeedSliderValueChangedAsync(sender, e);
        }

        private async Task HandleSpindleSpeedSliderValueChangedAsync(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastSpindleUpdate).TotalMilliseconds < SPINDLE_UPDATE_THROTTLE)
                {
                    return;
                }
                _lastSpindleUpdate = now;

                if (_controller != null)
                {
                    _controller.SpindleSpeed = e.NewValue;
                }

                bool isSpindleOn = _controller?.IsSpindleOn == true;

                if (_controller?.IsConnected == true && isSpindleOn && e.NewValue > 0)
                {
                    double speedDifference = Math.Abs(e.NewValue - _lastLoggedSpindleSpeed);
                    bool shouldLog = (now - _lastSpindleLogTime).TotalSeconds >= 1.0 || speedDifference >= 100;
                    
                    string spindleCommand = $"M3 S{e.NewValue:F0}";
                    bool success = await _controller.SendGCodeCommandWithConfirmationAsync(spindleCommand);
                    
                    if (success && shouldLog)
                    {
                        string logMessage = $"> Spindle hızı güncellendi: {e.NewValue:F0} RPM";
                        _controller.AddLogMessage(logMessage);
                        _lastSpindleLogTime = now;
                        _lastLoggedSpindleSpeed = e.NewValue;
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Spindle hız güncellemesi - {ex.Message}");
            }
        }

        public void HandleSpindleSpeedSliderTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSpindleSliderTouchActive = true;
                    
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    double relativePosition = touchPoint.X / slider.ActualWidth;
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureTouch(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderTouchMove(object sender, TouchEventArgs e)
        {
            try
            {
                if (_isSpindleSliderTouchActive && sender is Slider slider)
                {
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    double relativePosition = touchPoint.X / slider.ActualWidth;
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSpindleSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderTouchLeave(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSpindleSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    Point mousePoint = e.GetPosition(slider);
                    double relativePosition = mousePoint.X / slider.ActualWidth;
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureMouse();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed && sender is Slider slider && slider.IsMouseCaptured)
                {
                    Point mousePoint = e.GetPosition(slider);
                    double relativePosition = mousePoint.X / slider.ActualWidth;
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleSpindleSpeedSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    slider.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        #endregion

        #region Jog Speed Slider Handlers

        /// <summary>
        /// ✅ EKSIK OLAN METHOD - Jog Speed Slider ValueChanged Handler
        /// </summary>
        public void HandleJogSpeedSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastJogSpeedUpdate).TotalMilliseconds < JOG_SPEED_UPDATE_THROTTLE)
                {
                    return;
                }
                _lastJogSpeedUpdate = now;

                if (_controller != null)
                {
                    _controller.JogSpeedPercentage = e.NewValue;

                    // ✅ Force UI update for jog speed display
                    _controller.OnPropertyChanged(nameof(_controller.CurrentXYJogSpeed));
                    _controller.OnPropertyChanged(nameof(_controller.CurrentZJogSpeed));
                    _controller.OnPropertyChanged(nameof(_controller.JogSpeedDisplayText));

                    double speedDifference = Math.Abs(e.NewValue - _lastLoggedJogSpeed);
                    bool shouldLog = (now - _lastJogSpeedLogTime).TotalSeconds >= 2.0 || speedDifference >= 10;
                    
                    if (shouldLog)
                    {
                        string logMessage = $"> Jog hızı ayarlandı: {e.NewValue:F0}% (XY: {_controller.CurrentXYJogSpeed} mm/min, Z: {_controller.CurrentZJogSpeed} mm/min)";
                        _controller.AddLogMessage(logMessage);
                        _lastJogSpeedLogTime = now;
                        _lastLoggedJogSpeed = e.NewValue;
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Jog hız güncellemesi - {ex.Message}");
            }
        }

        public void HandleJogSpeedSliderTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSliderTouchActive = true;
                    
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (touchPoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = touchPoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureTouch(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderTouchMove(object sender, TouchEventArgs e)
        {
            try
            {
                if (_isSliderTouchActive && sender is Slider slider)
                {
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (touchPoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = touchPoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderTouchLeave(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    Point mousePoint = e.GetPosition(slider);
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (mousePoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = mousePoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureMouse();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed && sender is Slider slider && slider.IsMouseCaptured)
                {
                    Point mousePoint = e.GetPosition(slider);
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (mousePoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = mousePoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleJogSpeedSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    slider.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        #endregion

        #region A-Axis Speed Slider Handlers

        public void HandleAAxisSpeedSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastAAxisUpdate).TotalMilliseconds < A_AXIS_UPDATE_THROTTLE)
                {
                    return;
                }
                _lastAAxisUpdate = now;

                if (_controller != null)
                {
                    _controller.AAxisSpeedPercentage = e.NewValue;

                    // ✅ Force UI update for A-Axis display
                    _controller.OnPropertyChanged(nameof(_controller.CurrentAAxisJogSpeed));
                    _controller.OnPropertyChanged(nameof(_controller.AAxisSpeedDisplayText));

                    double speedDifference = Math.Abs(e.NewValue - _lastLoggedAAxisSpeed);
                    bool shouldLog = (now - _lastAAxisLogTime).TotalSeconds >= 2.0 || speedDifference >= 10;
                    
                    if (shouldLog)
                    {
                        string logMessage = $"> A-ekseni hızı ayarlandı: {e.NewValue:F0}% ({_controller.CurrentAAxisJogSpeed} deg/min)";
                        _controller.AddLogMessage(logMessage);
                        _lastAAxisLogTime = now;
                        _lastLoggedAAxisSpeed = e.NewValue;
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A-ekseni hız güncellemesi - {ex.Message}");
            }
        }

        public void HandleAAxisSpeedSliderTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isAAxisSliderTouchActive = true;
                    
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (touchPoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = touchPoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureTouch(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderTouchMove(object sender, TouchEventArgs e)
        {
            try
            {
                if (_isAAxisSliderTouchActive && sender is Slider slider)
                {
                    Point touchPoint = e.GetTouchPoint(slider).Position;
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (touchPoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = touchPoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isAAxisSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderTouchLeave(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    _isAAxisSliderTouchActive = false;
                    slider.ReleaseTouchCapture(e.TouchDevice);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    Point mousePoint = e.GetPosition(slider);
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (mousePoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = mousePoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    slider.CaptureMouse();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed && sender is Slider slider && slider.IsMouseCaptured)
                {
                    Point mousePoint = e.GetPosition(slider);
                    
                    double relativePosition;
                    if (slider.Orientation == Orientation.Vertical)
                    {
                        relativePosition = 1.0 - (mousePoint.Y / slider.ActualHeight);
                    }
                    else
                    {
                        relativePosition = mousePoint.X / slider.ActualWidth;
                    }
                    
                    relativePosition = Math.Max(0, Math.Min(1, relativePosition));
                    
                    double newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
                    slider.Value = newValue;
                    
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAAxisSpeedSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    slider.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        #endregion
    }
}