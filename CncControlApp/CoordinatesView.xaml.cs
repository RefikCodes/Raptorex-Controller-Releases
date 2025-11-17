// CoordinatesView.xaml.cs
using System;
using System.Diagnostics; // 🆕 for DIAG logs
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CncControlApp.Controls;

namespace CncControlApp
{
    public partial class CoordinatesView : UserControl
    {
        private bool _isUpdatingFromESP = false;
        private NumberPadPopup _currentNumberPad = null;
        private TextBox _sourceTextBox = null;
        private bool _isCompactMode = false;

        // 🆕 DIAG: per-axis button press timestamp
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _diagPressTimeByAxis =
            new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // Numeric input validation için regex pattern
        private static readonly Regex _numericRegex = new Regex(@"^-?\d*\.?\d*$");

        public CoordinatesView()
        {
            InitializeComponent();
            // 🆕 REMOVED: InitializeCoordinatesView() çağrısı kaldırıldı

            // Global MainController instance'ını kullan
            this.DataContext = App.MainController;

            // ESP'den gelen koordinat güncellemelerini dinle
            if (App.MainController?.MStatus != null)
            {
                App.MainController.MStatus.PropertyChanged += MStatus_PropertyChanged;
            }

            // Add GotFocus event handlers for coordinate textboxes
            TextBoxX.GotFocus += CoordinateTextBox_GotFocus;
            TextBoxY.GotFocus += CoordinateTextBox_GotFocus;
            TextBoxZ.GotFocus += CoordinateTextBox_GotFocus;
            TextBoxA.GotFocus += CoordinateTextBox_GotFocus;

            // Add LostFocus event handlers to close popup if needed
            TextBoxX.LostFocus += CoordinateTextBox_LostFocus;
            TextBoxY.LostFocus += CoordinateTextBox_LostFocus;
            TextBoxZ.LostFocus += CoordinateTextBox_LostFocus;
            TextBoxA.LostFocus += CoordinateTextBox_LostFocus;

            // UserControl'ün kendisini focusable yap
            this.Focusable = true;
        }

        /// <summary>
        /// Compact mode'u aktif/inaktif eder
        /// Compact mode'da Machine Coordinates paneli gizlenir ve CoordinatesView küçülür
        /// </summary>
        public void SetCompactMode(bool isCompact)
        {
            try
            {
                _isCompactMode = isCompact;
                
                // Grid row definition'larını güncelle
                var grid = this.FindName("MainGrid") as Grid;
                if (grid != null)
                {
                    if (isCompact)
                    {
                        // Compact mode: Sadece Local Coordinates görünür
                        grid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); // Local panel
                        grid.RowDefinitions[1].Height = new GridLength(0); // Machine panel gizli
                        
                        // Local panel border'ını da küçült
                        var localBorder = grid.FindName("LocalCoordinatesBorder") as Border;
                        if (localBorder != null)
                        {
                            localBorder.Margin = new Thickness(5, 5, 5, 5);
                        }
                        
                        App.MainController?.AddLogMessage("> 📏 CoordinatesView compact mode aktif - Machine panel gizlendi");
                    }
                    else
                    {
                        // Normal mode: Her iki panel görünür
                        grid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); // Local panel
                        grid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star); // Machine panel
                        
                        // Local panel border'ı normal boyuta döndür
                        var localBorder = grid.FindName("LocalCoordinatesBorder") as Border;
                        if (localBorder != null)
                        {
                            localBorder.Margin = new Thickness(5, 5, 5, 5);
                        }
                        
                        App.MainController?.AddLogMessage("> 📏 CoordinatesView normal mode aktif - Machine panel görünür");
                    }
                }

                // Machine Coordinates border'ının visibility'sini ayarla
                var machineBorder = this.FindName("MachineCoordinatesBorder") as Border;
                if (machineBorder != null)
                {
                    machineBorder.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ HATA: Compact mode ayarlanırken hata - {ex.Message}");
            }
        }

        /// <summary>
        /// Compact mode durumunu döndürür
        /// </summary>
        public bool IsCompactMode => _isCompactMode;

        #region Number Pad Event Handlers

        // Koşulu basitleştirmek için yardımcı property
        private bool IsExecutionLocked =>
            (App.MainController?.IsGCodeRunning ?? false) ||
            (App.MainController?.CanContinueExecution ?? false);

        private void CoordinateTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // RUN veya HOLD iken numpad açmayı engelle
                if (IsExecutionLocked)
                {
                    App.MainController?.AddLogMessage("> ⏳ Koordinat girişi kilitli (Run/Hold) – numpad açılmadı");
                    ClearSourceTextBoxFocus();
                    return;
                }

                if (sender is TextBox textBox && !_isUpdatingFromESP)
                {
                    // Mevcut numpad varsa engelle
                    if (NumberPadPopup.IsAnyNumberPadActive)
                    {
                        textBox.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ClearSourceTextBoxFocus();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                        return;
                    }

                    // Her ihtimale karşı kapat
                    CloseNumberPad();

                    _sourceTextBox = textBox;

                    // Get axis name from TextBox Tag property
                    string axisName = textBox.Tag?.ToString() ?? "X";

                    // Get current TextBox value to show in NumberPad
                    string currentValue = textBox.Text;
                    if (string.IsNullOrWhiteSpace(currentValue))
                        currentValue = "0";

                    // Create and show new number pad with current TextBox value
                    _currentNumberPad = new NumberPadPopup(currentValue, axisName, textBox);
                    _currentNumberPad.PositionNear(textBox);

                    // Handle the popup closing
                    _currentNumberPad.Closed += (s, args) =>
                    {
                        if (_currentNumberPad.DialogResultValue)
                        {
                            // User pressed G00 - the movement was executed
                            // Update the text box with the target position
                            textBox.Text = _currentNumberPad.EnteredValue;

                            // Trigger TextChanged event manually if needed
                            CoordinateTextBox_TextChanged(textBox, null);
                        }

                        _currentNumberPad = null;

                        // Focus'u kesinlikle temizle
                        ClearSourceTextBoxFocus();
                    };

                    _currentNumberPad.Show();

                    App.MainController?.AddLogMessage($"> 📟 {axisName} ekseni için numpad açıldı");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Number pad açılamadı - {ex.Message}");
            }
        }

        private void CoordinateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Don't close immediately on LostFocus as user might be clicking on the number pad
            // NumberPad will handle closing itself when G00/Cancel is pressed
        }

        public void CloseActiveNumberPad()
        {
            // Dışarıdan güvenli kapatma
            CloseNumberPad();
            ClearSourceTextBoxFocus();
        }

        private void CloseNumberPad()
        {
            try
            {
                if (_currentNumberPad != null)
                {
                    _currentNumberPad.Close();
                    _currentNumberPad = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing number pad: {ex.Message}");
            }
        }

        private void ClearSourceTextBoxFocus()
        {
            try
            {
                // Hemen focus temizle
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 1. Keyboard focus'u tamamen temizle
                        Keyboard.ClearFocus();

                        // 2. Source TextBox'tan focus'u kaldır
                        if (_sourceTextBox != null)
                        {
                            _sourceTextBox.IsEnabled = false;
                            _sourceTextBox.IsEnabled = true;
                        }

                        // 3. Bu UserControl'e focus ver
                        this.Focus();

                        // 4. Source TextBox referansını temizle
                        _sourceTextBox = null;

                        System.Diagnostics.Debug.WriteLine("Focus cleared successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in focus clearing action: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing TextBox focus: {ex.Message}");
            }
        }

        #endregion

        #region Numeric Input Validation

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // RUN/HOLD veya NumberPad aktifken klavye girişini blokla
            if (IsExecutionLocked || NumberPadPopup.IsAnyNumberPadActive)
            {
                e.Handled = true;
                return;
            }

            TextBox textBox = sender as TextBox;

            // Girilen karakter eklendiğinde oluşacak text
            string newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);

            // Regex ile kontrol et
            if (!_numericRegex.IsMatch(newText))
            {
                e.Handled = true; // Input'u engelle
                return;
            }

            // Birden fazla nokta kontrolü
            if (newText.Split('.').Length > 2)
            {
                e.Handled = true;
                return;
            }

            // Birden fazla eksi işareti kontrolü (sadece başta olabilir)
            if (newText.IndexOf('-') > 0 || newText.Split('-').Length > 2)
            {
                e.Handled = true;
                return;
            }
        }

        private void NumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // RUN/HOLD veya NumberPad aktifken klavye girişini blokla
            if (IsExecutionLocked || NumberPadPopup.IsAnyNumberPadActive)
            {
                e.Handled = true;
                return;
            }

            // İzin verilen tuşlar
            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Tab ||
                e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Home || e.Key == Key.End ||
                e.Key == Key.Enter || e.Key == Key.Escape)
            {
                return; // Bu tuşlara izin ver
            }

            // Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+X gibi kombinasyonlara izin ver
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.A || e.Key == Key.C || e.Key == Key.V || e.Key == Key.X || e.Key == Key.Z)
                {
                    return;
                }
            }
        }

        #endregion

        private void MStatus_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // ESP'den koordinat güncellemesi geldiğinde text box'ları güncelle
            _isUpdatingFromESP = true;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(App.MainController.MStatus.WorkX):
                            if (!TextBoxX.IsFocused)
                                App.MainController.TargetX = App.MainController.MStatus.WorkX.ToString("F3", CultureInfo.InvariantCulture);
                            LogUiUpdateElapsed("X"); // 🆕 DIAG
                            break;
                        case nameof(App.MainController.MStatus.WorkY):
                            if (!TextBoxY.IsFocused)
                                App.MainController.TargetY = App.MainController.MStatus.WorkY.ToString("F3", CultureInfo.InvariantCulture);
                            LogUiUpdateElapsed("Y"); // 🆕 DIAG
                            break;
                        case nameof(App.MainController.MStatus.WorkZ):
                            if (!TextBoxZ.IsFocused)
                                App.MainController.TargetZ = App.MainController.MStatus.WorkZ.ToString("F3", CultureInfo.InvariantCulture);
                            LogUiUpdateElapsed("Z"); // 🆕 DIAG
                            break;
                        case nameof(App.MainController.MStatus.WorkA):
                            if (!TextBoxA.IsFocused)
                                App.MainController.TargetA = App.MainController.MStatus.WorkA.ToString("F3", CultureInfo.InvariantCulture);
                            LogUiUpdateElapsed("A"); // 🆕 DIAG
                            break;
                    }
                });
            }
            finally
            {
                _isUpdatingFromESP = false;
            }
        }

        // 🆕 DIAG helper: logs elapsed from last button press to this UI update and clears the marker
        private void LogUiUpdateElapsed(string axis)
        {
            try
            {
                if (string.IsNullOrEmpty(axis)) return;

                DateTime t0;
                if (_diagPressTimeByAxis.TryGetValue(axis, out t0))
                {
                    _diagPressTimeByAxis.Remove(axis);
                    var elapsedMs = (DateTime.UtcNow - t0).TotalMilliseconds;
                    Debug.WriteLine($"[DIAG_UI] UIUpdate axis={axis} elapsed={elapsedMs:F0}ms");
                    App.MainController?.AddLogMessage($"> [DIAG_UI] UIUpdate axis={axis} elapsed={elapsedMs:F0} ms");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogUiUpdateElapsed error: {ex.Message}");
            }
        }

        private void CoordinateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ESP'den güncelleme sırasında button state'i güncelleme
            if (_isUpdatingFromESP) return;

            // Manuel değişikliklerde button state'lerini güncelle
            if (sender is TextBox textBox)
            {
                string axis = textBox.Tag?.ToString();
                // Binding üzerinden güncellenecek
            }
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Block button clicks when NumberPad is active
            if (NumberPadPopup.IsAnyNumberPadActive)
            {
                App.MainController?.AddLogMessage("> ⚠️ Numpad aktif iken bu işlem yapılamaz");
                return;
            }

            if (App.MainController?.IsConnected == true)
            {
                try
                {
                    // Hangi butonun tıklandığını öğren
                    Button clickedButton = sender as Button;
                    string axis = clickedButton?.Tag?.ToString();
                    var axisUpper = axis?.ToUpperInvariant();

                    // 🆕 DIAG: mark press
                    if (!string.IsNullOrEmpty(axisUpper))
                    {
                        _diagPressTimeByAxis[axisUpper] = DateTime.UtcNow;
                        Debug.WriteLine($"[DIAG_UI] ButtonPress axis={axisUpper} t={DateTime.UtcNow:O}");
                        App.MainController?.AddLogMessage($"> [DIAG_UI] ButtonPress axis={axisUpper}");
                    }

                    string gcode = "";
                    string axisName = "";

                    // Park pozisyonları: X=0, Y=0, Z=40, A=0
                    switch (axisUpper)
                    {
                        case "X":
                            gcode = "G00 X0";
                            axisName = "X ekseni";
                            break;
                        case "Y":
                            gcode = "G00 Y0";
                            axisName = "Y ekseni";
                            break;
                        case "Z":
                            gcode = "G00 Z40";  // ✅ Z park pozisyonu 40
                            axisName = "Z ekseni";
                            break;
                        case "A":
                            gcode = "G00 A0";
                            axisName = "A ekseni";
                            break;
                        default:
                            App.MainController.LogMessages.Add("> Geçersiz eksen!");
                            return;
                    }

                    App.MainController.LogMessages.Add($"> {axisName} park pozisyonuna gidiyor...");

                    // G00 komutunu gönder
                    bool isSuccessful = await App.MainController.SendGCodeCommandWithConfirmationAsync(gcode);

                    if (isSuccessful)
                    {
                        App.MainController.LogMessages.Add($"> {axisName} park pozisyonuna başarıyla gitti!");
                    }
                    else
                    {
                        App.MainController.LogMessages.Add($"> {axisName} park hareketi başarısız oldu!");
                    }
                }
                catch (Exception ex)
                {
                    App.MainController.LogMessages.Add($"> Park hareketi hatası: {ex.Message}");
                }
            }
            else
            {
                App.MainController.LogMessages.Add("> CNC makinesi bağlı değil!");
            }
        }

        private async void HomeAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Block button clicks when NumberPad is active
            if (NumberPadPopup.IsAnyNumberPadActive)
            {
                App.MainController?.AddLogMessage("> ⚠️ Numpad aktif iken bu işlem yapılamaz");
                return;
            }

            if (App.MainController?.IsConnected == true)
            {
                try
                {
                    App.MainController.LogMessages.Add("> Tüm eksenler home pozisyonuna gidiyor...");

                    // Tüm eksenleri home pozisyonuna götür
                    bool isSuccessful = await App.MainController.HomeAllAsync();

                    if (isSuccessful)
                    {
                        App.MainController.LogMessages.Add("> Tüm eksenler home pozisyonuna başarıyla gitti!");
                    }
                    else
                    {
                        App.MainController.LogMessages.Add("> Tüm eksenler home hareketi başarısız oldu!");
                    }
                }
                catch (Exception ex)
                {
                    App.MainController.LogMessages.Add($"> Home All hareketi hatası: {ex.Message}");
                }
            }
            else
            {
                App.MainController.LogMessages.Add("> CNC makinesi bağlı değil!");
            }
        }

        // GoButton_Click metodu kaldırıldı - artık NumberPad üzerinden G00 işlevselliği sağlanıyor

        private async void SetZeroButton_Click(object sender, RoutedEventArgs e)
        {
            // Block button clicks when NumberPad is active
            if (NumberPadPopup.IsAnyNumberPadActive)
            {
                App.MainController?.AddLogMessage("> ⚠️ Numpad aktif iken bu işlem yapılamaz");
                return;
            }

            if (App.MainController?.IsConnected == true)
            {
                // [DIAG] Measure total elapsed in UI handler (includes send + UI continuation scheduling)
                var swTotal = Stopwatch.StartNew();
                string axisForDiag = null;
                try
                {
                    // Hangi butonun tıklandığını öğren
                    Button clickedButton = sender as Button;
                    string axis = clickedButton?.Tag?.ToString();
                    var axisUpper = axis?.ToUpperInvariant();
                    axisForDiag = axisUpper ?? "?";

                    // 🆕 DIAG: mark the press time for UI update measurement
                    if (!string.IsNullOrEmpty(axisUpper))
                    {
                        _diagPressTimeByAxis[axisUpper] = DateTime.UtcNow;
                        Debug.WriteLine($"[DIAG_UI] ButtonPress axis={axisUpper} t={DateTime.UtcNow:O}");
                        App.MainController?.AddLogMessage($"> [DIAG_UI] ButtonPress axis={axisUpper}");
                    }

                    Debug.WriteLine($"[DIAG] ZeroClick START axis={axisForDiag} t={DateTime.UtcNow:O}");

                    // MainController'daki KALICI sıfırlama metodunu kullan
                    bool isSuccessful;
                    string axisName;
                    
                    switch (axisUpper)
                    {
                        case "X":
                            isSuccessful = await App.MainController.SetZeroXPermanentAsync();
                            axisName = "X ekseni";
                            break;
                        case "Y":
                            isSuccessful = await App.MainController.SetZeroYPermanentAsync();
                            axisName = "Y ekseni";
                            break;
                        case "Z":
                            isSuccessful = await App.MainController.SetZeroZPermanentAsync();
                            axisName = "Z ekseni";
                            break;
                        case "A":
                            isSuccessful = await App.MainController.SetZeroAxisAsync("A", true);
                            axisName = "A ekseni";
                            break;
                        default:
                            App.MainController.LogMessages.Add("> Geçersiz eksen!");
                            Debug.WriteLine($"[DIAG] ZeroClick ABORT axis={axisForDiag} reason=invalid axis");
                            return;
                    }
                    
                    swTotal.Stop();
                    Debug.WriteLine($"[DIAG] ZeroClick END axis={axisForDiag} totalElapsed={swTotal.ElapsedMilliseconds}ms");
                    App.MainController.AddLogMessage($"> [DIAG] ZeroClick axis={axisForDiag} totalElapsed={swTotal.ElapsedMilliseconds} ms");

                    if (isSuccessful)
                    {
                        // Immediate UI feedback: show 0.000 without waiting for next status WCO/WPos
                        ApplyImmediateUiZero(axisUpper);

                        App.MainController.LogMessages.Add($"> {axisName} kalıcı olarak sıfırlandı!");
                    }
                    else
                    {
                        App.MainController.LogMessages.Add($"> {axisName} kalıcı sıfırlama işlemi başarısız oldu!");
                    }
                }
                catch (Exception ex)
                {
                    swTotal.Stop();
                    Debug.WriteLine($"[DIAG] ZeroClick ERROR axis={axisForDiag} totalElapsed={swTotal.ElapsedMilliseconds}ms ex={ex.Message}");
                    App.MainController.LogMessages.Add($"> Beklenmeyen hata: {ex.Message}");
                }
            }
            else
            {
                App.MainController.LogMessages.Add("> CNC makinesi bağlı değil!");
            }
        }

        private async void HomeXYZButton_Click(object sender, RoutedEventArgs e)
        {
            // Block button clicks when NumberPad is active
            if (NumberPadPopup.IsAnyNumberPadActive)
            {
                App.MainController?.AddLogMessage("> ⚠️ Numpad aktif iken bu işlem yapılamaz");
                return;
            }

            if (App.MainController?.IsConnected == true)
            {
                try
                {
                    App.MainController.LogMessages.Add("> X, Y, Z eksenleri park pozisyonuna gidiyor...");
                    App.MainController.LogMessages.Add("> Park pozisyonları: X=0, Y=0, Z=40");

                    // XYZ park pozisyonlarını sırayla gönder - Güvenlik için Z önce yukarı
                    App.MainController.LogMessages.Add("> 1. Z ekseni güvenli yüksekliğe çıkıyor...");
                    bool successZ = await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 Z40");
                    if (!successZ)
                    {
                        App.MainController.LogMessages.Add("> ❌ Z ekseni park hareketi başarısız!");
                        return;
                    }

                    App.MainController.LogMessages.Add("> 2. X ekseni park pozisyonuna gidiyor...");
                    bool successX = await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 X0");
                    if (!successX)
                    {
                        App.MainController.LogMessages.Add("> ❌ X ekseni park hareketi başarısız!");
                        return;
                    }

                    App.MainController.LogMessages.Add("> 3. Y ekseni park pozisyonuna gidiyor...");
                    bool successY = await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 Y0");
                    if (!successY)
                    {
                        App.MainController.LogMessages.Add("> ❌ Y ekseni park hareketi başarısız!");
                        return;
                    }

                    App.MainController.LogMessages.Add("> ✅ X, Y, Z eksenleri park pozisyonuna başarıyla gitti!");
                    App.MainController.LogMessages.Add("> 📍 Park pozisyonu: X=0, Y=0, Z=40");
                }
                catch (Exception ex)
                {
                    App.MainController.LogMessages.Add($"> Park XYZ hareketi hatası: {ex.Message}");
                }
            }
            else
            {
                App.MainController.LogMessages.Add("> CNC makinesi bağlı değil!");
            }
        }

        /// <summary>
        /// FluidNC Machine Home All - $H komutu ile X, Y, Z eksenlerini limit switch'lere götürür
        /// </summary>
        private async void FluidNCHomeAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Block button clicks when NumberPad is active
            if (NumberPadPopup.IsAnyNumberPadActive)
            {
                App.MainController?.AddLogMessage("> ⚠️ Numpad aktif iken bu işlem yapılamaz");
                return;
            }

            if (App.MainController?.IsConnected == true)
            {
                try
                {
                    App.MainController.LogMessages.Add("> 🏠 FluidNC Machine Home All başlatılıyor...");
                    App.MainController.LogMessages.Add("> ⚠️ DİKKAT: X, Y, Z eksenleri limit switch'lere gidecek!");

                    // FluidNC $H komutu - Machine Home All
                    bool isSuccessful = await App.MainController.SendGCodeCommandWithConfirmationAsync("$H");

                    if (isSuccessful)
                    {
                        App.MainController.LogMessages.Add("> ✅ FluidNC Machine Home All tamamlandı!");
                        App.MainController.LogMessages.Add("> 📍 X, Y, Z eksenleri limit switch pozisyonlarında");
                        App.MainController.LogMessages.Add("> 🔧 Machine coordinates (0,0,0) olarak ayarlandı");

                        // Home işlemi sonrası 2 saniye bekle (işlemin tam bitmesi için)
                        await Task.Delay(2000);

                        App.MainController.LogMessages.Add("> 📊 Coordinate sistem güncellemesi tamamlandı");
                    }
                    else
                    {
                        App.MainController.LogMessages.Add("> ❌ HATA: FluidNC Machine Home All başarısız!");
                        App.MainController.LogMessages.Add("> 🔍 Limit switch'leri ve bağlantıları kontrol edin");
                    }
                }
                catch (Exception ex)
                {
                    App.MainController.LogMessages.Add($"> ❌ KRITIK HATA: FluidNC Home işlemi - {ex.Message}");
                    App.MainController.LogMessages.Add("> 🛑 Acil durdurma yapın ve sistem kontrolü edin");
                }
            }
            else
            {
                App.MainController.LogMessages.Add("> ❌ CNC makinesi bağlı değil!");
                App.MainController.LogMessages.Add("> 🔌 Bağlantıyı kontrol edin");
            }
        }

        // Cleanup when the control is unloaded
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseNumberPad();
        }

        // 🆕 DIAG + immediate UI feedback after successful zero
        private void ApplyImmediateUiZero(string axisUpper)
        {
            try
            {
                switch (axisUpper)
                {
                    case "X": App.MainController.TargetX = 0.0.ToString("F3", CultureInfo.InvariantCulture); break;
                    case "Y": App.MainController.TargetY = 0.0.ToString("F3", CultureInfo.InvariantCulture); break;
                    case "Z": App.MainController.TargetZ = 0.0.ToString("F3", CultureInfo.InvariantCulture); break;
                    case "A": App.MainController.TargetA = 0.0.ToString("F3", CultureInfo.InvariantCulture); break;
                    default: return;
                }

                // log elapsed from the press to this immediate UI update
                if (_diagPressTimeByAxis.TryGetValue(axisUpper, out var t0))
                {
                    _diagPressTimeByAxis.Remove(axisUpper);
                    var elapsedMs = (DateTime.UtcNow - t0).TotalMilliseconds;
                    Debug.WriteLine($"[DIAG_UI] ImmediateUIUpdate axis={axisUpper} elapsed={elapsedMs:F0}ms");
                    App.MainController?.AddLogMessage($"> [DIAG_UI] ImmediateUIUpdate axis={axisUpper} elapsed={elapsedMs:F0} ms");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyImmediateUiZero error: {ex.Message}");
            }
        }
    }
}