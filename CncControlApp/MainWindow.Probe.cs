// MainWindow.Probe.cs
// Main probe operations (Z, X, Y axis probing)
// Separated from MainWindow.xaml.cs for better organization

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;

namespace CncControlApp
{
    /// <summary>
    /// MainWindow partial class - Main probe operations
    /// Contains Z probe, X/Y axis probe, and coordinate reading methods
    /// </summary>
    public partial class MainWindow
    {
        #region Z Probe - Using ProbeManager

        /// <summary>
        /// Z Probe - ProbeManager kullanarak (yanal probelarla aynı sistem)
        /// Coarse probe → Fine probes (validation) → Final retract → Zero set (Z=10mm)
        /// </summary>
        private async void ZProbeButton_Click(object sender, RoutedEventArgs e)
        {
            IDisposable fastScope = null;
            Controls.StreamingPopup streamPopup = null;
            try
            {
                // StreamingPopup oluştur
                streamPopup = new Controls.StreamingPopup { Owner = this };
                streamPopup.SetTitle("Z Probe");
                streamPopup.SetSubtitle("Z ekseni probelaması başlatılıyor...");
                streamPopup.Show();
                
                RunUiLocker.BeginProbeUiGate();
                App.MainController?.AddLogMessage("> 🔧 Z Probe başlatılıyor (ProbeManager)...");
                streamPopup?.Append("🔧 Z Probe başlatılıyor...");
                
                // Probe süresince merkezi sorgu frekansını 100ms yap
                fastScope = App.MainController?.BeginScopedCentralStatusOverride(100);

                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil");
                    streamPopup?.Append("> ❌ CNC bağlı değil");
                    return;
                }

                streamPopup?.Append("> 📡 ProbeManager'a bağlanılıyor...");
                var probeManager = new Services.ProbeManager(App.MainController);
                
                streamPopup?.Append("> 🔍 Z probe sekansı başlatılıyor...");
                streamPopup?.Append("> ⏳ Coarse probe devam ediyor...");
                
                // ProbeManager ile Z probe çalıştır
                var result = await probeManager.ProbeZAsync(manageSession: true);

                if (result == null || !result.Success)
                {
                    string msg = result?.ErrorMessage ?? "Sonuç alınamadı";
                    App.MainController?.AddLogMessage($"> ❌ Z Probe başarısız: {msg}");
                    streamPopup?.Append($"> ❌ Probe başarısız: {msg}");
                    Controls.MessageDialog.ShowError("Z Probe Hatası", $"Z Probe başarısız!\n\n{msg}");
                    return;
                }

                double contact = result.ContactPosition;
                streamPopup?.Append($"> ✅ Temas noktası: {contact:F3} mm");
                streamPopup?.Append($"> 📏 Tolerans: {result.Tolerance:F3} mm");
                streamPopup?.Append($"> 📊 Fine ölçüm sayısı: {result.FineReadings.Count}");
                
                App.MainController?.AddLogMessage($"> ✅ Z Probe başarılı! Temas: {contact:F3} mm, Tolerans: {result.Tolerance:F3} mm");

                // Z probe için sıfırlama: temas noktası = 0, mevcut konum = +10mm (yukarı çekildi)
                // Z probe -Z yönünde (direction = -1), final retract +10mm
                // Temas noktasını 0 yapmak için: current position = -direction * finalRetract = -(-1) * 10 = +10
                const double finalRetract = 10.0;
                const int direction = -1; // Z probes downward
                double zeroOffset = -direction * finalRetract; // = +10
                
                streamPopup?.Append($"> 📍 Sıfırlama: Z={zeroOffset:F3} mm");
                App.MainController?.AddLogMessage($"> 📍 Sıfırlama offset: {zeroOffset:F3} mm (direction={direction})");

                // G10 L20 P0 Z10 - Temas noktası 0, mevcut konum +10mm
                string setZeroCmd = $"G10 L20 P0 Z{zeroOffset.ToString("F3", CultureInfo.InvariantCulture)}";
                App.MainController?.AddLogMessage($"> 📤 Sending: {setZeroCmd}");
                streamPopup?.Append($"> 🔧 Z=0 temas noktasına ayarlanıyor...");
                
                if (await App.MainController.SendGCodeCommandWithConfirmationAsync(setZeroCmd))
                {
                    App.MainController?.AddLogMessage("> ✅ Z ekseni sıfırlandı (temas = 0, mevcut = +10mm)");
                    streamPopup?.Append("> ✅ Z ekseni sıfırlandı");
                    streamPopup?.Append($"> ✅ Z Probe tamamlandı!");
                    
                    // Ölçüm listesi oluştur
                    string allVals = string.Join("\n", result.FineReadings.Select((v, idx) => $"Fine#{idx + 1}: {v:F3} mm"));
                    
                    Controls.MessageDialog.ShowInfo("Z Probe Tamamlandı",
                        $"Z Probe başarılı!\n\n" +
                        $"Ölçümler:\n{allVals}\n\n" +
                        $"Ortalama (2 ölçüm): {contact:F3} mm\n" +
                        $"Tolerans: {result.Tolerance:F3} mm\n\n" +
                        $"Z ekseni sıfırlandı (temas=0, mevcut=+10mm)");
                }
                else
                {
                    App.MainController?.AddLogMessage("> ❌ Z sıfırlama başarısız");
                    streamPopup?.Append("> ❌ Sıfırlama başarısız");
                    Controls.MessageDialog.ShowError("Z Probe Hatası", "Probe başarılı ancak sıfırlama başarısız!");
                }
                
                App.MainController?.AddLogMessage("> ✅ Z Probe tamamlandı");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ HATA: {ex.Message}");
                streamPopup?.Append($"> ❌ Hata: {ex.Message}");
                Controls.MessageDialog.ShowError("Z Probe Hatası", $"Probe hatası:\n{ex.Message}");
            }
            finally
            {
                // Hızlı sorgu scope'unu kapat
                try { fastScope?.Dispose(); } catch { }
                
                // G90 absolute moda dön
                try
                {
                    if (App.MainController?.IsConnected == true)
                    {
                        streamPopup?.Append("> 🔄 G90 absolute moda dönülüyor...");
                        await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                        App.MainController?.AddLogMessage("> 🔄 G90 absolute mod");
                    }
                }
                catch { }
                
                // Popup kapat (1.5 sn gecikme ile)
                if (streamPopup != null)
                {
                    await Task.Delay(1500);
                    streamPopup.ForceClose();
                }
                
                RunUiLocker.EndProbeUiGate();
            }
        }

        /// <summary>
        /// Sadece Idle bekleyen metod - HİÇ QUERY GÖNDERMİYOR
        /// Merkez sorgular, biz sadece Idle'ı izliyoruz
        /// </summary>
        private async Task<bool> WaitForIdleAsync(int timeoutMs, string tag)
        {
            var sw = Stopwatch.StartNew();
            int idleCount = 0;
            const int requiredIdleCount = 3;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string state = App.MainController?.MachineStatus ?? string.Empty;

                bool isIdle = state.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ||
                    state.IndexOf("<Idle|", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isAlarm = state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase) ||
                    state.IndexOf("<Alarm|", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isAlarm)
                {
                    App.MainController?.AddLogMessage($"> 🛑 [{tag}] Alarm tespit edildi!");
                    return false;
                }

                if (isIdle)
                {
                    idleCount++;
                    if (idleCount >= requiredIdleCount)
                    {
                        App.MainController?.AddLogMessage($"> ✅ [{tag}] Idle onaylandı ({sw.ElapsedMilliseconds}ms)");
                        return true;
                    }
                }
                else
                {
                    idleCount = 0;
                }

                await Task.Delay(50);
            }

            App.MainController?.AddLogMessage($"> ⌛ [{tag}] Timeout ({timeoutMs}ms)");
            return false;
        }

        #endregion

        #region Probe Button Handlers

        private async void PlusXProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeWithPopupAsync('X', +1);
        private async void MinusXProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeWithPopupAsync('X', -1);
        private async void PlusYProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeWithPopupAsync('Y', +1);
        private async void MinusYProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeWithPopupAsync('Y', -1);

        /// <summary>
        /// Wrapper that creates StreamingPopup for axis probes
        /// </summary>
        private async Task UnifiedProbeWithPopupAsync(char axis, int direction)
        {
            Controls.StreamingPopup streamPopup = null;
            try
            {
                string axisName = axis == 'X' ? "X" : "Y";
                string dirName = direction > 0 ? "+" : "-";
                
                streamPopup = new Controls.StreamingPopup { Owner = this };
                streamPopup.SetTitle($"{axisName}{dirName} Probe");
                streamPopup.SetSubtitle($"> {axisName}{dirName} axis probe sequence\n> Real-time progress shown below:");
                streamPopup.Show();
                
                await UnifiedProbeAsync(axis, direction, silent: true, stream: streamPopup);
                
                await Task.Delay(1500);
            }
            finally
            {
                try { streamPopup?.ForceClose(); } catch { }
            }
        }

        /// <summary>
        /// Unified probe using centralized ProbeManager for X/Y axes
        /// </summary>
        private async Task UnifiedProbeAsync(char axis, int direction, bool silent = false, Controls.StreamingPopup stream = null)
        {
            IDisposable fastScope = null;
            try
            {
                RunUiLocker.BeginProbeUiGate();
                
                string axisName = axis == 'X' ? "X" : "Y";
                string dirName = direction > 0 ? "+" : "-";
                App.MainController?.AddLogMessage($"> 🔧 {axisName}{dirName} Probe (Unified ProbeManager)");
                stream?.Append($"> 🔧 {axisName}{dirName} Probe starting...");

                fastScope = App.MainController?.BeginScopedCentralStatusOverride(100);

                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil");
                    stream?.Append("> ❌ CNC not connected");
                    return;
                }

                stream?.Append("> 📡 Connecting to ProbeManager...");
                var probeManager = new Services.ProbeManager(App.MainController);
                Services.ProbeResult result = null;
                
                stream?.Append($"> 🔍 Starting {axisName}{dirName} probe sequence...");
                stream?.Append("> ⏳ Coarse probe in progress...");
                
                if (axis == 'X')
                    result = direction > 0 ? await probeManager.ProbeXPlusAsync(30.0) : await probeManager.ProbeXMinusAsync(30.0);
                else if (axis == 'Y')
                    result = direction > 0 ? await probeManager.ProbeYPlusAsync(30.0) : await probeManager.ProbeYMinusAsync(30.0);

                if (result == null || !result.Success)
                {
                    string msg = result?.ErrorMessage ?? "No result received";
                    App.MainController?.AddLogMessage($"> ❌ Probe başarısız: {msg}");
                    stream?.Append($"> ❌ Probe failed: {msg}");
                    if (!silent)
                    {
                        Controls.MessageDialog.ShowError("Probe Hatası", $"{axisName}{dirName} Probe başarısız!\n\n{msg}");
                    }
                    return;
                }

                double contact = result.ContactPosition;
                stream?.Append($"> ✅ Probe contact at: {contact:F3} mm");
                stream?.Append($"> 📏 Tolerance: {result.Tolerance:F3} mm");
                stream?.Append($"> 📊 Fine readings: {result.FineReadings.Count}");
                
                App.MainController?.AddLogMessage($"> ✅ {axisName}{dirName} Probe başarılı! Temas: {contact:F3} mm, Tolerans: {result.Tolerance:F3} mm");

                // Use finalRetract constant (10mm) and direction
                const double finalRetract = 10.0;
                double zeroOffset = -direction * finalRetract;
                
                stream?.Append($"> 📍 Direction={direction}, FinalRetract={finalRetract:F1}mm, ZeroOffset={zeroOffset:F3}");
                App.MainController?.AddLogMessage($"> 📍 Sıfırlama offset: {zeroOffset:F3} mm (direction={direction})");

                stream?.Append($"> 🔧 Setting {axisName}=0 at contact point (offset {zeroOffset:F3})...");
                string setZeroCmd = $"G10 L20 P0 {axis}{zeroOffset.ToString("F3", CultureInfo.InvariantCulture)}";
                App.MainController?.AddLogMessage($"> 📤 Sending: {setZeroCmd}");
                if (await App.MainController.SendGCodeCommandWithConfirmationAsync(setZeroCmd))
                {
                    App.MainController?.AddLogMessage($"> ✅ {axisName} ekseni sıfırlandı");
                    stream?.Append($"> ✅ {axisName} axis zeroed successfully");
                    stream?.Append($"> ✅ {axisName}{dirName} Probe complete!");
                    
                    if (!silent)
                    {
                        Controls.MessageDialog.ShowInfo("Probe Başarılı", 
                            $"{axisName}{dirName} Probe tamamlandı!\n\n" +
                            $"Temas: {contact:F3} mm\n" +
                            $"Tolerans: {result.Tolerance:F3} mm\n" +
                            $"Ölçüm: {result.FineReadings.Count}\n\n" +
                            $"{axisName} ekseni sıfırlandı.");
                    }
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Hata: {ex.Message}");
                stream?.Append($"> ❌ Error: {ex.Message}");
                if (!silent)
                {
                    Controls.MessageDialog.ShowError("Probe Hatası", $"Probe hatası:\n{ex.Message}");
                }
            }
            finally
            {
                fastScope?.Dispose();
                try
                {
                    if (App.MainController?.IsConnected == true)
                    {
                        stream?.Append("> 🔄 Restoring G90 absolute mode...");
                        await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                        App.MainController?.AddLogMessage("> 🔄 G90 absolute mod");
                    }
                }
                catch { }
                RunUiLocker.EndProbeUiGate();
            }
        }

        #endregion

        #region Probe Helper Methods

        /// <summary>
        /// Get axis rapid rate from machine settings
        /// </summary>
        private double GetAxisRapid(char axis)
        {
            try
            {
                int id = (axis == 'X') ? 110 : (axis == 'Y') ? 111 : 112;
                var setting = App.MainController?.Settings?.FirstOrDefault(s => s.Id == id);
                if (setting != null && double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    return Math.Max(1.0, v);
            }
            catch { }
            return 1000.0;
        }

        #endregion
    }
}
