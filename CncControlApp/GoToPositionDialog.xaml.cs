using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace CncControlApp
{
    /// <summary>
    /// GoToPositionDialog - G00 hızlı konumlandırma komutu için dialog
    /// Kullanıcı X, Y, Z koordinatlarını girerek CNC'yi o pozisyona hareket ettirir
    /// </summary>
    public partial class GoToPositionDialog : Window
    {
        public GoToPositionDialog()
        {
            InitializeComponent();
            
            // İlk textbox'a focus ver
            Loaded += (s, e) => XPositionTextBox.Focus();
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

                // Feed rate (opsiyonel)
                int? feedRate = null;
                if (!string.IsNullOrWhiteSpace(FeedRateTextBox.Text))
                {
                    if (int.TryParse(FeedRateTextBox.Text, out int f) && f > 0)
                    {
                        feedRate = f;
                    }
                }

                // G00 komutu oluştur (hızlı hareket)
                string gcode = FormatG00Command(x, y, z, feedRate);

                App.MainController.AddLogMessage($"> 📍 Go To: {gcode}");

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
        /// G00 komutunu formatla
        /// </summary>
        private string FormatG00Command(double x, double y, double z, int? feedRate)
        {
            // Invariant culture ile formatla (nokta kullan)
            string xStr = x.ToString("F3", CultureInfo.InvariantCulture);
            string yStr = y.ToString("F3", CultureInfo.InvariantCulture);
            string zStr = z.ToString("F3", CultureInfo.InvariantCulture);

            string cmd = $"G00 X{xStr} Y{yStr} Z{zStr}";

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
    }
}
