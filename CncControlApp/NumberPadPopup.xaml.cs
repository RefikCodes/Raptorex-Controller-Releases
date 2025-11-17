using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CncControlApp.Controls
{
    public partial class NumberPadPopup : Window
    {
        #region Win32 API Imports for Ultimate Topmost Control

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int SW_SHOW = 5;

        #endregion

        // Static property to track if any numpad is active
        public static bool IsAnyNumberPadActive { get; private set; } = false;

        public string EnteredValue { get; private set; }
        public bool DialogResultValue { get; private set; }
        public string TargetAxis { get; private set; }
        private TextBox _sourceTextBox;
        private DispatcherTimer _visibilityTimer;
        private IntPtr _windowHandle;

        public NumberPadPopup(string initialValue = "0", string axis = "X", TextBox sourceTextBox = null)
        {
            InitializeComponent();

            // Set target axis
            TargetAxis = axis?.ToUpper() ?? "X";
            AxisNameTextBlock.Text = TargetAxis;

            // Source TextBox'ı hatırla
            _sourceTextBox = sourceTextBox;

            // Set initial value
            DisplayTextBox.Text = string.IsNullOrWhiteSpace(initialValue) ? "0" : initialValue;
            EnteredValue = DisplayTextBox.Text;
            DialogResultValue = false;

            // CRITICAL: Always start center screen to ensure visibility
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Topmost = true;
            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.Visibility = Visibility.Visible;

            // Set numpad as active
            IsAnyNumberPadActive = true;

            // Notify main window to block UI
            NotifyMainWindowOfNumberPadState(true);

            // Critical event handlers
            this.Loaded += NumberPadPopup_Loaded;
            this.Closed += NumberPadPopup_Closed;
            this.ContentRendered += NumberPadPopup_ContentRendered;

            // Start aggressive visibility timer
            StartVisibilityTimer();

            // LOG for debugging
            App.MainController?.AddLogMessage($"> 🚀 NUMPAD BAŞLATILIYOR - {TargetAxis} ekseni");
        }

        private void NumberPadPopup_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage($"> ⚡ NUMPAD LOADED event tetiklendi");

                // Get window handle
                _windowHandle = new WindowInteropHelper(this).Handle;
                App.MainController?.AddLogMessage($"> 🔧 Window Handle: {_windowHandle}");

                // Force center position first
                CenterOnScreen();

                // Position and force visibility
                SetupWindowForMaximumVisibility();

                this.Focus();
                DisplayTextBox.Focus();

                App.MainController?.AddLogMessage($"> ✅ NUMPAD tam olarak yüklendi ve görünür olmalı");
                App.MainController?.AddLogMessage($"> 📍 Final pozisyon: ({this.Left}, {this.Top})");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ NUMPAD Loaded hatası: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error in NumberPadPopup_Loaded: {ex.Message}");
            }
        }

        private void NumberPadPopup_ContentRendered(object sender, EventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage($"> 🎨 NUMPAD içerik render edildi");

                // Ensure center position after content render
                CenterOnScreen();

                // Final aggressive setup after content is rendered
                SetupWindowForMaximumVisibility();

                App.MainController?.AddLogMessage($"> 📍 ContentRendered pozisyon: ({this.Left}, {this.Top})");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ ContentRendered hatası: {ex.Message}");
            }
        }

        private void NumberPadPopup_Closed(object sender, EventArgs e)
        {
            try
            {
                // Stop the timer
                StopVisibilityTimer();

                // Clear active state
                IsAnyNumberPadActive = false;
                NotifyMainWindowOfNumberPadState(false);

                App.MainController?.AddLogMessage($"> 🔒 {TargetAxis} numpad kapatıldı");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Close hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Centers the window on the primary screen
        /// </summary>
        private void CenterOnScreen()
        {
            try
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                this.Left = (screenWidth - this.Width) / 2;
                this.Top = (screenHeight - this.Height) / 2;

                // Ensure window is within screen bounds
                if (this.Left < 0) this.Left = 0;
                if (this.Top < 0) this.Top = 0;
                if (this.Left + this.Width > screenWidth) this.Left = screenWidth - this.Width;
                if (this.Top + this.Height > screenHeight) this.Top = screenHeight - this.Height;

                App.MainController?.AddLogMessage($"> 🎯 Merkez pozisyon: ({this.Left}, {this.Top}) - Ekran: {screenWidth}x{screenHeight}");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterOnScreen hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up window for maximum visibility using every possible method
        /// </summary>
        private void SetupWindowForMaximumVisibility()
        {
            try
            {
                App.MainController?.AddLogMessage($"> 🔧 SetupWindowForMaximumVisibility başlatıldı");

                if (_windowHandle != IntPtr.Zero)
                {
                    // Show window
                    ShowWindow(_windowHandle, SW_SHOW);
                    App.MainController?.AddLogMessage($"> 👀 ShowWindow çağrıldı");

                    // Set topmost
                    SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
                    App.MainController?.AddLogMessage($"> ⬆️ SetWindowPos TOPMOST çağrıldı");

                    // Bring to top
                    BringWindowToTop(_windowHandle);
                    App.MainController?.AddLogMessage($"> 🔝 BringWindowToTop çağrıldı");

                    // Set foreground
                    SetForegroundWindow(_windowHandle);
                    App.MainController?.AddLogMessage($"> 🎯 SetForegroundWindow çağrıldı");
                }

                // WPF properties
                this.Topmost = true;
                this.Visibility = Visibility.Visible;
                this.WindowState = WindowState.Normal;
                this.Activate();

                App.MainController?.AddLogMessage($"> ✅ Tüm visibility ayarları uygulandı");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ SetupWindowForMaximumVisibility hatası: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error in SetupWindowForMaximumVisibility: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a timer to continuously ensure window visibility
        /// </summary>
        private void StartVisibilityTimer()
        {
            _visibilityTimer = new DispatcherTimer();
            _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500); // Less frequent for stability
            _visibilityTimer.Tick += VisibilityTimer_Tick;
            _visibilityTimer.Start();

            App.MainController?.AddLogMessage($"> ⏰ Visibility timer başlatıldı");
        }

        private void StopVisibilityTimer()
        {
            if (_visibilityTimer != null)
            {
                _visibilityTimer.Stop();
                _visibilityTimer.Tick -= VisibilityTimer_Tick;
                _visibilityTimer = null;

                App.MainController?.AddLogMessage($"> ⏰ Visibility timer durduruldu");
            }
        }

        private void VisibilityTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (this.IsLoaded && _windowHandle != IntPtr.Zero)
                {
                    // Check if window is visible and on top
                    IntPtr foregroundWindow = GetForegroundWindow();

                    if (foregroundWindow != _windowHandle)
                    {
                        // Force visibility again
                        SetupWindowForMaximumVisibility();
                        System.Diagnostics.Debug.WriteLine("NumberPad forced visible by timer");
                    }

                    // Ensure WPF properties
                    if (!this.Topmost || this.Visibility != Visibility.Visible)
                    {
                        this.Topmost = true;
                        this.Visibility = Visibility.Visible;
                        this.Activate();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in visibility timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Notify the main window about numpad state changes
        /// </summary>
        private void NotifyMainWindowOfNumberPadState(bool isActive)
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    if (isActive)
                    {
                        mainWindow.BlockUIForNumberPad();
                    }
                    else
                    {
                        mainWindow.UnblockUIForNumberPad();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error notifying main window: {ex.Message}");
            }
        }

        public void PositionNear(FrameworkElement element)
        {
            try
            {
                App.MainController?.AddLogMessage($"> 📍 PositionNear çağrıldı - ancak merkez konumda kalacak");

                // OVERRIDE: Always keep centered for now to ensure visibility
                CenterOnScreen();

                App.MainController?.AddLogMessage($"> 📍 Merkez pozisyonu korundu: ({this.Left}, {this.Top})");

                // Force visibility after positioning
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetupWindowForMaximumVisibility();
                }), DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ PositionNear hatası: {ex.Message}");
                // Fallback to center screen
                CenterOnScreen();
            }
        }

        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    string number = button.Content.ToString();

                    if (DisplayTextBox.Text == "0")
                    {
                        DisplayTextBox.Text = number;
                    }
                    else
                    {
                        DisplayTextBox.Text += number;
                    }

                    EnteredValue = DisplayTextBox.Text;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NumberButton error: {ex.Message}");
            }
        }

        private void DecimalButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!DisplayTextBox.Text.Contains("."))
                {
                    if (DisplayTextBox.Text == "0")
                    {
                        DisplayTextBox.Text = "0.";
                    }
                    else
                    {
                        DisplayTextBox.Text += ".";
                    }

                    EnteredValue = DisplayTextBox.Text;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DecimalButton error: {ex.Message}");
            }
        }

        private void MinusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!DisplayTextBox.Text.StartsWith("-"))
                {
                    if (DisplayTextBox.Text == "0")
                    {
                        DisplayTextBox.Text = "-";
                    }
                    else
                    {
                        DisplayTextBox.Text = "-" + DisplayTextBox.Text;
                    }

                    EnteredValue = DisplayTextBox.Text;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MinusButton error: {ex.Message}");
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisplayTextBox.Text = "0";
                EnteredValue = DisplayTextBox.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearButton error: {ex.Message}");
            }
        }

        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DisplayTextBox.Text.Length > 1)
                {
                    DisplayTextBox.Text = DisplayTextBox.Text.Substring(0, DisplayTextBox.Text.Length - 1);
                }
                else
                {
                    DisplayTextBox.Text = "0";
                }

                EnteredValue = DisplayTextBox.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackspaceButton error: {ex.Message}");
            }
        }

        private void PlusMinusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DisplayTextBox.Text != "0")
                {
                    if (DisplayTextBox.Text.StartsWith("-"))
                    {
                        DisplayTextBox.Text = DisplayTextBox.Text.Substring(1);
                    }
                    else
                    {
                        DisplayTextBox.Text = "-" + DisplayTextBox.Text;
                    }

                    EnteredValue = DisplayTextBox.Text;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlusMinusButton error: {ex.Message}");
            }
        }

        private async void G00Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate the entered value
                if (!double.TryParse(DisplayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double targetPosition))
                {
                    MessageBox.Show("Lütfen geçerli bir sayı girin.", "Geçersiz Giriş", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DisplayTextBox.Focus();
                    return;
                }

                // Check if CNC is connected
                if (App.MainController?.IsConnected != true)
                {
                    MessageBox.Show("CNC makinesi bağlı değil!", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // Get current position for logging purposes
                    double currentPosition = 0;
                    string axisName = $"{TargetAxis} ekseni";

                    switch (TargetAxis)
                    {
                        case "X":
                            currentPosition = App.MainController.MStatus?.WorkX ?? 0;
                            break;
                        case "Y":
                            currentPosition = App.MainController.MStatus?.WorkY ?? 0;
                            break;
                        case "Z":
                            currentPosition = App.MainController.MStatus?.WorkZ ?? 0;
                            break;
                        case "A":
                            currentPosition = App.MainController.MStatus?.WorkA ?? 0;
                            break;
                    }

                    // Format the target position with 3 decimal places
                    string targetPositionStr = targetPosition.ToString("F3", CultureInfo.InvariantCulture);
                    EnteredValue = targetPositionStr;

                    // Create G00 command for ABSOLUTE position
                    string gcode = $"G00 {TargetAxis}{targetPositionStr}";

                    // Log the movement with detailed information
                    App.MainController.LogMessages.Add($"> {axisName} MUTLAK pozisyon hareketi:");
                    App.MainController.LogMessages.Add($"  Mevcut pozisyon: {currentPosition:F3}");
                    App.MainController.LogMessages.Add($"  Hedef pozisyon: {targetPositionStr}");
                    App.MainController.LogMessages.Add($"  Hareket miktarı: {(targetPosition - currentPosition):F3}");
                    App.MainController.LogMessages.Add($"> G00 komutu: {gcode}");

                    // Set dialog result and close popup
                    DialogResultValue = true;

                    // Clear focus from source TextBox before closing
                    ClearSourceTextBoxFocus();

                    // Close the popup before sending command
                    this.Close();

                    // Send G00 command
                    bool isSuccessful = await App.MainController.SendGCodeCommandWithConfirmationAsync(gcode);

                    if (isSuccessful)
                    {
                        App.MainController.LogMessages.Add($"> ✅ {axisName} başarıyla {targetPositionStr} pozisyonuna gitti!");

                        // Update the target coordinate in the controller
                        switch (TargetAxis)
                        {
                            case "X":
                                App.MainController.TargetX = targetPositionStr;
                                break;
                            case "Y":
                                App.MainController.TargetY = targetPositionStr;
                                break;
                            case "Z":
                                App.MainController.TargetZ = targetPositionStr;
                                break;
                            case "A":
                                App.MainController.TargetA = targetPositionStr;
                                break;
                        }
                    }
                    else
                    {
                        App.MainController.LogMessages.Add($"> ❌ {axisName} G00 hareketi başarısız oldu!");
                    }
                }
                catch (Exception ex)
                {
                    App.MainController.LogMessages.Add($"> ❌ G00 hareketi hatası: {ex.Message}");
                    MessageBox.Show($"G00 hareketi sırasında hata oluştu: {ex.Message}", "Hareket Hatası", MessageBoxButton.OK, MessageBoxImage.Error);

                    ClearSourceTextBoxFocus();
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"G00Button error: {ex.Message}");
                MessageBox.Show("Beklenmeyen bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);

                ClearSourceTextBoxFocus();
                this.Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DialogResultValue = false;
                ClearSourceTextBoxFocus();
                this.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CancelButton error: {ex.Message}");
            }
        }

        private void ClearSourceTextBoxFocus()
        {
            try
            {
                if (_sourceTextBox != null)
                {
                    _sourceTextBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var parent = _sourceTextBox.Parent;
                        while (parent != null && !(parent is UserControl))
                        {
                            parent = (parent as FrameworkElement)?.Parent;
                        }

                        if (parent is UserControl userControl)
                        {
                            userControl.Focus();
                        }
                        else
                        {
                            System.Windows.Input.Keyboard.ClearFocus();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing focus: {ex.Message}");
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Enter:
                        G00Button_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Escape:
                        CancelButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Back:
                        BackspaceButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D0:
                    case System.Windows.Input.Key.NumPad0:
                        SimulateNumberClick("0");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D1:
                    case System.Windows.Input.Key.NumPad1:
                        SimulateNumberClick("1");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D2:
                    case System.Windows.Input.Key.NumPad2:
                        SimulateNumberClick("2");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D3:
                    case System.Windows.Input.Key.NumPad3:
                        SimulateNumberClick("3");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D4:
                    case System.Windows.Input.Key.NumPad4:
                        SimulateNumberClick("4");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D5:
                    case System.Windows.Input.Key.NumPad5:
                        SimulateNumberClick("5");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D6:
                    case System.Windows.Input.Key.NumPad6:
                        SimulateNumberClick("6");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D7:
                    case System.Windows.Input.Key.NumPad7:
                        SimulateNumberClick("7");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D8:
                    case System.Windows.Input.Key.NumPad8:
                        SimulateNumberClick("8");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.D9:
                    case System.Windows.Input.Key.NumPad9:
                        SimulateNumberClick("9");
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Decimal:
                    case System.Windows.Input.Key.OemPeriod:
                        DecimalButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.OemMinus:
                    case System.Windows.Input.Key.Subtract:
                        MinusButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Delete:
                        ClearButton_Click(null, null);
                        e.Handled = true;
                        break;
                }

                base.OnKeyDown(e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnKeyDown error: {ex.Message}");
            }
        }

        private void SimulateNumberClick(string number)
        {
            try
            {
                if (DisplayTextBox.Text == "0")
                {
                    DisplayTextBox.Text = number;
                }
                else
                {
                    DisplayTextBox.Text += number;
                }

                EnteredValue = DisplayTextBox.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SimulateNumberClick error: {ex.Message}");
            }
        }
    }
}