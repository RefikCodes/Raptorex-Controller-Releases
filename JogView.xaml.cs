using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Linq;

namespace CncControlApp
{
    public partial class JogView : UserControl
    {
        // ✅ Fields - tek kopya
        private bool _isJoggingActive = false;
        private bool _isSliderTouchActive = false;
        private bool _isSpindleSliderTouchActive = false;
        private ToggleButton _selectedStepButton = null;
        
        // ✅ Spindle throttling fields
        private DateTime _lastSpindleUpdate = DateTime.MinValue;
        private const int SPINDLE_UPDATE_THROTTLE = 50;
        
        // ✅ Spindle logging fields
        private DateTime _lastSpindleLogTime = DateTime.MinValue;
        private double _lastLoggedSpindleSpeed = -1;

        public JogView()
        {
            InitializeComponent();
            this.DataContext = App.MainController;
            this.Loaded += JogView_Loaded;
        }

        private void JogView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStepButtonSelection();
            
            if (App.MainController != null)
            {
                App.MainController.JogSpeedPercentage = 50;
            }
        }

        private bool HasProperty(object obj, string propertyName)
        {
            return obj.GetType().GetProperty(propertyName) != null;
        }

        // ✅ Optimized StopJoggingAsync
        private async Task StopJoggingAsync(Button button)
        {
            if (_isJoggingActive && App.MainController?.IsConnected == true)
            {
                _isJoggingActive = false;
                
                // ✅ Always call StopJogAsync regardless of step mode - step mode operations are instantaneous
                await App.MainController.StopJogAsync();
                
                button.ReleaseMouseCapture();
            }
        }

        private async Task StopJoggingTouchAsync(Button button, TouchDevice touchDevice)
        {
            if (_isJoggingActive && App.MainController?.IsConnected == true)
            {
                _isJoggingActive = false;
                
                // ✅ Always call StopJogAsync regardless of step mode - step mode operations are instantaneous
                await App.MainController.StopJogAsync();

                button.ReleaseTouchCapture(touchDevice);
            }
        }

        // ✅ Optimized Spindle slider with throttling
        private async void SpindleSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastSpindleUpdate).TotalMilliseconds < SPINDLE_UPDATE_THROTTLE)
                {
                    return;
                }
                _lastSpindleUpdate = now;

                var spindleSpeedProp = App.MainController?.GetType().GetProperty("SpindleSpeed");
                if (spindleSpeedProp != null)
                {
                    spindleSpeedProp.SetValue(App.MainController, e.NewValue);
                }

                var isSpindleOnProp = App.MainController?.GetType().GetProperty("IsSpindleOn");
                bool isSpindleOn = false;
                if (isSpindleOnProp != null)
                {
                    isSpindleOn = (bool)(isSpindleOnProp.GetValue(App.MainController) ?? false);
                }

                if (App.MainController?.IsConnected == true && isSpindleOn && e.NewValue > 0)
                {
                    double speedDifference = Math.Abs(e.NewValue - _lastLoggedSpindleSpeed);
                    bool shouldLog = (now - _lastSpindleLogTime).TotalSeconds >= 1.0 || speedDifference >= 100;
                    
                    string spindleCommand = $"M3 S{e.NewValue:F0}";
                    bool success = await App.MainController.SendGCodeCommandWithConfirmationAsync(spindleCommand);
                    
                    if (success && shouldLog)
                    {
                        App.MainController.LogMessages.Add($"> Spindle hızı güncellendi: {e.NewValue:F0} RPM");
                        _lastSpindleLogTime = now;
                        _lastLoggedSpindleSpeed = e.NewValue;
                    }
                }
            }
            catch (Exception ex)
            {
                App.MainController?.LogMessages?.Add($"> HATA: Spindle hız güncellemesi - {ex.Message}");
            }
        }

        #region Control Button Event Handlers

        private async void SpindleButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainController?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;
                        bool success = await App.MainController.ToggleSpindleAsync(isOn);
                        
                        if (!success)
                        {
                            button.IsChecked = !isOn;
                        }
                    }
                }
                catch (Exception)
                {
                    if (sender is ToggleButton button)
                    {
                        button.IsChecked = !button.IsChecked;
                    }
                }
            }
            else
            {
                if (sender is ToggleButton button)
                {
                    button.IsChecked = false;
                }
            }
        }

        #endregion

        #region Mouse Event Handlers

        private async void JogXPlus_Start(object sender, MouseButtonEventArgs e)
        {
            if (App.MainController?.IsConnected == true && !_isJoggingActive)
            {
                _isJoggingActive = true;
                await App.MainController.StartJogXPlusAsync();
                
                if (sender is Button button)
                {
                    button.CaptureMouse();
                }
            }
        }

        private async void Jog_Stop(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button button)
            {
                await StopJoggingAsync(button);
            }
        }

        private async void Jog_Stop_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button button)
            {
                await StopJoggingAsync(button);
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateStepButtonSelection()
        {
            var uniformGrid = FindStepButtonContainer();
            if (uniformGrid != null && App.MainController != null)
            {
                foreach (ToggleButton button in uniformGrid.Children.OfType<ToggleButton>())
                {
                    if (button.Tag is string tagValue &&
                        double.TryParse(tagValue, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double buttonStepSize))
                    {
                        if (Math.Abs(buttonStepSize - App.MainController.SelectedStepSize) < 0.001)
                        {
                            button.IsChecked = true;
                            _selectedStepButton = button;
                        }
                        else
                        {
                            button.IsChecked = false;
                        }
                    }
                }
            }
        }

        private UniformGrid FindStepButtonContainer()
        {
            return FindVisualChild<UniformGrid>(this);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                    return (T)child;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        #endregion
    }
}