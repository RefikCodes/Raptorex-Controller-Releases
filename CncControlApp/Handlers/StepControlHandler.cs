using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.ComponentModel;

namespace CncControlApp.Handlers
{
    public class StepControlHandler
    {
        private readonly MainControll _controller;
        private ToggleButton _selectedXYZStepButton = null;
        private ToggleButton _selectedAAxisStepButton = null;

        // References to UI elements
        private readonly UniformGrid _xyzStepButtonGrid;
        private readonly ToggleButton _xyzContinuousButton;
        private readonly ToggleButton _aContinuousButton;
        private readonly UniformGrid _aStepButtonGrid;

        public StepControlHandler(MainControll controller, UniformGrid xyzStepButtonGrid, ToggleButton xyzContinuousButton, ToggleButton aContinuousButton, UniformGrid aStepButtonGrid)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _xyzStepButtonGrid = xyzStepButtonGrid;
            _xyzContinuousButton = xyzContinuousButton;
            _aContinuousButton = aContinuousButton;
            _aStepButtonGrid = aStepButtonGrid;

            // Keep UI selections in sync when view-model changes without clicks
            _controller.PropertyChanged += OnControllerPropertyChanged;
        }

        private void OnControllerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                void RunOnUi(Action action)
                {
                    if (Application.Current?.Dispatcher?.CheckAccess() == true)
                        action();
                    else
                        Application.Current?.Dispatcher?.BeginInvoke(action);
                }

                switch (e.PropertyName)
                {
                    case nameof(MainControll.IsXYZStepMode):
                    case nameof(MainControll.SelectedXYZStepSize):
                        RunOnUi(UpdateXYZStepButtonSelection);
                        break;

                    case nameof(MainControll.IsAStepMode):
                    case nameof(MainControll.SelectedAStepSize):
                        RunOnUi(UpdateAAxisStepButtonSelection);
                        break;
                }
            }
            catch
            {
                // best effort; avoid UI noise
            }
        }

        public void HandleXYZContinuousClick(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is ToggleButton continuousButton && continuousButton.IsChecked == true)
                {
                    // Clear all step buttons
                    ClearXYZStepButtons();
                    _selectedXYZStepButton = null;

                    if (_controller != null)
                    {
                        _controller.IsXYZStepMode = false;
                        _controller.AddLogMessage("> XYZ Continuous Mode aktif");
                    }
                }
                else
                {
                    if (sender is ToggleButton button)
                        button.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: XYZ Continuous click - {ex.Message}");
                e.Handled = true;
            }
        }

        public void HandleXYZStepSizeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is ToggleButton clickedButton && clickedButton.Tag is string stepSizeString)
                {
                    // Clear continuous button
                    if (_xyzContinuousButton != null)
                    {
                        _xyzContinuousButton.IsChecked = false;
                    }

                    // Clear other step buttons
                    ClearXYZStepButtons();

                    clickedButton.IsChecked = true;
                    _selectedXYZStepButton = clickedButton;

                    if (double.TryParse(stepSizeString, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double stepSize))
                    {
                        _controller?.SetXYZStepSize(stepSize);

                        if (_controller != null)
                        {
                            _controller.IsXYZStepMode = true;
                            string logMessage = $"> XYZ Step Mode: {stepSize} mm";
                            _controller.AddLogMessage(logMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: XYZ step size click - {ex.Message}");
                e.Handled = true;
            }
        }

        public void HandleXYZStepSizeTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is ToggleButton button)
                {
                    e.Handled = true;
                    button.CaptureTouch(e.TouchDevice);
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleXYZStepSizeTouchUp(object sender, TouchEventArgs e)
        {
            try
            {
                if (sender is ToggleButton button)
                {
                    e.Handled = true;
                    button.ReleaseTouchCapture(e.TouchDevice);

                    if (button.Tag is string stepSizeString)
                    {
                        if (double.TryParse(stepSizeString, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double stepSize))
                        {
                            _controller?.SetXYZStepSize(stepSize);

                            if (_controller != null)
                            {
                                _controller.IsXYZStepMode = true;
                                _controller.AddLogMessage("> XYZ Step Mode otomatik olarak aktif edildi (Touch)");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleXYZStepSizePreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is ToggleButton button)
                {
                    button.CaptureMouse();
                }
            }
            catch (Exception)
            {
                e.Handled = true;
            }
        }

        public void HandleAContinuousClick(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is ToggleButton continuousButton && continuousButton.IsChecked == true)
                {
                    // Clear all A-axis step buttons
                    ClearAAxisStepButtons();
                    _selectedAAxisStepButton = null;

                    if (_controller != null)
                    {
                        _controller.IsAStepMode = false;
                        _controller.AddLogMessage("> A-Axis Continuous Mode aktif");
                    }
                }
                else
                {
                    if (sender is ToggleButton button)
                        button.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A Continuous click - {ex.Message}");
                e.Handled = true;
            }
        }

        public void HandleAAxisStepSizeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is ToggleButton clickedButton && clickedButton.Tag is string stepSizeString)
                {
                    // Clear continuous button
                    if (_aContinuousButton != null)
                    {
                        _aContinuousButton.IsChecked = false;
                    }

                    // Clear other step buttons
                    ClearAAxisStepButtons();

                    clickedButton.IsChecked = true;
                    _selectedAAxisStepButton = clickedButton;

                    if (double.TryParse(stepSizeString, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double stepSize))
                    {
                        _controller?.SetAStepSize(stepSize);

                        if (_controller != null)
                        {
                            _controller.IsAStepMode = true;
                            string logMessage = $"> A-Axis Step Mode: {stepSize}°";
                            _controller.AddLogMessage(logMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A-ekseni step size click - {ex.Message}");
                e.Handled = true;
            }
        }

        public void UpdateXYZStepButtonSelection()
        {
            if (_xyzStepButtonGrid != null && _controller != null)
            {
                bool isStepMode = _controller.IsXYZStepMode;

                if (_xyzContinuousButton != null)
                {
                    _xyzContinuousButton.IsChecked = !isStepMode;
                }

                if (isStepMode)
                {
                    foreach (ToggleButton button in _xyzStepButtonGrid.Children.OfType<ToggleButton>())
                    {
                        if (button.Tag is string tagValue &&
                            double.TryParse(tagValue, System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out double buttonStepSize))
                        {
                            if (Math.Abs(buttonStepSize - _controller.SelectedXYZStepSize) < 0.001)
                            {
                                button.IsChecked = true;
                                _selectedXYZStepButton = button;
                            }
                            else
                            {
                                button.IsChecked = false;
                            }
                        }
                    }
                }
                else
                {
                    ClearXYZStepButtons();
                    _selectedXYZStepButton = null;
                }
            }
        }

        public void UpdateAAxisStepButtonSelection()
        {
            if (_aStepButtonGrid != null && _controller != null)
            {
                bool isStepMode = _controller.IsAStepMode;

                if (_aContinuousButton != null)
                {
                    _aContinuousButton.IsChecked = !isStepMode;
                }

                if (isStepMode)
                {
                    foreach (ToggleButton button in _aStepButtonGrid.Children.OfType<ToggleButton>())
                    {
                        if (button.Tag is string tagValue &&
                            double.TryParse(tagValue, System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out double buttonStepSize))
                        {
                            if (Math.Abs(buttonStepSize - _controller.SelectedAStepSize) < 0.001)
                            {
                                button.IsChecked = true;
                                _selectedAAxisStepButton = button;
                            }
                            else
                            {
                                button.IsChecked = false;
                            }
                        }
                    }
                }
                else
                {
                    ClearAAxisStepButtons();
                    _selectedAAxisStepButton = null;
                }
            }
        }

        private void ClearXYZStepButtons()
        {
            if (_xyzStepButtonGrid != null)
            {
                foreach (ToggleButton button in _xyzStepButtonGrid.Children.OfType<ToggleButton>())
                {
                    button.IsChecked = false;
                }
            }
        }

        private void ClearAAxisStepButtons()
        {
            if (_aStepButtonGrid != null)
            {
                foreach (ToggleButton button in _aStepButtonGrid.Children.OfType<ToggleButton>())
                {
                    button.IsChecked = false;
                }
            }
        }
    }
}