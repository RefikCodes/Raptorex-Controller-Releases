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
 #region Z Probe - NEW SEQUENCE

        /// <summary>
        /// ✅ TAMAMEN YENİ Z PROBE SEKANS
        /// Hiç ? göndermiyor - Merkez sorgu çalışıyor
        /// 
        /// SEKANS:
        /// Coarse:2mm geri çek → Idle gör → Coarse Probe (F=rapid/8) → Idle gör
        /// Fine: en fazla6 ölçüm. Her ölçüm: [1.0mm geri çek → Idle → Fine Probe (F=rapid/15) → Idle →400ms bekle → Machine.Z oku]
        /// Doğrulama: İkinci fine’dan itibaren toplanan değerler içinde farkı <0.06 mm olan bir çift bulunursa, bu iki değerin ortalaması alınır ve probelama sonlandırılır.
        /// Başarısızlık:6 fine sonunda doğrulama yoksa ERROR popup ve abort.
        /// Son: (seçilen ortalama)+10 mm hedefe git → Idle → Z=10mm olarak sıfırla → popup
        /// 
        /// Feed: Coarse = rapid/8, Fine = rapid/15
        /// </summary>
   private async void ZProbeButton_Click(object sender, RoutedEventArgs e)
        {
 IDisposable fastScope = null;
 try
 {
 RunUiLocker.BeginProbeUiGate();
 App.MainController?.AddLogMessage("> 🔧 Z Probe başlatılıyor (YENİ SEKANS)...");
 
 // Probe süresince merkezi sorgu frekansını200ms yap
 fastScope = App.MainController?.BeginScopedCentralStatusOverride(200);

   if (App.MainController?.IsConnected != true)
    {
  App.MainController?.AddLogMessage("> ❌ CNC bağlı değil");
     return;
   }

    // Check and clear Hold/Alarm state before starting
    string currentState = App.MainController?.MachineStatus ?? "";
    if (currentState.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
    {
        App.MainController?.AddLogMessage("> ⏸ Hold durumu tespit edildi - temizleniyor...");
        bool released = await App.MainController.ExitHoldIfAnyAsync("ZProbe_PreCheck");
        if (!released)
        {
            App.MainController?.AddLogMessage("> ❌ Hold durumu temizlenemedi!");
            return;
        }
        await Task.Delay(300); // Wait for state to stabilize
    }
    
    if (currentState.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase))
    {
        App.MainController?.AddLogMessage("> 🛠️ Alarm durumu tespit edildi - unlock yapılıyor...");
        bool unlocked = await App.MainController.UnlockMachineAsync();
        if (!unlocked)
        {
            App.MainController?.AddLogMessage("> ❌ Alarm temizlenemedi!");
            return;
        }
        await Task.Delay(300); // Wait for state to stabilize
    }

    // Ensure clean modal state: G90 first, then G91
    if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"))
    {
       App.MainController?.AddLogMessage("> ❌ G90 başlangıç temizliği başarısız!");
     return;
    }
    App.MainController?.AddLogMessage("> ⚙️ G90 başlangıç (modal state temizliği)");

    // G91 - Rölatif mod
if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G91"))
    {
       App.MainController?.AddLogMessage("> ❌ G91 ayarlanamadı!");
       await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
     return;
         }
        App.MainController?.AddLogMessage("> ⚙️ G91 aktif (rölatif mod)");

            // Z rapid ayardan oku ($112)
         double zRapid = 1000.0;
  try
     {
        var zSetting = App.MainController?.Settings?.FirstOrDefault(s => s.Id == 112);
              if (zSetting != null && double.TryParse(zSetting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double zVal))
         {
    zRapid = Math.Max(1.0, zVal);
     }
     }
 catch { }

                // Feed hesapla: Coarse = rapid/8, Fine = rapid/15
  int coarseFeed = ClampProbeFeed((int)(zRapid / 8.0));
                int fineFeed = ClampProbeFeed((int)(zRapid / 15.0));
   
          App.MainController?.AddLogMessage($"> 🚀 Z rapid: {zRapid:F0} mm/min");
                App.MainController?.AddLogMessage($"> 📏 Coarse feed: {coarseFeed} mm/min (rapid/8)");
         App.MainController?.AddLogMessage($"> 📏 Fine feed: {fineFeed} mm/min (rapid/15)");

     // ===== COARSE PROBE =====
    // 1. 2mm geri çek
     App.MainController?.AddLogMessage("> 🔼 Coarse:2mm geri çek");
      if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 Z2.000"))
      {
       App.MainController?.AddLogMessage("> ❌2mm geri çekme başarısız");
              await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
       return;
     }

    // 2. Idle gör
        App.MainController?.AddLogMessage("> ⏳ Idle bekleniyor...");
              if (!await WaitForIdleAsync(15000, "Coarse_Retract"))
       {
         App.MainController?.AddLogMessage("> ❌ Idle görülmedi (2mm geri çekme)");
           await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
        return;
     }
         App.MainController?.AddLogMessage("> ✅ Idle görüldü");

    // 3. Coarse Probe başla (daha hızlı)
     string coarseCmd = $"G38.2 Z-30.000 F{coarseFeed}";
          App.MainController?.AddLogMessage($"> 🔍 Coarse probe: {coarseCmd}");
        if (!await App.MainController.SendGCodeCommandWithConfirmationAsync(coarseCmd))
             {
         App.MainController?.AddLogMessage("> ❌ Coarse probe gönderilemedi");
    await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
        return;
   }

      // 4. Idle gör
         App.MainController?.AddLogMessage("> ⏳ Idle bekleniyor (coarse sonrası)...");
   if (!await WaitForIdleAsync(45000, "Coarse_Probe"))
                {
             App.MainController?.AddLogMessage("> ❌ Idle görülmedi (coarse probe)");
           await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
       return;
        }
       App.MainController?.AddLogMessage("> ✅ Idle görüldü (coarse bitti)");

     // ===== Fine Probes (max6 with early validation) =====
 var fineContacts = new List<double>(6);
 const double toleranceThreshold =0.06; // mm
 bool validated = false;
 double avgUsed =0.0;
 double tolerance =0.0;
 int usedA = -1, usedB = -1;

 for (int i =0; i <6; i++)
 {
 int stepIndex = i +1;

 // A. 1.0mm geri çek
 App.MainController?.AddLogMessage($"> 🔼 Fine#{stepIndex}:1.0mm geri çek");
 if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G00 Z1.000"))
 {
 App.MainController?.AddLogMessage($"> ❌1.0mm geri çekme başarısız (fine#{stepIndex})");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }

 // B. Idle gör
 App.MainController?.AddLogMessage("> ⏳ Idle bekleniyor...");
 if (!await WaitForIdleAsync(15000, $"Fine{stepIndex}_Retract"))
 {
 App.MainController?.AddLogMessage($"> ❌ Idle görülmedi (fine#{stepIndex}1.0mm geri çekme)");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 App.MainController?.AddLogMessage("> ✅ Idle görüldü");

 // C. Fine probe başla
 DateTime fineStartTs = DateTime.UtcNow;
 string fineCmd = $"G38.2 Z-6.000 F{fineFeed}";
 App.MainController?.AddLogMessage($"> 🎯 Fine#{stepIndex} probe: {fineCmd}");
 if (!await App.MainController.SendGCodeCommandWithConfirmationAsync(fineCmd))
 {
 App.MainController?.AddLogMessage($"> ❌ Fine#{stepIndex} probe gönderilemedi");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }

 // D. Fine sonrası Idle gör
 App.MainController?.AddLogMessage("> ⏳ Idle bekleniyor (fine sonrası)...");
 if (!await WaitForIdleAsync(30000, $"Fine{stepIndex}_Probe"))
 {
 App.MainController?.AddLogMessage($"> ❌ Idle görülmedi (fine#{stepIndex} probe)");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 App.MainController?.AddLogMessage("> ✅ Idle görüldü (fine bitti)");

 // E. PRB koordinatını oku (fallback: Machine.Z)
 DateTime probeEndTs = DateTime.UtcNow;
 var (zOk, zVal, fromPrb) = await ReadContactZAsync(fineStartTs, 800);
 if (!zOk || !IsFinite(zVal))
 {
 App.MainController?.AddLogMessage($"> ❌ Fine#{stepIndex} Z okunamadı");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 fineContacts.Add(zVal);
 App.MainController?.AddLogMessage($"> 📍 Fine#{stepIndex} temas Z = {zVal:F3} mm (kaynak: {(fromPrb ? "PRB" : "MPos")})");

 // Doğrulama: ikinci fine’dan sonra kontrole başla
 if (fineContacts.Count >=2)
 {
 // Bütün çiftleri tara, en küçük farkı bul
 double minDiff = double.MaxValue;
 int bestI = -1, bestJ = -1;
 for (int a =0; a < fineContacts.Count -1; a++)
 {
 for (int b = a +1; b < fineContacts.Count; b++)
 {
 double diff = Math.Abs(fineContacts[a] - fineContacts[b]);
 if (diff < minDiff)
 {
 minDiff = diff; bestI = a; bestJ = b;
 }
 }
 }

 if (minDiff < toleranceThreshold)
 {
 usedA = bestI; usedB = bestJ;
 avgUsed = (fineContacts[bestI] + fineContacts[bestJ]) /2.0;
 tolerance = Math.Abs(fineContacts[bestI] - fineContacts[bestJ]);
 validated = true;
 App.MainController?.AddLogMessage($"> ✅ Doğrulama sağlandı: Fine#{bestI +1} ve Fine#{bestJ +1} fark={tolerance:0.000} mm < {toleranceThreshold:0.000} mm, ortalama={avgUsed:0.000} mm");
 break; // erken bitir
 }
 else
 {
 App.MainController?.AddLogMessage($"> ℹ️ Doğrulama yok: min fark={minDiff:0.000} mm (eşik {toleranceThreshold:0.000} mm)");
 }
 }
 }

 // Doğrulama başarısızsa ERROR popup ve abort
 if (!validated)
 {
 string list = string.Join("\n", fineContacts.Select((v, idx) => $"Fine#{idx +1}: {v:F3} mm"));
 Controls.MessageDialog.ShowError("Z Probe Hatası",
     $"Doğrulama başarısız!\n" +
     $"Eşik: {toleranceThreshold:0.000} mm\n\n" +
     $"Ölçümler:\n{list}");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }

 // ===== SON: avgUsed+10 hedefe git ve Z=10mm olarak sıfırla =====
 double currentMZ2 = App.MainController?.MStatus?.Z ?? double.NaN;
 if (!IsFinite(currentMZ2)) currentMZ2 = avgUsed; // güvenlik
 double targetMZ = avgUsed +10.0; // avgUsed'e göre10mm yukarı (bu makinede +Z)
 double deltaZ2 = targetMZ - currentMZ2; // G91 (relative) moddayız
 string deltaText2 = deltaZ2.ToString("F3", CultureInfo.InvariantCulture);
 App.MainController?.AddLogMessage($"> 🎯 Hedef Z(MPos) = avg(2 ölçüm)+10 = {targetMZ:F3} mm (mevcut: {currentMZ2:F3}, delta: {deltaZ2:+0.000;-0.000;0.000})");
 // Hedefe hareket
 if (!await App.MainController.SendGCodeCommandWithConfirmationAsync($"G00 Z{deltaText2}"))
 {
 App.MainController?.AddLogMessage("> ❌ Hedef Z'ye hareket başarısız");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 // Idle gör (hareket mesafesine göre timeout hesapla)
 int moveTimeout2 = EstimateTimeoutMsForRapid(Math.Abs(deltaZ2), zRapid);
 if (!await WaitForIdleAsync(moveTimeout2, "Final_MoveToAvgPlus10"))
 {
 App.MainController?.AddLogMessage("> ❌ Idle görülmedi (avg+10 hedef hareket)");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 App.MainController?.AddLogMessage("> ✅ Avg+10 hedef konuma ulaşıldı");

 //10mm olarak sıfırla
 App.MainController?.AddLogMessage("> 🎯 Z=10mm olarak sıfırlanıyor...");
 if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G92 Z10"))
 {
 App.MainController?.AddLogMessage("> ❌ G92 Z10 başarısız");
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 return;
 }
 App.MainController?.AddLogMessage("> ✅ Z=10mm olarak sıfırlandı");

 // G90 - Absolut mod
 await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
 App.MainController?.AddLogMessage("> ⚙️ G90 aktif");

 // Popup: tüm değerler, kullanılan iki ölçüm, ortalama ve tolerans
 string allVals = string.Join("\n", fineContacts.Select((v, idx) => $"Fine#{idx +1}: {v:F3} mm"));
 Controls.MessageDialog.ShowInfo("Z Probe Tamamlandı",
     $"Ölçümler:\n{allVals}\n\n" +
     $"Kullanılan: Fine#{usedA +1} ve Fine#{usedB +1}\n" +
     $"Ortalama (2 ölçüm): {avgUsed:0.000} mm\n" +
     $"Tolerans (fark): {tolerance:0.000} mm");

          App.MainController?.AddLogMessage("> ✅ Z Probe tamamlandı");
            }
            catch (Exception ex)
     {
             App.MainController?.AddLogMessage($"> ❌ HATA: {ex.Message}");
   try { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); } catch { }
     }
     finally
 {
 // Hızlı sorgu scope'unu kapat (150ms normale dön)
 try { fastScope?.Dispose(); } catch { }
 
 // Hold/Alarm cleanup
 try
 {
     var mc = App.MainController;
     if (mc != null)
     {
         var state = mc.MachineStatus ?? "";
         
         if (state.StartsWith("Hold", StringComparison.OrdinalIgnoreCase))
         {
             mc.AddLogMessage("> ⏸ Hold tespit edildi — kurtarma yapılıyor");
             bool released = await mc.ExitHoldIfAnyAsync("ZProbe_Cleanup");
             if (released)
             {
                 mc.AddLogMessage("> ✅ Hold temizlendi");
                 // Soft reset modal states'i resetliyor; G91 tekrar assert edilebilir
                 await Task.Delay(150);
             }
             else
             {
                 mc.AddLogMessage("> ⚠️ Hold temizlenemedi");
             }
         }
         
         if (state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase))
         {
             mc.AddLogMessage("> 🛠️ Alarm tespit edildi — kurtarma yapılıyor");
             await mc.RecoverFromAlarmAsync();
             await Task.Delay(200);
         }
         
         // G90 absolute mode'a dön
         await mc.SendGCodeCommandWithConfirmationAsync("G90");
         mc.AddLogMessage("> 🔄 G90 absolute mod");
     }
 }
 catch (Exception cleanupEx)
 {
     App.MainController?.AddLogMessage($"> ⚠️ Cleanup hatası: {cleanupEx.Message}");
 }
 
 RunUiLocker.EndProbeUiGate();
 }
    }

        /// <summary>
        /// ✅ Sadece Idle bekleyen metod - HİÇ QUERY GÖNDERMİYOR
 /// Merkez sorgular, biz sadece Idle'ı izliyoruz
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <param name="tag">Tag for logging</param>
        /// <returns>True if Idle confirmed</returns>
        private async Task<bool> WaitForIdleAsync(int timeoutMs, string tag)
        {
       var sw = Stopwatch.StartNew();
  int idleCount = 0;
      const int requiredIdleCount = 3; // 3 kez Idle görünce onaylı sayalım

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
  idleCount = 0; // Reset if not idle
  }

         await Task.Delay(50); // 50ms bekle, merkez query gönderiyor
          }

  App.MainController?.AddLogMessage($"> ⌛ [{tag}] Timeout ({timeoutMs}ms)");
            return false;
        }

        #endregion

        #region X/Y Axis Probe

        /// <summary>
        /// Advanced axis probe with double-touch for accuracy measurement
        /// </summary>
    /// <param name="axis">Axis to probe ('X' or 'Y')</param>
  /// <param name="directionSign">Direction: +1 for positive, -1 for negative</param>
        /// <returns>True if probe completed successfully</returns>
        private async Task<bool> AdvancedAxisProbeAsync(char axis, int directionSign)
        {
 try
   {
     if (App.MainController?.IsConnected != true)
   {
   App.MainController?.AddLogMessage($"> ❌ CNC bağlı değil - {axis} Probe yapılamaz");
         return false;
      }

         if (!await App.MainController.SendGCodeCommandWithConfirmationAsync("G91"))
              {
      App.MainController?.AddLogMessage("> ❌ Rölatif mod (G91) ayarlanamadı!");
             return false;
         }
    App.MainController?.AddLogMessage("> ⚙️ Rölatif mod aktif (G91)");

         double rapid = GetAxisRapid(axis);
 int coarseFeed = ClampProbeFeed((int)Math.Max(1.0, rapid / 3.0));
     int fineFeed = ClampProbeFeed((int)Math.Max(1.0, rapid / 6.0));
        App.MainController?.AddLogMessage($"> 🚀 {axis} rapid: {rapid:F0} (Kaba F={coarseFeed}, İnce F={fineFeed})");

         double coarseDistance = 30.0;
      double fineDistance = 6.0;
             double retract1 = 3.0;
    double retractFinal = 10.0;

int dir = directionSign >= 0 ? 1 : -1;

        string coarseCmd = $"G38.2 {AxisMove(axis, dir * coarseDistance)} F{coarseFeed}";
     App.MainController?.AddLogMessage($"> 🔍 Kaba probe: {coarseCmd}");
            DateTime coarseStartUtc = DateTime.UtcNow;
     if (!await ExecuteAndWaitWithAutoExtendAsync(coarseCmd, $"{axis}Probe_Coarse", EstimateTimeoutMsForFeed(coarseDistance, coarseFeed)))
        {
   App.MainController?.AddLogMessage("> ❌ Kaba probe başarısız!");
       await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
   return false;
   }

         var first = await ReadContactXYZAsync(coarseStartUtc);
           double firstContactAxis = (axis == 'X') ? first.x : first.y;
         App.MainController?.AddLogMessage($"> 📍 İlk temas {axis}={firstContactAxis:F3} mm (kaynak: {(first.fromPrb ? "PRB" : "WPos")})");

        string retract1Cmd = $"G00 {AxisMove(axis, -dir * retract1)}";
      if (!await ExecuteAndWaitAsync(retract1Cmd, $"{axis}Probe_Retract3", EstimateTimeoutMsForRapid(retract1, rapid)))
    {
      App.MainController?.AddLogMessage("> ❌ 3mm geri çekme başarısız!");
    await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
      return false;
       }

  string fineCmd = $"G38.2 {AxisMove(axis, dir * fineDistance)} F{fineFeed}";
      App.MainController?.AddLogMessage($"> 🎯 İnce probe: {fineCmd}");
            DateTime fineStartUtc = DateTime.UtcNow;
       if (!await ExecuteAndWaitWithAutoExtendAsync(fineCmd, $"{axis}Probe_Fine", EstimateTimeoutMsForFeed(fineDistance, fineFeed)))
        {
        App.MainController?.AddLogMessage("> ❌ İnce probe başarısız!");
     await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
   return false;
         }

   var second = await ReadContactXYZAsync(fineStartUtc);
        double secondContactAxis = (axis == 'X') ? second.x : second.y;
         App.MainController?.AddLogMessage($"> 📍 İkinci temas {axis}={secondContactAxis:F3} mm (kaynak: {(second.fromPrb ? "PRB" : "WPos")})");

        string finalRetractCmd = $"G00 {AxisMove(axis, -dir * retractFinal)}";
              await ExecuteAndWaitAsync(finalRetractCmd, $"{axis}Probe_FinalRetract", EstimateTimeoutMsForRapid(retractFinal, rapid));
   await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
  App.MainController?.AddLogMessage("> ⚙️ Absolut mod aktif (G90)");

        UpdateMainProbeCoordinates();

  double delta = secondContactAxis - firstContactAxis;
  double deltaAbs = Math.Abs(delta);
       Controls.MessageDialog.ShowInfo($"{axis} Probe Hassasiyet Kontrolü",
           $"İlk temas {axis}: {firstContactAxis:F3} mm\n" +
           $"İkinci temas {axis}: {secondContactAxis:F3} mm\n" +
           $"Fark (İkinci - İlk): {delta:+0.000;-0.000;0.000} mm\n" +
           $"Mutlak fark: {deltaAbs:0.000} mm");

                App.MainController?.AddLogMessage($"> ✅ {axis} Probe (Main) tamamlandı");
          return true;
 }
            catch (Exception ex)
          {
  App.MainController?.AddLogMessage($"> ❌ HATA: {axis} Probe - {ex.Message}");
   try { await App.MainController.SendGCodeCommandWithConfirmationAsync("G90"); } catch { }
      return false;
            }
  }

        #endregion

     #region Probe Button Handlers

        private async void PlusXProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeAsync('X', +1);
        private async void MinusXProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeAsync('X', -1);
        private async void PlusYProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeAsync('Y', +1);
        private async void MinusYProbeButton_Click(object sender, RoutedEventArgs e) => await UnifiedProbeAsync('Y', -1);

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
                if (stream != null) try { stream.Append($"> 🔧 {axisName}{dirName} Probe (Unified) started..."); } catch { }

                fastScope = App.MainController?.BeginScopedCentralStatusOverride(200);

                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil");
                    if (stream != null) try { stream.Append("> ❌ CNC bağlı değil"); } catch { }
                    return;
                }

                var probeManager = new Services.ProbeManager(App.MainController);
                Services.ProbeResult result = null;
                
                if (axis == 'X')
                    result = direction > 0 ? await probeManager.ProbeXPlusAsync(30.0) : await probeManager.ProbeXMinusAsync(30.0);
                else if (axis == 'Y')
                    result = direction > 0 ? await probeManager.ProbeYPlusAsync(30.0) : await probeManager.ProbeYMinusAsync(30.0);

                if (result == null || !result.Success)
                {
                    string msg = result?.ErrorMessage ?? "Sonuç alınamadı";
                    App.MainController?.AddLogMessage($"> ❌ Probe başarısız: {msg}");
                    if (silent)
                    {
                        if (stream != null) try { stream.Append($"> ❌ Probe başarısız: {msg}"); } catch { }
                        return;
                    }
                    Controls.MessageDialog.ShowError("Probe Hatası", $"{axisName}{dirName} Probe başarısız!\n\n{msg}");
                    return;
                }

                double contact = result.ContactPosition;
                App.MainController?.AddLogMessage($"> ✅ {axisName}{dirName} Probe başarılı! Temas: {contact:F3} mm, Tolerans: {result.Tolerance:F3} mm");

                string setZeroCmd = $"G10 L20 P0 {axis}0";
                if (await App.MainController.SendGCodeCommandWithConfirmationAsync(setZeroCmd))
                {
                    App.MainController?.AddLogMessage($"> ✅ {axisName} ekseni sıfırlandı");
                    if (silent)
                    {
                        if (stream != null)
                        {
                            try
                            {
                                stream.Append($"> ✅ {axisName}{dirName} Probe tamamlandı – Temas: {contact:F3} mm, Tol: {result.Tolerance:F3} mm; {axisName}=0 yapıldı");
                            }
                            catch { }
                        }
                    }
                    else
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
                if (!silent)
                {
                    Controls.MessageDialog.ShowError("Probe Hatası", $"Probe hatası:\n{ex.Message}");
                }
                else
                {
                    if (stream != null) try { stream.Append($"> ❌ Probe hatası: {ex.Message}"); } catch { }
                }
            }
            finally
            {
                fastScope?.Dispose();
                try
                {
                    if (App.MainController?.IsConnected == true)
                    {
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
        /// Clamp probe feed rate to safe range [1..MaxProbeFeed]
        /// </summary>
        private static int ClampProbeFeed(int feed)
        {
            if (feed < 1) return 1;
        return feed > MaxProbeFeed ? MaxProbeFeed : feed;
   }

   /// <summary>
        /// Get axis rapid rate from machine settings ($110 for X, $111 for Y)
        /// </summary>
        private double GetAxisRapid(char axis)
        {
      try
         {
           int id = (axis == 'X') ? 110 : 111;
var setting = App.MainController?.Settings?.FirstOrDefault(s => s.Id == id);
         if (setting != null && double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
         return Math.Max(1.0, v);
  }
   catch { }
            return 1000.0;
        }

        /// <summary>
    /// Generate axis movement G-code string
        /// </summary>
        private string AxisMove(char axis, double distanceMm)
      {
       string d = distanceMm.ToString("F3", CultureInfo.InvariantCulture);
            switch (axis)
{
           case 'X': return $"X{d}";
    case 'Y': return $"Y{d}";
         case 'Z': return $"Z{d}";
     default: return $"X{d}";
      }
   }

        #endregion
    }
}
