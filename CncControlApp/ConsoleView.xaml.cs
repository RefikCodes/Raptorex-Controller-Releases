using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CncControlApp
{
    public partial class ConsoleView : UserControl
    {
        // Console'a özel bağımsız log
        public ObservableCollection<string> ConsoleEntries { get; } = new ObservableCollection<string>();
        private System.Text.StringBuilder _responseBuffer = new System.Text.StringBuilder();

        public ConsoleView()
        {
            InitializeComponent();
            this.DataContext = this;

            // Auto-scroll
            ConsoleEntries.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (ConsoleListBox?.Items?.Count > 0)
                            ConsoleListBox.ScrollIntoView(ConsoleListBox.Items[ConsoleListBox.Items.Count - 1]);
                    }
                    catch { }
                }), DispatcherPriority.Background);
            };
            
            this.Loaded += ConsoleView_Loaded;
        }
        
        private async void ConsoleView_Loaded(object sender, RoutedEventArgs e)
        {
            AddConsole("📡 Serial console aktif");
            
            // CentralStatusQuerier'ın çalıştığından emin ol
            if (App.MainController?.IsConnected == true && !App.MainController.CentralStatusQuerierEnabled)
            {
                App.MainController.StartCentralStatusQuerier();
                await Task.Delay(100);
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendInputAsync();
        }

        private async void GCodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendInputAsync();
            }
        }

        private async Task SendInputAsync()
        {
            try
            {
                var text = GCodeTextBox.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                    return;

                if (App.MainController?.IsConnected != true)
                {
                    AddConsole("→ ❌ Bağlantı yok");
                    return;
                }

                if (App.MainController.IsGCodeRunning)
                {
                    AddConsole("→ ⏳ G-code çalışıyor");
                    return;
                }

                // Çok satırlı gönderim
                var lines = text.Replace("\r", "")
                                .Split('\n')
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToArray();

                foreach (var line in lines)
                {
                    // Merkezi sorgulamayı DURDUR
                    bool wasQuerierRunning = App.MainController.CentralStatusQuerierEnabled;
                    if (wasQuerierRunning)
                    {
                        App.MainController.StopCentralStatusQuerier();
                    }
                    
                    try
                    {
                        // GEÇİCİ subscription - sadece bu komutun yanıtı için
                        bool gotResponse = false;
                        object lockObj = new object();
                        
                        Action<string> tempHandler = (response) =>
                        {
                            lock (lockObj)
                            {
                                if (gotResponse) return;
                            }
                            
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    // Response'u buffer'a ekle
                                    _responseBuffer.Append(response);
                                    
                                    // Satır sonu karakteri varsa işle
                                    string bufferContent = _responseBuffer.ToString();
                                    if (bufferContent.Contains("\n"))
                                    {
                                        var splitLines = bufferContent.Split(new[] { '\n' }, StringSplitOptions.None);
                                        
                                        for (int i = 0; i < splitLines.Length - 1; i++)
                                        {
                                            string responseLine = splitLines[i].Trim('\r', ' ');
                                            if (!string.IsNullOrWhiteSpace(responseLine))
                                            {
                                                AddConsole($"← {responseLine}");
                                                
                                                // ok veya error aldık - yanıt tamamlandı
                                                if (responseLine == "ok" || responseLine.StartsWith("error:"))
                                                {
                                                    lock (lockObj)
                                                    {
                                                        gotResponse = true;
                                                    }
                                                }
                                            }
                                        }
                                        
                                        _responseBuffer.Clear();
                                        if (splitLines.Length > 0)
                                        {
                                            _responseBuffer.Append(splitLines[splitLines.Length - 1]);
                                        }
                                    }
                                }
                                catch { }
                            }), DispatcherPriority.Background);
                        };
                        
                        // Event'e subscribe
                        App.MainController.ConnectionManagerInstance.ResponseReceived += tempHandler;
                        
                        AddConsole($"→ {line}");
                        await App.MainController.SendGCodeCommandAsync(line);
                        
                        // Yanıt için bekle (max 2 saniye)
                        int waitCount = 0;
                        while (waitCount < 20)
                        {
                            bool isResponseReceived;
                            lock (lockObj)
                            {
                                isResponseReceived = gotResponse;
                            }
                            
                            if (isResponseReceived)
                                break;
                                
                            await Task.Delay(100);
                            waitCount++;
                        }
                        
                        // Event'ten unsubscribe
                        App.MainController.ConnectionManagerInstance.ResponseReceived -= tempHandler;
                        
                        // Buffer'ı temizle
                        _responseBuffer.Clear();
                    }
                    finally
                    {
                        // Merkezi sorgulamayı MUTLAKA TEKRAR BAŞLAT (finally ile garanti)
                        if (wasQuerierRunning)
                        {
                            App.MainController.StartCentralStatusQuerier();
                            await Task.Delay(100);
                        }
                    }
                    
                    // Komutlar arası küçük gecikme
                    await Task.Delay(50);
                }

                GCodeTextBox.Clear();
                GCodeTextBox.Focus();
            }
            catch (Exception ex)
            {
                AddConsole($"→ ❌ Hata: {ex.Message}");
            }
        }

        private void ClearTextButton_Click(object sender, RoutedEventArgs e)
        {
            GCodeTextBox.Clear();
            GCodeTextBox.Focus();
        }

        private void ConsoleListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ConsoleListBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(item))
            {
                // Satırın başındaki durum ikonlarını ayıkla
                var restored = item;
                if (restored.StartsWith("> ▶ SEND: "))
                    restored = restored.Substring("> ▶ SEND: ".Length);
                else if (restored.StartsWith("> ✅ OK: "))
                    restored = restored.Substring("> ✅ OK: ".Length);
                else if (restored.StartsWith("> ❌ FAILED: "))
                    restored = restored.Substring("> ❌ FAILED: ".Length);

                GCodeTextBox.Text = restored;
                GCodeTextBox.CaretIndex = GCodeTextBox.Text.Length;
                GCodeTextBox.Focus();
            }
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Content is string s)
            {
                // G-code ve parametre komutlarında trailing space ekleyelim
                // Hareket: G00, G01, G02, G03
                // Mod: G17, G90, G91, G94
                // Ayar: G10, G28, G30, G92
                // Probe: G38.2, G38.3, G38.4, G38.5
                // Eksen parametreleri: X, Y, Z, F, S
                var commandsNeedingSpace = new[] { 
                    "G00", "G01", "G02", "G03",
                    "G17", "G90", "G91", "G94",
                    "G10", "G28", "G30", "G92",
                    "G38.2", "G38.3", "G38.4", "G38.5",
                    "X", "Y", "Z", "F", "S"
                };
                
                if (Array.Exists(commandsNeedingSpace, cmd => cmd == s))
                    InsertText(s + " ");
                else
                    InsertText(s);
            }
        }

        private void KeyButton_SpecialClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button b)
            {
                switch (b.Tag as string)
                {
                    case "SPACE":
                        InsertText(" ");
                        break;
                    case "BACKSPACE":
                        if (!string.IsNullOrEmpty(GCodeTextBox.Text) && GCodeTextBox.CaretIndex > 0)
                        {
                            int index = GCodeTextBox.CaretIndex;
                            GCodeTextBox.Text = GCodeTextBox.Text.Remove(index - 1, 1);
                            GCodeTextBox.CaretIndex = Math.Max(0, index - 1);
                        }
                        break;
                    case "DELETE":
                        if (!string.IsNullOrEmpty(GCodeTextBox.Text) && GCodeTextBox.CaretIndex < GCodeTextBox.Text.Length)
                        {
                            int index = GCodeTextBox.CaretIndex;
                            GCodeTextBox.Text = GCodeTextBox.Text.Remove(index, 1);
                            GCodeTextBox.CaretIndex = index;
                        }
                        break;
                    case "CLEAR":
                        GCodeTextBox.Clear();
                        GCodeTextBox.Focus();
                        break;
                    case "ENTER":
                        _ = SendInputAsync();
                        break;
                    case "CTRLX":
                        // Soft Reset - GRBL 0x18 karakteri (Ctrl-X)
                        GCodeTextBox.Text = "\x18";
                        _ = SendInputAsync();
                        break;
                }
            }
        }

        private void InsertText(string text)
        {
            GCodeTextBox.Focus();
            int index = GCodeTextBox.CaretIndex;
            GCodeTextBox.Text = GCodeTextBox.Text.Insert(index, text);
            GCodeTextBox.CaretIndex = index + text.Length;
        }

        private void AddConsole(string entry)
        {
            ConsoleEntries.Add(entry);
            
            // Koleksiyon boyutunu sınırla
            while (ConsoleEntries.Count > 500)
            {
                ConsoleEntries.RemoveAt(0);
            }
        }
    }
}