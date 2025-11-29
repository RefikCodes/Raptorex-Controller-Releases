using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CncControlApp
{
 public partial class HomeZeroPanelView : UserControl
 {
 public HomeZeroPanelView()
 {
 InitializeComponent();
 DataContext = App.MainController; // bind if needed later
 }

 private async void HomeAllButton_Click(object sender, RoutedEventArgs e)
 {
 var mc = App.MainController;
 if (mc?.IsConnected != true)
 {
 mc?.AddLogMessage("> ❌ CNC bağlı değil - Homing yapılamaz");
 return;
 }
 (sender as Button).IsEnabled = false;
 mc.AddLogMessage("> 🏠 Homing başlatılıyor ($H)...");
 bool ok = await mc.HomeAllAsync(); // wraps internal MachineHomeAllAsync / $H
 mc.AddLogMessage(ok ? "> ✅ Homing tamamlandı" : "> ❌ Homing başarısız");
 (sender as Button).IsEnabled = true;
 }

 private async void SetZeroAllButton_Click(object sender, RoutedEventArgs e)
 {
 var mc = App.MainController;
 if (mc?.IsConnected != true)
 {
 mc?.AddLogMessage("> ❌ CNC bağlı değil - Set Zero All yapılamaz");
 return;
 }
 (sender as Button).IsEnabled = false;
 mc.AddLogMessage("> 🧩 Set Zero ALL (X,Y,Z permanent) başlatılıyor...");
 bool ok = await mc.SetZeroAllPermanentAsync();
 mc.AddLogMessage(ok ? "> ✅ Tüm eksenler (X,Y,Z) kalıcı olarak sıfırlandı" : "> ❌ Set Zero ALL başarısız");
 (sender as Button).IsEnabled = true;
 }

 private void GoToButton_Click(object sender, RoutedEventArgs e)
 {
 try
 {
 var dialog = new GoToPositionDialog();
 dialog.Owner = Application.Current.MainWindow;
 dialog.ShowDialog();
 }
 catch (System.Exception ex)
 {
 App.MainController?.AddLogMessage($"> ❌ Go To Position hata: {ex.Message}");
 }
 }
 }
}
