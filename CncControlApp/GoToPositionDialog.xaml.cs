using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CncControlApp
{
    /// <summary>
    /// GoToPositionDialog - G00 hızlı konumlandırma komutu için dialog
    /// Kullanıcı X, Y, Z koordinatlarını girerek CNC'yi o pozisyona hareket ettirir
    /// LOCAL (Work) veya MACHINE koordinat sistemini seçebilir
    /// </summary>
    public partial class GoToPositionDialog : Window
    {
        private bool _useLocalCoordinates = true;
        private double _maxFeedRate = 6000; // Default rapid rate
        private TextBox _lastFocusedTextBox; // Track last focused textbox for keypad
        
        // Renkler
        private static readonly SolidColorBrush LocalColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
        private static readonly SolidColorBrush LocalBgColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D3D2D"));
        private static readonly SolidColorBrush MachineColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF42A5F5"));
        private static readonly SolidColorBrush MachineBgColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D2D3D"));
        private static readonly SolidColorBrush InactiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF888888"));

        public GoToPositionDialog()
        {
            InitializeComponent();
            
            // Mevcut koordinatları yükle
            Loaded += (s, e) => 
            {
                InitializeFeedRateSlider();
                LoadCurrentCoordinates();
                
                // Set up focus tracking for keypad
                XPositionTextBox.GotFocus += TextBox_GotFocus;
                YPositionTextBox.GotFocus += TextBox_GotFocus;
                ZPositionTextBox.GotFocus += TextBox_GotFocus;
                
                XPositionTextBox.Focus();
                XPositionTextBox.SelectAll();
                _lastFocusedTextBox = XPositionTextBox;
            };
        }
        
        /// <summary>
        /// Track which textbox was last focused for keypad input
        /// </summary>
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                _lastFocusedTextBox = tb;
            }
        }

        /// <summary>
        /// Feed rate slider'ı başlat - max değeri rapid rate'den al
        /// </summary>
        private void InitializeFeedRateSlider()
        {
            // Makine ayarlarından rapid rate'leri al ($110=X, $111=Y, $112=Z)
            _maxFeedRate = GetMinRapidRate();
            
            // Slider'ı ayarla
            FeedRateSlider.Minimum = 0;
            FeedRateSlider.Maximum = _maxFeedRate;
            FeedRateSlider.Value = _maxFeedRate / 2; // Orta nokta default
            
            // Değeri güncelle
            UpdateFeedValueText();
        }

        /// <summary>
        /// En düşük rapid rate'i al (X, Y, Z arasından)
        /// </summary>
        private double GetMinRapidRate()
        {
            double defaultRate = 6000;
            try
            {
                var settings = App.MainController?.Settings;
                if (settings == null) return defaultRate;

                // $110=X rapid, $111=Y rapid, $112=Z rapid
                double xRapid = GetSettingValue(settings, 110, defaultRate);
                double yRapid = GetSettingValue(settings, 111, defaultRate);
                double zRapid = GetSettingValue(settings, 112, 3000);

                // En düşük XY rapid kullan
                return Math.Min(xRapid, yRapid);
            }
            catch
            {
                return defaultRate;
            }
        }

        private double GetSettingValue(System.Collections.ObjectModel.ObservableCollection<GCodeSetting> settings, int id, double defaultValue)
        {
            var setting = settings?.FirstOrDefault(s => s.Id == id);
            if (setting != null && double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return Math.Max(100, v);
            return defaultValue;
        }

        private void FeedRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateFeedValueText();
        }

        private void UpdateFeedValueText()
        {
            if (FeedValueText == null || FeedRateSlider == null) return;
            
            int feedValue = (int)FeedRateSlider.Value;
            FeedValueText.Text = $"{feedValue} mm/min";
        }

        /// <summary>
        /// Mevcut koordinatları textbox'lara yükle
        /// </summary>
        private void LoadCurrentCoordinates()
        {
            var mStatus = App.MainController?.MStatus;
            if (mStatus == null) return;

            if (_useLocalCoordinates)
            {
                XPositionTextBox.Text = mStatus.WorkX.ToString("F3", CultureInfo.InvariantCulture);
                YPositionTextBox.Text = mStatus.WorkY.ToString("F3", CultureInfo.InvariantCulture);
                ZPositionTextBox.Text = mStatus.WorkZ.ToString("F3", CultureInfo.InvariantCulture);
            }
            else
            {
                XPositionTextBox.Text = mStatus.X.ToString("F3", CultureInfo.InvariantCulture);
                YPositionTextBox.Text = mStatus.Y.ToString("F3", CultureInfo.InvariantCulture);
                ZPositionTextBox.Text = mStatus.Z.ToString("F3", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Renkleri güncelle (LOCAL yeşil, MACHINE mavi)
        /// </summary>
        private void UpdateColors()
        {
            if (_useLocalCoordinates)
            {
                // LOCAL aktif - yeşil
                LocalToggle.IsChecked = true;
                LocalToggle.Foreground = LocalColor;
                MachineToggle.IsChecked = false;
                MachineToggle.Foreground = InactiveColor;

                // Koordinat kutuları yeşil
                SetCoordinateColors(LocalColor, LocalBgColor);
            }
            else
            {
                // MACHINE aktif - mavi
                LocalToggle.IsChecked = false;
                LocalToggle.Foreground = InactiveColor;
                MachineToggle.IsChecked = true;
                MachineToggle.Foreground = MachineColor;

                // Koordinat kutuları mavi
                SetCoordinateColors(MachineColor, MachineBgColor);
            }
        }

        private void SetCoordinateColors(SolidColorBrush foreground, SolidColorBrush background)
        {
            // X - Label text stays white, only border/background changes
            XLabel.Background = background;
            XLabel.BorderBrush = foreground;
            XPositionTextBox.Foreground = foreground;
            XPositionTextBox.BorderBrush = foreground;
            XPositionTextBox.Background = background;
            XPositionTextBox.CaretBrush = foreground;

            // Y - Label text stays white, only border/background changes
            YLabel.Background = background;
            YLabel.BorderBrush = foreground;
            YPositionTextBox.Foreground = foreground;
            YPositionTextBox.BorderBrush = foreground;
            YPositionTextBox.Background = background;
            YPositionTextBox.CaretBrush = foreground;

            // Z - Label text stays white, only border/background changes
            ZLabel.Background = background;
            ZLabel.BorderBrush = foreground;
            ZPositionTextBox.Foreground = foreground;
            ZPositionTextBox.BorderBrush = foreground;
            ZPositionTextBox.Background = background;
            ZPositionTextBox.CaretBrush = foreground;
        }

        private void LocalToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_useLocalCoordinates)
            {
                _useLocalCoordinates = true;
                UpdateColors();
                LoadCurrentCoordinates();
            }
            else
            {
                // Zaten seçili, tekrar seç
                LocalToggle.IsChecked = true;
            }
        }

        private void MachineToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_useLocalCoordinates)
            {
                _useLocalCoordinates = false;
                UpdateColors();
                LoadCurrentCoordinates();
            }
            else
            {
                // Zaten seçili, tekrar seç
                MachineToggle.IsChecked = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                GoButton_Click(sender, e);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void GoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Bağlantı kontrolü
                if (App.MainController == null || !App.MainController.IsConnected)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil!");
                    MessageBox.Show("CNC bağlı değil!", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Değerleri parse et
                if (!TryParsePosition(XPositionTextBox.Text, out double x))
                {
                    ShowError("X pozisyonu geçersiz!");
                    XPositionTextBox.Focus();
                    return;
                }

                if (!TryParsePosition(YPositionTextBox.Text, out double y))
                {
                    ShowError("Y pozisyonu geçersiz!");
                    YPositionTextBox.Focus();
                    return;
                }

                if (!TryParsePosition(ZPositionTextBox.Text, out double z))
                {
                    ShowError("Z pozisyonu geçersiz!");
                    ZPositionTextBox.Focus();
                    return;
                }

                // Feed rate from slider
                int? feedRate = null;
                int sliderValue = (int)FeedRateSlider.Value;
                if (sliderValue > 0)
                {
                    feedRate = sliderValue;
                }

                // G53 (machine) veya G00 (work) komutu oluştur
                string gcode = FormatGCodeCommand(x, y, z, feedRate);

                string coordType = _useLocalCoordinates ? "LOCAL" : "MACHINE";
                App.MainController.AddLogMessage($"> 📍 Go To ({coordType}): {gcode}");

                // Komutu gönder
                bool success = await App.MainController.SendGCodeCommandAsync(gcode);

                if (success)
                {
                    App.MainController.AddLogMessage($"> ✅ Hareket komutu gönderildi");
                    Close();
                }
                else
                {
                    App.MainController.AddLogMessage($"> ❌ Hareket komutu gönderilemedi");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Go To hatası: {ex.Message}");
                ErrorLogger.LogError($"GoToPositionDialog.GoButton_Click", ex);
            }
        }

        /// <summary>
        /// Pozisyon değerini parse et (hem virgül hem nokta destekle)
        /// </summary>
        private bool TryParsePosition(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Türkçe ve İngilizce format desteği
            string normalized = text.Trim().Replace(',', '.');
            
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// G-Code komutunu formatla
        /// LOCAL: G00 (work coordinates)
        /// MACHINE: G53 G00 (machine coordinates)
        /// </summary>
        private string FormatGCodeCommand(double x, double y, double z, int? feedRate)
        {
            // Invariant culture ile formatla (nokta kullan)
            string xStr = x.ToString("F3", CultureInfo.InvariantCulture);
            string yStr = y.ToString("F3", CultureInfo.InvariantCulture);
            string zStr = z.ToString("F3", CultureInfo.InvariantCulture);

            string cmd;
            if (_useLocalCoordinates)
            {
                // Work koordinatları - G00
                cmd = $"G00 X{xStr} Y{yStr} Z{zStr}";
            }
            else
            {
                // Machine koordinatları - G53 G00
                cmd = $"G53 G00 X{xStr} Y{yStr} Z{zStr}";
            }

            // Feed rate varsa ekle
            if (feedRate.HasValue)
            {
                cmd += $" F{feedRate.Value}";
            }

            return cmd;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Giriş Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Keypad button click handler
        /// </summary>
        private void KeypadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string value = btn.Content?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    InsertTextAtCursor(value);
                }
            }
        }

        /// <summary>
        /// Clear button - clears the focused textbox
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFocusedTextBox != null)
            {
                _lastFocusedTextBox.Clear();
                _lastFocusedTextBox.Focus();
            }
        }

        /// <summary>
        /// Backspace button - deletes character before cursor
        /// </summary>
        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFocusedTextBox != null && _lastFocusedTextBox.Text.Length > 0)
            {
                int cursorPos = _lastFocusedTextBox.SelectionStart;
                if (_lastFocusedTextBox.SelectionLength > 0)
                {
                    // Delete selection
                    _lastFocusedTextBox.SelectedText = "";
                }
                else if (cursorPos > 0)
                {
                    // Delete character before cursor
                    _lastFocusedTextBox.Text = _lastFocusedTextBox.Text.Remove(cursorPos - 1, 1);
                    _lastFocusedTextBox.SelectionStart = cursorPos - 1;
                }
                _lastFocusedTextBox.Focus();
            }
        }

        /// <summary>
        /// Insert text at cursor position in focused textbox
        /// </summary>
        private void InsertTextAtCursor(string text)
        {
            if (_lastFocusedTextBox != null)
            {
                int cursorPos = _lastFocusedTextBox.SelectionStart;
                
                // If there's a selection, replace it
                if (_lastFocusedTextBox.SelectionLength > 0)
                {
                    _lastFocusedTextBox.SelectedText = text;
                    cursorPos = _lastFocusedTextBox.SelectionStart;
                }
                else
                {
                    // Insert at cursor position
                    _lastFocusedTextBox.Text = _lastFocusedTextBox.Text.Insert(cursorPos, text);
                    _lastFocusedTextBox.SelectionStart = cursorPos + text.Length;
                }
                
                _lastFocusedTextBox.Focus();
            }
        }
    }
}
