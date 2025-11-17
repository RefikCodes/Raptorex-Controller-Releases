using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp.Handlers
{
    public class JogMovementHandler
    {
        private readonly MainControll _controller;
        private bool _isXYZJoggingActive = false;
        private bool _isAJoggingActive = false;
        private volatile bool _touchEventInProgress = false;

        public JogMovementHandler(MainControll controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        #region Mouse Event Handlers

        public async Task HandleJogXPlusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch event'i işlem halindeyse mouse event'ini atla
                if (_touchEventInProgress)
                {
                    e.Handled = true;
                    return;
                }

                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    e.Handled = true;
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogXPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogXPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: X+ jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogXMinusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogXMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogXMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: X- jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogYPlusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogYPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogYPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Y+ jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogYMinusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogYMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogYMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Y- jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogZPlusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogZPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogZPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Z+ jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogZMinusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogZMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogZMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Z- jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogAPlusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsAStepMode)
                    {
                        await _controller.StartJogAPlusAsync();
                    }
                    else if (!_isAJoggingActive)
                    {
                        _isAJoggingActive = true;
                        await _controller.StartJogAPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A+ jog - {ex.Message}");
                _isAJoggingActive = false;
            }
        }

        public async Task HandleJogAMinusStart(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsAStepMode)
                    {
                        await _controller.StartJogAMinusAsync();
                    }
                    else if (!_isAJoggingActive)
                    {
                        _isAJoggingActive = true;
                        await _controller.StartJogAMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureMouse();
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A- jog - {ex.Message}");
                _isAJoggingActive = false;
            }
        }

        public async Task HandleJogStop(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Touch'tan gelen mouse event'ini filtrele
                if (e.StylusDevice != null)
                {
                    return;
                }
                
                if (sender is Button button)
                {
                    // Button ismine göre hangi axis olduğunu belirle
                    bool isXYZButton = IsXYZButton(button.Name);
                    bool isAAxisButton = IsAAxisButton(button.Name);
                    
                    // Sadece ilgili axis'in step mode durumunu kontrol et
                    if (isXYZButton && _controller?.IsXYZStepMode == true)
                    {
                        button.ReleaseMouseCapture();
                        return;
                    }
                    
                    if (isAAxisButton && _controller?.IsAStepMode == true)
                    {
                        button.ReleaseMouseCapture();
                        return;
                    }
                    
                    await StopJoggingAsync(button);
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Jog durdurma - {ex.Message}");
            }
        }

        public async Task HandleJogStopMouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    await StopJoggingAsync(button);
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Mouse leave jog durdurma - {ex.Message}");
            }
        }

        public async Task HandleJogMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // Hem XYZ hem de A-axis jogging'i kontrol et
                if ((_isXYZJoggingActive || _isAJoggingActive) && sender is Button button)
                {
                    Point mousePos = e.GetPosition(button);
                    bool isInside = mousePos.X >= 0 && mousePos.Y >= 0 && 
                                   mousePos.X <= button.ActualWidth && mousePos.Y <= button.ActualHeight;
            
                    if (!isInside)
                    {
                        await StopJoggingAsync(button);
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Mouse move jog kontrolü - {ex.Message}");
            }
        }

        #endregion

        #region Touch Event Handlers

        public async Task HandleJogXPlusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                // Touch event'inin işlem halinde olduğunu işaretle
                _touchEventInProgress = true;
                
                // Touch-to-mouse promotion'ı engelle
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogXPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogXPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
                
                // Touch event'i tamamlandı
                await Task.Delay(50);
                _touchEventInProgress = false;
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: X+ touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
                _touchEventInProgress = false;
            }
        }

        public async Task HandleJogXMinusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogXMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogXMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: X- touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogYPlusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogYPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogYPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Y+ touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogYMinusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogYMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogYMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Y- touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogZPlusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogZPlusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogZPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Z+ touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogZMinusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsXYZStepMode)
                    {
                        await _controller.StartJogZMinusAsync();
                    }
                    else if (!_isXYZJoggingActive)
                    {
                        _isXYZJoggingActive = true;
                        await _controller.StartJogZMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Z- touch jog - {ex.Message}");
                _isXYZJoggingActive = false;
            }
        }

        public async Task HandleJogAPlusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsAStepMode)
                    {
                        await _controller.StartJogAPlusAsync();
                    }
                    else if (!_isAJoggingActive)
                    {
                        _isAJoggingActive = true;
                        await _controller.StartJogAPlusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A+ touch jog - {ex.Message}");
                _isAJoggingActive = false;
            }
        }

        public async Task HandleJogAMinusTouchStart(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (_controller?.IsConnected == true)
                {
                    if (_controller.IsAStepMode)
                    {
                        await _controller.StartJogAMinusAsync();
                    }
                    else if (!_isAJoggingActive)
                    {
                        _isAJoggingActive = true;
                        await _controller.StartJogAMinusAsync();
                        
                        if (sender is Button button)
                        {
                            button.CaptureTouch(e.TouchDevice);
                        }
                    }
                }
                else
                {
                    _controller?.AddLogMessage("> ❌ CNC bağlı değil - jog çalışmıyor");
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: A- touch jog - {ex.Message}");
                _isAJoggingActive = false;
            }
        }

        public async Task HandleJogTouchStop(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                if (sender is Button button)
                {
                    // Button ismine göre hangi axis olduğunu belirle
                    bool isXYZButton = IsXYZButton(button.Name);
                    bool isAAxisButton = IsAAxisButton(button.Name);
                    
                    // Sadece ilgili axis'in step mode durumunu kontrol et
                    if (isXYZButton && _controller?.IsXYZStepMode == true)
                    {
                        if (button != null)
                        {
                            button.ReleaseTouchCapture(e.TouchDevice);
                        }
                        return;
                    }
                    
                    if (isAAxisButton && _controller?.IsAStepMode == true)
                    {
                        if (button != null)
                        {
                            button.ReleaseTouchCapture(e.TouchDevice);
                        }
                        return;
                    }
                }
                
                if (sender is Button btn)
                {
                    await StopJoggingTouchAsync(btn, e.TouchDevice);
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Touch jog durdurma - {ex.Message}");
            }
        }

        public async Task HandleJogTouchMove(object sender, TouchEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                // In step mode, we don't need to handle touch moves
                if (_controller?.IsXYZStepMode == true || _controller?.IsAStepMode == true)
                {
                    return;
                }
                
                // Hem XYZ hem de A-axis jogging'i kontrol et
                if ((_isXYZJoggingActive || _isAJoggingActive) && sender is Button button)
                {
                    Point touchPos = e.GetTouchPoint(button).Position;
                    bool isInside = touchPos.X >= 0 && touchPos.Y >= 0 && 
                                    touchPos.X <= button.ActualWidth && touchPos.Y <= button.ActualHeight;
                    
                    if (!isInside)
                    {
                        await StopJoggingTouchAsync(button, e.TouchDevice);
                    }
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Touch move jog kontrolü - {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private async Task StopJoggingAsync(Button button)
        {
            try
            {
                // Hangi axis jog'ının aktif olduğunu kontrol et
                bool shouldStop = false;
                
                if (_isXYZJoggingActive)
                {
                    _isXYZJoggingActive = false;
                    shouldStop = true;
                }
                
                if (_isAJoggingActive)
                {
                    _isAJoggingActive = false;
                    shouldStop = true;
                }
                
                if (shouldStop && _controller?.IsConnected == true)
                {
                    await _controller.StopJogAsync();
                }
                
                button.ReleaseMouseCapture();
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Jog durdurma - {ex.Message}");
                _isXYZJoggingActive = false;
                _isAJoggingActive = false;
            }
        }

        private async Task StopJoggingTouchAsync(Button button, TouchDevice touchDevice)
        {
            try
            {
                // Hangi axis jog'ının aktif olduğunu kontrol et
                bool shouldStop = false;
                
                if (_isXYZJoggingActive)
                {
                    _isXYZJoggingActive = false;
                    shouldStop = true;
                }
                
                if (_isAJoggingActive)
                {
                    _isAJoggingActive = false;
                    shouldStop = true;
                }
                
                if (shouldStop && _controller?.IsConnected == true)
                {
                    await _controller.StopJogAsync();
                }
                
                if (touchDevice != null)
                {
                    button.ReleaseTouchCapture(touchDevice);
                }
            }
            catch (Exception ex)
            {
                _controller?.AddLogMessage($"> ❌ HATA: Touch jog durdurma - {ex.Message}");
                _isXYZJoggingActive = false;
                _isAJoggingActive = false;
            }
        }

        private bool IsXYZButton(string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName)) return false;
            
            return buttonName.Contains("JogX") || 
                   buttonName.Contains("JogY") || 
                   buttonName.Contains("JogZ") ||
                   buttonName.Equals("btnJogXplus") ||
                   buttonName.Equals("btnJogXminus") ||
                   buttonName.Equals("btnJogYplus") ||
                   buttonName.Equals("btnJogYminus") ||
                   buttonName.Equals("btnJogZplus") ||
                   buttonName.Equals("btnJogZminus");
        }

        private bool IsAAxisButton(string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName)) return false;
            
            return buttonName.Contains("JogA") ||
                   buttonName.Equals("btnJogAplus") ||
                   buttonName.Equals("btnJogAminus");
        }

        // A-axis step mode değiştiğinde jogging state'ini sıfırla
        public void ResetAJoggingState()
        {
            _isAJoggingActive = false;
        }
        
        // XYZ step mode değiştiğinde jogging state'ini sıfırla
        public void ResetXYZJoggingState()
        {
            _isXYZJoggingActive = false;
        }

        #endregion
    }
}