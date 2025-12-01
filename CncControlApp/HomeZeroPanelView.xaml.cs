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

 /// <summary>
 /// GCode Move - Moves the GCode drawing origin to the current work position
 /// by setting X=0, Y=0 at current position (using G92 X0 Y0)
 /// This makes the GCode drawing appear at the current marker position
 /// </summary>
 private async void GCodeMoveButton_Click(object sender, RoutedEventArgs e)
 {
 var mc = App.MainController;
 if (mc?.IsConnected != true)
 {
 mc?.AddLogMessage("> ❌ CNC bağlı değil - GCode Move yapılamaz");
 return;
 }

 var btn = sender as Button;
 btn.IsEnabled = false;

 try
 {
 // Get current work position for logging
 double workX = mc.MStatus?.WorkX ?? 0;
 double workY = mc.MStatus?.WorkY ?? 0;

 mc.AddLogMessage($"> 📐 GCode Move: Çizim orijini mevcut pozisyona taşınıyor (Work X={workX:F3}, Y={workY:F3})...");

 // Set current position as X=0, Y=0 using G92
 // This shifts the work coordinate system so GCode (0,0) is at current position
 bool ok = await mc.SendGCodeCommandWithConfirmationAsync("G92 X0 Y0");

 if (ok)
 {
 mc.AddLogMessage("> ✅ GCode orijini mevcut pozisyona taşındı - Çizim yenileniyor...");
 
 // Refresh the PanelJogCanvas (koordinatların altındaki canvas) to show updated drawing
 await System.Threading.Tasks.Task.Delay(100); // Wait for coordinate update
 var mainWindow = Application.Current.MainWindow as MainWindow;
 mainWindow?.PanelJogCanvasInstance?.RefreshCanvas();
 
 mc.AddLogMessage("> ✅ GCode çizimi güncellendi");
 }
 else
 {
 mc.AddLogMessage("> ❌ GCode Move başarısız - G92 X0 Y0 komutu gönderilemedi");
 }
 }
 catch (System.Exception ex)
 {
 mc?.AddLogMessage($"> ❌ GCode Move hatası: {ex.Message}");
 ErrorLogger.LogError("HomeZeroPanelView.GCodeMoveButton_Click", ex);
 }
 finally
 {
 btn.IsEnabled = true;
 }
 }
 }
}
