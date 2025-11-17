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
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var ex = e.ExceptionObject as Exception;
                var message = $"UNHANDLED EXCEPTION:\n{ex?.ToString() ?? e.ExceptionObject?.ToString()}\n";
                System.IO.File.WriteAllText(logPath, message);
                MessageBox.Show($"Fatal Error: {ex?.Message ?? "Unknown error"}\n\nLogged to: {logPath}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
                
                // Log startup attempt
                System.IO.File.AppendAllText("startup.log", $"[{DateTime.Now:HH:mm:ss.fff}] Application starting...\n");
                
                MainController = new MainControll();
                
                System.IO.File.AppendAllText("startup.log", $"[{DateTime.Now:HH:mm:ss.fff}] MainController created successfully\n");
            }
            catch (Exception ex)
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var message = $"STARTUP ERROR:\n{ex.ToString()}\n\nStack Trace:\n{ex.StackTrace}\n";
                System.IO.File.WriteAllText(logPath, message);
                MessageBox.Show($"Startup Error: {ex.Message}\n\nDetails logged to: {logPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
