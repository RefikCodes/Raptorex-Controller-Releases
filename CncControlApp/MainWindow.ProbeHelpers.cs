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

        #region Center Probe Methods - TO BE REIMPLEMENTED

        // Center X Outer - Find X center from outer edges
        // ⚠️ PLACEHOLDER - Will be reimplemented
        private async void CenterXOuter_Click(object sender, RoutedEventArgs e)
        {
            await CenterXOuterSequenceAsync();
        }

        // Center Y Outer - Find Y center from outer edges
        // ⚠️ PLACEHOLDER - Will be reimplemented
        private async void CenterYOuter_Click(object sender, RoutedEventArgs e)
        {
            await CenterYOuterSequenceAsync();
        }

        // Center XY Outer - Find both X and Y center
        // ⚠️ PLACEHOLDER - Will be reimplemented
        private void CenterXYOuter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.MainController?.AddLogMessage("> 🔧 Center XY - TO BE REIMPLEMENTED");
                Controls.MessageDialog.ShowInfo("Not Implemented", 
                    "Center XY probe will be reimplemented.\n\n" +
                    "Yeni algoritma yazılacak.");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterXYOuter_Click error: {ex.Message}");
            }
        }

        // ⚠️ PLACEHOLDER - Will be reimplemented with new algorithm
        private Task<bool> CenterXOuterSequenceAsync()
        {
            try
            {
                App.MainController?.AddLogMessage("> 🔧 Center X Outer - TO BE REIMPLEMENTED");
                Controls.MessageDialog.ShowInfo("Not Implemented", 
                    "Center X probe will be reimplemented.\n\n" +
                    "Yeni algoritma yazılacak:\n" +
                    "1. +X probe → temas noktası kaydet\n" +
                    "2. Merkeze dön\n" +
                    "3. -X probe → temas noktası kaydet\n" +
                    "4. İki nokta ortasını hesapla\n" +
                    "5. Merkeze git ve X=0 yap");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterXOuterSequenceAsync error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        // ⚠️ PLACEHOLDER - Will be reimplemented with new algorithm
        private Task<bool> CenterYOuterSequenceAsync()
        {
            try
            {
                App.MainController?.AddLogMessage("> 🔧 Center Y Outer - TO BE REIMPLEMENTED");
                Controls.MessageDialog.ShowInfo("Not Implemented", 
                    "Center Y probe will be reimplemented.\n\n" +
                    "Yeni algoritma yazılacak:\n" +
                    "1. +Y probe → temas noktası kaydet\n" +
                    "2. Merkeze dön\n" +
                    "3. -Y probe → temas noktası kaydet\n" +
                    "4. İki nokta ortasını hesapla\n" +
                    "5. Merkeze git ve Y=0 yap");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ CenterYOuterSequenceAsync error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        #endregion
    }
}
