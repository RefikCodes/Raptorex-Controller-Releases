// MenuView.xaml.cs - CLEANED PROBE LOGGING + Selection Highlight
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace CncControlApp
{
    public enum MenuPage { Status, GCode, Jog, Probe, Console }

    public partial class MenuView : UserControl
    {
        public event Action<MenuPage> MenuSelectionChanged;
        private bool _isProbeActive = false;

        private const bool ProbeLogVerbose = false;

        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void keybd_event(uint bVk, uint bScan, uint dwFlags, uint dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int VK_CONTROL = 0x11;
        private const int VK_X = 0x58;

        public MenuView()
        {
            InitializeComponent();
            // Başlangıçta Settings (eski StatusButton) aktif kalsın
            EnsureSingleActive();
        }

        private void SafeLog(string msg)
        {
            if (ProbeLogVerbose)
                App.MainController?.AddLogMessage(msg);
        }

        #region Selection Helpers

        private void ClearAllActive()
        {
            StatusButton.Tag = null;
            GcodeButton.Tag = null;
            JogButton.Tag = null;
            ConsoleButton.Tag = null;
            ProbeButton.Tag = null;
        }

        private void SetActive(Button button, bool probeMode = false)
        {
            ClearAllActive();
            if (button == ProbeButton && probeMode)
                button.Tag = "ActiveProbe";
            else
                button.Tag = "Active";
        }

        private void EnsureSingleActive()
        {
            // Eğer hiç biri active değilse SETTINGS (StatusButton) aktif et
            if (!(StatusButton.Tag as string == "Active" ||
                  GcodeButton.Tag as string == "Active" ||
                  JogButton.Tag as string == "Active" ||
                  ConsoleButton.Tag as string == "Active" ||
                  ProbeButton.Tag as string == "ActiveProbe"))
            {
                StatusButton.Tag = "Active";
            }
        }

        #endregion

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                // Probe modunu kapat (başka menü seçildi)
                _isProbeActive = false;

                MenuPage selectedPage;
                switch (clickedButton.Name)
                {
                    case "GcodeButton":
                        selectedPage = MenuPage.GCode;
                        HideMainProbePanel();
                        SetActive(GcodeButton);
                        break;
                    case "JogButton":
                        selectedPage = MenuPage.Jog;
                        HideMainProbePanel();
                        SetActive(JogButton);
                        break;
                    case "ConsoleButton":
                        selectedPage = MenuPage.Console;
                        HideMainProbePanel();
                        SetActive(ConsoleButton);
                        break;
                    default:
                        // StatusButton artık Settings label'lı; yönlendirme Status sayfasına devam ediyor
                        selectedPage = MenuPage.Status;
                        HideMainProbePanel();
                        SetActive(StatusButton);
                        break;
                }
                MenuSelectionChanged?.Invoke(selectedPage);
            }
        }

        private void ProbeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    App.MainController?.AddLogMessage("> ❌ HATA: MainWindow referansı yok");
                    return;
                }

                _isProbeActive = !_isProbeActive;

                if (_isProbeActive)
                {
                    // Jog sayfası göster + Probe panel aç + sadece Probe highlight
                    MenuSelectionChanged?.Invoke(MenuPage.Jog);
                    mainWindow.ShowProbePanel();
                    SetActive(ProbeButton, probeMode: true);
                    App.MainController?.AddLogMessage("> 🟢 Probe UI aktif");
                }
                else
                {
                    // Probe kapat, Jog normal seçili kalsın
                    mainWindow.HideProbePanel();
                    SetActive(JogButton);
                    App.MainController?.AddLogMessage("> ⚪ Probe UI kapatıldı");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Probe toggle hata: {ex.Message}");
            }
        }

        private void ShowJogWithCoordinates()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.HideProbePanel();
                    _isProbeActive = false;
                    SafeLog("> Jog + Coordinates aktif");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> Jog with coordinates hata: {ex.Message}");
            }
        }

        private void HideMainProbePanel()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.HideProbePanel();
                    _isProbeActive = false;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"> Probe panel gizleme hata: {ex.Message}");
            }
        }

        private async void EmergencyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> 🚨 ACİL STOP başlatılıyor...");
                if (App.MainController?.IsConnected == true)
                {
                    bool emergencyStopSuccess = await App.MainController.EmergencyStopAsync();
                    bool resetSuccess = await App.MainController.EmergencyResetAsync();
                    bool recoverySuccess = await App.MainController.RecoverFromAlarmAsync();

                    if (emergencyStopSuccess && resetSuccess && recoverySuccess)
                    {
                        App.MainController?.AddLogMessage("> ✅ ACİL STOP tamamlandı");
                    }
                    else
                    {
                        App.MainController?.AddLogMessage("> ❌ ACİL STOP bazı adımlar başarısız");
                    }
                }
                else
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil (Acil Stop)");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ KRITIK HATA: Emergency stop - {ex.Message}");
            }
        }

        private void SendCtrlX()
        {
            try
            {
                keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY, 0);
                keybd_event(VK_X, 0, KEYEVENTF_EXTENDEDKEY, 0);
                keybd_event(VK_X, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
                SafeLog("> CTRL+X gönderildi");
            }
            catch (Exception ex)
            {
                SafeLog($"> CTRL+X hata: {ex.Message}");
            }
        }
    }
}