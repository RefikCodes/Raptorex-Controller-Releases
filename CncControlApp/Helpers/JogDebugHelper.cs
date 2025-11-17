using System;
using System.Linq;
using System.Threading.Tasks;

namespace CncControlApp.Helpers
{
    public static class JogDebugHelper
    {
        public static async Task TestSystemDiagnosis(MainControll controller) // ✅ MainControll olarak değiştirildi
        {
            try
            {
                controller?.AddLogMessage($"> === SİSTEM TANİ TESTİ ===");
                
                // 1. Bağlantı durumu
                controller?.AddLogMessage($"> 🔌 IsConnected: {controller.IsConnected}");
                bool hasSettings = controller?.Settings != null && controller.Settings.Count > 0;
                controller?.AddLogMessage($"> 🏗️ Has Settings: {hasSettings}");
                
                // 2. Settings durumu
                if (controller?.Settings != null)
                {
                    controller?.AddLogMessage($"> 📊 Settings Count: {controller.Settings.Count}");
                    
                    if (controller.Settings.Count > 0)
                    {
                        var first5 = controller.Settings.Take(5);
                        foreach (var setting in first5)
                        {
                            controller?.AddLogMessage($">   Setting ${setting.Id}: {setting.Value}");
                        }
                    }
                    else
                    {
                        controller?.AddLogMessage($"> ❌ PROBLEM: Settings tamamen boş!");
                        controller?.AddLogMessage($"> 🚨 Config okuma işlemi BAŞARISIZ!");
                    }
                }
                else
                {
                    controller?.AddLogMessage($"> ❌ CRITICAL: Settings NULL!");
                }
                
                // 3. Jog speed değerleri
                controller?.AddLogMessage($"> 🏃 MaxXYJogSpeed: {controller.MaxXYJogSpeed}");
                controller?.AddLogMessage($"> 🏃 CurrentXYJogSpeed: {controller.CurrentXYJogSpeed}");
                
                // 4. Test jog command generation
                if (controller.IsConnected)
                {
                    string testCmd = string.Format(System.Globalization.CultureInfo.InvariantCulture, 
                        "$J=G91X{0:F3} F{1}", 1.0, controller.CurrentXYJogSpeed);
                    controller?.AddLogMessage($"> 🧪 Test jog command: {testCmd}");
                }
                
                controller?.AddLogMessage($"> === TANİ TESTİ BİTTİ ===");
                
                // 5. Raw response debug
                if (controller.IsConnected && controller.Settings.Count == 0)
                {
                    controller?.AddLogMessage($"> 🔬 Raw response debug başlıyor...");
                    await TestRawResponse(controller);
                    
                    controller?.AddLogMessage($"> 🔄 Response processing test başlıyor...");
                    await TestResponseProcessing(controller);
                }
            }
            catch (Exception ex)
            {
                controller?.AddLogMessage($"> ❌ Tanı test hatası: {ex.Message}");
            }
        }

        public static async Task TestRawResponse(MainControll controller) // ✅ MainControll olarak değiştirildi
        {
            try
            {
                controller?.AddLogMessage($"> === RAW RESPONSE DEBUG ===");
                
                if (!controller.IsConnected)
                {
                    controller?.AddLogMessage($"> ❌ CNC bağlı değil!");
                    return;
                }
                
                controller?.AddLogMessage($"> 📤 Sending simple command: ?");
                
                bool sendResult = await controller.SendGCodeCommandAsync("?");
                controller?.AddLogMessage($"> 📨 Send result: {sendResult}");
                
                // Wait for response
                await Task.Delay(2000);
                
                controller?.AddLogMessage($"> === RAW RESPONSE DEBUG END ===");
            }
            catch (Exception ex)
            {
                controller?.AddLogMessage($"> ❌ Raw response test error: {ex.Message}");
            }
        }

        public static async Task TestResponseProcessing(MainControll controller) // ✅ MainControll olarak değiştirildi
        {
            try
            {
                controller?.AddLogMessage($"> === RESPONSE TEST BAŞLIYOR ===");
                
                if (!controller.IsConnected)
                {
                    controller?.AddLogMessage($"> ❌ CNC bağlı değil!");
                    return;
                }
                
                // Test implementation...
                controller?.AddLogMessage($"> === RESPONSE TEST BİTTİ ===");
            }
            catch (Exception ex)
            {
                controller?.AddLogMessage($"> ❌ Response test hatası: {ex.Message}");
            }
        }
    }
}