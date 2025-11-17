using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace CncControlApp.Handlers
{
    public class ControlButtonHandler
    {
        private readonly MainControll _controller;

        public ControlButtonHandler(MainControll controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public async Task HandleSpindleButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;
                        bool success = await _controller.ToggleSpindleAsync(isOn);

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

        public async Task HandleCoolantButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;
                        bool success = await _controller.ToggleCoolantAsync(isOn);

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

        public async Task HandleMistButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;
                        bool success = await _controller.ToggleMistAsync(isOn);

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

        public async Task HandleLightsButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;
                        bool success = await _controller.ToggleLightsAsync(isOn);

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

        public async Task HandleToolChangeButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;

                        if (isOn)
                        {
                            // ✅ Tool change state'ini güncelle
                            _controller.IsToolChangeOn = true;

                            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("G53 G00 Z0");
                            if (success)
                            {
                                _controller.AddLogMessage("> Tool change pozisyonuna gidiliyor...");
                                success = await _controller.SendGCodeCommandWithConfirmationAsync("G53 G00 X0 Y0");
                                if (success)
                                {
                                    _controller.AddLogMessage("> Tool change pozisyonuna varıldı");
                                }
                                else
                                {
                                    button.IsChecked = false;
                                    _controller.IsToolChangeOn = false;
                                    _controller.AddLogMessage("> HATA: Tool change pozisyonuna gidilemedi");
                                }
                            }
                            else
                            {
                                button.IsChecked = false;
                                _controller.IsToolChangeOn = false;
                                _controller.AddLogMessage("> HATA: Tool change pozisyonuna gidilemedi");
                            }
                        }
                        else
                        {
                            _controller.IsToolChangeOn = false;
                            _controller.AddLogMessage("> Tool change işlemi iptal edildi");
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = $"> HATA: Tool change kontrolü - {ex.Message}";
                    _controller.AddLogMessage(errorMessage);
                    if (sender is ToggleButton button)
                    {
                        button.IsChecked = !button.IsChecked;
                        _controller.IsToolChangeOn = button.IsChecked ?? false;
                    }
                }
            }
            else
            {
                _controller?.AddLogMessage("> CNC makinesi bağlı değil!");
                if (sender is ToggleButton button)
                {
                    button.IsChecked = false;
                    _controller.IsToolChangeOn = false;
                }
            }
        }

        public async Task HandleProbeButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;

                        if (isOn)
                        {
                            // Advanced probe sequence is disabled/removed
                            _controller.IsProbeOn = false;
                            button.IsChecked = false;
                            _controller.AddLogMessage("> ℹ️ Gelişmiş Probe sekansı devre dışı bırakıldı. Lütfen ana penceredeki Z Probe düğmesini kullanın.");
                            return;
                        }
                        else
                        {
                            // ✅ Probe işlemi manuel olarak durduruldu
                            _controller.IsProbeOn = false;
                            _controller.AddLogMessage("> ⏹️ Probe işlemi durduruldu");

                            // ✅ Feed hold gönder (probe işlemini iptal etmek için)
                            await _controller.SendGCodeCommandAsync("!");
                            _controller.AddLogMessage("> 🛑 Probe işlemi iptal edildi");

                            // Absolut moda geri dön
                            await Task.Delay(200);
                            await _controller.SendGCodeCommandAsync("G90");
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = $"> ❌ HATA: Probe kontrolü - {ex.Message}";
                    _controller.AddLogMessage(errorMessage);
                    if (sender is ToggleButton button)
                    {
                        button.IsChecked = !button.IsChecked;
                        _controller.IsProbeOn = button.IsChecked ?? false;
                    }

                    // Hata durumunda absolut moda geri dön
                    try
                    {
                        await _controller.SendGCodeCommandAsync("G90");
                    }
                    catch { /* Silent fail */ }
                }
            }
            else
            {
                _controller?.AddLogMessage("> ❌ CNC makinesi bağlı değil!");
                if (sender is ToggleButton button)
                {
                    button.IsChecked = false;
                    _controller.IsProbeOn = false;
                }
            }
        }

        /// <summary>
        /// Advanced probe sequence is intentionally disabled; keep a safe stub for compatibility.
        /// </summary>
        private async Task<bool> PerformAdvancedProbeSequence()
        {
            _controller?.AddLogMessage("> ℹ️ (Devre dışı) Gelişmiş Probe sekansı kaldırıldı.");
            await Task.Delay(10);
            return false;
        }

        /// <summary>
        /// Rölatif mod aktif et
        /// </summary>
        private async Task<bool> SetRelativeMode()
        {
            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("G91");
            if (success)
            {
                await Task.Delay(200);
                _controller.AddLogMessage("> ⚙️ Rölatif mod aktif");
            }
            else
            {
                _controller.AddLogMessage("> ❌ Rölatif mod ayarlanamadı");
            }
            return success;
        }

        /// <summary>
        /// Absolut mod aktif et
        /// </summary>
        private async Task<bool> SetAbsoluteMode()
        {
            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("G90");
            if (success)
            {
                await Task.Delay(200);
                _controller.AddLogMessage("> ⚙️ Absolut mod aktif");
            }
            else
            {
                _controller.AddLogMessage("> ❌ Absolut mod ayarlanamadı");
            }
            return success;
        }

        public async Task HandleVacuumButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;

                        if (isOn)
                        {
                            // ✅ Vacuum state'ini güncelle
                            _controller.IsVacuumOn = true;

                            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("M42 P12 S255");
                            if (success)
                            {
                                _controller.AddLogMessage("> Vacuum açıldı");
                            }
                            else
                            {
                                button.IsChecked = false;
                                _controller.IsVacuumOn = false;
                                _controller.AddLogMessage("> HATA: Vacuum açılamadı");
                            }
                        }
                        else
                        {
                            _controller.IsVacuumOn = false;

                            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("M42 P12 S0");
                            if (success)
                            {
                                _controller.AddLogMessage("> Vacuum kapatıldı");
                            }
                            else
                            {
                                button.IsChecked = true;
                                _controller.IsVacuumOn = true;
                                _controller.AddLogMessage("> HATA: Vacuum kapatılamadı");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = $"> HATA: Vacuum kontrolü - {ex.Message}";
                    _controller.AddLogMessage(errorMessage);
                    if (sender is ToggleButton button)
                    {
                        button.IsChecked = !button.IsChecked;
                        _controller.IsVacuumOn = button.IsChecked ?? false;
                    }
                }
            }
            else
            {
                _controller?.AddLogMessage("> CNC makinesi bağlı değil!");
                if (sender is ToggleButton button)
                {
                    button.IsChecked = false;
                    _controller.IsVacuumOn = false;
                }
            }
        }

        public async Task HandleAirBlastButtonClick(object sender, RoutedEventArgs e)
        {
            if (_controller?.IsConnected == true)
            {
                try
                {
                    if (sender is ToggleButton button)
                    {
                        bool isOn = button.IsChecked ?? false;

                        if (isOn)
                        {
                            // ✅ Air Blast state'ini güncelle
                            _controller.IsAirBlastOn = true;

                            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("M42 P13 S255");
                            if (success)
                            {
                                _controller.AddLogMessage("> Air blast açıldı");
                            }
                            else
                            {
                                button.IsChecked = false;
                                _controller.IsAirBlastOn = false;
                                _controller.AddLogMessage("> HATA: Air blast açılamadı");
                            }
                        }
                        else
                        {
                            _controller.IsAirBlastOn = false;

                            bool success = await _controller.SendGCodeCommandWithConfirmationAsync("M42 P13 S0");
                            if (success)
                            {
                                _controller.AddLogMessage("> Air blast kapatıldı");
                            }
                            else
                            {
                                button.IsChecked = true;
                                _controller.IsAirBlastOn = true;
                                _controller.AddLogMessage("> HATA: Air blast kapatılamadı");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = $"> HATA: Air blast kontrolü - {ex.Message}";
                    _controller.AddLogMessage(errorMessage);
                    if (sender is ToggleButton button)
                    {
                        button.IsChecked = !button.IsChecked;
                        _controller.IsAirBlastOn = button.IsChecked ?? false;
                    }
                }
            }
            else
            {
                _controller?.AddLogMessage("> CNC makinesi bağlı değil!");
                if (sender is ToggleButton button)
                {
                    button.IsChecked = false;
                    _controller.IsAirBlastOn = false;
                }
            }
        }
    }
}