using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CncControlApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static MainControll MainController { get; private set; }

        public App()
        {
            // Hata logger'ı başlat
            ErrorLogger.Initialize();

            // Yakalanmamış exception handler
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                ErrorLogger.LogCritical("Yakalanmamış kritik hata (UnhandledException)", ex);
                
                MessageBox.Show(
                    $"Kritik Hata: {ex?.Message ?? "Bilinmeyen hata"}\n\n" +
                    $"Detaylar masaüstündeki log dosyasına kaydedildi:\n{ErrorLogger.LogFilePath}", 
                    "Kritik Hata", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            };

            // UI thread exception handler
            DispatcherUnhandledException += (s, e) =>
            {
                ErrorLogger.LogError("UI thread hatası (DispatcherUnhandledException)", e.Exception);
                e.Handled = true; // Uygulamanın çökmesini engelle
                
                MessageBox.Show(
                    $"Bir hata oluştu: {e.Exception.Message}\n\n" +
                    $"Detaylar log dosyasına kaydedildi.",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            // Task exception handler
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                ErrorLogger.LogError("Arka plan görev hatası (UnobservedTaskException)", e.Exception);
                e.SetObserved(); // Exception'ı işlenmiş olarak işaretle
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
                
                ErrorLogger.LogInfo("Uygulama başlatılıyor...");
                
                MainController = new MainControll();
                
                ErrorLogger.LogInfo("MainController başarıyla oluşturuldu");
                
                // Arka planda güncelleme kontrolü yap (sessiz mod)
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000); // Uygulama tamamen yüklendikten sonra
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateChecker.CheckAndPromptAsync(silentIfNoUpdate: true);
                        });
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.LogWarning($"Güncelleme kontrolü başarısız: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorLogger.LogCritical("Uygulama başlatma hatası", ex);
                MessageBox.Show(
                    $"Uygulama başlatılamadı: {ex.Message}\n\n" +
                    $"Detaylar log dosyasına kaydedildi:\n{ErrorLogger.LogFilePath}", 
                    "Başlatma Hatası", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

#if DEBUG
            // Debug modda test'leri çalıştır
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(3000); // Uygulama tamamen yüklendikten sonra

                    System.Diagnostics.Debug.WriteLine("=== DEBUG: Running GCodeOverlayManager Tests ===");
                    CncControlApp.Tests.TestRunner.RunGCodeOverlayTests();
                    System.Diagnostics.Debug.WriteLine("=== DEBUG: Tests Complete ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Test execution failed: {ex.Message}");
                }
            });
#endif
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var mc = MainController;
                if (mc?.IsConnected == true && mc.IsGCodeRunning)
                {
                    ErrorLogger.LogWarning("Uygulama kapatılırken G-Code hala çalışıyordu - acil durdurma gönderildi");
                    mc.AddLogMessage("> ⚠️ Application exiting while G-Code is still running – issuing feed hold + soft reset to stop machine.");
                    // Feed hold '!' then soft reset (Ctrl-X0x18) to clear planner
                    try { mc.SendControlCharacterAsync('!').GetAwaiter().GetResult(); } catch { }
                    // small delay to let controller enter Hold
                    System.Threading.Thread.Sleep(250);
                    try { mc.SendControlCharacterAsync((char)0x18).GetAwaiter().GetResult(); } catch { }
                    mc.AddLogMessage("> 🛑 Emergency stop sequence sent during exit.");
                }
                
                ErrorLogger.LogShutdown();
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Uygulama kapatılırken hata oluştu", ex);
            }
            base.OnExit(e);
        }
    }
}
