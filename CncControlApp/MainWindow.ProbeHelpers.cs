using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using CncControlApp.Helpers;

namespace CncControlApp
{
    public partial class MainWindow
    {
        // Helpers: estimate a safe timeout for feed/rapid moves
        private int EstimateTimeoutMsForFeed(double distanceMm, int feedMmMin, int minMs = 8000)
        {
            if (feedMmMin < 1) feedMmMin = 1;
            double minutes = Math.Abs(distanceMm) / feedMmMin; // minutes = mm / (mm/min)
            int ms = (int)(minutes * 60000.0); // expected duration
            ms = (int)(ms * 2.0) + 3000; // 2x slack + 3s overhead
            return Math.Max(ms, minMs);
        }

        private int EstimateTimeoutMsForRapid(double distanceMm, double rapidMmMin, int minMs = 5000)
        {
            double feed = Math.Max(1.0, rapidMmMin);
            double minutes = Math.Abs(distanceMm) / feed;
            int ms = (int)(minutes * 60000.0);
            ms = (int)(ms * 1.8) + 2000; // 1.8x slack + 2s overhead
            return Math.Max(ms, minMs);
        }

        #region Center Probe Methods

        // Center X Outer - Find X center from outer edges
        private async void CenterXOuter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var probeManager = App.MainController?.ProbeManager;
                if (probeManager == null)
                {
                    Controls.MessageDialog.ShowInfo("Hata", "ProbeManager bulunamadı");
                    return;
                }

                var result = await probeManager.CenterXAsync();
                
                if (result.Success)
                {
                    string widthInfo = result.Width > 0 ? $"Genişlik: {result.Width:F3} mm\n\n" : "";
                    Controls.MessageDialog.ShowInfo("Center X Tamamlandı", 
                        widthInfo +
                        "X = 0 olarak ayarlandı.");
                }
                else
                {
                    Controls.MessageDialog.ShowInfo("Center X Başarısız", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterXOuter_Click error: {ex.Message}");
            }
        }

        // Center Y Outer - Find Y center from outer edges
        private async void CenterYOuter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var probeManager = App.MainController?.ProbeManager;
                if (probeManager == null)
                {
                    Controls.MessageDialog.ShowInfo("Hata", "ProbeManager bulunamadı");
                    return;
                }

                var result = await probeManager.CenterYAsync();
                
                if (result.Success)
                {
                    string depthInfo = result.Width > 0 ? $"Derinlik: {result.Width:F3} mm\n\n" : "";
                    Controls.MessageDialog.ShowInfo("Center Y Tamamlandı", 
                        depthInfo +
                        "Y = 0 olarak ayarlandı.");
                }
                else
                {
                    Controls.MessageDialog.ShowInfo("Center Y Başarısız", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterYOuter_Click error: {ex.Message}");
            }
        }

        // Center XY Outer - Find both X and Y center
        // TODO: CenterXYAsync henüz yazılmadı
        private void CenterXYOuter_Click(object sender, RoutedEventArgs e)
        {
            Controls.MessageDialog.ShowInfo("Henüz Uygulanmadı", 
                "Center XY probe henüz yazılmadı.\n\nCenter X ve Y test edildikten sonra eklenecek.");
        }

        #endregion
    }
}
