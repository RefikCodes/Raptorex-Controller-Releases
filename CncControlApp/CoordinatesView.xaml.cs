using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;

namespace CncControlApp
{
    public partial class CoordinatesView : UserControl
    {
        public CoordinatesView()
        {
            InitializeComponent();
        }

        private async void SetZeroButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    App.MainController?.AddLogMessage("> ❌ CNC bağlı değil - Set Zero yapılamaz");
                    return;
                }

                if (sender is Button button && button.Tag is string axis && !string.IsNullOrWhiteSpace(axis))
                {
                    bool ok = false;
                    switch (axis.ToUpperInvariant())
                    {
                        case "X":
                            ok = await App.MainController.SetZeroXPermanentAsync();
                            if (!ok) ok = await App.MainController.SetZeroAxisAsync("X", false);
                            break;
                        case "Y":
                            ok = await App.MainController.SetZeroYPermanentAsync();
                            if (!ok) ok = await App.MainController.SetZeroAxisAsync("Y", false);
                            break;
                        case "Z":
                            ok = await App.MainController.SetZeroZPermanentAsync();
                            if (!ok) ok = await App.MainController.SetZeroAxisAsync("Z", false);
                            break;
                        case "A":
                            ok = await App.MainController.SetZeroAxisAsync("A", true);
                            if (!ok) ok = await App.MainController.SetZeroAxisAsync("A", false);
                            break;
                        case "ALL":
                            ok = await App.MainController.SetZeroAllPermanentAsync();
                            if (!ok) ok = await App.MainController.SetZeroAxisAsync("ALL", false);
                            break;
                    }

                    if (ok)
                    {
                        App.MainController?.AddLogMessage($"> ✅ {axis} ekseni sıfırlandı (Coordinates panel)");
                    }
                    else
                    {
                        App.MainController?.AddLogMessage($"> ❌ {axis} ekseni sıfırlanamadı (Coordinates panel)");
                    }
                }
            }
            catch (System.Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ HATA: Set Zero (Coordinates) - {ex.Message}");
            }
        }

        public void CloseActiveNumberPad()
        {
            // Close any active number pad if needed
        }
    }
}
