using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CncControlApp
{
    /// <summary>
    /// Enhanced Console Application Window with Professional QWERTY Touch Keyboard and NumPad
    /// </summary>
    public partial class ConsoleWindow : Window
    {
        public ObservableCollection<string> ConsoleHistory { get; private set; }
        private bool _isKeyboardVisible = false;
        private bool _isShiftPressed = false;
        private bool _isCapsLockOn = false;
        private bool _isCtrlPressed = false;
        private bool _isAltPressed = false;
        private bool _isNumLockOn = true;

        public ConsoleWindow()
        {
            try
            {
                InitializeComponent();

                // Initialize console history
                ConsoleHistory = new ObservableCollection<string>();
                ConsoleOutputListBox.ItemsSource = ConsoleHistory;

                // Safe logging
                SafeLog("> 🖥️ Professional Console window başlatılıyor...");

                // Window ayarları - güvenli şekilde
                SetupWindow();

                // Add welcome messages
                AddConsoleMessage("=== PROFESSIONAL CONSOLE APPLICATION BAŞLATILDI ===");
                AddConsoleMessage($"📅 Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                AddConsoleMessage("⌨️ Professional QWERTY Touch Keyboard + NumPad");
                AddConsoleMessage("🔧 Function keys, modifier keys ve arrow keys desteklenir");
                AddConsoleMessage("📝 Komutlarınızı yazın ve Enter'a basın");
                AddConsoleMessage("📋 Sağ tık ile gelişmiş context menu");
                AddConsoleMessage("💡 help komutu ile tüm komutları görebilirsiniz");
                AddConsoleMessage("");

                SafeLog("> ✅ Professional Console window başarıyla açıldı");
                UpdateStatus("✅ Professional Console Ready - QWERTY + NumPad");
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console window başlatma hatası: {ex.Message}");
                // Hata durumunda da pencereyi göster
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.Show();
            }
        }

        private void SetupWindow()
        {
            try
            {
                // Owner ayarla - güvenli şekilde
                if (Application.Current?.MainWindow != null && Application.Current.MainWindow != this)
                {
                    this.Owner = Application.Current.MainWindow;
                }

                // Temel window ayarları
                this.ShowInTaskbar = true;
                this.WindowState = WindowState.Normal;
                this.Topmost = false;

                // Kiosk mode aktifse console window'u kiosk mode'un altında tut
                var mainWindow = Application.Current?.MainWindow as MainWindow;
                if (mainWindow?.IsKioskMode == true)
                {
                    this.Topmost = false;
                    SafeLog("> 🔧 Console window kiosk mode altında açıldı");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> ⚠️ Window ayarları hatası: {ex.Message}");
            }
        }

        #region Console Methods

        /// <summary>
        /// Add a message to console output
        /// </summary>
        public void AddConsoleMessage(string message)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
                    ConsoleHistory.Add(timestampedMessage);

                    // Auto scroll to bottom
                    if (ConsoleOutputListBox.Items.Count > 0)
                    {
                        ConsoleOutputListBox.ScrollIntoView(ConsoleOutputListBox.Items[ConsoleOutputListBox.Items.Count - 1]);
                    }

                    // Limit history to 1000 items for performance
                    if (ConsoleHistory.Count > 1000)
                    {
                        ConsoleHistory.RemoveAt(0);
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console mesaj ekleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Process console command
        /// </summary>
        private async Task ProcessCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                // Add command to console
                AddConsoleMessage($"cmd> {command}");

                // Update status
                UpdateStatus("⚙️ Komut işleniyor...");

                // Process the command
                string response = await ExecuteCommand(command.Trim().ToLower());

                // Add response to console
                if (!string.IsNullOrEmpty(response))
                {
                    AddConsoleMessage(response);
                }

                UpdateStatus("✅ Professional Console Ready - QWERTY + NumPad");
            }
            catch (Exception ex)
            {
                AddConsoleMessage($"❌ HATA: {ex.Message}");
                UpdateStatus("❌ Komut hatası");
                SafeLog($"> ❌ Komut işleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute console command
        /// </summary>
        private async Task<string> ExecuteCommand(string command)
        {
            await Task.Delay(100); // Simulate processing time

            switch (command)
            {
                case "help":
                case "?":
                    return GetHelpText();

                case "clear":
                case "cls":
                    ConsoleHistory.Clear();
                    return "";

                case "status":
                    return GetSystemStatus();

                case "keyboard":
                case "kbd":
                    return GetKeyboardStatus();

                case "time":
                case "date":
                    return $"📅 Tarih/Saat: {DateTime.Now:yyyy-MM-dd HH:mm:ss dddd}";

                case "version":
                case "ver":
                    return "🏷️ CNC Control App Professional Console v2.0 (QWERTY + NumPad)";

                case "exit":
                case "quit":
                    this.Close();
                    return "👋 Console kapatılıyor...";

                case "test":
                    return "✅ Professional keyboard test başarılı!";

                case "gcode":
                    return GetGCodeInfo();

                case "connection":
                case "conn":
                    return GetConnectionInfo();

                default:
                    if (command.StartsWith("echo "))
                    {
                        return $"📢 {command.Substring(5)}";
                    }
                    else if (command.StartsWith("gcode "))
                    {
                        return await SendGCodeCommand(command.Substring(6));
                    }
                    else
                    {
                        return $"❓ Bilinmeyen komut: '{command}'\n💡 'help' yazarak mevcut komutları görebilirsiniz";
                    }
            }
        }

        private string GetHelpText()
        {
            var help = new StringBuilder();
            help.AppendLine("📖 === PROFESSIONAL CONSOLE KOMUTLARI ===");
            help.AppendLine("help, ?          - Bu yardım metnini gösterir");
            help.AppendLine("clear, cls       - Console ekranını temizler");
            help.AppendLine("status           - Sistem durumunu gösterir");
            help.AppendLine("keyboard, kbd    - Klavye durumunu gösterir");
            help.AppendLine("time, date       - Tarih/saat bilgisini gösterir");
            help.AppendLine("version, ver     - Versiyon bilgisini gösterir");
            help.AppendLine("test             - Klavye test komutu");
            help.AppendLine("gcode            - G-Code durumu hakkında bilgi");
            help.AppendLine("connection, conn - Bağlantı durumu");
            help.AppendLine("echo <metin>     - Metni tekrarlar");
            help.AppendLine("gcode <komut>    - G-Code komutu gönderir");
            help.AppendLine("exit, quit       - Console'u kapatır");
            help.AppendLine("");
            help.AppendLine("⌨️ === KLAVYE ÖZELLİKLERİ ===");
            help.AppendLine("• Professional QWERTY Layout");
            help.AppendLine("• Numeric Keypad (NumPad)");
            help.AppendLine("• Function Keys (F1-F12)");
            help.AppendLine("• Modifier Keys (Shift, Ctrl, Alt)");
            help.AppendLine("• Arrow Keys & Special Keys");
            help.AppendLine("• Caps Lock & Num Lock Support");
            help.AppendLine("");
            help.AppendLine("💡 İPUCU: Dokunmatik klavye ile tüm standart PC fonksiyonları");
            return help.ToString();
        }

        private string GetKeyboardStatus()
        {
            var status = new StringBuilder();
            status.AppendLine("⌨️ === KLAVYE DURUMU ===");
            status.AppendLine($"🔤 Caps Lock: {(_isCapsLockOn ? "AÇIK" : "KAPALI")}");
            status.AppendLine($"🔢 Num Lock: {(_isNumLockOn ? "AÇIK" : "KAPALI")}");
            status.AppendLine($"⇧ Shift: {(_isShiftPressed ? "BASILMIŞ" : "SERBEST")}");
            status.AppendLine($"⌃ Ctrl: {(_isCtrlPressed ? "BASILMIŞ" : "SERBEST")}");
            status.AppendLine($"⌥ Alt: {(_isAltPressed ? "BASILMIŞ" : "SERBEST")}");
            status.AppendLine("");
            status.AppendLine("🔧 Klavye Layout: Professional QWERTY");
            status.AppendLine("🔢 NumPad: Tam Destekli");
            status.AppendLine("⚡ Function Keys: F1-F12 Aktif");
            status.AppendLine("🎯 Arrow Keys: 4 Yönlü Navigasyon");
            return status.ToString();
        }

        private string GetSystemStatus()
        {
            try
            {
                var status = new StringBuilder();
                status.AppendLine("🔍 === SİSTEM DURUMU ===");
                status.AppendLine($"📡 Bağlantı: {(App.MainController?.IsConnected == true ? "✅ Bağlı" : "❌ Bağlı değil")}");
                status.AppendLine($"🔗 Port: {App.MainController?.SelectedPort ?? "Seçili değil"}");
                status.AppendLine($"⚡ Durum: {App.MainController?.MachineStatus ?? "Bilinmiyor"}");
                status.AppendLine($"🎯 Pozisyon: X:{App.MainController?.MStatus?.WorkX:F2} Y:{App.MainController?.MStatus?.WorkY:F2} Z:{App.MainController?.MStatus?.WorkZ:F2}");
                
                if (App.MainController?.IsAAxisAvailable == true)
                {
                    status.AppendLine($"🔄 A Ekseni: {App.MainController?.MStatus?.WorkA:F2}°");
                }
                
                status.AppendLine($"⌨️ Klavye: Professional QWERTY + NumPad");
                status.AppendLine($"🖥️ Console: Enhanced v2.0");
                
                return status.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Durum alınamadı: {ex.Message}";
            }
        }

        private string GetGCodeInfo()
        {
            try
            {
                var info = new StringBuilder();
                info.AppendLine("📝 === G-CODE BİLGİSİ ===");
                info.AppendLine("💡 G-Code komutları göndermek için:");
                info.AppendLine("   gcode G00 X10 Y20 Z5");
                info.AppendLine("   gcode G01 X0 Y0 F1000");
                info.AppendLine("   gcode M3 S12000");
                info.AppendLine("");
                info.AppendLine("📋 Yaygın G-Code komutları:");
                info.AppendLine("   G00 - Hızlı hareket (Rapids)");
                info.AppendLine("   G01 - Doğrusal hareket (Linear)");
                info.AppendLine("   G02/G03 - Çember hareketi (Arc)");
                info.AppendLine("   G28 - Home pozisyonu");
                info.AppendLine("   G90/G91 - Absolute/Relative mode");
                info.AppendLine("   M3/M5 - Spindle açık/kapalı");
                info.AppendLine("   M8/M9 - Coolant açık/kapalı");
                info.AppendLine("");
                info.AppendLine("⌨️ Professional keyboard ile hassas G-Code girişi!");
                return info.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ G-Code bilgisi alınamadı: {ex.Message}";
            }
        }

        private string GetConnectionInfo()
        {
            try
            {
                var info = new StringBuilder();
                info.AppendLine("🔗 === BAĞLANTI BİLGİSİ ===");
                info.AppendLine($"📡 Durum: {(App.MainController?.IsConnected == true ? "✅ Bağlı" : "❌ Bağlı değil")}");
                info.AppendLine($"🔗 Port: {App.MainController?.SelectedPort ?? "Seçili değil"}");
                info.AppendLine($"⚡ Baudrate: 115200 (varsayılan)");
                info.AppendLine($"⏱️ Bağlantı süresi: {(App.MainController?.IsConnected == true ? "Aktif" : "Kapalı")}");
                
                if (App.MainController?.IsConnected == true)
                {
                    var availablePorts = App.MainController.AvailablePorts?.Count ?? 0;
                    info.AppendLine($"🔌 Kullanılabilir port sayısı: {availablePorts}");
                    
                    var settingsCount = App.MainController.Settings?.Count ?? 0;
                    info.AppendLine($"⚙️ Yüklenen setting sayısı: {settingsCount}");
                }
                
                info.AppendLine($"⌨️ Input Method: Professional Touch Keyboard");
                
                return info.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Bağlantı bilgisi alınamadı: {ex.Message}";
            }
        }

        private async Task<string> SendGCodeCommand(string gcode)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    return "❌ CNC makinesine bağlı değilsiniz!";
                }

                AddConsoleMessage($"📤 G-Code gönderiliyor: {gcode}");
                SafeLog($"> Console'dan G-Code gönderiliyor: {gcode}");

                bool success = await App.MainController.SendGCodeCommandWithConfirmationAsync(gcode);

                if (success)
                {
                    return $"✅ G-Code başarıyla gönderildi: {gcode}";
                }
                else
                {
                    return $"❌ G-Code gönderim hatası: {gcode}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ G-Code gönderim hatası: {ex.Message}";
            }
        }

        private void UpdateStatus(string status)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = status;
                });
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Status güncelleme hatası: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers

        private async void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string command = CommandInputTextBox.Text;
                CommandInputTextBox.Clear();
                await ProcessCommand(command);
                e.Handled = true;
            }
        }

        private async void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            string command = CommandInputTextBox.Text;
            CommandInputTextBox.Clear();
            await ProcessCommand(command);
        }

        private void CommandInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Focus olduğunda keyboard'u göster (isteğe bağlı)
            if (!_isKeyboardVisible)
            {
                // Otomatik keyboard açmak istemiyorsanız bu satırı kaldırın
                // ToggleKeyboard();
            }
        }

        private void CommandInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Focus kaybedince otomatik keyboard kapatma (isteğe bağlı)
        }

        private void KeyboardToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleKeyboard();
        }

        private void ToggleKeyboard()
        {
            _isKeyboardVisible = !_isKeyboardVisible;
            TouchKeyboardPanel.Visibility = _isKeyboardVisible ? Visibility.Visible : Visibility.Collapsed;
            KeyboardToggleButton.Content = _isKeyboardVisible ? "⌨️" : "⌨️";

            if (_isKeyboardVisible)
            {
                UpdateStatus("⌨️ Professional QWERTY Keyboard aktif");
                SafeLog("> 🎹 Professional touch keyboard açıldı");
            }
            else
            {
                UpdateStatus("✅ Professional Console Ready - QWERTY + NumPad");
                SafeLog("> 🎹 Professional touch keyboard kapatıldı");
            }
        }

        #endregion

        #region Professional Touch Keyboard Event Handlers

        private void KeyboardKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && !string.IsNullOrEmpty(button.Content?.ToString()))
            {
                string key = button.Content.ToString();
                
                // Apply case transformation based on current state
                if (key.Length == 1 && char.IsLetter(key[0]))
                {
                    if (_isCapsLockOn || _isShiftPressed)
                    {
                        key = key.ToUpper();
                    }
                    else
                    {
                        key = key.ToLower();
                    }
                }
                
                // Apply shift modifications for special characters
                if (_isShiftPressed)
                {
                    key = ApplyShiftTransformation(key);
                }
                
                CommandInputTextBox.Text += key;
                CommandInputTextBox.CaretIndex = CommandInputTextBox.Text.Length;
                CommandInputTextBox.Focus();
                
                // Reset shift state after use (but not caps lock)
                if (_isShiftPressed)
                {
                    _isShiftPressed = false;
                    UpdateModifierKeyStates();
                }
            }
        }

        private string ApplyShiftTransformation(string key)
        {
            switch (key.ToLower())
            {
                case "1": return "!";
                case "2": return "@";
                case "3": return "#";
                case "4": return "$";
                case "5": return "%";
                case "6": return "^";
                case "7": return "&";
                case "8": return "*";
                case "9": return "(";
                case "0": return ")";
                case "-": return "_";
                case "=": return "+";
                case "[": return "{";
                case "]": return "}";
                case "\\": return "|";
                case ";": return ":";
                case "'": return "\"";
                case ",": return "<";
                case ".": return ">";
                case "/": return "?";
                case "`": return "~";
                default: return key;
            }
        }

        private void SpaceKey_Click(object sender, RoutedEventArgs e)
        {
            CommandInputTextBox.Text += " ";
            CommandInputTextBox.CaretIndex = CommandInputTextBox.Text.Length;
            CommandInputTextBox.Focus();
        }

        private void BackspaceKey_Click(object sender, RoutedEventArgs e)
        {
            if (CommandInputTextBox.Text.Length > 0)
            {
                int caretIndex = CommandInputTextBox.CaretIndex;
                if (caretIndex > 0)
                {
                    CommandInputTextBox.Text = CommandInputTextBox.Text.Remove(caretIndex - 1, 1);
                    CommandInputTextBox.CaretIndex = caretIndex - 1;
                }
            }
            CommandInputTextBox.Focus();
        }

        private async void EnterKey_Click(object sender, RoutedEventArgs e)
        {
            string command = CommandInputTextBox.Text;
            CommandInputTextBox.Clear();
            await ProcessCommand(command);
        }

        private void ClearKey_Click(object sender, RoutedEventArgs e)
        {
            CommandInputTextBox.Clear();
            CommandInputTextBox.Focus();
        }

        private void TabKey_Click(object sender, RoutedEventArgs e)
        {
            CommandInputTextBox.Text += "\t";
            CommandInputTextBox.CaretIndex = CommandInputTextBox.Text.Length;
            CommandInputTextBox.Focus();
        }

        private void ShiftKey_Click(object sender, RoutedEventArgs e)
        {
            _isShiftPressed = !_isShiftPressed;
            UpdateModifierKeyStates();
            CommandInputTextBox.Focus();
        }

        private void CapsLockKey_Click(object sender, RoutedEventArgs e)
        {
            _isCapsLockOn = !_isCapsLockOn;
            UpdateModifierKeyStates();
            CommandInputTextBox.Focus();
            
            string status = _isCapsLockOn ? "🔤 CAPS LOCK AKTİF" : "🔤 caps lock pasif";
            UpdateStatus(status);
        }

        private void CtrlKey_Click(object sender, RoutedEventArgs e)
        {
            _isCtrlPressed = !_isCtrlPressed;
            UpdateModifierKeyStates();
            CommandInputTextBox.Focus();
        }

        private void AltKey_Click(object sender, RoutedEventArgs e)
        {
            _isAltPressed = !_isAltPressed;
            UpdateModifierKeyStates();
            CommandInputTextBox.Focus();
        }

        private void WindowsKey_Click(object sender, RoutedEventArgs e)
        {
            AddConsoleMessage("🗔 Windows key pressed - Function simulated");
            CommandInputTextBox.Focus();
        }

        private void NumLockKey_Click(object sender, RoutedEventArgs e)
        {
            _isNumLockOn = !_isNumLockOn;
            UpdateModifierKeyStates();
            CommandInputTextBox.Focus();
            
            string status = _isNumLockOn ? "🔢 NUM LOCK AKTİF" : "🔢 num lock pasif";
            UpdateStatus(status);
        }

        private void FunctionKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string functionKey)
            {
                AddConsoleMessage($"⚡ {functionKey} function key pressed");
                
                // Handle specific function keys
                switch (functionKey)
                {
                    case "F1":
                        AddConsoleMessage("💡 F1: help komutu çalıştırılıyor...");
                        _ = ProcessCommand("help");
                        break;
                    case "F5":
                        AddConsoleMessage("🔄 F5: Console refresh");
                        break;
                    case "F12":
                        AddConsoleMessage("🛠️ F12: Debug mode (simulated)");
                        break;
                }
                
                CommandInputTextBox.Focus();
            }
        }

        private void ArrowKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string direction)
            {
                int caretIndex = CommandInputTextBox.CaretIndex;
                
                switch (direction)
                {
                    case "Left":
                        if (caretIndex > 0)
                            CommandInputTextBox.CaretIndex = caretIndex - 1;
                        break;
                    case "Right":
                        if (caretIndex < CommandInputTextBox.Text.Length)
                            CommandInputTextBox.CaretIndex = caretIndex + 1;
                        break;
                    case "Up":
                        AddConsoleMessage("⬆️ Yukarı ok tuşuna basıldı");
                        break;
                    case "Down":
                        AddConsoleMessage("⬇️ Aşağı ok tuşuna basıldı");
                        break;
                }
                
                CommandInputTextBox.Focus();
            }
        }

        /// <summary>
        /// Updates the visual state of modifier keys based on current state
        /// </summary>
        private void UpdateModifierKeyStates()
        {
            try
            {
                // Log modifier key states
                var modifierStatus = new StringBuilder();
                modifierStatus.Append("⌨️ Modifier keys: ");
                
                if (_isShiftPressed) modifierStatus.Append("SHIFT ");
                if (_isCapsLockOn) modifierStatus.Append("CAPS ");
                if (_isCtrlPressed) modifierStatus.Append("CTRL ");
                if (_isAltPressed) modifierStatus.Append("ALT ");
                if (_isNumLockOn) modifierStatus.Append("NUM ");
                
                if (!_isShiftPressed && !_isCapsLockOn && !_isCtrlPressed && !_isAltPressed)
                {
                    modifierStatus.Append("(normal)");
                }
                
                AddConsoleMessage(modifierStatus.ToString());
                
                // Here you could also update button appearances if needed
                // For example, change button colors to indicate active state
                UpdateModifierButtonAppearances();
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Modifier key state update hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the visual appearance of modifier buttons
        /// </summary>
        private void UpdateModifierButtonAppearances()
        {
            try
            {
                // This method would update button colors/styles to show active state
                // Since we don't have direct references to the buttons, we could use FindName
                // or implement a different approach
                
                // Example implementation (would need actual button references):
                // UpdateButtonAppearance("ShiftButton", _isShiftPressed);
                // UpdateButtonAppearance("CapsLockButton", _isCapsLockOn);
                // UpdateButtonAppearance("CtrlButton", _isCtrlPressed);
                // UpdateButtonAppearance("AltButton", _isAltPressed);
                // UpdateButtonAppearance("NumLockButton", _isNumLockOn);
                
                System.Diagnostics.Debug.WriteLine($"Modifier states - Shift:{_isShiftPressed}, Caps:{_isCapsLockOn}, Ctrl:{_isCtrlPressed}, Alt:{_isAltPressed}, Num:{_isNumLockOn}");
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Button appearance update hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to update individual button appearance
        /// </summary>
        private void UpdateButtonAppearance(string buttonName, bool isActive)
        {
            try
            {
                var button = this.FindName(buttonName) as Button;
                if (button != null)
                {
                    if (isActive)
                    {
                        button.Background = new SolidColorBrush(Colors.Orange);
                        button.Foreground = new SolidColorBrush(Colors.White);
                    }
                    else
                    {
                        // Reset to default appearance
                        button.ClearValue(Button.BackgroundProperty);
                        button.ClearValue(Button.ForegroundProperty);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Button appearance update error for {buttonName}: {ex.Message}");
            }
        }

        #endregion

        #region Context Menu and ListBox Events

        private void ConsoleListBox_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (sender is ListBox listBox)
                {
                    // Ctrl+C - Kopyala
                    if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        CopySelectedConsoleOutput();
                        e.Handled = true;
                    }
                    // Ctrl+A - Tümünü Seç
                    else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        listBox.SelectAll();
                        e.Handled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console listbox klavye işlemi - {ex.Message}");
            }
        }

        private void ConsoleListBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Context menu otomatik açılacak
        }

        private void CopySelectedConsoleOutput_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedConsoleOutput();
        }

        private void SelectAllConsoleOutput_Click(object sender, RoutedEventArgs e)
        {
            ConsoleOutputListBox.SelectAll();
            UpdateStatus($"📋 Tüm console çıktısı seçildi ({ConsoleHistory.Count} satır)");
        }

        private void ClearConsoleOutput_Click(object sender, RoutedEventArgs e)
        {
            ClearConsole();
        }

        private void ExportConsoleOutput_Click(object sender, RoutedEventArgs e)
        {
            ExportConsoleHistory();
        }

        private void CopySelectedConsoleOutput()
        {
            try
            {
                if (ConsoleOutputListBox.SelectedItems.Count == 0)
                {
                    UpdateStatus("⚠️ Kopyalamak için satır seçin");
                    return;
                }

                var selectedText = new StringBuilder();
                foreach (string item in ConsoleOutputListBox.SelectedItems)
                {
                    selectedText.AppendLine(item);
                }

                Clipboard.SetText(selectedText.ToString());
                UpdateStatus($"📋 {ConsoleOutputListBox.SelectedItems.Count} satır kopyalandı");
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console kopyalama hatası: {ex.Message}");
                UpdateStatus("❌ Kopyalama hatası");
            }
        }

        #endregion

        #region Button Event Handlers

        private void ClearConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            ClearConsole();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            ExportConsoleHistory();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SafeLog("> 🖥️ Console window kapatılıyor...");
                this.Close();
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console window kapatma hatası: {ex.Message}");
                try
                {
                    this.Hide();
                }
                catch
                {
                    // Son çare
                }
            }
        }

        private void ClearConsole()
        {
            try
            {
                var result = MessageBox.Show(
                    "Console geçmişini temizlemek istediğinizden emin misiniz?",
                    "Console Temizle",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ConsoleHistory.Clear();
                    AddConsoleMessage("🗑️ Console geçmişi temizlendi");
                    UpdateStatus("✅ Console temizlendi");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console temizleme hatası: {ex.Message}");
                UpdateStatus("❌ Temizleme hatası");
            }
        }

        private void ExportConsoleHistory()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Console Geçmişini Dışa Aktar",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"console_history_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var content = new StringBuilder();
                    content.AppendLine($"=== CNC CONTROL APP CONSOLE HISTORY ===");
                    content.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    content.AppendLine($"Total Lines: {ConsoleHistory.Count}");
                    content.AppendLine("");

                    foreach (string line in ConsoleHistory)
                    {
                        content.AppendLine(line);
                    }

                    File.WriteAllText(saveFileDialog.FileName, content.ToString(), Encoding.UTF8);

                    UpdateStatus($"💾 Console geçmişi dışa aktarıldı: {Path.GetFileName(saveFileDialog.FileName)}");
                    AddConsoleMessage($"💾 Console geçmişi dışa aktarıldı: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Console dışa aktarma hatası: {ex.Message}");
                UpdateStatus("❌ Dışa aktarma hatası");
                AddConsoleMessage($"❌ Dışa aktarma hatası: {ex.Message}");
            }
        }

        private void DeleteKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tüm metni sil
                CommandInputTextBox.Clear();
                CommandInputTextBox.Focus();
                
                AddConsoleMessage("🗑️ Metin alanı tamamen temizlendi");
                UpdateStatus("🗑️ Input temizlendi");
            }
            catch (Exception ex)
            {
                SafeLog($"> ❌ Delete işlemi hatası: {ex.Message}");
            }
        }

        #endregion

        #region Window Event Handlers

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                base.OnClosed(e);
                SafeLog("> ✅ Professional Console window kapatıldı");
            }
            catch (Exception ex)
            {
                SafeLog($"> ⚠️ OnClosed hatası: {ex.Message}");
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            try
            {
                base.OnActivated(e);
                SafeLog("> 📱 Console window aktif edildi");
                UpdateStatus("✅ Professional Console Ready - QWERTY + NumPad");
            }
            catch (Exception ex)
            {
                SafeLog($"> ⚠️ OnActivated hatası: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Güvenli log yazma metodu
        /// </summary>
        private void SafeLog(string message)
        {
            try
            {
                // Async olarak log yaz - UI thread'i bloke etme
                Task.Run(() =>
                {
                    try
                    {
                        App.MainController?.AddLogMessage(message);
                    }
                    catch
                    {
                        // Log yazma hatası olursa sessizce geç
                        System.Diagnostics.Debug.WriteLine($"Console log error: {message}");
                    }
                });
            }
            catch
            {
                // Son çare - debug çıktısı
                System.Diagnostics.Debug.WriteLine($"Console safe log: {message}");
            }
        }

        /// <summary>
        /// Emergency close method - eğer normal close çalışmazsa
        /// </summary>
        public void ForceClose()
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.Hide();
                    this.Close();
                });
            }
            catch
            {
                try
                {
                    this.Close();
                }
                catch
                {
                    // Son çare
                    Environment.Exit(0);
                }
            }
        }

        #endregion
    }
}