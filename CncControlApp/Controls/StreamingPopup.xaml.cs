using System;
using System.Text;
using System.Windows;

namespace CncControlApp.Controls
{
 public partial class StreamingPopup : Window
 {
 private readonly StringBuilder _sb = new StringBuilder(4096);
 public StreamingPopup()
 {
 InitializeComponent();
 Owner = Application.Current?.MainWindow;
 }

 public void SetTitle(string title)
 {
 try { TitleText.Text = title ?? ""; } catch { }
 }

 public void SetSubtitle(string text)
 {
 try { SubtitleText.Text = text ?? ""; } catch { }
 }

 public void SetLiveLine(string text)
 {
 try { LiveLine.Text = text ?? ""; } catch { }
 }

 public void Append(string line)
 {
 if (string.IsNullOrWhiteSpace(line)) return;
 try
 {
 Dispatcher?.Invoke(() =>
 {
 _sb.AppendLine(line);
 LogText.Text = _sb.ToString();
 try { LogScroll?.ScrollToEnd(); } catch { }
 });
 }
 catch { }
 }

 private void CloseButton_Click(object sender, RoutedEventArgs e)
 {
 try { Close(); } catch { }
 }
 }
}
